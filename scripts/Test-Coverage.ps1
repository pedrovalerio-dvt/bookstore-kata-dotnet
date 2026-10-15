$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
dotnet test (Join-Path $root 'Bookstore.slnx') --configuration Release --nologo --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    throw "Tests or the 70% total line-coverage gate failed (exit $LASTEXITCODE)."
}
$reportPath = Join-Path $root 'artifacts\coverage\coverage.cobertura.xml'
[xml]$report = Get-Content $reportPath -Raw
$core = @($report.coverage.packages.package | Where-Object { $_.name -eq 'Bookstore.Core' })
if ($core.Count -ne 1) {
    throw 'The business-logic assembly is missing from the coverage report.'
}
$culture = [System.Globalization.CultureInfo]::InvariantCulture
$corePercent = [double]::Parse($core[0].'line-rate', $culture) * 100
$totalPercent = [double]::Parse($report.coverage.'line-rate', $culture) * 100
if ($corePercent -lt 85) {
    throw "Business-logic coverage is $corePercent%; the repository guideline requires 85%."
}
Write-Output ("Coverage gates passed: total {0:F2}% (minimum 70%), core {1:F2}% (minimum 85%)." -f $totalPercent, $corePercent)