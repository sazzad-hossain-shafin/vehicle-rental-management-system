using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace VehicleRental.Api.Tests.Support;

/// <summary>
/// Starts the real API (the full HTTP pipeline) in memory, pointed at a given database and running in
/// the given environment. The connection string reaches the app through configuration, exactly as in production.
/// </summary>
internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>A connection string for a database that does not exist, for tests that must not need one.</summary>
    public const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=unreachable;Timeout=2";

    private readonly string _connectionString;
    private readonly string _environment;

    public ApiFactory(string connectionString, string environment = "Development")
    {
        _connectionString = connectionString;
        _environment = environment;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:VehicleRentalDatabase"] = _connectionString
            }));

        builder.ConfigureLogging(logging => logging.ClearProviders());
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
