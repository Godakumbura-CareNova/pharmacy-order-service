[CmdletBinding()]
param(
    [string]$JMeterPath = $env:JMETER_PATH,
    [ValidateSet('Smoke', 'Normal', 'Higher', 'Fifty', 'Hundred')][string]$Profile = 'Smoke',
    [uri]$BaseUrl = 'http://localhost:5134',
    [ValidateNotNullOrEmpty()][string]$SearchTerm = 'Med1',
    [ValidateRange(0, 100)][double]$MaxErrorPercent = 1,
    [ValidateRange(1, 600000)][double]$MaxP95Ms = 1000,
    [ValidateRange(1, 3)][int]$ReadinessAttempts = 2,
    [ValidateRange(1, 60)][int]$ReadinessTimeoutSec = 30,
    [string]$OutputRoot,
    [switch]$OpenReport
)

$ErrorActionPreference = 'Stop'
$runDirectory = $null
$stage = 'Setup'
$measurementStarted = $false
try {
    if (-not $OutputRoot) { $OutputRoot = Join-Path $PSScriptRoot 'results' }
    $runName = '{0}-{1}-{2}' -f $Profile.ToLowerInvariant(), (Get-Date -Format 'yyyyMMdd-HHmmss'), ([guid]::NewGuid().ToString('N').Substring(0, 6))
    $runDirectory = (New-Item -ItemType Directory -Path (Join-Path $OutputRoot $runName) -Force).FullName
    if (-not $JMeterPath -or -not (Test-Path -LiteralPath $JMeterPath -PathType Leaf)) {
        throw 'Set JMETER_PATH or pass -JMeterPath with the full path to jmeter.bat.'
    }
    if ($BaseUrl.Scheme -notin @('http', 'https') -or $BaseUrl.AbsolutePath -ne '/' -or $BaseUrl.Query -or $BaseUrl.UserInfo) {
        throw 'BaseUrl must be an HTTP(S) origin, such as http://localhost:5134.'
    }
    $settings = @{
        Smoke = @{ Users = 1; Ramp = 1; Loops = 1 }
        Normal = @{ Users = 10; Ramp = 10; Loops = 5 }
        Higher = @{ Users = 30; Ramp = 20; Loops = 5 }
        Fifty = @{ Users = 50; Ramp = 20; Loops = 5 }
        Hundred = @{ Users = 100; Ramp = 20; Loops = 5 }
    }[$Profile]

    # Readiness/data checks are outside the measured load.
    $stage = 'Readiness'
    $readinessLog = New-Object System.Collections.Generic.List[object]
    function Get-ReadyJson([uri]$Uri) {
        for ($attempt = 1; $attempt -le $ReadinessAttempts; $attempt++) {
            Write-Host "Readiness $attempt/$ReadinessAttempts (timeout ${ReadinessTimeoutSec}s): $Uri"
            $watch = [Diagnostics.Stopwatch]::StartNew()
            try {
                $response = Invoke-RestMethod -Uri $Uri -TimeoutSec $ReadinessTimeoutSec
                $readinessLog.Add([pscustomobject]@{ Url = $Uri.AbsoluteUri; Attempt = $attempt; Success = $true; ElapsedMs = $watch.ElapsedMilliseconds; Error = $null })
                return $response
            } catch {
                $reason = $_.Exception.Message
                $readinessLog.Add([pscustomobject]@{ Url = $Uri.AbsoluteUri; Attempt = $attempt; Success = $false; ElapsedMs = $watch.ElapsedMilliseconds; Error = $reason })
                if ($attempt -eq $ReadinessAttempts) { throw "Readiness failed for $Uri after $ReadinessAttempts attempts: $reason. JMeter was not started." }
                Write-Warning "Readiness request failed: $reason. Retrying once before measuring."
            } finally {
                ConvertTo-Json -InputObject @($readinessLog.ToArray()) -Depth 4 | Set-Content -LiteralPath (Join-Path $runDirectory 'readiness.json') -Encoding UTF8
            }
        }
    }
    if ([string]::IsNullOrWhiteSpace($SearchTerm) -or $SearchTerm.Length -gt 200 -or $SearchTerm -match '[\r\n]') {
        throw 'SearchTerm must contain 1-200 characters and no line breaks.'
    }
    $encodedSearch = [uri]::EscapeDataString($SearchTerm)
    $catalogueUrl = [uri]::new($BaseUrl, "/api/catalogue/medicines?search=$encodedSearch&page=1&pageSize=12")
    $catalogue = Get-ReadyJson $catalogueUrl
    if (@($catalogue.items | Where-Object { $_.name -and $_.name.IndexOf($SearchTerm, [StringComparison]::OrdinalIgnoreCase) -ge 0 }).Count -eq 0) {
        throw "No medicine name matches '$SearchTerm'. Set -SearchTerm to a name available in the target catalogue."
    }
    $null = Get-ReadyJson ([uri]::new($BaseUrl, '/api/catalogue/categories'))

    $resultsFile = Join-Path $runDirectory 'samples.jtl'
    $reportDirectory = Join-Path $runDirectory 'report'
    $machine = [ordered]@{
        RecordedAt = (Get-Date).ToString('o'); OS = [Environment]::OSVersion.VersionString
        LogicalProcessors = [Environment]::ProcessorCount
        Cpu = 'Unavailable'; RAMGiB = $null; KafkaListening = 'Unknown'
        Data = "Search fixture: $SearchTerm; no automatic database seeding"
        Target = $BaseUrl.AbsoluteUri
        MachineScope = 'Load-generator PC; local Kafka listener does not describe Azure infrastructure'
        WarmUp = 'Readiness calls only; no dedicated warm-up period'
    }
    try {
        $machine.Cpu = (Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1).Name
        $machine.RAMGiB = [math]::Round((Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).TotalPhysicalMemory / 1GB, 1)
        $machine.KafkaListening = [bool](Get-NetTCPConnection -State Listen -LocalPort 9092 -ErrorAction SilentlyContinue)
    } catch { Write-Warning 'Some environment details are unavailable; record them manually if needed.' }
    $machine | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDirectory 'environment.json') -Encoding UTF8
    $arguments = @(
        '-n', '-t', (Join-Path $PSScriptRoot 'catalogue-load.jmx'),
        '-l', $resultsFile, '-j', (Join-Path $runDirectory 'jmeter.log'),
        '-e', '-o', $reportDirectory,
        "-Jusers=$($settings.Users)", "-JrampUp=$($settings.Ramp)", "-Jloops=$($settings.Loops)",
        "-Jserver=$($BaseUrl.DnsSafeHost)", "-Jport=$($BaseUrl.Port)", "-Jprotocol=$($BaseUrl.Scheme)",
        "-JsearchEncoded=$encodedSearch",
        '-Jjmeter.save.saveservice.output_format=csv', '-Jjmeter.save.saveservice.print_field_names=true',
        '-Jjmeter.save.saveservice.default_delimiter=,'
    )
    Write-Host "Running $Profile profile: $($settings.Users) users, $($settings.Loops) loops against $BaseUrl"
    $stage = 'Measurement'
    $measurementStarted = $true
    & $JMeterPath @arguments
    if ($LASTEXITCODE -ne 0) { throw "JMeter exited with code $LASTEXITCODE. See $runDirectory" }

    $samples = @(Import-Csv -LiteralPath $resultsFile)
    $expected = $settings.Users * $settings.Loops * 3
    if ($samples.Count -ne $expected) { throw "Expected $expected samples, got $($samples.Count). See $runDirectory" }
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $metrics = @($samples | Group-Object label | ForEach-Object {
        $group = @($_.Group)
        $times = @($group | ForEach-Object { [double]::Parse($_.elapsed, $culture) } | Sort-Object)
        $failed = @($group | Where-Object { $_.success -ne 'true' }).Count
        # Nearest-rank p95; JMeter's dashboard may use a different interpolation method.
        $p95 = $times[[int][math]::Ceiling(0.95 * $times.Count) - 1]
        $errorPercent = 100.0 * $failed / $group.Count
        [pscustomobject]@{
            Endpoint = $_.Name; Samples = $group.Count; Failed = $failed
            ErrorPercent = $errorPercent; P95Ms = $p95
            Passed = ($errorPercent -lt $MaxErrorPercent -and $p95 -le $MaxP95Ms) -or
                ($MaxErrorPercent -eq 0 -and $failed -eq 0 -and $p95 -le $MaxP95Ms)
        }
    })
    $passed = @($metrics | Where-Object { -not $_.Passed }).Count -eq 0
    $summary = [ordered]@{
        Stage = 'Completed'; MeasurementStarted = $measurementStarted
        Profile = $Profile; BaseUrl = $BaseUrl.AbsoluteUri; SearchTerm = $SearchTerm; Users = $settings.Users
        RampUpSeconds = $settings.Ramp; Loops = $settings.Loops; ThinkTimeMs = 1000
        Samples = $samples.Count; MaxErrorPercentExclusive = $MaxErrorPercent; MaxP95MsInclusive = $MaxP95Ms
        Passed = $passed; Endpoints = $metrics; Report = (Join-Path $reportDirectory 'index.html')
    }
    $summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runDirectory 'summary.json') -Encoding UTF8
    $metrics | Format-Table -AutoSize | Out-Host
    Write-Host "Report: $($summary.Report)"
    if ($OpenReport) { Start-Process $summary.Report }
    if (-not $passed) { throw 'Performance thresholds failed. See summary.json and the HTML report.' }
    Write-Host 'PASS: all endpoint thresholds met.'
    exit 0
} catch {
    $failure = $_.Exception.Message
    if ($runDirectory -and -not (Test-Path -LiteralPath (Join-Path $runDirectory 'summary.json'))) {
        [ordered]@{
            Profile = $Profile; BaseUrl = $BaseUrl.AbsoluteUri; SearchTerm = $SearchTerm
            Passed = $false; Stage = $stage; MeasurementStarted = $measurementStarted
            Error = $failure; Report = $null
        } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runDirectory 'summary.json') -Encoding UTF8
    }
    Write-Error -Message $failure -ErrorAction Continue
    exit 1
}
