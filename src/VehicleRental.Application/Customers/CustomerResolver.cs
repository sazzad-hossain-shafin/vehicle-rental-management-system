using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Application.Customers;

/// <summary>
/// The rule for matching a customer number and name against stored customers, shared
/// by <see cref="CustomerService"/> and the rental workflow.
/// </summary>
/// <remarks>
/// An unknown number means a new customer. A known number with the same name (ignoring
/// case and surrounding spaces) reuses the stored customer. A known number with a
/// different name is a conflict: stored identity data is never overwritten.
/// </remarks>
internal static class CustomerResolver
{
    /// <returns>
    /// The customer to use, and whether it is new. A new customer has not been
    /// stored yet; the caller adds it once its own operation has succeeded.
    /// </returns>
    /// <exception cref="ArgumentException">The number or name is empty or too long.</exception>
    /// <exception cref="ConflictException">The number belongs to a customer with a different name.</exception>
    public static async Task<(Customer Customer, bool IsNew)> ResolveAsync(
        ICustomerRepository customers,
        string customerNumber,
        string name,
        CancellationToken cancellationToken)
    {
        var candidate = new Customer(customerNumber, name);

        Customer? existing = await customers.GetByCustomerNumberAsync(candidate.CustomerNumber, cancellationToken);

        if (existing is null)
        {
            return (candidate, true);
        }

        if (!string.Equals(existing.Name, candidate.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                $"Customer number '{existing.CustomerNumber}' is already registered under a different name.");
        }

        return (existing, false);
    }
}
