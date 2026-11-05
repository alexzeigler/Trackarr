using System.Net;
using Microsoft.Data.Sqlite;

namespace Trackarr.Services.Settings;

/// <summary>
///     How Trackarr listens: port, bind address, URL base and reverse proxy trust. Read straight from the
///     database once at startup (before the web host exists, so SettingsService isn't available yet). Changes
///     made on the Network settings page apply after a restart.
/// </summary>
public sealed record HostingOptions
{
    public const int DefaultPort = 8585;

    public int Port { get; init; } = DefaultPort;
    public string BindAddress { get; init; } = "*";

    /// <summary>"" or a path like "/trackarr" (leading slash, no trailing slash).</summary>
    public string UrlBase { get; init; } = "";

    public bool ReverseProxy { get; init; }
    public string TrustedProxies { get; init; } = "";

    /// <summary>
    ///     Set when ASPNETCORE_URLS / --urls is given (Rider launch profiles, or an older Docker template). It
    ///     takes precedence over Port and BindAddress so existing setups keep working.
    /// </summary>
    public string? UrlsOverride { get; init; }

    /// <summary>The address(es) Kestrel listens on.</summary>
    public string ListenUrls => UrlsOverride ?? $"http://{FormatHost(BindAddress)}:{Port}";

    /// <summary>For the Docker healthcheck: a local URL that reaches the app whatever it's bound to.</summary>
    public string LocalHealthUrl
    {
        get
        {
            var first = ListenUrls.Split(';', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            var local = first.Replace("://*", "://localhost").Replace("://+", "://localhost")
                .Replace("://0.0.0.0", "://localhost").Replace("://[::]", "://localhost");
            return local.TrimEnd('/') + "/health";
        }
    }

    /// <summary>The value for &lt;base href&gt;: always ends in "/".</summary>
    public string BaseHref => UrlBase + "/";

    public static HostingOptions Load(string sqliteConnectionString, string? urlsOverride)
    {
        var values = ReadNetworkSettings(sqliteConnectionString);

        return new HostingOptions
        {
            Port = int.TryParse(values.GetValueOrDefault("Port"), out var port) && port is > 0 and <= 65535
                ? port
                : DefaultPort,
            BindAddress = string.IsNullOrWhiteSpace(values.GetValueOrDefault("BindAddress"))
                ? "*"
                : values["BindAddress"].Trim(),
            UrlBase = NormalizeUrlBase(values.GetValueOrDefault("UrlBase")),
            ReverseProxy = bool.TryParse(values.GetValueOrDefault("ReverseProxy"), out var proxy) && proxy,
            TrustedProxies = values.GetValueOrDefault("TrustedProxies") ?? "",
            UrlsOverride = string.IsNullOrWhiteSpace(urlsOverride) ? null : urlsOverride.Trim()
        };
    }

    /// <summary>"trackarr", "/trackarr/", " /a/b " → "/trackarr", "/a/b". Empty or "/" → "".</summary>
    public static string NormalizeUrlBase(string? value)
    {
        var trimmed = (value ?? "").Trim().Trim('/');
        return trimmed.Length == 0 ? "" : "/" + trimmed;
    }

    /// <summary>
    ///     Networks whose X-Forwarded-* headers are trusted. Empty means loopback plus the private ranges a reverse
    ///     proxy normally sits in (Docker networks, LAN). Entries are IPs or CIDR ranges, comma or space separated.
    ///     Invalid entries are returned in <paramref name="invalid" /> and skipped.
    /// </summary>
    public static List<IPNetwork> ParseTrustedProxies(string? value, out List<string> invalid)
    {
        invalid = [];
        var entries = (value ?? "").Split([',', ' ', ';', '\n'], StringSplitOptions.RemoveEmptyEntries);

        if (entries.Length == 0)
        {
            return
            [
                IPNetwork.Parse("127.0.0.0/8"), IPNetwork.Parse("::1/128"),
                IPNetwork.Parse("10.0.0.0/8"), IPNetwork.Parse("172.16.0.0/12"), IPNetwork.Parse("192.168.0.0/16"),
                IPNetwork.Parse("fc00::/7"), IPNetwork.Parse("fe80::/10")
            ];
        }

        List<IPNetwork> networks = [];
        foreach (var entry in entries)
        {
            if (IPNetwork.TryParse(entry, out var network))
                networks.Add(network);
            else if (IPAddress.TryParse(entry, out var address))
                networks.Add(new IPNetwork(address, address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128));
            else
                invalid.Add(entry);
        }

        return networks;
    }

    private static string FormatHost(string bindAddress)
    {
        // Bare IPv6 addresses need brackets in a URL.
        return IPAddress.TryParse(bindAddress, out var ip) &&
               ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{bindAddress}]"
            : bindAddress;
    }

    private static Dictionary<string, string> ReadNetworkSettings(string connectionString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT \"Key\", \"Value\" FROM \"AppSettings\" WHERE \"Category\" = 'Network'";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = reader.GetString(1);
        }
        catch (SqliteException)
        {
            // First start: the database or table doesn't exist yet. Defaults apply.
        }

        return result;
    }
}
