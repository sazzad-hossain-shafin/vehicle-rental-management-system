using System.Net;
using System.Text.Json;
using VehicleRental.Application.Customers;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Vehicles;

namespace VehicleRental.Api.Tests.Support;

internal static class ApiTestExtensions
{
    public const string V1 = "/api/v1";

    public static async Task<VehicleDto> CreateVehicleAsync(
        this HttpClient client,
        string registration = "ABC-123",
        decimal dailyRate = 100m,
        string type = "Car")
    {
        var response = await client.PostJsonAsync($"{V1}/vehicles", new
        {
            registrationNumber = registration,
            make = "Toyota",
            model = "Corolla",
            year = 2022,
            vehicleType = type,
            dailyRate
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await response.ReadAsync<VehicleDto>();
    }

    public static async Task<CustomerDto> CreateCustomerAsync(
        this HttpClient client,
        string number = "C1",
        string name = "Alice")
    {
        var response = await client.PostJsonAsync($"{V1}/customers", new { customerNumber = number, name });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await response.ReadAsync<CustomerDto>();
    }

    public static Task<HttpResponseMessage> StartRentalRawAsync(
        this HttpClient client,
        Guid vehicleId,
        Guid customerId,
        int days = 3,
        bool promotion = false) =>
        client.PostJsonAsync($"{V1}/rentals", new
        {
            vehicleId,
            customerId,
            rentalDays = days,
            promotionalDiscountRequested = promotion
        });

    public static async Task<RentalDto> StartRentalAsync(
        this HttpClient client,
        Guid vehicleId,
        Guid customerId,
        int days = 3,
        bool promotion = false)
    {
        var response = await client.StartRentalRawAsync(vehicleId, customerId, days, promotion);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await response.ReadAsync<RentalDto>();
    }

    /// <summary>Asserts a Problem Details response (RFC 9457) with the given status and returns its body.</summary>
    public static async Task<JsonElement> AssertProblemAsync(this HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        JsonElement body = await response.ReadJsonAsync();

        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));
        Assert.True(body.TryGetProperty("traceId", out _), "Problem details should carry a trace ID for support.");

        return body;
    }
}
