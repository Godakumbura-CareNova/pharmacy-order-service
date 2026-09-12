# GCN-30 Cart Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build one authenticated Patient cart that separates ready non-prescription medicines from prescription medicines awaiting pharmacy verification.

**Architecture:** The Pharmacy Order API owns cart persistence, stock validation, prescription linkage, and section totals. The React frontend adds a cart API client, a Patient cart page, and real cart actions from the medicine detail and prescription upload flows. GCN-30 does not create orders, process payment, reserve inventory, deduct stock, or expose checkout.

**Tech Stack:** ASP.NET Core 8, Entity Framework Core 8, MySQL/Pomelo, xUnit/Moq, React 18, React Router, Axios, Vitest, Testing Library, Lucide React.

**Spec:** `docs/superpowers/specs/2026-09-12-gcn-30-cart-management-design.md`

## Global Constraints

- Every cart endpoint requires the Patient authorization policy and obtains the Patient GUID from the JWT name-identifier claim.
- A Patient owns exactly one cart, identified by `Guid CustomerId`.
- A non-prescription medicine is one cart item per medicine; repeat adds increment its quantity up to current stock.
- A prescription cart item is linked to one owned `Prescription` record and starts in the pending-verification section.
- The cart reads current medicine price and stock; it does not reserve or deduct stock.
- GCN-30 has no checkout, order, payment, Kafka, or PharmacyStaff verification action.
- Commit messages use plain imperative descriptions without `feat:`, `fix:`, or `chore:` prefixes.

---

## File structure

### Backend worktree

`D:\CareNova Dev\pharmacy-order-service-GCN-30-Cart-Management`

- Modify `src/PharmacyOrder.Api/Models/Cart.cs` — change owner identity to `Guid`.
- Modify `src/PharmacyOrder.Api/Models/CartItem.cs` — add nullable prescription link and navigation properties.
- Modify `src/PharmacyOrder.Api/Data/AppDbContext.cs` — define cart ownership, item, and prescription relationships/indexes.
- Create `src/PharmacyOrder.Api/DTOs/Cart/*.cs` — replace the empty scaffolds with request and response contracts.
- Create `src/PharmacyOrder.Api/Services/Interfaces/ICartService.cs` and `src/PharmacyOrder.Api/Services/CartService.cs` — replace empty scaffolds with cart behavior.
- Create `src/PharmacyOrder.Api/Controllers/CartController.cs` — replace the empty scaffold with Patient-only HTTP endpoints.
- Modify `src/PharmacyOrder.Api/Services/PrescriptionService.cs` — link a successful prescription upload to its cart item in the same database transaction.
- Modify `src/PharmacyOrder.Api/Program.cs` — register `ICartService`.
- Create an EF migration under `src/PharmacyOrder.Api/Migrations/` — migrate cart ownership to GUID and add prescription linkage.
- Create/update `tests/PharmacyOrder.Api.Tests/Unit/CartDataModelTests.cs`, `CartServiceTests.cs`, `CartControllerTests.cs`, and `PrescriptionServiceTests.cs` — cover cart persistence, behavior, and prescription linkage.

### Frontend worktree

`D:\CareNova Dev\frontend-GCN-30-Cart-Management`

- Create `src/api/cartApi.js` — authenticated cart requests.
- Modify `src/pages/PatientMedicineDetailsPage.jsx` — add non-prescription medicines through the API.
- Modify `src/pages/PatientPrescriptionUploadPage.jsx` — show that successful uploads are awaiting verification in the cart.
- Create `src/pages/PatientCartPage.jsx` and `src/pages/patientCart.css` — display the three cart sections, totals, controls, and errors.
- Modify `src/components/DashboardLayout.jsx` — enable Patient **My cart** navigation.
- Modify `src/routes/AppRoutes.jsx` — add `/patient/cart` under the Patient role guard.
- Create tests for the detail-page add action and cart-page rendering/updates.

## Task 1: Establish the cart data model and API contracts

**Files:**
- Modify: `src/PharmacyOrder.Api/Models/Cart.cs`
- Modify: `src/PharmacyOrder.Api/Models/CartItem.cs`
- Modify: `src/PharmacyOrder.Api/Data/AppDbContext.cs`
- Create: `src/PharmacyOrder.Api/DTOs/Cart/AddCartItemRequestDto.cs`
- Create: `src/PharmacyOrder.Api/DTOs/Cart/UpdateCartItemRequestDto.cs`
- Create: `src/PharmacyOrder.Api/DTOs/Cart/CartItemResponseDto.cs`
- Create: `src/PharmacyOrder.Api/DTOs/Cart/CartResponseDto.cs`
- Test: `tests/PharmacyOrder.Api.Tests/Unit/CartDataModelTests.cs`

**Interfaces:**
- Produces `Cart.CustomerId: Guid` and `CartItem.PrescriptionId: int?`.
- Produces `AddCartItemRequestDto { int MedicineId; int Quantity; }` and `UpdateCartItemRequestDto { int Quantity; }`.
- Produces `CartItemResponseDto` with item ID, medicine ID/name/image path, unit price, quantity, line total, `PrescriptionId`, and `PrescriptionStatus`.
- Produces `CartResponseDto` with `ReadyItems`, `PendingItems`, `AttentionItems`, `ReadySubtotal`, `PendingSubtotal`, `AttentionSubtotal`, and `CartTotal`.

- [ ] **Step 1: Write failing model-contract tests**

```csharp
[Fact]
public async Task Cart_PersistsGuidPatientOwnership()
{
    var patientId = Guid.NewGuid();
    await using var db = CreateDbContext();
    db.Carts.Add(new Cart { CustomerId = patientId });
    await db.SaveChangesAsync();

    var saved = await db.Carts.SingleAsync();

    Assert.Equal(patientId, saved.CustomerId);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
dotnet test tests\PharmacyOrder.Api.Tests\PharmacyOrder.Api.Tests.csproj --filter "FullyQualifiedName~CartDataModelTests.Cart_PersistsGuidPatientOwnership"
```

Expected: the test does not compile because `Cart.CustomerId` is an integer.

- [ ] **Step 3: Implement the model and DTO contracts**

```csharp
public class Cart
{
    public int Id { get; set; }
    public Guid CustomerId { get; set; }
    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
}

public class CartItem
{
    public int Id { get; set; }
    public int CartId { get; set; }
    public int MedicineId { get; set; }
    public int? PrescriptionId { get; set; }
    public int Quantity { get; set; }
    public Cart? Cart { get; set; }
    public Medicine? Medicine { get; set; }
    public Prescription? Prescription { get; set; }
}
```

Configure the `CartItem` prescription foreign key with `DeleteBehavior.Restrict` and create an index on `Cart.CustomerId`.

- [ ] **Step 4: Run the targeted test to verify it passes**

Run the command from Step 2. Expected: PASS.

- [ ] **Step 5: Generate and inspect the migration**

Run after no Pharmacy API process is locking this worktree:

```powershell
dotnet ef migrations add AddPatientCartManagement --project src\PharmacyOrder.Api --startup-project src\PharmacyOrder.Api
```

Confirm the migration changes `Carts.CustomerId` from `int` to `char(36)` and adds nullable `CartItems.PrescriptionId` with its index and foreign key.

- [ ] **Step 6: Commit**

```powershell
git add src/PharmacyOrder.Api/Models src/PharmacyOrder.Api/Data src/PharmacyOrder.Api/DTOs/Cart src/PharmacyOrder.Api/Migrations tests/PharmacyOrder.Api.Tests/Unit/CartDataModelTests.cs
git commit -m "add patient cart data model"
```

## Task 2: Implement Patient cart service behavior

**Files:**
- Create: `src/PharmacyOrder.Api/Services/Interfaces/ICartService.cs`
- Create: `src/PharmacyOrder.Api/Services/CartService.cs`
- Test: `tests/PharmacyOrder.Api.Tests/Unit/CartServiceTests.cs`

**Interfaces:**
- Consumes the Task 1 cart models and DTOs.
- Produces `GetCartAsync(Guid patientId, CancellationToken)`, `AddNonPrescriptionItemAsync(Guid patientId, AddCartItemRequestDto request, CancellationToken)`, `UpdateItemAsync(Guid patientId, int cartItemId, UpdateCartItemRequestDto request, CancellationToken)`, `RemoveItemAsync(Guid patientId, int cartItemId, CancellationToken)`, and `AddPrescriptionItemAsync(Guid patientId, Prescription prescription, CancellationToken)`.

- [ ] **Step 1: Write failing service tests**

```csharp
[Fact]
public async Task AddNonPrescriptionItemAsync_IncrementsExistingItemWithinAvailableStock()
{
    await using var db = CreateDbContext();
    var medicine = new Medicine { Name = "Ibuprofen", Description = "Pain relief", Price = 200m, IsActive = true, StockQuantity = 3 };
    db.Medicines.Add(medicine);
    await db.SaveChangesAsync();
    var service = new CartService(db);
    var patientId = Guid.NewGuid();

    await service.AddNonPrescriptionItemAsync(patientId, new AddCartItemRequestDto { MedicineId = medicine.Id, Quantity = 1 }, CancellationToken.None);
    await service.AddNonPrescriptionItemAsync(patientId, new AddCartItemRequestDto { MedicineId = medicine.Id, Quantity = 2 }, CancellationToken.None);

    Assert.Equal(3, (await db.CartItems.SingleAsync()).Quantity);
}

[Fact]
public async Task GetCartAsync_PlacesPendingPrescriptionItemInPendingSection()
{
    var patientId = Guid.NewGuid();
    await using var db = CreateDbContext();
    var medicine = new Medicine { Name = "Amoxicillin", Description = "Antibiotic", Price = 300m, IsActive = true, StockQuantity = 10, PrescriptionRequired = true };
    var prescription = new Prescription { UploadedBy = patientId, Medicine = medicine, Quantity = 2, Status = "PendingVerification", OriginalFileName = "prescription.pdf", StoredFileName = "private-file.pdf", ContentType = "application/pdf", FileSizeBytes = 256 };
    var cart = new Cart { CustomerId = patientId };
    cart.Items.Add(new CartItem { Medicine = medicine, Prescription = prescription, Quantity = 2 });
    db.Carts.Add(cart);
    await db.SaveChangesAsync();

    var response = await new CartService(db).GetCartAsync(patientId, CancellationToken.None);

    Assert.Empty(response.ReadyItems);
    Assert.Single(response.PendingItems);
    Assert.Equal("PendingVerification", response.PendingItems[0].PrescriptionStatus);
}
```

- [ ] **Step 2: Run the service tests to verify they fail**

Run:

```powershell
dotnet test tests\PharmacyOrder.Api.Tests\PharmacyOrder.Api.Tests.csproj --filter "FullyQualifiedName~CartServiceTests"
```

Expected: FAIL because the service methods do not exist.

- [ ] **Step 3: Implement validation and section mapping**

Implement these rules:

```csharp
// Non-prescription add: medicine must be active, not require a prescription,
// and requested combined quantity must be between 1 and medicine.StockQuantity.
// Update/remove: cart item must belong to the caller's GUID cart.
// Prescription add: prescription.UploadedBy must equal patientId,
// prescription must reference an active medicine, and one linked item may exist per prescription.
// Sections: null PrescriptionId -> ready; Verified -> ready;
// PendingVerification -> pending; Rejected -> attention.
```

Compute every line total as `Medicine.Price * Quantity` and compute each section subtotal from its displayed items.

- [ ] **Step 4: Run the service tests to verify they pass**

Run the command from Step 2. Expected: PASS.

- [ ] **Step 5: Add ownership and invalid-state tests**

Add tests that prove another Patient cannot update/remove an item, a prescription medicine cannot use the non-prescription endpoint, quantities above stock are rejected, and another Patient's prescription cannot be linked.

- [ ] **Step 6: Run the targeted tests again**

Run the command from Step 2. Expected: PASS with all cart-service tests.

- [ ] **Step 7: Commit**

```powershell
git add src/PharmacyOrder.Api/Services tests/PharmacyOrder.Api.Tests/Unit/CartServiceTests.cs
git commit -m "add patient cart service"
```

## Task 3: Expose the Patient cart API and link prescription uploads

**Files:**
- Create: `src/PharmacyOrder.Api/Controllers/CartController.cs`
- Modify: `src/PharmacyOrder.Api/Services/PrescriptionService.cs`
- Modify: `src/PharmacyOrder.Api/Services/Interfaces/IPrescriptionService.cs` only if its response must expose cart-link information.
- Modify: `src/PharmacyOrder.Api/Program.cs`
- Test: `tests/PharmacyOrder.Api.Tests/Unit/CartControllerTests.cs`
- Test: `tests/PharmacyOrder.Api.Tests/Unit/PrescriptionServiceTests.cs`

**Interfaces:**
- Consumes `ICartService` from Task 2.
- Produces Patient-only routes: `GET /api/cart`, `POST /api/cart/items`, `PUT /api/cart/items/{id}`, and `DELETE /api/cart/items/{id}`.
- Produces a linked cart item for each successful prescription upload.

- [ ] **Step 1: Write failing controller and upload-linkage tests**

```csharp
[Fact]
public async Task AddItem_ForwardsGuidPatientIdentifierToCartService()
{
    var patientId = Guid.NewGuid();
    var cart = new Mock<ICartService>();
    cart.Setup(service => service.AddNonPrescriptionItemAsync(patientId, It.IsAny<AddCartItemRequestDto>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new CartResponseDto());
    var controller = BuildPatientController(cart.Object, patientId);

    var result = await controller.AddItem(new AddCartItemRequestDto { MedicineId = 1, Quantity = 1 }, CancellationToken.None);

    Assert.IsType<OkObjectResult>(result.Result);
}
```

Add a prescription-service test asserting a successful upload creates a cart item with the new prescription ID and its Patient GUID.

- [ ] **Step 2: Run the controller and prescription tests to verify they fail**

Run:

```powershell
dotnet test tests\PharmacyOrder.Api.Tests\PharmacyOrder.Api.Tests.csproj --filter "FullyQualifiedName~CartControllerTests|FullyQualifiedName~PrescriptionServiceTests"
```

Expected: FAIL because `CartController`, service registration, and upload linkage are absent.

- [ ] **Step 3: Implement the controller and transactional upload linkage**

Use the same `Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)` pattern as `PrescriptionController`. Apply `[Authorize(Policy = AuthorizationPolicies.Patient)]` to the controller. Return `NotFound` for another Patient's cart item and `BadRequest` with a `{ message }` response for validation errors.

In `PrescriptionService.UploadAsync`, begin a database transaction, save the prescription metadata, call `ICartService.AddPrescriptionItemAsync`, then commit. Roll back database changes if the cart linkage fails. Register `ICartService` in `Program.cs`.

- [ ] **Step 4: Run the targeted tests to verify they pass**

Run the command from Step 2. Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/PharmacyOrder.Api/Controllers/CartController.cs src/PharmacyOrder.Api/Services/PrescriptionService.cs src/PharmacyOrder.Api/Services/Interfaces src/PharmacyOrder.Api/Program.cs tests/PharmacyOrder.Api.Tests/Unit/CartControllerTests.cs tests/PharmacyOrder.Api.Tests/Unit/PrescriptionServiceTests.cs
git commit -m "add patient cart API"
```

## Task 4: Connect non-prescription selection and prescription upload to the cart

**Files:**
- Create: `src/api/cartApi.js`
- Modify: `src/pages/PatientMedicineDetailsPage.jsx`
- Modify: `src/pages/PatientPrescriptionUploadPage.jsx`
- Test: `src/pages/PatientMedicineDetailsPage.test.jsx`
- Test: `src/pages/PatientPrescriptionUploadPage.test.jsx`

**Interfaces:**
- Consumes API routes from Task 3 through `cartApi.get`, `cartApi.addItem`, `cartApi.updateItem`, and `cartApi.removeItem`.
- Produces a server-saved non-prescription cart item and prescription-upload confirmation that states the prescription is awaiting verification in the cart.

- [ ] **Step 1: Write failing frontend tests**

```jsx
it('adds a non-prescription medicine and confirms the cart action', async () => {
  getMedicine.mockResolvedValue({ data: { id: 2, name: 'Ibuprofen', price: 200, stockQuantity: 8, prescriptionRequired: false } });
  addItem.mockResolvedValue({ data: { readyItems: [{ medicineId: 2, quantity: 2 }] } });
  render(<MemoryRouter><PatientMedicineDetailsPage /></MemoryRouter>);

  await user.click(await screen.findByRole('button', { name: /add to cart/i }));

  expect(addItem).toHaveBeenCalledWith({ medicineId: 2, quantity: 1 });
  expect(await screen.findByText(/added to your cart/i)).toBeInTheDocument();
});
```

Add a prescription upload test that expects the success screen to say the prescription medicine is awaiting verification in **My cart**.

- [ ] **Step 2: Run frontend tests to verify they fail**

Run:

```powershell
npm test -- --run src/pages/PatientMedicineDetailsPage.test.jsx src/pages/PatientPrescriptionUploadPage.test.jsx
```

Expected: FAIL because `cartApi` and the cart confirmation copy do not exist.

- [ ] **Step 3: Implement the API client and patient actions**

```javascript
export const cartApi = {
  get: () => pharmacyApi.get('/cart'),
  addItem: (payload) => pharmacyApi.post('/cart/items', payload),
  updateItem: (id, payload) => pharmacyApi.put(`/cart/items/${id}`, payload),
  removeItem: (id) => pharmacyApi.delete(`/cart/items/${id}`),
};
```

For non-prescription medicines, call `cartApi.addItem({ medicineId: medicine.id, quantity })`, show API validation errors, and remove the old non-prescription session-only handoff. Keep the prescription session handoff until upload. On upload confirmation, show a **View my cart** action in addition to **Continue shopping**.

- [ ] **Step 4: Run the targeted frontend tests to verify they pass**

Run the command from Step 2. Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/api/cartApi.js src/pages/PatientMedicineDetailsPage.jsx src/pages/PatientPrescriptionUploadPage.jsx src/pages/PatientMedicineDetailsPage.test.jsx src/pages/PatientPrescriptionUploadPage.test.jsx
git commit -m "connect medicine selection to patient cart"
```

## Task 5: Build the Patient cart page and navigation

**Files:**
- Create: `src/pages/PatientCartPage.jsx`
- Create: `src/pages/patientCart.css`
- Modify: `src/components/DashboardLayout.jsx`
- Modify: `src/routes/AppRoutes.jsx`
- Test: `src/pages/PatientCartPage.test.jsx`

**Interfaces:**
- Consumes `cartApi.get`, `cartApi.updateItem`, and `cartApi.removeItem` from Task 4.
- Produces the protected Patient route `/patient/cart` and enabled **My cart** navigation.

- [ ] **Step 1: Write failing cart-page tests**

```jsx
it('renders ready and pending cart sections with their separate totals', async () => {
  get.mockResolvedValue({ data: {
    readyItems: [{ id: 1, medicineName: 'Ibuprofen 200mg', quantity: 1, lineTotal: 200 }],
    pendingItems: [{ id: 2, medicineName: 'Amoxicillin 250mg', quantity: 2, lineTotal: 600, prescriptionStatus: 'PendingVerification' }],
    attentionItems: [], readySubtotal: 200, pendingSubtotal: 600, attentionSubtotal: 0, cartTotal: 800,
  }});
  render(<MemoryRouter><PatientCartPage /></MemoryRouter>);

  expect(await screen.findByRole('heading', { name: /ready to order/i })).toBeInTheDocument();
  expect(screen.getByRole('heading', { name: /waiting for pharmacy verification/i })).toBeInTheDocument();
  expect(screen.getByText(/LKR 800\.00/i)).toBeInTheDocument();
});
```

Add tests for quantity update, removal, loading failure with retry, and empty cart.

- [ ] **Step 2: Run the cart-page tests to verify they fail**

Run:

```powershell
npm test -- --run src/pages/PatientCartPage.test.jsx
```

Expected: FAIL because the page and route do not exist.

- [ ] **Step 3: Implement the cart page and route**

Render separate cards for **Ready to order**, **Waiting for pharmacy verification**, and **Requires attention** only when the corresponding array has items. Each item includes image fallback, name, unit price, quantity controls, line subtotal, prescription-status badge when linked, and remove control. Disable quantity controls while an API update is in progress. Include a clearly labelled statement that checkout is available in a future story; do not render a checkout button.

Add this Patient navigation item before Prescriptions:

```javascript
{ to: '/patient/cart', label: 'My cart', icon: ShoppingCart, enabled: true }
```

Add the protected route:

```jsx
<Route path="/patient/cart" element={<PatientCartPage />} />
```

- [ ] **Step 4: Run the cart-page tests to verify they pass**

Run the command from Step 2. Expected: PASS.

- [ ] **Step 5: Run the complete frontend test suite and production build**

Run:

```powershell
npm test -- --run
npm run build
```

Expected: all tests pass and Vite reports a successful production build.

- [ ] **Step 6: Commit**

```powershell
git add src/pages/PatientCartPage.jsx src/pages/patientCart.css src/components/DashboardLayout.jsx src/routes/AppRoutes.jsx src/pages/PatientCartPage.test.jsx
git commit -m "add patient cart page"
```

## Task 6: Verify the integrated GCN-30 flow

**Files:**
- Modify only if a verification failure identifies a defect in a file from Tasks 1-5.

**Interfaces:**
- Consumes the completed frontend and backend APIs.
- Produces evidence that ready and pending items remain separate across a real patient session.

- [ ] **Step 1: Apply the migration to the local development database**

Run from the backend worktree after stopping only a server running from this same worktree:

```powershell
dotnet ef database update --project src\PharmacyOrder.Api --startup-project src\PharmacyOrder.Api
```

- [ ] **Step 2: Run the complete backend test suite**

Run:

```powershell
dotnet test tests\PharmacyOrder.Api.Tests\PharmacyOrder.Api.Tests.csproj
```

Expected: all tests pass.

- [ ] **Step 3: Manually verify the patient flow**

1. Sign in as a Patient.
2. Add an in-stock non-prescription medicine, such as Ibuprofen, from its detail page.
3. Open **My cart** and confirm it appears in **Ready to order**.
4. Upload a prescription for an in-stock prescription medicine, such as Amoxicillin.
5. Open **My cart** and confirm it appears in **Waiting for pharmacy verification**.
6. Change the Ibuprofen quantity within stock, remove it, and confirm the ready subtotal and cart total update.
7. Confirm no checkout button, order, payment, or stock deduction occurs.

- [ ] **Step 4: Inspect changes and commit any verified correction**

Run:

```powershell
git status --short
git diff --check
```

If a defect is corrected during verification, commit only that correction with a plain imperative message describing it.
