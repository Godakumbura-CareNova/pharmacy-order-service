$ErrorActionPreference = "Stop"
if (-not $env:SONAR_TOKEN) { throw "SONAR_TOKEN environment variable is required." }
$scannerArgs = @(
    '/k:CareNova-Pharmacy-Order-Service'
    '/d:sonar.host.url=' + ('http:' + [char]47 + [char]47 + 'localhost:9001')
    '/d:sonar.token=' + $env:SONAR_TOKEN
    '/d:sonar.coverage.exclusions=' + ([string][char]42 + [string][char]42 + '/Migrations/' + [string][char]42 + [string][char]42)
    '/d:sonar.cs.opencover.reportsPaths=' + ([string][char]42 + [string][char]42 + '/coverage.opencover.xml')
)
dotnet sonarscanner begin @scannerArgs
if ($LASTEXITCODE -ne 0) { throw "SonarScanner begin failed." }
dotnet build PharmacyOrder.sln --no-incremental
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
dotnet test tests/PharmacyOrder.Api.Tests/PharmacyOrder.Api.Tests.csproj --no-build --collect:"XPlat Code Coverage" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }
dotnet sonarscanner end ("/d:sonar.token=" + $env:SONAR_TOKEN)
if ($LASTEXITCODE -ne 0) { throw "SonarScanner end failed." }
