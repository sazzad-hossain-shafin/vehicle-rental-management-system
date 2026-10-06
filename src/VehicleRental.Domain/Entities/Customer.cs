namespace VehicleRental.Domain.Entities;

/// <summary>
/// A customer who can rent vehicles.
/// </summary>
public class Customer
{
    public string Id { get; }
    public string Name { get; }

    /// <exception cref="ArgumentException">The ID or name is null, empty or whitespace.</exception>
    public Customer(string id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id.Trim();
        Name = name.Trim();
    }
}
