using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Application.Vehicles;

public sealed class VehicleService
{
    private readonly IVehicleRepository _vehicles;
    private readonly IUnitOfWork _unitOfWork;

    public VehicleService(IVehicleRepository vehicles, IUnitOfWork unitOfWork)
    {
        _vehicles = vehicles;
        _unitOfWork = unitOfWork;
    }

    /// <exception cref="ArgumentException">The vehicle details are invalid (see <see cref="Vehicle"/>).</exception>
    /// <exception cref="ConflictException">A vehicle with this registration number already exists.</exception>
    public async Task<VehicleDto> AddVehicleAsync(
        AddVehicleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vehicle = new Vehicle(
            request.RegistrationNumber,
            request.Make,
            request.Model,
            request.Year,
            request.VehicleType,
            request.DailyRate);

        // A friendly early check. The database's unique index still decides a race between two requests.
        if (await _vehicles.GetByRegistrationNumberAsync(vehicle.RegistrationNumber, cancellationToken) is not null)
        {
            throw new ConflictException(
                $"A vehicle with registration number '{vehicle.RegistrationNumber}' already exists.");
        }

        await _vehicles.AddAsync(vehicle, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return vehicle.ToDto();
    }

    /// <exception cref="NotFoundException">No vehicle has this ID.</exception>
    public async Task<VehicleDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var vehicle = await _vehicles.GetByIdAsync(id, cancellationToken);

        return vehicle?.ToDto()
               ?? throw new NotFoundException($"Vehicle '{id}' was not found.");
    }

    /// <exception cref="NotFoundException">No vehicle has this registration number.</exception>
    public async Task<VehicleDto> GetByRegistrationNumberAsync(
        string registrationNumber,
        CancellationToken cancellationToken = default)
    {
        var vehicle = await _vehicles.GetByRegistrationNumberAsync(
            Vehicle.NormalizeRegistrationNumber(registrationNumber),
            cancellationToken);

        return vehicle?.ToDto()
               ?? throw new NotFoundException($"Vehicle '{registrationNumber}' was not found.");
    }

    public async Task<IReadOnlyList<VehicleDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var vehicles = await _vehicles.GetAllAsync(cancellationToken);

        return vehicles.Select(v => v.ToDto()).ToList();
    }

    /// <summary>
    /// Finds vehicles by type, maximum daily rate and/or availability.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The maximum daily rate is negative.</exception>
    public async Task<IReadOnlyList<VehicleDto>> SearchAsync(
        VehicleSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        if (criteria.MaximumDailyRate < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(criteria),
                "The maximum daily rate cannot be negative.");
        }

        var vehicles = await _vehicles.SearchAsync(criteria, cancellationToken);

        return vehicles.Select(v => v.ToDto()).ToList();
    }

    /// <summary>
    /// One page of the vehicles matching the criteria, ordered by registration number.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The page or page size is out of range (see <see cref="Paging"/>), or the maximum daily rate is negative.
    /// </exception>
    public async Task<PagedResult<VehicleDto>> SearchPageAsync(
        VehicleSearchCriteria criteria,
        int page = 1,
        int pageSize = Paging.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        if (criteria.MaximumDailyRate < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(criteria),
                "The maximum daily rate cannot be negative.");
        }

        int skip = Paging.ToSkip(page, pageSize);

        var result = await _vehicles.SearchPageAsync(criteria, skip, pageSize, cancellationToken);

        return new PagedResult<VehicleDto>(
            result.Items.Select(v => v.ToDto()).ToList(),
            page,
            pageSize,
            result.TotalCount);
    }
}
