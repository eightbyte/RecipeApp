using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using RecipeApp.API.Services;

namespace RecipeApp.Tests.Services;

public class ImageServiceTests
{
    private static ImageService CreateService(int maxFileSizeMb = 10)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ImageStorage:MaxFileSizeMb"] = maxFileSizeMb.ToString(),
            })
            .Build();

        return new ImageService(config, new StubWebHostEnvironment());
    }

    private static FakeFormFile MakeFile(string fileName, string contentType, long sizeBytes) =>
        new() { FileName = fileName, ContentType = contentType, Length = sizeBytes };

    private const long OneMb = 1024L * 1024L;

    [Fact]
    public void Validate_ValidJpeg_ReturnsOk()
    {
        var (ok, _) = CreateService().Validate(MakeFile("photo.jpg", "image/jpeg", 5 * OneMb));
        ok.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidPng_ReturnsOk()
    {
        var (ok, _) = CreateService().Validate(MakeFile("photo.png", "image/png", 1 * OneMb));
        ok.Should().BeTrue();
    }

    [Fact]
    public void Validate_ValidWebp_ReturnsOk()
    {
        var (ok, _) = CreateService().Validate(MakeFile("photo.webp", "image/webp", 2 * OneMb));
        ok.Should().BeTrue();
    }

    [Fact]
    public void Validate_InvalidExtension_ReturnsError()
    {
        var (ok, error) = CreateService().Validate(MakeFile("photo.gif", "image/gif", 1 * OneMb));
        ok.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Validate_FileTooLarge_ReturnsError()
    {
        var (ok, error) = CreateService().Validate(MakeFile("photo.jpg", "image/jpeg", 11 * OneMb));
        ok.Should().BeFalse();
        error.Should().Contain("maximum");
    }

    [Fact]
    public void Validate_ExactlyAtSizeLimit_ReturnsOk()
    {
        var (ok, _) = CreateService().Validate(MakeFile("photo.jpg", "image/jpeg", 10 * OneMb));
        ok.Should().BeTrue();
    }

    [Fact]
    public void Validate_MismatchedContentType_ReturnsError()
    {
        // .jpg extension but gif content-type
        var (ok, error) = CreateService().Validate(MakeFile("photo.jpg", "image/gif", 1 * OneMb));
        ok.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    // ── Test doubles ─────────────────────────────────────────────────────────

    private sealed class FakeFormFile : IFormFile
    {
        public string ContentType { get; init; } = "image/jpeg";
        public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{FileName}\"";
        public IHeaderDictionary Headers => new HeaderDictionary();
        public long Length { get; init; }
        public string Name => "file";
        public string FileName { get; init; } = "test.jpg";

        public void CopyTo(Stream target) { }
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Stream OpenReadStream() => new MemoryStream(new byte[Math.Max(0, (int)Length)]);
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "RecipeApp.API";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Testing";
    }
}
