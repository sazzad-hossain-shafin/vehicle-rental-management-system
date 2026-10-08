using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;
using VehicleRental.Domain.Pricing;

namespace VehicleRental.Application.Reservations;

/// <summary>
/// The reservation use cases: checking availability for dates, booking, cancelling and picking up. The domain
/// owns the rules (dates, pricing, lifecycle); this service finds the entities, asks the database-backed
/// availability query, and saves each change as one atomic unit.
/// </summary>
/// <remarks>
/// Availability is checked here to give a useful error, but the database has the final say: it refuses two
/// active reservations of one vehicle with overlapping dates, so two simultaneous requests cannot both succeed.
/// The loser gets a <see cref="ConflictException"/> when saving.
/// </remarks>
public sealed class ReservationService
{
    private readonly IVehicleRepository _vehicles;
    private readonly ICustomerRepository _customers;
    private readonly IReservationRepository _reservations;
    private readonly IRentalRepository _rentals;
    private readonly IVehicleAvailabilityQuery _availability;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public ReservationService(
        IVehicleRepository vehicles,
        ICustomerRepository customers,
        IReservationRepository reservations,
        IRentalRepository rentals,
        IVehicleAvailabilityQuery availability,
        IUnitOfWork unitOfWork,
        TimeProvider? timeProvider = null)
    {
        _vehicles = vehicles;
        _customers = customers;
        _reservations = reservations;
        _rentals = rentals;
        _availability = availability;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// One page of the vehicles that are free for the whole period <c>[startDate, endDate)</c>, ordered by
    /// registration number.
    /// </summary>
    /// <exception cref="ArgumentException">The period breaks the booking rules (see <see cref="Reservation.ValidatePeriod"/>).</exception>
    /// <exception cref="ArgumentOutOfRangeException">The page or page size is out of range.</exception>
    public async Task<PagedResult<VehicleDto>> GetAvailableVehiclesPageAsync(
        DateOnly startDate,
        DateOnly endDate,
        VehicleType? vehicleType = null,
        decimal? maximumDailyRate = null,
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        DateOnly today = Today();
        Reservation.ValidatePeriod(startDate, endDate, today);
        int skip = Paging.ToSkip(page, pageSize);

        var result = await _availability.SearchAvailableAsync(
            vehicleType, maximumDailyRate, startDate, endDate, today, skip, pageSize, cancellationToken);

        return new PagedResult<VehicleDto>(
            result.Items.Select(v => v.ToDto()).ToList(), page, pageSize, result.TotalCount);
    }

    /// <summary>
    /// Reserves a vehicle for a customer and stores the quoted price.
    /// </summary>
    /// <exception cref="ArgumentException">The dates break the booking rules.</exception>
    /// <exception cref="NotFoundException">The vehicle or customer does not exist.</exception>
    /// <exception cref="ConflictException">The vehicle is not free for the whole period, or another request took it first.</exception>
    public async Task<ReservationDto> CreateAsync(
        CreateReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateOnly today = Today();
        Reservation.ValidatePeriod(request.StartDate, request.EndDate, today);

        Vehicle vehicle = await _vehicles.GetByIdAsync(request.VehicleId, cancellationToken)
                          ?? throw new NotFoundException($"Vehicle '{request.VehicleId}' was not found.");

        Customer customer = await _customers.GetByIdAsync(request.CustomerId, cancellationToken)
                            ?? throw new NotFoundException($"Customer '{request.CustomerId}' was not found.");

        // Serialise with every other booking of this vehicle, then check: the check must see the result of any
        // rental or reservation that was being created at the same moment.
        await _unitOfWork.LockVehicleAsync(vehicle.Id, cancellationToken);

        if (!await _availability.IsAvailableAsync(
                vehicle.Id, request.StartDate, request.EndDate, today, cancellationToken))
        {
            throw new ConflictException(
                $"Vehicle '{vehicle.RegistrationNumber}' is not available from " +
                $"{request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}.");
        }

        int billableDays = Rental.CalculateBillableDays(request.StartDate, request.EndDate);
        IVehiclePricingStrategy strategy =
            PricingPolicy.SelectStrategy(billableDays, request.PromotionalDiscountRequested);

        Reservation reservation = Reservation.Create(
            customer, vehicle, request.StartDate, request.EndDate, today, _timeProvider.GetUtcNow(), strategy);

        await _reservations.AddAsync(reservation, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return reservation.ToDto(Today());
    }

    /// <exception cref="NotFoundException">The reservation does not exist.</exception>
    public async Task<ReservationDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await _reservations.GetByIdAsync(id, cancellationToken)
         ?? throw NotFound(id)).ToDto(Today());

    /// <summary>
    /// A reservation, but only if it belongs to the given customer. Someone else's reservation is reported
    /// exactly like one that does not exist.
    /// </summary>
    /// <exception cref="NotFoundException">The reservation does not exist or belongs to another customer.</exception>
    public async Task<ReservationDto> GetForCustomerAsync(
        Guid id,
        Guid customerId,
        CancellationToken cancellationToken = default) =>
        (await GetOwnedAsync(id, customerId, cancellationToken)).ToDto(Today());

    /// <summary>One page of all reservations (optionally one status), earliest start date first.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The page or page size is out of range.</exception>
    public async Task<PagedResult<ReservationDto>> GetPageAsync(
        ReservationStatus? status = null,
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        int skip = Paging.ToSkip(page, pageSize);

        var result = await _reservations.GetPageAsync(status, skip, pageSize, cancellationToken);

        return ToPage(result, page, pageSize);
    }

    /// <summary>One page of a single customer's reservations, earliest start date first.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The page or page size is out of range.</exception>
    public async Task<PagedResult<ReservationDto>> GetPageForCustomerAsync(
        Guid customerId,
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        int skip = Paging.ToSkip(page, pageSize);

        var result = await _reservations.GetPageForCustomerAsync(customerId, skip, pageSize, cancellationToken);

        return ToPage(result, page, pageSize);
    }

    /// <summary>Cancels an active reservation on behalf of the business (staff or admin).</summary>
    /// <exception cref="NotFoundException">The reservation does not exist.</exception>
    /// <exception cref="ConflictException">The reservation is not active, or was just changed by another request.</exception>
    public async Task<ReservationDto> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Reservation reservation = await _reservations.GetByIdAsync(id, cancellationToken)
                                  ?? throw NotFound(id);

        return await CancelAsync(reservation, cancellationToken);
    }

    /// <summary>
    /// Cancels a customer's own reservation. Only an active reservation that has not started yet can be cancelled
    /// this way; after its start date the rental desk handles it.
    /// </summary>
    /// <exception cref="NotFoundException">The reservation does not exist or belongs to another customer.</exception>
    /// <exception cref="ConflictException">The reservation cannot be cancelled online, or was just changed by another request.</exception>
    public async Task<ReservationDto> CancelForCustomerAsync(
        Guid id,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        Reservation reservation = await GetOwnedAsync(id, customerId, cancellationToken);

        if (reservation.Status != ReservationStatus.Active)
        {
            throw new ConflictException($"The reservation is already {reservation.Status.ToString().ToLowerInvariant()}.");
        }

        if (!reservation.CanBeCancelledByCustomer(Today()))
        {
            throw new ConflictException(
                "A reservation that has already started can only be cancelled by the rental desk.");
        }

        return await CancelAsync(reservation, cancellationToken);
    }

    /// <summary>
    /// Picks a reservation up: starts the rental, marks the vehicle rented and the reservation fulfilled, all
    /// saved together or not at all. The rental honours the reservation's quote.
    /// </summary>
    /// <exception cref="NotFoundException">The reservation does not exist.</exception>
    /// <exception cref="ConflictException">
    /// The reservation is not active, today is outside its pickup window, the vehicle is still rented out, or
    /// another request picked it up first.
    /// </exception>
    public async Task<PickupResultDto> PickUpAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Reservation reservation = await _reservations.GetByIdAsync(id, cancellationToken)
                                  ?? throw NotFound(id);

        Rental rental;

        try
        {
            rental = reservation.PickUp(Today(), _timeProvider.GetUtcNow());
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message, ex);
        }

        // Rental, vehicle status and reservation status are saved in one database transaction. If two
        // requests race, the reservation and vehicle row versions let only one of them save.
        await _rentals.AddAsync(rental, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new PickupResultDto(reservation.ToDto(Today()), rental.ToDto());
    }

    private async Task<ReservationDto> CancelAsync(Reservation reservation, CancellationToken cancellationToken)
    {
        try
        {
            reservation.Cancel(_timeProvider.GetUtcNow());
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException(ex.Message, ex);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return reservation.ToDto(Today());
    }

    private async Task<Reservation> GetOwnedAsync(Guid id, Guid customerId, CancellationToken cancellationToken)
    {
        Reservation? reservation = await _reservations.GetByIdAsync(id, cancellationToken);

        if (reservation is null || reservation.Customer.Id != customerId)
        {
            throw NotFound(id);
        }

        return reservation;
    }

    private static NotFoundException NotFound(Guid id) => new($"Reservation '{id}' was not found.");

    private PagedResult<ReservationDto> ToPage(PageResult<Reservation> result, int page, int pageSize)
    {
        DateOnly today = Today();

        return new(result.Items.Select(r => r.ToDto(today)).ToList(), page, pageSize, result.TotalCount);
    }

    private DateOnly Today() =>
        DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
}
