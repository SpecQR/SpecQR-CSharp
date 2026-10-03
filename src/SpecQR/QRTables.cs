namespace SpecQR;

/// <summary>QR Model 2 parameters, checked against the user-owned SpecQR reference.</summary>
internal static class QRTables
{
    // Rows are L, M, Q, H; column zero is deliberately unused.
    private static readonly byte[][] EccLengths =
    [
        [0, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        [0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28],
        [0, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        [0, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30]
    ];

    private static readonly byte[][] BlockCounts =
    [
        [0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25],
        [0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49],
        [0, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68],
        [0, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81]
    ];

    public static void ValidateVersion(int version)
    {
        if (version is < 1 or > 40)
            throw new ArgumentOutOfRangeException(nameof(version), version, "QR version must be from 1 through 40.");
    }

    public static void ValidateLevel(ErrorCorrectionLevel level)
    {
        if (level is not (ErrorCorrectionLevel.L or ErrorCorrectionLevel.M or ErrorCorrectionLevel.Q or ErrorCorrectionLevel.H))
            throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown error correction level.");
    }

    public static int Size(int version)
    {
        ValidateVersion(version);
        return 4 * version + 17;
    }

    public static int RawCodewords(int version)
    {
        ValidateVersion(version);
        int modules = (16 * version + 128) * version + 64;
        if (version >= 2)
        {
            int centers = version / 7 + 2;
            modules -= (25 * centers - 10) * centers - 55;
            if (version >= 7) modules -= 36;
        }
        return modules / 8;
    }

    public static int BlockCount(int version, ErrorCorrectionLevel level)
    {
        ValidateVersion(version);
        ValidateLevel(level);
        return BlockCounts[(int)level][version];
    }

    public static int EccPerBlock(int version, ErrorCorrectionLevel level)
    {
        ValidateVersion(version);
        ValidateLevel(level);
        return EccLengths[(int)level][version];
    }

    public static int DataCodewords(int version, ErrorCorrectionLevel level) =>
        RawCodewords(version) - BlockCount(version, level) * EccPerBlock(version, level);

    public static int CountBits(QRMode mode, int version)
    {
        ValidateVersion(version);
        return mode switch
        {
            QRMode.Numeric => version <= 9 ? 10 : version <= 26 ? 12 : 14,
            QRMode.Alphanumeric => version <= 9 ? 9 : version <= 26 ? 11 : 13,
            QRMode.Byte => version <= 9 ? 8 : 16,
            QRMode.Kanji => version <= 9 ? 8 : version <= 26 ? 10 : 12,
            QRMode.Eci or QRMode.Fnc1 or QRMode.Fnc1Second or QRMode.StructuredAppend => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "The mode is not an encodable segment mode.")
        };
    }

    public static int FormatBits(ErrorCorrectionLevel level) => level switch
    {
        ErrorCorrectionLevel.L => 1,
        ErrorCorrectionLevel.M => 0,
        ErrorCorrectionLevel.Q => 3,
        ErrorCorrectionLevel.H => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown error correction level.")
    };

    public static int[] AlignmentCenters(int version)
    {
        ValidateVersion(version);
        if (version == 1) return [];
        int count = version / 7 + 2;
        int denominator = 2 * (count - 1);
        int step = version == 32 ? 26 : 2 * ((4 * version + 4 + denominator - 1) / denominator);
        var centers = new int[count];
        centers[0] = 6;
        for (int index = count - 1, position = 4 * version + 10; index > 0; index--, position -= step)
            centers[index] = position;
        return centers;
    }
}
