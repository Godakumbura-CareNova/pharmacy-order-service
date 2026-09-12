# GCN-29 Prescription Upload Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Patients upload and view private prescription records linked to one selected prescription medicine and quantity.

**Architecture:** Extend the existing prescription model with medicine, quantity, safe display name, and verification status. The Pharmacy API saves approved file types to local prescription storage and returns files only after an authenticated ownership check. The React app receives the GCN-26 pending selection, uploads one document, displays a confirmation, and lists the caller's records.

**Tech Stack:** ASP.NET Core 8, EF Core/MySQL, xUnit, React 18, Vite, Vitest, Axios, Lucide React.

**Spec:** `docs/superpowers/specs/2026-09-12-gcn-29-prescription-upload-design.md`

## Global Constraints

- Patient-only feature; PharmacyStaff verification is deferred to GCN-32.
- Accept exactly one PDF, JPG, JPEG, or PNG file, maximum 10 MB.
- Store generated server filenames and safe relative paths; never trust client paths.
- Files must be returned only through an ownership-checked endpoint; do not expose anonymous static URLs.
- GCN-29 creates no cart item and emits no Kafka event.
- Use plain Git commit messages without `feat:`, `fix:`, or `chore:` prefixes.

---

## File Structure

### Backend

- Modify `src/PharmacyOrder.Api/Models/Prescription.cs` — add medicine relation, quantity, original filename, and status.
- Modify `src/PharmacyOrder.Api/Data/AppDbContext.cs` — configure the medicine relation and safe constraints.
- Create `src/PharmacyOrder.Api/DTOs/Prescription/PrescriptionResponseDto.cs` — patient-safe record response.
- Create `src/PharmacyOrder.Api/DTOs/Prescription/PrescriptionUploadRequestDto.cs` — multipart upload input.
- Create `src/PharmacyOrder.Api/Config/PrescriptionStorageSettings.cs` — local-path, extension, and size configuration.
- Modify `src/PharmacyOrder.Api/Services/Interfaces/IFileStorageService.cs` and `src/PharmacyOrder.Api/Services/FileStorageService.cs` — save and resolve prescription files separately from medicine images.
- Create `src/PharmacyOrder.Api/Services/Interfaces/IPrescriptionService.cs` and `src/PharmacyOrder.Api/Services/PrescriptionService.cs` — validation, persistence, private list, and owned-file retrieval.
- Create `src/PharmacyOrder.Api/Controllers/PrescriptionController.cs` — Patient-authorized HTTP endpoints.
- Modify `src/PharmacyOrder.Api/Program.cs` — register prescription settings and service.
- Modify `src/PharmacyOrder.Api/appsettings.json` — add non-secret local prescription storage defaults.
- Create an EF Core migration for prescription columns and FK.
- Create `tests/PharmacyOrder.Api.Tests/Unit/PrescriptionServiceTests.cs` — service behavior and ownership tests.

### Frontend

- Create `src/api/prescriptionApi.js` — upload, list, and owned-file URL calls.
- Create `src/pages/PatientPrescriptionUploadPage.jsx` and `src/pages/patientPrescriptionUpload.css` — selected-medicine summary, guarded file input, upload, and success confirmation.
- Create `src/pages/PatientPrescriptionsPage.jsx` and `src/pages/patientPrescriptions.css` — private history table, empty state, and file-open action.
- Modify `src/pages/PatientMedicineDetailsPage.jsx` — route prescription selections into the upload page while retaining session state.
- Modify `src/components/DashboardLayout.jsx` — enable the Patient Prescriptions navigation item and route it to history.
- Modify `src/routes/AppRoutes.jsx` — add Patient-only upload and history routes.
- Create page tests next to both new pages.

## Task 1: Extend prescription persistence and local storage

**Files:**
- Modify: `src/PharmacyOrder.Api/Models/Prescription.cs`
- Modify: `src/PharmacyOrder.Api/Data/AppDbContext.cs`
- Create: `src/PharmacyOrder.Api/Config/PrescriptionStorageSettings.cs`
- Modify: `src/PharmacyOrder.Api/Services/Interfaces/IFileStorageService.cs`
- Modify: `src/PharmacyOrder.Api/Services/FileStorageService.cs`
- Modify: `src/PharmacyOrder.Api/Program.cs`
- Modify: `src/PharmacyOrder.Api/appsettings.json`
- Create: `tests/PharmacyOrder.Api.Tests/Unit/PrescriptionStorageTests.cs`

**Consumes:** `IFormFile`, existing `FileStorageService`, `wwwroot/uploads` convention.

**Produces:** `Prescription` fields `MedicineId`, `Medicine`, `Quantity`, `OriginalFileName`, and `Status`; `SavePrescriptionAsync(IFormFile, CancellationToken)` returning a `/uploads/prescriptions/<generated-name>` path; `GetPrescriptionFilePath(string)` returning the safe physical file path.

- [ ] **Step 1: Write failing storage tests**

```csharp
[Fact]
public async Task SavePrescriptionAsync_SavesAllowedPdfWithGeneratedName()
{
    var file = CreateFormFile("doctor-note.pdf", "application/pdf", 1024);
    var path = await service.SavePrescriptionAsync(file, CancellationToken.None);
    Assert.StartsWith("/uploads/prescriptions/", path);
    Assert.EndsWith(".pdf", path);
}

[Fact]
public async Task SavePrescriptionAsync_RejectsUnsupportedExtension()
{
    var file = CreateFormFile("script.exe", "application/octet-stream", 1024);
    await Assert.ThrowsAsync<ArgumentException>(() => service.SavePrescriptionAsync(file, CancellationToken.None));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PharmacyOrder.Api.Tests --filter FullyQualifiedName~PrescriptionStorageTests`

Expected: FAIL because `SavePrescriptionAsync` does not exist.

- [ ] **Step 3: Implement model, settings, and storage methods**

```csharp
public int MedicineId { get; set; }
public Medicine? Medicine { get; set; }
public int Quantity { get; set; }
public string OriginalFileName { get; set; } = string.Empty;
public string Status { get; set; } = "PendingVerification";
```

Use `Path.GetExtension`, allow only `.pdf`, `.jpg`, `.jpeg`, `.png`, create the configured directory, write a GUID filename, and return only its `/uploads/prescriptions/` relative path. Configure `PrescriptionStorage` with `UploadPath: "wwwroot/uploads/prescriptions"`, `MaxFileSizeMb: 10`, and the four allowed extensions. Do not change medicine-image limits.

- [ ] **Step 4: Run the focused storage tests**

Run: `dotnet test tests/PharmacyOrder.Api.Tests --filter FullyQualifiedName~PrescriptionStorageTests`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/PharmacyOrder.Api/Models/Prescription.cs src/PharmacyOrder.Api/Data/AppDbContext.cs src/PharmacyOrder.Api/Config/PrescriptionStorageSettings.cs src/PharmacyOrder.Api/Services src/PharmacyOrder.Api/Program.cs src/PharmacyOrder.Api/appsettings.json tests/PharmacyOrder.Api.Tests/Unit/PrescriptionStorageTests.cs
git commit -m "add prescription file storage"
```

## Task 2: Build the patient prescription service and API

**Files:**
- Create: `src/PharmacyOrder.Api/DTOs/Prescription/PrescriptionUploadRequestDto.cs`
- Create: `src/PharmacyOrder.Api/DTOs/Prescription/PrescriptionResponseDto.cs`
- Create: `src/PharmacyOrder.Api/Services/Interfaces/IPrescriptionService.cs`
- Create: `src/PharmacyOrder.Api/Services/PrescriptionService.cs`
- Create: `src/PharmacyOrder.Api/Controllers/PrescriptionController.cs`
- Modify: `src/PharmacyOrder.Api/Program.cs`
- Create: `tests/PharmacyOrder.Api.Tests/Unit/PrescriptionServiceTests.cs`

**Consumes:** `AppDbContext`, `IFileStorageService`, `ClaimTypes.NameIdentifier`, `Prescription` storage from Task 1.

**Produces:** `UploadAsync(int patientId, PrescriptionUploadRequestDto, CancellationToken)`, `GetForPatientAsync(int patientId)`, and `GetOwnedFileAsync(int patientId, int prescriptionId)`.

- [ ] **Step 1: Write failing service tests**

```csharp
[Fact]
public async Task UploadAsync_CreatesPendingRecordForPrescriptionMedicine()
{
    var result = await service.UploadAsync(17, new PrescriptionUploadRequestDto
    {
        MedicineId = medicine.Id, Quantity = 2, File = CreateFormFile("doctor-note.pdf", "application/pdf", 1024)
    }, CancellationToken.None);
    Assert.Equal("PendingVerification", result.Status);
    Assert.Equal(2, result.Quantity);
    Assert.Equal("Amoxicillin", result.MedicineName);
}

[Fact]
public async Task GetOwnedFileAsync_RejectsAnotherPatientsPrescription()
{
    await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetOwnedFileAsync(99, prescription.Id));
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/PharmacyOrder.Api.Tests --filter FullyQualifiedName~PrescriptionServiceTests`

Expected: FAIL because `PrescriptionService` does not exist.

- [ ] **Step 3: Implement service validation and controller endpoints**

```csharp
[HttpPost]
public async Task<ActionResult<PrescriptionResponseDto>> Upload([FromForm] PrescriptionUploadRequestDto request, CancellationToken cancellationToken)
{
    var patientId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var result = await _prescriptions.UploadAsync(patientId, request, cancellationToken);
    return CreatedAtAction(nameof(DownloadFile), new { id = result.Id }, result);
}
```

Require the Patient policy on the controller. Reject a missing file, a medicine that is inactive, non-prescription, or out of stock, and quantity outside `1..medicine.StockQuantity` with a validation response. Use `Path.GetFileName(file.FileName)` for `OriginalFileName`. `GET /api/prescriptions` returns caller-owned records ordered by newest first. `GET /api/prescriptions/{id}/file` returns `NotFound` when the record is not owned by the caller and returns `FileStreamResult` with the saved content type otherwise.

- [ ] **Step 4: Run focused service tests and generate migration**

Run:

```powershell
dotnet test tests/PharmacyOrder.Api.Tests --filter FullyQualifiedName~PrescriptionServiceTests
dotnet ef migrations add AddPrescriptionUploadDetails --project src/PharmacyOrder.Api --startup-project src/PharmacyOrder.Api
```

Expected: tests PASS; migration adds prescription columns, medicine FK, and an index on `UploadedBy`.

- [ ] **Step 5: Commit**

```powershell
git add src/PharmacyOrder.Api/DTOs/Prescription src/PharmacyOrder.Api/Services src/PharmacyOrder.Api/Controllers/PrescriptionController.cs src/PharmacyOrder.Api/Program.cs src/PharmacyOrder.Api/Migrations tests/PharmacyOrder.Api.Tests/Unit/PrescriptionServiceTests.cs
git commit -m "add patient prescription upload API"
```

## Task 3: Add upload and confirmation UI

**Files:**
- Create: `src/api/prescriptionApi.js`
- Create: `src/pages/PatientPrescriptionUploadPage.jsx`
- Create: `src/pages/patientPrescriptionUpload.css`
- Modify: `src/pages/PatientMedicineDetailsPage.jsx`
- Modify: `src/routes/AppRoutes.jsx`
- Create: `src/pages/PatientPrescriptionUploadPage.test.jsx`

**Consumes:** `sessionStorage.carenova.pendingCartItem`, `catalogueApi.getMedicine`, `prescriptionApi.upload`.

**Produces:** `/patient/prescriptions/upload` with a valid selected prescription medicine, file guard, multipart request, and success confirmation.

- [ ] **Step 1: Write the failing upload-page test**

```jsx
it('shows the selected medicine and confirms a successful PDF upload', async () => {
  renderUploadPageWithPendingSelection({ medicineId: 3, quantity: 2, prescriptionRequired: true });
  expect(await screen.findByText(/Amoxicillin 250mg × 2/i)).toBeInTheDocument();
  await user.upload(screen.getByLabelText(/prescription file/i), new File(['pdf'], 'doctor-note.pdf', { type: 'application/pdf' }));
  await user.click(screen.getByRole('button', { name: /upload prescription/i }));
  expect(await screen.findByText(/prescription uploaded successfully/i)).toBeInTheDocument();
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npm run test:run -- PatientPrescriptionUploadPage.test.jsx`

Expected: FAIL because the page and route do not exist.

- [ ] **Step 3: Implement the upload route and page**

Create a `FormData` request with `medicineId`, `quantity`, and `file`. The page must refuse invalid extension or a browser-reported file larger than `10 * 1024 * 1024`, display the API error, and retain the selected summary. On success, clear `carenova.pendingCartItem`, show original filename, medicine, quantity, `Pending verification`, and a **Continue shopping** button to `/patient/medicines`. Change the GCN-26 prescription action to navigate here; keep non-prescription behavior unchanged for GCN-30.

- [ ] **Step 4: Run the focused frontend test**

Run: `npm run test:run -- PatientPrescriptionUploadPage.test.jsx`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/api/prescriptionApi.js src/pages/PatientPrescriptionUploadPage.jsx src/pages/patientPrescriptionUpload.css src/pages/PatientPrescriptionUploadPage.test.jsx src/pages/PatientMedicineDetailsPage.jsx src/routes/AppRoutes.jsx
git commit -m "add patient prescription upload page"
```

## Task 4: Add patient prescription history

**Files:**
- Create: `src/pages/PatientPrescriptionsPage.jsx`
- Create: `src/pages/patientPrescriptions.css`
- Modify: `src/components/DashboardLayout.jsx`
- Modify: `src/routes/AppRoutes.jsx`
- Create: `src/pages/PatientPrescriptionsPage.test.jsx`

**Consumes:** `prescriptionApi.list`, `prescriptionApi.getFileUrl`, Patient-only route protection.

**Produces:** `/patient/prescriptions`, an enabled Patient navigation entry, and a private record table.

- [ ] **Step 1: Write the failing history-page test**

```jsx
it('renders the patients uploaded prescription record', async () => {
  list.mockResolvedValue({ data: [{ id: 7, originalFileName: 'doctor-note.pdf', medicineName: 'Amoxicillin 250mg', quantity: 2, status: 'PendingVerification', uploadedAt: '2026-09-12T10:30:00Z' }] });
  render(<MemoryRouter><PatientPrescriptionsPage /></MemoryRouter>);
  expect(await screen.findByText('doctor-note.pdf')).toBeInTheDocument();
  expect(screen.getByText('Amoxicillin 250mg')).toBeInTheDocument();
  expect(screen.getByText('Pending verification')).toBeInTheDocument();
});
```

- [ ] **Step 2: Run it to verify it fails**

Run: `npm run test:run -- PatientPrescriptionsPage.test.jsx`

Expected: FAIL because the history page does not exist.

- [ ] **Step 3: Implement history, navigation, and file open action**

Add the Patient **Prescriptions** sidebar entry. The page calls `prescriptionApi.list()`, presents the approved table columns (file, medicine, quantity, status), displays `Pending verification` from `PendingVerification`, and gives each row an **Open file** link to `/api/prescriptions/{id}/file` using the existing authenticated Axios request path. Include loading, empty, error, and retry states.

- [ ] **Step 4: Run focused frontend tests and production build**

Run:

```powershell
npm run test:run -- PatientPrescriptionUploadPage.test.jsx PatientPrescriptionsPage.test.jsx
npm run build
```

Expected: all tests PASS and Vite build succeeds.

- [ ] **Step 5: Commit**

```powershell
git add src/pages/PatientPrescriptionsPage.jsx src/pages/patientPrescriptions.css src/pages/PatientPrescriptionsPage.test.jsx src/components/DashboardLayout.jsx src/routes/AppRoutes.jsx
git commit -m "add patient prescription history"
```

## Task 5: End-to-end verification

**Files:**
- Modify only if the checks expose a reproducible defect.

**Consumes:** completed API and frontend from Tasks 1–4.

**Produces:** verified upload-to-history flow with no unauthorised file access.

- [ ] **Step 1: Apply the new migration and start the API**

Run:

```powershell
dotnet ef database update --project src/PharmacyOrder.Api --startup-project src/PharmacyOrder.Api
dotnet run --project src/PharmacyOrder.Api --launch-profile http
```

- [ ] **Step 2: Verify the Patient flow manually**

Sign in as a Patient, choose an in-stock prescription-required medicine, select a valid quantity, upload a PDF or image under 10 MB, confirm the success details, visit **Prescriptions**, and open the owned file. Then try an invalid extension and a quantity greater than current stock; each must show a helpful error.

- [ ] **Step 3: Run backend and frontend verification**

Run:

```powershell
dotnet test tests/PharmacyOrder.Api.Tests
npm run test:run
npm run build
```

Expected: all test suites PASS and frontend build succeeds.

- [ ] **Step 4: Commit any corrective changes only if required**

```powershell
git -C 'D:\CareNova Dev\pharmacy-order-service\.worktrees\GCN-29-Prescription-Upload' add src tests
git -C 'D:\CareNova Dev\pharmacy-order-service\.worktrees\GCN-29-Prescription-Upload' commit -m "correct prescription upload flow"
git -C 'D:\CareNova Dev\frontend\.worktrees\GCN-29-Prescription-Upload' add src
git -C 'D:\CareNova Dev\frontend\.worktrees\GCN-29-Prescription-Upload' commit -m "correct prescription upload flow"
```

## Plan self-review

- Spec coverage: Tasks 1–2 cover the model, local storage, private API, validation, and no-Kafka constraint. Tasks 3–4 cover upload, confirmation, history, and navigation. Task 5 covers migration and end-to-end validation.
- Placeholder scan: no unfinished requirements or vague validation steps remain; every validation response and storage boundary is named.
- Type consistency: all later tasks use `PrescriptionUploadRequestDto`, `PrescriptionResponseDto`, `IPrescriptionService`, and `IFileStorageService.SavePrescriptionAsync` defined in Tasks 1–2.
