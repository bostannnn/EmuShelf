using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.Infrastructure.Achievements;

/// <summary>
/// Stores the Web API key as a DPAPI-protected blob under portable <c>Settings/</c> on Windows.
/// The key is encrypted for the current user (crypt32 <c>CryptProtectData</c>) so it travels with
/// the portable install but is unreadable by other users and never appears as plaintext on disk.
/// DPAPI is reached through P/Invoke to avoid an extra package dependency.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiCredentialStore : IRetroAchievementsCredentialStore
{
    private readonly string _blobPath;
    private readonly IAppLogger _logger;

    public WindowsDpapiCredentialStore(string blobPath, IAppLogger? logger = null)
    {
        _blobPath = blobPath;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public string? GetApiKey()
    {
        if (!File.Exists(_blobPath))
            return null;

        try
        {
            var protectedBytes = File.ReadAllBytes(_blobPath);
            var plain = Unprotect(protectedBytes);
            return plain is null ? null : Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning("Could not read the RetroAchievements credential blob.", ex);
            return null;
        }
    }

    public void SaveApiKey(string apiKey)
    {
        var plain = Encoding.UTF8.GetBytes(apiKey);
        var protectedBytes = Protect(plain)
            ?? throw new InvalidOperationException("DPAPI protection failed for the API key.");
        var tempPath = _blobPath + ".tmp";
        File.WriteAllBytes(tempPath, protectedBytes);
        File.Move(tempPath, _blobPath, overwrite: true);
    }

    public void ClearApiKey()
    {
        if (File.Exists(_blobPath))
            File.Delete(_blobPath);
    }

    private static byte[]? Protect(byte[] plain) => Transform(plain, encrypt: true);

    private static byte[]? Unprotect(byte[] cipher) => Transform(cipher, encrypt: false);

    private static byte[]? Transform(byte[] input, bool encrypt)
    {
        var inputBlob = default(DataBlob);
        var outputBlob = default(DataBlob);
        var handle = GCHandle.Alloc(input, GCHandleType.Pinned);
        try
        {
            inputBlob.DataSize = input.Length;
            inputBlob.DataPointer = handle.AddrOfPinnedObject();

            var success = encrypt
                ? CryptProtectData(
                    ref inputBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, ref outputBlob)
                : CryptUnprotectData(
                    ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, ref outputBlob);
            if (!success || outputBlob.DataPointer == IntPtr.Zero)
                return null;

            var result = new byte[outputBlob.DataSize];
            Marshal.Copy(outputBlob.DataPointer, result, 0, outputBlob.DataSize);
            return result;
        }
        finally
        {
            handle.Free();
            if (outputBlob.DataPointer != IntPtr.Zero)
                LocalFree(outputBlob.DataPointer);
        }
    }

    private const int CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int DataSize;
        public IntPtr DataPointer;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved,
        IntPtr prompt, int flags, ref DataBlob output);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr handle);
}
