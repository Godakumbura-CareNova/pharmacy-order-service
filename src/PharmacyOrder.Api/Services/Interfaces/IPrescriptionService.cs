using PharmacyOrder.Api.DTOs.Prescription;

namespace PharmacyOrder.Api.Services.Interfaces;

public interface IPrescriptionService
{
    Task<PrescriptionResponseDto> UploadAsync(Guid patientId, PrescriptionUploadRequestDto request, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrescriptionResponseDto>> GetForPatientAsync(Guid patientId);

    Task DeletePendingAsync(Guid patientId, int prescriptionId, CancellationToken cancellationToken);

    Task<PrescriptionFileResult> GetOwnedFileAsync(Guid patientId, int prescriptionId);
}
