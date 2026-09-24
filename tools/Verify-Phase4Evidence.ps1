param([string]$ResultsDirectory = 'artifacts/phase4/verified-tests')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$baseline = Get-Content -LiteralPath (Join-Path $root 'artifacts/phase4/accepted-source-hashes.json') -Raw | ConvertFrom-Json
$legacyNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $baseline | Where-Object { $_.Path -like 'tests*' }) {
    $source = [IO.File]::ReadAllText((Join-Path $root $entry.Path))
    foreach ($match in [regex]::Matches($source, '\bpublic\s+(?:async\s+)?(?:Task(?:<[^>]+>)?|void)\s+(\w+)\s*\(')) { [void]$legacyNames.Add($match.Groups[1].Value) }
}
$cases = foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root $ResultsDirectory) -Filter '*.trx') {
    [xml]$document = Get-Content -LiteralPath $file.FullName -Raw
    $methods = @{}
    foreach ($test in $document.TestRun.TestDefinitions.UnitTest) { $methods[$test.id] = $test.TestMethod }
    foreach ($result in $document.TestRun.Results.UnitTestResult) {
        $method = $methods[$result.testId]
        [pscustomobject]@{name=$result.testName;method=$method.name;class=$method.className;outcome=$result.outcome;legacy=$legacyNames.Contains($method.name);trx=$file.Name}
    }
}
$summary = [ordered]@{
    resultsDirectory = $ResultsDirectory
    total = @($cases).Count
    passed = @($cases | Where-Object outcome -eq 'Passed').Count
    failed = @($cases | Where-Object outcome -eq 'Failed').Count
    acceptedRegressionCases = @($cases | Where-Object legacy).Count
    acceptedRegressionPassed = @($cases | Where-Object { $_.legacy -and $_.outcome -eq 'Passed' }).Count
    postgres = @($cases | Where-Object { $_.class -like 'Sonda.Persistence.Tests.*' }).Count
    newPhase4Cases = @($cases | Where-Object { -not $_.legacy }).Count
    cases = $cases
}
if ($summary.total -ne 309 -or $summary.passed -ne 309 -or $summary.acceptedRegressionPassed -ne 237 -or $summary.postgres -ne 180) { throw "Incomplete/failed gate: $($summary | Select-Object total,passed,failed,acceptedRegressionPassed,postgres | ConvertTo-Json -Compress)" }
$summary | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $root 'artifacts/phase4/test-summary.json')
[pscustomobject]$summary | Select-Object total,passed,failed,acceptedRegressionPassed,postgres,newPhase4Cases

