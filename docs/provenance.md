# 実装の出典と依存関係

SpecQR-CSharp は SpecQR の公開契約を基に、このリポジトリで新しく記述した C# 実装です。QR encoder、Reed–Solomon、matrix、mask、PNG の処理を第三者の QR runtime に委譲しません。

2026-10-03 に実際の remote HEAD と source を確認した基準:

| 基準 | コミット | 役割 |
| --- | --- | --- |
| [SpecQR JavaScript](https://github.com/SpecQR/SpecQR/tree/15ad15e5c770ea0e39072f8f88b2733018f02ffd) | `15ad15e5c770ea0e39072f8f88b2733018f02ffd` | 公開 API、符号化契約、比較 oracle。package version `3.0.0-rc.2` |
| [SpecQR Swift](https://github.com/SpecQR/SpecQR-Swift/tree/0ef9613fe8f1ecd687da76ce797b4ef896afa477) | `0ef9613fe8f1ecd687da76ce797b4ef896afa477` | 移植時の意味、安全性修正、portable rendering、SA 契約 |
| [Conformance Lab](https://github.com/SpecQR/SpecQR-Conformance-Lab/tree/72ad78c979327e3e261526ea3c0a164efa4ab390) | `72ad78c979327e3e261526ea3c0a164efa4ab390` | vectors、検証範囲と non-claim の参考 |

これらは同じ SpecQR 所有の MIT プロジェクトです。著作権表記を LICENSE に保持します。QR の容量・block layout は Model 2 の定数表です。有料仕様書の本文は含めません。第三者 encoder の runtime source は取り込みません。

Kanji は QR 範囲に限定した決定的な Unicode–Shift_JIS 対応表を使用します。基準 JS の WHATWG decode と first-hit policy に沿い、実行時の OS codec/codepage や NuGet package に依存しません。

## .NET とライブラリ依存

対象は `net8.0` と `net10.0`、言語は C# 12 です。前者は既存の .NET 8 利用者、後者は .NET 10 の継続利用を想定します。.NET Framework、Unity、Mono、NativeAOT、ブラウザー/WASM は検証対象ではありません。.NET 8 は Microsoft のサポート期限に留意して選択してください。

Microsoft の標準ランタイム・SDK・BCL のみを使用し、`PackageReference` はありません。`NuGet.Config` は package source を空にし、外部 NuGet 依存が入り込まないようにしています。両対象の targeting pack を持つ SDK 8/10 をインストールしてください。PNG は BCL の zlib と独自 chunk/CRC 処理を使います。`System.Drawing` や OS 専用画像ライブラリは使用しません。

開発環境には Microsoft 公式 dotnet-install から SDK 8.0.425 / 10.0.401、runtime 8.0.31 / 10.0.12 を隔離導入しました。合計約 1.2 GiB。これらの SDK、展開物、マシン固有ログはリポジトリに含めません。

## 開発時の独立 oracle

外部参照 encoder/decoder はテストにだけ使用し、ライブラリの project reference、runtime dependency、出荷ソースには含めません。実際に使用したバージョン・件数・制限は [検証記録](verification.md) を参照してください。基準 SpecQR との一致は同系統の比較であり、独立 decoder 検証とは区別します。

この公開は GitHub source repository の公開です。NuGet registry、stable tag、release、JS の stable channel の変更は行いません。
