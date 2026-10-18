using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Unit;

public class FileStorageServiceTests
{
    [Fact]
    public async Task SaveMedicineImageAsync_StoresAllowedImageWithGeneratedName()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var settings = Options.Create(new FileStorageSettings
        {
            UploadPath = directory,
            MaxFileSizeMb = 5,
            AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" }
        });
        var prescriptionSettings = Options.Create(new PrescriptionStorageSettings
        {
            UploadPath = Path.Combine(directory, "prescriptions"),
            MaxFileSizeMb = 10,
            AllowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png" }
        });
        var service = new FileStorageService(settings, prescriptionSettings);
        await using var source = new MemoryStream(new byte[] { 1, 2, 3 });
        var file = new FormFile(source, 0, source.Length, "image", "medicine.png");

        var savedPath = await service.SaveMedicineImageAsync(file, CancellationToken.None);

        Assert.StartsWith("/uploads/medicines/", savedPath);
        Assert.True(File.Exists(Path.Combine(directory, Path.GetFileName(savedPath))));
        Directory.Delete(directory, true);
    }
}
