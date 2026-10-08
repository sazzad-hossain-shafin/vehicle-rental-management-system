using System.Net;
using VehicleRental.Api.Tests.Support;
using VehicleRental.Application;
using VehicleRental.Application.Rentals;
using VehicleRental.Application.Reservations;
using VehicleRental.Application.Vehicles;
using VehicleRental.Domain.Enums;

namespace VehicleRental.Api.Tests;

/// <summary>
/// Reservations through the real HTTP pipeline against PostgreSQL: availability, customer self-service, the
/// rental desk, pickup, ownership and the error contract.
/// </summary>
public class ReservationApiTests : ApiTestBase
{
    private const string V1 = ApiTestExtensions.V1;

    // The service uses the machine's local date (like rentals), so the tests do too.
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public ReservationApiTests(PostgresApiFixture api) : base(api)
    {
    }

    private static string Date(int offsetDays) => Today.AddDays(offsetDays).ToString("yyyy-MM-dd");

    private static object Booking(Guid vehicleId, int start, int end) =>
        new { vehicleId, startDate = Date(start), endDate = Date(end) };

    private static Task<HttpResponseMessage> ReserveRawAsync(HttpClient client, Guid vehicleId, int start, int end) =>
        client.PostJsonAsync($"{V1}/me/reservations", Booking(vehicleId, start, end));

    private static async Task<ReservationDto> ReserveAsync(HttpClient client, Guid vehicleId, int start, int end)
    {
        var response = await ReserveRawAsync(client, vehicleId, start, end);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return await response.ReadAsync<ReservationDto>();
    }

    private static async Task<PagedResult<VehicleDto>> AvailableAsync(HttpClient client, int start, int end, string extra = "")
    {
        var response = await client.GetAsync($"{V1}/vehicles/availability?startDate={Date(start)}&endDate={Date(end)}{extra}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.ReadAsync<PagedResult<VehicleDto>>();
    }

    // ----- Availability -----

    [DatabaseFact]
    public async Task Availability_RequiresASignIn()
    {
        using var anonymous = Api.CreateAnonymousClient();

        var response = await anonymous.GetAsync($"{V1}/vehicles/availability?startDate={Date(3)}&endDate={Date(5)}");

        await response.AssertProblemAsync(HttpStatusCode.Unauthorized);
    }

    [DatabaseFact]
    public async Task Availability_ListsFreeVehicles_AndDropsReservedOnesForOverlappingDates()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var reserved = await Client.CreateVehicleAsync("AAA-111");
        var free = await Client.CreateVehicleAsync("BBB-222");
        await ReserveAsync(alice.Client, reserved.Id, 5, 8);

        var overlapping = await AvailableAsync(alice.Client, 6, 7);
        var adjacentAfter = await AvailableAsync(alice.Client, 8, 10);
        var adjacentBefore = await AvailableAsync(alice.Client, 3, 5);

        Assert.Equal(new[] { free.Id }, overlapping.Items.Select(v => v.Id));
        Assert.Equal(2, adjacentAfter.TotalCount);     // the half-open interval: free again on the end date
        Assert.Equal(2, adjacentBefore.TotalCount);    // and free up to the start date
    }

    [DatabaseFact]
    public async Task Availability_FiltersAndPages()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        await Client.CreateVehicleAsync("AAA-111", dailyRate: 40m);
        await Client.CreateVehicleAsync("BBB-222", dailyRate: 100m, type: "Van");
        await Client.CreateVehicleAsync("CCC-333", dailyRate: 100m);

        var vans = await AvailableAsync(alice.Client, 3, 5, "&vehicleType=van");
        var cheap = await AvailableAsync(alice.Client, 3, 5, "&maxDailyRate=50");
        var page = await AvailableAsync(alice.Client, 3, 5, "&page=2&pageSize=2");

        Assert.Equal("BBB-222", Assert.Single(vans.Items).RegistrationNumber);
        Assert.Equal("AAA-111", Assert.Single(cheap.Items).RegistrationNumber);
        Assert.Equal("CCC-333", Assert.Single(page.Items).RegistrationNumber);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
    }

    [DatabaseFact]
    public async Task Availability_ChecksTheDates()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");

        var missing = await alice.Client.GetAsync($"{V1}/vehicles/availability");
        var onlyStart = await alice.Client.GetAsync($"{V1}/vehicles/availability?startDate={Date(3)}");
        var malformed = await alice.Client.GetAsync($"{V1}/vehicles/availability?startDate=tomorrow&endDate={Date(5)}");
        var empty = await alice.Client.GetAsync($"{V1}/vehicles/availability?startDate={Date(3)}&endDate={Date(3)}");
        var backwards = await alice.Client.GetAsync($"{V1}/vehicles/availability?startDate={Date(5)}&endDate={Date(3)}");
        var past = await alice.Client.GetAsync($"{V1}/vehicles/availability?startDate={Date(-1)}&endDate={Date(2)}");
        var tooLong = await alice.Client.GetAsync($"{V1}/vehicles/availability?startDate={Date(1)}&endDate={Date(200)}");
        var badType = await alice.Client.GetAsync($"{V1}/vehicles/availability?startDate={Date(3)}&endDate={Date(5)}&vehicleType=boat");
        var badPage = await alice.Client.GetAsync($"{V1}/vehicles/availability?startDate={Date(3)}&endDate={Date(5)}&pageSize=1000");

        var missingBody = await missing.AssertProblemAsync(HttpStatusCode.BadRequest);
        Assert.True(missingBody.GetProperty("errors").TryGetProperty("startDate", out _));
        Assert.True(missingBody.GetProperty("errors").TryGetProperty("endDate", out _));
        await onlyStart.AssertProblemAsync(HttpStatusCode.BadRequest);
        await malformed.AssertProblemAsync(HttpStatusCode.BadRequest);
        await empty.AssertProblemAsync(HttpStatusCode.BadRequest);
        await backwards.AssertProblemAsync(HttpStatusCode.BadRequest);
        await past.AssertProblemAsync(HttpStatusCode.BadRequest);
        await tooLong.AssertProblemAsync(HttpStatusCode.BadRequest);
        await badType.AssertProblemAsync(HttpStatusCode.BadRequest);
        await badPage.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    [DatabaseFact]
    public async Task Availability_CountsAnActiveRentalAsOccupyingTheVehicle()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var vehicle = await Client.CreateVehicleAsync("AAA-111");
        var customer = await Client.CreateCustomerAsync("C9", "Walk In");
        await Client.StartRentalAsync(vehicle.Id, customer.Id, 4);   // out for the next four days

        Assert.Empty((await AvailableAsync(alice.Client, 2, 3)).Items);
        Assert.Single((await AvailableAsync(alice.Client, 4, 6)).Items);   // due back on day 4
    }

    // ----- Customer: creating -----

    [DatabaseFact]
    public async Task ACustomer_CanReserve_AndGetsAQuote()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var vehicle = await Client.CreateVehicleAsync("AAA-111", dailyRate: 100m);

        var response = await ReserveRawAsync(alice.Client, vehicle.Id, 5, 8);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var reservation = await response.ReadAsync<ReservationDto>();
        Assert.Equal($"/api/v1/me/reservations/{reservation.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(ReservationStatus.Active, reservation.Status);
        Assert.Equal(alice.User.CustomerId, reservation.CustomerId);
        Assert.Equal(vehicle.Id, reservation.VehicleId);
        Assert.Equal(Today.AddDays(5), reservation.StartDate);
        Assert.Equal(Today.AddDays(8), reservation.EndDate);
        Assert.Equal(3, reservation.BillableDays);
        Assert.Equal(300m, reservation.TotalCost);
        Assert.Equal("Normal pricing", reservation.PricingDescription);
        Assert.Null(reservation.RentalId);
        Assert.False(reservation.IsExpired);

        // A reservation does not check the vehicle out.
        var stillThere = await Client.GetAsync($"{V1}/vehicles/{vehicle.Id}");
        Assert.Equal(VehicleAvailabilityStatus.Available, (await stillThere.ReadAsync<VehicleDto>()).AvailabilityStatus);
    }

    [DatabaseFact]
    public async Task ALongBooking_GetsTheLongTermQuote()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var vehicle = await Client.CreateVehicleAsync("AAA-111", dailyRate: 100m);

        var reservation = await ReserveAsync(alice.Client, vehicle.Id, 2, 9);   // 7 days

        Assert.Equal(560m, reservation.TotalCost);
        Assert.Contains("Long-term", reservation.PricingDescription);
    }

    [DatabaseFact]
    public async Task TheCustomerIsAlwaysTheSignedInOne_WhateverTheRequestSays()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var bob = await Api.CreateCustomerClientAsync("Bob");
        var vehicle = await Client.CreateVehicleAsync("AAA-111");
        var other = await Client.CreateVehicleAsync("BBB-222");
        var third = await Client.CreateVehicleAsync("CCC-333");

        // Body fields, a query string and a header all try to book on behalf of Bob.
        var viaBody = await alice.Client.PostJsonAsync($"{V1}/me/reservations", new
        {
            vehicleId = vehicle.Id,
            startDate = Date(5),
            endDate = Date(8),
            customerId = bob.User.CustomerId,
            customerNumber = "BOB",
            userId = bob.User.Id
        });

        var viaQuery = await alice.Client.PostJsonAsync(
            $"{V1}/me/reservations?customerId={bob.User.CustomerId}&customerNumber=BOB", Booking(other.Id, 5, 8));

        var request = new HttpRequestMessage(HttpMethod.Post, $"{V1}/me/reservations")
        {
            Content = System.Net.Http.Json.JsonContent.Create(Booking(third.Id, 5, 8), options: Json.Options)
        };
        request.Headers.Add("X-Customer-Id", bob.User.CustomerId.ToString());
        var viaHeader = await alice.Client.SendAsync(request);

        foreach (var response in new[] { viaBody, viaQuery, viaHeader })
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(alice.User.CustomerId, (await response.ReadAsync<ReservationDto>()).CustomerId);
        }

        var bobs = await (await bob.Client.GetAsync($"{V1}/me/reservations")).ReadAsync<PagedResult<ReservationDto>>();
        Assert.Empty(bobs.Items);
    }

    [DatabaseFact]
    public async Task AnOverlappingBooking_Returns409ProblemDetails_AndAnAdjacentOneIsFine()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var bob = await Api.CreateCustomerClientAsync("Bob");
        var vehicle = await Client.CreateVehicleAsync("AAA-111");
        await ReserveAsync(alice.Client, vehicle.Id, 5, 8);

        var overlapping = await ReserveRawAsync(bob.Client, vehicle.Id, 7, 10);
        var body = await overlapping.AssertProblemAsync(HttpStatusCode.Conflict);
        Assert.Contains("not available", body.GetProperty("detail").GetString());
        Assert.DoesNotContain("EX_Reservations", body.GetRawText());   // no database internals

        var adjacent = await ReserveRawAsync(bob.Client, vehicle.Id, 8, 10);
        Assert.Equal(HttpStatusCode.Created, adjacent.StatusCode);
    }

    [DatabaseFact]
    public async Task ManySimultaneousBookings_OfOneVehicle_ExactlyOneSucceeds()
    {
        var vehicle = await Client.CreateVehicleAsync("AAA-111");
        var customers = new List<CustomerSession>();
        for (int i = 0; i < 6; i++)
        {
            customers.Add(await Api.CreateCustomerClientAsync($"Customer {i}"));
        }

        var responses = await Task.WhenAll(customers.Select(c => Task.Run(() => ReserveRawAsync(c.Client, vehicle.Id, 5, 8))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var all = await (await Client.GetAsync($"{V1}/reservations")).ReadAsync<PagedResult<ReservationDto>>();
        Assert.Equal(1, all.TotalCount);
    }

    [DatabaseFact]
    public async Task AnInvalidBooking_Returns400OrNotFound_WithProblemDetails()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var vehicle = await Client.CreateVehicleAsync("AAA-111");

        var past = await ReserveRawAsync(alice.Client, vehicle.Id, -2, 1);
        var empty = await ReserveRawAsync(alice.Client, vehicle.Id, 4, 4);
        var backwards = await ReserveRawAsync(alice.Client, vehicle.Id, 6, 4);
        var tooFarAhead = await ReserveRawAsync(alice.Client, vehicle.Id, 400, 402);
        var tooLong = await ReserveRawAsync(alice.Client, vehicle.Id, 1, 100);
        var unknownVehicle = await ReserveRawAsync(alice.Client, Guid.NewGuid(), 3, 5);
        var missingFields = await alice.Client.PostJsonAsync($"{V1}/me/reservations", new { vehicleId = vehicle.Id });
        var badDate = await alice.Client.PostJsonAsync($"{V1}/me/reservations",
            new { vehicleId = vehicle.Id, startDate = "next week", endDate = Date(5) });
        var badGuid = await alice.Client.PostJsonAsync($"{V1}/me/reservations",
            new { vehicleId = "not-a-guid", startDate = Date(3), endDate = Date(5) });

        await past.AssertProblemAsync(HttpStatusCode.BadRequest);
        await empty.AssertProblemAsync(HttpStatusCode.BadRequest);
        await backwards.AssertProblemAsync(HttpStatusCode.BadRequest);
        await tooFarAhead.AssertProblemAsync(HttpStatusCode.BadRequest);
        await tooLong.AssertProblemAsync(HttpStatusCode.BadRequest);
        await unknownVehicle.AssertProblemAsync(HttpStatusCode.NotFound);
        var fieldErrors = await missingFields.AssertProblemAsync(HttpStatusCode.BadRequest);
        Assert.True(fieldErrors.GetProperty("errors").TryGetProperty("startDate", out _));
        await badDate.AssertProblemAsync(HttpStatusCode.BadRequest);
        await badGuid.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    [DatabaseFact]
    public async Task ABookingStartingToday_IsAllowed()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var vehicle = await Client.CreateVehicleAsync("AAA-111");

        var reservation = await ReserveAsync(alice.Client, vehicle.Id, 0, 1);

        Assert.Equal(Today, reservation.StartDate);
        Assert.Equal(1, reservation.BillableDays);
    }

    // ----- Customer: reading -----

    [DatabaseFact]
    public async Task ACustomer_ListsOnlyTheirOwnReservations_WithPaging()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var bob = await Api.CreateCustomerClientAsync("Bob");
        var v1 = await Client.CreateVehicleAsync("AAA-111");
        var v2 = await Client.CreateVehicleAsync("BBB-222");
        var a3 = await ReserveAsync(alice.Client, v1.Id, 20, 22);
        var a1 = await ReserveAsync(alice.Client, v1.Id, 2, 4);
        var a2 = await ReserveAsync(alice.Client, v2.Id, 10, 12);
        await ReserveAsync(bob.Client, v2.Id, 3, 5);

        var first = await (await alice.Client.GetAsync($"{V1}/me/reservations?pageSize=2")).ReadAsync<PagedResult<ReservationDto>>();
        var second = await (await alice.Client.GetAsync($"{V1}/me/reservations?page=2&pageSize=2")).ReadAsync<PagedResult<ReservationDto>>();
        var beyond = await (await alice.Client.GetAsync($"{V1}/me/reservations?page=5&pageSize=2")).ReadAsync<PagedResult<ReservationDto>>();

        Assert.Equal(new[] { a1.Id, a2.Id }, first.Items.Select(r => r.Id));   // earliest start first
        Assert.Equal(new[] { a3.Id }, second.Items.Select(r => r.Id));
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Empty(beyond.Items);
        Assert.Equal(3, beyond.TotalCount);

        var badPage = await alice.Client.GetAsync($"{V1}/me/reservations?page=0");
        await badPage.AssertProblemAsync(HttpStatusCode.BadRequest);
    }

    [DatabaseFact]
    public async Task ACustomer_CanReadTheirOwnReservation_ButAnotherCustomersLooksMissing()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var bob = await Api.CreateCustomerClientAsync("Bob");
        var vehicle = await Client.CreateVehicleAsync("AAA-111");
        var mine = await ReserveAsync(alice.Client, vehicle.Id, 5, 8);

        var own = await alice.Client.GetAsync($"{V1}/me/reservations/{mine.Id}");
        var crossed = await bob.Client.GetAsync($"{V1}/me/reservations/{mine.Id}");
        var unknown = await bob.Client.GetAsync($"{V1}/me/reservations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(mine.Id, (await own.ReadAsync<ReservationDto>()).Id);

        var crossedBody = await crossed.AssertProblemAsync(HttpStatusCode.NotFound);
        var unknownBody = await unknown.AssertProblemAsync(HttpStatusCode.NotFound);
        Assert.Equal(unknownBody.GetProperty("title").GetString(), crossedBody.GetProperty("title").GetString());
        Assert.DoesNotContain("Alice", crossedBody.GetRawText());

        // The staff-only route is not a way around it.
        var viaStaffRoute = await bob.Client.GetAsync($"{V1}/reservations/{mine.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, viaStaffRoute.StatusCode);
    }

    // ----- Customer: cancelling -----

    [DatabaseFact]
    public async Task ACustomer_CanCancelTheirOwnFutureReservation_AndTheDatesBecomeFree()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var bob = await Api.CreateCustomerClientAsync("Bob");
        var vehicle = await Client.CreateVehicleAsync("AAA-111");
        var mine = await ReserveAsync(alice.Client, vehicle.Id, 5, 8);

        var cancelled = await alice.Client.PostAsync($"{V1}/me/reservations/{mine.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var body = await cancelled.ReadAsync<ReservationDto>();
        Assert.Equal(ReservationStatus.Cancelled, body.Status);
        Assert.NotNull(body.CancelledAt);

        // Cancelling again is a conflict, and the dates can be booked by someone else.
        var again = await alice.Client.PostAsync($"{V1}/me/reservations/{mine.Id}/cancel", null);
        await again.AssertProblemAsync(HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.Created, (await ReserveRawAsync(bob.Client, vehicle.Id, 5, 8)).StatusCode);
    }

    [DatabaseFact]
    public async Task ACustomer_CannotCancelAnotherCustomersReservation()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var bob = await Api.CreateCustomerClientAsync("Bob");
        var vehicle = await Client.CreateVehicleAsync("AAA-111");
        var mine = await ReserveAsync(alice.Client, vehicle.Id, 5, 8);

        var attempt = await bob.Client.PostAsync($"{V1}/me/reservations/{mine.Id}/cancel", null);
        var viaStaffRoute = await bob.Client.PostAsync($"{V1}/reservations/{mine.Id}/cancel", null);

        await attempt.AssertProblemAsync(HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.Forbidden, viaStaffRoute.StatusCode);
        var still = await (await Client.GetAsync($"{V1}/reservations/{mine.Id}")).ReadAsync<ReservationDto>();
        Assert.Equal(ReservationStatus.Active, still.Status);
    }

    [DatabaseFact]
    public async Task ACustomer_CannotCancelOnTheStartDate_ButStaffCan()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var vehicle = await Client.CreateVehicleAsync("AAA-111");
        var starting = await ReserveAsync(alice.Client, vehicle.Id, 0, 2);   // starts today

        var byCustomer = await alice.Client.PostAsync($"{V1}/me/reservations/{starting.Id}/cancel", null);
        var body = await byCustomer.AssertProblemAsync(HttpStatusCode.Conflict);
        Assert.Contains("rental desk", body.GetProperty("detail").GetString());

        var byStaff = await Client.PostAsync($"{V1}/reservations/{starting.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, byStaff.StatusCode);
        Assert.Equal(ReservationStatus.Cancelled, (await byStaff.ReadAsync<ReservationDto>()).Status);
    }

    // ----- Customers are not staff -----

    [DatabaseFact]
    public async Task ACustomer_CannotUseTheRentalDeskEndpoints()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var vehicle = await Client.CreateVehicleAsync("AAA-111");
        var reservation = await ReserveAsync(alice.Client, vehicle.Id, 0, 2);

        var forbidden = new[]
        {
            await alice.Client.GetAsync($"{V1}/reservations"),
            await alice.Client.GetAsync($"{V1}/reservations/{reservation.Id}"),
            await alice.Client.PostJsonAsync($"{V1}/reservations", new
            {
                customerId = alice.User.CustomerId, vehicleId = vehicle.Id, startDate = Date(5), endDate = Date(7)
            }),
            await alice.Client.PostAsync($"{V1}/reservations/{reservation.Id}/cancel", null),
            await alice.Client.PostAsync($"{V1}/reservations/{reservation.Id}/pickup", null),
            await alice.Client.GetAsync($"{V1}/rentals"),
            await alice.Client.PostJsonAsync($"{V1}/vehicles", new
            {
                registrationNumber = "EVIL-1", make = "A", model = "B", year = 2020, vehicleType = "Car", dailyRate = 1
            })
        };

        Assert.All(forbidden, response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));

        // And the reservation is untouched.
        var still = await (await Client.GetAsync($"{V1}/reservations/{reservation.Id}")).ReadAsync<ReservationDto>();
        Assert.Equal(ReservationStatus.Active, still.Status);
    }

    [DatabaseFact]
    public async Task Anonymous_CannotTouchReservations()
    {
        using var anonymous = Api.CreateAnonymousClient();

        var responses = new[]
        {
            await anonymous.GetAsync($"{V1}/me/reservations"),
            await anonymous.PostJsonAsync($"{V1}/me/reservations", Booking(Guid.NewGuid(), 3, 5)),
            await anonymous.GetAsync($"{V1}/reservations"),
            await anonymous.PostAsync($"{V1}/reservations/{Guid.NewGuid()}/pickup", null)
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
    }

    // ----- Staff -----

    [DatabaseFact]
    public async Task Staff_ListAndReadAllReservations_FilteredByStatus()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var bob = await Api.CreateCustomerClientAsync("Bob");
        var v1 = await Client.CreateVehicleAsync("AAA-111");
        var v2 = await Client.CreateVehicleAsync("BBB-222");
        var a = await ReserveAsync(alice.Client, v1.Id, 5, 8);
        var b = await ReserveAsync(bob.Client, v2.Id, 3, 5);
        await alice.Client.PostAsync($"{V1}/me/reservations/{a.Id}/cancel", null);

        var all = await (await Client.GetAsync($"{V1}/reservations")).ReadAsync<PagedResult<ReservationDto>>();
        var active = await (await Client.GetAsync($"{V1}/reservations?status=active")).ReadAsync<PagedResult<ReservationDto>>();
        var cancelled = await (await Client.GetAsync($"{V1}/reservations?status=Cancelled")).ReadAsync<PagedResult<ReservationDto>>();
        var one = await Client.GetAsync($"{V1}/reservations/{b.Id}");

        Assert.Equal(new[] { b.Id, a.Id }, all.Items.Select(r => r.Id));   // earliest start first
        Assert.Equal(b.Id, Assert.Single(active.Items).Id);
        Assert.Equal(a.Id, Assert.Single(cancelled.Items).Id);
        Assert.Equal("Bob", (await one.ReadAsync<ReservationDto>()).CustomerName);

        await (await Client.GetAsync($"{V1}/reservations?status=nonsense")).AssertProblemAsync(HttpStatusCode.BadRequest);
        await (await Client.GetAsync($"{V1}/reservations/{Guid.NewGuid()}")).AssertProblemAsync(HttpStatusCode.NotFound);
    }

    [DatabaseFact]
    public async Task Staff_CanBookAtTheDesk_ForAnExistingCustomer_WithThePromotion()
    {
        var customer = await Client.CreateCustomerAsync("C9", "Walk In");
        var vehicle = await Client.CreateVehicleAsync("AAA-111", dailyRate: 100m);

        var response = await Client.PostJsonAsync($"{V1}/reservations", new
        {
            customerId = customer.Id,
            vehicleId = vehicle.Id,
            startDate = Date(5),
            endDate = Date(8),
            promotionalDiscountRequested = true
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var reservation = await response.ReadAsync<ReservationDto>();
        Assert.Equal($"/api/v1/reservations/{reservation.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(customer.Id, reservation.CustomerId);
        Assert.Equal(270m, reservation.TotalCost);

        var unknownCustomer = await Client.PostJsonAsync($"{V1}/reservations", new
        {
            customerId = Guid.NewGuid(),
            vehicleId = vehicle.Id,
            startDate = Date(15),
            endDate = Date(17)
        });
        await unknownCustomer.AssertProblemAsync(HttpStatusCode.NotFound);
    }

    [DatabaseFact]
    public async Task ACustomerBooking_CannotAskForThePromotion()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var vehicle = await Client.CreateVehicleAsync("AAA-111", dailyRate: 100m);

        var response = await alice.Client.PostJsonAsync($"{V1}/me/reservations", new
        {
            vehicleId = vehicle.Id,
            startDate = Date(5),
            endDate = Date(8),
            promotionalDiscountRequested = true
        });

        Assert.Equal(300m, (await response.ReadAsync<ReservationDto>()).TotalCost);
    }

    // ----- Pickup -----

    [DatabaseFact]
    public async Task Pickup_StartsTheRental_FulfilsTheReservation_AndKeepsTheQuote()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var vehicle = await Client.CreateVehicleAsync("AAA-111", dailyRate: 100m);
        var reservation = await ReserveAsync(alice.Client, vehicle.Id, 0, 3);

        var response = await Client.PostAsync($"{V1}/reservations/{reservation.Id}/pickup", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.ReadAsync<PickupResultDto>();
        Assert.Equal(ReservationStatus.Fulfilled, result.Reservation.Status);
        Assert.Equal(result.Rental.Id, result.Reservation.RentalId);
        Assert.Equal(RentalStatus.Active, result.Rental.Status);
        Assert.Equal(alice.User.CustomerId, result.Rental.CustomerId);
        Assert.Equal(vehicle.Id, result.Rental.VehicleId);
        Assert.Equal(Today, result.Rental.StartDate);
        Assert.Equal(Today.AddDays(3), result.Rental.ExpectedReturnDate);
        Assert.Equal(reservation.TotalCost, result.Rental.TotalCost);
        Assert.Equal(reservation.PricingDescription, result.Rental.PricingDescription);

        // The vehicle is now physically rented, and the customer sees the rental as their own.
        var vehicleNow = await (await Client.GetAsync($"{V1}/vehicles/{vehicle.Id}")).ReadAsync<VehicleDto>();
        Assert.Equal(VehicleAvailabilityStatus.Rented, vehicleNow.AvailabilityStatus);
        var rentals = await (await alice.Client.GetAsync($"{V1}/me/rentals")).ReadAsync<PagedResult<RentalDto>>();
        Assert.Equal(result.Rental.Id, Assert.Single(rentals.Items).Id);

        // A pickup can be done once, and the rental can be returned as usual.
        var second = await Client.PostAsync($"{V1}/reservations/{reservation.Id}/pickup", null);
        await second.AssertProblemAsync(HttpStatusCode.Conflict);
        Assert.Equal(1, (await (await Client.GetAsync($"{V1}/rentals")).ReadAsync<PagedResult<RentalDto>>()).TotalCount);

        var returned = await Client.PostAsync($"{V1}/rentals/{result.Rental.Id}/return", null);
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
    }

    [DatabaseFact]
    public async Task Pickup_IsRefused_BeforeTheStartDate_AndForCancelledReservations()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var v1 = await Client.CreateVehicleAsync("AAA-111");
        var v2 = await Client.CreateVehicleAsync("BBB-222");
        var future = await ReserveAsync(alice.Client, v1.Id, 3, 5);
        var cancelled = await ReserveAsync(alice.Client, v2.Id, 3, 5);
        await alice.Client.PostAsync($"{V1}/me/reservations/{cancelled.Id}/cancel", null);

        var tooEarly = await Client.PostAsync($"{V1}/reservations/{future.Id}/pickup", null);
        var afterCancel = await Client.PostAsync($"{V1}/reservations/{cancelled.Id}/pickup", null);
        var unknown = await Client.PostAsync($"{V1}/reservations/{Guid.NewGuid()}/pickup", null);

        await tooEarly.AssertProblemAsync(HttpStatusCode.Conflict);
        await afterCancel.AssertProblemAsync(HttpStatusCode.Conflict);
        await unknown.AssertProblemAsync(HttpStatusCode.NotFound);
        Assert.Equal(0, (await (await Client.GetAsync($"{V1}/rentals")).ReadAsync<PagedResult<RentalDto>>()).TotalCount);
    }

    [DatabaseFact]
    public async Task Pickup_WhileTheVehicleIsStillRentedOut_IsAConflict()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var vehicle = await Client.CreateVehicleAsync("AAA-111");
        var walkIn = await Client.CreateCustomerAsync("C9", "Walk In");
        var reservation = await ReserveAsync(alice.Client, vehicle.Id, 2, 5);

        // A walk-in takes the vehicle for one day, which ends before the reservation starts.
        await Client.StartRentalAsync(vehicle.Id, walkIn.Id, 1);

        var response = await Client.PostAsync($"{V1}/reservations/{reservation.Id}/pickup", null);

        await response.AssertProblemAsync(HttpStatusCode.Conflict);
    }

    [DatabaseFact]
    public async Task ConcurrentPickups_ExactlyOneRentalIsCreated()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var vehicle = await Client.CreateVehicleAsync("AAA-111");
        var reservation = await ReserveAsync(alice.Client, vehicle.Id, 0, 3);
        var staff = new List<HttpClient> { Client };
        for (int i = 0; i < 3; i++)
        {
            staff.Add(await Api.CreateStaffClientAsync());
        }

        var responses = await Task.WhenAll(staff.Select(c =>
            Task.Run(() => c.PostAsync($"{V1}/reservations/{reservation.Id}/pickup", null))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, (await (await Client.GetAsync($"{V1}/rentals")).ReadAsync<PagedResult<RentalDto>>()).TotalCount);
    }

    // ----- Reservations and walk-in rentals -----

    [DatabaseFact]
    public async Task AWalkInRental_CannotTakeAVehicleReservedForItsDays()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var vehicle = await Client.CreateVehicleAsync("AAA-111");
        var walkIn = await Client.CreateCustomerAsync("C9", "Walk In");
        await ReserveAsync(alice.Client, vehicle.Id, 2, 5);

        var overlapping = await Client.StartRentalRawAsync(vehicle.Id, walkIn.Id, days: 4);
        var body = await overlapping.AssertProblemAsync(HttpStatusCode.Conflict);
        Assert.Contains("reserved", body.GetProperty("detail").GetString());

        var shortRental = await Client.StartRentalRawAsync(vehicle.Id, walkIn.Id, days: 2);   // ends the day the reservation starts
        Assert.Equal(HttpStatusCode.Created, shortRental.StatusCode);
    }

    [DatabaseFact]
    public async Task ACancelledReservation_NoLongerBlocksAWalkInRental()
    {
        var alice = await Api.CreateCustomerClientAsync("Alice");
        var vehicle = await Client.CreateVehicleAsync("AAA-111");
        var walkIn = await Client.CreateCustomerAsync("C9", "Walk In");
        var reservation = await ReserveAsync(alice.Client, vehicle.Id, 1, 4);
        await alice.Client.PostAsync($"{V1}/me/reservations/{reservation.Id}/cancel", null);

        var rental = await Client.StartRentalRawAsync(vehicle.Id, walkIn.Id, days: 3);

        Assert.Equal(HttpStatusCode.Created, rental.StatusCode);
    }

    // ----- Several API instances against one database -----

    [DatabaseFact]
    public async Task WalkInRentalsAndReservations_HandledByDifferentApiInstances_NeverDoubleBookAVehicle()
    {
        // A second, independent API host (its own services, caches and connection pool) on the same database. They
        // share nothing in memory, so only the database can keep them consistent.
        await using var second = new ApiFactory(Api.ConnectionString, signingKey: Api.Factory.SigningKey);

        HttpClient OnSecondHost(HttpClient signedIn)
        {
            HttpClient client = second.CreateClient();
            client.DefaultRequestHeaders.Authorization = signedIn.DefaultRequestHeaders.Authorization;

            return client;
        }

        var staffFirst = Client;
        var staffSecond = OnSecondHost(await Api.CreateStaffClientAsync());
        var walkIn = await Client.CreateCustomerAsync("C9", "Walk In");
        var customers = new[]
        {
            await Api.CreateCustomerClientAsync("One"),
            await Api.CreateCustomerClientAsync("Two"),
            await Api.CreateCustomerClientAsync("Three")
        };
        var onSecond = new[] { OnSecondHost(customers[1].Client) };

        for (int attempt = 0; attempt < 6; attempt++)
        {
            var vehicle = await Client.CreateVehicleAsync($"RACE-{attempt}");

            var requests = new List<Task<HttpResponseMessage>>
            {
                // Rentals (today to day 3) and reservations (day 1 to day 4) all overlap each other.
                Task.Run(() => (attempt % 2 == 0 ? staffFirst : staffSecond).StartRentalRawAsync(vehicle.Id, walkIn.Id, days: 3)),
                Task.Run(() => ReserveRawAsync(customers[0].Client, vehicle.Id, 1, 4)),
                Task.Run(() => ReserveRawAsync(onSecond[0], vehicle.Id, 1, 4)),
                Task.Run(() => ReserveRawAsync(customers[2].Client, vehicle.Id, 1, 4))
            };

            var responses = await Task.WhenAll(requests);

            Assert.True(
                responses.Count(r => r.StatusCode == HttpStatusCode.Created) == 1,
                $"Attempt {attempt}: expected exactly one booking, got {string.Join(", ", responses.Select(r => (int)r.StatusCode))}.");
            Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

            // One claim in the database: either a rental or a reservation for this vehicle, never both.
            var rentals = await (await Client.GetAsync($"{V1}/rentals")).ReadAsync<PagedResult<RentalDto>>();
            var reservations = await (await Client.GetAsync($"{V1}/reservations")).ReadAsync<PagedResult<ReservationDto>>();
            int claims = rentals.Items.Count(r => r.VehicleId == vehicle.Id)
                         + reservations.Items.Count(r => r.VehicleId == vehicle.Id && r.Status == ReservationStatus.Active);
            Assert.Equal(1, claims);
        }
    }
}

