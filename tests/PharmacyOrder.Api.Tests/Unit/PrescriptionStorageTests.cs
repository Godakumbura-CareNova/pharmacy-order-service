using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Unit;

public class PrescriptionStorageTests : IDisposable
{
    private readonly string _uploadPath = Path.Combine(Path.GetTempPath(), "carenova-prescriptions", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SavePrescriptionAsync_SavesAllowedPdfWithGeneratedName()
    {
        var service = CreateService();

        var path = await service.SavePrescriptionAsync(CreateFile("doctor-note.pdf", "application/pdf"), CancellationToken.None);

        Assert.StartsWith("prescriptions/", path);
        Assert.EndsWith(".pdf", path);
        Assert.True(File.Exists(Path.Combine(_uploadPath, Path.GetFileName(path))));
    }

    [Fact]
    public async Task SavePrescriptionAsync_RejectsUnsupportedExtension()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() => service.SavePrescriptionAsync(CreateFile("script.exe", "application/octet-stream"), CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_uploadPath)) Directory.Delete(_uploadPath, true);
    }

    private FileStorageService CreateService()
    {
        var imageSettings = Options.Create(new FileStorageSettings());
        var prescriptionSettings = Options.Create(new PrescriptionStorageSettings
        {
            UploadPath = _uploadPath,
            MaxFileSizeMb = 10,
            AllowedExtensions = [".pdf", ".jpg", ".jpeg", ".png"]
        });
        return new FileStorageService(imageSettings, prescriptionSettings);
    }

    private static IFormFile CreateFile(string name, string contentType)
    {
        var content = new MemoryStream("prescription"u8.ToArray());
        return new FormFile(content, 0, content.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = contentType };
    }
}
