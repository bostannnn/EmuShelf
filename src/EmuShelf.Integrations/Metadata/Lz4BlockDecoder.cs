namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Decompresses a single LZ4 block (raw block format, not the framed format) into a
/// caller-sized output buffer. ZSO stores each disc block as one LZ4 block. Only enough
/// bytes to fill <paramref name="output"/> are produced; trailing alignment padding in
/// <paramref name="input"/> is ignored.
/// </summary>
internal static class Lz4BlockDecoder
{
    public static bool TryDecompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var inputPosition = 0;
        var outputPosition = 0;

        while (outputPosition < output.Length)
        {
            if (inputPosition >= input.Length)
                return false;
            var token = input[inputPosition++];

            var literalLength = token >> 4;
            if (literalLength == 15 && !TryReadLengthExtension(input, ref inputPosition, ref literalLength))
                return false;

            if (literalLength > 0)
            {
                if (inputPosition + literalLength > input.Length ||
                    outputPosition + literalLength > output.Length)
                    return false;
                input.Slice(inputPosition, literalLength).CopyTo(output[outputPosition..]);
                inputPosition += literalLength;
                outputPosition += literalLength;
            }

            // The final sequence is literals only and completes the block.
            if (outputPosition >= output.Length)
                break;

            if (inputPosition + 2 > input.Length)
                return false;
            var offset = input[inputPosition] | (input[inputPosition + 1] << 8);
            inputPosition += 2;
            if (offset == 0 || offset > outputPosition)
                return false;

            var matchLength = token & 0x0F;
            if (matchLength == 15 && !TryReadLengthExtension(input, ref inputPosition, ref matchLength))
                return false;
            matchLength += 4;
            if (outputPosition + matchLength > output.Length)
                return false;

            var matchPosition = outputPosition - offset;
            for (var index = 0; index < matchLength; index++)
                output[outputPosition + index] = output[matchPosition + index];
            outputPosition += matchLength;
        }

        return outputPosition == output.Length;
    }

    private static bool TryReadLengthExtension(
        ReadOnlySpan<byte> input,
        ref int inputPosition,
        ref int length)
    {
        int extension;
        do
        {
            if (inputPosition >= input.Length)
                return false;
            extension = input[inputPosition++];
            length += extension;
        }
        while (extension == 255);
        return true;
    }
}
