using VehicleRental.Domain.Entities;

namespace VehicleRental.Application.Abstractions;

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <exception cref="Exceptions.ConflictException">A customer with the same ID already exists.</exception>
    Task AddAsync(Customer customer, CancellationToken cancellationToken = default);
}
