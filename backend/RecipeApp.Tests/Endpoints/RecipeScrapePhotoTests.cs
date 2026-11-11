using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RecipeApp.API.DTOs.Scrape;
using RecipeApp.API.Enums;
using RecipeApp.API.Services;
using RecipeApp.API.Services.Llm;
using RecipeApp.Tests.Infrastructure;
using RecipeApp.Tests.Services;

namespace RecipeApp.Tests.Endpoints;

/// <summary>How an imported recipe's photo travels from the source page through the preview to a stored file.</summary>
[Collection("Database")]
public class RecipeScrapePhotoTests(DatabaseFixture db) : IAsyncLifetime
{
    private readonly string _imageFolder = Path.Combine(Path.GetTempPath(), "recipeapp-photo-tests", Guid.NewGuid().ToString("N"));

    public ValueTask InitializeAsync() => db.TruncateAllAsync();

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_imageFolder)) Directory.Delete(_imageFolder, recursive: true);
        return ValueTask.CompletedTask;
    }

    private const string PhotoUrl = "https://cdn.example.com/chili.jpg";

    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    private HttpClient CreateClient(
        StubHttpClientFactory http,
        int maxFileSizeMb = 10,
        int imageFetchTimeoutSeconds = 15,
        ILlmStructuredClient? llm = null)
    {
        var factory = db.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ImageStorage:BasePath"]                     = _imageFolder,
                ["ImageStorage:MaxFileSizeMb"]                = maxFileSizeMb.ToString(),
                ["RecipeScraping:ImageFetchTimeoutSeconds"]   = imageFetchTimeoutSeconds.ToString(),
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHttpClientFactory>();
                services.AddSingleton<IHttpClientFactory>(http);
                services.RemoveAll<IHeadlessRenderer>();
                services.AddSingleton<IHeadlessRenderer>(new FakeHeadlessRenderer());
                services.RemoveAll<IModelServerStatusProbe>();
                services.AddSingleton<IModelServerStatusProbe>(new StubModelServerStatusProbe(ModelServerState.Ready, "model.gguf"));
                if (llm is not null)
                {
                    services.RemoveAll<ILlmStructuredClient>();
                    services.AddSingleton(llm);
                }
            });
        });
        return factory.CreateClient();
    }

    private static ScrapeConfirmRequest ConfirmRequest(string? imageUrl, Guid? ingredientId = null) => new(
        Name:        "Chili",
        Description: null,
        SourceUrl:   "https://example.com/chili",
        Servings:    4,
        Ingredients: [new ScrapeConfirmIngredientRequest(
            IngredientId:             ingredientId,
            NewIngredientName:        ingredientId is null ? "kidney beans" : null,
            NewIngredientDisplayName: ingredientId is null ? "Kidney Beans" : null,
            Category:                 ingredientId is null ? IngredientCategory.Canned : null,
            Amount:                   400m,
            Unit:                     "g",
            Notes:                    null,
            DisplayOrder:             0)],
        Steps:    [new ScrapeConfirmStepRequest(1, "Simmer.", [0])],
        ImageUrl: imageUrl);

    private string[] StoredFiles => Directory.Exists(_imageFolder) ? Directory.GetFiles(_imageFolder) : [];

    private static async Task<ScrapeConfirmResponse> ConfirmAsync(HttpClient client, ScrapeConfirmRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/recipes/scrape/confirm", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ScrapeConfirmResponse>())!;
    }

    // ── Scrape ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Scrape_PreviewCarriesThePagesPhoto()
    {
        var page = """
            <html><head><meta property="og:image" content="/img/chili.jpg"></head>
            <body><script type="application/ld+json">
            {"@type":"Recipe","name":"Chili","recipeIngredient":["400 g kidney beans"],"recipeInstructions":["Simmer."]}
            </script></body></html>
            """;
        var extraction = new JsonObject
        {
            ["name"] = "Chili", ["description"] = null, ["servings"] = 4,
            ["ingredients"] = new JsonArray(new JsonObject
            {
                ["name"] = "kidney beans", ["display_name"] = "Kidney Beans", ["amount"] = 400, ["unit"] = "g", ["notes"] = null,
            }),
            ["steps"] = new JsonArray(new JsonObject
            {
                ["step_number"] = 1, ["instruction"] = "Simmer.", ["ingredient_indexes"] = new JsonArray(0), ["stated_amounts"] = new JsonArray(),
            }),
        };
        var client = CreateClient(StubHttpClientFactory.Returning(page), llm: new StubLlmStructuredClient(extraction));

        var response = await client.PostAsJsonAsync("/api/v1/recipes/scrape", new ScrapeRecipeRequest("https://example.com/chili"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ScrapePreviewResponse>())!.ImageUrl
            .Should().Be("https://example.com/img/chili.jpg");
    }

    // ── Confirm ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirm_WithAPhoto_StoresItAndSetsTheRecipesImage()
    {
        var http = new StubHttpClientFactory(_ => StubHttpClientFactory.Ok(JpegBytes, "application/octet-stream"));

        var body = await ConfirmAsync(CreateClient(http), ConfirmRequest(PhotoUrl));

        body.PhotoSkipped.Should().BeFalse();
        body.Recipe.ImageUrl.Should().MatchRegex(@"^/uploads/images/[0-9a-f-]{36}\.jpg$");
        http.RequestedUrls.Should().Equal(PhotoUrl);
        StoredFiles.Should().ContainSingle()
            .Which.Should().EndWith(Path.GetFileName(body.Recipe.ImageUrl!));
        (await File.ReadAllBytesAsync(StoredFiles[0])).Should().Equal(JpegBytes);
    }

    [Fact]
    public async Task Confirm_WithoutAPhoto_FetchesNothingAndSkipsNothing()
    {
        var http = new StubHttpClientFactory(_ => StubHttpClientFactory.Ok(JpegBytes, "image/jpeg"));

        var body = await ConfirmAsync(CreateClient(http), ConfirmRequest(imageUrl: null));

        body.PhotoSkipped.Should().BeFalse();
        body.Recipe.ImageUrl.Should().BeNull();
        http.RequestedUrls.Should().BeEmpty();
    }

    public static TheoryData<string, Func<HttpRequestMessage, HttpResponseMessage>> UnusableDownloads => new()
    {
        { "a failed download", _ => StubHttpClientFactory.Status(HttpStatusCode.NotFound) },
        // The server's word is not enough: a GIF labelled as a JPEG is still a GIF.
        { "the wrong type", _ => StubHttpClientFactory.Ok("GIF89a......"u8.ToArray(), "image/jpeg") },
        { "a page in place of a photo", _ => StubHttpClientFactory.Ok("<!DOCTYPE html><html></html>") },
        { "an oversized photo", _ => StubHttpClientFactory.Ok([.. JpegBytes, .. new byte[1024 * 1024]], "image/jpeg") },
    };

    [Theory]
    [MemberData(nameof(UnusableDownloads))]
    public async Task Confirm_PhotoUnusable_SavesWithoutItAndFlagsTheSkip(
        string because, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var client = CreateClient(new StubHttpClientFactory(respond), maxFileSizeMb: 1);

        var body = await ConfirmAsync(client, ConfirmRequest(PhotoUrl));

        body.PhotoSkipped.Should().BeTrue(because);
        body.Recipe.ImageUrl.Should().BeNull();
        body.Recipe.Name.Should().Be("Chili");
        StoredFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirm_PhotoDownloadTimesOut_SavesWithoutItAndFlagsTheSkip()
    {
        var http = new StubHttpClientFactory(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return StubHttpClientFactory.Ok(JpegBytes, "image/jpeg");
        });

        var body = await ConfirmAsync(CreateClient(http, imageFetchTimeoutSeconds: 1), ConfirmRequest(PhotoUrl));

        body.PhotoSkipped.Should().BeTrue();
        body.Recipe.ImageUrl.Should().BeNull();
    }

    [Fact]
    public async Task Confirm_ConnectionDropsMidDownload_SavesWithoutItAndFlagsTheSkip()
    {
        var http = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new DroppingStream()),
        });

        var body = await ConfirmAsync(CreateClient(http), ConfirmRequest(PhotoUrl));

        body.PhotoSkipped.Should().BeTrue();
        body.Recipe.ImageUrl.Should().BeNull();
    }

    /// <summary>A body whose connection resets on the first read.</summary>
    private sealed class DroppingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            throw new IOException("Connection reset by peer.");

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("Connection reset by peer.");
    }

    [Fact]
    public async Task Confirm_DatabaseSaveFails_RemovesTheStoredPhoto()
    {
        var http = new StubHttpClientFactory(_ => StubHttpClientFactory.Ok(JpegBytes, "image/jpeg"));

        // An ingredient that does not exist fails the save on its foreign key, and the test server rethrows the app's unhandled exception.
        var confirm = () => CreateClient(http).PostAsJsonAsync("/api/v1/recipes/scrape/confirm",
            ConfirmRequest(PhotoUrl, ingredientId: Guid.NewGuid()));

        await confirm.Should().ThrowAsync<DbUpdateException>();
        http.RequestedUrls.Should().Equal(PhotoUrl);
        StoredFiles.Should().BeEmpty();
    }
}
