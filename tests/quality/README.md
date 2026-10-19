# ASE Code Quality & Metrics

Owner: **MGND Kumara — IT24101515**. This contribution configures automated analysis, metrics, reports and a quality gate. Existing tests are reused; their authors retain ownership of the test logic.

## Setup on another computer

Use the same pharmacy repository revision and tests. Install the .NET 10 SDK matching the validated environment (10.0.302) and the .NET 8 runtime required by the net8.0 tests. Check `dotnet --list-sdks` and `dotnet --list-runtimes`. The application still targets .NET 8; no target framework was changed.

Run from the pharmacy repository:

```powershell
dotnet tool restore --tool-manifest ./dotnet-tools.json
powershell -NoProfile -ExecutionPolicy Bypass -File ./tests/quality/run-quality.ps1 -OpenReport
```

The manifest pins ReportGenerator 5.5.11. PowerShell 5.1 or newer is supported. Initial restore requires NuGet connectivity. No SonarQube installation, Azure deployment, Selenium, JMeter or separately running API is required. The existing test fixtures use test databases; investigate setup failures rather than changing business code to make the gate pass.

Release builds avoid the Debug executable lock encountered when the API was running. Do not run two quality/test builds concurrently or run the API from the same Release output folder during this check.

## What the runner does

1. Restores the pinned local report tool.
2. Rebuilds the API in Release with .NET analyzers enabled and `8.0-recommended` rules.
3. Saves compiler diagnostics as SARIF and extracts CA-rule warnings into CSV.
4. Runs the API tests with Coverlet and saves TRX results.
5. Generates an HTML coverage report and text coverage summary.
6. Evaluates the explicit policy and writes JSON/Markdown summaries.
7. Returns exit code 0 on success, 1 on build/test/tool/report failures or failed thresholds.

The installed SDK supplies the analyzer implementation; the analysis level alone does not pin its binaries. Reproduce with the documented SDK, or document any SDK change and review the baseline.

## Coverage scope: be explicit

The original unfiltered baseline was **22.9% line coverage, 76.5% branch coverage**, with 251 passing tests. It included thousands of unexecuted generated EF migration-designer/model-snapshot lines.

`coverage.runsettings` excludes the whole `Migrations/` folder, includes only the API assembly, and excludes test assemblies. This matches SonarQube (`sonar.coverage.exclusions=**/Migrations/**`). Handwritten controllers and services remain measured.

Keep the original unfiltered report for context. A higher scoped percentage reflects a changed denominator, **not new tests or improved correctness**. The report only proves code execution, not assertion quality or absence of bugs.

## Policy and limitations

`quality-policy.json` defines:

- At most **33 CA-rule warnings**: the initial successful analysis build had 33. This is an explicit temporary legacy-warning ceiling, not a zero-warning claim or a finding-by-finding regression baseline. A removed warning can mask a new warning under this count-based policy; review CSV/SARIF differences too.
- At least **60% scoped line coverage**: a starting floor near the measured existing scoped coverage, not a claim of adequate testing for every class.
- At least **70% branch coverage**: protects a starting floor below the observed 76.5% baseline.
- Build/test failures always fail the runner.

Coverage gates and summaries now read ReportGenerator's `coverage-report/Summary.json`, using the same aggregation and displayed precision as the HTML dashboard. Raw Coverlet root metrics are kept separately under `RawCoverlet` in the runner summary; do not mix the two metric sources.

These initial thresholds expose and preserve a starting point; they are team choices, not lecturer requirements or production standards. Do not lower them silently to get a pass. Important business logic may need stronger coverage targets and additional tests.

Compiler errors fail the build. The warning ceiling counts only CA analyzer warnings, not NuGet/MSBuild warnings or all C# warnings; those remain visible in the build log and must be reviewed separately. No warnings are automatically suppressed or fixed.

For a failure demonstration, copy the policy to a temporary file, set the maximum warnings to zero (or minimum coverage to 100), and pass `-PolicyPath <temporary-file>`. Keep the default policy unchanged. A failing gate is useful evidence that automation enforces checks.

## Outputs

### Verified baseline (17 October 2026)

The original runner completed successfully with SDK 10.0.302: **251 tests passed, 33 active CA warnings, 60.26% scoped line coverage, 77.51% branch coverage**, based on Coverlet root values. Evidence: `results/20261017-165735-e6ddfa/summary.json`. These historical metrics were not calculated from the HTML dashboard; see the reconciliation note below. The historical unfiltered report used Debug; the automated report uses Release, so compiler output differences also affect exact counts. Do not attribute the difference to newly added tests.

#### Coverage reconciliation (18 October 2026)

For the exact same run `20261017-170143-a47258`, raw Coverlet root values were 355/458 branches (77.51%), whereas ReportGenerator's text/HTML/JSON summaries reported 331/430 (76.9%). Both input attachment copies had identical SHA-256 hashes. This was a difference in metric aggregation between outputs, not evidence of an additional test run or improved tests. The runner previously mixed sources. It now uses ReportGenerator for both gates and summaries; historical evidence is left unchanged. ReportGenerator also displays line coverage to one decimal place (60.2% for that run). The precise internal branch-normalization cause was not established; do not claim it was random test variation.

For that historical run the additional `reconciliation/Summary.json` reproduces ReportGenerator's totals from the saved raw XML. For presentation, use the latest verified runner summary and its matching HTML report together.

A separate run using a temporary zero-warning policy checks failure handling without changing the default policy. Both source files and generated-file exclusions remain unchanged during that check.

### Files

Every invocation creates `results/<timestamp>-<unique-id>/` containing:

- `analysis-build.log`, `analysis.sarif`, `analyzer-findings.csv`.
- `test-results/`: raw coverage XML and TRX.
- `coverage-report/index.html`: share the **whole folder**, not only index.html.
- `summary.json`, `SUMMARY.md`: test counts, metrics, policy outcomes and warning counts per rule.
- Tool/test/report-generation logs.

Results and the earlier manually generated report are ignored by Git. Package selected evidence deliberately. Configuration/scripts/docs and the root `dotnet-tools.json` must be shared with your friend along with matching application/test code. Do not share patient uploads, secrets or machine-specific NuGet credentials.

## Group runner integration

Run as a separate process because the script uses exit codes:

```powershell
$qualityRunner = Join-Path $projectRoot 'pharmacy-order-service/tests/quality/run-quality.ps1'
powershell -NoProfile -ExecutionPolicy Bypass -File $qualityRunner
if ($LASTEXITCODE -ne 0) { throw 'Code quality/metrics failed.' }
```

This is the quality contribution's integration contract. The shared runner is now available at `frontend/assessment/run-assessment.ps1`; it currently integrates quality and load, with unit/UI marked pending. See its README. Full four-contribution integration is not complete.

## Two-minute demo and viva

1. Show the policy and explain the generated-file exclusions.
2. Run the quality command live (restore dependencies beforehand for predictable timing).
3. Show warnings by rule, a real warning location and the line/branch coverage dashboard.
4. Explain limits: static findings need review, coverage is not correctness, scoped coverage differs from the original baseline, and the legacy-warning ceiling allows existing debt.

Suggested findings to explain: CA1304/CA1305 concern culture-dependent conversions, CA1862 concerns some case-insensitive string comparisons, and CA1707/CA1711 concern naming. Read each actual location before proposing a fix; EF-translated queries can behave differently from in-memory strings. No automatic fixes are part of this contribution.

## References

- https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview
- https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-options/errors-warnings
- https://github.com/coverlet-coverage/coverlet/blob/master/Documentation/VSTestIntegration.md