# CareNova Sprint 2 Kafka Event Design

## Scope

Kafka is added only to the Pharmacy Order Service for these Sprint 2 stories:

- GCN-27: Medicine and category management
- GCN-28: Stock management and low-stock tracking
- GCN-31: Checkout, order placement, and order tracking
- GCN-32: Staff order review, prescription verification, and status management

GCN-25, GCN-26, GCN-29, and GCN-30 use the existing HTTP API and MySQL operations only. They do not publish or consume Kafka events.

## Architecture

The existing `pharmacy-order-service` remains one ASP.NET Core service. It contains both Kafka producers and background Kafka consumers. No additional microservice is created for Sprint 2.

```text
HTTP request
  -> controller
  -> application service
  -> MySQL business data + outbox record (one transaction)
  -> outbox publisher
  -> Kafka topic
  -> background consumer in Pharmacy Order Service
  -> staff notification record in MySQL
```

The MySQL database remains the source of truth for medicine, category, stock, cart, prescription, and order data. Kafka is used to distribute recorded business events after those changes are saved.

## Kafka Infrastructure

Local Docker Compose starts:

- one Kafka broker in KRaft mode;
- Kafka UI for local event inspection.

Local development topics have one partition and replication factor one. The API connects through `localhost:9092`.

## Topics and Event Contracts

| Topic | Events | Event key |
| --- | --- | --- |
| `carenova.medicine.events.v1` | `MedicineCreated`, `MedicineUpdated`, `MedicineDeactivated`, `CategoryCreated`, `CategoryUpdated`, `CategoryDeleted` | medicine ID or category ID |
| `carenova.inventory.events.v1` | `StockUpdated`, `LowStockDetected`, `MedicineOutOfStock`, `MedicineBackInStock` | medicine ID |
| `carenova.order.events.v1` | `OrderPlaced`, `PrescriptionVerified`, `OrderReadyForCollection`, `OrderRejected`, `OrderCollected` | order ID |

Every event contains `eventId`, `eventType`, `occurredAtUtc`, `aggregateId`, and a type-specific payload. Prescription files and patient-sensitive data are never sent in Kafka event payloads. A prescription identifier may be sent when needed.

Using a medicine or order ID as the event key keeps related events in their Kafka partition order.

## Producer Flow

After a service validates and saves a business action, it writes an outbox record in the same MySQL transaction. The API returns its normal response without waiting for Kafka consumers.

A background outbox publisher reads unpublished records, publishes the serialized event to the configured topic, and marks the outbox record as published only after Kafka acknowledges it. Failed publishing remains retryable and is logged.

## Consumer Flow

`KafkaConsumerService` runs as a hosted background service with a named consumer group. It consumes the three topics and creates staff-facing notification records for:

- medicine and category catalogue changes;
- low-stock, out-of-stock, and back-in-stock events;
- new pending medicine orders;
- staff order-status decisions.

Before a consumer handles an event, it checks the staff notification table by `eventId`. A previously handled event is acknowledged without creating another notification. This makes consumer processing idempotent when Kafka retries a message.

Failed consumer handling is logged and retried. A message that exceeds the configured retry limit is sent to a dead-letter topic for inspection instead of blocking later messages indefinitely.

## Data Changes

Two new database entities are introduced:

### EventOutbox

- `Id`
- `EventId` (unique)
- `Topic`
- `EventType`
- `EventKey`
- `PayloadJson`
- `OccurredAtUtc`
- `PublishedAtUtc` (nullable)
- `PublishAttempts`
- `LastError` (nullable)

### StaffNotification

- `Id`
- `EventId` (unique)
- `NotificationType`
- `Title`
- `Message`
- `RelatedEntityType`
- `RelatedEntityId`
- `CreatedAtUtc`
- `IsRead`

The unique `EventId` field on staff notifications prevents duplicate notifications. The existing medicine, stock, and order tables continue to hold operational data.

## Story Integration

### GCN-27

Medicine and category create, update, deactivate, and delete operations save an outbox event. Deactivated medicines remain in staff records and remain hidden from the patient catalogue.

### GCN-28

Stock updates publish `StockUpdated`. The backend evaluates the configured low-stock threshold. Crossing into low stock publishes `LowStockDetected`; zero stock publishes `MedicineOutOfStock`; stock restored above zero publishes `MedicineBackInStock`.

### GCN-31

Successful checkout saves an order with `Pending` status and emits `OrderPlaced`.

### GCN-32

Prescription verification and valid order status changes emit the appropriate order event. Valid transitions are `Pending -> ReadyForCollection`, `Pending -> Rejected`, and `ReadyForCollection -> Collected`.

## Configuration

The Pharmacy API receives Kafka settings through configuration or environment variables:

```json
{
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "ConsumerGroupId": "carenova-pharmacy-notifications",
    "LowStockThreshold": 10
  }
}
```

Passwords and production connection values are supplied through local secrets or deployment environment variables, not committed configuration.

## Developer Validation

During development, verify that Docker Kafka and Kafka UI are running, a successful business operation creates an outbox record, the publisher sends it to the expected topic, and the consumer creates exactly one staff notification for its `eventId`. Existing backend unit and integration checks should remain runnable.
