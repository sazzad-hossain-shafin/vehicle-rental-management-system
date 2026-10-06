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
    /// <exception cref="ConflictException">A vehicle with this ID already exists.</exception>
    public async Task<VehicleDto> AddVehicleAsync(
        AddVehicleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vehicle = new Vehicle(
            request.Id,
            request.Make,
            request.Model,
            request.Year,
            request.VehicleType,
            request.DailyRate);

        if (await _vehicles.GetByIdAsync(vehicle.Id, cancellationToken) is not null)
        {
            throw new ConflictException($"A vehicle with ID '{vehicle.Id}' already exists.");
        }

        await _vehicles.AddAsync(vehicle, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return vehicle.ToDto();
    }

    /// <exception cref="NotFoundException">No vehicle has this ID.</exception>
    public async Task<VehicleDto> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var vehicle = await _vehicles.GetByIdAsync((id ?? "").Trim(), cancellationToken);

        return vehicle?.ToDto()
               ?? throw new NotFoundException($"Vehicle '{id}' was not found.");
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
}
