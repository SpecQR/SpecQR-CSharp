namespace SpecQR;

/// <summary>QR Reed-Solomon block coding over GF(256), reduced by x⁸+x⁴+x³+x²+1.</summary>
internal static class ReedSolomon
{
    public static byte Multiply(byte left, byte right)
    {
        int multiplicand = left, multiplier = right, product = 0;
        while (multiplier != 0)
        {
            if ((multiplier & 1) != 0) product ^= multiplicand;
            multiplier >>= 1;
            multiplicand <<= 1;
            if ((multiplicand & 0x100) != 0) multiplicand ^= 0x11D;
        }
        return (byte)product;
    }

    // Coefficients are in descending power order, including the leading one.
    public static byte[] Generator(int degree)
    {
        if (degree is < 1 or > 255)
            throw new ArgumentOutOfRangeException(nameof(degree), degree, "The generator degree must be from 1 through 255.");
        var coefficients = new byte[degree + 1];
        coefficients[0] = 1;
        byte root = 1;
        for (int factor = 0; factor < degree; factor++)
        {
            // Update from high indices so every old coefficient is consumed first.
            for (int index = factor + 1; index > 0; index--)
                coefficients[index] ^= Multiply(coefficients[index - 1], root);
            root = Multiply(root, 2);
        }
        return coefficients;
    }

    public static byte[] Remainder(ReadOnlySpan<byte> data, ReadOnlySpan<byte> generator)
    {
        if (generator.Length is < 2 or > 256 || generator[0] != 1)
            throw new ArgumentException("The generator must be monic with degree from 1 through 255.", nameof(generator));
        int degree = generator.Length - 1;
        var remainder = new byte[degree];
        foreach (byte value in data)
        {
            byte factor = (byte)(value ^ remainder[0]);
            for (int index = 0; index < degree - 1; index++)
                remainder[index] = (byte)(remainder[index + 1] ^ Multiply(generator[index + 1], factor));
            remainder[degree - 1] = Multiply(generator[degree], factor);
        }
        return remainder;
    }

    public static byte[] Interleave(byte[] data, int version, ErrorCorrectionLevel level)
    {
        ArgumentNullException.ThrowIfNull(data);
        int expectedDataLength = QRTables.DataCodewords(version, level);
        if (data.Length != expectedDataLength)
            throw new ArgumentException($"Expected {expectedDataLength} data codewords for version {version} at {level}.", nameof(data));

        int blockCount = QRTables.BlockCount(version, level);
        int eccLength = QRTables.EccPerBlock(version, level);
        int rawLength = QRTables.RawCodewords(version);
        int shortCount = blockCount - rawLength % blockCount;
        int shortDataLength = data.Length / blockCount;
        byte[] generator = Generator(eccLength);
        var checks = new byte[blockCount][];
        var starts = new int[blockCount];
        var lengths = new int[blockCount];
        int sourceOffset = 0;
        for (int block = 0; block < blockCount; block++)
        {
            int length = shortDataLength + (block < shortCount ? 0 : 1);
            starts[block] = sourceOffset;
            lengths[block] = length;
            checks[block] = Remainder(data.AsSpan(sourceOffset, length), generator);
            sourceOffset += length;
        }

        var result = new byte[rawLength];
        int destination = 0;
        for (int column = 0; column <= shortDataLength; column++)
            for (int block = 0; block < blockCount; block++)
                if (column < lengths[block])
                    result[destination++] = data[starts[block] + column];
        for (int column = 0; column < eccLength; column++)
            for (int block = 0; block < blockCount; block++)
                result[destination++] = checks[block][column];

        if (sourceOffset != data.Length || destination != result.Length)
            throw new InvalidOperationException("QR block interleaving produced an inconsistent length.");
        return result;
    }
}
