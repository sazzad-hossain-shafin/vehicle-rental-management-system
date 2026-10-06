using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using VehicleRental.Application.Security;

namespace VehicleRental.Api.Tests.Support;

/// <summary>
/// Starts the real API (the full HTTP pipeline) in memory, pointed at a given database and running in
/// the given environment. The connection string and token settings reach the app through configuration,
/// exactly as in production.
/// </summary>
internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>A connection string for a database that does not exist, for tests that must not need one.</summary>
    public const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=unreachable;Timeout=2";

    public const string Issuer = "VehicleRental.Api.Tests";
    public const string Audience = "VehicleRental.Api.Tests.Clients";

    private readonly string _connectionString;
    private readonly string _environment;
    private readonly Dictionary<string, string?> _extraSettings;

    /// <summary>
    /// The signing key for this factory: random for each factory, held only in memory for the life of the
    /// test and never written anywhere. A "restarted" app can be given the same key to keep tokens valid.
    /// </summary>
    public string SigningKey { get; }

    /// <summary>Everything the application logged, so tests can check that secrets never appear in logs.</summary>
    public List<string> Logs { get; } = new();

    public ApiFactory(
        string connectionString,
        string environment = "Development",
        string? signingKey = null,
        Dictionary<string, string?>? extraSettings = null)
    {
        _connectionString = connectionString;
        _environment = environment;
        _extraSettings = extraSettings ?? new Dictionary<string, string?>();
        SigningKey = signingKey ?? NewRandomKey();
    }

    public static string NewRandomKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    /// <summary>A fresh random password that satisfies the password policy. Never reused or written down.</summary>
    public static string NewPassword() => "Aa1-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:VehicleRentalDatabase"] = _connectionString,
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
                ["Jwt:SigningKey"] = SigningKey
            };

            foreach (var (key, value) in _extraSettings)
            {
                settings[key] = value;
            }

            configuration.AddInMemoryCollection(settings);
        });

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new CapturingLoggerProvider(Logs));
        });
    }

    /// <summary>
    /// A client carrying a token this factory's key signed, for tests that need no database. The token is
    /// built here, not obtained from the login endpoint.
    /// </summary>
    public HttpClient CreateClientWithToken(string token)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    public HttpClient CreateClientAs(string role, Guid? customerId = null) =>
        CreateClientWithToken(TestTokens.Create(SigningKey, [role], customerId: customerId));
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
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
            string message = formatter(state, exception);

            lock (_logs)
            {
                _logs.Add(exception is null ? message : message + " " + exception);
            }
        }
    }
}

/// <summary>Builds access tokens for tests, signed with a key the test controls.</summary>
internal static class TestTokens
{
    /// <param name="issuedAt">When the token starts being valid; defaults to now. Use a past time for an expired token.</param>
    /// <param name="lifetime">How long it is valid for, from <paramref name="issuedAt"/>.</param>
    public static string Create(
        string signingKey,
        IEnumerable<string> roles,
        Guid? userId = null,
        Guid? customerId = null,
        string issuer = ApiFactory.Issuer,
        string audience = ApiFactory.Audience,
        DateTime? issuedAt = null,
        TimeSpan? lifetime = null,
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var claims = new List<Claim> { new(AuthClaims.Subject, (userId ?? Guid.NewGuid()).ToString()) };
        claims.AddRange(roles.Select(r => new Claim(AuthClaims.Role, r)));

        if (customerId is { } id)
        {
            claims.Add(new Claim(AuthClaims.CustomerId, id.ToString()));
        }

        DateTime start = issuedAt ?? DateTime.UtcNow;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = start,
            IssuedAt = start,
            Expires = start.Add(lifetime ?? TimeSpan.FromMinutes(10)),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), algorithm)
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}

internal static class Json
{
    /// <summary>The same JSON conventions the API uses: camelCase names, enums as text.</summary>
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Options))!;

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return document.RootElement.Clone();
    }

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string url, object body) =>
        client.PostAsJsonAsync(url, body, Options);
}
