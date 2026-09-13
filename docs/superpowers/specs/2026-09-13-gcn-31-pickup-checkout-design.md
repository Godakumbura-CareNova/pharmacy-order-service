# GCN-31 Pharmacy Pickup Checkout and Order Tracking

## Approved scope

Patients review ready cart items, confirm a pharmacy pickup order, and view current and previous orders. New orders have Pending status. The other statuses are ReadyForCollection, Collected, and Rejected. There are no delivery address fields, payment provider integrations, or automatic expiry rules in this story. Payment processing belongs to GCN-39 through GCN-43. An order is never presented as paid merely because it was placed.

## Patient experience

My cart provides Checkout ready medicines below the ready subtotal. The button is disabled when there are no ready items. Checkout lists medicine names, quantities, unit prices, line totals, and the order total, followed by Pharmacy pickup and Confirm and place order. Patients can return to the cart to edit quantities. Pending prescription items remain in the cart.

Confirmation displays the order number, Pending status, total, and View my order. Medicine Orders in the Patient navigation becomes enabled. Its page lists all owned orders, newest first, with order number, date, total, and readable status. Each order opens its saved item details. Empty, loading, retry, and not-found states are explicit. Rejected orders display a reason when supplied.

Example: Ibuprofen x 1 at LKR 200 and Paracetamol x 2 at LKR 150 produce a Pending pickup order totaling LKR 500. Amoxicillin x 2 waiting for prescription verification remains in the cart. Approval makes that item eligible for a later, separately confirmed order.

## Checkout validation and ownership

Every patient endpoint requires the Patient policy and derives the GUID customer ID from authenticated claims. Requests cannot select another patient's cart or orders. The client submits the exact reviewed cart item IDs, quantities, unit prices, and a stable request ID; the backend calculates authoritative prices and totals. If reviewed values changed, placement is blocked with a refresh-and-review message rather than silently ordering changed quantities or prices.

The server rejects an empty selection, duplicate item IDs, non-owned items, inactive or missing medicines, invalid quantities, and insufficient stock. A prescription medicine included in checkout requires an owned Verified prescription matching the medicine and requested quantity. Missing, rejected, or pending prescriptions block that submitted order. Pending items outside the selection do not block ready items. Prescription references already used by a non-rejected order cannot authorize an additional order.

## Persistence and retries

Extend Order.CustomerId from the existing integer scaffold to Guid, consistent with Cart and Prescription. Preserve item medicine name, quantity, unit price, and prescription reference as order-time snapshots. Record a unique per-patient checkout request ID and the normalized request fingerprint. A repeat of the same request returns its existing order; reuse with different content is rejected. The frontend retains the request ID across uncertain failures and disables submission while a request is running.

Order creation, stock allocation, inventory outbox records, and deletion of selected cart rows commit in one transaction. Failure rolls everything back. Use relational database locking or conditional updates to prevent concurrent overselling; lock medicine/batch records in a consistent ID order and validate cart rows again inside the transaction. Aggregate quantities when multiple selected rows refer to one medicine. A changed cart cannot be cleared by a stale checkout request.

The GUID migration must not silently fabricate ownership for existing integer-owned orders. Check for legacy rows and fail with an actionable migration message if ownership cannot be mapped. Generate the migration without applying it to the user's database.

## Stock consistency

Adding to a cart or verifying a prescription does not deduct stock. Confirming an order deducts its quantities once. ReadyForCollection and Collected do not deduct again.

InventoryService currently recalculates Medicine.StockQuantity from stock batches. Checkout therefore deducts batch quantities and the medicine aggregate together and stores allocations linking each order item to its batches. Allocate positive, unexpired stock in earliest-expiry order. Insufficient eligible batch stock blocks checkout. Preserve existing inventory event/outbox conventions so stock changes remain observable.

Rejection restores those recorded batch allocations and the corresponding aggregate exactly once in the same transaction as the status change. Collection cannot be followed by rejection. The staff workflow owns status changes; patients cannot mutate status. This story supplies the service transition rules and tests for future staff integration, without adding a separate staff management screen. Integration must also guard against concurrent manual batch edits overwriting checkout deductions.

## API and component boundaries

- OrderController: Patient-only review, placement, list, and detail endpoints under /api/orders; standardized validation/conflict errors and no internal exception disclosure.
- OrderService: authoritative checkout validation, idempotency, transactional placement, owned history, and guarded status transitions.
- Order stock allocation records: durable record of batch deductions and restoration.
- Frontend orderApi: request wrappers following pharmacyApi conventions.
- PatientCheckoutPage: review and confirm with retained retry identity.
- PatientOrdersPage and PatientOrderDetailsPage: owned history and status presentation.
- PatientCartPage, DashboardLayout, AppRoutes: checkout action and Patient navigation/routes.

## Verification

Unit tests cover totals, eligible prescriptions, quantity aggregation, state transitions, and snapshot history. Relational integration tests cover ownership, empty selections, missing prescriptions, stock conflicts, rollback, duplicate retries, cart preservation, batch restoration, and simultaneous requests for the last units. In-memory EF tests alone cannot prove locking or transaction correctness.

Frontend tests cover ready-only checkout, review totals, retry identity, placement errors, confirmation, history, and readable statuses. Run the backend suite and frontend suite/build. User performs the final manual demo using their own servers; do not start or stop servers, apply migrations, or change live stock for the demo.
