# Unit Testing & Mocking

Owner: **Minadi Pathirana (IT24101496)**

40 unit tests for the pharmacy order service, 5 per user story (GCN-25 to GCN-32). Each test checks one
class in isolation by replacing its dependencies with test doubles.

| File | User story |
|---|---|
| `GCN25_MedicineCatalogueBrowsingTests.cs` | Medicine catalogue browsing, search and filtering |
| `GCN26_MedicineDetailsQuantitySelectionTests.cs` | Medicine details and quantity selection |
| `GCN27_MedicineCategoryManagementTests.cs` | Medicine category management |
| `GCN28_StockManagementLowStockTests.cs` | Stock management and low stock tracking |
| `GCN29_PrescriptionUploadTests.cs` | Prescription upload |
| `GCN30_CartManagementTests.cs` | Cart management |
| `GCN31_CheckoutOrderTrackingTests.cs` | Checkout and order tracking |
| `GCN32_PharmacyOrderReviewTests.cs` | Pharmacy order review |
| `TestDoubles.cs` | Shared helpers: fake database, logged-in user, mocked uploaded file |

## Tools

- **xUnit**: test framework; `[Fact]` for single cases, `[Theory]` + `[InlineData]` for parameterized cases.
- **Moq**: stubs (`Setup(...).ReturnsAsync(...)`), mocks (`Verify(..., Times.Once/Never)`),
  spies (`Callback` to record calls and their order) and dummies (`Mock.Of<T>()`).
- **EF Core InMemory**: a fake database for services that use `AppDbContext` directly, so no MySQL is needed.

## Run

From the `pharmacy-order-service` folder (needs the .NET 8 SDK):

```bash
dotnet test tests/unit-mocking
dotnet test tests/unit-mocking --logger "console;verbosity=normal"   # show each test
dotnet test tests/unit-mocking --filter "FullyQualifiedName~GCN29"   # one user story
```

The tests also run automatically in the SE3112 pipeline (`.github/workflows/se3112-test-pipeline.yml`)
on every push to an SE3112 branch.
