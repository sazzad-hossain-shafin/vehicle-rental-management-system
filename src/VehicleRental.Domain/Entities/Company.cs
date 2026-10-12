using System.Text.RegularExpressions;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Domain.Entities;

/// <summary>
/// An independent rental company on the platform. Phase 14A-3 only adds the structure: nothing creates or reads
/// companies yet, and nothing in the running application depends on this class.
/// </summary>
public partial class Company
{
    public const int SlugMinLength = 3;
    public const int SlugMaxLength = 63;
    public const int NameMaxLength = 200;
    public const int TimeZoneIdMaxLength = 64;

    /// <summary>
    /// Lower-case letters and digits, with single hyphens only between them (so no leading, trailing or doubled hyphen).
    /// The same rule is repeated as a database check constraint.
    /// </summary>
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    /// <summary>An IANA zone name such as "Australia/Sydney" or "UTC". Windows names (they contain spaces) are not accepted.</summary>
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_+\-]*(?:/[A-Za-z0-9_+\-]+){0,2}$")]
    private static partial Regex TimeZonePattern();

    /// <summary>
    /// Names that would collide with the website's own routes or look like official platform addresses. Compared after
    /// normalisation, so "Admin" and "admin" are both refused.
    /// </summary>
    public static IReadOnlySet<string> ReservedSlugs { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "about", "account", "admin", "administrator", "api", "app", "assets", "auth", "billing", "blog", "c", "company",
        "companies", "contact", "dashboard", "docs", "health", "help", "home", "legal", "login", "logout", "manage",
        "me", "openapi", "platform", "pricing", "privacy", "register", "reservations", "root", "scalar", "settings",
        "signin", "signup", "static", "status", "support", "system", "terms", "vehicles", "www",
    };

    public Guid Id { get; private set; }

    public string Slug { get; private set; }

    public string Name { get; private set; }

    public CompanyStatus Status { get; private set; }

    /// <summary>An IANA zone name. All "today" date rules will be evaluated in this zone (a later phase).</summary>
    public string TimeZoneId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When a platform administrator approved the company; null until then.</summary>
    public DateTimeOffset? ApprovedAt { get; private set; }

    private Company()
    {
        Slug = null!;
        Name = null!;
        TimeZoneId = null!;
    }

    /// <summary>Creates a company awaiting review. Throws <see cref="ArgumentException"/> for an invalid slug, name or time zone.</summary>
    public Company(string slug, string name, string timeZoneId, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string normalizedSlug = NormalizeSlug(slug);
        ValidateSlug(normalizedSlug);

        if (name.Trim().Length > NameMaxLength)
        {
            throw new ArgumentException($"The name cannot exceed {NameMaxLength} characters.", nameof(name));
        }

        Id = Guid.CreateVersion7();
        Slug = normalizedSlug;
        Name = name.Trim();
        TimeZoneId = ValidateTimeZone(timeZoneId);
        Status = CompanyStatus.PendingReview;
        CreatedAt = createdAt;
    }

    /// <summary>Trims and lower-cases a slug, so "Acme-Rentals " and "acme-rentals" are the same address.</summary>
    public static string NormalizeSlug(string? value) => (value ?? "").Trim().ToLowerInvariant();

    /// <summary>Checks an already normalised slug; throws <see cref="ArgumentException"/> saying what is wrong.</summary>
    public static void ValidateSlug(string normalizedSlug)
    {
        if (normalizedSlug.Length is < SlugMinLength or > SlugMaxLength)
        {
            throw new ArgumentException($"The web address must be {SlugMinLength}-{SlugMaxLength} characters.", nameof(normalizedSlug));
        }

        if (!SlugPattern().IsMatch(normalizedSlug))
        {
            throw new ArgumentException(
                "The web address may contain only lower-case letters, digits and single hyphens between them " +
                "(it cannot start or end with a hyphen).",
                nameof(normalizedSlug));
        }

        if (ReservedSlugs.Contains(normalizedSlug))
        {
            throw new ArgumentException($"'{normalizedSlug}' is reserved and cannot be used as a web address.", nameof(normalizedSlug));
        }
    }

    /// <summary>Returns the zone name when it is a known IANA zone; throws <see cref="ArgumentException"/> otherwise.</summary>
    public static string ValidateTimeZone(string? timeZoneId)
    {
        string value = (timeZoneId ?? "").Trim();

        if (value.Length is 0 or > TimeZoneIdMaxLength || !TimeZonePattern().IsMatch(value))
        {
            throw new ArgumentException("The time zone must be an IANA name such as 'Australia/Sydney'.", nameof(timeZoneId));
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(value);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException($"'{value}' is not a known time zone.", nameof(timeZoneId), ex);
        }

        return value;
    }

    /// <summary>A platform administrator approves a company that is awaiting review.</summary>
    public void Approve(DateTimeOffset now)
    {
        if (Status != CompanyStatus.PendingReview)
        {
            throw new InvalidOperationException($"Only a company awaiting review can be approved; this one is {Status}.");
        }

        Status = CompanyStatus.Active;
        ApprovedAt = now;
    }

    /// <summary>Switches a company off. A company never approved is simply rejected this way.</summary>
    public void Suspend()
    {
        if (Status == CompanyStatus.Suspended)
        {
            throw new InvalidOperationException("The company is already suspended.");
        }

        Status = CompanyStatus.Suspended;
    }

    /// <summary>Switches a suspended company back on. A company that was never approved cannot be reactivated, only approved.</summary>
    public void Reactivate()
    {
        if (Status != CompanyStatus.Suspended || ApprovedAt is null)
        {
            throw new InvalidOperationException("Only a company that was approved and then suspended can be reactivated.");
        }

        Status = CompanyStatus.Active;
    }
}
