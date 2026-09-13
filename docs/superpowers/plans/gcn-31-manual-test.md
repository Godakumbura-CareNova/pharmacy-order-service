# GCN-31 manual test

Run the new worktrees, not the GCN-30 folders. Keep the existing authentication service and MySQL running. The assistant has not started/stopped application servers or applied this migration.

## Pharmacy API

In your terminal stop the old Pharmacy API if it occupies port 5134. Then:

```powershell
cd "D:\CareNova Dev\pharmacy-order-service-GCN-31-Checkout-Order-Tracking"
dotnet ef database update --project src\PharmacyOrder.Api --startup-project src\PharmacyOrder.Api
dotnet run --project src\PharmacyOrder.Api --launch-profile http
```

The migration stops if Orders already contains legacy records. Do not delete them to bypass the guard: their integer customer IDs need an explicit mapping to authenticated GUID owners. The new app uses the same existing user-secrets ID as the previous Pharmacy API checkout.

## Frontend

In your terminal stop the old frontend if it occupies port 3000. Then:

```powershell
cd "D:\CareNova Dev\frontend-GCN-31-Checkout-Order-Tracking"
npm run dev
```

Open http://localhost:3000 and sign in as a Patient.

## Demo

1. As pharmacy staff, ensure a test non-prescription medicine is active and has a positive unexpired stock batch. Record the quantity before testing.
2. As Patient, add two units to the cart. Keep a prescription item awaiting verification in the same cart.
3. Choose Checkout ready medicines. Confirm names, quantities, unit prices, total and Pharmacy pickup. The pending prescription is not part of this review.
4. Confirm and place the order. Expect an order number and Pending status. View my order opens saved line details.
5. Open Medicine Orders. Expect the new order at the top, with readable status and a working detail link.
6. Return to My cart. Only the ordered items were removed; pending prescriptions remain.
7. Check staff inventory: batch quantities and the medicine total have both decreased by exactly the ordered quantity.
8. Change an ordered medicine's catalogue name/price, then revisit order details. Its saved name and price remain unchanged.
9. Try checkout with no ready items. There must be no active Place order action.
10. Review a quantity, then reduce available stock in another staff session before confirming. Expect a stock warning and no order. Return to the cart to correct the quantity.

Status changes and rejection restoration are tested at the service level for the upcoming authorized staff workflow. GCN-31 does not add the staff management screen or payment processing.

## Automated verification

Backend: `dotnet test --no-restore`.
Frontend: `npm run test:run`, then `npm run build`.
Tests use isolated SQLite databases and an in-process HTTP test host. They do not modify the user's application database. MySQL migration SQL is generated for review, but live MySQL migration/concurrency execution remains a manual/environment-specific check.
