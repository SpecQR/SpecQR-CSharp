# C# feature parity と完了条件

比較対象は [provenance](provenance.md) の 3 つの実コミットです。JavaScript のソース上の契約を調べ、Swift の安全性修正と Lab の検証範囲を反映しています。C# API 名やプラットフォームとの接続を含む言語固有の差分は [compatibility](compatibility.md) に明示します。

| 項目 | C# の提供機能 | 検証条件 |
| --- | --- | --- |
| QR Code Model 2 V1–40、L/M/Q/H | `Generate`, `QROptions`, `QRCapacity` | 全 V×ECC×mask の行列・コードワード |
| マスク 0–7、自動マスク、バージョン・範囲、ECC 引き上げ | `MaskPattern`, `Version`, `MinVersion`, `MaxVersion`, `BoostErrorCorrection` | 固定条件、自動選択のスコアと同点時の選択、範囲の境界 |
| Numeric / Alphanumeric / UTF-8 / バイナリ | `QRMode`, string/byte[] オーバーロード | ペイロード、文字数ビット、空・不正入力 |
| 決定的な QR Kanji | `QRMode.Kanji`, `QRSegment.Kanji` | 許可範囲・非対応文字・独立した復号検証 |
| 混合最適化、手動セグメント | `OptimizeSegments`, `GenerateSegments` | JavaScript との一致、モード混在、Unicode スカラー |
| ECI / FNC1 第 1・第 2 位置 / SA ヘッダー | `QRSegment` のファクトリ / `QROptions` | 境界値、順序、競合拒否、メタデータ |
| 計画・診断・容量 | `Estimate`, `AnalyzeSegments`, `GetCapacity`, `Planning`, `Diagnostics` | 容量不足を返す、行列未生成、容量上限に近い場合の警告 |
| 高水準 GS1 の生データ・パーセントの意味保持 | `Gs1` / `GS1.ParseElementString` | 生データの検証、リテラルパーセントの安全性 |
| GS1 の限定カタログ・解析・検証・チェックデジット | `GS1` の静的 API | カタログ、AI の長さ、区切り、GTIN/SSCC |
| GS1 Digital Link | 作成・解析・検証・正規化 | クエリ・パスの方針、不正エスケープ・重複・未知クエリ |
| SA のテキスト・バイナリ・手動分割 | `GenerateStructuredAppend`, `GenerateSegmentsStructuredAppend` | 2–16、貪欲法、バージョン選択、段落・バイト境界 |
| SA のパリティ・マージ・簡潔な診断 | パリティ補助機能、`MergeStructuredAppendParts` | 正準 UTF-8、欠落・重複拒否、summary/full |
| 行列・SVG・PNG・data URL・画素・canvas adapter | 生成結果の描画メソッド | 配置・寸法、CRC、独立した復号検証、移植可能な BCL |
| スキャン診断 | 描画・計画の診断 | コントラスト、quiet zone、印刷、容量 |
| CLI・実例 | `SpecQR.Cli`, `examples/Basic` | 実行、不正引数、独立した利用側プログラム |
| 日本語中心の文書、冒頭の英語説明、MIT | README/docs/LICENSE | 出典、既知の制限、依存なしの監査 |
| 並行処理・不変性・資源 | 全 API | 並行呼び出し、返却値変更、不正な enum・オーバーフロー |
| Windows/Linux/macOS、net8/net10 | GitHub Actions Verify | 各検証枠が対象フレームワークとランタイムを明示して実行 |
| GitHub 公開、リモートから取得して利用 | SpecQR/SpecQR-CSharp | 公開範囲・SHA の一致、新規 clone |

この表は範囲・受入条件です。実行済み件数、判定、CI のリンクは [verification](verification.md) に記録します。未実行・スキップを成功として数えません。

## 対象外

Micro QR、rMQR、デコーダー本体、GS1 の全カタログ認証、装飾モジュール、ロゴの重ね合わせ、System.Drawing、NuGet 公開、自動 Release・デプロイは対象外です。.NET Framework / Unity / Mono / Native AOT / WASM の動作は主張しません。Windows/Unix の描画に追加のネイティブパッケージは不要です。
