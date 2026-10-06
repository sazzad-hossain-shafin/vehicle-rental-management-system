using VehicleRental.Application.Abstractions;
using VehicleRental.Application.Exceptions;

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

    /// <exception cref="NotFoundException">No customer has this ID.</exception>
    public async Task<CustomerDto> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetByIdAsync((id ?? "").Trim(), cancellationToken);

        return customer?.ToDto()
               ?? throw new NotFoundException($"Customer '{id}' was not found.");
    }

    /// <summary>
    /// Returns the customer with this ID, registering a new one if none exists.
    /// See <see cref="CustomerResolver"/> for how an existing customer is matched.
    /// </summary>
    /// <exception cref="ArgumentException">The ID or name is empty.</exception>
    /// <exception cref="ConflictException">The ID belongs to a customer with a different name.</exception>
    public async Task<CustomerDto> RegisterOrGetAsync(
        string id,
        string name,
        CancellationToken cancellationToken = default)
    {
        var (customer, isNew) = await CustomerResolver.ResolveAsync(_customers, id, name, cancellationToken);

        if (isNew)
        {
            await _customers.AddAsync(customer, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return customer.ToDto();
    }
}
