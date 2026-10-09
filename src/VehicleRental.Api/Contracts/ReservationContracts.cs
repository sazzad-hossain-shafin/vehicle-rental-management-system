using System.ComponentModel.DataAnnotations;
using VehicleRental.Application;

namespace VehicleRental.Api.Contracts;

/// <summary>
/// A customer's own booking request. There is deliberately no customer field: the booking is always for the
/// signed-in customer, taken from the verified token.
/// </summary>
public sealed class CreateMyReservationRequest
{
    /// <summary>The ID of the vehicle to reserve.</summary>
    [Required]
    public Guid? VehicleId { get; init; }

    /// <summary>The first day the vehicle is held, as yyyy-MM-dd.</summary>
    [Required]
    public DateOnly? StartDate { get; init; }

    /// <summary>The day the vehicle is free again, as yyyy-MM-dd. The vehicle is not held on this day.</summary>
    [Required]
    public DateOnly? EndDate { get; init; }
}

/// <summary>A booking made at the rental desk (staff or admin) for an existing customer.</summary>
public sealed class CreateDeskReservationRequest
{
    /// <summary>The ID of the customer the booking is for.</summary>
    [Required]
    public Guid? CustomerId { get; init; }

    /// <summary>The ID of the vehicle to reserve.</summary>
    [Required]
    public Guid? VehicleId { get; init; }

    /// <summary>The first day the vehicle is held, as yyyy-MM-dd.</summary>
    [Required]
    public DateOnly? StartDate { get; init; }

    /// <summary>The day the vehicle is free again, as yyyy-MM-dd. The vehicle is not held on this day.</summary>
    [Required]
    public DateOnly? EndDate { get; init; }

    /// <summary>Whether the promotional discount was requested. It applies only to short bookings.</summary>
    public bool PromotionalDiscountRequested { get; init; }
}

/// <summary>Query-string parameters of the availability search.</summary>
/// <param name="StartDate">The first day, as yyyy-MM-dd (required).</param>
/// <param name="EndDate">The day the vehicle is returned, as yyyy-MM-dd (required, not held).</param>
/// <param name="VehicleType">Only vehicles of this type (any letter case).</param>
/// <param name="MaxDailyRate">Only vehicles whose daily rate is at most this amount.</param>
/// <param name="Page">The page number, starting at 1.</param>
/// <param name="PageSize">Items per page, 1 to 100.</param>
public sealed record AvailabilityQuery(
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    string? VehicleType = null,
    decimal? MaxDailyRate = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>Query-string parameters of the quote request.</summary>
/// <param name="StartDate">The first day, as yyyy-MM-dd (required).</param>
/// <param name="EndDate">The day the vehicle is returned, as yyyy-MM-dd (required, not held).</param>
public sealed record QuoteQuery(DateOnly? StartDate = null, DateOnly? EndDate = null);

/// <summary>Query-string filter and paging of the staff reservation list.</summary>
/// <param name="Status">Only reservations with this status: Active, Cancelled or Fulfilled (any letter case).</param>
/// <param name="Page">The page number, starting at 1.</param>
/// <param name="PageSize">Items per page, 1 to 100.</param>
public sealed record ReservationListQuery(
    string? Status = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);
