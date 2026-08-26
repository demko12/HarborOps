using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.Modules.Crew.Application;

internal static class RegisterMedicalExaminationValidator
{
    internal const string ExamTypeField = "examType";
    internal const string ClinicField = "clinic";
    internal const string ExamDateField = "examDate";
    internal const string ExpiryDateField = "expiryDate";
    internal const string FitnessResultField = "fitnessResult";

    internal static IReadOnlyDictionary<string, string[]> Validate(RegisterMedicalExaminationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        ValidateRequiredLength(
            request.ExamType,
            MedicalExamination.ExamTypeMaxLength,
            ExamTypeField,
            "Exam type",
            errors);

        ValidateRequiredLength(
            request.Clinic,
            MedicalExamination.ClinicMaxLength,
            ClinicField,
            "Clinic",
            errors);

        if (request.ExamDate is null)
        {
            errors[ExamDateField] = ["Exam date is required."];
        }

        ValidateExpiryDate(request.ExamDate, request.ExpiryDate, errors);

        ValidateFitnessResult(request.FitnessResult, errors);

        return errors;
    }

    private static void ValidateExpiryDate(
        DateOnly? examDate,
        DateOnly? expiryDate,
        IDictionary<string, string[]> errors)
    {
        if (expiryDate is null)
        {
            errors[ExpiryDateField] = ["Expiry date is required."];
            return;
        }

        if (examDate is not null && expiryDate.Value < examDate.Value)
        {
            errors[ExpiryDateField] = ["Expiry date must not be earlier than the exam date."];
        }
    }

    private static void ValidateFitnessResult(string? fitnessResult, IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(fitnessResult))
        {
            errors[FitnessResultField] = ["Fitness result is required."];
            return;
        }

        if (fitnessResult.Length > MedicalExamination.FitnessResultMaxLength ||
            !CrewFitnessResults.All.Contains(fitnessResult))
        {
            errors[FitnessResultField] = ["Fitness result is not supported."];
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
