using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Application.InMemory;

/// <summary>
/// TEMPORARY in-memory storage, used until a database implementation exists. Customer
/// IDs are unique, ignoring case. It is not thread-safe.
/// </summary>
public sealed class InMemoryCustomerRepository : ICustomerRepository
{
    private readonly List<Customer> _customers = new();

    /// <summary>The number of stored customers.</summary>
    public int Count => _customers.Count;

    public Task<Customer?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Find(id));
    }

    public Task AddAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Find(customer.Id) is not null)
        {
            throw new ConflictException($"A customer with ID '{customer.Id}' already exists.");
        }

        _customers.Add(customer);

        return Task.CompletedTask;
    }

    private Customer? Find(string id) =>
        _customers.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
}
