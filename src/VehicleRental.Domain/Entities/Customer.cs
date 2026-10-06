namespace VehicleRental.Domain.Entities;

/// <summary>
/// A customer who can rent vehicles. It has a generated, permanent <see cref="Id"/> and a
/// business identifier, its <see cref="CustomerNumber"/>.
/// </summary>
public class Customer
{
    public const int CustomerNumberMaxLength = 20;
    public const int NameMaxLength = 200;

    /// <summary>The permanent internal identifier. It never changes, unlike business identifiers.</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// The identifier people use for this customer, stored trimmed and upper-case.
    /// It is unique across customers.
    /// </summary>
    public string CustomerNumber { get; private set; }

    public string Name { get; private set; }

    /// <summary>
    /// For the persistence layer only. It bypasses validation because it is used to rebuild
    /// customers that were already validated when first created.
    /// </summary>
    private Customer()
    {
        CustomerNumber = null!;
        Name = null!;
    }

    /// <exception cref="ArgumentException">
    /// The customer number or name is empty, or longer than the allowed length.
    /// </exception>
    public Customer(string customerNumber, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string normalizedNumber = NormalizeCustomerNumber(customerNumber);

        if (normalizedNumber.Length > CustomerNumberMaxLength)
        {
            throw new ArgumentException(
                $"The customer number cannot exceed {CustomerNumberMaxLength} characters.",
                nameof(customerNumber));
        }

        if (name.Trim().Length > NameMaxLength)
        {
            throw new ArgumentException($"The name cannot exceed {NameMaxLength} characters.", nameof(name));
        }

        Id = Guid.CreateVersion7();
        CustomerNumber = normalizedNumber;
        Name = name.Trim();
    }

    /// <summary>
    /// The form in which customer numbers are stored and compared: trimmed and upper-case.
    /// Use it on user input before looking a customer up.
    /// </summary>
    public static string NormalizeCustomerNumber(string? value) =>
        (value ?? "").Trim().ToUpperInvariant();
}
