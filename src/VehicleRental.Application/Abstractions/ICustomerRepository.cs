using VehicleRental.Domain.Entities;

namespace VehicleRental.Application.Abstractions;

public interface ICustomerRepository
{
    /// <param name="customerNumber">A customer number in normalized form; see <see cref="Customer.NormalizeCustomerNumber"/>.</param>
    Task<Customer?> GetByCustomerNumberAsync(
        string customerNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a customer. It is stored when <see cref="IUnitOfWork.SaveChangesAsync"/> succeeds.
    /// </summary>
    /// <exception cref="Exceptions.ConflictException">
    /// The customer number is already used. Depending on the implementation this is
    /// raised here or when the changes are saved.
    /// </exception>
    Task AddAsync(Customer customer, CancellationToken cancellationToken = default);
}
