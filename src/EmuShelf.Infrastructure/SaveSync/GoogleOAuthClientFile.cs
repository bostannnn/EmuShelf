using System.Text.Json;

namespace EmuShelf.Infrastructure.SaveSync;

/// <summary>An OAuth client id and secret read from a Google Cloud console download.</summary>
/// <param name="ClientId">The client id. Public by design — it identifies the application.</param>
/// <param name="ClientSecret">The client secret. Handed to rclone and never stored by EmuShelf.</param>
/// <param name="ProjectId">The Google Cloud project the client belongs to, for display.</param>
public sealed record GoogleOAuthClient(string ClientId, string ClientSecret, string? ProjectId);

/// <summary>
/// Reads the <c>client_secret_*.json</c> the Google Cloud console produces for an OAuth client, so
/// connecting a personal Google client is choosing a file rather than copying two long strings.
/// </summary>
/// <remarks>
/// Only the values are read; the file is never copied, and the secret goes straight to rclone, which
/// owns every credential EmuShelf touches. rclone's shared client is rate-limited (a slow sync
/// before a launch) and Google is retiring it during 2026, which is why this exists.
/// </remarks>
public static class GoogleOAuthClientFile
{
    /// <summary>Reads the client from <paramref name="path"/>.</summary>
    /// <exception cref="InvalidDataException">The file is not a usable OAuth client download.</exception>
    public static async Task<GoogleOAuthClient> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        JsonDocument document;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("That file is not valid JSON.", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("That file could not be read.", ex);
        }

        using (document)
        {
            // Google names the section after the client type: "installed" for a desktop client,
            // "web" for a web one. A desktop client is the one that works with rclone's local
            // redirect, but read either rather than rejecting a file whose values are right there.
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("That file is not a Google OAuth client download.");

            var section = root.TryGetProperty("installed", out var installed) ? installed
                : root.TryGetProperty("web", out var web) ? web
                : root;

            var clientId = ReadString(section, "client_id");
            var clientSecret = ReadString(section, "client_secret");
            if (clientId is null || clientSecret is null)
            {
                throw new InvalidDataException(
                    "That file has no client_id and client_secret. Download the OAuth client JSON from " +
                    "Google Cloud console → Credentials → your OAuth client → Download JSON.");
            }

            return new GoogleOAuthClient(clientId, clientSecret, ReadString(section, "project_id"));
        }
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
}
