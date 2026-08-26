using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetMemberMedicalExaminationsQuery(
    DbContext dbContext,
    TimeProvider timeProvider,
    IOptions<MedicalExaminationOptions> options)
    : IGetMemberMedicalExaminationsQuery
{
    public async Task<GetMemberMedicalExaminationsResponse> GetAsync(
        Guid crewMemberId,
        CancellationToken cancellationToken)
    {
        var crewMemberExists = await dbContext.Set<CrewMember>()
            .AsNoTracking()
            .AnyAsync(member => member.Id == crewMemberId, cancellationToken);

        if (!crewMemberExists)
        {
            throw new CrewMemberNotFoundException(crewMemberId);
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var expiringSoonThreshold = today.AddDays(options.Value.ExpiringSoonWithinDays);

        var rows = await (
            from exam in dbContext.Set<MedicalExamination>().AsNoTracking()
            join member in dbContext.Set<CrewMember>().AsNoTracking()
                on exam.CrewMemberId equals member.Id
            where exam.CrewMemberId == crewMemberId
            select new
            {
                exam.Id,
                exam.CrewMemberId,
                MemberName = member.Name,
                member.EmploymentStatus,
                exam.ExamType,
                exam.Clinic,
                exam.ExamDate,
                exam.ExpiryDate,
                exam.FitnessResult,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .OrderByDescending(row => row.ExamDate)
            .ThenByDescending(row => row.Id)
            .Select(row => new MedicalExaminationListItem(
                row.Id,
                row.CrewMemberId,
                row.MemberName,
                row.EmploymentStatus,
                row.ExamType,
                row.Clinic,
                row.ExamDate,
                row.ExpiryDate,
                row.FitnessResult,
                row.ExpiryDate < today,
                row.ExpiryDate >= today && row.ExpiryDate <= expiringSoonThreshold))
            .ToList();

        var currentFitnessResult = rows
            .Where(row => row.ExamDate <= today)
            .OrderByDescending(row => row.ExamDate)
            .ThenByDescending(row => row.Id)
            .Select(row => row.FitnessResult)
            .FirstOrDefault();

        return new GetMemberMedicalExaminationsResponse(items, currentFitnessResult);
    }
}
