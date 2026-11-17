using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Tezgah.Core.Modules;
using Tezgah.Core.Persistence;
using Tezgah.Core.Results;
using Tezgah.Core.Web;
using Tezgah.Platform.Auth;
using Tezgah.Platform.Domain;
using Tezgah.Platform.Persistence;

namespace Tezgah.Platform.Admin;

/// <summary>What the platform operator does: open companies and decide which modules each one gets.</summary>
internal static class AdminEndpoints
{
    public sealed record CatalogModule(string Key, string Name, string Description, IReadOnlyList<string> DependsOn);

    public sealed record CompanyResponse(
        Guid Id, string Name, string Sector, DateTimeOffset CreatedAt, IReadOnlyList<string> Modules, string? OwnerEmail);

    public sealed record CreateCompanyRequest(
        string? Name, string? Sector, string? OwnerFullName, string? OwnerEmail, IReadOnlyList<string>? Modules);

    /// <param name="OwnerTemporaryPassword">
    /// Shown once, for the operator to pass on. It signs in only to choose a new password: the
    /// operator never knows the password the owner actually uses.
    /// </param>
    public sealed record CreatedCompany(CompanyResponse Company, string OwnerTemporaryPassword);

    public sealed record SetModulesRequest(IReadOnlyList<string>? Modules);

    public static void Map(IEndpointRouteBuilder api)
    {
        var admin = api.MapGroup("/admin").RequireAuthorization(Policies.PlatformAdmin).WithTags("Platform administration");
        admin.MapGet("/modules", ListModules);
        admin.MapGet("/companies", ListCompaniesAsync);
        admin.MapPost("/companies", CreateCompanyAsync);
        admin.MapPut("/companies/{companyId:guid}/modules", SetModulesAsync);
    }

    private static IReadOnlyList<CatalogModule> ListModules(ModuleCatalog catalog) =>
        [.. catalog.Modules.Select(module =>
            new CatalogModule(module.Info.Key, module.Info.Name, module.Info.Description, module.Info.DependsOn))];

    private static async Task<IReadOnlyList<CompanyResponse>> ListCompaniesAsync(PlatformDbContext db, CancellationToken ct)
    {
        var tenants = await db.Tenants.AsNoTracking().OrderBy(tenant => tenant.Name).ToListAsync(ct);
        var owners = await db.Users.AsNoTracking()
            .Where(user => user.Role == Roles.Owner && user.TenantId != null)
            .ToDictionaryAsync(user => user.TenantId!.Value, user => user.Email, ct);

        return tenants.ConvertAll(tenant => ToResponse(tenant, owners.GetValueOrDefault(tenant.Id)));
    }

    private static async Task<IResult> CreateCompanyAsync(
        CreateCompanyRequest request, PlatformDbContext db, ModuleCatalog catalog, IPasswordHasher<User> hasher,
        TimeProvider time, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Sector)
            || string.IsNullOrWhiteSpace(request.OwnerFullName) || string.IsNullOrWhiteSpace(request.OwnerEmail)
            || !request.OwnerEmail.Contains('@'))
        {
            return Error.Validation("invalid_company", "Company name, sector, owner name and a valid owner e-mail are required.").ToProblem();
        }

        var modules = (request.Modules ?? []).Distinct().ToList();
        if (catalog.Validate(modules) is { } problem)
        {
            return Error.Validation("invalid_module_selection", problem).ToProblem();
        }

        var now = time.GetUtcNow();
        var tenant = Tenant.Create(request.Name, request.Sector, modules, now);
        var owner = User.Create(tenant.Id, request.OwnerEmail, request.OwnerFullName, Roles.Owner, now);
        var temporaryPassword = Secrets.NewTemporaryPassword();
        owner.SetTemporaryPassword(hasher.HashPassword(owner, temporaryPassword));
        db.AddRange(tenant, owner);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return Error.Conflict("email_in_use", "Another account already uses this e-mail address.").ToProblem();
        }

        return Results.Created(
            $"/api/admin/companies/{tenant.Id}", new CreatedCompany(ToResponse(tenant, owner.Email), temporaryPassword));
    }

    private static async Task<IResult> SetModulesAsync(
        Guid companyId, SetModulesRequest request, PlatformDbContext db, ModuleCatalog catalog, TenantModules tenantModules,
        TimeProvider time, CancellationToken ct)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Id == companyId, ct);
        if (tenant is null)
        {
            return Error.NotFound("company_not_found", "This company does not exist.").ToProblem();
        }

        var modules = (request.Modules ?? []).Distinct().ToList();
        if (catalog.Validate(modules) is { } problem)
        {
            return Error.Validation("invalid_module_selection", problem).ToProblem();
        }

        // Turning a module off only closes the door. Its data stays, so turning it back on loses nothing.
        tenant.SetModules(modules, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        tenantModules.Forget(tenant.Id);

        var ownerEmail = await db.Users
            .Where(user => user.TenantId == tenant.Id && user.Role == Roles.Owner)
            .Select(user => user.Email)
            .FirstOrDefaultAsync(ct);
        return Results.Ok(ToResponse(tenant, ownerEmail));
    }

    private static CompanyResponse ToResponse(Tenant tenant, string? ownerEmail) => new(
        tenant.Id, tenant.Name, tenant.Sector, tenant.CreatedAt,
        [.. tenant.Modules.Select(module => module.ModuleKey).Order(StringComparer.Ordinal)], ownerEmail);
}
