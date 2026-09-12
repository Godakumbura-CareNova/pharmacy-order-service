# GCN-30 Cart Management Design

## Scope

GCN-30 gives each authenticated Patient one saved cart. The cart separates medicines that are ready for a future checkout from prescription medicines that are waiting for pharmacy verification. It supports adding, changing quantity, removing, and viewing cart items. It does not create orders, take payment, reserve stock, or provide checkout; those behaviors are deferred to GCN-31.

## Patient flow

1. On the medicine-detail page, a Patient selects a quantity for an available non-prescription medicine and chooses **Add to cart**.
2. The API validates the active medicine and current stock, then creates or updates that Patient's cart item. The catalogue action confirms that the item was added and offers a route to **My cart**.
3. The **My cart** page shows a **Ready to order** section for non-prescription medicines and a **Waiting for pharmacy verification** section for prescription medicines with `PendingVerification` status.
4. The Patient can increase or decrease an item quantity, or remove an item. Every update revalidates medicine activity and available stock on the API.
5. When a Patient uploads a prescription successfully, GCN-30 creates a cart item linked to that prescription record. The item appears in the pending-verification section.
6. A future GCN-32 status update will move a `Verified` prescription item into **Ready to order** or show a `Rejected` item in a **Requires attention** section. GCN-30 does not provide pharmacy verification controls.

## Cart presentation

For example, the cart can display:

```text
Ready to order
Ibuprofen 200mg × 1              LKR 200.00
Paracetamol 500mg × 2            LKR 300.00
Ready subtotal                    LKR 500.00

Waiting for pharmacy verification
Amoxicillin 250mg × 2            LKR 600.00
Prescription pending verification
```

The cart presents the ready subtotal, pending subtotal, and whole-cart total. It does not render a checkout action in GCN-30. GCN-31 will add **Checkout ready medicines** below the ready subtotal and create an order from ready items only.

## Data model

The existing cart models use integer customer identifiers, while the shared user-auth service issues GUID user identifiers. GCN-30 changes `Cart.CustomerId` to `Guid` and creates a unique cart for each Patient.

`CartItem` gains a nullable `PrescriptionId` and a navigation relationship to `Prescription`:

- `PrescriptionId = null` identifies a non-prescription cart item.
- `PrescriptionId` identifies a prescription-required item, owned by the same authenticated Patient as the cart.

The API maintains one non-prescription item per medicine in a Patient cart by incrementing its quantity on repeat additions. Each uploaded prescription creates at most one linked cart item, identified by its `PrescriptionId`; separate uploads remain separate cart items. The cart reads medicine price and stock at request time; it does not persist a checkout price or deduct stock in this story.

## API

All cart endpoints require the Patient authorization policy and derive the Patient GUID from the JWT.

- `GET /api/cart` returns the caller's cart, item summaries, prescription state where applicable, section subtotals, and whole-cart total.
- `POST /api/cart/items` adds or increments a non-prescription medicine. It receives `medicineId` and `quantity`.
- `PUT /api/cart/items/{id}` replaces an owned cart item quantity after validating current stock.
- `DELETE /api/cart/items/{id}` removes an owned cart item.

The existing prescription-upload operation creates its linked cart item in the same database transaction after the upload metadata is accepted. This prevents a successful prescription upload from being omitted from the cart.

The API rejects inactive medicines, prescription-required medicines submitted through the non-prescription add endpoint, quantities below one, quantities above current stock, missing prescriptions, and prescription records owned by another Patient.

## Frontend

The Patient navigation gains **My cart**. The non-prescription action on the medicine-detail page calls the cart API rather than only retaining session state. After a prescription upload succeeds, its confirmation explains that the medicine is in the cart and awaiting verification.

The cart page displays separate responsive sections with medicine image, name, unit price, quantity controls, line subtotal, remove action, and clearly labelled prescription status. It has loading, empty-cart, validation-error, and retry states.

## Deferred work

GCN-31 will add **Checkout ready medicines**, delivery/contact details, order creation, stock deduction, and order confirmation. GCN-32 will allow PharmacyStaff to verify or reject prescriptions. GCN-30 does not publish or consume Kafka events.

## Validation

Backend tests cover Patient ownership, GUID cart ownership, item merging, quantity and stock validation, prescription linkage, and separation of ready and pending totals. Frontend tests cover adding a non-prescription item, cart section rendering, quantity updates, item removal, and pending-prescription status. The full backend and frontend test suites and production builds run before the feature branches are pushed.
