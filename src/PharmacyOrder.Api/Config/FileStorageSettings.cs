namespace PharmacyOrder.Api.Config;

public class FileStorageSettings
{
    public string UploadPath { get; set; } = "wwwroot/uploads/medicines";
    public int MaxFileSizeMb { get; set; } = 5;
    public string[] AllowedExtensions { get; set; } = Array.Empty<string>();
}
