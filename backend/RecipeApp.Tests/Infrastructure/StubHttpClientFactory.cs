using System.Net;
using System.Text;

namespace RecipeApp.Tests.Infrastructure;

/// <summary>
/// An <see cref="IHttpClientFactory"/> whose responses are decided per request, so a test can
/// script different bodies per URL or a rate-limit-then-succeed sequence. Every request is
/// recorded, which is how the retry and image-harvest tests assert on call counts.
/// </summary>
public class StubHttpClientFactory : IHttpClientFactory
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
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

    private HttpResponseMessage Handle(HttpRequestMessage request)
    {
        RequestedUrls.Add(request.RequestUri!.ToString());
        return _respond(request);
    }

    private class StubHandler(StubHttpClientFactory owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(owner.Handle(request));
    }
}
