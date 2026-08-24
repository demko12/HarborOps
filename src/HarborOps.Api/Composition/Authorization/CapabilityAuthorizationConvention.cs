using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace HarborOps.Api.Composition.Authorization;


internal sealed class CapabilityAuthorizationConvention : IActionModelConvention
{
    public void Apply(ActionModel action)
    {
        var routes = (
            from controllerSelector in action.Controller.Selectors
            from actionSelector in action.Selectors
            let route = AttributeRouteModel.CombineAttributeRouteModel(
                controllerSelector.AttributeRouteModel,
                actionSelector.AttributeRouteModel)
            where route?.Template is not null
            select route.Template).ToArray();

        if (routes.Length == 0)
        {
            return;
        }

        var capability = routes
            .Select(template => "/" + template.TrimStart('~', '/'))
            .SelectMany(routePath => BusinessCapabilityRouting.Map
                .Where(entry => routePath.StartsWith(entry.RoutePrefix, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Capability))
            .Distinct(StringComparer.Ordinal)
            .SingleOrDefault();

        if (capability is null)
        {
            return;
        }

        var httpMethods = action.Selectors
            .Concat(action.Controller.Selectors)
            .SelectMany(selector => selector.ActionConstraints ?? [])
            .OfType<HttpMethodActionConstraint>()
            .SelectMany(constraint => constraint.HttpMethods)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var isReadOnly = httpMethods.Length > 0 &&
            httpMethods.All(method => method is "GET" or "HEAD");

        var policyName = $"{capability}.{(isReadOnly ? "read" : "manage")}";

        action.Filters.Add(new AuthorizeFilter(policyName));
    }
}
