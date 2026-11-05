using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Trackarr.MockServer;

/// <summary>
///     The API key Trackarr must send to the mock, editable from the dashboard. A random key is
///     generated on first run and saved next to the project so restarts keep it.
/// </summary>
public class MockSettings
{
    private readonly object _lock = new();
    private readonly ILogger<MockSettings> _logger;
    private readonly string _path;
    private string _apiKey;

    public MockSettings(IHostEnvironment env, IConfiguration config, ILogger<MockSettings> logger)
    {
        _logger = logger;
        _path = config["MockServer:SettingsFile"] is { Length: > 0 } p
            ? Path.GetFullPath(p, env.ContentRootPath)
            : Path.Combine(env.ContentRootPath, "mockserver-settings.json");

        _apiKey = Load() ?? "";
        if (_apiKey.Length == 0)
        {
            _apiKey = GenerateKey();
            Save();
            logger.LogInformation("Generated a new mock API key (saved to {Path})", _path);
        }
    }

    public string ApiKey
    {
        get
        {
            lock (_lock) return _apiKey;
        }
    }

    public static string GenerateKey()
    {
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
    }

    public void SetApiKey(string key)
    {
        lock (_lock)
        {
            _apiKey = key;
            Save();
        }

        _logger.LogInformation("Mock API key changed");
    }

    /// <summary>Accepts the key the same ways Radarr/Sonarr do: X-Api-Key header or apikey query param.</summary>
    public bool IsAuthorized(HttpContext ctx)
    {
        var supplied = ctx.Request.Headers["X-Api-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(supplied)) supplied = ctx.Request.Query["apikey"].FirstOrDefault();
        if (string.IsNullOrEmpty(supplied)) return false;

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied),
            Encoding.UTF8.GetBytes(ApiKey));
    }

    private string? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            return doc.RootElement.TryGetProperty("apiKey", out var k) ? k.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not read {Path}: {Msg}. A new key will be generated.", _path, ex.Message);
            return null;
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(new { apiKey = _apiKey }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not save {Path}: {Msg}. The key will reset on restart.", _path, ex.Message);
        }
    }
}
