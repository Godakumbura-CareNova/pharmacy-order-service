# GCN-31 Checkout & Order Tracking — QA test suite

| Layer | File | Count | Needs running services? |
|---|---|---|---|
| Unit (xUnit) | `PharmacyOrder.Api.Tests/Unit/CheckoutOrderUnitTests.cs` | 8 | No |
| Integration (xUnit + in-memory SQLite) | `PharmacyOrder.Api.Tests/Integration/CheckoutOrderTrackingIntegrationTests.cs` | 8 | No |
| Vitest (React) | `../../frontend/src/pages/PatientCheckoutTracking.test.jsx` | 4 | No |
| Selenium (xUnit + Chrome) | `PharmacyOrder.Selenium.Tests/CheckoutOrderTrackingUiTests.cs` | 4 | **Yes** — frontend + auth + pharmacy API |
| JMeter | `jmeter/register-load-test.jmx` | 1 plan | **Yes** — auth API |
| JMeter | `jmeter/checkout-orders-load-test.jmx` | 1 plan | **Yes** — auth + pharmacy API |

## Unit + integration

```bash
# from pharmacy-order-service/
dotnet test tests/PharmacyOrder.Api.Tests --filter "FullyQualifiedName~CheckoutOrder"
```

## Vitest

```bash
# from frontend/
npx vitest run src/pages/PatientCheckoutTracking.test.jsx
```

## Selenium

Start all three apps first (separate terminals):

```bash
cd carenova-user-auth-service && dotnet run --project src/UserAuth.Api --launch-profile http   # :5133
cd pharmacy-order-service    && dotnet run --project src/PharmacyOrder.Api --launch-profile http # :5134
cd frontend                  && npm run dev                                                      # :3000
```

The two logged-in tests need an existing **Patient** account. Defaults are
`patient@carenova.test` / `Patient@123`; override with environment variables:

```bash
# Git Bash
export CARENOVA_PATIENT_EMAIL=someone@example.com
export CARENOVA_PATIENT_PASSWORD='TheirPassword1!'
export CARENOVA_HEADLESS=false      # optional: watch the browser
dotnet test tests/PharmacyOrder.Selenium.Tests
```

Chrome must be installed; Selenium Manager downloads the matching chromedriver automatically.

## JMeter (5.6.3)

GUI — open the `.jmx`, change the **User Defined Variables** on the Test Plan node
(HOST, ports, THREADS, PATIENT_EMAIL/PASSWORD), press ▶, watch **View Results Tree** / **Summary Report**.

Headless:

```bash
JM=/c/apache-jmeter-5.6.3/bin/jmeter.bat
$JM -n -t tests/jmeter/register-load-test.jmx        -Jthreads=10 -Jloops=1 -l register.jtl
$JM -n -t tests/jmeter/checkout-orders-load-test.jmx -Jthreads=10 -Jloops=5 -l checkout.jtl
```

`register-load-test.jmx` posts a unique random user to `POST /api/auth/register` per sample
and asserts a 2xx. `checkout-orders-load-test.jmx` logs each virtual user in once,
extracts the JWT, then loops over `GET /api/orders/review`, `GET /api/orders`,
and a negative `POST /api/orders` with an empty cart (asserts 400).
