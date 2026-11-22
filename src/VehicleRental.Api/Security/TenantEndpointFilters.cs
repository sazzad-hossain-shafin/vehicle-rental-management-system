using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using VehicleRental.Application.Tenancy;
using VehicleRental.Domain.Enums;
using VehicleRental.Infrastructure.Tenancy;

namespace VehicleRental.Api.Security;

/// <summary>
/// Endpoint filters that will resolve a request to a company in later phases. In Phase 14A-4 they are built and tested
/// but NOT attached to any production route. The company is never read from a body, a query string, a custom header or
/// the token: it comes from the route, and for management it is verified against an active membership in the database
/// on every request.
///
/// Typical use (later): <c>.RequireCompanyMember(CompanyRole.Staff).InTenantTransaction()</c>. The order matters: the
/// tenant is bound first, then the transaction starts and applies the tenant setting, then the handler runs.
/// </summary>
internal static class TenantEndpointFilters
{
    /// <summary>
    /// Public storefront: the route value is a company slug. Unknown, malformed, reserved, pending and suspended companies
    /// all answer the same 404, so the response reveals nothing about which companies exist.
    /// </summary>
    public static RouteHandlerBuilder RequirePublicCompany(this RouteHandlerBuilder builder, string routeValue = "slug") =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            HttpContext http = context.HttpContext;

            if (!http.RequestServices.GetRequiredService<IOptions<TenancyOptions>>().Value.Enabled)
            {
                return NotFound();
            }

            string? slug = http.Request.RouteValues[routeValue] as string;
            CompanySummary? company = await http.RequestServices
                .GetRequiredService<ICompanyDirectory>()
                .FindActiveBySlugAsync(slug, http.RequestAborted);

            if (company is null)
            {
                return NotFound();
            }

            http.RequestServices.GetRequiredService<ITenantBinder>().Bind(company.Id, TenantSource.PublicSlug);

            return await next(context);
        });

    /// <summary>
    /// Management: the route value is a company ID, which is only a CLAIM until it is verified. The signed-in user must
    /// have an ACTIVE membership in that company, with at least <paramref name="minimum"/> role, and the company must not
    /// be suspended. A company that does not exist and a company the user is not a member of answer the same 404.
    /// </summary>
    public static RouteHandlerBuilder RequireCompanyMember(
        this RouteHandlerBuilder builder,
        CompanyRole minimum = CompanyRole.Staff,
        string routeValue = "companyId") =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            HttpContext http = context.HttpContext;

            if (!http.RequestServices.GetRequiredService<IOptions<TenancyOptions>>().Value.Enabled)
            {
                return NotFound();
            }

            Guid? userId = http.User.GetUserId();

            if (userId is null || !Guid.TryParse(http.Request.RouteValues[routeValue] as string, out Guid companyId))
            {
                return NotFound();
            }

            CompanySummary? company = await http.RequestServices
                .GetRequiredService<ICompanyDirectory>()
                .FindManageableAsync(companyId, http.RequestAborted);

            CompanyRole? role = company is null
                ? null
                : await http.RequestServices.GetRequiredService<IMembershipDirectory>()
                    .FindActiveRoleAsync(company.Id, userId.Value, http.RequestAborted);

            if (company is null || role is null)
            {
                return NotFound();
            }

            // Owner outranks Staff. A member who lacks the role is refused; they already know the company exists.
            if (!Satisfies(role.Value, minimum))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "The request was not accepted.",
                    detail: "Your role in this company does not allow this.");
            }

            http.RequestServices.GetRequiredService<ITenantBinder>().Bind(company.Id, TenantSource.Membership);

            return await next(context);
        });

    /// <summary>
    /// Runs the handler inside the tenant transaction: begin (tenant already bound, setting applied and confirmed) ->
    /// handler -> commit, or roll back when the handler throws or answers 400 or above. The unit of work joins it.
    /// </summary>
    public static RouteHandlerBuilder InTenantTransaction(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            HttpContext http = context.HttpContext;
            ITenantTransaction transaction = http.RequestServices.GetRequiredService<ITenantTransaction>();

            await transaction.BeginAsync(http.RequestAborted);

            try
            {
                object? result = await next(context);

                if (result is IStatusCodeHttpResult { StatusCode: >= 400 })
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                else
                {
                    await transaction.CommitAsync(http.RequestAborted);
                }

                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        });

    private static bool Satisfies(CompanyRole actual, CompanyRole required) =>
        actual == CompanyRole.Owner || actual == required;

    private static object NotFound() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found.");
}
