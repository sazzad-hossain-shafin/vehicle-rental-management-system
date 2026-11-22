using Microsoft.EntityFrameworkCore;
using VehicleRental.Application.Tenancy;
using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Infrastructure.Tenancy;

/// <summary>
/// Looks companies up so a request can be resolved to a tenant. It reads the Companies table through a context that has
/// NO tenant (companies are not tenant data), and it is not used by any production route yet.
/// </summary>
internal sealed class CompanyDirectory : ICompanyDirectory
{
    private readonly VehicleRentalDbContext _db;

    public CompanyDirectory(VehicleRentalDbContext db) => _db = db;

    public async Task<CompanySummary?> FindActiveBySlugAsync(string? slug, CancellationToken cancellationToken = default)
    {
        string normalized = Company.NormalizeSlug(slug);

        try
        {
            Company.ValidateSlug(normalized);
        }
        catch (ArgumentException)
        {
            return null; // malformed or reserved: no database round trip, same answer as "unknown"
        }

        return await _db.Companies
            .AsNoTracking()
            .Where(c => c.Slug == normalized && c.Status == CompanyStatus.Active)
            .Select(c => new CompanySummary(c.Id, c.Slug, c.TimeZoneId, c.Status))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CompanySummary?> FindManageableAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            return null;
        }

        return await _db.Companies
            .AsNoTracking()
            .Where(c => c.Id == companyId && (c.Status == CompanyStatus.Active || c.Status == CompanyStatus.PendingReview))
            .Select(c => new CompanySummary(c.Id, c.Slug, c.TimeZoneId, c.Status))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListActiveIdsAsync(CancellationToken cancellationToken = default) =>
        await _db.Companies
            .AsNoTracking()
            .Where(c => c.Status == CompanyStatus.Active)
            .OrderBy(c => c.Slug)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
}

/// <summary>Reads memberships. Consulted on every management request; access is never taken from the token.</summary>
internal sealed class MembershipDirectory : IMembershipDirectory
{
    private readonly VehicleRentalDbContext _db;

    public MembershipDirectory(VehicleRentalDbContext db) => _db = db;

    public async Task<CompanyRole?> FindActiveRoleAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty || userId == Guid.Empty)
        {
            return null;
        }

        return await _db.CompanyMemberships
            .AsNoTracking()
            .Where(m => m.CompanyId == companyId && m.UserId == userId && m.Status == MembershipStatus.Active)
            .Select(m => (CompanyRole?)m.Role)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
