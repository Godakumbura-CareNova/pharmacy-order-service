# ASE catalogue performance tests

This is a portable copy of the manually created catalogue plan. The original files in `C:\ASE assignment` are unchanged. No application code is modified.

## Prerequisites

- Java and Apache JMeter (verified locally with Java 17 / JMeter 5.6.3).
- Windows PowerShell 5.1 or newer.
- Pharmacy API, database and its normal infrastructure running locally.
- Known test data: default search term `Med1`, or supply `-SearchTerm` matching an existing medicine name.

Restore Kafka before collecting representative performance measurements; repeated background connection errors can affect measurements. Use a controlled test database. This plan sends only anonymous GET requests and does not require the frontend, login or gateway.

## Run from the pharmacy repository

```powershell
$env:JMETER_PATH = 'C:\Users\User\Downloads\apache-jmeter-5.6.3\apache-jmeter-5.6.3\bin\jmeter.bat'
powershell -NoProfile -File .\tests\performance\ase\run-load-tests.ps1 -Profile Smoke -OpenReport
```

Use your own JMeter installation path. Run the script as a separate process: it uses exit codes for integration with the group's runner. Alternatively pass `-JMeterPath` explicitly. If script execution is restricted, use a process-scoped execution policy permitted by your environment; do not change machine-wide settings.

| Profile | Users | Ramp-up seconds | Loops | Expected requests |
|---|---:|---:|---:|---:|
| Smoke | 1 | 1 | 1 | 3 |
| Normal | 10 | 10 | 5 | 150 |
| Higher | 30 | 20 | 5 | 450 |
| Fifty | 50 | 20 | 5 | 750 |
| Hundred | 100 | 20 | 5 | 1500 |

Change `-Profile Smoke` to `-Profile Normal` or `-Profile Higher`. Each user pauses one second before each request. These are loop-based profiles, not fixed-duration tests. Thirty users does not establish maximum capacity.

`-Profile Fifty` configures 50 threads over a 20-second ramp-up, five loops and 750 requests. It is a short comparison run, not a sustained 50-user capacity test: earlier users may finish before all threads have started. Confirm actual overlap in the active-threads graph. Use duration-based workloads for a later capacity study.

`-Profile Hundred` configures 100 threads over a 20-second ramp-up, five loops and 1,500 requests. It has the same short-run and actual-concurrency limitations; it does not establish maximum system capacity.

`-BaseUrl http://localhost:5134` can target another agreed test environment. Do not run load against shared production without agreement.

## Requests and checks

1. Browse medicines: status 200, valid JSON, nonempty items, pagination, medicine identifiers/names and nonnegative prices.
2. Search: the same checks, plus returned names contain the configured search term, ignoring case.
3. Browse categories: status 200 and a nonempty JSON array with numeric identifiers and string names.

JSON validation uses JMeter's built-in Groovy JSR223 assertions with JsonSlurper. It does not depend on whitespace or JSON property order. No extra plugin is needed.

The runner verifies API readiness and search data before measuring, disables GUI listeners in its plan, sets timeouts, and writes a new results directory for every run. It never appends to the original 30-user results.

Readiness requests allow 30 seconds and at most two attempts per endpoint by default. Override with `-ReadinessTimeoutSec` and `-ReadinessAttempts` if needed. These attempts are logged in `readiness.json` and **excluded from measured samples**; they do not relax the 1,000 ms p95 gate or the JMeter sampler timeouts. Retried readiness calls can warm the API, so record them when comparing runs. If readiness still fails, the runner writes a failure summary with `Stage: Readiness`, `MeasurementStarted: false`, and the target/error. This does not prove whether the cause is cold start, network or an unhealthy service; check Azure logs to diagnose it.

### Deployed pharmacy API

Use `-BaseUrl` with the direct deployed service origin and `-SearchTerm 'Paracetamol 500mg'` for the currently observed Azure data. The runner does not modify the database. Start with Smoke, then run Normal/Higher at an agreed time. No local API or local Kafka is required for a remote target. Environment hardware details describe the load-generator PC, not Azure; record Azure plan, scaling, deployment version and concurrent activity separately. Network latency and cold starts affect remote measurements. Do not compare remote and localhost timings as identical environments.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\performance\ase\run-load-tests.ps1 -JMeterPath $env:JMETER_PATH -BaseUrl 'https://carenova-pharmacy-order-aegsa8hxffcqephb.centralindia-01.azurewebsites.net' -SearchTerm 'Paracetamol 500mg' -Profile Smoke -OpenReport
```

## Reports and pass/fail

Under `results/<profile>-<timestamp>-<unique-id>/`:

- `samples.jtl`: raw CSV samples.
- `jmeter.log`: diagnostic log.
- `report/index.html`: JMeter dashboard; share the whole report folder.
- `summary.json`: workload configuration and per-endpoint threshold outcomes.
- `environment.json`: available machine details, Kafka listener snapshot and warm-up limitations.

Default thresholds: each endpoint must have **less than 1% errors** and **p95 <= 1000 ms**. Pass `-MaxErrorPercent` and `-MaxP95Ms` to change these justified team targets. Setting `-MaxErrorPercent 0` means no errors permitted. These are proposed targets, not assignment-mandated requirements.

The runner uses nearest-rank p95, which may differ slightly from dashboard interpolation. It exits 0 on success and 1 on missing prerequisites, incomplete samples, execution errors or failed thresholds. JMeter's process exit code alone does not establish that assertions or performance thresholds passed.

Run profiles sequentially without simultaneous UI tests or unrelated heavy activity. Record hardware, data size, service versions, warm-up conditions and background services when explaining results. Current tiny test data is not representative of production-scale catalogue queries.

## References

See [RESULTS-AND-DEMO.md](RESULTS-AND-DEMO.md) for the saved comparison, group integration command and viva notes.

- https://jmeter.apache.org/usermanual/get-started.html
- https://jmeter.apache.org/usermanual/generating-dashboard.html
