using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeApp.API.Data;

namespace RecipeApp.Tests.Infrastructure;

public class RecipeAppFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Prevent the development auto-migrate + seed block from running
        builder.UseEnvironment("Testing");

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
