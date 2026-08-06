using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.Modules.Crew.Application;

internal static class RegisterCrewMemberValidator
{
    internal const string BaseRankIdField = "baseRankId";
    internal const string CdcNumberField = "cdcNumber";
    internal const string DateOfBirthField = "dateOfBirth";
    internal const string EmploymentStatusField = "employmentStatus";
    internal const string HireDateField = "hireDate";
    internal const string ImoSeafarerIdField = "imoSeafarerId";
    internal const string NameField = "name";
    internal const string NationalityField = "nationality";
    internal const string PassportExpiryField = "passportExpiry";
    internal const string PassportNumberField = "passportNumber";

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterCrewMemberRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateBaseRankId(request.BaseRankId, errors);

        ValidateRequiredLength(
            request.Name,
            CrewMember.NameMaxLength,
            NameField,
            "Name",
            errors);

        ValidateDateOfBirth(request.DateOfBirth, errors);

        ValidateRequiredLength(
            request.Nationality,
            CrewMember.NationalityMaxLength,
            NationalityField,
            "Nationality",
            errors);

        ValidateRequiredLength(
            request.ImoSeafarerId,
            CrewMember.ImoSeafarerIdMaxLength,
            ImoSeafarerIdField,
            "IMO seafarer ID",
            errors);

        ValidateRequiredLength(
            request.PassportNumber,
            CrewMember.PassportNumberMaxLength,
            PassportNumberField,
            "Passport number",
            errors);

        ValidateRequiredDate(
            request.PassportExpiry,
            PassportExpiryField,
            "Passport expiry",
            errors);

        ValidateRequiredLength(
            request.CdcNumber,
            CrewMember.CdcNumberMaxLength,
            CdcNumberField,
            "CDC number",
            errors);

        ValidateRequiredDate(
            request.HireDate,
            HireDateField,
            "Hire date",
            errors);

        ValidateRequiredLength(
            request.EmploymentStatus,
            CrewMember.EmploymentStatusMaxLength,
            EmploymentStatusField,
            "Employment status",
            errors);

        return errors;
    }

    private static void ValidateBaseRankId(
        Guid? baseRankId,
        IDictionary<string, string[]> errors)
    {
        if (baseRankId is null || baseRankId.Value == Guid.Empty)
        {
            errors[BaseRankIdField] = ["Base rank is required."];
        }
    }

    private static void ValidateDateOfBirth(
        DateOnly? dateOfBirth,
        IDictionary<string, string[]> errors)
    {
        if (dateOfBirth is null)
        {
            errors[DateOfBirthField] = ["Date of birth is required."];
            return;
        }

        if (dateOfBirth.Value > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            errors[DateOfBirthField] = ["Date of birth must not be in the future."];
        }
    }

    private static void ValidateRequiredDate(
        DateOnly? value,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (value is null)
        {
            errors[field] = [$"{displayName} is required."];
        }
    }

    private static void ValidateRequiredLength(
        string? value,
        int maxLength,
        string field,
        string displayName,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"{displayName} is required."];
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors[field] = [$"{displayName} must not exceed {maxLength} characters."];
        }
    }
}
