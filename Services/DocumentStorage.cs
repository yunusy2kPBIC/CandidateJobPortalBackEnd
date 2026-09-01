using CandidatePortal.Api.Configuration;

namespace CandidatePortal.Api.Services;

public sealed class DocumentStorage(PortalOptions options)
{
    public async Task<string> SaveResumeAsync(int userId, IFormFile upload, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(options.LocalStoragePath, AppContext.BaseDirectory);
        var resumeDirectory = Path.Combine(root, "resumes");
        Directory.CreateDirectory(resumeDirectory);
        var extension = Path.GetExtension(upload.FileName).ToLowerInvariant();
        var destination = Path.Combine(resumeDirectory, $"{userId}-{Guid.NewGuid():N}{extension}");
        await using var stream = File.Create(destination);
        await upload.CopyToAsync(stream, cancellationToken);
        return destination;
    }
}
