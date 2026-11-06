using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Services.Llm;
using RecipeApp.API.Services.Seeding;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Seeding;

/// <summary>The stages that call the model check the Model Server first, so a stopped server costs one line rather than a run of failed recipes.</summary>
public class SeedRecipesCommandModelServerTests : IDisposable
{
    private readonly string _contentRoot =
        Path.Combine(Path.GetTempPath(), "recipeapp-seed-command-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Stands in for the harvester and records whether the command got as far as reading the manifest.</summary>
    private sealed class RecordingHarvester : IWaybackHarvester
    {
        public bool Reached { get; private set; }

        public Task<SeedDiscoveryResult> DiscoverAsync(CancellationToken ct = default) => throw Reach();
        public Task<SeedManifest> EnsureManifestAsync(CancellationToken ct = default) => throw Reach();
        public Task<SeedHarvestResult> HarvestAsync(SeedManifest manifest, int? limit = null, CancellationToken ct = default) => throw Reach();

        private SeedHarvestException Reach()
        {
            Reached = true;
            return new SeedHarvestException("the command reached the manifest");
        }
    }

    /// <summary>Resolved by the stages before they read the manifest, and never reached in these tests because the harvester stops them first.</summary>
    private sealed class UnreachedSeeder : IRecipeLibrarySeeder
    {
        public Task<SeedParseResult> ParseAsync(SeedManifest manifest, int? limit = null, bool force = false, CancellationToken ct = default) =>
            throw new InvalidOperationException("The seeder must not be reached.");
        public Task<SeedNormaliseResult> NormaliseAsync(SeedManifest manifest, int? limit = null, bool force = false, CancellationToken ct = default) =>
            throw new InvalidOperationException("The seeder must not be reached.");
        public Task<SeedPersistResult> PersistAsync(SeedManifest manifest, int? limit = null, bool force = false, CancellationToken ct = default) =>
            throw new InvalidOperationException("The seeder must not be reached.");
    }

    private (IServiceProvider Services, RecordingHarvester Harvester, SeedCacheStore Cache) Build(ModelServerState state)
    {
        var harvester = new RecordingHarvester();
        var services  = new ServiceCollection()
            .AddSingleton<IOptions<RecipeSeedingOptions>>(Options.Create(new RecipeSeedingOptions()))
            .AddSingleton<IHostEnvironment>(new TestHostEnvironment(_contentRoot))
            .AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>))
            .AddSingleton<SeedCacheStore>()
            .AddSingleton<IWaybackHarvester>(harvester)
            .AddSingleton<IRecipeLibrarySeeder, UnreachedSeeder>()
            .AddSingleton<IModelServerStatusProbe>(new StubModelServerStatusProbe(state))
            .BuildServiceProvider();

        return (services, harvester, services.GetRequiredService<SeedCacheStore>());
    }

    [Theory]
    [InlineData("--normalise")]
    [InlineData("--build-catalogue")]
    public async Task ModelStage_ModelServerNotReady_ExitsOneBeforeReadingTheManifest(string stage)
    {
        var (services, harvester, _) = Build(ModelServerState.Offline);

        var exitCode = await SeedRecipesCommand.RunAsync(services, NullLogger.Instance, [stage]);

        exitCode.Should().Be(1);
        harvester.Reached.Should().BeFalse();
    }

    /// <summary>--force discards a GPU pass, so it must not run before the check that decides there is a GPU to redo it.</summary>
    [Fact]
    public async Task NormaliseForce_ModelServerNotReady_KeepsTheCachedNormalisedOutput()
    {
        var (services, _, cache) = Build(ModelServerState.Loading);
        cache.EnsureDirectories();
        var cached = Path.Combine(cache.NormalisedDirectory, "kept.json");
        await File.WriteAllTextAsync(cached, "{}");

        var exitCode = await SeedRecipesCommand.RunAsync(services, NullLogger.Instance, ["--normalise", "--force"]);

        exitCode.Should().Be(1);
        File.Exists(cached).Should().BeTrue();
    }

    [Fact]
    public async Task Normalise_ModelServerReady_GoesOnToTheManifest()
    {
        var (services, harvester, _) = Build(ModelServerState.Ready);

        await SeedRecipesCommand.RunAsync(services, NullLogger.Instance, ["--normalise"]);

        harvester.Reached.Should().BeTrue();
    }

    /// <summary>Stage 5 needs no model, so persisting must not wait on a Model Server that is off.</summary>
    [Fact]
    public async Task Persist_ModelServerNotReady_IsNotChecked()
    {
        var (services, harvester, _) = Build(ModelServerState.Offline);

        await SeedRecipesCommand.RunAsync(services, NullLogger.Instance, ["--persist"]);

        harvester.Reached.Should().BeTrue();
    }
}
