using Microsoft.EntityFrameworkCore;
using VehicleRental.Application.Abstractions;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Infrastructure.Persistence;

public sealed class CustomerRepository : ICustomerRepository
{
    private readonly VehicleRentalDbContext _db;

    public CustomerRepository(VehicleRentalDbContext db) => _db = db;

    // Tracked: a rental started with this customer attaches to the same instance.
    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<Customer?> GetByCustomerNumberAsync(
        string customerNumber,
        CancellationToken cancellationToken = default) =>
        _db.Customers.FirstOrDefaultAsync(c => c.CustomerNumber == customerNumber, cancellationToken);

    // The INSERT happens in IUnitOfWork.SaveChangesAsync, where the unique index decides duplicates.
    public Task AddAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        _db.Customers.Add(customer);

        return Task.CompletedTask;
    }
}
