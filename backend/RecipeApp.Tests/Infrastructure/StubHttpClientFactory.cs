using System.Net;
using System.Text;

namespace RecipeApp.Tests.Infrastructure;

/// <summary>An <see cref="IHttpClientFactory"/> whose responses are decided per request and recorded, so tests can script per-URL bodies and assert on call counts.</summary>
public class StubHttpClientFactory : IHttpClientFactory
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        _respond = (request, _) => Task.FromResult(respond(request));

    /// <summary>Answers asynchronously, so a test can hold a request open until the caller gives up on it.</summary>
    public StubHttpClientFactory(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        _respond = respond;

    /// <summary>Absolute URLs requested, in order.</summary>
    public List<string> RequestedUrls { get; } = [];

    /// <summary>Returns the same canned body to every request.</summary>
    public static StubHttpClientFactory Returning(string body, string mediaType = "text/html") =>
        new(_ => Ok(body, mediaType));

    public static HttpResponseMessage Ok(string body, string mediaType = "text/html") =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    public static HttpResponseMessage Ok(byte[] body, string mediaType) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType) },
            },
        };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    public HttpClient CreateClient(string name) => new(new StubHandler(this));

    private Task<HttpResponseMessage> Handle(HttpRequestMessage request, CancellationToken ct)
    {
        RequestedUrls.Add(request.RequestUri!.ToString());
        return _respond(request, ct);
    }

    private class StubHandler(StubHttpClientFactory owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => owner.Handle(request, ct);
    }
}
