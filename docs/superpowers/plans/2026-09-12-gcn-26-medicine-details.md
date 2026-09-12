# GCN-26 Medicine Details and Quantity Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Patients view one available medicine, select a valid quantity, and see the total price before GCN-29.

**Architecture:** Extend the separate Patient catalogue API with an active, in-stock medicine detail endpoint. Add a Patient React details route that receives the catalogue navigation, constrains quantity using stock, and changes its main action by prescription requirement.

**Tech Stack:** ASP.NET Core 8, EF Core, xUnit, React, React Router, Vitest.

**Spec:** `docs/superpowers/specs/2026-09-12-gcn-26-medicine-details-design.md`

## Global Constraints

- Patient-only route and API.
- Do not expose inactive or zero-stock medicines for ordering.
- Quantity is at least 1 and at most current stock.
- Do not create carts or Kafka events in this story.

---

### Task 1: Detail API

**Files:**
- Modify: `src/PharmacyOrder.Api/Services/Interfaces/ICatalogueService.cs`
- Modify: `src/PharmacyOrder.Api/Services/CatalogueService.cs`
- Modify: `src/PharmacyOrder.Api/Controllers/CatalogueController.cs`
- Test: `tests/PharmacyOrder.Api.Tests/Unit/CatalogueServiceTests.cs`

- [ ] Write a failing test that seeds active stock 5 and asserts `GetMedicineAsync(id)` returns it, then another that asserts a zero-stock medicine throws `KeyNotFoundException`.
- [ ] Run `dotnet test tests/PharmacyOrder.Api.Tests/PharmacyOrder.Api.Tests.csproj --filter FullyQualifiedName~CatalogueServiceTests` and confirm the method is missing.
- [ ] Add `Task<CatalogueMedicineResponseDto> GetMedicineAsync(int id)`; query `IsActive && StockQuantity > 0`, map with the existing catalogue mapper, and add `GET /api/catalogue/medicines/{id}`.
- [ ] Run the focused tests and `dotnet build src/PharmacyOrder.Api/PharmacyOrder.Api.csproj`.
- [ ] Commit with `add patient medicine details API`.

### Task 2: Details page

**Files:**
- Modify: `src/api/catalogueApi.js`
- Create: `src/pages/PatientMedicineDetailsPage.jsx`
- Create: `src/pages/patientMedicineDetails.css`
- Modify: `src/pages/PatientMedicineCataloguePage.jsx`
- Modify: `src/routes/AppRoutes.jsx`
- Test: `src/pages/PatientMedicineDetailsPage.test.jsx`

- [ ] Write a failing UI test that supplies stock 5, clicks plus five times, and asserts quantity remains 5 and total is unit price times 5. Write a second test asserting the prescription button says `Continue to prescription upload`.
- [ ] Run `npm run test:run -- PatientMedicineDetailsPage.test.jsx` and confirm the page is missing.
- [ ] Add `catalogueApi.getMedicine(id)`. Add the Patient-protected detail route. Navigate catalogue cards to it only when available. Implement the page with medicine details, quantity +/- buttons, total, stock-bound message, and prescription-dependent button text. Preserve selected medicine and quantity in navigation state for GCN-29.
- [ ] Run the page test and `npm run build`.
- [ ] Commit with `add patient medicine details page`.

### Task 3: Developer run-through

- [ ] Restart the Pharmacy API, sign in as Patient, open an in-stock catalogue medicine, select quantities one through stock maximum, and verify the total and prescription action label.
- [ ] Run `git status --short` in both repositories and commit only a verified fix if required.
