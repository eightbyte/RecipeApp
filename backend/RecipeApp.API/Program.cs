using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using RecipeApp.API.Data;
using RecipeApp.API.Endpoints;
using RecipeApp.API.Services;
using RecipeApp.API.Services.Llm;
using RecipeApp.API.Services.RecipePacks;
using RecipeApp.API.Services.Seeding;
using Scalar.AspNetCore;

// The old token is still recognised so it can be answered with a message naming its replacement rather than silently starting the web host.
const string ImportCatalogueCommand = "import-catalogue";
const string RetiredSeedCatalogueCommand = "seed-catalogue";

var isImportCatalogue = args.Contains(ImportCatalogueCommand);
var isRetiredSeedCatalogue = args.Contains(RetiredSeedCatalogueCommand);
var isSeedDensities = args.Contains("seed-densities");

// seed-recipes and import-recipes take flags of their own, so everything after the command token is withheld from the host's command-line configuration.
var seedRecipesIndex = SeedRecipesCommand.IndexIn(args);
var isSeedRecipes    = seedRecipesIndex >= 0;
var seedRecipesArgs  = isSeedRecipes ? args[(seedRecipesIndex + 1)..] : [];

var importRecipesIndex = ImportRecipesCommand.IndexIn(args);
var isImportRecipes    = importRecipesIndex >= 0;
var importRecipesArgs  = isImportRecipes ? args[(importRecipesIndex + 1)..] : [];

var commandIndex = new[] { seedRecipesIndex, importRecipesIndex }.Where(index => index >= 0).DefaultIfEmpty(-1).Min();
var hostArgs     = commandIndex >= 0 ? args[..commandIndex] : args;

// --force re-applies the artefact over existing rows, read from the host arguments only so import-recipes' own --force never force-imports the catalogue.
var catalogueForce = hostArgs.Contains("--force");

var builder = WebApplication.CreateBuilder(hostArgs);

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
builder.Services.AddSingleton<MeasurementConverter>();

// ── Measurement handling ──────────────────────────────────────────────────────
builder.Services.Configure<MeasurementOptions>(
    builder.Configuration.GetSection(MeasurementOptions.SectionName));

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

// Headless render fallback for client-side-rendered pages, launched lazily on first use.
builder.Services.AddSingleton<IHeadlessRenderer, PlaywrightHeadlessRenderer>();

// ── Seed recipe library (USDA MyPlate import) ────────────────────────────────
builder.Services.Configure<RecipeSeedingOptions>(
    builder.Configuration.GetSection(RecipeSeedingOptions.SectionName));

builder.Services.AddSingleton<SeedCacheStore>();
builder.Services.AddSingleton<IWaybackHarvester, WaybackHarvester>();
builder.Services.AddSingleton<MyPlateRecipeParser>();
builder.Services.AddSingleton<SeedRecipeNormaliser>();
builder.Services.AddSingleton<IRecipeLibrarySeeder, RecipeLibrarySeeder>();

// Stage 4.5: the store is needed by every run because import-catalogue reads the artefact, but the builder only by the machine that authors it.
builder.Services.AddSingleton<IngredientCatalogueFileStore>();
builder.Services.AddSingleton<CatalogueBuildFileStore>();
builder.Services.AddSingleton<IngredientCatalogueBuilder>();
builder.Services.AddScoped<SeedCatalogueResolver>();
builder.Services.AddScoped<SeedRecipePersister>();

// ── Recipe packs ─────────────────────────────────────────────────────────────
builder.Services.Configure<RecipePackOptions>(
    builder.Configuration.GetSection(RecipePackOptions.SectionName));

builder.Services.AddSingleton<RecipePackFileStore>();
builder.Services.AddScoped<RecipePackImporter>();
builder.Services.AddScoped<FirstRunSeeder>();

builder.Services.AddHttpClient(WaybackHarvester.HttpClientName, (sp, client) =>
{
    var seeding = sp.GetRequiredService<IOptions<RecipeSeedingOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(seeding.FetchTimeoutSeconds);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(seeding.UserAgent);
});

// ── LLM (the Model Server, llama.cpp's llama-server over HTTP) ───────────────
builder.Services.Configure<LlmOptions>(
    builder.Configuration.GetSection(LlmOptions.SectionName));

// No timeout: each caller's LlmTimeoutSeconds budget owns the limit, so a long completion surfaces as LlmTimeout rather than a failure.
builder.Services.AddHttpClient(ModelServerClient.HttpClientName, client =>
    client.Timeout = Timeout.InfiniteTimeSpan);

builder.Services.AddSingleton<ILlmStructuredClient, ModelServerClient>();
builder.Services.AddSingleton<IModelServerStatusProbe, ModelServerStatusProbe>();

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
// The Model Server is deliberately absent: the updater rolls back on a failed readiness check, and the server is off most of the time.
builder.Services.AddHealthChecks()
    .AddNpgSql(
        sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")!,
        name: "postgres",
        tags: ["ready"]
    );

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── seed-densities command — assigns curated densities then exits ─────────────
// Idempotent and inference-free, so it is safe to re-run whenever the catalogue grows.
if (isSeedDensities)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await IngredientDensitySeeder.SeedAsync(db, app.Logger);
    return;
}

// ── seed-recipes command — harvests the USDA MyPlate library then exits ──────
// Stages 1-4 run without a database, and Stage 5 migrates and imports the catalogue itself before persisting anything.
if (isSeedRecipes)
{
    // Ctrl+C is a normal way to stop a long harvest, and cancelling cooperatively lets the cache flush the last page.
    using var harvestCancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        harvestCancellation.Cancel();
    };

    Environment.ExitCode = await SeedRecipesCommand.RunAsync(
        app.Services, app.Logger, seedRecipesArgs, harvestCancellation.Token);
    return;
}

// ── import-recipes command — imports one recipe pack then exits ──────────────
// Migrates and loads the catalogue first, so it works against an empty database.
if (isImportRecipes)
{
    Environment.ExitCode = await ImportRecipesCommand.RunAsync(app.Services, app.Logger, importRecipesArgs);
    return;
}

// ── seed-catalogue — retired ─────────────────────────────────────────────────
// Answered rather than ignored, because silently starting the web host would look like the command had run.
if (isRetiredSeedCatalogue)
{
    app.Logger.LogError(
        "'{Retired}' was retired and no longer exists. Use 'dotnet run -- {Replacement}' instead: " +
        "it loads the curated catalogue from seed-data/ingredient-catalogue.json with no LLM and no network.",
        RetiredSeedCatalogueCommand, ImportCatalogueCommand);
    Environment.ExitCode = 1;
    return;
}

// ── import-catalogue command — loads the committed artefact then exits ───────
// No LLM, no network, no arguments, idempotent.
if (isImportCatalogue)
{
    try
    {
        // Densities attach to catalogue rows, so running them together makes the catalogue-then-densities order hard to get wrong.
        await CatalogueImport.RunAsync(app.Services, app.Logger, catalogueForce);
    }
    catch (Exception ex) when (ex is FileNotFoundException or CatalogueValidationException)
    {
        app.Logger.LogError("{Message}", ex.Message);
        Environment.ExitCode = 1;
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
api.MapModelServerEndpoints();
api.MapMealPlanEndpoints();
api.MapShoppingListEndpoints();

// ── First-run seeding ───────────────────────────────────────────────────────
// Every environment except Testing: migrate, then seed the catalogue, densities and sample recipes, once per database.
if (FirstRunSeeder.RunsIn(app.Environment))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<FirstRunSeeder>().RunAsync();
}

app.Run();

// Expose the implicit Program class to the test project
public partial class Program { }
