param(
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [switch]$SkipChecks
)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Push-Location $PSScriptRoot
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw '.NET 10 SDK が必要です（ビルド用 PC のみ）。配布版を利用する PC へのインストールは不要です。'
    }
    if (-not $SkipChecks) {
        dotnet run --project tests/Evikit.Checks -c Release -- ../examples/reference
        if ($LASTEXITCODE -ne 0) { throw 'Core checks failed.' }
    }
    $target = Join-Path $PSScriptRoot "artifacts/evikit-$Runtime"
    dotnet publish src/Evikit.Windows -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -p:DebugType=None -o $target
    if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
    Copy-Item README.md, THIRD-PARTY-NOTICES.md, WINDOWS-ACCEPTANCE.md, QA.md -Destination $target
    Copy-Item licenses -Destination $target -Recurse -Force
    if (-not (Test-Path (Join-Path $target "sample-project"))) { Copy-Item examples/demo -Destination (Join-Path $target "sample-project") -Recurse }
    if (-not (Test-Path (Join-Path $target 'evikit.exe'))) { throw 'evikit.exe missing.' }
    $zip = Join-Path $PSScriptRoot "artifacts/evikit-$Runtime.zip"
    Compress-Archive -Path $target -DestinationPath $zip -Force
    Get-FileHash $zip -Algorithm SHA256 | Format-List
    Write-Host "Ready: $zip"
} finally { Pop-Location }
