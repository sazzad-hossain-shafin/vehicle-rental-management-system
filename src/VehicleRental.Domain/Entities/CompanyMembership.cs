using VehicleRental.Domain.Enums;

namespace VehicleRental.Domain.Entities;

/// <summary>
/// A user's role inside one company. There is at most one row per company and user: removing someone keeps the row
/// (Status = Removed) as history, and adding them again restores it. Phase 14A-3 only adds the structure.
/// </summary>
public class CompanyMembership
{
    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>The login (an Identity user). Held as an ID only, so the domain does not depend on Identity.</summary>
    public Guid UserId { get; private set; }

    public CompanyRole Role { get; private set; }

    public MembershipStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Who added the member (null for the first owner and for system actions).</summary>
    public Guid? CreatedByUserId { get; private set; }

    private CompanyMembership()
    {
    }

    public CompanyMembership(Guid companyId, Guid userId, CompanyRole role, DateTimeOffset createdAt, Guid? createdByUserId = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company is required.", nameof(companyId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A user is required.", nameof(userId));
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        Id = Guid.CreateVersion7();
        CompanyId = companyId;
        UserId = userId;
        Role = role;
        Status = MembershipStatus.Active;
        CreatedAt = createdAt;
        CreatedByUserId = createdByUserId;
    }

    /// <summary>Takes access away but keeps the history.</summary>
    public void Remove()
    {
        if (Status == MembershipStatus.Removed)
        {
            throw new InvalidOperationException("The membership is already removed.");
        }

        Status = MembershipStatus.Removed;
    }

    /// <summary>Gives a removed member access again, with the role chosen now.</summary>
    public void Restore(CompanyRole role)
    {
        if (Status != MembershipStatus.Removed)
        {
            throw new InvalidOperationException("Only a removed membership can be restored.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        Role = role;
        Status = MembershipStatus.Active;
    }
}
