namespace PharmacyOrder.Api.Services.Interfaces;

public interface IPrescriptionCleanupService
{
    Task DeletePendingAsync(Guid patientId, int prescriptionId, CancellationToken cancellationToken);
}
