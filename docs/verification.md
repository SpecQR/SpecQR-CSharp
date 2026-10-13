# 検証結果

2026-10-03 のローカル実行は macOS arm64、Microsoft SDK 10.0.401、実ランタイム .NET 8.0.31 / 10.0.12 です。未実行・スキップを成功として数えません。GitHub 上の各 OS の結果は [Verify workflow](https://github.com/SpecQR/SpecQR-CSharp/actions/workflows/verify.yml) で、対象コミットとジョブの判定を確認してください。

## 実行済みのローカル検証

| 検証 | 結果・範囲 |
| --- | --- |
| Release 構成のソリューションビルド | net8.0 / net10.0、ともに警告 0・エラー 0。ライブラリ・CLI・テスト・Basic 利用側プログラム・レビュアー用プログラムをビルド |
| 統合ハーネス | 各ランタイム **5,980 件成功、スキップ 0**。対象フレームワーク・実ランタイム・テストアセンブリ・ライブラリの SHA を出力 |
| 完全行列・符号語 | 1,588 件。全 V1–40×ECC4×mask8 を含む。データコードワード・インターリーブ後のコードワード・全モジュールを比較 |
| 独立参照エンコーダー | 上記の固定条件 960 件は Nayuki 1.8.0 とも一致。GS1/Kanji/SA/自動セグメント化について Nayuki との同等性は主張しない |
| 容量・計画 | 容量 640 + 最大容量直前/一致/直後 1,920 = 2,560 件 |
| GS1 | 1,644 件の検査。基準 fixture 1,411 件のうち 1,409 件は完全一致、2 件は dot-path の安全性修正による明示的な拒否。追加検査は 233 件 |
| Structured Append | 22 fixture の完全行列・分割情報 + 12 件のマージ・制御の回帰検査 = 34 件 |
| 不正入力・資源・変更分離・並列 | 133 件 |
| 移植可能な描画 | 18 件。PNG CRC/zlib/画素、SVG、stream / canvas など |
| fixture の整合性 | 3 fixture の固定 SHA を毎回検査 |
| 独立した ZXing-C++ 3.1.1 | **779 シンボル、既定 scale 8 の PNG 783 枚成功、スキップ 0**。全 68,118,976 画素と quiet zone も照合。FNC1 第 2 位置の全 152 indicator・608 シンボル、リテラルパーセント・区切りの 32 件、SA 12 シンボルを含む |
| 低水準の行列・RS | 各ランタイム 4,320 件の完全行列比較。全 V/ECC×zero/FF/random×8 固定マスク + 自動選択。全マスクのスコアとインターリーブ後のコードワードも一致 |
| GF/RS の独立比較 | 全 65,536 件の GF 積、生成多項式・剰余の次数 1–255、不正入力 62 件、並行処理 128 件。JavaScript の固定ソースと一致 |
| Kanji の全対応表 | 6,953 組 / 27,812 バイト。WHATWG による再生成・基準 JavaScript の全項目との照合が一致 |
| 独立した利用側プログラム・再現性 | net8.0/net10.0 ごとに独立したソースコピー 2 個をビルドし、DLL のハッシュが一致。利用側プログラムの実行時もフレームワークとランタイムを検査 |
| 依存監査 | 全 6 プロジェクトに PackageReference なし。復元後の assets と実行時 manifest に NuGet パッケージ依存なし |
| CLI/Basic | 両ランタイムで実行成功。CLI の不正引数、容量超過時の終了コード、PNG の全画素と SVG のエスケープも検証 |

証拠:

- [統合結果・source SHA](evidence/core-local.json)
- [ZXing-C++ の実行結果と識別情報](evidence/decoder-local.json)
- [依存監査](evidence/dependency-audit.json)
- [独立した利用側プログラム・再現ビルド](evidence/clean-consumer-local.json)
- [低水準比較のソース・ハーネスの SHA と結果](../tools/matrix-check/verified-results.json)
- [独立統合レビュー](integrated-review.md)
- [fixture manifest](../tests/SpecQR.Tests/Fixtures/manifest.json)

<a id="公開ciの必須lane"></a>

## 公開 CI の必須 lane

`Verify` はテストのみを実行し、パッケージ公開、タグ・Release の作成、デプロイを行いません。

- Windows / Linux / macOS × net8.0 / net10.0 の 6 検証枠: 厳格なビルド、正しいランタイムでの統合テスト、独立レビュアーの回帰テスト、Basic 利用側プログラム、依存監査。
- Ubuntu の独立検証枠: 固定 JavaScript との全行列・RS 比較、ハッシュを固定した ZXing-C++ デコーダー、ハッシュを固定した ZXing Java 3.5.4 の SA ヘッダー・復号・訂正検証、両ランタイムの独立した利用側プログラムと再現ビルド。

Java 17 環境は CI で用意します。ローカル Mac には JDK を追加しておらず、ローカルの Java デコーダーを実行済みとは記録しません。Java の実結果は同 workflow の independent ジョブと成果物を参照してください。Java は FNC1 第 2 位置の独立 oracle として使わず、その範囲は ZXing-C++ が担当します。

初回 CI では、V4-L・mask 0・`SPECQR / 12345 %` の既定描画（scale 8、margin 4）を ZXing Java が検出できませんでした。[失敗した公開実行](https://github.com/SpecQR/SpecQR-CSharp/actions/runs/37120592042)を保存しています。同一 PNG の全 107,584 画素と quiet zone の一致、および ZXing-C++ による正確な復号は検証済みです。Java 用の本検証 426 画像は scale 3 で生成し、別途、既定倍率の原画像と独立した Python 標準ライブラリによる対照画像の Java 検出結果を比較します。この診断は本検証の成功数に混ぜず、画像と結果を CI の成果物に残します。既定 PNG がすべてのデコーダーで検出されるという主張はしません。

CI は対象フレームワークを引数で渡し、ハーネスが実行したターゲットとランタイムを検査します。誤って net10 アセンブリを net8 として起動した負例対照は、非ゼロ終了コードで拒否されることを確認済みです。デコーダーもテスト専用プロトコルの識別情報を読み、古いアセンブリや別ターゲットのアセンブリを成功として扱いません。

## 再現方法と限界

[tests/README](../tests/README.md) と [CONTRIBUTING](../CONTRIBUTING.md) に基本手順があります。追加の低レベル比較:

```sh
python3 tools/matrix-check/run.py --baseline ../SpecQR-reference --dotnet dotnet
node tools/generate-kanji-map.mjs --check
python3 tools/verify-clean-consumer.py --framework net8.0
python3 tools/verify-clean-consumer.py --framework net10.0
```

基準 JavaScript は `15ad15e5c770ea0e39072f8f88b2733018f02ffd` の変更がない checkout を要求します。ソースと fixture は同系統の契約比較であり、独立したエンコーダー・デコーダーとは区別します。再現 DLL の比較は同じ SDK・フラグでの 2 ビルドです。異なる SDK パッチバージョンや OS にまたがる DLL ハッシュの完全一致は主張しません。

再現ビルドの PathMap は物理パスに正規化します。macOS の一時ディレクトリの別名をそのまま使うと、CodeView/PDB のパスが異なるため DLL ハッシュも異なります。正規化後は公開ソースを新規取得した環境でも両ランタイムの独立した 2 ビルドが一致し、利用側プログラムの実行が成功しています。

これらは合成した行列・画像によるソフトウェア検証です。実カメラ、印刷・損傷の全パターン、全スキャナーの SA 自動マージ、ISO/GS1 の正式認証、全 ECI 文字コード、対象外ランタイムの動作は主張しません。既知の意図的差分は [compatibility](compatibility.md)、資源上限は [resource safety](resource-safety.md) を参照してください。
