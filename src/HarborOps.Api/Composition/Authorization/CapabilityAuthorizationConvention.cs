using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace HarborOps.Api.Composition.Authorization;


internal sealed class CapabilityAuthorizationConvention : IActionModelConvention
{
    public void Apply(ActionModel action)
    {
        var routeTemplate = action.Selectors
            .Select(selector => selector.AttributeRouteModel?.Template)
            .FirstOrDefault(template => template is not null)
            ?? action.Controller.Selectors
                .Select(selector => selector.AttributeRouteModel?.Template)
                .FirstOrDefault(template => template is not null);

        if (routeTemplate is null)
        {
            return;
        }

        var routePath = "/" + routeTemplate.TrimStart('/');
        var capability = BusinessCapabilityRouting.Map
            .Where(entry => routePath.StartsWith(entry.RoutePrefix, StringComparison.OrdinalIgnoreCase))
            .Select(entry => (string?)entry.Capability)
            .FirstOrDefault();

        if (capability is null)
        {
            return;
        }

        var httpMethods = action.Selectors
            .SelectMany(selector => selector.ActionConstraints ?? [])
            .OfType<HttpMethodActionConstraint>()
            .SelectMany(constraint => constraint.HttpMethods)
            .ToArray();

        var isReadOnly = httpMethods.Length > 0 &&
            httpMethods.All(method => method is "GET" or "HEAD");

        var policyName = $"{capability}.{(isReadOnly ? "read" : "manage")}";

        action.Filters.Add(new AuthorizeFilter(policyName));
    }
}
