using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.DTOs.Prescription;

namespace PharmacyOrder.Api.Tests.Integration;

/// <summary>GCN-29: prescription upload through the real HTTP pipeline (multipart, JWT, file storage).</summary>
public class PrescriptionControllerIntegrationTests : IClassFixture<PrescriptionApiFactory>
{
    private const string PrescriptionsUrl = "/api/prescriptions";

    private readonly PrescriptionApiFactory _factory;
    private readonly Guid _patientId = Guid.NewGuid();
    private readonly HttpClient _patient;

    public PrescriptionControllerIntegrationTests(PrescriptionApiFactory factory)
    {
        _factory = factory;
        _patient = factory.CreatePatientClient(_patientId);
    }

    private static MultipartFormDataContent UploadForm(int medicineId, int quantity, string? fileName = "doctor-note.pdf", string content = "%PDF-1.4 prescription")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(medicineId.ToString()), "medicineId" },
            { new StringContent(quantity.ToString()), "quantity" }
        };
        if (fileName is not null)
        {
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
            file.Headers.ContentType = new MediaTypeHeaderValue(fileName.EndsWith(".pdf") ? "application/pdf" : "application/octet-stream");
            form.Add(file, "file", fileName);
        }
        return form;
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return body!["message"].ToString()!;
    }

    // 1 — a visitor without a token cannot upload
    [Fact]
    public async Task Upload_WithoutToken_Returns401()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Rx-Anon");

        var response = await _factory.CreateClientAs(role: null).PostAsync(PrescriptionsUrl, UploadForm(medicine.Id, 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // 2 — Scenario 2: valid PDF → 201, medicine + quantity + file stored against the signed-in customer
    [Fact]
    public async Task Upload_ValidPdf_Returns201AndStoresMedicineQuantityAndFile()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Rx-Valid", stock: 8);

        var response = await _patient.PostAsync(PrescriptionsUrl, UploadForm(medicine.Id, 3));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PrescriptionResponseDto>();
        Assert.Equal(medicine.Id, body!.MedicineId);
        Assert.Equal("IT-Rx-Valid", body.MedicineName);
        Assert.Equal(3, body.Quantity);
        Assert.Equal("doctor-note.pdf", body.OriginalFileName);
        Assert.Equal("PendingVerification", body.Status);

        var stored = await _factory.QueryAsync(db => db.Prescriptions.SingleAsync(p => p.Id == body.Id));
        Assert.Equal(_patientId, stored.UploadedBy);
        Assert.True(File.Exists(Path.Combine(_factory.UploadPath, Path.GetFileName(stored.FilePath))), "Uploaded file should exist on disk.");
    }

    // 3 — Scenario 3: prescription medicine without a file is blocked with a warning
    [Fact]
    public async Task Upload_WithoutFile_Returns400WithWarning()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Rx-NoFile");

        var response = await _patient.PostAsync(PrescriptionsUrl, UploadForm(medicine.Id, 1, fileName: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("A prescription file is required.", await MessageAsync(response));
        Assert.Equal(0, await _factory.QueryAsync(db => db.Prescriptions.CountAsync(p => p.MedicineId == medicine.Id)));
    }

    // 4 — unsupported file format → error message
    [Fact]
    public async Task Upload_UnsupportedFormat_Returns400WithMessage()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Rx-BadFormat");

        var response = await _patient.PostAsync(PrescriptionsUrl, UploadForm(medicine.Id, 1, fileName: "prescription.exe"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Only PDF, JPG, JPEG, and PNG prescription files are supported.", await MessageAsync(response));
    }

    // 5 — Scenario 1 (API side): a non-prescription medicine never goes through the prescription flow
    [Fact]
    public async Task Upload_ForNonPrescriptionMedicine_Returns404()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-NoRx", prescriptionRequired: false);

        var response = await _patient.PostAsync(PrescriptionsUrl, UploadForm(medicine.Id, 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Prescription medicine is unavailable.", await MessageAsync(response));
    }

    // 6 — quantity above the available stock is rejected
    [Fact]
    public async Task Upload_QuantityAboveStock_Returns400()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Rx-Qty", stock: 2);

        var response = await _patient.PostAsync(PrescriptionsUrl, UploadForm(medicine.Id, 5));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Quantity must be between 1 and 2.", await MessageAsync(response));
    }

    // 7 — GET /api/prescriptions returns only the caller's uploads
    [Fact]
    public async Task GetMine_ReturnsOnlyOwnUploads()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Rx-Mine");
        var otherPatient = _factory.CreatePatientClient(Guid.NewGuid());
        await _patient.PostAsync(PrescriptionsUrl, UploadForm(medicine.Id, 1, "mine.pdf"));
        await otherPatient.PostAsync(PrescriptionsUrl, UploadForm(medicine.Id, 1, "theirs.pdf"));

        var mine = await _patient.GetFromJsonAsync<List<PrescriptionResponseDto>>(PrescriptionsUrl);

        Assert.Contains(mine!, p => p.OriginalFileName == "mine.pdf");
        Assert.DoesNotContain(mine!, p => p.OriginalFileName == "theirs.pdf");
    }

    // 8 — the uploaded file can be downloaded by its owner only
    [Fact]
    public async Task DownloadFile_OwnerGetsFile_OtherCustomerGets404()
    {
        var medicine = await _factory.SeedMedicineAsync("IT-Rx-Download");
        var created = await (await _patient.PostAsync(PrescriptionsUrl, UploadForm(medicine.Id, 1, content: "%PDF-1.4 secret"))).Content.ReadFromJsonAsync<PrescriptionResponseDto>();

        var owner = await _patient.GetAsync($"{PrescriptionsUrl}/{created!.Id}/file");
        var stranger = await _factory.CreatePatientClient(Guid.NewGuid()).GetAsync($"{PrescriptionsUrl}/{created.Id}/file");

        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        Assert.Equal("application/pdf", owner.Content.Headers.ContentType!.MediaType);
        Assert.Equal("%PDF-1.4 secret", await owner.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
    }
}
