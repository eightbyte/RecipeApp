using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;

namespace RecipeApp.API.Services;

public class ImageService(IConfiguration config, IWebHostEnvironment env)
{
    /// <summary>URL path the stored files are served under (see the static-files mapping in Program.cs).</summary>
    public const string PublicPathPrefix = "/uploads/images/";

    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    private static readonly string[] AllowedContentTypes =
        ["image/jpeg", "image/png", "image/webp"];

    private long MaxBytes =>
        config.GetValue("ImageStorage:MaxFileSizeMb", 10) * 1024L * 1024L;

    private string UploadRoot =>
        Path.Combine(env.ContentRootPath,
            config.GetValue<string>("ImageStorage:BasePath") ?? "uploads/images");

    public (bool ok, string? error) Validate(IFormFile file)
    {
        if (file.Length > MaxBytes)
            return (false, $"File exceeds the maximum size of {MaxBytes / 1024 / 1024} MB.");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return (false, $"Unsupported file type. Allowed: {string.Join(", ", AllowedExtensions)}");

        if (!AllowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
            return (false, "Invalid content type.");

        return (true, null);
    }

    public async Task<string> SaveAsync(IFormFile file)
    {
        Directory.CreateDirectory(UploadRoot);
        var ext      = Path.GetExtension(file.FileName).ToLowerInvariant();
        var fileName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(UploadRoot, fileName);

        await using var stream = File.Create(filePath);
        await file.CopyToAsync(stream);

        return PublicPathPrefix + fileName;
    }

    /// <summary>
    /// Copies an image already on disk into storage under a caller-chosen name, overwriting any file
    /// of that name. For the Phase 9 seed import, whose photos are harvested files rather than
    /// uploads and whose names must be deterministic so a re-import replaces its own photo.
    /// </summary>
    /// <param name="sourcePath">The image to copy.</param>
    /// <param name="fileNameStem">Name without extension; the source's extension is kept.</param>
    /// <returns>The public URL, in the same form <see cref="SaveAsync"/> returns.</returns>
    public async Task<string> ImportAsync(string sourcePath, string fileNameStem, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException($"Unsupported image type '{ext}' for {sourcePath}.");

        // The stem is a validated slug with a fixed prefix, but a path separator in it would write
        // outside storage, so it is refused here rather than trusted.
        if (fileNameStem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"'{fileNameStem}' is not a usable file name.", nameof(fileNameStem));

        Directory.CreateDirectory(UploadRoot);
        var fileName = fileNameStem + ext;

        await using (var source = File.OpenRead(sourcePath))
        await using (var target = File.Create(Path.Combine(UploadRoot, fileName)))
            await source.CopyToAsync(target, ct);

        return PublicPathPrefix + fileName;
    }

    public void Delete(string? imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl)) return;
        var fileName = Path.GetFileName(imageUrl);
        var filePath = Path.Combine(UploadRoot, fileName);
        if (File.Exists(filePath)) File.Delete(filePath);
    }
}
