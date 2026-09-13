# GCN-31 Implementation Plan

> Execute using superpowers:executing-plans and test-driven-development. Frontend work is independent after the API contract below.

**Goal:** Review and place pickup orders from ready cart items and track owned orders.
**Architecture:** Patient-only controller, transactional OrderService, durable order snapshots and batch allocations; React checkout and history pages.
**Tech Stack:** .NET 8, EF Core/MySQL, React/Vite, xUnit, Vitest.
**Spec:** ../specs/2026-09-13-gcn-31-pickup-checkout-design.md

## Constraints
No servers started or stopped, no live migration execution, no payment processing or delivery fields. Work only on GCN31 feature branches.

## API contract
GET /api/orders/review returns {items:[{cartItemId,medicineName,quantity,unitPrice,lineTotal}],totalAmount}.
POST /api/orders accepts {requestId:Guid,items:[{cartItemId,quantity,unitPrice}]}.
GET /api/orders and GET /api/orders/{id} return owned orders.
Order response: {id,orderNumber,orderDate,status:string,totalAmount,rejectionReason,items:[{medicineName,quantity,unitPrice,lineTotal}]}.

## Tasks
- [x] Add relational test support and order behavior tests. Assert Pending status, snapshots, batch/aggregate deductions, pending cart preservation, idempotency, empty and invalid prescription failures, rollback, ownership and rejection restoration. Execute `dotnet test` to establish failures before implementation.
- [x] Extend Models/Order.cs and OrderItem.cs; add OrderStockAllocation.cs. Add GUID ownership, request identity and fingerprint, medicine-name snapshots and prescription links. Configure uniqueness and FK restrictions in AppDbContext. Generate guarded migration; do not apply it.
- [x] Implement DTOs/Order and Services/OrderService.cs with owned review/history, transactional revalidation, earliest-expiry allocation, inventory outbox events and conditional concurrency updates. Place order and remove exact reviewed cart items in the same transaction. Retrying the same request returns the saved order. Invalid request identity reuse conflicts.
- [x] Implement guarded status transition service with exactly-once batch restoration for rejection; no public patient mutation endpoint. Ensure manual inventory updates cannot overwrite concurrent checkout deductions.
- [x] Implement Controllers/OrderController.cs, interface, DI and integration tests: claims-based patient ownership, controlled validation/conflict errors, order history and detail not-found.
- [x] Frontend: add orderApi.js, PatientCheckoutPage.jsx, PatientOrdersPage.jsx, PatientOrderDetailsPage.jsx and shared order styling; update cart, routes and Patient navigation. Test confirmation, pending exclusion, retry identity, empty state and history/status display, then run frontend suite/build.
- [x] Run backend full suite, inspect migration SQL and frontend full suite/build. Document limits of relational-provider checks and manual demo instructions. Commit only verified changes; do not push.

## Verification result
57 backend tests and 33 frontend tests passed; frontend production build passed. SQLite transaction/concurrency and in-process HTTP checks passed. MySQL migration SQL generated and reviewed; live migration and the user demo are pending. NuGet audit emitted the existing NU1900 warning.
