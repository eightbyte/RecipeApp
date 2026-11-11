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

    /// <summary>The largest image storage accepts, from <c>ImageStorage:MaxFileSizeMb</c>.</summary>
    public long MaxBytes =>
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

    /// <summary>Checks an image already on disk against the upload rules before <see cref="ImportAsync"/> copies it, so importers can report every problem up front.</summary>
    /// <returns>Null when the file can be imported; otherwise why not.</returns>
    public string? ValidateImportSource(string sourcePath)
    {
        if (!File.Exists(sourcePath))
            return $"no image file at {sourcePath}";

        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return $"unsupported image type '{ext}'; allowed: {string.Join(", ", AllowedExtensions)}";

        var length = new FileInfo(sourcePath).Length;
        if (length > MaxBytes)
            return $"image is {length / 1024 / 1024} MB; the maximum is {MaxBytes / 1024 / 1024} MB";

        return null;
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

    /// <summary>Stores downloaded bytes under the upload rules, judging the type by the bytes alone because a remote server's content type is not proof.</summary>
    /// <returns>The public URL, or null when the bytes are not a jpg, png or webp within <see cref="MaxBytes"/>.</returns>
    public async Task<string?> SaveDownloadAsync(byte[] content, CancellationToken ct = default)
    {
        if (content.Length > MaxBytes) return null;
        if (DetectExtension(content) is not { } ext || !AllowedExtensions.Contains(ext)) return null;

        Directory.CreateDirectory(UploadRoot);
        var fileName = $"{Guid.NewGuid()}{ext}";
        await File.WriteAllBytesAsync(Path.Combine(UploadRoot, fileName), content, ct);

        return PublicPathPrefix + fileName;
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The file extension implied by a payload's own magic bytes, or null if unrecognised.</summary>
    public static string? DetectExtension(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
            return ".jpg";

        if (content.Length >= 8 && content[..8].SequenceEqual(PngSignature))
            return ".png";

        if (content.Length >= 12
            && content[..4].SequenceEqual("RIFF"u8)
            && content[8..12].SequenceEqual("WEBP"u8))
            return ".webp";

        if (content.Length >= 6 && content[..4].SequenceEqual("GIF8"u8))
            return ".gif";

        return null;
    }

    /// <summary>Copies an image already on disk into storage under a caller-chosen name, overwriting any file of that name, so re-imports replace their own photo.</summary>
    /// <param name="fileNameStem">Name without extension; the source's extension is kept.</param>
    /// <returns>The public URL, in the same form <see cref="SaveAsync"/> returns.</returns>
    public async Task<string> ImportAsync(string sourcePath, string fileNameStem, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            throw new InvalidOperationException($"Unsupported image type '{ext}' for {sourcePath}.");

        // The stem is a validated slug with a fixed prefix, but a path separator in it would write outside storage, so it is refused.
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
