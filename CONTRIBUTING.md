# Contributing

変更時は日本語を中心に、変更理由・影響・実行した検証を記録してください。既存の公開されている挙動と安全性に対する差分を明示します。

.NET SDK 8 と 10 をインストールし、リポジトリのルートで次を実行します。ライブラリ、CLI、テストに第三者パッケージは必要ありません。

```sh
dotnet restore SpecQR.sln --configfile NuGet.Config
dotnet build SpecQR.sln -c Release --no-restore
dotnet run --project tests/SpecQR.Tests -c Release -f net8.0 --no-build -- --expected-framework net8.0
dotnet run --project tests/SpecQR.Tests -c Release -f net10.0 --no-build -- --expected-framework net10.0
dotnet run --project examples/Basic -c Release -f net10.0 --no-build
```

外部 oracle の再生成・検証は開発時だけに行います。インストール先とバージョンを隔離し、実行時のソースやプロジェクトに第三者の QR コード実装を入れないでください。ソースと fixture の識別情報を固定し、スキップした検証は理由を記録します。GitHub workflow は検証だけを行います。公開レジストリへの push、タグ・Release の作成、デプロイは含みません。
