namespace PharmacyOrder.Api.Config;

public class PrescriptionStorageSettings
{
    public string UploadPath { get; set; } = "uploads/prescriptions";

    public int MaxFileSizeMb { get; set; } = 10;

    public string[] AllowedExtensions { get; set; } = Array.Empty<string>();
}
