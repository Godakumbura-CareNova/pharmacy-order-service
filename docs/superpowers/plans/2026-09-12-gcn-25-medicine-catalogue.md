# GCN-25 Medicine Catalogue Browsing, Search and Filtering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give signed-in Patient users a paginated, searchable, category-filterable medicine catalogue that shows stock availability.

**Architecture:** A dedicated catalogue service exposes Patient-authorized API endpoints and returns a small paginated read model rather than reusing staff medicine-management responses. A Patient dashboard page calls the catalogue API and renders existing CareNova-themed cards with filter controls and disabled details actions for zero-stock medicines.

**Tech Stack:** ASP.NET Core 8, EF Core/MySQL, xUnit, React 18, React Router, Axios, Vite, React Testing Library.

**Spec:** `docs/superpowers/specs/2026-09-12-gcn-25-medicine-catalogue-design.md`

## Global Constraints

- The customer catalogue is available only to the `Patient` role.
- Return only active medicines.
- Use server-side pagination with a maximum page size of 12.
- Show zero-stock medicines as `Out of stock` and disable their View details action.
- Do not add Kafka events for GCN-25.
- Use plain Git commit messages without prefixes.

---

### Task 1: Customer catalogue contracts and service

**Files:**
- Create: `src/PharmacyOrder.Api/DTOs/Catalogue/CatalogueMedicineResponseDto.cs`
- Create: `src/PharmacyOrder.Api/DTOs/Catalogue/PaginatedCatalogueResponseDto.cs`
- Create: `src/PharmacyOrder.Api/DTOs/Catalogue/CatalogueCategoryResponseDto.cs`
- Create: `src/PharmacyOrder.Api/Services/Interfaces/ICatalogueService.cs`
- Create: `src/PharmacyOrder.Api/Services/CatalogueService.cs`
- Test: `tests/PharmacyOrder.Api.Tests/Unit/CatalogueServiceTests.cs`

**Interfaces:**
- Produces `Task<PaginatedCatalogueResponseDto> GetMedicinesAsync(string? search, int? categoryId, int page, int pageSize)`.
- Produces `Task<IReadOnlyList<CatalogueCategoryResponseDto>> GetCategoriesAsync()`.
- `CatalogueMedicineResponseDto` has `Id`, `Name`, `Description`, `CategoryId`, `CategoryName`, `Price`, `ImagePath`, `PrescriptionRequired`, `StockQuantity`, and `AvailabilityStatus`.

- [ ] **Step 1: Write failing service tests**

```csharp
[Fact]
public async Task GetMedicinesAsync_FiltersActiveMatchesAndReturnsTwelveItemPage()
{
    await using var db = CreateDbContext();
    var category = new Category { Name = "Pain Relief" };
    db.Categories.Add(category);
    db.Medicines.AddRange(Enumerable.Range(1, 13).Select(index => new Medicine
    {
        Name = $"Pain Tablet {index}", Description = "Relief", Category = category,
        Price = 100m, IsActive = true, StockQuantity = index
    }));
    db.Medicines.Add(new Medicine { Name = "Archived Tablet", Description = "Relief", Category = category, IsActive = false });
    await db.SaveChangesAsync();

    var result = await new CatalogueService(db).GetMedicinesAsync("pain", category.Id, 1, 12);

    Assert.Equal(12, result.Items.Count);
    Assert.Equal(13, result.TotalCount);
    Assert.Equal(2, result.TotalPages);
    Assert.DoesNotContain(result.Items, item => item.Name == "Archived Tablet");
}

[Fact]
public async Task GetMedicinesAsync_RepresentsZeroStockAsOutOfStock()
{
    await using var db = CreateDbContext();
    var category = new Category { Name = "Vitamins" };
    db.Medicines.Add(new Medicine
    {
        Name = "Vitamin C", Description = "Vitamin supplement", Category = category,
        Price = 200m, IsActive = true, StockQuantity = 0
    });
    await db.SaveChangesAsync();

    var result = await new CatalogueService(db).GetMedicinesAsync(null, null, 1, 12);

    Assert.Single(result.Items);
    Assert.Equal("Out of stock", result.Items[0].AvailabilityStatus);
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/PharmacyOrder.Api.Tests/PharmacyOrder.Api.Tests.csproj --filter FullyQualifiedName~CatalogueServiceTests`

Expected: FAIL because `CatalogueService` and its DTOs do not exist.

- [ ] **Step 3: Implement catalogue DTOs and query service**

```csharp
public async Task<PaginatedCatalogueResponseDto> GetMedicinesAsync(
    string? search, int? categoryId, int page, int pageSize)
{
    var safePage = Math.Max(page, 1);
    var safePageSize = Math.Clamp(pageSize, 1, 12);
    var query = _db.Medicines.Include(m => m.Category).Where(m => m.IsActive);

    if (!string.IsNullOrWhiteSpace(search))
    {
        var term = search.Trim().ToLower();
        query = query.Where(m => m.Name.ToLower().Contains(term) || m.Description.ToLower().Contains(term));
    }
    if (categoryId.HasValue) query = query.Where(m => m.CategoryId == categoryId.Value);

    var totalCount = await query.CountAsync();
    var items = await query.OrderBy(m => m.Name).Skip((safePage - 1) * safePageSize)
        .Take(safePageSize).Select(m => new CatalogueMedicineResponseDto
        {
            Id = m.Id, Name = m.Name, Description = m.Description,
            CategoryId = m.CategoryId, CategoryName = m.Category!.Name,
            Price = m.Price, ImagePath = m.ImagePath,
            PrescriptionRequired = m.PrescriptionRequired, StockQuantity = m.StockQuantity,
            AvailabilityStatus = m.StockQuantity == 0 ? "Out of stock" : "Available"
        }).ToListAsync();
    return new PaginatedCatalogueResponseDto { Items = items, Page = safePage, PageSize = safePageSize, TotalCount = totalCount, TotalPages = (int)Math.Ceiling(totalCount / (double)safePageSize) };
}
```

Implement `GetCategoriesAsync` by selecting distinct categories linked to at least one active medicine, ordered by category name.

- [ ] **Step 4: Run the service tests and confirm they pass**

Run: `dotnet test tests/PharmacyOrder.Api.Tests/PharmacyOrder.Api.Tests.csproj --filter FullyQualifiedName~CatalogueServiceTests`

Expected: PASS.

- [ ] **Step 5: Commit the service layer**

```powershell
git add src/PharmacyOrder.Api/DTOs/Catalogue src/PharmacyOrder.Api/Services/Interfaces/ICatalogueService.cs src/PharmacyOrder.Api/Services/CatalogueService.cs tests/PharmacyOrder.Api.Tests/Unit/CatalogueServiceTests.cs
git commit -m "add customer medicine catalogue service"
```

### Task 2: Patient catalogue API and authorization

**Files:**
- Create: `src/PharmacyOrder.Api/Controllers/CatalogueController.cs`
- Modify: `src/PharmacyOrder.Api/Security/AuthorizationPolicies.cs`
- Modify: `src/PharmacyOrder.Api/Program.cs`
- Test: `tests/PharmacyOrder.Api.Tests/Unit/CatalogueControllerTests.cs`

**Interfaces:**
- Consumes `ICatalogueService` from Task 1.
- Produces `GET /api/catalogue/medicines` and `GET /api/catalogue/categories`.
- Uses `AuthorizationPolicies.Patient`, which requires the `Patient` role.

- [ ] **Step 1: Write failing controller tests**

```csharp
[Fact]
public async Task GetMedicines_ReturnsCataloguePageFromService()
{
    var service = new Mock<ICatalogueService>();
    service.Setup(item => item.GetMedicinesAsync("pain", 3, 2, 12)).ReturnsAsync(new PaginatedCatalogueResponseDto());
    var result = await new CatalogueController(service.Object).GetMedicines("pain", 3, 2, 12);
    Assert.IsType<OkObjectResult>(result.Result);
}
```

- [ ] **Step 2: Run the controller test and confirm it fails**

Run: `dotnet test tests/PharmacyOrder.Api.Tests/PharmacyOrder.Api.Tests.csproj --filter FullyQualifiedName~CatalogueControllerTests`

Expected: FAIL because the controller and Patient policy do not exist.

- [ ] **Step 3: Add policy, service registration, and controller**

```csharp
public const string Patient = "Patient";
options.AddPolicy(Patient, policy => policy.RequireRole(Patient));
builder.Services.AddScoped<ICatalogueService, CatalogueService>();

[ApiController]
[Route("api/catalogue")]
[Authorize(Policy = AuthorizationPolicies.Patient)]
public class CatalogueController : ControllerBase
{
    [HttpGet("medicines")]
    public async Task<ActionResult<PaginatedCatalogueResponseDto>> GetMedicines(
        [FromQuery] string? search, [FromQuery] int? categoryId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 12)
        => Ok(await _catalogue.GetMedicinesAsync(search, categoryId, page, pageSize));
}
```

- [ ] **Step 4: Run focused tests and build the API**

Run: `dotnet test tests/PharmacyOrder.Api.Tests/PharmacyOrder.Api.Tests.csproj --filter FullyQualifiedName~Catalogue`

Run: `dotnet build src/PharmacyOrder.Api/PharmacyOrder.Api.csproj`

Expected: focused tests PASS and API build succeeds.

- [ ] **Step 5: Commit the API**

```powershell
git add src/PharmacyOrder.Api/Controllers/CatalogueController.cs src/PharmacyOrder.Api/Security/AuthorizationPolicies.cs src/PharmacyOrder.Api/Program.cs tests/PharmacyOrder.Api.Tests/Unit/CatalogueControllerTests.cs
git commit -m "add patient medicine catalogue API"
```

### Task 3: Patient catalogue page, routes, and navigation

**Files:**
- Create: `src/api/catalogueApi.js`
- Create: `src/pages/PatientMedicineCataloguePage.jsx`
- Create: `src/pages/patientMedicineCatalogue.css`
- Modify: `src/routes/AppRoutes.jsx`
- Modify: `src/components/DashboardLayout.jsx`
- Modify: `src/pages/PatientDashboard.jsx`
- Test: `src/pages/PatientMedicineCataloguePage.test.jsx`

**Interfaces:**
- Consumes `catalogueApi.listMedicines({ search, categoryId, page, pageSize: 12 })` and `catalogueApi.listCategories()`.
- Displays `PaginatedCatalogueResponseDto` from Task 1.
- Produces the Patient-protected route `/patient/medicines`.

- [ ] **Step 1: Write a failing patient-page test**

```jsx
it('renders the returned medicine and disables its details button when out of stock', async () => {
  catalogueApi.listCategories.mockResolvedValue({ data: [{ id: 1, name: 'Pain Relief' }] });
  catalogueApi.listMedicines.mockResolvedValue({ data: {
    items: [{ id: 5, name: 'Paracetamol', categoryName: 'Pain Relief', price: 150,
      prescriptionRequired: false, availabilityStatus: 'Out of stock', stockQuantity: 0 }],
    page: 1, totalPages: 1, totalCount: 1
  }});

  render(<PatientMedicineCataloguePage />);
  expect(await screen.findByText('Paracetamol')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: /view details/i })).toBeDisabled();
});
```

- [ ] **Step 2: Run the page test and confirm it fails**

Run: `npm test -- --watchAll=false PatientMedicineCataloguePage.test.jsx`

Expected: FAIL because the page and catalogue API module do not exist.

- [ ] **Step 3: Implement API client and page**

```javascript
export const catalogueApi = {
  listMedicines: ({ search = '', categoryId = '', page = 1 }) =>
    pharmacyApi.get('/catalogue/medicines', { params: { search, categoryId: categoryId || undefined, page, pageSize: 12 } }),
  listCategories: () => pharmacyApi.get('/catalogue/categories'),
};
```

Create a responsive card grid. Use the existing dashboard layout and theme variables. Render a disabled `View details` button when `availabilityStatus === 'Out of stock'`; otherwise render an enabled button without navigation, because the details route is introduced by GCN-26. Reset the page to 1 when the patient searches or changes category. Add an empty-result message and retry action for failed loads.

Enable the Patient `Medicines` navigation item, add `/patient/medicines` under the existing Patient `RoleProtectedRoute`, and change the Patient dashboard Medicines quick link to this route.

- [ ] **Step 4: Run frontend test and production build**

Run: `npm test -- --watchAll=false PatientMedicineCataloguePage.test.jsx`

Run: `npm run build`

Expected: page test PASS and Vite production build succeeds.

- [ ] **Step 5: Commit the customer screen**

```powershell
git add src/api/catalogueApi.js src/pages/PatientMedicineCataloguePage.jsx src/pages/patientMedicineCatalogue.css src/pages/PatientMedicineCataloguePage.test.jsx src/routes/AppRoutes.jsx src/components/DashboardLayout.jsx src/pages/PatientDashboard.jsx
git commit -m "add patient medicine catalogue page"
```

### Task 4: Developer run-through

**Files:**
- Modify: none unless a verified defect is found.

**Interfaces:**
- Consumes the Patient API and page from Tasks 1–3.
- Produces evidence that the catalogue accepts filters, paginates, and displays zero-stock medicine correctly.

- [ ] **Step 1: Start local dependencies and services**

Run the Pharmacy API on port 5134 and frontend on port 3000. Sign in as a Patient account.

- [ ] **Step 2: Demonstrate the catalogue**

Open `http://localhost:3000/patient/medicines`. Search for a seeded medicine, choose its category, and confirm the page controls change the returned page after more than 12 active medicines are present.

- [ ] **Step 3: Demonstrate stock handling**

Confirm a medicine with zero stock shows `Out of stock` and a disabled View details button. Confirm an in-stock medicine shows an enabled button.

- [ ] **Step 4: Record final validation and commit only if needed**

Run: `git status --short`

Expected: no uncommitted changes unless a verified defect was fixed. If a fix was needed, commit it with a plain descriptive message.
