# GCN-28 Stock Management and Low-Stock Tracking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build staff batch inventory management, low-stock alerts, stock availability updates, and Kafka inventory events.

**Architecture:** `InventoryService` owns batch validation, recalculates `Medicine.StockQuantity`, and writes `Stock`, `Medicine`, and outbox records in one transaction. A React staff Inventory page consumes staff-only endpoints and follows the GCN-27 dashboard style.

**Tech Stack:** .NET 8, EF Core, MySQL, Confluent.Kafka, React, Vite, Axios.

**Spec:** `docs/superpowers/specs/2026-09-12-gcn-28-stock-management-design.md`

## Global Constraints

- Require `PharmacyStaff` for every inventory endpoint.
- Publish only GCN-28 events to `carenova.inventory.events.v1`.
- Use `Kafka:LowStockThreshold`, default `10`.
- Never add credentials or JWT secrets to tracked files.
- Use plain commit messages without `feat:`, `fix:`, or `chore:` prefixes.

---

### Task 1: Inventory contracts, batch constraint, and events

**Files:**
- Create: `src/PharmacyOrder.Api/DTOs/Inventory/StockBatchRequestDto.cs`
- Create: `src/PharmacyOrder.Api/DTOs/Inventory/StockBatchResponseDto.cs`
- Create: `src/PharmacyOrder.Api/DTOs/Inventory/MedicineInventoryResponseDto.cs`
- Create: `src/PharmacyOrder.Api/Events/InventoryEvent.cs`
- Modify: `src/PharmacyOrder.Api/Events/EventTypes.cs`
- Modify: `src/PharmacyOrder.Api/Data/AppDbContext.cs`
- Create: EF-generated migration named `AddInventoryBatchConstraint` under `src/PharmacyOrder.Api/Migrations/`
- Test: `tests/PharmacyOrder.Api.Tests/Unit/InventoryEventTests.cs`

**Interfaces:**

```csharp
public record InventoryEvent(Guid EventId, string EventType, DateTime OccurredAtUtc,
    int MedicineId, string EntityType, JsonElement Data);
public const string InventoryTopic = "carenova.inventory.events.v1";
public const string InventoryUpdated = "InventoryUpdated";
public const string LowStockDetected = "LowStockDetected";
public const string MedicineOutOfStock = "MedicineOutOfStock";
```

- [ ] Write a failing test that constructs an `InventoryEvent` with `InventoryUpdated` and medicine ID `14`.
- [ ] Run `dotnet test tests/PharmacyOrder.Api.Tests/PharmacyOrder.Api.Tests.csproj --filter "FullyQualifiedName~InventoryEventTests"`; expect failure because the contract does not exist.
- [ ] Add DTOs, event constants, and a unique EF Core index on `(MedicineId, BatchNumber)`, then create the migration.
- [ ] Re-run the focused test and `dotnet build src/PharmacyOrder.Api/PharmacyOrder.Api.csproj`; expect both pass.
- [ ] Commit: `git commit -m "add inventory contracts and event types"`.

### Task 2: Inventory service, API, availability, and Kafka outbox

**Files:**
- Create: `src/PharmacyOrder.Api/Services/Interfaces/IInventoryService.cs`
- Create: `src/PharmacyOrder.Api/Services/InventoryService.cs`
- Create: `src/PharmacyOrder.Api/Controllers/InventoryController.cs`
- Modify: `src/PharmacyOrder.Api/Program.cs`
- Modify: `src/PharmacyOrder.Api/Services/KafkaConsumerService.cs`
- Test: `tests/PharmacyOrder.Api.Tests/Unit/InventoryServiceTests.cs`

**Interfaces:**

```csharp
Task<IReadOnlyList<MedicineInventoryResponseDto>> GetInventoryAsync(bool lowStockOnly);
Task<IReadOnlyList<StockBatchResponseDto>> GetBatchesAsync(int medicineId);
Task<StockBatchResponseDto> CreateBatchAsync(StockBatchRequestDto request);
Task<StockBatchResponseDto> UpdateBatchAsync(int id, StockBatchRequestDto request);
```

- [ ] Write failing tests that: create batch `PAR-001` with quantity `8`, assert `Medicine.StockQuantity == 8`, and assert queued `InventoryUpdated` and `LowStockDetected` events; reject a negative quantity; reject expiry before manufacturing.
- [ ] Run `dotnet test tests/PharmacyOrder.Api.Tests/PharmacyOrder.Api.Tests.csproj --filter "FullyQualifiedName~InventoryServiceTests"`; expect failure because the service does not exist.
- [ ] Implement validation, create/update batches, calculate total with `Stocks.Where(s => s.MedicineId == medicineId).Sum(s => s.Quantity)`, update `Medicine.StockQuantity`, and save database changes plus `InventoryUpdated`, conditional `LowStockDetected`, and conditional `MedicineOutOfStock` outbox records in one transaction.
- [ ] Add staff endpoints: `GET /api/staff/inventory?lowStockOnly=`, `GET /api/staff/inventory/{medicineId}/batches`, `POST /api/staff/inventory/batches`, and `PUT /api/staff/inventory/batches/{id}`. Register the service and subscribe the Kafka consumer to both medicine and inventory topics.
- [ ] Re-run focused tests and API build; expect pass.
- [ ] Commit: `git commit -m "add stock management and inventory events"`.

### Task 3: Pharmacy Staff Inventory screen

**Files:**
- Create: `src/api/inventoryApi.js`
- Create: `src/pages/InventoryManagementPage.jsx`
- Create: `src/pages/InventoryManagementPage.test.jsx`
- Modify: `src/components/DashboardLayout.jsx`
- Modify: `src/routes/AppRoutes.jsx`
- Modify: `src/pages/medicineManagement.css`

**Interface:**

```javascript
export const inventoryApi = {
  list: (lowStockOnly = false) => pharmacyApi.get(`/staff/inventory?lowStockOnly=${lowStockOnly}`),
  batches: (medicineId) => pharmacyApi.get(`/staff/inventory/${medicineId}/batches`),
  createBatch: (payload) => pharmacyApi.post('/staff/inventory/batches', payload),
  updateBatch: (id, payload) => pharmacyApi.put(`/staff/inventory/batches/${id}`, payload),
};
```

- [ ] Write a failing page test that mocks `inventoryApi.list()` with Paracetamol at quantity `4`, then expects `Paracetamol` and `Low stock` to render.
- [ ] Run `npm test -- --run src/pages/InventoryManagementPage.test.jsx`; expect failure because the page does not exist.
- [ ] Add the protected `/pharmacy/inventory` route and sidebar item. Build a batch add/edit form with medicine, batch, quantity, prices, manufacturing date, expiry date; show totals, availability status, low-stock-only filter, and batch details using the existing red/pale-pink cards.
- [ ] Run the page test and `npm run build`; expect pass.
- [ ] Commit: `git commit -m "add pharmacy inventory management screen"`.

### Task 4: Migration and local Kafka developer run-through

**Files:**
- Modify: `docker-compose.kafka.yml` only if topic setup requires it.
- Modify: `docs/superpowers/specs/2026-09-12-gcn-28-stock-management-design.md` only if implementation exposes a specification correction.

- [ ] Run `dotnet ef database update --project src/PharmacyOrder.Api`; expect the batch-constraint migration to apply.
- [ ] Start Kafka and create `carenova.inventory.events.v1` in Kafka UI with one partition and replication factor one.
- [ ] Create a batch at quantity `8`; confirm low stock and `InventoryUpdated` plus `LowStockDetected`. Update it to `0`; confirm out of stock and `MedicineOutOfStock`.
- [ ] Run `dotnet test tests/PharmacyOrder.Api.Tests/PharmacyOrder.Api.Tests.csproj`, `npm run build`, and `git status --short`.
- [ ] Commit only required documentation updates: `git commit -m "update GCN-28 implementation notes"`.
