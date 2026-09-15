using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using PharmacyOrder.Api.Config;
using PharmacyOrder.Api.Services;

namespace PharmacyOrder.Api.Tests.Unit;

/// <summary>GCN-29 unit tests 6–7 for the prescription file storage rules.</summary>
public class PrescriptionStorageQaTests : IDisposable
{
    private readonly string _uploadPath = Path.Combine(Path.GetTempPath(), "carenova-prescriptions", Guid.NewGuid().ToString("N"));

    private FileStorageService CreateService() => new(
        Options.Create(new FileStorageSettings { AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" } }),
        Options.Create(new PrescriptionStorageSettings { UploadPath = _uploadPath, MaxFileSizeMb = 10, AllowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png" } }));

    private static IFormFile File(string name, string content = "%PDF-1.4 prescription")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name) { Headers = new HeaderDictionary() };
    }

    // 6 — a supported file is written to disk under a generated name and the stored path resolves back to it
    [Fact]
    public async Task SavePrescriptionAsync_SupportedFile_IsStoredAndResolvable()
    {
        var service = CreateService();

        var stored = await service.SavePrescriptionAsync(File("doctor-note.PDF"), CancellationToken.None);
        var physical = service.GetPrescriptionFilePath(stored);

        Assert.StartsWith("prescriptions/", stored);
        Assert.EndsWith(".pdf", stored);
        Assert.DoesNotContain("doctor-note", stored);
        Assert.True(System.IO.File.Exists(physical), $"Expected stored file at {physical}");
        Assert.Equal("%PDF-1.4 prescription", await System.IO.File.ReadAllTextAsync(physical));
    }

    // 7 — "Uploaded file is not a supported format → show an error message" (and empty / oversized files are refused)
    [Fact]
    public async Task SavePrescriptionAsync_UnsupportedEmptyOrOversizedFile_IsRejectedWithMessage()
    {
        var service = CreateService();

        var unsupported = await Assert.ThrowsAsync<ArgumentException>(() => service.SavePrescriptionAsync(File("malware.exe"), CancellationToken.None));
        var wordDoc = await Assert.ThrowsAsync<ArgumentException>(() => service.SavePrescriptionAsync(File("note.docx"), CancellationToken.None));
        var empty = await Assert.ThrowsAsync<ArgumentException>(() => service.SavePrescriptionAsync(File("empty.pdf", ""), CancellationToken.None));
        var oversized = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.SavePrescriptionAsync(new FormFile(Stream.Null, 0, 11L * 1024 * 1024, "file", "huge.pdf"), CancellationToken.None));

        Assert.Equal("Only PDF, JPG, JPEG, and PNG prescription files are supported.", unsupported.Message);
        Assert.Equal("Only PDF, JPG, JPEG, and PNG prescription files are supported.", wordDoc.Message);
        Assert.Equal("Prescription file must be between 1 byte and 10 MB.", empty.Message);
        Assert.Equal("Prescription file must be between 1 byte and 10 MB.", oversized.Message);
        Assert.False(Directory.Exists(_uploadPath) && Directory.EnumerateFiles(_uploadPath).Any(), "Nothing should be written for rejected files.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_uploadPath)) Directory.Delete(_uploadPath, recursive: true);
    }
}
