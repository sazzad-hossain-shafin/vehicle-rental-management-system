using Microsoft.AspNetCore.Identity;
using VehicleRental.Domain.Entities;

namespace VehicleRental.Infrastructure.Identity;

/// <summary>
/// A login. It is a security entity, separate from the business <see cref="Domain.Entities.Customer"/>:
/// Identity owns the credentials, the domain owns the customer. The two are linked only here, so the
/// domain knows nothing about Identity.
/// </summary>
/// <remarks>
/// Staff and admin accounts have no customer. A customer account links to exactly one customer, and
/// each customer has at most one account (enforced by a unique index). The login name is the email.
/// </remarks>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>The customer this account belongs to; null for staff and admin accounts.</summary>
    public Guid? CustomerId { get; set; }

    public Customer? Customer { get; set; }
}
