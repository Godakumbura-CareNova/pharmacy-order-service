# GCN-32 Pharmacy Review and Order Status Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable Pharmacy Staff to decide pending prescriptions, manage pickup-order status, and publish the agreed GCN-32 Kafka events.

**Architecture:** Add focused staff-only services and controllers that reuse the existing private prescription storage, order transition, outbox, and Kafka publisher. Add staff queue pages that call those APIs and reload after every decision. Patient pages continue to obtain decisions by reading their existing endpoints.

**Tech Stack:** ASP.NET Core 8, EF Core/MySQL, Kafka/Confluent.Kafka, React, Vite, Axios, Vitest, React Testing Library.

**Spec:** `docs/superpowers/specs/2026-09-13-gcn-32-pharmacy-order-review-design.md`

## Global Constraints

- All staff APIs require the existing `PharmacyStaff` policy.
- Prescription files remain private and outside `wwwroot`.
- Rejecting a prescription or order requires a non-empty reason no longer than 1000 characters.
- Only `PendingVerification` prescriptions may be decided.
- Only `Pending -> ReadyForCollection`, `Pending -> Rejected`, and `ReadyForCollection -> Collected` order transitions are allowed.
- Every successful staff decision writes a Kafka outbox entry in the same transaction; event payloads contain no patient identity or file path.
- Kafka consumer processing remains idempotent by EventId.
- Do not start local servers, Docker, or apply database migrations; the user runs them manually.

---

## File structure

### Backend

- Create `src/PharmacyOrder.Api/DTOs/Staff/StaffPrescriptionDto.cs` — queue response and decision request.
- Create `src/PharmacyOrder.Api/DTOs/Staff/StaffOrderDto.cs` — staff order response and status request.
- Create `src/PharmacyOrder.Api/Services/StaffPrescriptionService.cs` — secure prescription review and transactional outbox event.
- Create `src/PharmacyOrder.Api/Services/StaffOrderService.cs` — staff-facing status operation that delegates valid stock restoration and saves status events.
- Create `src/PharmacyOrder.Api/Controllers/StaffPrescriptionController.cs` — staff-only queue, file, and decision endpoints.
- Create `src/PharmacyOrder.Api/Controllers/StaffOrderController.cs` — staff-only order queue and status endpoint.
- Modify `src/PharmacyOrder.Api/Models/Prescription.cs`, `src/PharmacyOrder.Api/Data/AppDbContext.cs`, and create an EF migration — persist rejection reason and decision timestamp.
- Modify `src/PharmacyOrder.Api/Events/EventTypes.cs`, `src/PharmacyOrder.Api/Services/OrderService.cs`, and `src/PharmacyOrder.Api/Services/KafkaNotificationHandler.cs` — add GCN-32 constants, outbox helper, and useful staff notification titles.
- Modify `src/PharmacyOrder.Api/Services/Interfaces/IPrescriptionService.cs` and `src/PharmacyOrder.Api/Program.cs` only where required for the new staff-specific services.
- Create `tests/PharmacyOrder.Api.Tests/Integration/StaffPrescriptionTests.cs` and `tests/PharmacyOrder.Api.Tests/Integration/StaffOrderTests.cs`.
- Extend `tests/PharmacyOrder.Api.Tests/Integration/PickupOrderHttpTests.cs` for staff authorization and HTTP responses.

### Frontend

- Create `src/api/staffPrescriptionApi.js` and `src/api/staffOrderApi.js` — staff API clients.
- Create `src/pages/PharmacyPrescriptionReviewPage.jsx` and `src/pages/PharmacyPrescriptionReviewPage.test.jsx`.
- Create `src/pages/PharmacyOrderManagementPage.jsx` and `src/pages/PharmacyOrderManagementPage.test.jsx`.
- Create `src/pages/pharmacyStaffQueues.css` — shared queue layout, status badges, action rows, and inline rejection form.
- Modify `src/routes/AppRoutes.jsx`, `src/components/DashboardLayout.jsx`, and `src/pages/PharmacyStaffDashboard.jsx` — routes, enabled staff navigation, and links from dashboard cards.
- Modify `src/pages/PatientPrescriptionsPage.jsx` and its test — display a rejection reason if returned by the existing patient endpoint.

## Task 1: Prescription decision model and private staff service

**Files:**
- Create: `src/PharmacyOrder.Api/DTOs/Staff/StaffPrescriptionDto.cs`
- Create: `src/PharmacyOrder.Api/Services/StaffPrescriptionService.cs`
- Modify: `src/PharmacyOrder.Api/Models/Prescription.cs`
- Modify: `src/PharmacyOrder.Api/Data/AppDbContext.cs`
- Create: `src/PharmacyOrder.Api/Migrations/<timestamp>_AddPrescriptionDecisionDetails.cs`
- Test: `tests/PharmacyOrder.Api.Tests/Integration/StaffPrescriptionTests.cs`

**Interfaces:**
- Produces `Task<IReadOnlyList<StaffPrescriptionDto>> ListPendingAsync(CancellationToken)`.
- Produces `Task<StaffPrescriptionDto> DecideAsync(int id, PrescriptionDecisionRequest request, CancellationToken)`.
- `PrescriptionDecisionRequest` contains `string Decision` and nullable `string? Reason`.
- `StaffPrescriptionDto` contains ID, original file name, medicine ID/name, quantity, status, uploaded time, decision reason, and reviewed time.

- [ ] **Step 1: Write failing integration tests**

```csharp
[Fact]
public async Task Verify_pending_prescription_updates_status_and_writes_private_event()
{
    var result = await fixture.StaffPrescriptions.DecideAsync(1,
        new PrescriptionDecisionRequest { Decision = "Verified" });

    Assert.Equal("Verified", result.Status);
    var eventEntry = Assert.Single(await fixture.Db.EventOutboxEntries
        .Where(entry => entry.EventType == EventTypes.PrescriptionVerified).ToListAsync());
    Assert.DoesNotContain(fixture.Patient.ToString(), eventEntry.PayloadJson);
    Assert.DoesNotContain("FilePath", eventEntry.PayloadJson);
}

[Theory]
[InlineData("Rejected", "")]
[InlineData("Invalid", "reason")]
public async Task Invalid_prescription_decisions_are_rejected(string decision, string reason)
{
    await Assert.ThrowsAsync<ArgumentException>(() => fixture.StaffPrescriptions.DecideAsync(1,
        new PrescriptionDecisionRequest { Decision = decision, Reason = reason }));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PharmacyOrder.Api.Tests --no-restore --filter StaffPrescriptionTests --verbosity quiet`

Expected: FAIL because the staff prescription DTO and service do not exist.

- [ ] **Step 3: Add the persistent decision fields and migration**

Add nullable `DecisionReason` and `ReviewedAtUtc` to `Prescription`. Configure maximum length 1000 for the reason. Generate a migration that adds nullable columns so existing pending uploads remain valid.

```csharp
public string? DecisionReason { get; set; }
public DateTime? ReviewedAtUtc { get; set; }
```

- [ ] **Step 4: Implement the minimal staff prescription service**

Load pending records with their Medicine, validate `Verified` or `Rejected`, validate rejection reason, then begin a serializable transaction. Update the record, set `IsVerified`, `DecisionReason`, and `ReviewedAtUtc`; save it and add one outbox entry for `PrescriptionVerified` keyed by the prescription ID. Save again and commit. A non-pending record throws `InvalidOperationException` so the controller returns 409.

```csharp
if (prescription.Status != "PendingVerification")
    throw new InvalidOperationException("This prescription has already been decided.");

var verified = request.Decision == "Verified";
prescription.Status = verified ? "Verified" : "Rejected";
prescription.IsVerified = verified;
prescription.DecisionReason = verified ? null : request.Reason!.Trim();
prescription.ReviewedAtUtc = DateTime.UtcNow;
```

- [ ] **Step 5: Run the task tests to verify they pass**

Run: `dotnet test tests/PharmacyOrder.Api.Tests --no-restore --filter StaffPrescriptionTests --verbosity quiet`

Expected: PASS, including pending queue ordering, verify, reject, duplicate decision, and event-payload privacy assertions.

- [ ] **Step 6: Commit the task**

```powershell
git add src/PharmacyOrder.Api/DTOs/Staff src/PharmacyOrder.Api/Models/Prescription.cs src/PharmacyOrder.Api/Data/AppDbContext.cs src/PharmacyOrder.Api/Migrations tests/PharmacyOrder.Api.Tests/Integration/StaffPrescriptionTests.cs
git commit -m "add pharmacy prescription decisions"
```

## Task 2: Staff prescription HTTP endpoints and authorization

**Files:**
- Create: `src/PharmacyOrder.Api/Controllers/StaffPrescriptionController.cs`
- Modify: `src/PharmacyOrder.Api/Program.cs`
- Test: `tests/PharmacyOrder.Api.Tests/Integration/PickupOrderHttpTests.cs`

**Interfaces:**
- Consumes `StaffPrescriptionService` from Task 1.
- Produces `GET /api/staff/prescriptions`, `GET /api/staff/prescriptions/{id}/file`, and `PUT /api/staff/prescriptions/{id}/decision`.

- [ ] **Step 1: Write failing HTTP tests**

```csharp
[Fact]
public async Task Pharmacy_staff_can_verify_a_pending_prescription()
{
    var response = await staffClient.PutAsJsonAsync("/api/staff/prescriptions/1/decision",
        new { decision = "Verified" });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}

[Fact]
public async Task Patient_cannot_access_staff_prescription_queue()
{
    var response = await patientClient.GetAsync("/api/staff/prescriptions");
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}
```

- [ ] **Step 2: Run the HTTP tests to verify they fail**

Run: `dotnet test tests/PharmacyOrder.Api.Tests --no-restore --filter "FullyQualifiedName~StaffPrescription" --verbosity quiet`

Expected: FAIL because the staff routes are not mapped.

- [ ] **Step 3: Implement the controller**

Use `[Authorize(Policy = AuthorizationPolicies.PharmacyStaff)]` and `[Route("api/staff/prescriptions")]`. Reuse `FileExtensionContentTypeProvider` for the private download response. Map validation errors to 400, missing IDs/files to 404, and duplicate decisions to 409.

- [ ] **Step 4: Register and test the service**

Register `StaffPrescriptionService` as scoped in `Program.cs`, update the test host service collection, and ensure no patient role can access any staff route.

- [ ] **Step 5: Run the task tests to verify they pass**

Run: `dotnet test tests/PharmacyOrder.Api.Tests --no-restore --filter "FullyQualifiedName~StaffPrescription" --verbosity quiet`

Expected: PASS for 401, 403, 404, 400, 409, list, decision, and secure download behavior.

- [ ] **Step 6: Commit the task**

```powershell
git add src/PharmacyOrder.Api/Controllers/StaffPrescriptionController.cs src/PharmacyOrder.Api/Program.cs tests/PharmacyOrder.Api.Tests/Integration/PickupOrderHttpTests.cs
git commit -m "add staff prescription review API"
```

## Task 3: Staff order transitions and Kafka status events

**Files:**
- Create: `src/PharmacyOrder.Api/DTOs/Staff/StaffOrderDto.cs`
- Create: `src/PharmacyOrder.Api/Services/StaffOrderService.cs`
- Create: `src/PharmacyOrder.Api/Controllers/StaffOrderController.cs`
- Modify: `src/PharmacyOrder.Api/Events/EventTypes.cs`
- Modify: `src/PharmacyOrder.Api/Services/OrderService.cs`
- Modify: `src/PharmacyOrder.Api/Services/KafkaNotificationHandler.cs`
- Test: `tests/PharmacyOrder.Api.Tests/Integration/StaffOrderTests.cs`
- Test: `tests/PharmacyOrder.Api.Tests/Integration/PickupOrderTests.cs`

**Interfaces:**
- Produces `Task<IReadOnlyList<StaffOrderDto>> ListActiveAsync(CancellationToken)`.
- Produces `Task<OrderResponseDto> UpdateStatusAsync(int id, StaffOrderStatusRequest request, CancellationToken)`.
- `StaffOrderStatusRequest` contains `string Status` and nullable `string? Reason`.

- [ ] **Step 1: Write failing integration tests**

```csharp
[Fact]
public async Task Marking_order_ready_writes_one_ready_event()
{
    var order = await fixture.PlacePendingOrderAsync();
    await fixture.StaffOrders.UpdateStatusAsync(order.Id,
        new StaffOrderStatusRequest { Status = "ReadyForCollection" });

    var entry = Assert.Single(await fixture.Db.EventOutboxEntries
        .Where(e => e.EventType == EventTypes.OrderReadyForCollection).ToListAsync());
    Assert.Equal(order.Id.ToString(), entry.EventKey);
}

[Fact]
public async Task Rejected_order_restores_stock_once_and_writes_one_rejection_event()
{
    var order = await fixture.PlacePendingOrderAsync();
    await fixture.StaffOrders.UpdateStatusAsync(order.Id,
        new StaffOrderStatusRequest { Status = "Rejected", Reason = "Damaged stock" });

    Assert.Equal(10, (await fixture.Db.Medicines.FindAsync(1))!.StockQuantity);
    Assert.Single(await fixture.Db.EventOutboxEntries.Where(e => e.EventType == EventTypes.OrderRejected).ToListAsync());
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/PharmacyOrder.Api.Tests --no-restore --filter StaffOrderTests --verbosity quiet`

Expected: FAIL because no staff order service or status event exists.

- [ ] **Step 3: Add event constants and an outbox helper**

Add `PrescriptionVerified`, `OrderReadyForCollection`, `OrderRejected`, and `OrderCollected` to `EventTypes`. Add a private `AddOrderEvent(Order order, string eventType)` helper to `OrderService`; it serializes an `OrderEvent` with only `{ Status, RejectionReason }` and creates an order-topic outbox entry keyed by the decimal-free order ID.

- [ ] **Step 4: Extend `TransitionAsync` transactionally**

Keep existing validation and stock restoration behavior. After the state update, call `AddOrderEvent` once, save it in the same transaction, and commit. A repeated same-status request returns the current order without creating a second event. Correct the earlier transition rule so `ReadyForCollection -> Rejected` is forbidden, matching the design.

- [ ] **Step 5: Implement staff queue and endpoint**

Load only `Pending` and `ReadyForCollection` orders with snapshot items. The controller uses the PharmacyStaff policy and maps invalid requests to 400, invalid transitions to 409, and missing orders to 404.

- [ ] **Step 6: Run the task tests to verify they pass**

Run: `dotnet test tests/PharmacyOrder.Api.Tests --no-restore --filter "StaffOrderTests|PickupOrderTests" --verbosity quiet`

Expected: PASS for all allowed transitions, blocked transitions, one-time stock restoration, order event records, duplicate attempts, and notification title/message mapping.

- [ ] **Step 7: Commit the task**

```powershell
git add src/PharmacyOrder.Api/DTOs/Staff src/PharmacyOrder.Api/Services/StaffOrderService.cs src/PharmacyOrder.Api/Controllers/StaffOrderController.cs src/PharmacyOrder.Api/Events/EventTypes.cs src/PharmacyOrder.Api/Services/OrderService.cs src/PharmacyOrder.Api/Services/KafkaNotificationHandler.cs tests/PharmacyOrder.Api.Tests/Integration
git commit -m "add pharmacy order status management"
```

## Task 4: Pharmacy Staff frontend queues

**Files:**
- Create: `src/api/staffPrescriptionApi.js`
- Create: `src/api/staffOrderApi.js`
- Create: `src/pages/PharmacyPrescriptionReviewPage.jsx`
- Create: `src/pages/PharmacyPrescriptionReviewPage.test.jsx`
- Create: `src/pages/PharmacyOrderManagementPage.jsx`
- Create: `src/pages/PharmacyOrderManagementPage.test.jsx`
- Create: `src/pages/pharmacyStaffQueues.css`
- Modify: `src/routes/AppRoutes.jsx`
- Modify: `src/components/DashboardLayout.jsx`
- Modify: `src/pages/PharmacyStaffDashboard.jsx`

**Interfaces:**
- `staffPrescriptionApi.listPending()`, `.download(id)`, and `.decide(id, payload)` call `/staff/prescriptions`.
- `staffOrderApi.listActive()` and `.updateStatus(id, payload)` call `/staff/orders`.
- Routes are `/pharmacy/prescriptions` and `/pharmacy/orders`, restricted to PharmacyStaff.

- [ ] **Step 1: Write failing page tests**

```jsx
it('verifies a pending prescription and reloads the queue', async () => {
  listPending.mockResolvedValue({ data: [pendingPrescription] });
  decide.mockResolvedValue({ data: { ...pendingPrescription, status: 'Verified' } });
  render(<MemoryRouter><PharmacyPrescriptionReviewPage /></MemoryRouter>);

  await userEvent.click(await screen.findByRole('button', { name: 'Verify' }));
  await waitFor(() => expect(decide).toHaveBeenCalledWith(7, { decision: 'Verified' }));
  expect(listPending).toHaveBeenCalledTimes(2);
});
```

```jsx
it('requires a reason before rejecting an order', async () => {
  render(<MemoryRouter><PharmacyOrderManagementPage /></MemoryRouter>);
  await userEvent.click(await screen.findByRole('button', { name: 'Reject order CN-000003' }));
  await userEvent.click(screen.getByRole('button', { name: 'Confirm rejection' }));
  expect(updateStatus).not.toHaveBeenCalled();
  expect(screen.getByText('Provide a rejection reason.')).toBeInTheDocument();
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npm test -- --run src/pages/PharmacyPrescriptionReviewPage.test.jsx src/pages/PharmacyOrderManagementPage.test.jsx`

Expected: FAIL because the staff pages and clients do not exist.

- [ ] **Step 3: Implement API clients and prescription queue**

Use the configured `pharmacyApi` client. Render loading, empty, retry, open-file, Verify, and inline Reject-with-reason states. On success call `load()`; do not optimistically overwrite the queue.

- [ ] **Step 4: Implement order queue**

Group cards by Pending and Ready for collection. Offer Ready and Reject for Pending; offer Collected only for ReadyForCollection. Include order number, time, line quantities, total, and displayed rejection reason when returned.

- [ ] **Step 5: Wire routes and navigation**

Replace the disabled staff `Medicine Orders` and `Prescriptions` sidebar items with enabled links. Add staff-only routes and make the dashboard cards link to their queues. Preserve all existing patient and management routes.

- [ ] **Step 6: Run the focused frontend tests to verify they pass**

Run: `npm test -- --run src/pages/PharmacyPrescriptionReviewPage.test.jsx src/pages/PharmacyOrderManagementPage.test.jsx`

Expected: PASS for loading, successful actions, required rejection reason, reload, and error display.

- [ ] **Step 7: Commit the task**

```powershell
git add src/api src/pages/PharmacyPrescriptionReviewPage.jsx src/pages/PharmacyPrescriptionReviewPage.test.jsx src/pages/PharmacyOrderManagementPage.jsx src/pages/PharmacyOrderManagementPage.test.jsx src/pages/pharmacyStaffQueues.css src/routes/AppRoutes.jsx src/components/DashboardLayout.jsx src/pages/PharmacyStaffDashboard.jsx
git commit -m "add pharmacy review queues"
```

## Task 5: Patient decision visibility and final verification

**Files:**
- Modify: `src/PharmacyOrder.Api/DTOs/Prescription/PrescriptionResponseDto.cs`
- Modify: `src/PharmacyOrder.Api/Services/PrescriptionService.cs`
- Modify: `src/pages/PatientPrescriptionsPage.jsx`
- Modify: `src/pages/PatientPrescriptionsPage.test.jsx`
- Test: `tests/PharmacyOrder.Api.Tests/Integration/StaffPrescriptionTests.cs`

**Interfaces:**
- Existing patient `GET /api/prescriptions` adds nullable `decisionReason` and `reviewedAtUtc` fields.
- Existing patient order endpoints already return `status` and `rejectionReason` from GCN-31.

- [ ] **Step 1: Write failing patient visibility tests**

```csharp
[Fact]
public async Task Patient_history_returns_rejection_reason_without_file_path()
{
    var response = await fixture.Prescriptions.GetForPatientAsync(fixture.Patient);
    var prescription = Assert.Single(response);
    Assert.Equal("Image is unreadable", prescription.DecisionReason);
}
```

```jsx
it('shows the pharmacy rejection reason for a rejected prescription', async () => {
  list.mockResolvedValue({ data: [{ ...rejectedPrescription, decisionReason: 'Image is unreadable' }] });
  render(<MemoryRouter><PatientPrescriptionsPage /></MemoryRouter>);
  expect(await screen.findByText('Image is unreadable')).toBeInTheDocument();
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run backend: `dotnet test tests/PharmacyOrder.Api.Tests --no-restore --filter Patient_history_returns_rejection_reason --verbosity quiet`

Run frontend: `npm test -- --run src/pages/PatientPrescriptionsPage.test.jsx`

Expected: FAIL because the DTO and UI do not expose the reason.

- [ ] **Step 3: Implement patient-safe decision visibility**

Add only decision reason and reviewed timestamp to the patient prescription DTO/projection. Render an accessible `Pharmacy note:` line below a rejected prescription status. Never return storage paths, EventIds, or staff identity.

- [ ] **Step 4: Run all backend tests and the frontend production build**

Run backend: `dotnet test tests/PharmacyOrder.Api.Tests --no-restore --verbosity quiet`

Run frontend: `npm run build`

Expected: both pass. Record any external package metadata warning separately from test or build results.

- [ ] **Step 5: Run diff and migration checks**

Run: `git diff --check`

Expected: no whitespace errors. Review the generated migration to confirm it contains only the two nullable prescription-decision columns.

- [ ] **Step 6: Commit the task**

```powershell
git add src/PharmacyOrder.Api/DTOs/Prescription src/PharmacyOrder.Api/Services/PrescriptionService.cs tests/PharmacyOrder.Api.Tests/Integration/StaffPrescriptionTests.cs
git commit -m "show prescription review decisions to patients"
```

## Manual verification after implementation

The user starts the authentication API, Pharmacy API, frontend, and Kafka themselves. Do not start them automatically.

1. Sign in as Pharmacy Staff and open **Prescriptions**. Verify a pending Amoxicillin prescription; it disappears from the pending queue.
2. Sign in as the patient. Confirm the same prescription shows `Verified`; check out its ready cart item.
3. Sign back in as Pharmacy Staff. Open **Medicine Orders**, mark the new Pending order `Ready for Collection`, then `Collected`.
4. Create another order and reject it with a reason. Confirm the patient sees the reason and stock is restored once.
5. In Kafka UI, inspect `carenova.order.events.v1` for `PrescriptionVerified`, `OrderReadyForCollection`, `OrderRejected`, and `OrderCollected`, each with a non-sensitive payload and matching aggregate/key.
6. In MySQL Workbench, confirm one EventOutbox entry and at most one StaffNotifications row per EventId.
