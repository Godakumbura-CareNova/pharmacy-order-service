[CmdletBinding()]
param(
    [string]$PolicyPath,
    [string]$OutputRoot,
    [switch]$OpenReport
)

$ErrorActionPreference = 'Stop'
$previousLocation = Get-Location
try {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
    Set-Location $repoRoot
    if (-not $PolicyPath) { $PolicyPath = Join-Path $PSScriptRoot 'quality-policy.json' }
    if (-not $OutputRoot) { $OutputRoot = Join-Path $PSScriptRoot 'results' }
    $policy = Get-Content -LiteralPath $PolicyPath -Raw | ConvertFrom-Json
    if ($policy.analysisLevel -ne '8.0-recommended' -or $null -eq $policy.maxAnalyzerWarnings -or
        $policy.maxAnalyzerWarnings -lt 0 -or $null -eq $policy.minLineCoveragePercent -or
        $policy.minLineCoveragePercent -lt 0 -or $policy.minLineCoveragePercent -gt 100 -or
        $null -eq $policy.minBranchCoveragePercent -or $policy.minBranchCoveragePercent -lt 0 -or $policy.minBranchCoveragePercent -gt 100) {
        throw 'Invalid quality policy. Use the documented ruleset and valid numeric thresholds.'
    }
    $runName = '{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ([guid]::NewGuid().ToString('N').Substring(0, 6))
    $runDirectory = (New-Item -ItemType Directory -Path (Join-Path $OutputRoot $runName) -Force).FullName
    $sarifPath = Join-Path $runDirectory 'analysis.sarif'
    $reportPath = Join-Path $runDirectory 'coverage-report'
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $sdk = (& dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'The .NET SDK is not available.' }

    Write-Host 'Restoring the pinned local ReportGenerator tool...'
    & dotnet tool restore --tool-manifest (Join-Path $repoRoot 'dotnet-tools.json') 2>&1 |
        Tee-Object -FilePath (Join-Path $runDirectory 'tool-restore.log') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Local tool restore failed. Check SDK/runtime installation and NuGet access.' }

    Write-Host 'Running static analysis (Release configuration)...'
    & dotnet build 'src/PharmacyOrder.Api/PharmacyOrder.Api.csproj' --configuration Release --no-incremental `
        '-p:EnableNETAnalyzers=true' "-p:AnalysisLevel=$($policy.analysisLevel)" "-p:ErrorLog=$sarifPath%2Cversion=2.1" 2>&1 |
        Tee-Object -FilePath (Join-Path $runDirectory 'analysis-build.log') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Analysis build failed. See analysis-build.log; do not treat this as a clean analysis result.' }
    if (-not (Test-Path -LiteralPath $sarifPath)) { throw 'The compiler did not produce the required SARIF report.' }
    $sarif = Get-Content -LiteralPath $sarifPath -Raw | ConvertFrom-Json
    $findings = @($sarif.runs | ForEach-Object { $_.results } | Where-Object { $null -ne $_ })
    $activeFindings = @($findings | Where-Object { @($_.suppressions | Where-Object { $_.status -eq 'accepted' }).Count -eq 0 })
    $warnings = @($activeFindings | Where-Object { $_.level -eq 'warning' -and $_.ruleId -match '^CA\d+$' })
    $errors = @($activeFindings | Where-Object { $_.level -eq 'error' })
    $rows = @($warnings | ForEach-Object {
        $location = @($_.locations)[0].physicalLocation
        [pscustomobject]@{
            Rule = $_.ruleId; File = $location.artifactLocation.uri
            Line = $location.region.startLine; Message = $_.message.text
        }
    })
    $rows | Export-Csv -LiteralPath (Join-Path $runDirectory 'analyzer-findings.csv') -NoTypeInformation -Encoding UTF8
    $rules = @($rows | Group-Object Rule | Sort-Object Count -Descending | Select-Object Name, Count)

    Write-Host 'Running API tests and collecting scoped coverage...'
    & dotnet test 'tests/PharmacyOrder.Api.Tests/PharmacyOrder.Api.Tests.csproj' --configuration Release `
        '--collect:XPlat Code Coverage' --settings (Join-Path $PSScriptRoot 'coverage.runsettings') `
        --results-directory (Join-Path $runDirectory 'test-results') '--logger:trx;LogFileName=tests.trx' 2>&1 |
        Tee-Object -FilePath (Join-Path $runDirectory 'tests.log') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'API tests or coverage collection failed. See tests.log and TRX results.' }
    # TRX may copy attachments into its own machine-specific In directory.
    # Select the collector's direct GUID subdirectory rather than counting that duplicate.
    $coverageFiles = @(Get-ChildItem (Join-Path $runDirectory 'test-results') -Directory |
        Where-Object { $_.Name -match '^[0-9a-fA-F-]{36}$' } |
        ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Filter 'coverage.cobertura.xml' -File })
    if ($coverageFiles.Count -ne 1) { throw "Expected one coverage report, got $($coverageFiles.Count)." }
    [xml]$coverage = Get-Content -LiteralPath $coverageFiles[0].FullName -Raw
    $rawLinePercent = 100 * [double]::Parse($coverage.coverage.'line-rate', $culture)
    $rawBranchPercent = 100 * [double]::Parse($coverage.coverage.'branch-rate', $culture)
    $trxFiles = @(Get-ChildItem (Join-Path $runDirectory 'test-results') -Recurse -Filter '*.trx')
    if ($trxFiles.Count -ne 1) { throw 'Expected exactly one TRX test report.' }
    [xml]$trx = Get-Content -LiteralPath $trxFiles[0].FullName -Raw
    $counters = $trx.TestRun.ResultSummary.Counters

    Write-Host 'Generating HTML coverage report...'
    & dotnet tool run reportgenerator -- "-reports:$($coverageFiles[0].FullName)" "-targetdir:$reportPath" '-reporttypes:Html;TextSummary;JsonSummary' 2>&1 |
        Tee-Object -FilePath (Join-Path $runDirectory 'report-generation.log') | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $reportPath 'index.html'))) { throw 'HTML coverage report generation failed.' }

    # Use the same aggregated metrics and precision as the HTML dashboard.
    $reportSummaryFile = Join-Path $reportPath 'Summary.json'
    if (-not (Test-Path -LiteralPath $reportSummaryFile)) { throw 'ReportGenerator JSON summary is missing.' }
    $reportMetrics = (Get-Content -LiteralPath $reportSummaryFile -Raw | ConvertFrom-Json).summary
    if ($null -eq $reportMetrics.linecoverage -or $null -eq $reportMetrics.branchcoverage -or
        $reportMetrics.coverablelines -le 0 -or $reportMetrics.totalbranches -le 0) {
        throw 'ReportGenerator did not produce usable line and branch coverage metrics.'
    }
    $linePercent = [double]$reportMetrics.linecoverage
    $branchPercent = [double]$reportMetrics.branchcoverage

    $passed = $errors.Count -eq 0 -and $warnings.Count -le $policy.maxAnalyzerWarnings -and
        $linePercent -ge $policy.minLineCoveragePercent -and $branchPercent -ge $policy.minBranchCoveragePercent
    $summary = [ordered]@{
        RecordedAt = (Get-Date).ToString('o'); DotnetSdk = $sdk; AnalysisLevel = $policy.analysisLevel
        Passed = $passed; AnalyzerWarnings = $warnings.Count; CompilerErrors = $errors.Count
        LineCoveragePercent = $linePercent; BranchCoveragePercent = $branchPercent
        CoveredLines = [int]$reportMetrics.coveredlines; CoverableLines = [int]$reportMetrics.coverablelines
        CoveredBranches = [int]$reportMetrics.coveredbranches; TotalBranches = [int]$reportMetrics.totalbranches
        CoverageMetricSource = 'ReportGenerator coverage-report/Summary.json; same aggregation and displayed precision as HTML'
        RawCoverlet = [ordered]@{
            LineCoveragePercent = $rawLinePercent; BranchCoveragePercent = $rawBranchPercent
            CoveredBranches = [int]$coverage.coverage.'branches-covered'; TotalBranches = [int]$coverage.coverage.'branches-valid'
        }
        TestsTotal = [int]$counters.total; TestsPassed = [int]$counters.passed; TestsFailed = [int]$counters.failed
        Policy = $policy; WarningsByRule = $rules
        CoverageScope = 'PharmacyOrder.Api; generated EF Designer and ModelSnapshot files excluded; migration Up/Down logic included'
        Report = (Join-Path $reportPath 'index.html')
    }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $runDirectory 'summary.json') -Encoding UTF8
    $reportLines = @(
        '# Quality check results', '', "Overall passed: $passed", '',
        "Tests: $($counters.passed)/$($counters.total) passed; $($counters.failed) failed.",
        "Analyzer warnings: $($warnings.Count) (maximum $($policy.maxAnalyzerWarnings)).", 
        ('Line coverage: {0:N1}% (minimum {1}%).' -f $linePercent, $policy.minLineCoveragePercent),
        ('Branch coverage: {0:N1}% (minimum {1}%).' -f $branchPercent, $policy.minBranchCoveragePercent),
        "Branch counts: $($summary.CoveredBranches)/$($summary.TotalBranches).",
        'Metric source: ReportGenerator JSON summary, matching the HTML dashboard. Raw Coverlet values are retained separately in summary.json.',
        '', '## Analyzer warnings by rule', '', '| Rule | Count |', '|---|---:|'
    )
    foreach ($rule in $rules) { $reportLines += "| $($rule.Name) | $($rule.Count) |" }
    $reportLines += @('', 'See analyzer-findings.csv for messages/locations, analysis.sarif for machine-readable findings, and coverage-report/index.html for coverage.', '', "Scope: $($summary.CoverageScope)", '', 'Coverage scope changes are not test improvements. Warning limits are a temporary count ceiling, not a guarantee of no new findings.')
    $reportLines | Set-Content -LiteralPath (Join-Path $runDirectory 'SUMMARY.md') -Encoding UTF8
    Write-Host ('Coverage: lines {0:N2}%; branches {1:N2}%. Analyzer warnings: {2}.' -f $linePercent, $branchPercent, $warnings.Count)
    Write-Host "Results: $runDirectory"
    if ($OpenReport) { Start-Process (Join-Path $reportPath 'index.html') }
    if (-not $passed) { throw 'Quality thresholds failed. See summary.json and SUMMARY.md.' }
    Write-Host 'PASS: quality checks met the configured policy.'
    exit 0
} catch {
    Write-Error -Message $_.Exception.Message -ErrorAction Continue
    exit 1
} finally {
    Set-Location $previousLocation
}
