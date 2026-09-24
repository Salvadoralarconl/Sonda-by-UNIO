param([string]$ResultsDirectory = 'artifacts/phase6/foundation-regression')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$accepted = Get-Content -LiteralPath (Join-Path $root 'artifacts/phase5/test-summary.json') -Raw | ConvertFrom-Json
$baseline = Get-Content -LiteralPath (Join-Path $root 'docs/reference/phase-5-code-baseline.json') -Raw | ConvertFrom-Json
$phase5 = Get-Content -LiteralPath (Join-Path $root 'artifacts/phase5/source-inventory.json') -Raw | ConvertFrom-Json
$unchanged = @($baseline.files) + @($phase5 | Where-Object path -Like 'tests/*')
$changed = @($unchanged | Where-Object { (Get-FileHash -LiteralPath (Join-Path $root $_.path)).Hash -ne $_.sha256 })
if ($changed.Count) { throw "Accepted core/test files changed: $($changed.path -join ', ')" }
$cases = @(foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root $ResultsDirectory) -Filter '*.trx') {
    [xml]$document = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($result in $document.TestRun.Results.UnitTestResult) {
        [pscustomobject]@{ name = $result.testName; outcome = $result.outcome; trx = $file.Name }
    }
})
$missing = @($accepted.cases | Where-Object { $name = $_.name; @($cases | Where-Object { $_.name -eq $name -and $_.outcome -eq 'Passed' }).Count -ne 1 })
if ($missing.Count -or @($cases | Where-Object outcome -ne 'Passed').Count) { throw 'Accepted regression missing, duplicate or failed; inspect TRX.' }
$report = [ordered]@{ total = $cases.Count; passed = $cases.Count; accepted382Passed = $accepted.cases.Count; unchangedCoreAndTestFiles = $unchanged.Count; changed = $changed; cases = $cases }
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $root 'artifacts/phase6/regression-summary.json')
[pscustomobject]$report | Select-Object total,passed,accepted382Passed,unchangedCoreAndTestFiles
