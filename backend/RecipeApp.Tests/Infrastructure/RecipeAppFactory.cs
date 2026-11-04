using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RecipeApp.API.Data;

namespace RecipeApp.Tests.Infrastructure;

public class RecipeAppFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Keeps first-run seeding out of integration tests so each test controls its own data.
        builder.UseEnvironment(FirstRunSeeder.TestingEnvironmentName);

        // Inject the Testcontainers connection string into configuration so the lazily resolved NpgSql health check uses the right database.
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the production DbContext options and replace with the test container
            var descriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(connectionString, o => o.MigrationsAssembly("RecipeApp.API")));
        });
    }
}
