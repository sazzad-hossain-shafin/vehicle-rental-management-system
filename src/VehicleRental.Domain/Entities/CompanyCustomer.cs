namespace VehicleRental.Domain.Entities;

/// <summary>
/// The link between a company and a customer (the person). The customer number belongs to the link, so it is unique
/// within a company, and two companies can each know the same person under different numbers. A company sees a
/// customer only through its own links. Phase 14A-3 only adds the structure; today's customers keep their global
/// number until the backfill phase creates their links.
/// </summary>
public class CompanyCustomer
{
    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid CustomerId { get; private set; }

    /// <summary>Normalised (trimmed, upper-case) like <see cref="Customer.CustomerNumber"/>.</summary>
    public string CustomerNumber { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private CompanyCustomer()
    {
        CustomerNumber = null!;
    }

    public CompanyCustomer(Guid companyId, Guid customerId, string customerNumber, DateTimeOffset createdAt)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company is required.", nameof(companyId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("A customer is required.", nameof(customerId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(customerNumber);

        string normalized = Customer.NormalizeCustomerNumber(customerNumber);

        if (normalized.Length > Customer.CustomerNumberMaxLength)
        {
            throw new ArgumentException(
                $"The customer number cannot exceed {Customer.CustomerNumberMaxLength} characters.",
                nameof(customerNumber));
        }

        Id = Guid.CreateVersion7();
        CompanyId = companyId;
        CustomerId = customerId;
        CustomerNumber = normalized;
        CreatedAt = createdAt;
    }
}
