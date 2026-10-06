using VehicleRental.Application;

namespace VehicleRental.Api.Contracts;

/// <summary>Query-string filters and paging for the vehicle list.</summary>
/// <param name="VehicleType">Only vehicles of this type: Car, Motorcycle or Van (any letter case).</param>
/// <param name="MaxDailyRate">Only vehicles whose daily rate is at most this amount.</param>
/// <param name="Availability">Only vehicles with this availability: Available or Rented (any letter case).</param>
/// <param name="Page">The page number, starting at 1.</param>
/// <param name="PageSize">Items per page, 1 to 100.</param>
public sealed record VehicleListQuery(
    string? VehicleType = null,
    decimal? MaxDailyRate = null,
    string? Availability = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>Query-string paging for list endpoints.</summary>
/// <param name="Page">The page number, starting at 1.</param>
/// <param name="PageSize">Items per page, 1 to 100.</param>
public sealed record PageQuery(
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);
