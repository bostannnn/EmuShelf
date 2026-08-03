using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.Infrastructure.Storage;

/// <summary>Internal reusable DPAPI boundary for portable credential blobs on Windows.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsDpapiProtectedTextStore
{
    private readonly string _blobPath;
    private readonly string _credentialName;
    private readonly IAppLogger _logger;

    public WindowsDpapiProtectedTextStore(
        string blobPath,
        string credentialName,
        IAppLogger? logger = null)
    {
        _blobPath = blobPath;
        _credentialName = credentialName;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public string? Read()
    {
        if (!File.Exists(_blobPath))
            return null;

        try
        {
            var protectedBytes = File.ReadAllBytes(_blobPath);
            var plain = Transform(protectedBytes, encrypt: false);
            return plain is null ? null : Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not read the {_credentialName} credential blob.", ex);
            return null;
        }
    }

    public void Write(string value)
    {
        var plain = Encoding.UTF8.GetBytes(value);
        var protectedBytes = Transform(plain, encrypt: true)
            ?? throw new InvalidOperationException($"DPAPI protection failed for {_credentialName} credentials.");
        AtomicFile.WriteAllBytes(_blobPath, protectedBytes);
    }

    public void Clear()
    {
        if (File.Exists(_blobPath))
            File.Delete(_blobPath);
    }

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
