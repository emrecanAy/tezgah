using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Tezgah.Core.Results;
using Tezgah.Core.Web;

namespace Tezgah.Core.Modules;

public static class ModuleEndpoints
{
    /// <summary>
    /// Mounts every shipped module under <c>/api/{key}</c>. The whole group answers 403
    /// <c>module_not_enabled</c> for a company that has not been given the module, so a module
    /// cannot forget to check.
    /// </summary>
    public static void MapModules(this IEndpointRouteBuilder api, ModuleCatalog catalog)
    {
        foreach (var module in catalog.Modules)
        {
            var key = module.Info.Key;
            var group = api.MapGroup($"/{key}")
                .RequireAuthorization(Policies.TenantUser)
                .WithTags(module.Info.Name)
                .AddEndpointFilter(async (context, next) =>
                {
                    var http = context.HttpContext;
                    var tenantId = http.User.GetTenantId()!.Value;
                    var modules = http.RequestServices.GetRequiredService<ITenantModules>();

                    return await modules.IsEnabledAsync(tenantId, key, http.RequestAborted)
                        ? await next(context)
                        : Error.Forbidden("module_not_enabled", $"The '{key}' module is not enabled for this company.").ToProblem();
                });

            module.MapEndpoints(group);
        }
    }
}
