# 独立統合レビュー

Independent pre-publication review of the C# implementation. No unresolved blocking defect remains in the reviewed source snapshot. Executed evidence is distinguished from checks that the reviewer did not run.

2026-10-03、実装担当とは別のレビュアーが、ライブラリを読み、専用の依存なしテストを追加し、統合テストも実行しました。レビュアーは runtime source を直接変更していません。指摘は担当者が修正しました。

対象 snapshot の 21 個の source/configuration file の SHA-256 は [review-evidence.json](review-evidence.json) に記録しています。相対パスから作った集合識別子は `ffdf35d9e1ef59bde59566a77639f0dd8ce184e8e1c1cf11dacba0c489b85e62` です。記録スクリプトは検証前後の source hash が変わっていないことを確認します。ドキュメント変更だけではこの識別子は変わりません。

最終統合記録 [core-local.json](evidence/core-local.json) とライブラリの全 17 C# source hashes、両 framework の実行 assembly hashes が一致することも照合しました。

## 修正済みの指摘

1. **Digital Link の dot-segment による値の消失。** AI 10 の値 `..` を path に置くと、URL 正規化が AI/value を除去しました。`.` は不正な AI/value path を作りました。基準 JavaScript にも存在する問題を実際に再現しました。C# は選択された path 値が `.` / `..` の場合に拒否し、`PathAis = []` で query に置いた値は保持します。literal `%2e` は通常データとして保持します。2 個の該当 fixture は意図的な差分として明示しています。
2. **Structured Append 適合性テストの option 誤り。** テストが text/binary 入力にも manual 専用の Full split-unit detail を指定していたため、正しい runtime validation によって停止しました。Full は manual fixture のみに指定するよう修正され、最終の SA group は両対象で成功しました。

高レベル GS1/FNC1 の literal `%` を byte mode で保持する既存の安全性修正も確認しました。明示的な unsafe alphanumeric は拒否し、低レベル FNC1 segment の `%` / `%%` escaping は呼び出し側が管理します。

## このレビュアーが実行した検証

ホストは macOS Arm64、SDK は 10.0.401 です。対象 framework と実際の runtime major を照合し、故意に誤った framework を要求したときに失敗することも確認しました。

| 検証 | net8.0 / .NET 8.0.31 | net10.0 / .NET 10.0.12 |
| --- | --- | --- |
| Reviewer executable | 1,360 assertions、成功 | 1,360 assertions、成功 |
| Fixture integrity | 3 files、成功 | 3 files、成功 |
| Exact matrix / data / interleaved codewords | 1,588 cases、成功 | 1,588 cases、成功 |
| Capacity / planning boundaries | 2,560 cases、成功 | 2,560 cases、成功 |
| Negative / resource / mutation / concurrency | 133 checks、成功 | 133 checks、成功 |
| Portable rendering | 18 checks、成功 | 18 checks、成功 |
| Structured Append | 34 cases/checks、成功 | 34 cases/checks、成功 |
| GS1 | 1,644 cases/checks、成功 | 1,644 cases/checks、成功 |
| Wrong framework identity | 拒否を確認 | 拒否を確認 |

skip は両対象とも 0 です。GS1 group は 1,411 基準 fixture と追加検証を含みます。上の統合 group の件数は harness が報告する case/check 単位で、全内部 assertion 数を意味しません。

Reviewer executable は特に次を検証します。

- 80 件の決定的な混在 Unicode SA 入力を V1/2/4/9/10/26/27/40 と optimizer on/off で生成し、全 chunk の bit length を公開 `Estimate` と照合。次の scalar を一つ追加すると容量を超えることを検証し、最大 fitting prefix を確認。
- V1-L の homogeneous 境界: 35 digits、21 alphanumeric characters、15 bytes、9 Kanji scalars。分割数と各 chunk の長さを確認。
- 300-byte manual segment が V2-L で 30 bytes × 10 symbols に分割できること。分割前の 8-bit count-field 上限で誤って拒否しないこと。summary は unit list を保持せず、full と生成 codewords が一致すること。
- segment input/output、matrix の各 row、data/interleaved codewords の defensive copy、32 並行生成、FNC1 literal-percent の両形態と optimizer on/off。
- `.` / `..` path 拒否と query roundtrip。render geometry の整数境界、raster 上限、不正色/XML/DPI の拒否、異常 option で出力 stream に一切書かないこと、SVG title escaping、不正 SA header getter の安全性。

再実行:

```sh
python3 tools/review/run.py --dotnet dotnet --output artifacts/review-evidence.json
```

このスクリプトは package source を追加せず、両 framework を build して実際の executable を起動します。sandbox の compiler-server IPC に依存しないよう、build server と shared compilation を無効にしています。

## ソース確認の範囲

QR の tables、GF(256)/Reed–Solomon、interleaving、function/data module の配置、mask scoring と同点時の選択、segment/control 検証、count-width bands、planning preflight、GS1 catalog/URL adapter、SA tracker/merge、portable renderer を確認しました。共有 tables は公開されず、encoder 作業状態は呼び出しごとです。PNG は BCL zlib と独自 framing/CRC、SVG は限定した色形式と XML escaping を使用します。

`PackageReference`、第三者 QR runtime の呼び出し、`System.Drawing` 依存はライブラリ内にありません。依存なし・MIT・user-owned baseline と開発用 oracle の区別は [provenance](provenance.md) に記載されています。最終 dependency audit と外部 decoder の実行記録は [verification](verification.md) を参照してください。

## このレビューの限界

この reviewer は Windows/Linux の execution、GitHub CI、外部 decoder、物理カメラ/印刷試験を実行していません。これらを上表の pass に含めません。公開にあたっての実 CI、外部 oracle/decoder、clean consumer、公開 commit の一致は [verification](verification.md) に分けて記録します。

URL helper は bounded SpecQR contract であり、ブラウザ URL API 全体や GS1 全仕様の認証ではありません。低レベル FNC1 escaping、Unicode/ECI の責務、resource limits、対象外 platform はそれぞれの API 文書に従います。このレビューは検査した snapshot に対する結果であり、将来の変更や全入力に対する無欠陥の証明ではありません。
