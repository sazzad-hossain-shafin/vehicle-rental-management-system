namespace VehicleRental.Application.Security;

/// <summary>
/// The role names used across the system. Referenced everywhere instead of repeating the strings.
/// </summary>
public static class Roles
{
    /// <summary>Runs the business and can administer staff accounts.</summary>
    public const string Admin = "Admin";

    /// <summary>Works the rental desk: fleet, customers and rentals.</summary>
    public const string Staff = "Staff";

    /// <summary>A customer with an online account. Sees only their own data.</summary>
    public const string Customer = "Customer";

    public static IReadOnlyList<string> All { get; } = [Admin, Staff, Customer];
}

/// <summary>
/// The claim types written into access tokens and read back by the API.
/// </summary>
public static class AuthClaims
{
    public const string Subject = "sub";
    public const string Role = "role";

    /// <summary>The ID of the <c>Customer</c> a customer account belongs to. Only present for customer accounts.</summary>
    public const string CustomerId = "customer_id";
}
