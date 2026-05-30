using FluentAssertions;
using RecipeApp.Tests.Infrastructure;
using System.Net;

namespace RecipeApp.Tests.Endpoints;

[Collection("Database")]
public class HealthEndpointTests(DatabaseFixture db) : IAsyncLifetime
{
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Liveness_AlwaysReturns200()
    {
        var client = db.CreateClient();
        var response = await client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_WithConnectedDb_Returns200()
    {
        var client = db.CreateClient();
        var response = await client.GetAsync("/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
