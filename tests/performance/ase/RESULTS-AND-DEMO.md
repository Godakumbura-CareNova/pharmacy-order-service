# Load-testing evidence, integration and live demo

## Ownership

Area: Load & Performance Testing. Owner: **HYP Silva - IT24101493**.
Repo: pharmacy-order-service. Tools: Java 17, JMeter 5.6.3, Windows PowerShell.

## Local results: historical baseline

These are the owner's automated runs on 17 October 2026, before replacing substring checks with semantic JSON assertions. Do not present them as results of the updated assertions.

| Metric | Normal | Higher |
|---|---:|---:|
| Concurrent users configured | 10 | 30 |
| Ramp-up seconds | 10 | 20 |
| Loops per user | 5 | 5 |
| Requests | 150 | 450 |
| Failed requests | 0 | 0 |
| Mean response time, ms | 11.59 | 13.61 |
| Dashboard total p95, ms | 21.45 | 21.45 |
| Overall requests/second | 6.48 | 13.39 |
| All per-endpoint thresholds passed | Yes | Yes |

Sources (ignored local evidence folders):

- `results/normal-20261017-153444-3795b3/summary.json`, `samples.jtl` and `report/statistics.json`.
- `results/higher-20261017-154532-388540/summary.json`, `samples.jtl` and `report/statistics.json`.
- The normal report also has a `report-corrected` folder fixing the original header date display; raw measurements were not changed.

Runner nearest-rank endpoint p95:

| Endpoint | Normal p95 ms | Higher p95 ms |
|---|---:|---:|
| Browse medicines | 21 | 24 |
| Search medicines | 23 | 21 |
| Browse categories | 14 | 13 |

Interpretation: this higher workload completed more requests per second with a modest increase in mean response time and no failures. Some percentiles improved due to normal run variability or warm caches; these single runs do not establish causation. Overall throughput includes the one-second think time and ramp-up; it is not maximum API capacity. Three times the configured users did not produce three times the measured throughput.

Targets: less than 1% failed requests and p95 <= 1000 ms for **each endpoint**. These are provisional team response/responsiveness targets, not business SLAs or lecturer-specified values. Explain and revise them if the real requirements demand different limits.

## Local results: updated checks and environment

### Updated JSON assertions verified

Both profiles were rerun after the semantic JSON assertions were added:

| Profile | Requests | Failures | Browse p95 ms | Search p95 ms | Categories p95 ms | Runner result |
|---|---:|---:|---:|---:|---:|---|
| Normal | 150 | 0 | 25 | 23 | 15 | PASS |
| Higher | 450 | 0 | 21 | 20 | 13 | PASS |

Sources: `results/normal-20261017-161503-9dd5f3` and `results/higher-20261017-162127-dc93b4`. These are the latest full-profile evidence for the strengthened test plan. The earlier comparison remains a separately labelled historical baseline. The same small-data/background-infrastructure limitations apply; no Kafka recovery was performed for these reruns.

### Test conditions

Environment inspected after these runs:

- Windows 11 Pro; Intel Core i7-13620H; 16 logical processors; approximately 15.7 GiB physical RAM.
- Pharmacy API on localhost:5134 with local MySQL. Source base revision observed: `644e24b`, plus these uncommitted assessment files.
- Two catalogue medicines and one category at setup; the search fixture is Med1.
- JMeter and the API share the same machine and resources.
- Kafka was stopped at setup and no port 9092 listener was present during the later inspection. Exact Kafka state during each historical run was not recorded. Background Kafka retry activity may influence measurements.
- No dedicated warm-up or repeated statistical trials; readiness calls and earlier manual runs may warm the service/database.
- Short loop-based runs measure only a small, anonymous, read-only workflow. They do not evaluate writes, large datasets, long-term stability, production hosting or maximum capacity.

Before final representative measurements, restore normal infrastructure, use a documented dataset, avoid concurrent UI/load jobs, and repeat Normal/Higher with the updated assertions. The runner now saves `environment.json` for each new run; unavailable details should be recorded manually.

## Deployed Azure results (17 October 2026)

These measurements are **separate from the localhost results above**. JMeter ran on the owner's PC and sent HTTPS requests directly to the deployed pharmacy microservice:

```text
https://carenova-pharmacy-order-aegsa8hxffcqephb.centralindia-01.azurewebsites.net
```

The gateway, frontend, authentication flow, checkout and order writes were not part of this workload. The target was verified from `BaseUrl` in each summary and the request URL column in the JTL, not inferred from the locally stored HTML report.

### Workload and data

- Three anonymous GET scenarios: browse medicines, search medicines and browse categories.
- Search term: **Paracetamol 500mg**. Azure had four medicines at inspection; local data had two and used Med1. No deployed database changes were made.
- Five loops per user, three requests per loop, one-second pause before each request.
- Ramp-up: 10 seconds for Normal; 20 seconds for Higher, Fifty and Hundred.
- Each request checks HTTP 200 and semantic JSON content. The runner also checks error rate and nearest-rank p95 for each endpoint.
- Targets unchanged: **error rate < 1% and endpoint p95 <= 1,000 ms**.

### Comparison of full-profile runs

| Metric | Normal | Higher | Fifty | Hundred |
|---|---:|---:|---:|---:|
| Configured users | 10 | 30 | 50 | 100 |
| Requests | 150 | 450 | 750 | 1,500 |
| Request/assertion failures | 0 | 0 | 0 | 0 |
| Error rate | 0% | 0% | 0% | 0% |
| Mean response time, ms | 356.31 | 314.59 | 303.12 | 332.85 |
| Dashboard total p95, ms | 1,075.15 | 564.70 | 552.35 | 644.40 |
| Overall requests/second | 5.39 | 11.85 | 19.74 | 38.29 |
| Per-endpoint performance gate | **FAIL** | PASS | PASS | PASS |

Mean, total p95 and throughput come from each dashboard's `report/statistics.json`. The gate uses per-endpoint nearest-rank p95 from `summary.json`, so its numbers can differ slightly from dashboard interpolation.

| Endpoint p95, ms (runner) | 10 users | 30 users | 50 users | 100 users |
|---|---:|---:|---:|---:|
| Browse medicines | 872 | 616 | 581 | 723 |
| Search medicines | **1,283** | 380 | 338 | 626 |
| Browse categories | 223 | 259 | 221 | 460 |

**Keep the failed 10-user result.** All responses passed their status/content assertions, but search breached the 1,000 ms p95 limit. The green request-success chart does not mean the performance gate passed. The thresholds were not raised to hide the failure.

### Saved evidence

Run directories under `results/`:

| Profile | Evidence directory |
|---|---|
| Normal / 10 users | `normal-20261017-201420-347ae9` |
| Higher / 30 users | `higher-20261017-201628-c272dc` |
| Fifty / 50 users | `fifty-20261017-202755-4db462` |
| Hundred / 100 users | `hundred-20261017-203052-2b5799` |

Each contains `samples.jtl`, `summary.json`, `environment.json`, `jmeter.log` and the full `report/` folder. The 100-user JTL recorded a maximum of 100 active threads, establishing that this run reached 100 concurrent simulated users, though not for a sustained capacity trial.

Earlier Azure Smoke runs `smoke-20261017-200956-64537d` and `smoke-20261017-201050-d48b8e` each made three requests with no assertion failures, but browsing took 1,517 ms and 1,110 ms respectively, failing the existing performance gate. One sample per endpoint is a readiness observation, not a reliable tail-latency estimate.

### Interpretation and limitations

- These short runs show the observed catalogue behaviour under the documented profiles. The 100-user run completed 1,500 requests without assertion failures and met each endpoint's p95 target.
- The 10-user test failed while later, larger runs passed. Cache warming, network variability and changing server activity are possible explanations, **not proven causes**. Repeat equivalent profiles with documented warm-up conditions before drawing causal conclusions.
- Results include internet latency and HTTPS connection overhead. Do not mix them with localhost measurements in one capacity conclusion.
- Four medicines are a tiny dataset; catalogue reads do not represent realistic stock/order write workloads or whole-system capacity.
- Five-loop tests have ramp-up and ramp-down effects. Configured thread count is not necessarily sustained active concurrency. These runs do not identify the breaking point or maximum sustainable request rate.
- CPU must be correlated with the actual run window. App Service Plan CPU can reflect shared apps and averaged measurements; no precise CPU baseline or per-run server metric export is included in this evidence.
- Azure pricing tier, instance count/autoscaling, deployment revision, database tier and concurrent activity were not verified for these historical runs. Record them before a reproducible capacity study. PC hardware in environment.json describes the load generator, not Azure.
- Local Kafka status is not evidence of deployed Kafka health. Local services do not need to run when testing the remote API.

For the presentation, state: **"We measured the deployed catalogue API with up to 100 concurrent simulated users in a short read-only workload."** Do not claim the entire pharmacy order system supports a proven maximum of 100 users.

### Repeat a deployed profile

From the pharmacy repository:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\performance\ase\run-load-tests.ps1 -JMeterPath $env:JMETER_PATH -BaseUrl 'https://carenova-pharmacy-order-aegsa8hxffcqephb.centralindia-01.azurewebsites.net' -SearchTerm 'Paracetamol 500mg' -Profile Normal -OpenReport
```

Set JMETER_PATH to the installed JMeter launcher first. Select Smoke, Higher, Fifty or Hundred to change profiles. Re-running is not necessary merely to reproduce the documentation; the table above records the existing evidence.

## Group integration contract

The shared runner now exists at `frontend/assessment/run-assessment.ps1`. Quality and load are integrated; unit testing and UI/E2E remain pending. See its README for combined reports and exit codes. A full-group pass is not claimed until all four contributions are integrated and verified.

From a parent PowerShell script, assuming the frontend and pharmacy folders are siblings:

```powershell
# $projectRoot is the directory containing all six repositories.
$loadRunner = Join-Path $projectRoot 'pharmacy-order-service/tests/performance/ase/run-load-tests.ps1'
powershell -NoProfile -ExecutionPolicy Bypass -File $loadRunner -JMeterPath $env:JMETER_PATH -BaseUrl 'https://carenova-pharmacy-order-aegsa8hxffcqephb.centralindia-01.azurewebsites.net' -SearchTerm 'Paracetamol 500mg' -Profile Normal
if ($LASTEXITCODE -ne 0) { throw 'Load/performance checks failed.' }
```

The group runner must set JMETER_PATH, check deployed readiness/search data, and schedule this sequentially rather than alongside browser tests. For localhost, prepare/start its API, database and normal infrastructure instead. For the two-minute demo use `-Profile Smoke -OpenReport` with the same explicit Azure BaseUrl/SearchTerm. A smoke performance-gate failure is possible and must be explained honestly. Run the child in a separate PowerShell process because it exits with 0/1.

Each run gets a unique directory. Keep raw JTL, summary, environment and the complete HTML report folder as submission evidence. They are ignored by Git; package selected evidence deliberately rather than assuming a git push includes it. JMX, scripts and documentation belong in source control once approved.

## Two-minute live demo

1. **0:00-0:20:** Show the JMX: three GET requests, status assertions, JSON assertions and timer. Explain users/ramp-up/loops.
2. **0:20-0:50:** Run Smoke through the PowerShell runner. Show the three results, threshold checks and exit status.
3. **0:50-1:25:** Open the saved deployed comparison dashboards, including the failed 10-user result, and explain p95, throughput and errors.
4. **1:25-2:00:** Explain thresholds, small-data/short-run limitations, and how the group runner calls the deployed test. Keep local baseline figures clearly labelled.

Timing is a rehearsal target, not a guarantee; JMeter startup/report generation varies. Have saved reports ready while showing the live command's output.

## Viva preparation

- **Concurrent users vs requests:** threads simulate users; each user performs 3 requests per loop. Thirty users x 5 loops x 3 requests = 450 samples.
- **Ramp-up:** introduces users over time rather than starting all at once.
- **Think time:** a one-second delay before each sampler, independent for each thread; it is not API response time.
- **Mean vs p95:** the mean averages all durations; p95 describes the tail experienced by slower requests.
- **Throughput:** completed requests per second, influenced by workload, pacing and run duration.
- **Assertions:** status 200 alone is insufficient; valid JSON and expected data are checked too.
- **Automation:** JMeter runs non-interactively, reports are saved, and per-endpoint thresholds produce a 0/1 process exit code.
- **Load vs stress:** these test known increasing loads. No saturation/breaking point was established, so do not call this proof of maximum capacity.
- **Why no login:** selected catalogue routes allow anonymous reads; removing unrelated auth traffic isolates the selected workload.
- **Failure demonstration:** the runner was previously checked with `-MaxP95Ms 1`, which deliberately failed and returned exit code 1. This verifies the gate, not a new production requirement.
- **Scope:** tests observe the service; no backend business code is changed.
