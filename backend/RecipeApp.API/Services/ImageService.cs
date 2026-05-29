using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;

namespace RecipeApp.API.Services;

public class ImageService(IConfiguration config, IWebHostEnvironment env)
{
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

        return $"/uploads/images/{fileName}";
    }

    public void Delete(string? imageUrl)
    {
        if (string.IsNullOrEmpty(imageUrl)) return;
        var fileName = Path.GetFileName(imageUrl);
        var filePath = Path.Combine(UploadRoot, fileName);
        if (File.Exists(filePath)) File.Delete(filePath);
    }
}
