using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace RecipeApp.Tests.Infrastructure;

/// <summary>
/// Minimal <see cref="IHostEnvironment"/> for services that only need a content root — the seed
/// cache resolves its relative cache directory against one.
/// </summary>
public class TestHostEnvironment : IHostEnvironment
{
    public TestHostEnvironment(string contentRootPath)
    {
        // A real host's content root always exists; PhysicalFileProvider insists on it.
        Directory.CreateDirectory(contentRootPath);

        ContentRootPath         = contentRootPath;
        ContentRootFileProvider = new PhysicalFileProvider(contentRootPath);
    }

    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "RecipeApp.Tests";
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; }
}
