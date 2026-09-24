param([string]$ResultsDirectory = 'artifacts/phase5/final-tests')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$baseline = Get-Content -LiteralPath (Join-Path $root 'docs/reference/phase-5-code-baseline.json') -Raw | ConvertFrom-Json
$changed = @($baseline.files | Where-Object { (Get-FileHash -LiteralPath (Join-Path $root $_.path) -Algorithm SHA256).Hash -ne $_.sha256 })
if ($changed.Count) { throw "Accepted source changed: $($changed.path -join ', ')" }
$original = Get-Content -LiteralPath (Join-Path $root 'artifacts/phase4/accepted-source-hashes.json') -Raw | ConvertFrom-Json
$legacyNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $original | Where-Object { $_.Path -like 'tests*' }) {
    $source = [IO.File]::ReadAllText((Join-Path $root $entry.Path))
    foreach ($match in [regex]::Matches($source, '\bpublic\s+(?:async\s+)?(?:Task(?:<[^>]+>)?|void)\s+(\w+)\s*\(')) { [void]$legacyNames.Add($match.Groups[1].Value) }
}
$cases = @(foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root $ResultsDirectory) -Filter '*.trx') {
    [xml]$doc = Get-Content -LiteralPath $file.FullName -Raw
    $methods = @{}
    foreach ($test in $doc.TestRun.TestDefinitions.UnitTest) { $methods[$test.id] = $test.TestMethod }
    foreach ($result in $doc.TestRun.Results.UnitTestResult) {
        $method = $methods[$result.testId]
        $accepted = $method.className -match '^Sonda\.(Phase1|Phase3|Acquisition|Persistence)\.Tests\.'
        [pscustomobject]@{ name=$result.testName; method=$method.name; class=$method.className; outcome=$result.outcome; accepted309=$accepted; original237=($accepted -and $legacyNames.Contains($method.name)); trx=$file.Name }
    }
})
$summary = [ordered]@{
    total=$cases.Count; passed=@($cases | Where-Object outcome -eq 'Passed').Count
    accepted309Passed=@($cases | Where-Object { $_.accepted309 -and $_.outcome -eq 'Passed' }).Count
    original237Passed=@($cases | Where-Object { $_.original237 -and $_.outcome -eq 'Passed' }).Count
    newPhase5=@($cases | Where-Object { -not $_.accepted309 }).Count
    unchangedBaselineFiles=$baseline.files.Count
    cases=$cases
}
if ($summary.total -ne $summary.passed -or $summary.accepted309Passed -ne 309 -or $summary.original237Passed -ne 237) { throw 'Regression or acceptance gate failed; inspect TRX.' }
$summary | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $root 'artifacts/phase5/test-summary.json')
@{ compared=$baseline.files.Count; changed=$changed; originalCoreMigrationChain=@('20260924023552_DurablePersistence','20260924032911_InterpretationPolicies','20260924044645_FileAcquisition') } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'artifacts/phase5/core-integrity.json')
[pscustomobject]$summary | Select-Object total,passed,accepted309Passed,original237Passed,newPhase5,unchangedBaselineFiles
