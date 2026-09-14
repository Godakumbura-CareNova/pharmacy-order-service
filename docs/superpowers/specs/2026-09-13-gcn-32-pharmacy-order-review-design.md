# GCN-32 Pharmacy Review and Order Status Management Design

## Scope

GCN-32 gives authenticated Pharmacy Staff one place to review pending prescriptions and manage placed medicine orders. It completes the workflow started by GCN-29 and GCN-31:

```text
Patient uploads prescription
  -> PendingVerification
  -> Pharmacy Staff verifies or rejects it
  -> Verified medicine becomes checkout-ready
  -> Patient places pickup order
  -> Pending
  -> ReadyForCollection or Rejected
  -> Collected
```

The story does not implement online payment, delivery, stock deductions at collection, or a new notification interface. Stock remains reserved and deducted during GCN-31 checkout. GCN-39 through GCN-43 handle payment.

## Roles and access

All staff endpoints require the existing `PharmacyStaff` authorization policy. Patients retain their existing private prescription and order-history endpoints. A staff member can view a prescription file only through an authorized API action; files remain outside `wwwroot` and are never emitted in Kafka payloads.

## Prescription review

The staff prescription queue lists records whose status is `PendingVerification`, with medicine name, requested quantity, original file name, upload time, and a secure file-open action.

Staff actions are:

- **Verify**: changes the prescription to `Verified` and marks it verified. The patient can then include the associated cart item in checkout if the quantity remains valid.
- **Reject**: changes the prescription to `Rejected`. A staff-entered reason is stored and returned to the patient. Rejected prescriptions cannot be used at checkout.

Only a pending prescription can be decided. Repeating a decision or trying to decide another status returns a clear conflict response and does not create another event.

## Order review

The staff order queue lists Pending and ReadyForCollection orders, with patient-safe operational details: order number, items, quantities, total, order time, current status, and rejection reason where relevant. It does not expose prescription files in the order payload.

Allowed changes are strictly:

| Current status | Staff action | New status |
| --- | --- | --- |
| Pending | Mark ready for collection | ReadyForCollection |
| Pending | Reject, with reason | Rejected |
| ReadyForCollection | Mark collected | Collected |

Invalid transitions are rejected without changing stock, cart rows, or order data. Rejection uses the existing GCN-31 restoration behavior once only: allocated stock is restored and the order cannot later progress to collection.

## Kafka events and notifications

Each successful prescription or order decision writes an outbox entry in the same database transaction as the state change. The existing publisher emits it to `carenova.order.events.v1`, keyed by the order ID when an order exists. Event payloads contain only event identity, time, aggregate ID/type, and non-sensitive state data.

| Action | Event | Aggregate | Payload |
| --- | --- | --- | --- |
| Verify prescription | PrescriptionVerified | Prescription | prescription status and medicine ID |
| Mark order ready | OrderReadyForCollection | Order | order status |
| Reject order | OrderRejected | Order | order status and rejection reason |
| Mark collected | OrderCollected | Order | order status |

`KafkaConsumerService` continues to write idempotent staff notifications by EventId. Patient-visible changes come from normal API reads, not Kafka notifications.

## API

- `GET /api/staff/prescriptions?status=PendingVerification` — pending prescription queue.
- `GET /api/staff/prescriptions/{id}/file` — authorized private-file download.
- `PUT /api/staff/prescriptions/{id}/decision` — `{ decision: "Verified" | "Rejected", reason?: string }`.
- `GET /api/staff/orders?status=Pending|ReadyForCollection` — operational order queue.
- `PUT /api/staff/orders/{id}/status` — `{ status: "ReadyForCollection" | "Rejected" | "Collected", reason?: string }`.

The API validates IDs, decision values, status transitions, and a non-empty rejection reason. It returns 401/403 for unauthenticated or non-staff callers, 404 for missing records, and 409 for an already-decided prescription or invalid order transition.

## Frontend

Pharmacy Staff receives enabled **Prescriptions** and **Medicine Orders** navigation items. The prescription page has a pending-review table with Open file, Verify, and Reject controls. Reject opens a small inline reason field before submitting. The order page groups current orders by status and exposes only their allowed next actions.

After a successful staff action, the relevant list reloads and shows its new status. The patient pages already reload their status from the backend; they will show Verified/Rejected prescriptions and Pending/ReadyForCollection/Rejected/Collected orders without polling.

## Error handling

The frontend keeps the relevant queue visible and shows the backend message when an action fails. If another staff member already made the decision, reload shows the current state rather than allowing a second update. Missing files return a generic not-found response. Kafka publisher failure does not undo a completed decision; its outbox row remains available for retry.

## Validation

Backend integration and HTTP tests cover role access, private file access, valid and invalid prescription decisions, valid and invalid order transitions, rejection stock restoration, transactional outbox records, event payload privacy, and duplicate action behavior. Frontend tests cover queue loading, staff actions, status refresh, and error states. The backend suite and frontend production build must pass before this branch is proposed for review.
