using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/positions")]
public sealed class CrewPositionsController(
    ISearchCrewPositionsQuery searchCrewPositionsQuery,
    RegisterCrewPositionService registerCrewPositionService,
    UpdateCrewPositionService updateCrewPositionService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<SearchCrewPositionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SearchCrewPositionsResponse>> Search(
        [FromQuery] SearchCrewPositionsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await searchCrewPositionsQuery.SearchAsync(request, cancellationToken);

            return Ok(response);
        }
        catch (SearchCrewPositionsValidationException exception)
        {
            return BadRequest(new HttpValidationProblemDetails(
                exception.Errors.ToDictionary(
                    error => error.Key,
                    error => error.Value,
                    StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
        }
    }

    [HttpPost]
    [ProducesResponseType<RegisterCrewPositionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegisterCrewPositionResponse>> Register(
        [FromBody] RegisterCrewPositionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerCrewPositionService.RegisterAsync(request, cancellationToken);

            return Created($"/api/crew/positions/{response.Id}", response);
        }
        catch (RegisterCrewPositionValidationException exception)
        {
            return BadRequest(new HttpValidationProblemDetails(
                exception.Errors.ToDictionary(
                    error => error.Key,
                    error => error.Value,
                    StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
        }
        catch (DuplicateCrewPositionException exception)
        {
            return Conflict(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [CrewPositionFieldValidation.RankNameField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A position with this rank and department already exists.",
            });
        }
    }

    [HttpPut("{positionId:guid}")]
    [ProducesResponseType<UpdateCrewPositionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UpdateCrewPositionResponse>> Update(
        Guid positionId,
        [FromBody] UpdateCrewPositionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await updateCrewPositionService.UpdateAsync(positionId, request, cancellationToken));
        }
        catch (UpdateCrewPositionValidationException exception)
        {
            return BadRequest(new HttpValidationProblemDetails(
                exception.Errors.ToDictionary(
                    error => error.Key,
                    error => error.Value,
                    StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
        }
        catch (CrewPositionNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Crew position not found.",
                Detail = exception.Message,
            });
        }
        catch (DuplicateCrewPositionException exception)
        {
            return Conflict(new HttpValidationProblemDetails(
                new Dictionary<string, string[]>
                {
                    [CrewPositionFieldValidation.RankNameField] = [exception.Message],
                })
            {
                Status = StatusCodes.Status409Conflict,
                Title = "A position with this rank and department already exists.",
            });
        }
    }
}
