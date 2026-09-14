using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Services;

public class FileStorageService : IFileStorageService
{
    private readonly FileStorageSettings _settings;
    private readonly PrescriptionStorageSettings _prescriptionSettings;

    public FileStorageService(
        IOptions<FileStorageSettings> settings,
        IOptions<PrescriptionStorageSettings> prescriptionSettings)
    {
        _settings = settings.Value;
        _prescriptionSettings = prescriptionSettings.Value;
    }

    public async Task<string> SaveMedicineImageAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > _settings.MaxFileSizeMb * 1024 * 1024) throw new ArgumentException("Image must be between 1 byte and 5 MB.");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_settings.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Only JPG, JPEG, PNG, and WEBP images are supported.");
        Directory.CreateDirectory(_settings.UploadPath);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(_settings.UploadPath, fileName);
        await using var output = File.Create(path);
        await file.CopyToAsync(output, cancellationToken);
        return $"/uploads/medicines/{fileName}";
    }

    public async Task<string> SavePrescriptionAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0 || file.Length > _prescriptionSettings.MaxFileSizeMb * 1024 * 1024)
            throw new ArgumentException("Prescription file must be between 1 byte and 10 MB.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_prescriptionSettings.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Only PDF, JPG, JPEG, and PNG prescription files are supported.");

        var directory = Path.GetFullPath(_prescriptionSettings.UploadPath);
        Directory.CreateDirectory(directory);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(directory, fileName);

        await using var output = File.Create(path);
        await file.CopyToAsync(output, cancellationToken);
        return $"prescriptions/{fileName}";
    }

    public string GetPrescriptionFilePath(string storedPath)
    {
        var fileName = Path.GetFileName(storedPath);
        return Path.Combine(Path.GetFullPath(_prescriptionSettings.UploadPath), fileName);
    }

    public Task DeletePrescriptionAsync(string storedPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetPrescriptionFilePath(storedPath);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
