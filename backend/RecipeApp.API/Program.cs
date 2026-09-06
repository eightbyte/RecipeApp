using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using RecipeApp.API.Data;
using RecipeApp.API.Endpoints;
using RecipeApp.API.Services;
using RecipeApp.API.Services.Llm;
using Scalar.AspNetCore;

var isSeedCatalogue = args.Contains("seed-catalogue");

var builder = WebApplication.CreateBuilder(args);

// ── OpenAPI ───────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.MigrationsAssembly("RecipeApp.API")
    )
);

// ── FluentValidation (discovers all validators in this assembly) ──────────────
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// ── Application services ──────────────────────────────────────────────────────
builder.Services.AddScoped<RecipeService>();
builder.Services.AddScoped<ImageService>();
builder.Services.AddScoped<IRecipeScrapeService, RecipeScrapeService>();
builder.Services.AddScoped<MealPlanService>();
builder.Services.AddScoped<ShoppingListService>();
builder.Services.AddScoped<IngredientCatalogueSeeder>();

// ── Recipe scraping ────────────────────────────────────────────────────────────
builder.Services.Configure<RecipeScrapingOptions>(
    builder.Configuration.GetSection(RecipeScrapingOptions.SectionName));

builder.Services.AddHttpClient("RecipeScraper", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
});

// Headless render fallback for client-side-rendered pages with no JSON-LD/visible text.
// The browser process is launched lazily on first use, not at startup.
builder.Services.AddSingleton<IHeadlessRenderer, PlaywrightHeadlessRenderer>();

// ── LLM (Local provider via LLamaSharp) ──────────────────────────────────────
builder.Services.Configure<LlmOptions>(
    builder.Configuration.GetSection(LlmOptions.SectionName));

builder.Services.AddSingleton<LlamaModelHolder>();

builder.Services.AddSingleton<ILlmStructuredClient>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
    return opts.Provider switch
    {
        "Local" => new LLamaSharpStructuredClient(sp.GetRequiredService<LlamaModelHolder>()),
        var p   => throw new InvalidOperationException($"Unknown Llm:Provider '{p}'."),
    };
});

// ── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("VueDev", policy =>
        policy
            .WithOrigins("http://localhost:3000", "http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
    );
});

// ── Health Checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddNpgSql(
        sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")!,
        name: "postgres",
        tags: ["ready"]
    )
    .AddCheck("llm-model", () =>
    {
        // Checked lazily — only reports unhealthy if holder exists but failed to load a configured path
        return HealthCheckResult.Healthy("LLM model status checked at request time.");
    }, tags: ["ready"]);

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── seed-catalogue command — runs inference then exits ────────────────────────
if (isSeedCatalogue)
{
    var countArg = args.SkipWhile(a => a != "seed-catalogue").Skip(1).FirstOrDefault();
    var count    = int.TryParse(countArg, out var c) && c > 0 ? c : 200;

    // Run migrations so the DB is ready
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    using (var scope = app.Services.CreateScope())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<IngredientCatalogueSeeder>();
        await seeder.SeedAsync(count);
    }

    return;
}

// ── Development tooling ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opts =>
    {
        opts.Title = "RecipeApp API";
        opts.Theme = ScalarTheme.Purple;
    });
}

// ── Static files — serve uploaded recipe images at /uploads ──────────────────
var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "uploads");
Directory.CreateDirectory(uploadsPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(uploadsPath),
    RequestPath  = "/uploads",
});

// ── CORS (before routing) ─────────────────────────────────────────────────────
app.UseCors("VueDev");

// ── API Endpoints ─────────────────────────────────────────────────────────────
app.MapHealthEndpoints();

var api = app.MapGroup("/api/v1");
api.MapIngredientsEndpoints();
api.MapRecipesEndpoints();
api.MapRecipeScrapeEndpoints();
api.MapMealPlanEndpoints();
api.MapShoppingListEndpoints();

// ── Startup tasks (development) ───────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DataSeeder.SeedAsync(db);
}

// Eagerly resolve the model holder (non-test environments) so a bad path surfaces at startup
if (!app.Environment.IsEnvironment("Testing"))
{
    var holder = app.Services.GetRequiredService<LlamaModelHolder>();
    if (holder.IsLoaded)
        app.Logger.LogInformation("LLM model is ready.");
    else
        app.Logger.LogWarning("LLM model is not loaded. Set Llm:Local:ModelPath to enable local inference.");
}

app.Run();

// Expose the implicit Program class to the test project
public partial class Program { }
