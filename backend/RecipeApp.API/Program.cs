using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.Endpoints;
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

// ── AutoMapper ────────────────────────────────────────────────────────────────
builder.Services.AddAutoMapper(typeof(Program).Assembly);

// ── FluentValidation ──────────────────────────────────────────────────────────
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// ── CORS ──────────────────────────────────────────────────────────────────────
// Allow Vue dev servers (Vite default 5173, our configured port 3000)
builder.Services.AddCors(options =>
{
    options.AddPolicy("VueDev", policy =>
        policy
            .WithOrigins(
                "http://localhost:3000",
                "http://localhost:5173"
            )
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

// ── Image / Static Files ──────────────────────────────────────────────────────
builder.Services.AddDirectoryBrowser();

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Development middleware ────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Scalar provides a clean interactive API UI at /scalar/v1
    app.MapScalarApiReference(opts =>
    {
        opts.Title = "RecipeApp API";
        opts.Theme = ScalarTheme.Purple;
    });
}

// ── Static files (recipe images) ─────────────────────────────────────────────
app.UseStaticFiles();

// ── CORS ──────────────────────────────────────────────────────────────────────
app.UseCors("VueDev");

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapHealthEndpoints();

// Apply any pending EF migrations on startup (development convenience)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
