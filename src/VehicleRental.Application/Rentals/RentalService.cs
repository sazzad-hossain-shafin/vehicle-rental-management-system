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
    /// Starts a rental today, identifying the vehicle by registration number and the customer by
    /// number and name. An unknown customer number registers a new customer; a known number is
    /// reused if the name matches (see <see cref="CustomerResolver"/>).
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

        EnsureAvailable(vehicle, await HasActiveRentalAsync(vehicle, cancellationToken));

        var (customer, isNewCustomer) = await CustomerResolver.ResolveAsync(
            _customers,
            request.CustomerNumber,
            request.CustomerName,
            cancellationToken);

        return await StartAsync(
            vehicle,
            customer,
            isNewCustomer,
            request.RentalDays,
            request.PromotionalDiscountRequested,
            cancellationToken);
    }

    /// <summary>
    /// Starts a rental today for an existing vehicle and an existing customer, identified by ID.
    /// The same saving and conflict guarantees apply as for the number-based overload.
    /// </summary>
    /// <exception cref="NotFoundException">The vehicle or the customer does not exist.</exception>
    /// <exception cref="ConflictException">The vehicle is already rented.</exception>
    /// <exception cref="ArgumentException">The rental length is invalid.</exception>
    public async Task<RentalDto> StartRentalAsync(
        StartRentalByIdRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Vehicle vehicle = await _vehicles.GetByIdAsync(request.VehicleId, cancellationToken)
                          ?? throw new NotFoundException($"Vehicle '{request.VehicleId}' was not found.");

        Customer customer = await _customers.GetByIdAsync(request.CustomerId, cancellationToken)
                            ?? throw new NotFoundException($"Customer '{request.CustomerId}' was not found.");

        EnsureAvailable(vehicle, await HasActiveRentalAsync(vehicle, cancellationToken));

        return await StartAsync(
            vehicle,
            customer,
            isNewCustomer: false,
            request.RentalDays,
            request.PromotionalDiscountRequested,
            cancellationToken);
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

        return await CompleteAsync(rental, cancellationToken);
    }

    /// <summary>
    /// Completes the rental with this ID, as of today, and makes its vehicle available again.
    /// </summary>
    /// <exception cref="NotFoundException">The rental does not exist.</exception>
    /// <exception cref="ConflictException">The rental is already completed, or it was just changed by another request.</exception>
    public async Task<RentalDto> ReturnRentalAsync(
        Guid rentalId,
        CancellationToken cancellationToken = default)
    {
        Rental rental = await _rentals.GetByIdAsync(rentalId, cancellationToken)
                        ?? throw new NotFoundException($"Rental '{rentalId}' was not found.");

        if (rental.Status != RentalStatus.Active)
        {
            throw new ConflictException($"Rental '{rentalId}' has already been completed.");
        }

        return await CompleteAsync(rental, cancellationToken);
    }

    /// <exception cref="NotFoundException">The rental does not exist.</exception>
    public async Task<RentalDto> GetRentalAsync(Guid rentalId, CancellationToken cancellationToken = default)
    {
        Rental? rental = await _rentals.GetByIdAsync(rentalId, cancellationToken);

        return rental?.ToDto()
               ?? throw new NotFoundException($"Rental '{rentalId}' was not found.");
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
    /// Every rental, active and completed, oldest first. Intended for small data sets such as the
    /// console client; use <see cref="GetRentalHistoryPageAsync"/> for anything that can grow.
    /// </summary>
    public async Task<IReadOnlyList<RentalDto>> GetRentalHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        var rentals = await _rentals.GetAllAsync(cancellationToken);

        return rentals.Select(r => r.ToDto()).ToList();
    }

    /// <summary>
    /// One page of the rental history, oldest start date first.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The page or page size is out of range (see <see cref="Paging"/>).</exception>
    public async Task<PagedResult<RentalDto>> GetRentalHistoryPageAsync(
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        int skip = Paging.ToSkip(page, pageSize);

        var result = await _rentals.GetPageAsync(skip, pageSize, cancellationToken);

        return new PagedResult<RentalDto>(
            result.Items.Select(r => r.ToDto()).ToList(),
            page,
            pageSize,
            result.TotalCount);
    }

    private async Task<bool> HasActiveRentalAsync(Vehicle vehicle, CancellationToken cancellationToken) =>
        await _rentals.GetActiveForVehicleAsync(vehicle.Id, cancellationToken) is not null;

    private static void EnsureAvailable(Vehicle vehicle, bool hasActiveRental)
    {
        if (hasActiveRental || vehicle.AvailabilityStatus != VehicleAvailabilityStatus.Available)
        {
            throw new ConflictException($"Vehicle '{vehicle.RegistrationNumber}' is not available.");
        }
    }

    private async Task<RentalDto> StartAsync(
        Vehicle vehicle,
        Customer customer,
        bool isNewCustomer,
        int rentalDays,
        bool promotionalDiscountRequested,
        CancellationToken cancellationToken)
    {
        DateOnly startDate = Today();
        DateOnly returnDate = startDate.AddDays(rentalDays);

        int billableDays = Rental.CalculateBillableDays(startDate, returnDate);
        IVehiclePricingStrategy strategy =
            PricingPolicy.SelectStrategy(billableDays, promotionalDiscountRequested);

        // Every check has passed. Starting the rental is the first change to any entity,
        // and nothing is stored until it succeeds.
        Rental rental;

        try
        {
            rental = Rental.Start(customer, vehicle, startDate, returnDate, strategy);
        }
        catch (InvalidOperationException ex)
        {
            // The domain refused a state change, for example because the vehicle was rented in the meantime.
            throw new ConflictException(ex.Message, ex);
        }

        if (isNewCustomer)
        {
            await _customers.AddAsync(customer, cancellationToken);
        }

        await _rentals.AddAsync(rental, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return rental.ToDto();
    }

    private async Task<RentalDto> CompleteAsync(Rental rental, CancellationToken cancellationToken)
    {
        try
        {
            rental.Complete(Today());
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message, ex);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return rental.ToDto();
    }

    private async Task<Vehicle> GetVehicleAsync(string registrationNumber, CancellationToken cancellationToken) =>
        await _vehicles.GetByRegistrationNumberAsync(
            Vehicle.NormalizeRegistrationNumber(registrationNumber),
            cancellationToken)
        ?? throw new NotFoundException($"Vehicle '{registrationNumber}' was not found.");

    private DateOnly Today() =>
        DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
}
