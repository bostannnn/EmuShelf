using System.IO.Compression;
using EmuShelf.Core.SaveSync;

namespace EmuShelf.Infrastructure.SaveSync;

/// <summary>
/// Writes exported saves into a single <c>.zip</c>. The archive is built at a sibling temporary path
/// and moved onto the destination only when <see cref="Complete"/> is called, so an interrupted or
/// failed export never leaves a half-written file where the user chose to save it. Disposing without
/// completing discards the temporary file.
/// </summary>
public sealed class ZipSaveExportSink : ISaveExportSink, IDisposable
{
    private readonly string _destinationPath;
    private readonly string _temporaryPath;
    private readonly FileStream _file;
    private readonly ZipArchive _archive;
    private bool _completed;
    private bool _disposed;

    public ZipSaveExportSink(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        _destinationPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(_destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        _temporaryPath = _destinationPath + ".emushelf-tmp";
        _file = new FileStream(_temporaryPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        _archive = new ZipArchive(_file, ZipArchiveMode.Create, leaveOpen: true);
    }

    public async Task AddFileAsync(string entryPath, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPath);
        ArgumentNullException.ThrowIfNull(content);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var entry = _archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await content.CopyToAsync(entryStream, 81920, cancellationToken);
    }

    /// <summary>Finishes the archive and moves it onto the destination path, replacing any existing file.</summary>
    public void Complete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_completed)
            return;

        _archive.Dispose();
        _file.Dispose();
        File.Move(_temporaryPath, _destinationPath, overwrite: true);
        _completed = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (!_completed)
        {
            _archive.Dispose();
            _file.Dispose();
            TryDeleteTemporary();
        }
    }

    private void TryDeleteTemporary()
    {
        try
        {
            if (File.Exists(_temporaryPath))
                File.Delete(_temporaryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp file next to the destination is harmless; do not mask the real failure.
        }
    }
}
