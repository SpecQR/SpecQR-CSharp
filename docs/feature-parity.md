# C# feature parity と完了条件

比較対象は [provenance](provenance.md) の 3 つの実コミットです。JS の source contract を調べ、Swift の安全性修正と Lab の coverage boundaries を反映しています。C# API 名や platform adapters を含む言語固有差分は [compatibility](compatibility.md) に明示します。

| 項目 | C# surface | 検証条件 |
| --- | --- | --- |
| QR Code Model 2 V1–40、L/M/Q/H | `Generate`, `QROptions`, `QRCapacity` | 全 V×ECC×mask の matrix/codewords |
| mask0–7、自動mask、version/range、ECC boost | `MaskPattern`, `Version`, `MinVersion`, `MaxVersion`, `BoostErrorCorrection` | 固定条件、auto score/tie、range境界 |
| Numeric / alphanumeric / UTF-8 / binary | `QRMode`, string/byte[] overloads | payload、count bits、空・不正入力 |
| deterministic QR Kanji | `QRMode.Kanji`, `QRSegment.Kanji` | 許可範囲・非対応文字・独立decode |
| mixed最適化、manual segments | `OptimizeSegments`, `GenerateSegments` | JS一致、mode混在、Unicode scalar |
| ECI / FNC1 first/second / SA header | `QRSegment` factories / `QROptions` | 境界値、順序、競合拒否、metadata |
| planning / diagnostics / capacity | `Estimate`, `AnalyzeSegments`, `GetCapacity`, `Planning`, `Diagnostics` | overflowを返す、matrix未生成、near-limit警告 |
| high-level GS1 raw・percent意味保持 | `Gs1` / `GS1.ParseElementString` | raw validator、literalpercent安全性 |
| GS1 boundedcatalog/parser/validator/checkdigits | `GS1` static API | catalog、AI lengths、separator、GTIN/SSCC |
| GS1 Digital Link | create/parse/validate/normalize | query/path policy、不正escape・重複・未知query |
| SA text/binary/manual split | `GenerateStructuredAppend`, `GenerateSegmentsStructuredAppend` | 2–16、greedy、version選択、段落/byte境界 |
| SA parity/merge/compactdiagnostics | parity helpers, `MergeStructuredAppendParts` | canonicalUTF8、欠落・重複拒否、summary/full |
| matrix/SVG/PNG/dataURL/pixels/canvasadapter | result renderer methods | geometry・CRC・独立decode・portable BCL |
| scan diagnostics | render/plan diagnostics | contrast・quietzone・print・capacity |
| CLI/examples | `SpecQR.Cli`, `examples/Basic` | 実行、badargs、独立consumer |
| 日本語中心docs、Englishopening、MIT | README/docs/LICENSE | provenance、knownlimits、依存なし監査 |
| concurrency/immutability/resources | 全API | concurrent calls、返却値変更、不正enum/overflow |
| Windows/Linux/macOS、net8/net10 | GitHub Actions Verify | 各laneが対象framework/runtimeを明示して実行 |
| GitHub公開、remoteconsumer | SpecQR/SpecQR-CSharp | publicvisibility・SHA一致、freshclone |

この表は範囲・受入条件です。実行済み件数、判定、CI link は [verification](verification.md) に記録します。未実行・skip を pass として数えません。

## 対象外

Micro QR、rMQR、decoder本体、全GS1catalog認証、装飾module、logooverlay、System.Drawing、NuGet公開、自動release/deploy。.NET Framework / Unity / Mono / NativeAOT / WASM の動作は主張しません。Windows/Unixの描画に追加native packageは不要です。
