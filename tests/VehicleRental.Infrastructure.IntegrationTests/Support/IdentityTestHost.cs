using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace VehicleRental.Infrastructure.IntegrationTests.Support;

/// <summary>
/// Builds the real Infrastructure registrations (the same call the API makes) over a test database, so account
/// code is tested exactly as it is wired in production. Each call to <see cref="CreateScope"/> is one "request".
/// </summary>
internal sealed class IdentityTestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public List<string> Logs { get; } = new();

    public IdentityTestHost(string connectionString, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:VehicleRentalDatabase"] = connectionString,
            ["Jwt:Issuer"] = "test-issuer",
            ["Jwt:Audience"] = "test-audience",
            ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
        };

        foreach (var (key, value) in extra ?? new())
        {
            settings[key] = value;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddLogging(logging => logging.AddProvider(new CapturingLoggerProvider(Logs)));
        services.AddInfrastructure();

        _provider = services.BuildServiceProvider(validateScopes: true);
    }

    public IServiceScope CreateScope() => _provider.CreateScope();

    public void Dispose() => _provider.Dispose();

    /// <summary>A fresh random password that satisfies the policy. Never reused or written down.</summary>
    public static string NewPassword() => "Aa1-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10));

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _logs;

        public CapturingLoggerProvider(List<string> logs) => _logs = logs;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_logs);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger : ILogger
        {
            private readonly List<string> _logs;

            public CapturingLogger(List<string> logs) => _logs = logs;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (_logs)
                {
                    _logs.Add(formatter(state, exception));
                }
            }
        }
    }
}
