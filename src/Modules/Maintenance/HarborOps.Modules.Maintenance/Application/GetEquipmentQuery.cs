using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetEquipmentQuery(DbContext dbContext)
{
    public async Task<GetEquipmentResponse> GetEquipmentAsync(
        GetEquipmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetEquipmentValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetEquipmentValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetEquipmentValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetEquipmentValidator.DefaultPageSize;

        var equipment = dbContext.Set<Equipment>().AsNoTracking();

        if (request.VesselId.HasValue)
        {
            equipment = equipment.Where(item => item.VesselId == request.VesselId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            var category = request.Category.Trim();
            equipment = equipment.Where(item => item.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(request.Manufacturer))
        {
            var manufacturer = request.Manufacturer.Trim();
            equipment = equipment.Where(item => item.Manufacturer == manufacturer);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            equipment = equipment.Where(item =>
                item.Name.Contains(search)
                || (item.SerialNumber != null && item.SerialNumber.Contains(search)));
        }

        var totalCount = await equipment.CountAsync(cancellationToken);

        var items = await equipment
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(item => new EquipmentListItem(
                item.Id,
                item.VesselId,
                item.Name,
                item.Category,
                item.Manufacturer,
                item.SerialNumber))
            .ToListAsync(cancellationToken);

        return new GetEquipmentResponse(items, pageNumber, pageSize, totalCount);
    }
}
