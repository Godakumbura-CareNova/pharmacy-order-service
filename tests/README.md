# GCN-32 Pharmacy Order Review — QA test suite

| Layer | File | Count | Needs running services? |
|---|---|---|---|
| Unit (xUnit) | `PharmacyOrder.Api.Tests/Unit/PharmacyOrderReviewUnitTests.cs` | 12 | No |
| Integration (xUnit + in-memory SQLite) | `PharmacyOrder.Api.Tests/Integration/PharmacyOrderReviewIntegrationTests.cs` | 12 | No |
| Vitest (React) | `../../frontend/src/pages/PharmacyOrderReview.test.jsx` | 4 | No |
| Selenium (xUnit + Chrome) | `PharmacyOrder.Selenium.Tests/PharmacyOrderReviewUiTests.cs` | 4 | **Yes** — frontend + auth + pharmacy API |
| JMeter | `jmeter/pharmacy-order-review-flow.jmx` | 13-step GET/PUT journey | **Yes** — auth + pharmacy API |

## Unit + integration

```bash
# from pharmacy-order-service/
dotnet test tests/PharmacyOrder.Api.Tests --filter "FullyQualifiedName~PharmacyOrderReviewUnitTests"
dotnet test tests/PharmacyOrder.Api.Tests --filter "FullyQualifiedName~PharmacyOrderReviewIntegrationTests"
```

## Vitest

```bash
# from frontend/
npx vitest run src/pages/PharmacyOrderReview.test.jsx
```

## Test accounts (one-time setup)

| Role | Email | Password |
|---|---|---|
| Patient | `patient@carenova.test` | `Patient@123` |
| PharmacyStaff | `staff@carenova.test` | `Staff@123` |

Register both through the frontend (or `POST /api/auth/register`), then log in to the
frontend as the seeded admin (`admin@carenova.org` / `Admin@12345`) → **Users** and change
`staff@carenova.test` to **PharmacyStaff**. You also need at least one **non-prescription**
medicine with stock (created via the staff *Medicines* / *Inventory* pages); note its id.

## Selenium

Start the three apps first (separate terminals):

```bash
cd carenova-user-auth-service && dotnet run --project src/UserAuth.Api --launch-profile http     # :5133
cd pharmacy-order-service    && dotnet run --project src/PharmacyOrder.Api --launch-profile http # :5134
cd frontend                  && npm run dev                                                      # :3000
```

```bash
# from pharmacy-order-service/
dotnet test tests/PharmacyOrder.Selenium.Tests
CARENOVA_HEADLESS=false dotnet test tests/PharmacyOrder.Selenium.Tests    # watch the browser
```

Override accounts with `CARENOVA_STAFF_EMAIL`, `CARENOVA_STAFF_PASSWORD`,
`CARENOVA_PATIENT_EMAIL`, `CARENOVA_PATIENT_PASSWORD`, and the URL with `CARENOVA_UI_URL`.
The tests never change order or prescription state.

## JMeter (5.6.3)

`pharmacy-order-review-flow.jmx` runs one journey (1 thread / 1 loop):

```
 1  PATIENT login                          →  TOKEN
 2  PATIENT POST /api/cart/items           (MEDICINE_ID, must be non-prescription)
 3  PATIENT GET  /api/orders/review
 4  PATIENT POST /api/orders               →  ORDER_ID, ORDER_NUMBER   (status Pending)
 5  STAFF   login                          →  TOKEN
 6  STAFF   GET  /api/staff/orders         queue contains ORDER_NUMBER
 7  STAFF   PUT  /api/staff/orders/{id}/status   ReadyForCollection   → 200
 8  STAFF   PUT  /api/staff/orders/{id}/status   Rejected, no reason  → 409 (asserted)
 9  STAFF   PUT  /api/staff/orders/{id}/status   Collected            → 200
10  STAFF   GET  /api/staff/orders         order has left the queue
11  PATIENT login again                    →  TOKEN
12  PATIENT GET  /api/orders/{id}          status Collected
13  PATIENT GET  /api/patient-notifications
```

GUI: open the file, check **User Defined Variables** on the Test Plan node
(ports, accounts, `MEDICINE_ID`), press ▶, then read **View Results Tree** — click a row,
**Response data** tab, dropdown **JSON**. Row 6 / 10 are the GET screenshots, row 7 / 9 the PUT ones.

Headless:

```bash
/c/apache-jmeter-5.6.3/bin/jmeter.bat -n -t tests/jmeter/pharmacy-order-review-flow.jmx -l review.jtl
```

Every run places and collects a new order. Step 13 only lists notifications when the Kafka
consumer is running; with `Kafka:Enabled=false` it returns an empty list (still 200).
