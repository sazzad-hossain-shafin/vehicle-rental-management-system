using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Domain.Tests;

public class CompanyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private static Company NewCompany(string slug = "acme-rentals") => new(slug, "Acme Rentals", "Australia/Sydney", Now);

    [Fact]
    public void New_company_awaits_review_and_is_not_approved()
    {
        Company company = NewCompany();

        Assert.NotEqual(Guid.Empty, company.Id);
        Assert.Equal("acme-rentals", company.Slug);
        Assert.Equal("Acme Rentals", company.Name);
        Assert.Equal(CompanyStatus.PendingReview, company.Status);
        Assert.Null(company.ApprovedAt);
        Assert.Equal("Australia/Sydney", company.TimeZoneId);
        Assert.Equal(Now, company.CreatedAt);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("a-b-c")]
    [InlineData("acme-rentals-2")]
    [InlineData("123")]
    [InlineData("a1b")]
    public void Valid_slugs_are_accepted(string slug) => Assert.Equal(slug, NewCompany(slug).Slug);

    [Fact]
    public void A_slug_of_the_maximum_length_is_accepted()
    {
        string slug = new('a', Company.SlugMaxLength);

        Assert.Equal(slug, NewCompany(slug).Slug);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    [InlineData("-abc")]
    [InlineData("abc-")]
    [InlineData("a--b")]
    [InlineData("a b c")]
    [InlineData("acme_rentals")]
    [InlineData("acme.rentals")]
    [InlineData("acmé")]
    [InlineData("acme/rentals")]
    public void Slugs_with_a_bad_length_character_or_hyphen_position_are_rejected(string slug) =>
        Assert.Throws<ArgumentException>(() => NewCompany(slug));

    [Fact]
    public void A_slug_that_is_too_long_is_rejected() =>
        Assert.Throws<ArgumentException>(() => NewCompany(new string('a', Company.SlugMaxLength + 1)));

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("api")]
    [InlineData("www")]
    [InlineData("manage")]
    [InlineData("platform")]
    [InlineData("login")]
    public void Reserved_names_are_rejected_whatever_their_case(string slug) =>
        Assert.Throws<ArgumentException>(() => NewCompany(slug));

    [Fact]
    public void A_slug_is_trimmed_and_lower_cased_so_case_cannot_create_a_second_address()
    {
        Assert.Equal("acme-rentals", NewCompany("  Acme-Rentals ").Slug);
        Assert.Equal(Company.NormalizeSlug("Acme-Rentals"), Company.NormalizeSlug("acme-rentals"));
    }

    [Fact]
    public void A_name_is_required_and_bounded()
    {
        Assert.Throws<ArgumentException>(() => new Company("acme", " ", "UTC", Now));
        Assert.Throws<ArgumentException>(() => new Company("acme", new string('x', Company.NameMaxLength + 1), "UTC", Now));
        Assert.Equal("Acme", new Company("acme", "  Acme ", "UTC", Now).Name);
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("Australia/Sydney")]
    [InlineData("Europe/London")]
    [InlineData("America/Argentina/Buenos_Aires")]
    public void Known_iana_time_zones_are_accepted(string zone) => Assert.Equal(zone, Company.ValidateTimeZone(zone));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Not/AZone")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("AUS Eastern Standard Time")]
    [InlineData("Australia/Sydney; DROP TABLE")]
    [InlineData("../etc/passwd")]
    public void Unknown_or_non_iana_time_zones_are_rejected(string zone) =>
        Assert.Throws<ArgumentException>(() => Company.ValidateTimeZone(zone));

    [Fact]
    public void Approving_makes_the_company_active_and_records_when()
    {
        Company company = NewCompany();

        company.Approve(Now.AddDays(1));

        Assert.Equal(CompanyStatus.Active, company.Status);
        Assert.Equal(Now.AddDays(1), company.ApprovedAt);
    }

    [Fact]
    public void Only_a_company_awaiting_review_can_be_approved()
    {
        Company company = NewCompany();
        company.Approve(Now);

        Assert.Throws<InvalidOperationException>(() => company.Approve(Now));
    }

    [Fact]
    public void A_suspended_company_that_was_approved_can_be_reactivated()
    {
        Company company = NewCompany();
        company.Approve(Now);
        company.Suspend();

        Assert.Equal(CompanyStatus.Suspended, company.Status);

        company.Reactivate();

        Assert.Equal(CompanyStatus.Active, company.Status);
        Assert.Equal(Now, company.ApprovedAt);
    }

    [Fact]
    public void A_rejected_company_cannot_be_reactivated_only_approved()
    {
        Company company = NewCompany();
        company.Suspend(); // rejected during review

        Assert.Null(company.ApprovedAt);
        Assert.Throws<InvalidOperationException>(() => company.Reactivate());
    }

    [Fact]
    public void Suspending_twice_and_reactivating_an_active_company_are_refused()
    {
        Company company = NewCompany();
        company.Approve(Now);

        Assert.Throws<InvalidOperationException>(() => company.Reactivate());

        company.Suspend();

        Assert.Throws<InvalidOperationException>(() => company.Suspend());
    }
}
