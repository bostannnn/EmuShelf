namespace EmuShelf.Integrations.Metadata.Chd;

/// <summary>
/// Canonical Huffman decoder ported from MAME/libchdr, used to decode the compressed CHD
/// hunk map's compression-type stream. Only the RLE tree import and single-symbol decode
/// paths are needed here.
/// </summary>
internal sealed class ChdHuffmanDecoder
{
    private readonly int _numCodes;
    private readonly int _maxBits;
    private readonly uint[] _lookup;
    private readonly byte[] _nodeNumBits;
    private readonly uint[] _nodeBits;

    public ChdHuffmanDecoder(int numCodes, int maxBits)
    {
        _numCodes = numCodes;
        _maxBits = maxBits;
        _lookup = new uint[1 << maxBits];
        _nodeNumBits = new byte[numCodes];
        _nodeBits = new uint[numCodes];
    }

    public uint DecodeOne(ChdBitReader bits)
    {
        var lookup = _lookup[bits.Peek(_maxBits)];
        bits.Remove((int)(lookup & 0x1f));
        return lookup >> 5;
    }

    public bool TryImportTreeRle(ChdBitReader bits)
    {
        var numBits = _maxBits >= 16 ? 5 : _maxBits >= 8 ? 4 : 3;
        var node = 0;
        while (node < _numCodes)
        {
            var nodeBits = (int)bits.Read(numBits);
            if (nodeBits != 1)
            {
                _nodeNumBits[node++] = (byte)nodeBits;
                continue;
            }

            nodeBits = (int)bits.Read(numBits);
            if (nodeBits == 1)
            {
                _nodeNumBits[node++] = (byte)nodeBits;
                continue;
            }

            var repeat = (int)bits.Read(numBits) + 3;
            if (repeat + node > _numCodes)
                return false;
            while (repeat-- > 0)
                _nodeNumBits[node++] = (byte)nodeBits;
        }

        if (node != _numCodes || !AssignCanonicalCodes())
            return false;
        BuildLookupTable();
        return !bits.Overflow;
    }

    private bool AssignCanonicalCodes()
    {
        var histogram = new uint[33];
        for (var code = 0; code < _numCodes; code++)
        {
            var length = _nodeNumBits[code];
            if (length > _maxBits)
                return false;
            if (length <= 32)
                histogram[length]++;
        }

        uint start = 0;
        for (var length = 32; length > 0; length--)
        {
            var next = (start + histogram[length]) >> 1;
            if (length != 1 && next * 2 != start + histogram[length])
                return false;
            histogram[length] = start;
            start = next;
        }

        for (var code = 0; code < _numCodes; code++)
        {
            var length = _nodeNumBits[code];
            if (length > 0)
                _nodeBits[code] = histogram[length]++;
        }
        return true;
    }

    private void BuildLookupTable()
    {
        for (var code = 0; code < _numCodes; code++)
        {
            var length = _nodeNumBits[code];
            if (length == 0)
                continue;

            var value = ((uint)code << 5) | (length & 0x1fu);
            var shift = _maxBits - length;
            var start = _nodeBits[code] << shift;
            var end = (_nodeBits[code] + 1) << shift;
            for (var index = start; index < end; index++)
                _lookup[index] = value;
        }
    }
}
