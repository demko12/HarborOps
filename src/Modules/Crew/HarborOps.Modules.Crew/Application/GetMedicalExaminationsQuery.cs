using HarborOps.Modules.Crew.Contracts;
using HarborOps.Modules.Crew.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HarborOps.Modules.Crew.Application;

internal sealed class GetMedicalExaminationsQuery(
    DbContext dbContext,
    TimeProvider timeProvider,
    IOptions<MedicalExaminationOptions> options)
    : IGetMedicalExaminationsQuery
{
    public async Task<GetMedicalExaminationsResponse> GetAsync(
        GetMedicalExaminationsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetMedicalExaminationsValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetMedicalExaminationsValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetMedicalExaminationsValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetMedicalExaminationsValidator.DefaultPageSize;
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var expiringSoonThreshold = today.AddDays(options.Value.ExpiringSoonWithinDays);

        var rows =
            from exam in dbContext.Set<MedicalExamination>().AsNoTracking()
            join member in dbContext.Set<CrewMember>().AsNoTracking()
                on exam.CrewMemberId equals member.Id
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
            };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            rows = rows.Where(row => row.MemberName.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(request.FitnessResult))
        {
            rows = rows.Where(row => row.FitnessResult == request.FitnessResult);
        }

        if (request.Expired is { } expired)
        {
            rows = expired
                ? rows.Where(row => row.ExpiryDate < today)
                : rows.Where(row => row.ExpiryDate >= today);
        }

        if (request.ExpiringWithinDays is { } expiringWithinDays)
        {
            var windowEnd = today.AddDays(expiringWithinDays);
            rows = rows.Where(row => row.ExpiryDate >= today && row.ExpiryDate <= windowEnd);
        }

        var totalCount = await rows.CountAsync(cancellationToken);

        var items = await rows
            .OrderByDescending(row => row.ExpiryDate < today)
            .ThenBy(row => row.ExpiryDate)
            .ThenBy(row => row.MemberName)
            .ThenBy(row => row.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
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
            .ToListAsync(cancellationToken);

        return new GetMedicalExaminationsResponse(items, pageNumber, pageSize, totalCount);
    }
}
