using VehicleRental.Domain.Enums;
using VehicleRental.Domain.Pricing;

namespace VehicleRental.Domain.Entities;

/// <summary>
/// A rental of one vehicle by one customer. The agreed price is calculated once,
/// when the rental starts, and stored here, so later changes to pricing rules or
/// to the vehicle never alter a rental's history.
/// </summary>
/// <remarks>
/// Billing rule: each full day between the start date and the expected return
/// date is billed, counting the start day but not the return day. A rental from
/// 1 Oct to 4 Oct is 3 billable days. A rental must be at least one day long.
/// The agreed price is fixed; returning early or late does not change it.
/// </remarks>
public class Rental
{
    public Guid Id { get; }
    public Customer Customer { get; }
    public Vehicle Vehicle { get; }
    public DateOnly StartDate { get; }
    public DateOnly ExpectedReturnDate { get; }
    public DateOnly? ActualReturnDate { get; private set; }
    public RentalStatus Status { get; private set; }

    /// <summary>The vehicle's daily rate when the rental started.</summary>
    public decimal DailyRateAtRental { get; }

    /// <summary>The name of the pricing strategy that produced <see cref="TotalCost"/>.</summary>
    public string PricingDescription { get; }

    public int BillableDays { get; }

    /// <summary>The agreed total, fixed when the rental started.</summary>
    public decimal TotalCost { get; }

    /// <summary>
    /// For the persistence layer only. It bypasses validation because it is used to rebuild
    /// rentals that were already validated and priced when they were started.
    /// </summary>
    private Rental()
    {
        Customer = null!;
        Vehicle = null!;
        PricingDescription = null!;
    }

    private Rental(
        Customer customer,
        Vehicle vehicle,
        DateOnly startDate,
        DateOnly expectedReturnDate,
        int billableDays,
        string pricingDescription,
        decimal totalCost)
    {
        Id = Guid.CreateVersion7();
        Customer = customer;
        Vehicle = vehicle;
        StartDate = startDate;
        ExpectedReturnDate = expectedReturnDate;
        BillableDays = billableDays;
        DailyRateAtRental = vehicle.DailyRate;
        PricingDescription = pricingDescription;
        TotalCost = totalCost;
        Status = RentalStatus.Active;
    }

    /// <summary>
    /// The number of billable days between two dates.
    /// </summary>
    /// <exception cref="ArgumentException">The return date is not after the start date.</exception>
    public static int CalculateBillableDays(DateOnly startDate, DateOnly expectedReturnDate)
    {
        int days = expectedReturnDate.DayNumber - startDate.DayNumber;

        if (days < 1)
        {
            throw new ArgumentException(
                "The expected return date must be at least one day after the start date.",
                nameof(expectedReturnDate));
        }

        return days;
    }

    /// <summary>
    /// Starts a rental: prices it with the given strategy, stores the result and
    /// marks the vehicle as rented.
    /// </summary>
    /// <exception cref="ArgumentNullException">The customer, vehicle or strategy is null.</exception>
    /// <exception cref="ArgumentException">The dates do not span at least one day.</exception>
    /// <exception cref="InvalidOperationException">The vehicle is not available.</exception>
    public static Rental Start(
        Customer customer,
        Vehicle vehicle,
        DateOnly startDate,
        DateOnly expectedReturnDate,
        IVehiclePricingStrategy pricingStrategy)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(pricingStrategy);

        int billableDays = CalculateBillableDays(startDate, expectedReturnDate);
        decimal totalCost = pricingStrategy.CalculateCost(vehicle.DailyRate, billableDays);

        // Last, so nothing changes if any check above fails.
        vehicle.MarkAsRented();

        return new Rental(
            customer,
            vehicle,
            startDate,
            expectedReturnDate,
            billableDays,
            pricingStrategy.Name,
            totalCost);
    }

    /// <summary>
    /// Completes the rental, records the return date and makes the vehicle available again.
    /// </summary>
    /// <exception cref="InvalidOperationException">The rental is already completed.</exception>
    /// <exception cref="ArgumentException">The return date is before the start date.</exception>
    public void Complete(DateOnly actualReturnDate)
    {
        if (Status != RentalStatus.Active)
        {
            throw new InvalidOperationException("The rental has already been completed.");
        }

        if (actualReturnDate < StartDate)
        {
            throw new ArgumentException(
                "The return date cannot be before the start date.",
                nameof(actualReturnDate));
        }

        Vehicle.MarkAsAvailable();
        ActualReturnDate = actualReturnDate;
        Status = RentalStatus.Completed;
    }
}
