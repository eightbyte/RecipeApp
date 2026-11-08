using Microsoft.Extensions.Logging;

namespace RecipeApp.Tests.Infrastructure;

/// <summary>An <see cref="ILogger{T}"/> that keeps every entry written to it, so a test can assert on what was logged.</summary>
public class RecordingLogger<T> : ILogger<T>
{
    public record Entry(LogLevel Level, string Message);

    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add(new Entry(logLevel, formatter(state, exception)));
}
