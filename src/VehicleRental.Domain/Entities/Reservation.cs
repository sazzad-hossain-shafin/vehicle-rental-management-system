using VehicleRental.Domain.Enums;
using VehicleRental.Domain.Pricing;

namespace VehicleRental.Domain.Entities;

/// <summary>
/// A booking of one vehicle by one customer for a future date range. A reservation is a promise and a
/// price quote, not a checked-out vehicle: the vehicle is physically handed over, and a
/// <see cref="Rental"/> started, only when staff pick the reservation up.
/// </summary>
/// <remarks>
/// <para>
/// Dates are a half-open interval, <c>[StartDate, EndDate)</c>: the vehicle is held from the start of
/// <see cref="StartDate"/> up to, but not including, <see cref="EndDate"/>. A reservation from 1 Oct to
/// 4 Oct holds the vehicle on 1, 2 and 3 Oct and has 3 billable days, the same counting as a rental. Another
/// booking may therefore begin on 4 Oct, the day this one ends.
/// </para>
/// <para>
/// The quote (daily rate, billable days, pricing description and total) is calculated once, when the
/// reservation is made, by the pricing policy, and stored here. Later changes to the vehicle's rate or to
/// the pricing rules never change it, and a rental started at pickup honours it.
/// </para>
/// <para>
/// Lifecycle: <see cref="ReservationStatus.Active"/> becomes <see cref="ReservationStatus.Cancelled"/> or
/// <see cref="ReservationStatus.Fulfilled"/>, and those two are final.
/// </para>
/// </remarks>
public class Reservation
{
    /// <summary>The furthest ahead a reservation may start, counted from today.</summary>
    public const int MaxAdvanceBookingDays = 365;

    /// <summary>The longest a single reservation may be, in billable days.</summary>
    public const int MaxReservationDays = 90;

    public Guid Id { get; }
    public Customer Customer { get; }
    public Vehicle Vehicle { get; }

    /// <summary>The first day the vehicle is held.</summary>
    public DateOnly StartDate { get; }

    /// <summary>The day the vehicle is free again. It is not itself held (the interval is half-open).</summary>
    public DateOnly EndDate { get; }

    public ReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }

    /// <summary>The rental started when the reservation was picked up. Null until then.</summary>
    public Guid? RentalId { get; private set; }

    /// <summary>The vehicle's daily rate when the reservation was made.</summary>
    public decimal DailyRateAtReservation { get; }

    public int BillableDays { get; }

    /// <summary>The name of the pricing strategy that produced <see cref="TotalCost"/>.</summary>
    public string PricingDescription { get; }

    /// <summary>The quoted total, fixed when the reservation was made.</summary>
    public decimal TotalCost { get; }

    /// <summary>For the persistence layer only; it bypasses validation (see <see cref="Rental"/>).</summary>
    private Reservation()
    {
        Customer = null!;
        Vehicle = null!;
        PricingDescription = null!;
    }

    private Reservation(
        Customer customer,
        Vehicle vehicle,
        DateOnly startDate,
        DateOnly endDate,
        DateTimeOffset createdAt,
        int billableDays,
        string pricingDescription,
        decimal totalCost)
    {
        Id = Guid.CreateVersion7();
        Customer = customer;
        Vehicle = vehicle;
        StartDate = startDate;
        EndDate = endDate;
        CreatedAt = createdAt;
        BillableDays = billableDays;
        DailyRateAtReservation = vehicle.DailyRate;
        PricingDescription = pricingDescription;
        TotalCost = totalCost;
        Status = ReservationStatus.Active;
    }

    /// <summary>
    /// Creates an active reservation priced with the given strategy.
    /// </summary>
    /// <param name="today">The current date; a reservation cannot start before it.</param>
    /// <param name="now">The current instant, recorded as <see cref="CreatedAt"/>.</param>
    /// <exception cref="ArgumentNullException">The customer, vehicle or strategy is null.</exception>
    /// <exception cref="ArgumentException">
    /// The start date is in the past or more than <see cref="MaxAdvanceBookingDays"/> days ahead, the end date
    /// is not after the start date, or the reservation is longer than <see cref="MaxReservationDays"/> days.
    /// </exception>
    public static Reservation Create(
        Customer customer,
        Vehicle vehicle,
        DateOnly startDate,
        DateOnly endDate,
        DateOnly today,
        DateTimeOffset now,
        IVehiclePricingStrategy pricingStrategy)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(pricingStrategy);

        ValidatePeriod(startDate, endDate, today);

        int billableDays = Rental.CalculateBillableDays(startDate, endDate);
        decimal totalCost = pricingStrategy.CalculateCost(vehicle.DailyRate, billableDays);

        return new Reservation(customer, vehicle, startDate, endDate, now, billableDays, pricingStrategy.Name, totalCost);
    }

    /// <summary>
    /// Checks a booking period against the booking rules. Used for new reservations and for availability searches.
    /// </summary>
    /// <exception cref="ArgumentException">See <see cref="Create"/>.</exception>
    public static void ValidatePeriod(DateOnly startDate, DateOnly endDate, DateOnly today)
    {
        if (startDate < today)
        {
            throw new ArgumentException("The start date cannot be in the past.", nameof(startDate));
        }

        if (startDate > today.AddDays(MaxAdvanceBookingDays))
        {
            throw new ArgumentException(
                $"The start date cannot be more than {MaxAdvanceBookingDays} days ahead.", nameof(startDate));
        }

        if (endDate <= startDate)
        {
            throw new ArgumentException("The end date must be after the start date.", nameof(endDate));
        }

        if (endDate.DayNumber - startDate.DayNumber > MaxReservationDays)
        {
            throw new ArgumentException(
                $"A reservation cannot be longer than {MaxReservationDays} days.", nameof(endDate));
        }
    }

    /// <summary>
    /// Whether two half-open date ranges <c>[start, end)</c> share at least one day. Ranges that merely touch
    /// (one ends the day the other starts) do not overlap.
    /// </summary>
    public static bool Overlaps(DateOnly startA, DateOnly endA, DateOnly startB, DateOnly endB) =>
        startA < endB && startB < endA;

    public bool OverlapsPeriod(DateOnly start, DateOnly end) => Overlaps(StartDate, EndDate, start, end);

    /// <summary>Whether the business can still cancel the reservation (it is active).</summary>
    public bool CanBeCancelled => Status == ReservationStatus.Active;

    /// <summary>
    /// Whether the customer may cancel it themselves: it is active and has not started yet.
    /// </summary>
    public bool CanBeCancelledByCustomer(DateOnly today) => CanBeCancelled && today < StartDate;

    /// <summary>Cancels an active reservation, releasing the vehicle for its dates.</summary>
    /// <exception cref="InvalidOperationException">The reservation is not active.</exception>
    public void Cancel(DateTimeOffset now)
    {
        if (Status != ReservationStatus.Active)
        {
            throw new InvalidOperationException(
                $"The reservation is already {Status.ToString().ToLowerInvariant()}.");
        }

        Status = ReservationStatus.Cancelled;
        CancelledAt = now;
    }

    /// <summary>
    /// Hands the vehicle over: starts a rental from this reservation and marks the reservation fulfilled.
    /// Pickup is possible from <see cref="StartDate"/> until the day before <see cref="EndDate"/>. The rental
    /// begins today, ends on <see cref="EndDate"/> and honours this reservation's quote, so picking up late
    /// does not reprice (or discount) it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The reservation is not active, today is outside the pickup window, or the vehicle is not available
    /// (it is still rented out). Nothing changes in that case.
    /// </exception>
    public Rental PickUp(DateOnly today, DateTimeOffset now)
    {
        if (Status != ReservationStatus.Active)
        {
            throw new InvalidOperationException(
                $"The reservation is {Status.ToString().ToLowerInvariant()} and cannot be picked up.");
        }

        if (today < StartDate)
        {
            throw new InvalidOperationException(
                $"The reservation starts on {StartDate:yyyy-MM-dd} and cannot be picked up earlier.");
        }

        if (today >= EndDate)
        {
            throw new InvalidOperationException(
                $"The reservation ended on {EndDate:yyyy-MM-dd} and can no longer be picked up.");
        }

        // The only step that can still fail, and it changes nothing when it does.
        Vehicle.MarkAsRented();

        Rental rental = Rental.StartFromReservation(this, today);

        Status = ReservationStatus.Fulfilled;
        FulfilledAt = now;
        RentalId = rental.Id;

        return rental;
    }
}
