using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Crew.Application;

public sealed class RegisterMedicalExaminationService(DbContext dbContext)
{
    public async Task<RegisterMedicalExaminationResponse> RegisterAsync(
        Guid crewMemberId,
        RegisterMedicalExaminationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RegisterMedicalExaminationValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RegisterMedicalExaminationValidationException(errors);
        }

        var crewMemberExists = await dbContext.Set<CrewMember>()
            .AsNoTracking()
            .AnyAsync(member => member.Id == crewMemberId, cancellationToken);

        if (!crewMemberExists)
        {
            throw new CrewMemberNotFoundException(crewMemberId);
        }

        var examType = request.ExamType!.Trim();
        var clinic = request.Clinic!.Trim();

        var medicalExamination = MedicalExamination.Create(
            crewMemberId,
            examType,
            clinic,
            request.ExamDate!.Value,
            request.ExpiryDate!.Value,
            request.FitnessResult!);

        await dbContext.Set<MedicalExamination>().AddAsync(medicalExamination, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RegisterMedicalExaminationResponse(medicalExamination.Id);
    }
}
