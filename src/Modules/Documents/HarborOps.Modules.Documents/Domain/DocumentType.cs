namespace HarborOps.Modules.Documents.Domain;

public sealed class DocumentType
{
    internal const int NameMaxLength = 200;
    internal const int IssuingAuthorityMaxLength = 200;

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int MandatoryValidityPeriod { get; private set; }

    public string IssuingAuthority { get; private set; } = string.Empty;

    public bool RequiresSurvey { get; private set; }

    private DocumentType()
    {
    }

    public DocumentType(string name, int mandatoryValidityPeriod, string issuingAuthority, bool requiresSurvey)
    {
        Name = name;
        MandatoryValidityPeriod = mandatoryValidityPeriod;
        IssuingAuthority = issuingAuthority;
        RequiresSurvey = requiresSurvey;
    }

    internal static DocumentType Register(
        string name,
        int mandatoryValidityPeriod,
        string issuingAuthority,
        bool requiresSurvey)
    {
        return new DocumentType(name, mandatoryValidityPeriod, issuingAuthority, requiresSurvey);
    }
}
