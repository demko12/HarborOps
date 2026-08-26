using HarborOps.Modules.Crew.Application;
using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;

namespace HarborOps.UnitTests.Crew;

public sealed class RegisterMedicalExaminationValidatorTests
{
    [Fact]
    public void Validate_returns_no_errors_for_a_valid_request()
    {
        var request = CreateValidRequest();

        var errors = RegisterMedicalExaminationValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_empty_exam_type()
    {
        var request = CreateValidRequest() with { ExamType = " " };

        var errors = RegisterMedicalExaminationValidator.Validate(request);

        Assert.Contains(RegisterMedicalExaminationValidator.ExamTypeField, errors);
    }

    [Fact]
    public void Validate_rejects_exam_type_exceeding_max_length()
    {
        var request = CreateValidRequest() with { ExamType = new string('a', MedicalExamination.ExamTypeMaxLength + 1) };

        var errors = RegisterMedicalExaminationValidator.Validate(request);

        Assert.Contains(RegisterMedicalExaminationValidator.ExamTypeField, errors);
    }

    [Fact]
    public void Validate_rejects_empty_clinic()
    {
        var request = CreateValidRequest() with { Clinic = " " };

        var errors = RegisterMedicalExaminationValidator.Validate(request);

        Assert.Contains(RegisterMedicalExaminationValidator.ClinicField, errors);
    }

    [Fact]
    public void Validate_rejects_clinic_exceeding_max_length()
    {
        var request = CreateValidRequest() with { Clinic = new string('a', MedicalExamination.ClinicMaxLength + 1) };

        var errors = RegisterMedicalExaminationValidator.Validate(request);

        Assert.Contains(RegisterMedicalExaminationValidator.ClinicField, errors);
    }

    [Fact]
    public void Validate_rejects_missing_exam_date()
    {
        var request = CreateValidRequest() with { ExamDate = null };

        var errors = RegisterMedicalExaminationValidator.Validate(request);

        Assert.Contains(RegisterMedicalExaminationValidator.ExamDateField, errors);
    }

    [Fact]
    public void Validate_rejects_missing_expiry_date()
    {
        var request = CreateValidRequest() with { ExpiryDate = null };

        var errors = RegisterMedicalExaminationValidator.Validate(request);

        Assert.Contains(RegisterMedicalExaminationValidator.ExpiryDateField, errors);
    }

    [Fact]
    public void Validate_rejects_expiry_date_before_exam_date()
    {
        var request = CreateValidRequest() with
        {
            ExamDate = new DateOnly(2026, 6, 1),
            ExpiryDate = new DateOnly(2026, 5, 1),
        };

        var errors = RegisterMedicalExaminationValidator.Validate(request);

        Assert.Contains(RegisterMedicalExaminationValidator.ExpiryDateField, errors);
    }

    [Fact]
    public void Validate_accepts_expiry_date_equal_to_exam_date()
    {
        var request = CreateValidRequest() with
        {
            ExamDate = new DateOnly(2026, 6, 1),
            ExpiryDate = new DateOnly(2026, 6, 1),
        };

        var errors = RegisterMedicalExaminationValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_accepts_an_already_expired_expiry_date_relative_to_today()
    {
        var request = CreateValidRequest() with
        {
            ExamDate = new DateOnly(2020, 1, 1),
            ExpiryDate = new DateOnly(2021, 1, 1),
        };

        var errors = RegisterMedicalExaminationValidator.Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_rejects_missing_fitness_result()
    {
        var request = CreateValidRequest() with { FitnessResult = null };

        var errors = RegisterMedicalExaminationValidator.Validate(request);

        Assert.Contains(RegisterMedicalExaminationValidator.FitnessResultField, errors);
    }

    [Theory]
    [InlineData("fit")]
    [InlineData("Unknown")]
    [InlineData("")]
    public void Validate_rejects_a_fitness_result_outside_the_supported_vocabulary(string fitnessResult)
    {
        var request = CreateValidRequest() with { FitnessResult = fitnessResult };

        var errors = RegisterMedicalExaminationValidator.Validate(request);

        Assert.Contains(RegisterMedicalExaminationValidator.FitnessResultField, errors);
    }

    [Theory]
    [InlineData(CrewFitnessResults.Fit)]
    [InlineData(CrewFitnessResults.FitWithRestrictions)]
    [InlineData(CrewFitnessResults.Unfit)]
    public void Validate_accepts_every_supported_fitness_result(string fitnessResult)
    {
        var request = CreateValidRequest() with { FitnessResult = fitnessResult };

        var errors = RegisterMedicalExaminationValidator.Validate(request);

        Assert.Empty(errors);
    }

    private static RegisterMedicalExaminationRequest CreateValidRequest()
    {
        return new RegisterMedicalExaminationRequest
        {
            ExamType = "Periodic",
            Clinic = "Clinic A",
            ExamDate = new DateOnly(2026, 1, 1),
            ExpiryDate = new DateOnly(2027, 1, 1),
            FitnessResult = CrewFitnessResults.Fit,
        };
    }
}
