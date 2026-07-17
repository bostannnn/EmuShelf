namespace EmuShelf.Integrations.Metadata.Chd;

/// <summary>
/// Decodes CHD LZMA-compressed hunks. CHD stores raw LZMA streams (no 13-byte header)
/// with fixed properties encoded as byte 93 (lc=3, lp=0, pb=2); the dictionary window is
/// the output hunk itself, so only the literal-context settings are needed here.
/// </summary>
internal sealed class ChdLzmaDecoder
{
    // libchdr writes decoder_props[0] = 93 for every CHD LZMA stream.
    private const int PropByte = 93;
    private readonly int _lc;
    private readonly int _lp;
    private readonly int _pb;

    public ChdLzmaDecoder(uint hunkBytes)
    {
        _ = hunkBytes;
        var value = PropByte;
        _lc = value % 9;
        value /= 9;
        _lp = value % 5;
        _pb = value / 5;
    }

    public bool TryDecompress(byte[] input, byte[] output) =>
        ChdLzma.TryDecode(input, output, _lc, _lp, _pb);
}
