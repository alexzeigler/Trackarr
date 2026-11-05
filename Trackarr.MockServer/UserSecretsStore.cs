using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace Trackarr.MockServer;

/// <summary>
///     Writes the live-source settings back into this project's user secrets (secrets.json), the same
///     place <c>dotnet user-secrets set</c> writes to, so edits from the dashboard survive restarts.
/// </summary>
public class UserSecretsStore(ILogger<UserSecretsStore> logger)
{
    public string SecretsPath { get; } = PathHelper.GetSecretsPathFromSecretsId(
        typeof(UserSecretsStore).Assembly.GetCustomAttribute<UserSecretsIdAttribute>()!.UserSecretsId);

    /// <summary>
    ///     Sets each key (flat "Section:Key" form, like <c>dotnet user-secrets</c>). A null/empty value
    ///     removes the key. Every other secret in the file is left untouched.
    /// </summary>
    public void Save(IReadOnlyDictionary<string, string?> values)
    {
        JsonObject root;
        try
        {
            root = File.Exists(SecretsPath)
                ? JsonNode.Parse(File.ReadAllText(SecretsPath),
                    documentOptions: new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true
                    }) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"secrets.json is not valid JSON ({ex.Message}); fix or delete {SecretsPath}");
        }

        foreach (var (key, value) in values)
        {
            // Hand-written secrets may use nested objects; drop that copy so the flat key is the only one.
            RemoveNested(root, key.Split(':'));

            if (string.IsNullOrWhiteSpace(value)) root.Remove(key);
            else root[key] = value.Trim();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(SecretsPath)!);
        File.WriteAllText(SecretsPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        logger.LogInformation("Saved live source settings to user secrets ({Path})", SecretsPath);
    }

    private static void RemoveNested(JsonObject obj, string[] parts)
    {
        if (parts.Length < 2 || obj[parts[0]] is not JsonObject child) return;
        if (parts.Length == 2) child.Remove(parts[1]);
        else RemoveNested(child, parts[1..]);
        if (child.Count == 0) obj.Remove(parts[0]);
    }
}
