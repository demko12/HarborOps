using HarborOps.Modules.Compliance.Application;
using HarborOps.Modules.Compliance.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Compliance.Presentation;

[ApiController]
[Route("api/compliance/surveys")]
public sealed class SurveyAuditsController(SurveyAuditService surveyAuditService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RegisterSurveyAuditResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RegisterSurveyAuditResponse>> Register(RegisterSurveyAuditRequest request, CancellationToken cancellationToken)
    {
        try { var response = await surveyAuditService.RegisterAsync(request, cancellationToken); return Created($"/api/compliance/surveys/{response.Id}", response); }
        catch (SurveyAuditValidationException exception) { return Validation(exception); }
    }

    [HttpGet]
    public async Task<ActionResult<SurveyAuditListResponse>> Get([FromQuery] SurveyAuditListRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await surveyAuditService.GetAsync(request, cancellationToken)); }
        catch (SurveyAuditValidationException exception) { return Validation(exception); }
    }

    [HttpGet("{surveyId:int}")]
    public async Task<ActionResult<SurveyAuditDetails>> GetDetails(int surveyId, CancellationToken cancellationToken)
    {
        var response = await surveyAuditService.GetDetailsAsync(surveyId, cancellationToken);
        return response is null ? NotFound() : Ok(response);
    }

    private BadRequestObjectResult Validation(SurveyAuditValidationException exception) => BadRequest(new HttpValidationProblemDetails(exception.Errors.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal)) { Status = 400, Title = "One or more validation errors occurred." });
}
