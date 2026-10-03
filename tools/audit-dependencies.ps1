$ErrorActionPreference = 'Stop'
$projects = Get-ChildItem -Recurse -Filter *.csproj | Where-Object { $_.FullName -notmatch '[\\/]obj[\\/]' }
foreach ($project in $projects) {
    [xml]$xml = Get-Content $project.FullName
    if ($xml.SelectNodes('//PackageReference').Count -ne 0) { throw "PackageReference found in $($project.Name)" }
}
$assets = Get-ChildItem -Recurse -Filter project.assets.json
if ($assets.Count -eq 0) { throw 'No restored project assets found; dependency audit did not execute.' }
foreach ($asset in $assets) {
    $data = Get-Content -Raw $asset.FullName | ConvertFrom-Json
    foreach ($library in $data.libraries.PSObject.Properties) {
        if ($library.Value.type -eq 'package') { throw "NuGet dependency: $($library.Name)" }
    }
}
$runtimes = Get-ChildItem src/SpecQR/bin/Release -Recurse -Filter SpecQR.deps.json
if ($runtimes.Count -eq 0) { throw 'No library dependency manifest found.' }
foreach ($runtime in $runtimes) {
    $data = Get-Content -Raw $runtime.FullName | ConvertFrom-Json
    foreach ($library in $data.libraries.PSObject.Properties) {
        if ($library.Value.type -eq 'package') { throw "Runtime package dependency: $($library.Name)" }
    }
}
Write-Output "PASS dependency audit: $($projects.Count) projects, $($assets.Count) restore manifests, $($runtimes.Count) runtime manifests; zero packages."
