$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$statusPath = '.tools/local-preview-status.json'
if (Test-Path $statusPath) {
    $previous = Get-Content $statusPath -Raw | ConvertFrom-Json
    if ($previous.state -eq 'ready' -and (Get-Process -Id $previous.hostPid -ErrorAction SilentlyContinue)) {
        Write-Output "A preview is already running at $($previous.url). Close its Chrome window before starting another."
        exit
    }
}
$env:DOTNET_ROOT = Join-Path (Get-Location) '.tools/dotnet'
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.tools/cli-home'
$env:NUGET_PACKAGES = Join-Path (Get-Location) '.tools/packages'
$testSecret = (Get-Content -LiteralPath '.tools/pg-password' -Raw).Trim()
$previousConnection = $env:SONDA_TEST_ADMIN
try {
    $env:SONDA_TEST_ADMIN = "Host=127.0.0.1;Port=55432;Database=postgres;Username=sonda_owner;Password=$testSecret"
    $preview = Start-Process -FilePath (Join-Path $env:DOTNET_ROOT 'dotnet.exe') -ArgumentList 'tools/Sonda.LocalPreview/bin/Release/net10.0/Sonda.LocalPreview.dll' -WorkingDirectory (Get-Location).Path -WindowStyle Hidden -RedirectStandardOutput '.tools/local-preview.stdout.log' -RedirectStandardError '.tools/local-preview.stderr.log' -PassThru
    Write-Output "Local preview starting (process $($preview.Id)). Chrome will open when the sample database is ready."
} finally { $env:SONDA_TEST_ADMIN = $previousConnection }
