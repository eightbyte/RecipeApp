using System.Net;

namespace RecipeApp.Tests.Services;

/// <summary>Returns an HttpClient that responds to every request with the given canned HTML body.</summary>
public class FakeHttpClientFactory(string html) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(new StaticResponseHandler(html));

    private class StaticResponseHandler(string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html),
            });
    }
}
