param([Parameter(Mandatory=$true)][string]$AdminConnection, [string]$PostgresBin)
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dotnetPath = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) { $dotnetPath = 'dotnet' }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools/packages'
$env:SONDA_TEST_ADMIN = $AdminConnection
if ($PostgresBin) { $env:SONDA_PG_BIN = $PostgresBin }
Push-Location $projectRoot
try {
    & $dotnetPath restore Sonda.sln --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed' }
    & $dotnetPath build Sonda.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    & $dotnetPath test tests/Sonda.Phase1.Tests -c Release --no-build --no-restore --logger 'trx;LogFileName=phase1.trx' --results-directory artifacts/test-results/review
    if ($LASTEXITCODE -ne 0) { throw 'Phase 1 regression' }
    & $dotnetPath test tests/Sonda.Persistence.Tests -c Release --no-build --no-restore --logger 'trx;LogFileName=phase2.trx' --results-directory artifacts/test-results/review
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL acceptance failed' }
} finally { Pop-Location; Remove-Item Env:SONDA_TEST_ADMIN -ErrorAction SilentlyContinue }
