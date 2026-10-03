# 検証結果

2026-10-03 のローカル実行は macOS arm64、Microsoft SDK 10.0.401、実ランタイム .NET 8.0.31 / 10.0.12 です。未実行・skip を成功として数えません。GitHub 上の各 OS の結果は [Verify workflow](https://github.com/SpecQR/SpecQR-CSharp/actions/workflows/verify.yml) で、対象コミットと job conclusion を確認してください。

## 実行済みのローカル検証

| 検証 | 結果・範囲 |
| --- | --- |
| Release solution build | net8.0 / net10.0、ともに警告 0・エラー 0。library / CLI / tests / Basic consumer / reviewer をビルド |
| 統合ハーネス | 各 runtime **5,980 cases 成功、skip 0**。対象framework・実runtime・testassembly・library SHAを出力 |
| 完全行列・符号語 | 1,588 cases。全V1–40×ECC4×mask8を含む。data codewords / interleaved codewords / 全moduleを比較 |
| 独立参照 encoder | 上記の固定条件960casesはNayuki1.8.0とも一致。GS1/Kanji/SA/autosegmentationへのNayuki同等性は主張しない |
| 容量・planning | 容量640 + 最大容量直前/一致/直後1,920 = 2,560cases |
| GS1 | 1,644checks。基準fixture1,411のうち1,409は完全一致、2件はdot-path安全修正の明示拒否。追加233checks |
| Structured Append | 22fixtureの完全行列・分割情報 + 12merge/control回帰 = 34cases |
| 不正入力・資源・変更分離・並列 | 133cases |
| portable rendering | 18cases。PNG CRC/zlib/画素、SVG、stream/canvas等 |
| fixture integrity | 3fixtureの固定SHAを毎回検査 |
| 独立ZXing-C++3.1.1 | **779symbols、既定scale8のPNG783images成功、skip0**。全68,118,976画素とquietzoneも照合。FNC1second全152indicator・608symbols、literalpercent/separator32cases、SA12symbolsを含む |
| 低レベルmatrix/RS | 各runtime4,320exactmatrix cases。全V/ECC×zero/FF/random×8固定mask+auto。全maskscoreとinterleavedcodewordsも一致 |
| GF/RSの独立比較 | 全65,536GF積、generator/remainder degrees1–255、negative62、parallel128。JS固定sourceと一致 |
| 全Kanji mapping | 6,953pairs / 27,812bytes。WHATWG再生成・基準JS全entry照合一致 |
| clean consumer / 再現性 | net8.0/net10.0ごとに独立sourcecopy2個をbuildしDLLhash一致。consumer実行時もframework/runtimeを検査 |
| 依存監査 | 全6projectにPackageReferenceなし。restore assets / runtime manifestsにNuGet package依存なし |
| CLI/Basic | 両runtimeで実行成功。CLI不正引数、overflow終了code、PNG全画素とSVGescapingも検証 |

証拠:

- [統合結果・source SHA](evidence/core-local.json)
- [ZXing-C++ の実行結果とidentity](evidence/decoder-local.json)
- [依存監査](evidence/dependency-audit.json)
- [clean consumer / 再現build](evidence/clean-consumer-local.json)
- [低レベル比較のsource/harness SHA・結果](../tools/matrix-check/verified-results.json)
- [独立統合レビュー](integrated-review.md)
- [fixture manifest](../tests/SpecQR.Tests/Fixtures/manifest.json)

## 公開CIの必須lane

`Verify` は test のみを実行し、package publish、tag、release、deploymentを行いません。

- Windows / Linux / macOS × net8.0 / net10.0 の6lanes: strictbuild、正しいruntimeの統合test、独立reviewerの回帰test、Basic consumer、依存監査。
- Ubuntu独立lane: 固定JSとの全matrix/RS比較、hash固定ZXing-C++ decoder、hash固定ZXingJava3.5.4のSAheader/復号/correction検証、両runtimeのcleanconsumerと再現build。

Java17環境はCIで用意します。ローカルMacにはJDKを追加しておらず、ローカルJava decoderを実行済みとは記録しません。Javaの実結果は同workflowのindependentjobとartifactを参照してください。JavaはFNC1secondの独立oracleとして使わず、その範囲はZXing-C++が担当します。

初回CIでは、V4-L・mask0・`SPECQR / 12345 %` の既定描画（scale8、margin4）を ZXing Java が検出できませんでした。[失敗した公開実行](https://github.com/SpecQR/SpecQR-CSharp/actions/runs/37120592042)を保存しています。同一 PNG の全107,584画素とquiet zoneの一致、およびZXing-C++による正確な復号は検証済みです。Java用の本検証426画像はscale3で生成し、別途、既定倍率の原画像と独立したPython標準ライブラリによる対照画像のJava検出結果を比較します。この診断は本検証の成功数に混ぜず、画像と結果をCI artifactに残します。既定PNGがすべてのデコーダーで検出されるという主張はしません。

CIは対象frameworkを引数で渡し、ハーネスが実行したtarget/runtimeを検査します。誤ってnet10assemblyをnet8として起動したnegativecontrolはnonzeroで拒否されることを確認済みです。decoderもtest-only protocolのidentityを読み、古い/別targetのassemblyを成功として扱いません。

## 再現方法と限界

[tests/README](../tests/README.md) と [CONTRIBUTING](../CONTRIBUTING.md) に基本手順があります。追加の低レベル比較:

```sh
python3 tools/matrix-check/run.py --baseline ../SpecQR-reference --dotnet dotnet
node tools/generate-kanji-map.mjs --check
python3 tools/verify-clean-consumer.py --framework net8.0
python3 tools/verify-clean-consumer.py --framework net10.0
```

基準JSは `15ad15e5c770ea0e39072f8f88b2733018f02ffd` のclean checkoutを要求します。sourceとfixtureは同系統の契約比較であり、独立encoder/decoderとは区別します。再現DLLの比較は同じSDK・flagsでの2buildです。異なるSDK patchやOSにまたがるDLLhashの完全一致は主張しません。

再現buildのPathMapは物理パスに正規化します。macOSの一時ディレクトリの別名をそのまま使うと、CodeView/PDBのパスが異なるためDLLhashも異なります。正規化後は公開sourceを新規取得した環境でも両runtimeの独立2buildが一致し、consumer実行が成功しています。

これらはsyntheticmatrix/imageのソフトウェア検証です。physicalcamera、印刷・損傷の全パターン、全scannerのSA自動merge、ISO/GS1正式認証、全ECI charset、対象外runtimeの動作は主張しません。既知の意図的差分は [compatibility](compatibility.md)、資源上限は [resource safety](resource-safety.md) を参照してください。
