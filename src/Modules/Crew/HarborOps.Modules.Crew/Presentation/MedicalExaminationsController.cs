using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Crew.Presentation;

[ApiController]
[Route("api/crew/medical-examinations")]
public sealed class MedicalExaminationsController(
    IGetMedicalExaminationsQuery getMedicalExaminationsQuery,
    IGetMemberMedicalExaminationsQuery getMemberMedicalExaminationsQuery,
    RegisterMedicalExaminationService registerMedicalExaminationService)
    : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<GetMedicalExaminationsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetMedicalExaminationsResponse>> GetMedicalExaminations(
        [FromQuery] GetMedicalExaminationsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getMedicalExaminationsQuery.GetAsync(request, cancellationToken));
        }
        catch (GetMedicalExaminationsValidationException exception)
        {
            return BadRequest(new HttpValidationProblemDetails(exception.Errors.ToDictionary(
                error => error.Key,
                error => error.Value,
                StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
        }
    }

    [HttpGet("/api/crew/members/{crewMemberId:guid}/medical-examinations")]
    [ProducesResponseType<GetMemberMedicalExaminationsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetMemberMedicalExaminationsResponse>> GetMemberMedicalExaminations(
        Guid crewMemberId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await getMemberMedicalExaminationsQuery.GetAsync(crewMemberId, cancellationToken));
        }
        catch (CrewMemberNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Crew member not found.",
                Detail = exception.Message,
            });
        }
    }

    [HttpPost("/api/crew/members/{crewMemberId:guid}/medical-examinations")]
    [ProducesResponseType<RegisterMedicalExaminationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegisterMedicalExaminationResponse>> Register(
        Guid crewMemberId,
        [FromBody] RegisterMedicalExaminationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await registerMedicalExaminationService.RegisterAsync(
                crewMemberId,
                request,
                cancellationToken);

            return Created(
                $"/api/crew/members/{crewMemberId}/medical-examinations/{response.Id}",
                response);
        }
        catch (RegisterMedicalExaminationValidationException exception)
        {
            return BadRequest(new HttpValidationProblemDetails(exception.Errors.ToDictionary(
                error => error.Key,
                error => error.Value,
                StringComparer.Ordinal))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred.",
            });
        }
        catch (CrewMemberNotFoundException exception)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Crew member not found.",
                Detail = exception.Message,
            });
        }
    }
}
