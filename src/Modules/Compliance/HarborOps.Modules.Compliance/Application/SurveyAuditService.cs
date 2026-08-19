using HarborOps.Modules.Compliance.Contracts;
using HarborOps.Modules.Compliance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Compliance.Application;

public sealed class SurveyAuditService(DbContext dbContext)
{
    public async Task<RegisterSurveyAuditResponse> RegisterAsync(RegisterSurveyAuditRequest request, CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0) throw new SurveyAuditValidationException(errors);
        var survey = new SurveyAudit(request.VesselId!.Value, request.SurveyDate!.Value, request.Type!.Trim(), request.Result!.Trim(), request.Findings!.Trim(), request.DocumentId, string.IsNullOrWhiteSpace(request.InspectorName) ? null : request.InspectorName.Trim());
        dbContext.Set<SurveyAudit>().Add(survey);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(survey.Id);
    }

    public async Task<SurveyAuditListResponse> GetAsync(SurveyAuditListRequest request, CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0) throw new SurveyAuditValidationException(errors);
        var query = dbContext.Set<SurveyAudit>().AsNoTracking().AsQueryable();
        if (request.VesselId is { } vesselId) query = query.Where(x => x.VesselId == vesselId);
        if (!string.IsNullOrWhiteSpace(request.Type)) query = query.Where(x => x.Type == request.Type.Trim());
        if (!string.IsNullOrWhiteSpace(request.Result)) query = query.Where(x => x.Result == request.Result.Trim());
        if (request.FromDate is { } from) query = query.Where(x => x.Date >= from);
        if (request.ToDate is { } to) query = query.Where(x => x.Date <= to);
        if (!string.IsNullOrWhiteSpace(request.Search)) { var term = request.Search.Trim(); query = query.Where(x => x.Type.Contains(term) || (x.InspectorName != null && x.InspectorName.Contains(term)) || x.Findings.Contains(term)); }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.Date).ThenBy(x => x.Id).Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).Select(x => new SurveyAuditListItem(x.Id, x.VesselId, x.Date, x.Type, x.InspectorName, x.Result, x.DocumentId)).ToListAsync(cancellationToken);
        return new(items, total, request.PageNumber, request.PageSize);
    }

    public Task<SurveyAuditDetails?> GetDetailsAsync(int id, CancellationToken cancellationToken) => dbContext.Set<SurveyAudit>().AsNoTracking().Where(x => x.Id == id).Select(x => new SurveyAuditDetails(x.Id, x.VesselId, x.Date, x.Type, x.InspectorName, x.Result, x.Findings, x.DocumentId)).SingleOrDefaultAsync(cancellationToken);

    private static Dictionary<string, string[]> Validate(RegisterSurveyAuditRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.VesselId is null || request.VesselId == Guid.Empty) errors["vesselId"] = ["Vessel is required."];
        if (request.SurveyDate is null) errors["surveyDate"] = ["Survey date is required."];
        else if (request.SurveyDate > DateOnly.FromDateTime(DateTime.UtcNow)) errors["surveyDate"] = ["Survey date cannot be in the future."];
        if (string.IsNullOrWhiteSpace(request.Type) || !SurveyAudit.SupportedTypes.Contains(request.Type.Trim())) errors["type"] = ["Select a supported survey or audit type."];
        if (string.IsNullOrWhiteSpace(request.Result) || !SurveyAudit.SupportedResults.Contains(request.Result.Trim())) errors["result"] = ["Select a supported result."];
        if (string.IsNullOrWhiteSpace(request.Findings)) errors["findings"] = ["Findings are required."];
        else if (request.Findings.Trim().Length > SurveyAudit.FindingsMaxLength) errors["findings"] = ["Findings are too long."];
        if (request.InspectorName?.Trim().Length > SurveyAudit.InspectorNameMaxLength) errors["inspectorName"] = ["Inspector name is too long."];
        return errors;
    }
    private static Dictionary<string, string[]> Validate(SurveyAuditListRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.PageNumber < 1) errors["pageNumber"] = ["Page number must be at least 1."];
        if (request.PageSize is < 1 or > 100) errors["pageSize"] = ["Page size must be between 1 and 100."];
        if (request.FromDate is { } from && request.ToDate is { } to && from > to) errors["dateRange"] = ["From date must not be after to date."];
        if (!string.IsNullOrWhiteSpace(request.Type) && !SurveyAudit.SupportedTypes.Contains(request.Type.Trim())) errors["type"] = ["Select a supported survey or audit type."];
        if (!string.IsNullOrWhiteSpace(request.Result) && !SurveyAudit.SupportedResults.Contains(request.Result.Trim())) errors["result"] = ["Select a supported result."];
        return errors;
    }
}

public sealed class SurveyAuditValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
