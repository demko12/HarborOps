using HarborOps.Modules.Identity.Application;
using HarborOps.Modules.Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace HarborOps.Modules.Identity.Presentation;

public static class IdentityAdminEndpoints
{
    public static IEndpointRouteBuilder MapIdentityAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity/admin").WithTags("Identity Administration");

        group.MapGet("/users", async (
                [AsParameters] SearchUsersRequest request,
                UserAdministrationService service,
                CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.SearchAsync(request, cancellationToken));
            }
            catch (AdminValidationException exception)
            {
                return ValidationProblem(exception);
            }
        })
            .RequireAuthorization(Permissions.IdentityUsers.Read);

        group.MapPost("/users", async (
                CreateUserRequest request,
                UserAdministrationService service,
                CancellationToken cancellationToken) =>
        {
            try
            {
                var response = await service.CreateAsync(request, cancellationToken);
                return Results.Created($"/api/identity/admin/users/{response.Id}", response);
            }
            catch (AdminValidationException exception)
            {
                return ValidationProblem(exception);
            }
            catch (DuplicateUserEmailException exception)
            {
                return Conflict("A user with this email already exists.", exception.Message);
            }
        })
            .RequireAuthorization(Permissions.IdentityUsers.Manage);

        group.MapGet("/roles", async (
                RoleAdministrationService service,
                CancellationToken cancellationToken) => Results.Ok(await service.ListAsync(cancellationToken)))
            .RequireAuthorization(Permissions.IdentityRoles.Read);

        group.MapPost("/roles", async (
                CreateRoleRequest request,
                RoleAdministrationService service,
                CancellationToken cancellationToken) =>
        {
            try
            {
                var response = await service.CreateAsync(request, cancellationToken);
                return Results.Created($"/api/identity/admin/roles/{response.Id}", response);
            }
            catch (AdminValidationException exception)
            {
                return ValidationProblem(exception);
            }
            catch (DuplicateRoleNameException exception)
            {
                return Conflict("A role with this name already exists.", exception.Message);
            }
        })
            .RequireAuthorization(Permissions.IdentityRoles.Manage);

        group.MapPut("/users/{userId:guid}/roles", async (
                Guid userId,
                AssignUserRolesRequest request,
                RoleAdministrationService service,
                CancellationToken cancellationToken) =>
        {
            try
            {
                await service.AssignUserRolesAsync(userId, request, cancellationToken);
                return Results.NoContent();
            }
            catch (UserNotFoundException)
            {
                return Results.NotFound();
            }
            catch (RoleNotFoundException)
            {
                return Results.NotFound();
            }
            catch (FinalIdentityAdministratorException exception)
            {
                return Conflict("Final administrator protection.", exception.Message);
            }
        })
            .RequireAuthorization(Permissions.IdentityUsers.Manage);

        group.MapPut("/roles/{roleId:guid}/permissions", async (
                Guid roleId,
                AssignRolePermissionsRequest request,
                RoleAdministrationService service,
                CancellationToken cancellationToken) =>
        {
            try
            {
                await service.AssignRolePermissionsAsync(roleId, request, cancellationToken);
                return Results.NoContent();
            }
            catch (RoleNotFoundException)
            {
                return Results.NotFound();
            }
            catch (AdminValidationException exception)
            {
                return ValidationProblem(exception);
            }
            catch (FinalIdentityAdministratorException exception)
            {
                return Conflict("Final administrator protection.", exception.Message);
            }
        })
            .RequireAuthorization(Permissions.IdentityRoles.Manage);

        return app;
    }

    private static IResult ValidationProblem(AdminValidationException exception)
    {
        return Results.ValidationProblem(exception.Errors, statusCode: StatusCodes.Status400BadRequest);
    }

    private static IResult Conflict(string title, string detail)
    {
        return Results.Conflict(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = title,
            Detail = detail,
        });
    }
}
