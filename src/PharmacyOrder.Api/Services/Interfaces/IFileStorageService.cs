using Microsoft.AspNetCore.Http;

namespace PharmacyOrder.Api.Services.Interfaces;

public interface IFileStorageService
{
    Task<string> SaveMedicineImageAsync(IFormFile file, CancellationToken cancellationToken);

    Task<string> SavePrescriptionAsync(IFormFile file, CancellationToken cancellationToken);

    string GetPrescriptionFilePath(string storedPath);
}
