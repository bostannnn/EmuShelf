namespace EmuShelf.Integrations.Metadata.Chd;

/// <summary>
/// MSB-first bit reader over a byte buffer, ported from MAME/libchdr's bitstream so the
/// compressed CHD hunk map decodes bit-for-bit identically.
/// </summary>
internal sealed class ChdBitReader
{
    private readonly byte[] _data;
    private readonly int _length;
    private uint _buffer;
    private int _bits;
    private int _offset;

    public ChdBitReader(byte[] data)
    {
        _data = data;
        _length = data.Length;
    }

    public bool Overflow => _offset - _bits / 8 > _length;

    public uint Peek(int numBits)
    {
        if (numBits == 0)
            return 0;
        if (numBits > _bits)
        {
            while (_bits <= 24)
            {
                if (_offset < _length)
                    _buffer |= (uint)(_data[_offset] << (24 - _bits));
                _offset++;
                _bits += 8;
            }
        }
        return _buffer >> (32 - numBits);
    }

    public void Remove(int numBits)
    {
        _buffer <<= numBits;
        _bits -= numBits;
    }

    public uint Read(int numBits)
    {
        var result = Peek(numBits);
        Remove(numBits);
        return result;
    }
}
