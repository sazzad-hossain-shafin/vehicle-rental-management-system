using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Application.Customers;

public sealed class CustomerService
{
    private readonly ICustomerRepository _customers;
    private readonly IUnitOfWork _unitOfWork;

    public CustomerService(ICustomerRepository customers, IUnitOfWork unitOfWork)
    {
        _customers = customers;
        _unitOfWork = unitOfWork;
    }

    /// <exception cref="NotFoundException">No customer has this number.</exception>
    public async Task<CustomerDto> GetByCustomerNumberAsync(
        string customerNumber,
        CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetByCustomerNumberAsync(
            Customer.NormalizeCustomerNumber(customerNumber),
            cancellationToken);

        return customer?.ToDto()
               ?? throw new NotFoundException($"Customer '{customerNumber}' was not found.");
    }

    /// <summary>
    /// Returns the customer with this number, registering a new one if none exists.
    /// See <see cref="CustomerResolver"/> for how an existing customer is matched.
    /// </summary>
    /// <exception cref="ArgumentException">The number or name is empty or too long.</exception>
    /// <exception cref="ConflictException">The number belongs to a customer with a different name.</exception>
    public async Task<CustomerDto> RegisterOrGetAsync(
        string customerNumber,
        string name,
        CancellationToken cancellationToken = default)
    {
        var (customer, isNew) = await CustomerResolver.ResolveAsync(
            _customers,
            customerNumber,
            name,
            cancellationToken);

        if (isNew)
        {
            await _customers.AddAsync(customer, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return customer.ToDto();
    }
}
