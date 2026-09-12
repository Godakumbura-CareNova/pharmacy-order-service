using PharmacyOrder.Api.DTOs.Prescription;

namespace PharmacyOrder.Api.Services.Interfaces;

public interface IPrescriptionService
{
    Task<PrescriptionResponseDto> UploadAsync(int patientId, PrescriptionUploadRequestDto request, CancellationToken cancellationToken);

    Task<IReadOnlyList<PrescriptionResponseDto>> GetForPatientAsync(int patientId);

    Task<PrescriptionFileResult> GetOwnedFileAsync(int patientId, int prescriptionId);
}
