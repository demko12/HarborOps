using HarborOps.Modules.Voyages.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Voyages.Application;

public sealed class CreatePortCallStatementService(
    DbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<Guid> CreateAsync(
        Guid voyageId,
        Guid stopId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var transaction = await PortCallClearanceGuard.BeginSerializableTransactionAsync(
            dbContext,
            cancellationToken);

        try
        {
            await PortCallLog.LoadStopAsync(
                dbContext,
                voyageId,
                stopId,
                tracking: false,
                cancellationToken);

            var exists = await dbContext.Set<PortCallStatement>()
                .AsNoTracking()
                .AnyAsync(statement => statement.VoyageStopId == stopId, cancellationToken);

            if (exists)
            {
                throw new PortCallStatementAlreadyExistsException(stopId);
            }

            var statement = PortCallStatement.CreateDraft(
                voyageId,
                stopId,
                actorUserId,
                timeProvider.GetUtcNow().UtcDateTime);

            await dbContext.Set<PortCallStatement>().AddAsync(statement, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return statement.Id;
        }
        catch (DbUpdateException exception)
        {
            throw new PortCallLogConflictException(stopId, exception);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
