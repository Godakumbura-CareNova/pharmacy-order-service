# Pharmacy Order Service — QA test suite (GCN-25 → GCN-32)

All QA-authored tests for the pharmacy features, merged onto `dev`. Files named `*QaTests.cs`
sit next to the developers' own test files of the same name so both sets run together.

| Ticket | Feature | Unit / integration (xUnit) | Load (JMeter) |
|---|---|---|---|
| GCN-25 | Medicine catalogue browsing, search, filtering | `Unit/CatalogueServiceQaTests.cs`, `Unit/CatalogueControllerTests.cs` (P7), `Integration/CatalogueControllerIntegrationTests.cs` (+ `CatalogueApiFactory.cs`) | `performance/CatalogueLoadTest.jmx` |
| GCN-26 | Medicine details & quantity selection | `Unit/MedicineDetailsServiceQaTests.cs`, `Unit/MedicineDetailsControllerQaTests.cs`, `Integration/MedicineDetailsIntegrationTests.cs` | `performance/MedicineDetailsLoadTest.jmx` |
| GCN-27 | Medicine category management | `Unit/CategoryControllerTests.cs`, `Unit/MedicineControllerTests.cs`, additions in `Unit/CategoryServiceTests.cs` & `Unit/MedicineServiceTests.cs` | `performance/MedicineCategoryLoadTest.jmx` |
| GCN-28 | Stock management & low-stock tracking | `Unit/InventoryControllerTests.cs`, `Integration/InventoryControllerIntegrationTests.cs` (+ `InventoryApiFactory.cs`) | `performance/InventoryLoadTest.jmx` |
| GCN-29 | Prescription upload | `Unit/PrescriptionServiceQaTests.cs`, `Unit/PrescriptionControllerQaTests.cs`, `Unit/PrescriptionStorageQaTests.cs`, `Integration/PrescriptionControllerIntegrationTests.cs` (+ `PrescriptionApiFactory.cs`) | `performance/PrescriptionUploadLoadTest.jmx` (+ `sample-prescription.pdf`) |
| GCN-30 | Cart management | `Unit/CartServiceQaTests.cs`, `Unit/CartControllerQaTests.cs`, `Integration/CartControllerIntegrationTests.cs` (+ `CartApiFactory.cs`) | `performance/CartManagementLoadTest.jmx` |
| GCN-31 | Checkout & order tracking | `Unit/CheckoutOrderUnitTests.cs` (8), `Integration/CheckoutOrderTrackingIntegrationTests.cs` (8) | `jmeter/cart-checkout-flow.jmx`, `jmeter/checkout-orders-load-test.jmx`, `jmeter/register-load-test.jmx` |
| GCN-32 | Pharmacy order review | `Unit/PharmacyOrderReviewUnitTests.cs` (12), `Integration/PharmacyOrderReviewIntegrationTests.cs` (12) | `jmeter/pharmacy-order-review-flow.jmx` |

Browser (Selenium, Chrome) tests live in `PharmacyOrder.Selenium.Tests/`:
`CheckoutOrderTrackingUiTests.cs` (GCN-31, 4 tests) and `PharmacyOrderReviewUiTests.cs` (GCN-32, 4 tests).
Frontend Vitest suites are in the `frontend` repo (`src/pages/*.test.jsx`).

## Run everything (no services needed)

```bash
# from pharmacy-order-service/
dotnet test tests/PharmacyOrder.Api.Tests
```

Run one ticket's tests, e.g. GCN-29:

```bash
dotnet test tests/PharmacyOrder.Api.Tests --filter "FullyQualifiedName~Prescription"
```

The `*ApiFactory` integration tests boot the real API through `WebApplicationFactory` with EF
InMemory instead of MySQL and the Kafka background services removed, so no broker or database
is required.

## Selenium (GCN-31 / GCN-32)

Start the three apps first (separate terminals):

```bash
cd carenova-user-auth-service && dotnet run --project src/UserAuth.Api --launch-profile http     # :5133
cd pharmacy-order-service    && dotnet run --project src/PharmacyOrder.Api --launch-profile http # :5134
cd frontend                  && npm run dev                                                      # :3000
```

```bash
dotnet test tests/PharmacyOrder.Selenium.Tests
CARENOVA_HEADLESS=false dotnet test tests/PharmacyOrder.Selenium.Tests    # watch the browser
```

Accounts (override with `CARENOVA_PATIENT_EMAIL/PASSWORD`, `CARENOVA_STAFF_EMAIL/PASSWORD`, URL with `CARENOVA_UI_URL`):

| Role | Email | Password |
|---|---|---|
| Patient | `patient@carenova.test` | `Patient@123` |
| PharmacyStaff | `staff@carenova.test` | `Staff@123` |

Register both via the frontend, then as the seeded admin (`admin@carenova.org` / `Admin@12345`)
change `staff@carenova.test` to **PharmacyStaff**. The Selenium tests never change order or
prescription state.

## JMeter (5.6.3)

`performance/*.jmx` (GCN-25 → 30) are per-feature load plans. `jmeter/*.jmx` (GCN-31 / 32) are
end-to-end journeys — open one, adjust **User Defined Variables** on the Test Plan node (ports,
accounts, `MEDICINE_ID` must be a non-prescription medicine), press ▶ and read
**View Results Tree** / **Summary Report**.

- `cart-checkout-flow.jmx` — login → add to cart → GET cart → PUT quantity → review → place order → GET orders.
- `checkout-orders-load-test.jmx` — N patients hitting review / history / empty-checkout.
- `register-load-test.jmx` — unique random registrations against the auth service.
- `pharmacy-order-review-flow.jmx` — patient places an order → staff GET queue → PUT ReadyForCollection
  → PUT Rejected-without-reason (409) → PUT Collected → patient sees Collected.

Headless:

```bash
/c/apache-jmeter-5.6.3/bin/jmeter.bat -n -t tests/jmeter/pharmacy-order-review-flow.jmx -l review.jtl
```
