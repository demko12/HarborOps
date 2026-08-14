namespace HarborOps.Modules.Documents.Contracts;

public sealed record DocumentTypeListItem(
    int DocumentTypeId,
    string Name,
    int MandatoryValidityPeriodDays,
    string IssuingAuthority,
    bool RequiresSurvey);
