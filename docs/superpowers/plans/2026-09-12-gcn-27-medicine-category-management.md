# GCN-27 Medicine and Category Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver Pharmacy Staff medicine and category management with secure staff-only APIs, image support, medicine deactivation, and Kafka medicine/category events.

**Architecture:** The Pharmacy Order API remains the single ASP.NET Core service. Staff HTTP requests validate a CareNova JWT and update MySQL data; the same database transaction creates an outbox entry. A hosted outbox publisher sends serialized events to Kafka, and a hosted consumer writes one staff notification per event.

**Tech Stack:** React 18, Vite, React Router 6, Axios, ASP.NET Core 8, EF Core 8, MySQL, FluentValidation, Confluent.Kafka, Docker Compose, Apache Kafka in KRaft mode.

**Spec:** `docs/superpowers/specs/2026-09-12-kafka-event-design.md`

## Global Constraints

- Use Kafka only for GCN-27, GCN-28, GCN-31, and GCN-32; this task implements only the GCN-27 event types.
- Use `carenova.medicine.events.v1` for medicine and category events.
- Keep the database as the source of truth; write an outbox record in the same transaction as the business change.
- Never commit the JWT signing secret; obtain it through .NET user secrets or `JwtSettings__Secret` environment variable.
- Protect all staff create, update, deactivate, and category-delete APIs with the `PharmacyStaff` policy.
- Keep the existing CareNova red, pale-pink, rounded-card dashboard theme.

---

## File Structure

### Backend

- `src/PharmacyOrder.Api/Config/KafkaSettings.cs`: typed broker, group, retry, and threshold configuration.
- `src/PharmacyOrder.Api/Models/EventOutbox.cs`: durable unpublished-event record.
- `src/PharmacyOrder.Api/Models/StaffNotification.cs`: idempotent consumer result for staff-visible events.
- `src/PharmacyOrder.Api/Events/MedicineEvent.cs`: immutable event contract for medicine and category changes.
- `src/PharmacyOrder.Api/Events/EventTypes.cs`: event type constants and topic constant.
- `src/PharmacyOrder.Api/Services/KafkaEventProducer.cs`: publishes an outbox event to Kafka.
- `src/PharmacyOrder.Api/Services/OutboxPublisherService.cs`: hosted worker that publishes pending outbox records.
- `src/PharmacyOrder.Api/Services/KafkaConsumerService.cs`: hosted worker that turns consumed events into staff notifications.
- `src/PharmacyOrder.Api/Services/CategoryService.cs`: category business rules and outbox creation.
- `src/PharmacyOrder.Api/Services/MedicineService.cs`: medicine business rules, image storage, deactivation, and outbox creation.
- `src/PharmacyOrder.Api/Controllers/CategoryController.cs`: staff category API.
- `src/PharmacyOrder.Api/Controllers/MedicineController.cs`: staff medicine API and image upload API.
- `src/PharmacyOrder.Api/Data/AppDbContext.cs`: entity sets, indexes, relationships, and transaction support.
- `src/PharmacyOrder.Api/Program.cs`: JWT validation, CORS, service registration, and hosted workers.
- `docker-compose.kafka.yml`: local Kafka broker and Kafka UI.

### Frontend

- `src/api/pharmacyApi.js`: Axios client for `http://localhost:5134/api` with the existing bearer token.
- `src/api/categoryApi.js`: category request functions.
- `src/api/medicineApi.js`: medicine request and multipart image-upload functions.
- `src/pages/CategoryManagementPage.jsx`: staff category list and add/edit/delete form.
- `src/pages/MedicineManagementPage.jsx`: staff medicine list, filters, add/edit/deactivate action, and image upload.
- `src/pages/medicineManagement.css`: styles that use existing CareNova colour variables and dashboard card patterns.
- `src/routes/AppRoutes.jsx`: PharmacyStaff-only routes for the two management pages.
- `src/components/DashboardLayout.jsx`: Pharmacy Staff navigation entries for Medicines and Inventory.

## Task 1: Local Kafka and secure API foundation

**Files:**
- Create: `docker-compose.kafka.yml`
- Create: `src/PharmacyOrder.Api/Config/KafkaSettings.cs`
- Modify: `src/PharmacyOrder.Api/PharmacyOrder.Api.csproj`
- Modify: `src/PharmacyOrder.Api/appsettings.json`
- Modify: `src/PharmacyOrder.Api/Program.cs`

**Interfaces:**
- Produces `KafkaSettings` with `BootstrapServers`, `ConsumerGroupId`, `LowStockThreshold`, and `PublishRetryLimit`.
- Produces authorization policy `PharmacyStaff`, accepting only the `PharmacyStaff` role claim.

- [ ] **Step 1: Add the failing authorization test**

Create `tests/PharmacyOrder.Api.Tests/Integration/StaffAuthorizationTests.cs` with a request to `POST /api/staff/categories` that has no bearer token and asserts HTTP 401.

- [ ] **Step 2: Run the new test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~StaffAuthorizationTests`

Expected: FAIL because the staff route and JWT middleware do not exist.

- [ ] **Step 3: Add Docker Kafka configuration**

Create a KRaft Kafka service mapped to `9092:9092` and Kafka UI mapped to `8081:8080`. Configure the broker listener as `PLAINTEXT://localhost:9092` and Kafka UI bootstrap server as `kafka:9092`.

- [ ] **Step 4: Add secure application configuration**

Add `Confluent.Kafka` version `2.12.0`. Bind `KafkaSettings`. Configure JWT bearer validation with issuer `CareNova.UserAuth`, audience `CareNova.Client`, `HmacSha256` signing key from `JwtSettings:Secret`, and `ClockSkew = TimeSpan.Zero`. Add a `PharmacyStaff` policy requiring the role `PharmacyStaff`. Configure CORS to allow `http://localhost:3000`.

- [ ] **Step 5: Run the authorization test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~StaffAuthorizationTests`

Expected: PASS with unauthenticated staff requests rejected before controller execution.

- [ ] **Step 6: Commit the foundation**

Run: `git add docker-compose.kafka.yml src/PharmacyOrder.Api && git commit -m "chore: configure Kafka and staff authorization"`

## Task 2: Event outbox and staff notification persistence

**Files:**
- Create: `src/PharmacyOrder.Api/Models/EventOutbox.cs`
- Create: `src/PharmacyOrder.Api/Models/StaffNotification.cs`
- Modify: `src/PharmacyOrder.Api/Models/Medicine.cs`
- Modify: `src/PharmacyOrder.Api/Data/AppDbContext.cs`
- Create: EF Core migration named `AddKafkaEventOutbox` under `src/PharmacyOrder.Api/Migrations/`
- Test: `tests/PharmacyOrder.Api.Tests/Unit/EventOutboxPersistenceTests.cs`

**Interfaces:**
- Produces `DbSet<EventOutbox> EventOutboxEntries` and `DbSet<StaffNotification> StaffNotifications`.
- Adds `bool IsActive` to `Medicine`, defaulting to `true`.

- [ ] **Step 1: Write the failing persistence tests**

Add a test that saves two `EventOutbox` records with the same `EventId` and expects a unique-constraint failure. Add a test that creates a new `Medicine` and expects `IsActive` to be true.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~EventOutboxPersistenceTests`

Expected: FAIL because the entities and indexes do not exist.

- [ ] **Step 3: Implement the entities and mapping**

Create `EventOutbox` with `Id`, unique `Guid EventId`, `Topic`, `EventType`, `EventKey`, `PayloadJson`, `OccurredAtUtc`, nullable `PublishedAtUtc`, `PublishAttempts`, and nullable `LastError`. Create `StaffNotification` with unique `Guid EventId`, type, title, message, related entity type and ID, timestamp, and `IsRead`. Configure unique indexes for both event IDs and set `Medicine.IsActive` default to true.

- [ ] **Step 4: Generate the EF Core migration**

Run: `dotnet ef migrations add AddKafkaEventOutbox --project src/PharmacyOrder.Api --startup-project src/PharmacyOrder.Api`

Expected: a migration creating outbox and staff-notification tables and the medicine active-status column.

- [ ] **Step 5: Run the persistence tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~EventOutboxPersistenceTests`

Expected: PASS.

- [ ] **Step 6: Commit persistence changes**

Run: `git add src/PharmacyOrder.Api tests/PharmacyOrder.Api.Tests && git commit -m "feat: add Kafka outbox persistence"`

## Task 3: Kafka event publishing and consumption

**Files:**
- Create: `src/PharmacyOrder.Api/Events/EventTypes.cs`
- Create: `src/PharmacyOrder.Api/Events/MedicineEvent.cs`
- Create: `src/PharmacyOrder.Api/Services/Interfaces/IKafkaEventProducer.cs`
- Create: `src/PharmacyOrder.Api/Services/KafkaEventProducer.cs`
- Create: `src/PharmacyOrder.Api/Services/OutboxPublisherService.cs`
- Create: `src/PharmacyOrder.Api/Services/KafkaConsumerService.cs`
- Modify: `src/PharmacyOrder.Api/Program.cs`
- Test: `tests/PharmacyOrder.Api.Tests/Unit/MedicineEventTests.cs`

**Interfaces:**
- `Task PublishAsync(EventOutbox outboxEntry, CancellationToken cancellationToken)` publishes to `carenova.medicine.events.v1`.
- `MedicineEvent` has `Guid EventId`, `string EventType`, `DateTime OccurredAtUtc`, `int AggregateId`, `string AggregateType`, and `JsonElement Data`.

- [ ] **Step 1: Write the failing event-serialization test**

Create a `MedicineCreated` event with aggregate ID 25, serialize it, deserialize it, and assert that `EventId`, `EventType`, and `AggregateId` are unchanged.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~MedicineEventTests`

Expected: FAIL because the event contract is missing.

- [ ] **Step 3: Implement the event contract and producer**

Use `System.Text.Json` with camel-case JSON. Publish event key `AggregateId.ToString()` and wait for the Kafka delivery report before returning from `PublishAsync`. On delivery success set `PublishedAtUtc`; on failure increment `PublishAttempts`, set `LastError`, and leave `PublishedAtUtc` null.

- [ ] **Step 4: Implement hosted workers**

The outbox worker queries unpublished records ordered by `Id`, tries each record, and delays five seconds between polls. The consumer joins `carenova-pharmacy-notifications`, consumes `carenova.medicine.events.v1`, and creates one `StaffNotification` with the same `EventId`. It skips an event that already has a notification and commits its offset only after successful handling.

- [ ] **Step 5: Run the event test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~MedicineEventTests`

Expected: PASS.

- [ ] **Step 6: Commit Kafka event processing**

Run: `git add src/PharmacyOrder.Api tests/PharmacyOrder.Api.Tests && git commit -m "feat: publish medicine Kafka events"`

## Task 4: Category management API

**Files:**
- Modify: `src/PharmacyOrder.Api/DTOs/Category/CategoryRequestDto.cs`
- Modify: `src/PharmacyOrder.Api/DTOs/Category/CategoryResponseDto.cs`
- Modify: `src/PharmacyOrder.Api/Repositories/ICategoryRepository.cs`
- Modify: `src/PharmacyOrder.Api/Repositories/CategoryRepository.cs`
- Modify: `src/PharmacyOrder.Api/Services/Interfaces/ICategoryService.cs`
- Modify: `src/PharmacyOrder.Api/Services/CategoryService.cs`
- Modify: `src/PharmacyOrder.Api/Validators/CategoryValidator.cs`
- Modify: `src/PharmacyOrder.Api/Controllers/CategoryController.cs`
- Test: `tests/PharmacyOrder.Api.Tests/Unit/CategoryServiceTests.cs`

**Interfaces:**
- `POST /api/staff/categories`, `PUT /api/staff/categories/{id}`, and `DELETE /api/staff/categories/{id}` require `PharmacyStaff`.
- `GET /api/staff/categories` requires `PharmacyStaff` and includes medicine counts.

- [ ] **Step 1: Write failing category-service tests**

Test that a valid category name creates a `CategoryCreated` outbox record. Test that a case-insensitive duplicate category name is rejected. Test that deleting a category assigned to an active medicine throws `CategoryInUseException` and creates no outbox record.

- [ ] **Step 2: Run the category-service tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~CategoryServiceTests`

Expected: FAIL because category service behaviour is not implemented.

- [ ] **Step 3: Implement category validation and service operations**

Require a trimmed category name from 2 to 80 characters. Reject duplicate names ignoring case. On create, update, and deletion add the corresponding outbox event within the database transaction. Permit deletion only when no active medicine has the category.

- [ ] **Step 4: Implement controller responses**

Return 201 with the new category for create, 200 with the changed category for update, 204 for deletion, 400 for validation/duplicate errors, and 409 for an in-use category.

- [ ] **Step 5: Run category-service tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~CategoryServiceTests`

Expected: PASS.

- [ ] **Step 6: Commit category management**

Run: `git add src/PharmacyOrder.Api tests/PharmacyOrder.Api.Tests && git commit -m "feat: add staff category management"`

## Task 5: Medicine management API and image upload

**Files:**
- Modify: `src/PharmacyOrder.Api/DTOs/Medicine/MedicineRequestDto.cs`
- Modify: `src/PharmacyOrder.Api/DTOs/Medicine/MedicineResponseDto.cs`
- Modify: `src/PharmacyOrder.Api/Repositories/IMedicineRepository.cs`
- Modify: `src/PharmacyOrder.Api/Repositories/MedicineRepository.cs`
- Modify: `src/PharmacyOrder.Api/Services/Interfaces/IMedicineService.cs`
- Modify: `src/PharmacyOrder.Api/Services/MedicineService.cs`
- Modify: `src/PharmacyOrder.Api/Services/FileStorageService.cs`
- Modify: `src/PharmacyOrder.Api/Validators/MedicineValidator.cs`
- Modify: `src/PharmacyOrder.Api/Controllers/MedicineController.cs`
- Test: `tests/PharmacyOrder.Api.Tests/Unit/MedicineServiceTests.cs`

**Interfaces:**
- `GET /api/staff/medicines?includeInactive=true` lists staff-visible active and deactivated medicines.
- `POST /api/staff/medicines`, `PUT /api/staff/medicines/{id}`, `PATCH /api/staff/medicines/{id}/deactivate`, and `POST /api/staff/medicines/{id}/image` require `PharmacyStaff`.

- [ ] **Step 1: Write failing medicine-service tests**

Test that creating a valid medicine creates `MedicineCreated` in the outbox. Test that a duplicate active medicine name in the same category is rejected ignoring case. Test that a negative price is rejected. Test that deactivation changes `IsActive` to false and creates `MedicineDeactivated`.

- [ ] **Step 2: Run the medicine-service tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~MedicineServiceTests`

Expected: FAIL because medicine-service behaviour is not implemented.

- [ ] **Step 3: Implement medicine validation and business rules**

Require trimmed name from 2 to 120 characters, description up to 2,000 characters, existing category, decimal price greater than or equal to zero, and a boolean prescription-required flag. Reject a duplicate active medicine name within the same category. Deactivation sets `IsActive` false without deleting the record.

- [ ] **Step 4: Implement secure image storage and endpoint**

Accept `.jpg`, `.jpeg`, `.png`, and `.webp` files up to 5 MB. Generate a server-side filename, save below `wwwroot/uploads/medicines`, save its relative path in `Medicine.ImagePath`, and return the updated medicine. Reject unsupported type and size with 400.

- [ ] **Step 5: Implement controller responses**

Return 201 on creation, 200 on read/update/upload, 204 on deactivation, 404 for absent medicine/category, 400 for validation, and 409 for duplicates.

- [ ] **Step 6: Run medicine-service tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~MedicineServiceTests`

Expected: PASS.

- [ ] **Step 7: Commit medicine management**

Run: `git add src/PharmacyOrder.Api tests/PharmacyOrder.Api.Tests && git commit -m "feat: add staff medicine management"`

## Task 6: Staff management frontend

**Files:**
- Create: `src/api/pharmacyApi.js`
- Create: `src/api/categoryApi.js`
- Create: `src/api/medicineApi.js`
- Create: `src/pages/CategoryManagementPage.jsx`
- Create: `src/pages/MedicineManagementPage.jsx`
- Create: `src/pages/medicineManagement.css`
- Modify: `src/routes/AppRoutes.jsx`
- Modify: `src/components/DashboardLayout.jsx`
- Modify: `src/pages/PharmacyStaffDashboard.jsx`
- Test: `src/pages/MedicineManagementPage.test.jsx`

**Interfaces:**
- `categoryApi.list()`, `categoryApi.create(payload)`, `categoryApi.update(id, payload)`, and `categoryApi.remove(id)`.
- `medicineApi.list()`, `medicineApi.create(payload)`, `medicineApi.update(id, payload)`, `medicineApi.deactivate(id)`, and `medicineApi.uploadImage(id, file)`.

- [ ] **Step 1: Write the failing staff-page test**

Render `MedicineManagementPage` with a mocked `medicineApi.list` returning one medicine. Assert that its name, category, price, prescription status, and Deactivate button appear.

- [ ] **Step 2: Run the frontend test to verify it fails**

Run: `npm run test:run -- MedicineManagementPage.test.jsx`

Expected: FAIL because the management page does not exist.

- [ ] **Step 3: Implement API clients**

Create `pharmacyApi` with base URL `VITE_PHARMACY_API_BASE_URL || 'http://localhost:5134/api'`. Reuse the existing local-storage bearer token. Use multipart `FormData` only for image upload and JSON for all other requests.

- [ ] **Step 4: Implement management pages**

Add PharmacyStaff-only routes `/pharmacy/medicines` and `/pharmacy/categories`. Build a medicine table with active/deactivated badge, add/edit modal form, prescription-required checkbox, image upload control, and deactivate confirmation. Build category list with add/edit/delete controls. Use existing dashboard cards, red action buttons, pale-pink warning panels, and responsive form layout.

- [ ] **Step 5: Handle user-visible request errors**

Show field validation messages, duplicate warnings, category-in-use warnings, and a retryable loading error. After a successful action, refresh the affected list and show a short success message.

- [ ] **Step 6: Run the frontend test to verify it passes**

Run: `npm run test:run -- MedicineManagementPage.test.jsx`

Expected: PASS.

- [ ] **Step 7: Commit the staff frontend**

Run: `git add src && git commit -m "feat: add staff medicine management screens"`

## Task 7: End-to-end developer verification and documentation

**Files:**
- Modify: `README.md`
- Modify: `../frontend/README.md`

- [ ] **Step 1: Document the local run sequence**

Document `docker compose -f docker-compose.kafka.yml up -d`, Kafka UI at `http://localhost:8081`, backend at `http://localhost:5134/swagger`, frontend at `http://localhost:3000`, and the `JwtSettings__Secret` local configuration requirement.

- [ ] **Step 2: Verify the backend builds**

Run: `dotnet build --no-restore`

Expected: successful build with no new warnings.

- [ ] **Step 3: Verify the frontend builds**

Run: `npm run build`

Expected: successful Vite production build.

- [ ] **Step 4: Verify the event flow locally**

Start Kafka, create a category and medicine through Swagger with a PharmacyStaff bearer token, inspect `carenova.medicine.events.v1` in Kafka UI, and confirm one related notification row is created.

- [ ] **Step 5: Commit documentation**

Run: `git add README.md docs/superpowers && git commit -m "docs: document GCN-27 local setup"`

## Plan Self-Review

- GCN-27 add, view, edit, deactivate, category management, image upload, prescription flag, price validation, duplicate-name validation, and in-use-category protection are covered by Tasks 4 through 6.
- The approved Kafka event, outbox, idempotent consumer, and staff-notification design is covered by Tasks 1 through 3.
- The staff-only API requirement is covered by Task 1 and applied explicitly in Tasks 4 and 5.
- No Sprint 2 Kafka work is assigned to GCN-25, GCN-26, GCN-29, or GCN-30.
