# GCN-31 new-order Kafka verification

The follow-up adds `OrderPlaced` to `carenova.order.events.v1`, keyed by order ID.
The payload includes Pending status and no patient identity or prescription data.
The generated order ID is obtained with a first SaveChanges; the outbox event is
saved with a second SaveChanges inside the same transaction. Both roll back on failure.
Replaying the checkout request returns the existing order without another outbox event.
The existing publisher delivers the event; the consumer creates a staff notification
and deduplicates by EventId. Kafka redelivery can occur; this is not exactly-once delivery.

## Manual run (user starts services)

From this backend worktree, start Docker Desktop and then run:

```powershell
docker compose -f docker-compose.kafka.yml up -d
docker compose -f docker-compose.kafka.yml ps
```

Wait until Kafka is available, then restart the Pharmacy API yourself to load the code:

```powershell
dotnet run --project src/PharmacyOrder.Api --launch-profile http
```

No new database migration is required for this follow-up. Keep the existing frontend
and authentication services running for patient testing.

1. Place a new valid pickup order. Previously placed orders are not backfilled.
2. Open Kafka UI at http://localhost:8081 and inspect `carenova.order.events.v1` messages.
3. Find `OrderPlaced` with AggregateId and message key matching the new order ID.
4. In the database, inspect EventOutboxEntries for that EventId and check PublishedAtUtc.
5. Inspect StaffNotifications: the same EventId should occur once, with title
   `New order received` and the matching RelatedEntityId. This creates a database
   notification; it does not add a new notification UI.
6. Retry the exact same checkout request including its RequestId: no second order
   or OrderPlaced outbox entry should appear.
7. Attempt an invalid checkout: no order event should be created.

## Validation and remaining scope

Automated SQLite tests cover event identity, duplicate checkout requests, duplicate
notification handling, existing medicine/inventory notifications, and rollback after
either SaveChanges. They do not start Kafka or prove live broker delivery.

GCN-32 order-status events remain future work. The existing publisher stops selecting
an event after PublishRetryLimit failures (default 3); start Kafka before testing new
orders. The design's dead-letter handling is not implemented. A failed consumer record
is now sought again so a later offset commit cannot silently skip it.
