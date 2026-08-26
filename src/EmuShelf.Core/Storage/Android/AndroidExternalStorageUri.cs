using System.Text;

namespace EmuShelf.Core.Storage.Android;

/// <summary>
/// Builds and parses <c>content://com.android.externalstorage.documents</c> Storage Access Framework
/// URIs — the one DocumentsProvider that maps onto real <c>/storage/…</c> paths — as pure string
/// operations so both the storage layer (Milestone D) and the launch layer (Milestone B) share one
/// tested implementation instead of the private copy the A1 head grew.
///
/// Two directions:
/// <list type="bullet">
/// <item><b>Build</b> the tree-scoped document URI form an emulator actually accepts. Measured on the
/// Thor: the bare <c>…/document/&lt;id&gt;</c> form is rejected because a persisted grant matches only the
/// <c>…/tree/&lt;tree&gt;/document/&lt;id&gt;</c> form (0b). So a launch URI is always tree-scoped.</item>
/// <item><b>Resolve</b> such a URI back to its <c>/storage/…</c> path, valid only because EmuShelf holds
/// all-files access, for EmuShelf's own readers (<c>FolderScanner</c>, the disc hashers).</item>
/// </list>
/// </summary>
public static class AndroidExternalStorageUri
{
    /// <summary>The external-storage DocumentsProvider authority — the only provider mapped to real paths.</summary>
    public const string Authority = "com.android.externalstorage.documents";

    private const string Scheme = "content";

    /// <summary>Absolute-path root of the built-in shared "primary" volume.</summary>
    private const string PrimaryRoot = "/storage/emulated/0";

    /// <summary>The document-id volume label of the built-in shared storage.</summary>
    public const string PrimaryVolume = "primary";

    /// <summary>
    /// The external (shared-storage) app-data files directory for a package —
    /// <c>/storage/emulated/0/Android/data/&lt;package&gt;/files</c>. Scoped-storage Android emulators keep
    /// their data here; EmuShelf reads it directly as a path under all-files access, which reaches
    /// <c>Android/data</c> on the Thor (DECISIONS 2026-08-20). Used to auto-locate the fixed-location
    /// emulators' saves (DuckStation, Dolphin, …) without the user picking a folder.
    /// </summary>
    public static string ExternalAppFilesDirectory(string packageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        return $"{PrimaryRoot}/Android/data/{packageName}/files";
    }

    /// <summary>
    /// Builds a document id (<c>&lt;volume&gt;:&lt;relative&gt;</c>) from a volume label and a
    /// forward-slashed path relative to that volume's root. The relative part is normalised to forward
    /// slashes and trimmed of leading/trailing slashes.
    /// </summary>
    public static string BuildDocumentId(string volume, string relative)
    {
        ArgumentException.ThrowIfNullOrEmpty(volume);
        var normalised = (relative ?? string.Empty).Replace('\\', '/').Trim('/');
        return normalised.Length == 0 ? $"{volume}:" : $"{volume}:{normalised}";
    }

    /// <summary>
    /// Builds the tree-scoped document URI <c>content://…/tree/&lt;tree&gt;/document/&lt;document&gt;</c>.
    /// <paramref name="treeRelative"/> must be an ancestor of <paramref name="documentRelative"/> within the
    /// same volume (the tree the receiving emulator is expected to hold a persisted grant to); both are
    /// paths relative to the volume root. This is the exact form that launched Metal Gear Solid on the
    /// Thor (0b).
    /// </summary>
    public static string BuildTreeDocumentUri(string volume, string treeRelative, string documentRelative)
    {
        var treeId = BuildDocumentId(volume, treeRelative);
        var documentId = BuildDocumentId(volume, documentRelative);
        return $"{Scheme}://{Authority}/tree/{Encode(treeId)}/document/{Encode(documentId)}";
    }

    /// <summary>
    /// Splits an absolute <c>/storage/…</c> path into its document-id volume and volume-relative path.
    /// <c>/storage/emulated/0/…</c> → <c>("primary", "…")</c>; <c>/storage/AE6A-1092/…</c> →
    /// <c>("AE6A-1092", "…")</c>. Returns false for a path that is not under <c>/storage</c>.
    /// </summary>
    public static bool TrySplitLocalPath(string absolutePath, out string volume, out string relative)
    {
        volume = string.Empty;
        relative = string.Empty;
        if (string.IsNullOrEmpty(absolutePath))
            return false;

        var normalised = absolutePath.Replace('\\', '/');
        if (normalised.StartsWith(PrimaryRoot, StringComparison.Ordinal))
        {
            volume = PrimaryVolume;
            relative = normalised[PrimaryRoot.Length..].Trim('/');
            return true;
        }

        const string storagePrefix = "/storage/";
        if (!normalised.StartsWith(storagePrefix, StringComparison.Ordinal))
            return false;

        var rest = normalised[storagePrefix.Length..];
        var slash = rest.IndexOf('/');
        var volumeLabel = slash < 0 ? rest : rest[..slash];
        // "self" and "emulated" are FUSE bookkeeping mounts, not addressable document volumes.
        if (volumeLabel.Length == 0 ||
            volumeLabel.Equals("self", StringComparison.Ordinal) ||
            volumeLabel.Equals("emulated", StringComparison.Ordinal))
        {
            return false;
        }

        volume = volumeLabel;
        relative = slash < 0 ? string.Empty : rest[(slash + 1)..].Trim('/');
        return true;
    }

    /// <summary>
    /// Resolves an <c>externalstorage</c> tree and/or document URI to its real <c>/storage/…</c> path, or
    /// returns null for any other provider or a URI that would escape its volume. Valid only under
    /// all-files access; the caller falls back to a SAF-backed reader otherwise. When the URI carries a
    /// <c>/document/</c> segment that id wins (it is the actual target); otherwise the <c>/tree/</c> id is
    /// used (a picked folder).
    /// </summary>
    public static string? TryResolveLocalPath(Uri? uri)
    {
        if (uri is null || !uri.Host.Equals(Authority, StringComparison.Ordinal))
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var documentId = ExtractId(segments, "document") ?? ExtractId(segments, "tree");
        return ResolveDocumentId(documentId);
    }

    // Resolves a <volume>:<relative> document id to its /storage/… path, or null for a missing/unsafe id.
    private static string? ResolveDocumentId(string? documentId)
    {
        if (documentId is null)
            return null;

        var parts = documentId.Split(':', 2);
        var volume = parts[0];
        var relative = parts.Length > 1 ? parts[1] : string.Empty;

        // A volume label is a single path segment; a separator would let the combined path escape its
        // volume, "." / ".." would resolve to a parent of /storage (the empty-relative branch below
        // returns the root directly, skipping the containment check), and an empty volume is SAF's blocked
        // "root of all storage" pick.
        if (string.IsNullOrEmpty(volume) ||
            volume is "." or ".." ||
            volume.AsSpan().IndexOfAny('/', '\\') >= 0)
        {
            return null;
        }

        var root = volume.Equals(PrimaryVolume, StringComparison.Ordinal)
            ? PrimaryRoot
            : $"/storage/{volume}";

        if (string.IsNullOrEmpty(relative))
            return root;

        // Defence in depth: the document picker never emits a rooted or '..'-bearing id, but this runs
        // with all-files access, so resolve any '.'/'..' segments and confirm the result stays inside the
        // volume. Android paths are always POSIX ('/'), so this is done with explicit string work rather
        // than System.IO.Path, whose Windows behaviour (drive-rooting a leading '/', '\' separators) would
        // both corrupt the path and break these tests on the Windows CI runner.
        return CombineUnderRoot(root, relative);
    }

    // Appends a forward-slashed relative path to a POSIX root, resolving '.'/'..' segments and returning
    // null if the path would climb above the root. OS-independent by construction.
    private static string? CombineUnderRoot(string root, string relative)
    {
        var resolved = new List<string>();
        foreach (var segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (segment)
            {
                case ".":
                    continue;
                case "..":
                    if (resolved.Count == 0)
                        return null; // would escape the volume root
                    resolved.RemoveAt(resolved.Count - 1);
                    break;
                default:
                    resolved.Add(segment);
                    break;
            }
        }

        return resolved.Count == 0 ? root : root + "/" + string.Join('/', resolved);
    }

    /// <summary>String overload of <see cref="TryResolveLocalPath(Uri?)"/>.</summary>
    public static string? TryResolveLocalPath(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? TryResolveLocalPath(parsed) : null;

    private static string? ExtractId(string[] segments, string marker)
    {
        var index = Array.IndexOf(segments, marker);
        if (index < 0 || index + 1 >= segments.Length)
            return null;
        return Uri.UnescapeDataString(segments[index + 1]);
    }

    // Mirrors Android's Uri.encode for a single path segment: percent-encode everything except the
    // RFC 3986 unreserved set plus the sub-delims Android leaves literal (!'()*). This reproduces the
    // exact byte form of the URIs measured working on the Thor (':' and '/' escaped, '(' ')' literal),
    // so a URI EmuShelf builds is identical to one the system document picker would hand the emulator.
    private static string Encode(string value)
    {
        var builder = new StringBuilder(value.Length * 3);
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if ((c is >= 'A' and <= 'Z') || (c is >= 'a' and <= 'z') || (c is >= '0' and <= '9') ||
                c is '-' or '_' or '.' or '~' or '!' or '\'' or '(' or ')' or '*')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2"));
            }
        }

        return builder.ToString();
    }
}
