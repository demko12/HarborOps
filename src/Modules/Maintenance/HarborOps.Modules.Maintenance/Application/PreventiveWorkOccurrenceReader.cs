using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed record PreventiveWorkOccurrence(
    Guid ScheduleId,
    Guid? OccurrenceId,
    Guid VesselId,
    string MaintenanceType,
    string Frequency,
    DateOnly DueDate,
    DateTime DueUtc,
    ScheduleOccurrenceState State,
    Guid? WorkOrderId,
    IReadOnlyList<Guid> EquipmentIds)
{
    internal PreventiveWorkOccurrenceItem ToItem(DateOnly today)
    {
        return new PreventiveWorkOccurrenceItem(
            ScheduleId,
            OccurrenceId,
            VesselId,
            MaintenanceType,
            Frequency,
            DueDate,
            DueUtc,
            State.ToString(),
            ScheduleOccurrence.IsOverdue(State, DueDate, today),
            WorkOrderId,
            EquipmentIds);
    }
}

internal sealed record PreventiveWorkScheduleScope(Guid VesselId, IReadOnlyList<Guid> EquipmentIds);

internal static class PreventiveWorkOccurrenceReader
{
    private sealed record GeneratedOccurrenceRow(
        Guid Id,
        Guid ScheduleId,
        DateOnly DueDate,
        ScheduleOccurrenceState State,
        Guid WorkOrderId);

    private sealed record ScheduleRow(
        Guid Id,
        string MaintenanceType,
        string Frequency,
        DateOnly? NextDueDate);

    internal static async Task<IReadOnlyList<PreventiveWorkOccurrence>> ReadAsync(
        DbContext dbContext,
        DateOnly from,
        DateOnly to,
        Guid? vesselId,
        Guid? scheduleId,
        CancellationToken cancellationToken)
    {
        var links = dbContext.Set<ScheduleEquipment>().AsNoTracking();
        var equipment = dbContext.Set<Equipment>().AsNoTracking();

        var generatedRows = await dbContext.Set<ScheduleOccurrence>()
            .AsNoTracking()
            .Where(occurrence => occurrence.DueDate >= from && occurrence.DueDate <= to)
            .Where(occurrence => scheduleId == null || occurrence.ScheduleId == scheduleId)
            .Select(occurrence => new GeneratedOccurrenceRow(
                occurrence.Id,
                occurrence.ScheduleId,
                occurrence.DueDate,
                occurrence.State,
                occurrence.WorkOrderId))
            .ToListAsync(cancellationToken);

        var generatedScheduleIds = generatedRows
            .Select(row => row.ScheduleId)
            .Distinct()
            .ToList();

        var scheduleQuery = dbContext.Set<MaintenanceSchedule>().AsNoTracking();

        if (scheduleId is { } requestedScheduleId)
        {
            scheduleQuery = scheduleQuery.Where(schedule => schedule.Id == requestedScheduleId);
        }

        if (vesselId is { } requestedVesselId)
        {
            scheduleQuery = scheduleQuery.Where(schedule => links.Any(link =>
                link.ScheduleId == schedule.Id
                && equipment.Any(item => item.Id == link.EquipmentId && item.VesselId == requestedVesselId)));
        }

        var scheduleRows = await scheduleQuery
            .Where(schedule =>
                (schedule.NextDueDate != null && schedule.NextDueDate <= to)
                || generatedScheduleIds.Contains(schedule.Id))
            .Select(schedule => new ScheduleRow(
                schedule.Id,
                schedule.MaintenanceType,
                schedule.Frequency,
                schedule.NextDueDate))
            .ToListAsync(cancellationToken);

        var scope = await ReadScopeAsync(
            dbContext,
            scheduleRows.Select(row => row.Id).ToList(),
            cancellationToken);

        var generatedByKey = generatedRows.ToDictionary(row => (row.ScheduleId, row.DueDate));
        var generatedBySchedule = generatedRows
            .GroupBy(row => row.ScheduleId)
            .ToDictionary(group => group.Key, group => group.Select(row => row.DueDate).ToList());

        var occurrences = new List<PreventiveWorkOccurrence>();

        foreach (var schedule in scheduleRows)
        {
            if (!scope.TryGetValue(schedule.Id, out var scheduleScope))
            {
                continue;
            }

            if (!Enum.TryParse<MaintenanceFrequency>(schedule.Frequency, ignoreCase: true, out var frequency)
                || !Enum.IsDefined(frequency)
                || !ScheduleOccurrenceCalculator.IsTimeBased(frequency))
            {
                continue;
            }

            var dueDates = new SortedSet<DateOnly>();

            if (generatedBySchedule.TryGetValue(schedule.Id, out var generatedDueDates))
            {
                dueDates.UnionWith(generatedDueDates);
            }

            if (schedule.NextDueDate is { } nextDueDate)
            {
                dueDates.UnionWith(
                    ScheduleOccurrenceCalculator.DueDatesInWindow(frequency, nextDueDate, from, to));
            }

            foreach (var dueDate in dueDates)
            {
                if (generatedByKey.TryGetValue((schedule.Id, dueDate), out var generated))
                {
                    occurrences.Add(new PreventiveWorkOccurrence(
                        schedule.Id,
                        generated.Id,
                        scheduleScope.VesselId,
                        schedule.MaintenanceType,
                        schedule.Frequency,
                        dueDate,
                        PreventiveWorkDueTime.ForDueDate(dueDate),
                        generated.State,
                        generated.WorkOrderId,
                        scheduleScope.EquipmentIds));

                    continue;
                }

                occurrences.Add(new PreventiveWorkOccurrence(
                    schedule.Id,
                    null,
                    scheduleScope.VesselId,
                    schedule.MaintenanceType,
                    schedule.Frequency,
                    dueDate,
                    PreventiveWorkDueTime.ForDueDate(dueDate),
                    ScheduleOccurrenceState.Pending,
                    null,
                    scheduleScope.EquipmentIds));
            }
        }

        return Order(occurrences);
    }

    internal static async Task<IReadOnlyDictionary<Guid, PreventiveWorkScheduleScope>> ReadScopeAsync(
        DbContext dbContext,
        IReadOnlyList<Guid> scheduleIds,
        CancellationToken cancellationToken)
    {
        if (scheduleIds.Count == 0)
        {
            return new Dictionary<Guid, PreventiveWorkScheduleScope>();
        }

        var scopeRows = await (
            from link in dbContext.Set<ScheduleEquipment>().AsNoTracking()
            join item in dbContext.Set<Equipment>().AsNoTracking() on link.EquipmentId equals item.Id
            where scheduleIds.Contains(link.ScheduleId)
            orderby link.ScheduleId, item.Name, item.Id
            select new
            {
                link.ScheduleId,
                EquipmentId = item.Id,
                item.VesselId,
            })
            .ToListAsync(cancellationToken);

        return scopeRows
            .GroupBy(row => row.ScheduleId)
            .ToDictionary(
                group => group.Key,
                group => new PreventiveWorkScheduleScope(
                    group.First().VesselId,
                    group.Select(row => row.EquipmentId).ToList()));
    }

    internal static IReadOnlyList<PreventiveWorkOccurrence> Order(
        IEnumerable<PreventiveWorkOccurrence> occurrences)
    {
        return occurrences
            .OrderBy(occurrence => occurrence.DueDate)
            .ThenBy(occurrence => occurrence.ScheduleId)
            .ThenBy(occurrence => occurrence.OccurrenceId ?? Guid.Empty)
            .ToList();
    }
}
