# 適合性と回帰テスト

The test harness uses only .NET standard libraries; no NuGet test framework or QR runtime dependency is required. External encoders and decoders are development-only oracles.

## 通常のテスト

リポジトリのルートから実行します。対象フレームワークを明示し、実際に起動したランタイムとアセンブリの SHA-256 を結果に表示します。

```sh
dotnet build SpecQR.sln -c Release
dotnet run --project tests/SpecQR.Tests -c Release -f net8.0 --no-build -- --expected-framework net8.0
dotnet run --project tests/SpecQR.Tests -c Release -f net10.0 --no-build -- --expected-framework net10.0
```

通常のテストはオフラインで実行できます。各グループの成功・失敗と件数を JSON 行として出力し、失敗時は終了コード 1 を返します。独立デコーダーの実行は別コマンドであり、このハーネスの成功には含めません。

- `Fixtures/core.json.gz`: JavaScript の固定コミットから生成した 1,588 件の完全行列・データ符号語・インターリーブ後符号語。Version 1–40 × ECC 4 種 × mask 8 種の全組合せ、モード、ECI 境界、FNC1、混在文字列、最適化、ECC boost、固定 seed の入力を含みます。そのうち 960 件は Nayuki 1.8.0 の完全行列とも一致しています。
- 同ファイルの容量 640 件と見積もり 1,920 件: 全 Version × ECC × 4 データモードの最大容量の直前・一致・直後。
- `Fixtures/structured-append-differential.json`: テキスト、任意バイト列、手動セグメント、Unicode scalar 分割の完全行列と分割情報。
- `Fixtures/gs1-upstream.json`: 対応 AI カタログ、チェックデジット、要素文字列、Digital Link の固定比較データ。
- 追加テスト: 不正な値・制御セグメント順序・不正 Unicode・資源上限・防御的コピー・並行生成、PNG CRC/zlib/RGBA 画素、SVG XML エスケープ、stream 所有権、canvas adapter、Structured Append のマージ検証。

`Fixtures/manifest.json` にソースの固定コミットと各 fixture の SHA-256 を記録しています。テスト開始時に整合性も確認します。fixture の圧縮は保存容量を抑えるためで、比較はハッシュだけではなく展開後の全行列・全符号語に対して行います。

## 独立デコーダー

```sh
python3 tools/verify-decode.py --framework net10.0 --dependency-dir ../.tools/zxing-cpp --report decoder-report.json
```

このコマンドは `tools/zxing-cpp-requirements.txt` で SHA-256 固定された ZXing-C++ 3.1.1 の wheel を指定ディレクトリにインストールします。NumPy/Pillow は不要です。インストール済み依存だけを許可する場合は `--no-install` を付けます。対応 wheel がない環境や未ビルドのターゲットでは明示的に失敗し、未実行を成功として記録しません。

全 152 FNC1-second application indicator、手動制御と高水準オプションの両方、任意バイト、GS1/FNC1 の `%` と区切り文字、各データモード/ECC/mask、ECI 境界、Version 40、PNG 出力、Structured Append メンバーを検証します。ZXing-C++ が FNC1-second の application indicator をデータ先頭に返す仕様も含めて照合します。これは合成画像での復号検証であり、印刷物や実カメラの検証ではありません。

Java 17 以上を用意すると、独立した Structured Append メタデータ・誤り訂正検証も実行できます。

```sh
python3 tools/verify-zxing-java.py --framework net10.0 --report artifacts/zxing-java.json
```

公式 Maven 配布の ZXing Java 3.5.4 JAR を SHA-256 固定で取得します。全 135 通りの合法な SA header、7 組の実際の分割データ、426 行列と 426 PNG、3 符号語を損傷した 32 シンボル、不正行列 1 件を検証します。復号したヘッダーを使う順不同再構成、パリティ、完全な訂正後データ符号語も照合します。`--no-download` は既存 JAR のみを許可します。`--prepare-only` は生成の確認だけで、885 件の decode を明示的に未実行として記録します。

## fixture の再生成

```sh
git clone https://github.com/SpecQR/SpecQR ../SpecQR-reference
git -C ../SpecQR-reference checkout 15ad15e5c770ea0e39072f8f88b2733018f02ffd
npm install --prefix ../.tools/specqr-oracles --ignore-scripts --no-audit --no-fund nayuki-qr-code-generator@1.8.0
SPECQR_DEV_NODE_MODULES=../.tools/specqr-oracles node tools/generate-fixtures.mjs ../SpecQR-reference
```

`SPECQR_DEV_NODE_MODULES` は `node_modules` を含むディレクトリを指定します。再生成ツールは参照ソースのコミット、変更の有無、Nayuki バージョンを検査します。JavaScript/Swift/Lab リポジトリのライセンスは MIT です。参照実装や第三者 QR エンコーダーのソースを C# 実行ライブラリへ取り込むことはありません。

## 意図した差分

高水準 GS1/FNC1 API の文字列内 `%` は literal として保持します。自動選択では byte モードにし、危険な明示 alphanumeric は拒否します。低水準の明示 FNC1 制御セグメントでは標準の `%`/`%%` エスケープ規則が残ります。安全性 fixture の期待値は JavaScript を明示 byte モードで実行した結果です。

Digital Link のパスに `.` / `..` を AI 値として配置すると URL 正規化で失われるため、C# はその配置を拒否します。クエリに保持する明示設定は許可します。対応するテストは「上流と一致した」と集計せず、安全性の差分として検証します。
