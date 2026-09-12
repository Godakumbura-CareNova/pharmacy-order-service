# GCN-28 Stock Management and Low-Stock Tracking Design

## Goal

Allow Pharmacy Staff to manage medicine stock by batch, automatically keep each medicine's available quantity current, identify low-stock and out-of-stock medicines, and publish inventory events through Kafka.

## Scope

This design implements GCN-28 only. It uses the existing medicine catalogue from GCN-27 and does not add cart, checkout, order, or customer catalogue functionality.

## Data Model

The existing `Stock` entity remains one record per medicine batch:

- `MedicineId`
- `BatchNumber`
- `Quantity`
- `PurchasePrice`
- `SellingPrice`
- `ManufacturingDate`
- `ExpiryDate`

`Medicine.StockQuantity` is the sum of every batch quantity for that medicine. It is the authoritative value that later customer-facing stories use to decide whether a medicine can be ordered. `Medicine.IsActive` remains a catalogue-management flag; a zero-stock medicine is still active and visible to staff, but unavailable for ordering.

## Staff Workflow

1. Staff select a medicine and enter batch details.
2. The API validates the request and creates a batch or updates an existing batch.
3. The API recalculates `Medicine.StockQuantity` from all of its batches.
4. The batch, medicine total, and Kafka outbox event are saved together.
5. The Inventory screen refreshes the inventory list and low-stock alerts.

Staff can view every medicine's total stock, view batches for a medicine, add a batch, edit a batch, and filter to low-stock medicines.

## Validation Rules

- Medicine must exist.
- Batch number is required and unique for a medicine.
- Quantity must be a whole number greater than or equal to zero.
- Purchase price and selling price must be zero or greater.
- Manufacturing date must be on or before expiry date.
- Expiry date must be supplied.

The server returns clear validation errors. The frontend displays the returned message without discarding entered values.

## Availability and Low Stock

`Kafka:LowStockThreshold` remains `10` for local development.

- Total quantity above 10: available, normal stock.
- Total quantity from 1 to 10: available, low stock.
- Total quantity 0: out of stock and unavailable for ordering.

The low-stock list includes quantities from 0 to 10 so staff can see both medicines that need restocking and medicines that have run out.

## Kafka Events

Inventory events use the topic `carenova.inventory.events.v1`.

All events contain an event ID, event type, occurred-at UTC timestamp, medicine ID, and a payload with batch and recalculated total quantity. They contain no patient data or prescription data.

- `InventoryUpdated` is published after every valid batch add or update.
- `LowStockDetected` is published when an update leaves the total quantity at or below the configured threshold.
- `MedicineOutOfStock` is published when an update leaves the total quantity at zero.

The existing outbox publisher sends these events after the database transaction succeeds. The existing Kafka consumer records staff notifications idempotently using the event ID.

## API Contract

All endpoints require the `PharmacyStaff` policy.

- `GET /api/staff/inventory` returns medicine inventory totals and supports `lowStockOnly=true`.
- `GET /api/staff/inventory/{medicineId}/batches` returns batch records for one medicine.
- `POST /api/staff/inventory/batches` creates a stock batch.
- `PUT /api/staff/inventory/batches/{id}` updates a stock batch.

The inventory response includes medicine ID, medicine name, total quantity, availability status, low-stock status, and its batches where requested.

## Frontend

The Pharmacy Staff sidebar gets an `Inventory` item. The Inventory page follows the existing red, pale-pink, and white CareNova staff dashboard theme.

The page contains:

- a batch add/edit form with a medicine selector;
- an inventory list with medicine name, total quantity, availability, and low-stock indicator;
- a low-stock-only filter;
- a batch detail view for each medicine.

## Verification

Developer verification covers batch creation, batch update, total recalculation, invalid quantity/date rejection, low-stock totals, zero-stock availability, and the three Kafka event types. The existing GCN-27 developer workflow remains unchanged.
