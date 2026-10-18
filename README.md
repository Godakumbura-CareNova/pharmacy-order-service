# pharmacy-order
Microservice responsible for medicine catalogue browsing, cart management, checkout and order tracking within the Godakumbura CareNova 
e-pharmacy and doctor-channelling platform. Handles medicine search/filtering, inventory and stock tracking, shopping cart operations, prescription upload on order and order status management for both patients and pharmacy staff.

## SE3112 Test Automation Pipeline

CareNova is a microservice system (SE3022): a React frontend, an Ocelot API gateway and .NET 8 services
(user-auth, pharmacy-order, doctor-payment) with MySQL and Kafka, deployed on Azure. The testing pipeline
targets the **pharmacy-order service** and its frontend pages.

### Who did what

| Testing area | Owner | Tools | Location |
|---|---|---|---|
| Unit Testing & Mocking | Minadi Pathirana (IT24101496) | xUnit, Moq, EF Core InMemory | `tests/unit-mocking/` |
| Code Quality & Metrics | MGND Kumara (IT24101515) | .NET analyzers, Coverlet, ReportGenerator, SonarQube | `tests/quality/`; SonarQube setup on `feature/SE3112-sonarqube-quality` |
| Load & Performance Testing | HYP Silva (IT24101493) | Apache JMeter | `tests/performance/ase/` |
| UI / E2E Testing | leesha2110 | Selenium WebDriver, NUnit | `frontend` repo, `tests/selenium/ASE*E2ETests.cs` |
| Test automation (group) | All members | GitHub Actions | `.github/workflows/se3112-test-pipeline.yml` |

### How the pipeline runs

The workflow **SE3112 Test Automation Pipeline** has one job per testing area and a final report job.

| Trigger | Jobs that run |
|---|---|
| Push to any `feature/SE3112-*` branch | Unit & Mocking, Code Quality |
| Push to `feature/SE3112-testing-automation`, or any pull request | All four areas |
| Manual: Actions → SE3112 Test Automation Pipeline → Run workflow | All four areas, with options for load profile and frontend branch |

- **Unit & Mocking**: runs the 40 unit tests and publishes a test report.
- **Code Quality**: Release build with .NET analyzers, coverage from the API test suite, and a quality gate
  (`tests/quality/quality-policy.json`: at least 60% line and 70% branch coverage, at most 33 analyzer warnings).
  SonarQube analysis also runs when the `SONAR_TOKEN` and `SONAR_HOST_URL` secrets are set.
- **Load & Performance**: installs JMeter and runs the catalogue load plan against the deployed pharmacy service
  (gate: under 1% errors and p95 at most 1000 ms per endpoint).
- **UI / E2E**: checks out the Selenium tests from the frontend repo and runs the patient purchase and
  pharmacy staff journeys in Chrome against the deployed frontend.
- **Unified Test Report**: one table on the run's summary page with the result and key metrics of every area.
  Coverage HTML, SARIF, the JMeter dashboard and TRX files are attached to the run as artifacts.

Load and UI tests use the deployed Azure environment, so they only run on the group branch, pull requests and
manual runs, not on every push.

### Setup (repository secrets)

Settings → Secrets and variables → Actions:

| Secret | Used by |
|---|---|
| `CARENOVA_TEST_PATIENT_EMAIL`, `CARENOVA_TEST_PATIENT_PASSWORD` | UI / E2E (patient account on the deployed site) |
| `CARENOVA_TEST_STAFF_EMAIL`, `CARENOVA_TEST_STAFF_PASSWORD` | UI / E2E (pharmacy staff account) |
| `SONAR_TOKEN`, `SONAR_HOST_URL` | Optional SonarQube analysis (server must be reachable from GitHub) |

### Run each area locally

From this repository (needs the .NET 8 SDK; PowerShell for the scripts):

```powershell
dotnet test tests/unit-mocking                                                   # Unit & Mocking
powershell -ExecutionPolicy Bypass -File tests/quality/run-quality.ps1 -OpenReport   # Code Quality
powershell -ExecutionPolicy Bypass -File tests/performance/ase/run-load-tests.ps1 `
  -JMeterPath C:\path\to\apache-jmeter-5.6.3\bin\jmeter.bat -Profile Smoke `
  -BaseUrl https://carenova-pharmacy-order-aegsa8hxffcqephb.centralindia-01.azurewebsites.net `
  -SearchTerm "Paracetamol 500mg"                                                 # Load & Performance
```

UI / E2E, from the `frontend` repository with the four `CARENOVA_TEST_*` variables set:

```powershell
dotnet test tests/selenium/selenium.csproj --filter "FullyQualifiedName~ASEPatientPurchaseE2ETests|FullyQualifiedName~ASEPharmacyStaffE2ETests"
```

Each area's folder has its own README with more detail.
