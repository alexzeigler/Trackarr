namespace Trackarr.Core.Auth;

/// <summary>Someone who can sign in to Trackarr. For now there's only ever one: the admin made on first run.</summary>
public class User
{
    public int Id { get; set; }

    /// <summary>Matched without regard to case.</summary>
    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    ///     Changes whenever the password does. Sign-in cookies carry it, so an old cookie stops working after a
    ///     password change or a reset.
    /// </summary>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
}
