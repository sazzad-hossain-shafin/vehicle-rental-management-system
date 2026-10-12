using VehicleRental.Domain.Entities;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Domain.Tests;

public class CompanyMembershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void New_membership_is_active_with_the_given_role_and_adder()
    {
        Guid adder = Guid.NewGuid();

        var membership = new CompanyMembership(CompanyId, UserId, CompanyRole.Staff, Now, adder);

        Assert.NotEqual(Guid.Empty, membership.Id);
        Assert.Equal(CompanyId, membership.CompanyId);
        Assert.Equal(UserId, membership.UserId);
        Assert.Equal(CompanyRole.Staff, membership.Role);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Equal(Now, membership.CreatedAt);
        Assert.Equal(adder, membership.CreatedByUserId);
    }

    [Fact]
    public void A_company_and_a_user_are_required()
    {
        Assert.Throws<ArgumentException>(() => new CompanyMembership(Guid.Empty, UserId, CompanyRole.Owner, Now));
        Assert.Throws<ArgumentException>(() => new CompanyMembership(CompanyId, Guid.Empty, CompanyRole.Owner, Now));
    }

    [Fact]
    public void An_undefined_role_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompanyMembership(CompanyId, UserId, (CompanyRole)99, Now));

    [Fact]
    public void Removing_keeps_the_row_as_history_and_cannot_be_repeated()
    {
        var membership = new CompanyMembership(CompanyId, UserId, CompanyRole.Staff, Now);

        membership.Remove();

        Assert.Equal(MembershipStatus.Removed, membership.Status);
        Assert.Throws<InvalidOperationException>(() => membership.Remove());
    }

    [Fact]
    public void A_removed_member_can_be_restored_with_a_new_role_but_an_active_one_cannot()
    {
        var membership = new CompanyMembership(CompanyId, UserId, CompanyRole.Staff, Now);

        Assert.Throws<InvalidOperationException>(() => membership.Restore(CompanyRole.Owner));

        membership.Remove();
        membership.Restore(CompanyRole.Owner);

        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Equal(CompanyRole.Owner, membership.Role);
    }
}

public class CompanyCustomerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_link_keeps_company_customer_and_a_normalised_number()
    {
        Guid company = Guid.NewGuid();
        Guid customer = Guid.NewGuid();

        var link = new CompanyCustomer(company, customer, "  web-100 ", Now);

        Assert.NotEqual(Guid.Empty, link.Id);
        Assert.Equal(company, link.CompanyId);
        Assert.Equal(customer, link.CustomerId);
        Assert.Equal("WEB-100", link.CustomerNumber);
        Assert.Equal(Now, link.CreatedAt);
    }

    [Fact]
    public void A_company_a_customer_and_a_number_are_required()
    {
        Assert.Throws<ArgumentException>(() => new CompanyCustomer(Guid.Empty, Guid.NewGuid(), "C1", Now));
        Assert.Throws<ArgumentException>(() => new CompanyCustomer(Guid.NewGuid(), Guid.Empty, "C1", Now));
        Assert.Throws<ArgumentException>(() => new CompanyCustomer(Guid.NewGuid(), Guid.NewGuid(), " ", Now));
    }

    [Fact]
    public void The_number_has_the_same_length_limit_as_a_customer_number()
    {
        string tooLong = new('X', Customer.CustomerNumberMaxLength + 1);

        Assert.Throws<ArgumentException>(() => new CompanyCustomer(Guid.NewGuid(), Guid.NewGuid(), tooLong, Now));
        Assert.Equal(
            Customer.CustomerNumberMaxLength,
            new CompanyCustomer(Guid.NewGuid(), Guid.NewGuid(), new string('X', Customer.CustomerNumberMaxLength), Now).CustomerNumber.Length);
    }
}
