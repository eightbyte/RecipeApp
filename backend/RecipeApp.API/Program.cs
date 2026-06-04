using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using RecipeApp.API.Data;
using RecipeApp.API.Endpoints;
using RecipeApp.API.Services;
using Scalar.AspNetCore;

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

// ── Recipe scraping ────────────────────────────────────────────────────────────
builder.Services.Configure<RecipeScrapingOptions>(
    builder.Configuration.GetSection(RecipeScrapingOptions.SectionName));

builder.Services.AddHttpClient("RecipeScraper", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("RecipeApp/1.0 (+https://github.com/your-repo)");
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
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "postgres",
        tags: ["ready"]
    );

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

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

app.Run();

// Expose the implicit Program class to the test project
public partial class Program { }
