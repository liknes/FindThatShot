using Microsoft.Extensions.Logging;

namespace VideoArchiveManager.App.Services.Diagnostics;

// Bridges Microsoft.Extensions.Logging into the in-app diagnostics buffer.
// Registered alongside the existing Debug provider so every ILogger call in
// the app and its services is mirrored into DiagnosticsLog (and from there
// to the on-disk file and the Diagnostics window).
[ProviderAlias("Diagnostics")]
public sealed class DiagnosticsLoggerProvider : ILoggerProvider
{
    private readonly IDiagnosticsLog _sink;

    public DiagnosticsLoggerProvider(IDiagnosticsLog sink) => _sink = sink;

    public ILogger CreateLogger(string categoryName) => new DiagnosticsLogger(categoryName, _sink);

    public void Dispose() { }

    private sealed class DiagnosticsLogger : ILogger
    {
        private readonly string _category;
        private readonly IDiagnosticsLog _sink;

        public DiagnosticsLogger(string category, IDiagnosticsLog sink)
        {
            _category = category;
            _sink = sink;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // The minimum level is enforced by the logging factory's filters, so
        // anything that reaches us is in-scope.
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception is null) return;

            _sink.Add(new LogEntry(
                DateTimeOffset.Now,
                logLevel,
                _category,
                message,
                exception?.ToString()));
        }
    }
}
