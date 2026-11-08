using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Trackarr.Data;

/// <summary>
///     Encrypts secrets (API keys, the proxy password) before they're written to the database, using the Data
///     Protection keys in the keys folder next to it (/config/keys in Docker). A copy of the database on its own, such
///     as one shared for support, doesn't give the secrets away. Someone with the whole /config folder still can.
///     <para>
///         Encrypted values start with <see cref="Prefix" />. Anything else is a value saved before encryption existed:
///         it reads as-is, and <see cref="SecretUpgrade" /> encrypts it at startup.
///     </para>
/// </summary>
public sealed class SecretProtector(IDataProtectionProvider provider)
{
    public const string Prefix = "dp1:";

    private readonly IDataProtector _protector = provider.CreateProtector("Trackarr.Secrets.v1");

    /// <summary>For EF Core: encrypts on the way into the database and decrypts on the way out.</summary>
    public ValueConverter<string, string> Converter => field ??= new ValueConverter<string, string>(
        v => Protect(v),
        v => Unprotect(v));

    public static bool IsProtected(string? value)
    {
        return value?.StartsWith(Prefix, StringComparison.Ordinal) == true;
    }

    public string Protect(string value)
    {
        return value.Length == 0 || IsProtected(value) ? value : Prefix + _protector.Protect(value);
    }

    /// <summary>
    ///     The plain value. A value that can't be decrypted, because the keys folder was lost or comes from another
    ///     install, reads as empty, so the server shows a missing API key instead of every page failing.
    /// </summary>
    public string Unprotect(string value)
    {
        return TryUnprotect(value, out var plain) ? plain : string.Empty;
    }

    public bool TryUnprotect(string value, out string plain)
    {
        if (!IsProtected(value))
        {
            plain = value;
            return true;
        }

        try
        {
            plain = _protector.Unprotect(value[Prefix.Length..]);
            return true;
        }
        catch (CryptographicException)
        {
            plain = string.Empty;
            return false;
        }
    }

    /// <summary>"••••1a2b" for showing that a secret is set without showing it. Empty when there's none.</summary>
    public static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= 8 ? "••••" : "••••" + value[^4..];
    }
}
