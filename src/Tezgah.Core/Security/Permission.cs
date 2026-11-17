using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tezgah.Core.Web;

namespace Tezgah.Core.Security;

/// <summary>
/// Something a user may do, declared by the module that owns it, with the lowest company role that
/// may do it. Endpoints ask for permissions, never for roles, so the role ladder can change without
/// touching a single endpoint.
/// </summary>
/// <param name="Key">Stable identifier, e.g. <c>orders.cancel</c>. The client uses it to show or hide actions.</param>
public sealed record Permission(string Key, string Description, string MinimumRole)
{
    public bool IsGrantedTo(ClaimsPrincipal user) =>
        user.GetTenantId() is not null && Roles.Rank(user.GetRole()) >= Roles.Rank(MinimumRole);
}

internal sealed class PermissionRequirement(Permission permission) : IAuthorizationRequirement
{
    public Permission Permission { get; } = permission;
}

internal sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (requirement.Permission.IsGrantedTo(context.User))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class PermissionEndpoints
{
    /// <summary>
    /// Closes the endpoint to users whose role is below the permission's. The permission is also kept
    /// as endpoint metadata, which is how a test proves that no module endpoint was left without one.
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, Permission permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder
            .RequireAuthorization(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build())
            .WithMetadata(permission);

    /// <summary>Registers the handler that decides permission requirements.</summary>
    public static IServiceCollection AddPermissions(this IServiceCollection services) =>
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
}
