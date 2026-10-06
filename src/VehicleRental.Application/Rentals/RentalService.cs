using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Customers;
using VehicleRental.Application.Exceptions;
using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;
using VehicleRental.Domain.Pricing;

namespace VehicleRental.Application.Rentals;

/// <summary>
/// The rental use cases. It finds the entities, calls the domain operations (which
/// own the rules for pricing, dates and state changes) and saves the result.
/// </summary>
public sealed class RentalService
{
    private readonly IVehicleRepository _vehicles;
    private readonly ICustomerRepository _customers;
    private readonly IRentalRepository _rentals;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public RentalService(
        IVehicleRepository vehicles,
        ICustomerRepository customers,
        IRentalRepository rentals,
        IUnitOfWork unitOfWork,
        TimeProvider? timeProvider = null)
    {
        _vehicles = vehicles;
        _customers = customers;
        _rentals = rentals;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Whether a rental of this length can be offered the promotional discount.
    /// </summary>
    public bool IsPromotionalDiscountAvailable(int rentalDays) =>
        PricingPolicy.IsPromotionalDiscountAvailable(rentalDays);

    /// <summary>
    /// Starts a rental today. An unknown customer number registers a new customer; a known
    /// number is reused if the name matches (see <see cref="CustomerResolver"/>).
    /// </summary>
    /// <remarks>
    /// The vehicle, the rental and any new customer are saved together or not at all. If another
    /// request rents the same vehicle at the same moment, exactly one of them is saved and the
    /// other gets a <see cref="ConflictException"/> when saving.
    /// </remarks>
    /// <exception cref="NotFoundException">The vehicle does not exist.</exception>
    /// <exception cref="ConflictException">
    /// The vehicle is already rented, or the customer number belongs to a different name.
    /// </exception>
    /// <exception cref="ArgumentException">The customer details or the rental length are invalid.</exception>
    public async Task<RentalDto> StartRentalAsync(
        StartRentalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Vehicle vehicle = await GetVehicleAsync(request.VehicleRegistrationNumber, cancellationToken);

        bool hasActiveRental =
            await _rentals.GetActiveForVehicleAsync(vehicle.Id, cancellationToken) is not null;

        if (hasActiveRental || vehicle.AvailabilityStatus != VehicleAvailabilityStatus.Available)
        {
            throw new ConflictException($"Vehicle '{vehicle.RegistrationNumber}' is not available.");
        }

        var (customer, isNewCustomer) = await CustomerResolver.ResolveAsync(
            _customers,
            request.CustomerNumber,
            request.CustomerName,
            cancellationToken);

        DateOnly startDate = Today();
        DateOnly returnDate = startDate.AddDays(request.RentalDays);

        int billableDays = Rental.CalculateBillableDays(startDate, returnDate);
        IVehiclePricingStrategy strategy =
            PricingPolicy.SelectStrategy(billableDays, request.PromotionalDiscountRequested);

        // Every check has passed. Starting the rental is the first change to any entity,
        // and nothing is stored until it succeeds.
        Rental rental = Rental.Start(customer, vehicle, startDate, returnDate, strategy);

        if (isNewCustomer)
        {
            await _customers.AddAsync(customer, cancellationToken);
        }

        await _rentals.AddAsync(rental, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return rental.ToDto();
    }

    /// <summary>
    /// Completes the vehicle's active rental, as of today, and makes the vehicle available again.
    /// </summary>
    /// <exception cref="NotFoundException">The vehicle does not exist.</exception>
    /// <exception cref="ConflictException">The vehicle has no active rental, or it was just changed by another request.</exception>
    public async Task<RentalDto> ReturnVehicleAsync(
        string vehicleRegistrationNumber,
        CancellationToken cancellationToken = default)
    {
        Vehicle vehicle = await GetVehicleAsync(vehicleRegistrationNumber, cancellationToken);

        Rental rental = await _rentals.GetActiveForVehicleAsync(vehicle.Id, cancellationToken)
                        ?? throw new ConflictException(
                            $"Vehicle '{vehicle.RegistrationNumber}' is not currently rented.");

        rental.Complete(Today());

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return rental.ToDto();
    }

    /// <summary>
    /// The vehicle's active rental, or null if it is not rented.
    /// </summary>
    /// <exception cref="NotFoundException">The vehicle does not exist.</exception>
    public async Task<RentalDto?> GetActiveRentalForVehicleAsync(
        string vehicleRegistrationNumber,
        CancellationToken cancellationToken = default)
    {
        Vehicle vehicle = await GetVehicleAsync(vehicleRegistrationNumber, cancellationToken);

        Rental? rental = await _rentals.GetActiveForVehicleAsync(vehicle.Id, cancellationToken);

        return rental?.ToDto();
    }

    /// <summary>
    /// Every rental, active and completed, oldest first.
    /// </summary>
    public async Task<IReadOnlyList<RentalDto>> GetRentalHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        var rentals = await _rentals.GetAllAsync(cancellationToken);

        return rentals.Select(r => r.ToDto()).ToList();
    }

    private async Task<Vehicle> GetVehicleAsync(string registrationNumber, CancellationToken cancellationToken) =>
        await _vehicles.GetByRegistrationNumberAsync(
            Vehicle.NormalizeRegistrationNumber(registrationNumber),
            cancellationToken)
        ?? throw new NotFoundException($"Vehicle '{registrationNumber}' was not found.");

    private DateOnly Today() =>
        DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
}
