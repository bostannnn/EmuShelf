namespace EmuShelf.Core.Storage.Android;

/// <summary>
/// Decides, as a pure function, whether a set of persisted SAF grants EmuShelf itself holds covers a
/// game's launch URI — i.e. whether EmuShelf can delegate a read grant for that URI to an emulator with
/// <c>FLAG_GRANT_READ_URI_PERMISSION</c> instead of relying on the emulator's own grant.
///
/// The authoritative runtime check is Android's <c>ContentResolver.CheckUriPermission</c>, which the head
/// still calls before attaching the flag. This exists so the head can reason about coverage <em>without</em>
/// a live context (which held grant to ask for, "do we already hold one?") and so the matching semantics are
/// exercised in the desktop suite. It works only for the <c>com.android.externalstorage.documents</c>
/// provider, the one mapped to real <c>/storage/…</c> paths — every launch URI EmuShelf builds is of that
/// provider — by resolving both the held tree and the target document back to their paths (reusing the tested
/// <see cref="AndroidExternalStorageUri"/>) and testing directory containment.
/// </summary>
public static class AndroidUriGrantCoverage
{
    /// <summary>
    /// True when any of <paramref name="heldTreeUris"/> (SAF tree URIs EmuShelf holds a persisted grant to)
    /// covers <paramref name="targetContentUri"/> (the ROM's <c>tree/document</c> launch URI).
    /// </summary>
    public static bool IsCovered(IEnumerable<string> heldTreeUris, string? targetContentUri) =>
        FindCoveringGrant(heldTreeUris, targetContentUri) is not null;

    /// <summary>
    /// The first held grant URI that covers <paramref name="targetContentUri"/>, or null when none does
    /// (including when the target is not an external-storage content URI EmuShelf could grant).
    /// </summary>
    public static string? FindCoveringGrant(IEnumerable<string> heldTreeUris, string? targetContentUri)
    {
        ArgumentNullException.ThrowIfNull(heldTreeUris);

        var targetPath = AndroidExternalStorageUri.TryResolveLocalPath(targetContentUri);
        if (targetPath is null)
            return null;

        foreach (var held in heldTreeUris)
        {
            var heldPath = AndroidExternalStorageUri.TryResolveLocalPath(held);
            if (heldPath is not null && IsAncestorOrSelf(heldPath, targetPath))
                return held;
        }

        return null;
    }

    // True when 'ancestor' is the same real path as, or a directory prefix of, 'path'. Both are already
    // resolved /storage/… paths (POSIX '/'), so a segment-boundary prefix check is exact — "/a/roms" must
    // not be treated as covering "/a/roms-backup/x".
    private static bool IsAncestorOrSelf(string ancestor, string path)
    {
        var normalisedAncestor = ancestor.TrimEnd('/');
        if (path.Equals(normalisedAncestor, StringComparison.Ordinal))
            return true;
        return path.StartsWith(normalisedAncestor + "/", StringComparison.Ordinal);
    }
}
