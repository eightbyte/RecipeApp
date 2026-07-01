using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace RecipeApp.API.Services;

/// <summary>
/// Owns a single lazily-launched Chromium instance (singleton) and hands out short-lived
/// browser contexts for rendering. Launching a whole browser per request would be far too
/// expensive, so the browser process itself is shared and only page/context creation is
/// bounded by <see cref="RecipeScrapingOptions.HeadlessMaxConcurrency"/>.
/// </summary>
public sealed class PlaywrightHeadlessRenderer(
    IOptions<RecipeScrapingOptions> options,
    ILogger<PlaywrightHeadlessRenderer> logger) : IHeadlessRenderer, IAsyncDisposable
{
    private readonly RecipeScrapingOptions _options = options.Value;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private readonly SemaphoreSlim _concurrencyGate = new(Math.Max(1, options.Value.HeadlessMaxConcurrency));

    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public async Task<string> RenderAsync(string url, TimeSpan timeout, CancellationToken ct)
    {
        var browser = await EnsureBrowserAsync(ct);

        await _concurrencyGate.WaitAsync(ct);
        try
        {
            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1280, Height = 1800 },
            });
            try
            {
                var page = await context.NewPageAsync();
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout   = (float)timeout.TotalMilliseconds,
                });
                return await page.ContentAsync();
            }
            finally
            {
                await context.CloseAsync();
            }
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    private async Task<IBrowser> EnsureBrowserAsync(CancellationToken ct)
    {
        if (_browser is not null) return _browser;

        await _initGate.WaitAsync(ct);
        try
        {
            if (_browser is not null) return _browser;

            logger.LogInformation("Launching headless Chromium for recipe scraping fallback…");
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            return _browser;
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.CloseAsync();
        _playwright?.Dispose();
        _initGate.Dispose();
        _concurrencyGate.Dispose();
    }
}
