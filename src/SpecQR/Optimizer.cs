using System.Text;

namespace SpecQR;

internal static class SegmentOptimizer
{
    private readonly record struct State(QRMode Mode, int Remainder, int Bits, int SegmentCount, int Previous)
    {
        internal bool Precedes(State other) => Bits < other.Bits || Bits == other.Bits && SegmentCount < other.SegmentCount;
    }

    internal static QRSegment Segment(string text, QRMode mode) => mode switch
    {
        QRMode.Numeric => QRSegment.Numeric(text),
        QRMode.Alphanumeric => QRSegment.Alphanumeric(text),
        QRMode.Kanji => QRSegment.Kanji(text),
        _ => QRSegment.Byte(text)
    };

    internal static QRSegment[] Single(string text, QRMode requested, bool allowKanji)
    {
        var selected = requested;
        if (selected == QRMode.Auto)
        {
            if (text.Length != 0 && text.All(c => c is >= '0' and <= '9')) selected = QRMode.Numeric;
            else if (text.Length != 0 && text.All(c => SegmentEncoder.AlphaValue(c) >= 0)) selected = QRMode.Alphanumeric;
            else if (allowKanji && text.Length != 0 && text.EnumerateRunes().All(r => KanjiMap.Value(r).HasValue)) selected = QRMode.Kanji;
            else selected = QRMode.Byte;
        }
        return [Segment(text, selected)];
    }

    /// <summary>Exact packing-cost dynamic programming with at most seven states per Unicode scalar.</summary>
    internal static QRSegment[] Optimized(string text, int version, bool allowKanji)
    {
        var scalars = text.EnumerateRunes().ToArray();
        if (scalars.Length == 0) return [QRSegment.Byte("")];
        QRMode[] modes = allowKanji ? [QRMode.Numeric, QRMode.Alphanumeric, QRMode.Kanji, QRMode.Byte] : [QRMode.Numeric, QRMode.Alphanumeric, QRMode.Byte];
        var layers = new State[scalars.Length + 1][];
        layers[0] = [new(QRMode.Auto, 0, 0, 0, 0)];
        for (var i = 0; i < scalars.Length; i++)
        {
            var rune = scalars[i];
            var numeric = rune.Value is >= 48 and <= 57;
            var alpha = SegmentEncoder.AlphaValue(rune.Value) >= 0;
            var kanji = allowKanji && KanjiMap.Value(rune).HasValue;
            var next = new List<State>(7);
            for (var previous = 0; previous < layers[i].Length; previous++)
            {
                var state = layers[i][previous];
                foreach (var mode in modes)
                {
                    if (mode == QRMode.Numeric && !numeric || mode == QRMode.Alphanumeric && !alpha || mode == QRMode.Kanji && !kanji) continue;
                    var same = state.Mode == mode;
                    var remainder = same ? state.Remainder : 0;
                    var extra = mode switch
                    {
                        QRMode.Numeric => remainder == 0 ? 4 : 3,
                        QRMode.Alphanumeric => remainder == 0 ? 6 : 5,
                        QRMode.Kanji => 13,
                        _ => rune.Utf8SequenceLength * 8
                    };
                    var nextRemainder = mode == QRMode.Numeric ? (remainder + 1) % 3 : mode == QRMode.Alphanumeric ? (remainder + 1) % 2 : 0;
                    var candidate = new State(mode, nextRemainder,
                        state.Bits + extra + (same ? 0 : 4 + QRTables.CountBits(mode, version)),
                        state.SegmentCount + (same ? 0 : 1), previous);
                    var index = next.FindIndex(s => s.Mode == mode && s.Remainder == nextRemainder);
                    if (index < 0) next.Add(candidate);
                    else if (candidate.Precedes(next[index])) next[index] = candidate;
                }
            }
            layers[i + 1] = next.ToArray();
        }
        var best = 0;
        for (var i = 1; i < layers[^1].Length; i++) if (layers[^1][i].Precedes(layers[^1][best])) best = i;
        var assignments = new QRMode[scalars.Length];
        for (var i = scalars.Length; i > 0; i--)
        {
            var state = layers[i][best];
            assignments[i - 1] = state.Mode; best = state.Previous;
        }
        var result = new List<QRSegment>();
        var start = 0;
        var startUtf16 = 0;
        var endUtf16 = 0;
        for (var end = 1; end <= scalars.Length; end++)
        {
            endUtf16 += scalars[end - 1].Utf16SequenceLength;
            if (end < scalars.Length && assignments[end] == assignments[start]) continue;
            result.Add(Segment(text[startUtf16..endUtf16], assignments[start]));
            start = end; startUtf16 = endUtf16;
        }
        return result.ToArray();
    }
}
