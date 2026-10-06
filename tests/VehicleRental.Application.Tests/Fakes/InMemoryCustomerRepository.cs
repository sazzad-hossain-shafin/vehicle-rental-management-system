using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Application.Tests.Fakes;

/// <summary>
/// Test double for <see cref="ICustomerRepository"/>. Customer numbers are unique.
/// </summary>
internal sealed class InMemoryCustomerRepository : ICustomerRepository
{
    private readonly List<Customer> _customers = new();

    public int Count => _customers.Count;

    public Task<Customer?> GetByCustomerNumberAsync(
        string customerNumber,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Find(customerNumber));
    }

    public Task AddAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Find(customer.CustomerNumber) is not null)
        {
            throw new ConflictException(
                $"A customer with number '{customer.CustomerNumber}' already exists.");
        }

        _customers.Add(customer);

        return Task.CompletedTask;
    }

    private Customer? Find(string customerNumber) =>
        _customers.FirstOrDefault(c => c.CustomerNumber == customerNumber);
}
