# Contributing

変更時は日本語を中心に、変更理由・影響・実行した検証を記録してください。既存の public behavior と安全性の差分を明示します。

.NET SDK 8 と 10 をインストールし、リポジトリ root で次を実行します。ライブラリ、CLI、テストに第三者 package は必要ありません。

```sh
dotnet restore SpecQR.sln --configfile NuGet.Config
dotnet build SpecQR.sln -c Release --no-restore
dotnet run --project tests/SpecQR.Tests -c Release -f net8.0 --no-build -- --expected-framework net8.0
dotnet run --project tests/SpecQR.Tests -c Release -f net10.0 --no-build -- --expected-framework net10.0
dotnet run --project examples/Basic -c Release -f net10.0 --no-build
```

外部 oracle の再生成・検証は development-only です。インストール先と version を隔離し、runtime source / project に third-party QR code を入れないでください。sourceとfixtureのidentityを固定し、skipは理由を記録します。GitHub workflowは検証だけを行います。公開registryへのpush、tag/release、deploymentは含みません。
