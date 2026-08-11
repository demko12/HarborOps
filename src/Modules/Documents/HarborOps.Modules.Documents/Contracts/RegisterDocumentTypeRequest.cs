namespace HarborOps.Modules.Documents.Contracts;

public sealed record RegisterDocumentTypeRequest
{
    public string? Name { get; init; }

    public int? MandatoryValidityPeriodDays { get; init; }

    public string? IssuingAuthority { get; init; }

    public bool? RequiresSurvey { get; init; }
}
