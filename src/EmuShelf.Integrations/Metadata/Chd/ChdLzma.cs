namespace EmuShelf.Integrations.Metadata.Chd;

/// <summary>
/// Minimal LZMA decoder for a single raw LZMA stream (no 13-byte header, no end marker,
/// known output size). Ported from Igor Pavlov's public-domain LZMA SDK reference decoder
/// and specialized to decode directly into a caller-provided output buffer, which also
/// serves as the dictionary window. Verified against chdman-produced LZMA hunks.
/// </summary>
internal static class ChdLzma
{
    private const uint TopValue = 1u << 24;
    private const int NumBitModelTotalBits = 11;
    private const uint BitModelTotal = 1u << NumBitModelTotalBits;
    private const int NumMoveBits = 5;
    private const int NumPosBitsMax = 4;
    private const int NumStates = 12;
    private const int NumLenToPosStates = 4;
    private const int NumAlignBits = 4;
    private const int EndPosModelIndex = 14;
    private const int NumFullDistances = 1 << (EndPosModelIndex >> 1);
    private const int MatchMinLen = 2;

    public static bool TryDecode(byte[] input, byte[] output, int lc, int lp, int pb)
    {
        try
        {
            new Decoder(lc, lp, pb).Decode(input, output);
            return true;
        }
        catch (IndexOutOfRangeException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private sealed class Decoder
    {
        private byte[] _input = [];
        private int _inPos;
        private uint _range;
        private uint _code;

        private readonly int _lc;
        private readonly int _lp;
        private readonly uint _posMask;
        private readonly uint _literalPosMask;

        private readonly ushort[] _isMatch = NewProbs(NumStates << NumPosBitsMax);
        private readonly ushort[] _isRep = NewProbs(NumStates);
        private readonly ushort[] _isRepG0 = NewProbs(NumStates);
        private readonly ushort[] _isRepG1 = NewProbs(NumStates);
        private readonly ushort[] _isRepG2 = NewProbs(NumStates);
        private readonly ushort[] _isRep0Long = NewProbs(NumStates << NumPosBitsMax);
        private readonly ushort[] _posSlot = NewProbs(NumLenToPosStates << 6);
        private readonly ushort[] _posDecoders = NewProbs(NumFullDistances - EndPosModelIndex);
        private readonly ushort[] _posAlign = NewProbs(1 << NumAlignBits);
        private readonly ushort[] _literals;
        private readonly LenDecoder _lenDecoder = new();
        private readonly LenDecoder _repLenDecoder = new();

        public Decoder(int lc, int lp, int pb)
        {
            _lc = lc;
            _lp = lp;
            _posMask = (1u << pb) - 1;
            _literalPosMask = (1u << lp) - 1;
            _literals = NewProbs(0x300 << (lc + lp));
        }

        public void Decode(byte[] input, byte[] output)
        {
            _input = input;
            InitRange();

            uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;
            var state = 0;
            var outPos = 0;
            var length = output.Length;

            while (outPos < length)
            {
                var posState = (uint)outPos & _posMask;
                if (DecodeBit(_isMatch, (uint)(state << NumPosBitsMax) + posState) == 0)
                {
                    var prevByte = outPos > 0 ? output[outPos - 1] : (byte)0;
                    var litState =
                        (((uint)outPos & _literalPosMask) << _lc) + ((uint)prevByte >> (8 - _lc));
                    output[outPos] = state < 7
                        ? DecodeLiteral(litState)
                        : DecodeLiteralMatched(litState, output[outPos - (int)rep0 - 1]);
                    outPos++;
                    state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
                    continue;
                }

                uint len;
                if (DecodeBit(_isRep, (uint)state) == 1)
                {
                    if (DecodeBit(_isRepG0, (uint)state) == 0)
                    {
                        if (DecodeBit(_isRep0Long, (uint)(state << NumPosBitsMax) + posState) == 0)
                        {
                            state = state < 7 ? 9 : 11;
                            output[outPos] = output[outPos - (int)rep0 - 1];
                            outPos++;
                            continue;
                        }
                    }
                    else
                    {
                        uint distance;
                        if (DecodeBit(_isRepG1, (uint)state) == 0)
                        {
                            distance = rep1;
                        }
                        else
                        {
                            if (DecodeBit(_isRepG2, (uint)state) == 0)
                            {
                                distance = rep2;
                            }
                            else
                            {
                                distance = rep3;
                                rep3 = rep2;
                            }
                            rep2 = rep1;
                        }
                        rep1 = rep0;
                        rep0 = distance;
                    }
                    len = _repLenDecoder.Decode(this, posState) + MatchMinLen;
                    state = state < 7 ? 8 : 11;
                }
                else
                {
                    rep3 = rep2;
                    rep2 = rep1;
                    rep1 = rep0;
                    len = _lenDecoder.Decode(this, posState) + MatchMinLen;
                    state = state < 7 ? 7 : 10;
                    rep0 = DecodeDistance(len);
                    if (rep0 == 0xFFFFFFFF)
                        return; // end marker (not used by CHD, but handled defensively)
                }

                if (rep0 >= (uint)outPos)
                    throw new InvalidOperationException("LZMA distance exceeds output.");
                for (var i = 0; i < len && outPos < length; i++, outPos++)
                    output[outPos] = output[outPos - (int)rep0 - 1];
            }
        }

        private uint DecodeDistance(uint len)
        {
            var lenState = (int)Math.Min(len - MatchMinLen, NumLenToPosStates - 1);
            var posSlot = BitTreeDecode(_posSlot, lenState << 6, 6);
            if (posSlot < 4)
                return posSlot;

            var numDirect = (int)((posSlot >> 1) - 1);
            var dist = (2 | (posSlot & 1)) << numDirect;
            if (posSlot < EndPosModelIndex)
            {
                dist += ReverseBitTreeDecode(
                    _posDecoders, (int)dist - (int)posSlot - 1, numDirect);
            }
            else
            {
                dist += DecodeDirectBits(numDirect - NumAlignBits) << NumAlignBits;
                dist += ReverseBitTreeDecode(_posAlign, 0, NumAlignBits);
            }
            return (uint)dist;
        }

        private byte DecodeLiteral(uint litState)
        {
            var offset = (int)(0x300 * litState);
            uint symbol = 1;
            do
            {
                symbol = (symbol << 1) | DecodeBit(_literals, (uint)(offset + symbol));
            }
            while (symbol < 0x100);
            return (byte)symbol;
        }

        private byte DecodeLiteralMatched(uint litState, byte matchByte)
        {
            var offset = (int)(0x300 * litState);
            uint symbol = 1;
            uint match = matchByte;
            do
            {
                var matchBit = (match >> 7) & 1;
                match <<= 1;
                var bit = DecodeBit(_literals, (uint)(offset + ((1 + matchBit) << 8) + symbol));
                symbol = (symbol << 1) | bit;
                if (matchBit != bit)
                {
                    while (symbol < 0x100)
                        symbol = (symbol << 1) | DecodeBit(_literals, (uint)(offset + symbol));
                    break;
                }
            }
            while (symbol < 0x100);
            return (byte)symbol;
        }

        private void InitRange()
        {
            _inPos = 0;
            _code = 0;
            _range = 0xFFFFFFFF;
            for (var i = 0; i < 5; i++)
                _code = (_code << 8) | NextByte();
        }

        private byte NextByte() => _input[_inPos++];

        private void Normalize()
        {
            if (_range < TopValue)
            {
                _code = (_code << 8) | NextByte();
                _range <<= 8;
            }
        }

        public uint DecodeBit(ushort[] probs, uint index)
        {
            var prob = probs[index];
            var bound = (_range >> NumBitModelTotalBits) * prob;
            uint result;
            if (_code < bound)
            {
                _range = bound;
                probs[index] = (ushort)(prob + ((BitModelTotal - prob) >> NumMoveBits));
                result = 0;
            }
            else
            {
                _range -= bound;
                _code -= bound;
                probs[index] = (ushort)(prob - (prob >> NumMoveBits));
                result = 1;
            }
            Normalize();
            return result;
        }

        public uint DecodeDirectBits(int numBits)
        {
            uint result = 0;
            for (; numBits > 0; numBits--)
            {
                _range >>= 1;
                var t = (_code - _range) >> 31;
                _code -= _range & (t - 1);
                result = (result << 1) | (1 - t);
                Normalize();
            }
            return result;
        }

        public uint BitTreeDecode(ushort[] probs, int offset, int numBits)
        {
            uint m = 1;
            for (var i = 0; i < numBits; i++)
                m = (m << 1) | DecodeBit(probs, (uint)(offset + m));
            return m - (1u << numBits);
        }

        public uint ReverseBitTreeDecode(ushort[] probs, int offset, int numBits)
        {
            uint m = 1;
            uint symbol = 0;
            for (var i = 0; i < numBits; i++)
            {
                var bit = DecodeBit(probs, (uint)(offset + m));
                m = (m << 1) | bit;
                symbol |= bit << i;
            }
            return symbol;
        }

        private static ushort[] NewProbs(int count)
        {
            var probs = new ushort[count];
            Array.Fill(probs, (ushort)(BitModelTotal >> 1));
            return probs;
        }
    }

    private sealed class LenDecoder
    {
        private readonly ushort[] _choice =
            { (ushort)(BitModelTotal >> 1), (ushort)(BitModelTotal >> 1) };
        private readonly ushort[] _low = NewProbs((1 << NumPosBitsMax) << 3);
        private readonly ushort[] _mid = NewProbs((1 << NumPosBitsMax) << 3);
        private readonly ushort[] _high = NewProbs(1 << 8);

        public uint Decode(Decoder decoder, uint posState)
        {
            if (decoder.DecodeBit(_choice, 0) == 0)
                return decoder.BitTreeDecode(_low, (int)(posState << 3), 3);
            if (decoder.DecodeBit(_choice, 1) == 0)
                return 8 + decoder.BitTreeDecode(_mid, (int)(posState << 3), 3);
            return 16 + decoder.BitTreeDecode(_high, 0, 8);
        }

        private static ushort[] NewProbs(int count)
        {
            var probs = new ushort[count];
            Array.Fill(probs, (ushort)(BitModelTotal >> 1));
            return probs;
        }
    }
}
