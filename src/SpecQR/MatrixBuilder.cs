namespace SpecQR;

internal sealed record MatrixBuildResult(
    bool[][] Matrix, int MaskPattern, int Penalty, IReadOnlyList<QRMaskPenalty> Penalties);

/// <summary>Places QR function and data modules and selects the lowest-penalty mask.</summary>
internal static class MatrixBuilder
{
    public static MatrixBuildResult Build(byte[] codewords, int version, ErrorCorrectionLevel level, int? mask)
    {
        ArgumentNullException.ThrowIfNull(codewords);
        int size = QRTables.Size(version);
        QRTables.ValidateLevel(level);
        if (mask is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(mask), mask, "QR mask must be from 0 through 7.");
        if (codewords.Length != QRTables.RawCodewords(version))
            throw new ArgumentException("The complete interleaved codewords must exactly fill the selected version.", nameof(codewords));

        var template = new SymbolGrid(size);
        template.DrawFunctions(version, level);
        template.PlaceData(codewords);
        var penalties = new List<QRMaskPenalty>(mask.HasValue ? 1 : 8);
        int bestMask = 0, bestPenalty = int.MaxValue;
        SymbolGrid? selected = null;
        int firstMask = mask ?? 0, lastMask = mask ?? 7;
        for (int candidateMask = firstMask; candidateMask <= lastMask; candidateMask++)
        {
            var candidate = template.Clone();
            candidate.ApplyMask(candidateMask);
            candidate.DrawFormat(level, candidateMask);
            int penalty = MaskPenaltyEvaluator.Score(candidate.Modules, size);
            penalties.Add(new QRMaskPenalty(candidateMask, penalty));
            // Strict comparison preserves the lower mask number on a tie.
            if (penalty < bestPenalty)
            {
                selected = candidate;
                bestMask = candidateMask;
                bestPenalty = penalty;
            }
        }
        return new MatrixBuildResult(selected!.ToRows(), bestMask, bestPenalty, penalties.AsReadOnly());
    }

    // Flat buffers keep coordinates unambiguous: index = row * size + column.
    // Function ownership is separate from its current color, including white bits.
    private sealed class SymbolGrid
    {
        private readonly int size;
        private readonly bool[] reserved;
        public bool[] Modules { get; }

        public SymbolGrid(int size)
        {
            this.size = size;
            Modules = new bool[size * size];
            reserved = new bool[size * size];
        }

        private SymbolGrid(SymbolGrid source)
        {
            size = source.size;
            Modules = (bool[])source.Modules.Clone();
            reserved = (bool[])source.reserved.Clone();
        }

        public SymbolGrid Clone() => new(this);

        private void Function(int column, int row, bool dark)
        {
            // Finder separators at an outside edge are clipped to the symbol.
            if (column < 0 || row < 0 || column >= size || row >= size) return;
            int index = row * size + column;
            reserved[index] = true;
            Modules[index] = dark;
        }

        public void DrawFunctions(int version, ErrorCorrectionLevel level)
        {
            DrawFinder(3, 3);
            DrawFinder(size - 4, 3);
            DrawFinder(3, size - 4);
            for (int coordinate = 8; coordinate < size - 8; coordinate++)
            {
                Function(coordinate, 6, (coordinate & 1) == 0);
                Function(6, coordinate, (coordinate & 1) == 0);
            }

            int[] centers = QRTables.AlignmentCenters(version);
            int last = centers.Length - 1;
            for (int row = 0; row < centers.Length; row++)
                for (int column = 0; column < centers.Length; column++)
                {
                    if ((row == 0 && (column == 0 || column == last)) || (row == last && column == 0))
                        continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            Function(centers[column] + dx, centers[row] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }

            DrawFormat(level, 0);
            Function(8, size - 8, true);
            if (version >= 7)
            {
                int bits = BchWord(version, 12, 0x1F25);
                for (int bit = 0; bit < 18; bit++)
                {
                    int far = size - 11 + bit % 3, near = bit / 3;
                    bool dark = (bits & (1 << bit)) != 0;
                    Function(far, near, dark);
                    Function(near, far, dark);
                }
            }
        }

        private void DrawFinder(int centerColumn, int centerRow)
        {
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int radius = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Function(centerColumn + dx, centerRow + dy, radius != 2 && radius != 4);
                }
        }

        public void DrawFormat(ErrorCorrectionLevel level, int mask)
        {
            int bits = BchWord((QRTables.FormatBits(level) << 3) | mask, 10, 0x537) ^ 0x5412;
            for (int bit = 0; bit < 15; bit++)
            {
                bool dark = (bits & (1 << bit)) != 0;
                if (bit < 6) Function(8, bit, dark);
                else if (bit < 8) Function(8, bit + 1, dark);
                else Function(14 - bit + (bit == 8 ? 1 : 0), 8, dark);
                if (bit < 8) Function(size - 1 - bit, 8, dark);
                else Function(8, size - 15 + bit, dark);
            }
        }

        // Polynomial long division in GF(2), with the systematic payload retained.
        private static int BchWord(int payload, int degree, int polynomial)
        {
            int remainder = payload;
            for (int bit = 0; bit < degree; bit++)
            {
                bool top = (remainder & (1 << (degree - 1))) != 0;
                remainder <<= 1;
                if (top) remainder ^= polynomial;
            }
            return (payload << degree) | remainder;
        }

        public void PlaceData(ReadOnlySpan<byte> codewords)
        {
            int bit = 0;
            bool upward = true;
            for (int right = size - 1; right > 0; right -= 2)
            {
                if (right == 6) right--;
                for (int step = 0; step < size; step++)
                {
                    int row = upward ? size - 1 - step : step;
                    for (int column = right; column >= right - 1; column--)
                    {
                        int index = row * size + column;
                        if (reserved[index]) continue;
                        Modules[index] = bit < codewords.Length * 8 &&
                            (codewords[bit / 8] & (1 << (7 - bit % 8))) != 0;
                        bit++;
                    }
                }
                upward = !upward;
            }
            // Remaining modules are the standard's 0..7 zero-valued remainder bits.
            if (bit < codewords.Length * 8 || bit - codewords.Length * 8 > 7)
                throw new InvalidOperationException("QR data placement did not match the symbol's available modules.");
        }

        public void ApplyMask(int mask)
        {
            for (int row = 0; row < size; row++)
                for (int column = 0; column < size; column++)
                {
                    int index = row * size + column;
                    if (!reserved[index] && Invert(mask, column, row)) Modules[index] = !Modules[index];
                }
        }

        private static bool Invert(int mask, int column, int row)
        {
            int product = column * row;
            return mask switch
            {
                0 => (column + row) % 2 == 0,
                1 => row % 2 == 0,
                2 => column % 3 == 0,
                3 => (column + row) % 3 == 0,
                4 => (row / 2 + column / 3) % 2 == 0,
                5 => product % 2 + product % 3 == 0,
                6 => (product % 2 + product % 3) % 2 == 0,
                7 => ((column + row) % 2 + product % 3) % 2 == 0,
                _ => throw new ArgumentOutOfRangeException(nameof(mask))
            };
        }

        public bool[][] ToRows()
        {
            var rows = new bool[size][];
            for (int row = 0; row < size; row++)
                rows[row] = Modules.AsSpan(row * size, size).ToArray();
            return rows;
        }
    }
}

/// <summary>SpecQR N1–N4 scoring; finder-like patterns use exact in-symbol 11-module windows.</summary>
internal static class MaskPenaltyEvaluator
{
    public static int Score(ReadOnlySpan<bool> modules, int size)
    {
        if (size is < 1 or > 177 || modules.Length != size * size)
            throw new ArgumentException("The matrix must be square and no larger than a version 40 symbol.", nameof(modules));
        int penalty = 0, dark = 0;
        for (int line = 0; line < size; line++)
        {
            penalty += ScoreLine(modules, line * size, 1, size);
            penalty += ScoreLine(modules, line, size, size);
        }
        for (int row = 0; row < size - 1; row++)
            for (int column = 0; column < size - 1; column++)
            {
                int index = row * size + column;
                bool color = modules[index];
                if (modules[index + 1] == color && modules[index + size] == color && modules[index + size + 1] == color)
                    penalty += 3;
            }
        foreach (bool value in modules) if (value) dark++;
        return penalty + Math.Abs(dark * 20 - modules.Length * 10) / modules.Length * 10;
    }

    private static int ScoreLine(ReadOnlySpan<bool> modules, int start, int step, int count)
    {
        int penalty = 0, runLength = 0, window = 0;
        bool previous = modules[start];
        for (int offset = 0; offset < count; offset++)
        {
            bool value = modules[start + offset * step];
            if (value == previous) runLength++;
            else
            {
                if (runLength >= 5) penalty += runLength - 2;
                previous = value;
                runLength = 1;
            }
            window = ((window << 1) | (value ? 1 : 0)) & 0x7FF;
            if (offset >= 10 && (window == 0b10111010000 || window == 0b00001011101)) penalty += 40;
        }
        return penalty + (runLength >= 5 ? runLength - 2 : 0);
    }
}
