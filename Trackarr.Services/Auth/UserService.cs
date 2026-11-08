using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trackarr.Core.Auth;
using Trackarr.Data;

namespace Trackarr.Services.Auth;

/// <summary>
///     The Trackarr login (AUDIT S1): creating the first user, checking passwords, changing them, and telling the
///     sign-in cookie whether it's still valid.
/// </summary>
public sealed class UserService(IDbContextFactory<AppDbContext> dbFactory, ILogger<UserService> logger)
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 256;
    public const int MaxUsernameLength = 100;

    /// <summary>The claim that carries <see cref="User.SecurityStamp" /> in the sign-in cookie.</summary>
    public const string StampClaim = "trackarr:stamp";

    /// <summary>Set to "true" to remove the login on the next start, for when nobody can sign in.</summary>
    public const string ResetVariable = "TRACKARR_RESET_AUTH";

    private static readonly User TimingUser = new() { Username = "timing" };

    private readonly PasswordHasher<User> _hasher = new();
    private readonly string _timingHash = new PasswordHasher<User>().HashPassword(TimingUser, "timing-only");

    // User id -> current security stamp, so the cookie check on every request doesn't hit the database.
    private readonly ConcurrentDictionary<int, string> _stamps = new();

    // Once a user exists it stays that way until ResetAsync, so this skips the query on every request.
    private volatile bool _hasUser;

    public async Task<bool> HasUserAsync(CancellationToken ct = default)
    {
        if (_hasUser) return true;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        _hasUser = await db.Users.AnyAsync(ct);
        return _hasUser;
    }

    /// <summary>Why <paramref name="username" /> can't be used, or null when it's fine.</summary>
    public static string? CheckUsername(string? username)
    {
        var trimmed = username?.Trim() ?? "";
        if (trimmed.Length == 0) return "Enter a username.";
        return trimmed.Length > MaxUsernameLength
            ? $"Use at most {MaxUsernameLength} characters for the username."
            : null;
    }

    /// <summary>Why <paramref name="password" /> can't be used, or null when it's fine.</summary>
    public static string? CheckPassword(string? password)
    {
        var length = password?.Length ?? 0;
        if (length < MinPasswordLength) return $"Use at least {MinPasswordLength} characters for the password.";
        return length > MaxPasswordLength ? $"Use at most {MaxPasswordLength} characters for the password." : null;
    }

    /// <summary>
    ///     Creates the first login. Throws <see cref="InvalidOperationException" /> when one already exists, so the
    ///     setup page can't be used to add a second admin.
    /// </summary>
    public async Task<User> CreateFirstUserAsync(string username, string password, CancellationToken ct = default)
    {
        if (CheckUsername(username) is { } usernameError) throw new ArgumentException(usernameError, nameof(username));
        if (CheckPassword(password) is { } passwordError) throw new ArgumentException(passwordError, nameof(password));

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // SQLite transactions from Microsoft.Data.Sqlite are BEGIN IMMEDIATE, so two setup forms submitted at once
        // can't both see an empty table.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (await db.Users.AnyAsync(ct)) throw new InvalidOperationException("A Trackarr login already exists.");

        var user = new User { Username = username.Trim() };
        user.PasswordHash = _hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _hasUser = true;
        _stamps[user.Id] = user.SecurityStamp;
        logger.LogInformation("Created the Trackarr login '{Username}'", user.Username);
        return user;
    }

    /// <summary>The user when the username and password match, otherwise null.</summary>
    public async Task<User?> ValidateCredentialsAsync(string? username, string? password,
        CancellationToken ct = default)
    {
        var name = username?.Trim() ?? "";
        password ??= "";

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // The column is NOCASE, so this matches regardless of case.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == name, ct);
        if (user is null)
        {
            // Hash anyway, so a wrong username takes as long as a wrong password.
            _hasher.VerifyHashedPassword(TimingUser, _timingHash, password);
            return null;
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = _hasher.HashPassword(user, password);

        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>
    ///     Changes the password and the security stamp, which signs out every device, including the one making the
    ///     change. Returns an error message, or null on success.
    /// </summary>
    public async Task<string?> ChangePasswordAsync(int userId, string? currentPassword, string? newPassword,
        CancellationToken ct = default)
    {
        if (CheckPassword(newPassword) is { } error) return error;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null) return "Your login no longer exists. Sign in again.";

        if (_hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword ?? "") ==
            PasswordVerificationResult.Failed)
            return "The current password is wrong.";

        user.PasswordHash = _hasher.HashPassword(user, newPassword!);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.PasswordChangedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        _stamps[user.Id] = user.SecurityStamp;
        logger.LogInformation("Password changed for '{Username}'. Every device has to sign in again", user.Username);
        return null;
    }

    /// <summary>True when a cookie for <paramref name="userId" /> with <paramref name="stamp" /> is still valid.</summary>
    public async Task<bool> IsCurrentAsync(int userId, string? stamp, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(stamp)) return false;
        if (_stamps.TryGetValue(userId, out var known)) return known == stamp;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var current = await db.Users.Where(u => u.Id == userId).Select(u => u.SecurityStamp)
            .FirstOrDefaultAsync(ct);
        if (current is null) return false;

        _stamps[userId] = current;
        return current == stamp;
    }

    /// <summary>True when <paramref name="principal" /> belongs to a user whose cookie is still valid.</summary>
    public Task<bool> IsCurrentAsync(ClaimsPrincipal? principal, CancellationToken ct = default)
    {
        return int.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None,
            CultureInfo.InvariantCulture, out var id)
            ? IsCurrentAsync(id, principal!.FindFirstValue(StampClaim), ct)
            : Task.FromResult(false);
    }

    /// <summary>Removes every login, so the next visitor gets the setup page. Returns how many were removed.</summary>
    public async Task<int> ResetAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var removed = await db.Users.ExecuteDeleteAsync(ct);

        _stamps.Clear();
        _hasUser = false;
        return removed;
    }

    /// <summary>The identity stored in the sign-in cookie.</summary>
    public static ClaimsPrincipal CreatePrincipal(User user, string authenticationType)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(StampClaim, user.SecurityStamp)
        ], authenticationType);
        return new ClaimsPrincipal(identity);
    }
}
