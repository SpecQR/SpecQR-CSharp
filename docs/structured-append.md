# Structured Append

`QRCode.GenerateStructuredAppend(textOrBytes, options)` は 2〜16 symbols を生成します。index は公開 API では 1-based、QR の header は 0-based です。全 symbols で version と ECC は共通です。自動 version は最少 symbols 数より、分割可能な最小 version を優先します。各 chunk は最大 fitting prefix を選び、text を Unicode scalar の途中で分割しません。

```csharp
var set = QRCode.GenerateStructuredAppend(new string('A', 60),
    new QRStructuredAppendOptions {
        QrOptions = new QROptions { Version = 1, ErrorCorrectionLevel = ErrorCorrectionLevel.L },
        MaxSymbols = 16
    });
foreach (var symbol in set.Symbols) Console.WriteLine(symbol.ToSvg());
```

header を含めて 1 symbol に収まる入力は拒否します。通常は `Generate` を使用してください。ECI、GS1/FNC1、ECC boost と high-level SA は組み合わせません。low-level header は `QROptions.StructuredAppend` または先頭の `QRSegment.StructuredAppend` で明示できます。

`GenerateSegmentsStructuredAppend` は numeric/alphanumeric/Kanji の segment 境界を保持し、byte text は scalar、binary は byte で分割します。非 byte segment 自体が一つの symbol に収まらない場合は、利用者がその segment を分割します。空・control segment は high-level manual SA に渡せません。

`CalculateStructuredAppendParity` は元の text の UTF-8 / binary の raw bytes の XOR です。`CalculateStructuredAppendSegmentsParity` の Kanji も UTF-8 で、Shift_JIS encoded bits ではありません。

`MergeStructuredAppendParts` は decoder が返した index、total、parity と payload を検査します。欠落・重複・type mismatch・parity mismatch を拒否し、正しい順番に結合します。画像の読み取りや scanner の metadata 対応を追加する API ではありません。XOR parity は integrity/authentication の代わりにはなりません。

診断は standard で split-unit count と symbol ranges のみ保持します。`SplitUnits = QRStructuredAppendSplitUnitsDetail.Full` で unit ごとの情報を要求できます。full は manual segment input のみ有効です。既定では入力 byte ごとの診断 object を作りません。

資源保護として text / canonical manual message と decoded merge 全体は 1,000,000 bytes 以下、manual segment は 32,768 以下です。実際の QR 容量はこれより小さく、bit capacity を先に検査します。分割探索は bounded range と線形 text bit tracker を使用します。
