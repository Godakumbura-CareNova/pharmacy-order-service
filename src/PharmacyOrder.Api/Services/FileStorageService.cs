using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Services;

public class FileStorageService : IFileStorageService
{
    private readonly FileStorageSettings _settings;
    public FileStorageService(IOptions<FileStorageSettings> settings) => _settings = settings.Value;

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
}
