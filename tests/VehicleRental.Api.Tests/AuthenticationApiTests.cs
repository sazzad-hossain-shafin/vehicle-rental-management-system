using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using VehicleRental.Api.Tests.Support;
using VehicleRental.Application;
using VehicleRental.Application.Accounts;
using VehicleRental.Application.Customers;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Security;
using VehicleRental.Infrastructure.Identity;
using VehicleRental.Infrastructure.Persistence;

namespace VehicleRental.Api.Tests;

/// <summary>Sign-in, registration, tokens and account security, against the real database.</summary>
public class AuthenticationApiTests : ApiTestBase
{
    public AuthenticationApiTests(PostgresApiFixture api) : base(api)
    {
    }

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";

    private HttpClient Anonymous() => Api.CreateAnonymousClient();

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        await client.PostJsonAsync("/api/v1/auth/login", new { email, password });

    /// <summary>The login failure response with the per-request trace ID removed, so two can be compared.</summary>
    private static async Task<string> FailureShapeAsync(HttpResponseMessage response)
    {
        var body = await response.ReadJsonAsync();

        return $"{(int)response.StatusCode}|{response.Content.Headers.ContentType?.MediaType}|" +
               $"{body.GetProperty("title").GetString()}|{body.GetProperty("detail").GetString()}|{body.GetProperty("type").GetString()}";
    }

    // ----- Login -----

    [DatabaseFact]
    public async Task Login_WithValidCredentials_ReturnsABearerTokenAndTheUser()
    {
        using var admin = await Api.CreateAdminClientAsync();
        string email = NewEmail();
        string password = ApiFactory.NewPassword();
        (await admin.PostJsonAsync("/api/v1/admin/staff", new { email, password })).EnsureSuccessStatusCode();

        using var anonymous = Anonymous();
        var response = await LoginAsync(anonymous, email, password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("Bearer", body.GetProperty("tokenType").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("accessToken").GetString()));
        Assert.True(body.GetProperty("expiresAtUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(25));
        Assert.Equal(email, body.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal(new[] { Roles.Staff }, body.GetProperty("user").GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [DatabaseFact]
    public async Task Login_Email_IsCaseInsensitive()
    {
        string password = ApiFactory.NewPassword();
        using var admin = await Api.CreateAdminClientAsync();
        string email = NewEmail();
        (await admin.PostJsonAsync("/api/v1/admin/staff", new { email, password })).EnsureSuccessStatusCode();

        using var anonymous = Anonymous();
        var response = await LoginAsync(anonymous, email.ToUpperInvariant(), password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [DatabaseFact]
    public async Task Token_CarriesOnlyTheExpectedNonSensitiveClaims()
    {
        var customer = await Api.CreateCustomerClientAsync();
        using var anonymous = Anonymous();
        var login = await (await LoginAsync(anonymous, customer.Email, customer.Password)).ReadJsonAsync();

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(login.GetProperty("accessToken").GetString()!);

        var types = jwt.Claims.Select(c => c.Type).ToHashSet();
        Assert.Subset(new HashSet<string> { "sub", "jti", "role", "customer_id", "iss", "aud", "iat", "nbf", "exp" }, types);
        Assert.Equal(customer.User.Id.ToString(), jwt.Subject);
        Assert.Equal(customer.User.CustomerId.ToString(), jwt.Claims.Single(c => c.Type == "customer_id").Value);
        Assert.DoesNotContain(customer.Email, jwt.EncodedPayload);
        Assert.DoesNotContain(customer.Password, jwt.EncodedPayload);
        Assert.InRange((jwt.ValidTo - jwt.ValidFrom).TotalMinutes, 29.9, 30.1);
    }

    [DatabaseFact]
    public async Task LoginFailures_LookIdentical_ForWrongPasswordUnknownEmailAndShortcuts()
    {
        var customer = await Api.CreateCustomerClientAsync();
        using var anonymous = Anonymous();

        var wrongPassword = await LoginAsync(anonymous, customer.Email, ApiFactory.NewPassword());
        var unknownEmail = await LoginAsync(anonymous, NewEmail(), ApiFactory.NewPassword());
        var emptyLooking = await LoginAsync(anonymous, customer.Email, "x");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(await FailureShapeAsync(wrongPassword), await FailureShapeAsync(unknownEmail));
        Assert.Equal(await FailureShapeAsync(wrongPassword), await FailureShapeAsync(emptyLooking));
    }

    [DatabaseFact]
    public async Task ALoginFailure_IsProblemDetails_WithAGenericMessage()
    {
        using var anonymous = Anonymous();

        var response = await LoginAsync(anonymous, NewEmail(), ApiFactory.NewPassword());

        var problem = await response.AssertProblemAsync(HttpStatusCode.Unauthorized);
        Assert.Equal("The email or password is incorrect.", problem.GetProperty("detail").GetString());
    }

    [DatabaseFact]
    public async Task AfterRepeatedFailures_TheAccountIsLocked_AndTheLockedResponseLooksLikeAnyOtherFailure()
    {
        var customer = await Api.CreateCustomerClientAsync();
        using var anonymous = Anonymous();

        HttpResponseMessage? last = null;
        for (int i = 0; i < 5; i++)
        {
            last = await LoginAsync(anonymous, customer.Email, ApiFactory.NewPassword());
            Assert.Equal(HttpStatusCode.Unauthorized, last.StatusCode);
        }

        var correctButLocked = await LoginAsync(anonymous, customer.Email, customer.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, correctButLocked.StatusCode);
        Assert.Equal(await FailureShapeAsync(last!), await FailureShapeAsync(correctButLocked));
    }

    [DatabaseFact]
    public async Task LockingOneAccount_DoesNotAffectAnother()
    {
        var victim = await Api.CreateCustomerClientAsync();
        var bystander = await Api.CreateCustomerClientAsync();
        using var anonymous = Anonymous();

        for (int i = 0; i < 6; i++)
        {
            await LoginAsync(anonymous, victim.Email, ApiFactory.NewPassword());
        }

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(anonymous, bystander.Email, bystander.Password)).StatusCode);
    }

    [DatabaseTheory]
    [InlineData("{}")]
    [InlineData("{\"email\":\"a@example.test\"}")]
    [InlineData("{\"password\":\"x\"}")]
    public async Task Login_WithMissingFields_Is400ProblemDetails(string body)
    {
        using var anonymous = Anonymous();

        var response = await anonymous.PostAsync("/api/v1/auth/login",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    // ----- Registration -----

    [DatabaseFact]
    public async Task Register_CreatesACustomerAccountLinkedToANewCustomer()
    {
        string email = NewEmail();
        string password = ApiFactory.NewPassword();
        using var anonymous = Anonymous();

        var response = await anonymous.PostJsonAsync("/api/v1/auth/register", new { email, password, name = "Alice Example" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.ReadAsync<UserDto>();
        Assert.Equal(email, user.Email);
        Assert.Equal(new[] { Roles.Customer }, user.Roles);
        Assert.NotNull(user.CustomerId);

        // The customer record exists and has the registered name; staff can see it.
        var customer = await (await Client.GetAsync($"/api/v1/customers/{user.CustomerId}")).ReadAsync<CustomerDto>();
        Assert.Equal("Alice Example", customer.Name);
        Assert.Equal(user.CustomerNumber, customer.CustomerNumber);
    }

    [DatabaseFact]
    public async Task Register_ThenSignIn_ThenTheCustomerCanSeeTheirOwnProfile()
    {
        var customer = await Api.CreateCustomerClientAsync("Dana Example");

        var profile = await (await customer.Client.GetAsync("/api/v1/me/customer")).ReadAsync<CustomerDto>();

        Assert.Equal("Dana Example", profile.Name);
        Assert.Equal(customer.User.CustomerId, profile.Id);
    }

    [DatabaseFact]
    public async Task Register_NeverEchoesThePassword_OrAnySecurityField()
    {
        string password = ApiFactory.NewPassword();
        using var anonymous = Anonymous();

        var response = await anonymous.PostJsonAsync("/api/v1/auth/register", new { email = NewEmail(), password, name = "Alice" });

        string text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(password, text);
        Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", text, StringComparison.OrdinalIgnoreCase);
    }

    [DatabaseFact]
    public async Task Register_WithAnEmailThatIsAlreadyUsed_Is409_RegardlessOfCase()
    {
        string email = NewEmail();
        using var anonymous = Anonymous();
        await anonymous.PostJsonAsync("/api/v1/auth/register", new { email, password = ApiFactory.NewPassword(), name = "First" });

        var response = await anonymous.PostJsonAsync("/api/v1/auth/register",
            new { email = email.ToUpperInvariant(), password = ApiFactory.NewPassword(), name = "Second" });

        await response.AssertProblemAsync(HttpStatusCode.Conflict);
    }

    [DatabaseTheory]
    [InlineData("short1A")]                     // too short
    [InlineData("alllowercase123")]             // no upper-case
    [InlineData("ALLUPPERCASE123")]             // no lower-case
    [InlineData("NoDigitsAtAllHere")]           // no digit
    public async Task Register_WithAWeakPassword_Is400_AndCreatesNothing(string weakPassword)
    {
        using var anonymous = Anonymous();
        int customersBefore = await CountAsync(c => c.Customers.CountAsync());

        var response = await anonymous.PostJsonAsync("/api/v1/auth/register",
            new { email = NewEmail(), password = weakPassword, name = "Alice" });

        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
        Assert.Equal(customersBefore, await CountAsync(c => c.Customers.CountAsync()));
        Assert.Equal(0, await CountAsync(c => c.Users.CountAsync(u => u.CustomerId != null)));
    }

    [DatabaseTheory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task Register_WithABadEmail_Is400(string email)
    {
        using var anonymous = Anonymous();

        var response = await anonymous.PostJsonAsync("/api/v1/auth/register",
            new { email, password = ApiFactory.NewPassword(), name = "Alice" });

        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    // ----- Roles cannot be chosen -----

    [DatabaseFact]
    public async Task Register_IgnoresAnyRoleOrCustomerLinkSentInTheBody()
    {
        string email = NewEmail();
        string password = ApiFactory.NewPassword();
        using var anonymous = Anonymous();
        var someoneElsesCustomer = await Client.CreateCustomerAsync("C-VICTIM", "Victim");

        var response = await anonymous.PostJsonAsync("/api/v1/auth/register", new
        {
            email,
            password,
            name = "Mallory",
            role = "Admin",
            roles = new[] { "Admin", "Staff" },
            isAdmin = true,
            customerId = someoneElsesCustomer.Id
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.ReadAsync<UserDto>();
        Assert.Equal(new[] { Roles.Customer }, user.Roles);
        Assert.NotEqual(someoneElsesCustomer.Id, user.CustomerId);

        // And the token that results has only the Customer role.
        var login = await (await LoginAsync(anonymous, email, password)).ReadJsonAsync();
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(login.GetProperty("accessToken").GetString()!);
        Assert.Equal(new[] { Roles.Customer }, jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value));
    }

    [DatabaseFact]
    public async Task ACustomer_CannotCreateStaff_OrAdminAccounts()
    {
        var customer = await Api.CreateCustomerClientAsync();

        var response = await customer.Client.PostJsonAsync("/api/v1/admin/staff",
            new { email = NewEmail(), password = ApiFactory.NewPassword(), role = "Admin" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DatabaseFact]
    public async Task Staff_CannotCreateStaffAccounts_OnlyAdminsCan()
    {
        var response = await Client.PostJsonAsync("/api/v1/admin/staff",
            new { email = NewEmail(), password = ApiFactory.NewPassword() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DatabaseFact]
    public async Task AnAdmin_CanCreateStaff_AndTheNewStaffMemberCanWorkTheDesk_ButNotCreateMoreStaff()
    {
        using var admin = await Api.CreateAdminClientAsync();
        string email = NewEmail();
        string password = ApiFactory.NewPassword();

        var created = await admin.PostJsonAsync("/api/v1/admin/staff", new { email, password, role = "Admin" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = await created.ReadAsync<UserDto>();
        Assert.Equal(new[] { Roles.Staff }, user.Roles); // the role is fixed, whatever the body said
        Assert.Null(user.CustomerId);

        using var staff = await Api.SignInAsync(email, password);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/v1/rentals")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PostJsonAsync("/api/v1/admin/staff", new { email = NewEmail(), password = ApiFactory.NewPassword() })).StatusCode);
    }

    [DatabaseFact]
    public async Task AnAdmin_CreatingAStaffAccountWithAWeakPassword_Is400()
    {
        using var admin = await Api.CreateAdminClientAsync();

        var response = await admin.PostJsonAsync("/api/v1/admin/staff", new { email = NewEmail(), password = "weak" });

        await response.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    // ----- Stored data and responses -----

    [DatabaseFact]
    public async Task PasswordsAreStoredHashed_NeverInPlainText()
    {
        var customer = await Api.CreateCustomerClientAsync();

        string? hash = await QueryAsync(c => c.Users.Where(u => u.Email == customer.Email).Select(u => u.PasswordHash).SingleAsync());

        Assert.NotNull(hash);
        Assert.NotEqual(customer.Password, hash);
        Assert.DoesNotContain(customer.Password, hash);
        Assert.StartsWith("AQAAAA", hash);
    }

    [DatabaseFact]
    public async Task NoResponse_ContainsAPasswordHash_OrTheSigningKey()
    {
        var customer = await Api.CreateCustomerClientAsync();
        using var anonymous = Anonymous();
        var login = await LoginAsync(anonymous, customer.Email, customer.Password);

        var responses = new List<string>
        {
            await login.Content.ReadAsStringAsync(),
            await (await customer.Client.GetAsync("/api/v1/me")).Content.ReadAsStringAsync(),
            await (await customer.Client.GetAsync("/api/v1/me/customer")).Content.ReadAsStringAsync(),
            await (await Client.GetAsync("/api/v1/customers/by-number/" + customer.User.CustomerNumber)).Content.ReadAsStringAsync()
        };

        foreach (string text in responses)
        {
            Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("securityStamp", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("AQAAAA", text);                 // the hash format's prefix
            Assert.DoesNotContain(Api.Factory.SigningKey, text);
            Assert.DoesNotContain(customer.Password, text);
        }
    }

    // ----- /me -----

    [DatabaseFact]
    public async Task Me_DescribesTheSignedInAccount_WithOnlySafeFields()
    {
        var customer = await Api.CreateCustomerClientAsync();

        var body = await (await customer.Client.GetAsync("/api/v1/me")).ReadJsonAsync();

        Assert.Equal(
            new[] { "customerId", "customerNumber", "email", "id", "roles" },
            body.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(customer.Email, body.GetProperty("email").GetString());
    }

    [DatabaseFact]
    public async Task Me_ForAStaffMember_HasNoCustomer()
    {
        var body = await (await Client.GetAsync("/api/v1/me")).ReadAsync<UserDto>();

        Assert.Equal(new[] { Roles.Staff }, body.Roles);
        Assert.Null(body.CustomerId);
        Assert.Null(body.CustomerNumber);
    }

    [DatabaseFact]
    public async Task ATokenForADeletedAccount_NoLongerWorksForMe()
    {
        var customer = await Api.CreateCustomerClientAsync();

        using (var scope = Api.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.DeleteAsync((await users.FindByIdAsync(customer.User.Id.ToString()))!)).Succeeded);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await customer.Client.GetAsync("/api/v1/me")).StatusCode);
    }

    // ----- Existing data -----

    [DatabaseFact]
    public async Task CustomersCreatedAtTheDesk_NeedNoLogin_AndStillWorkWithRentals()
    {
        var vehicle = await Client.CreateVehicleAsync("ABC-123");
        var walkIn = await Client.CreateCustomerAsync("C1", "Walk-in");

        var rental = await Client.StartRentalAsync(vehicle.Id, walkIn.Id);

        Assert.Equal(walkIn.Id, rental.CustomerId);
        Assert.Equal(0, await CountAsync(c => c.Users.CountAsync(u => u.CustomerId == walkIn.Id)));
    }

    // ----- helpers -----

    private async Task<int> CountAsync(Func<VehicleRentalDbContext, Task<int>> query) => await QueryAsync(query);

    private async Task<T> QueryAsync<T>(Func<VehicleRentalDbContext, Task<T>> query)
    {
        await using var context = new VehicleRentalDbContext(VehicleRentalDbContextOptions.Create(Api.ConnectionString));

        return await query(context);
    }
}
