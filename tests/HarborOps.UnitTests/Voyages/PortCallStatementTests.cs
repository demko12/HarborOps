using HarborOps.Modules.Voyages.Domain;

namespace HarborOps.UnitTests.Voyages;

public sealed class PortCallStatementTests
{
    private static readonly Guid ActorId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private const string ActorName = "master@harborops.test";

    private static readonly DateTime ArrivalUtc = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_new_statement_is_a_draft_without_snapshot_values()
    {
        var statement = PortCallStatement.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), ActorId, ArrivalUtc);

        Assert.Equal(PortCallStatement.DraftStatus, statement.Status);
        Assert.False(statement.IsFinalized);
        Assert.Null(statement.FinalizedByUserId);
        Assert.Null(statement.FinalizedByName);
        Assert.Null(statement.PortTimeZone);
        Assert.Null(statement.PortName);
        Assert.Null(statement.SourceVersion);
    }

    [Fact]
    public void Finalizing_stores_the_snapshot_and_numbers_the_entries_in_order()
    {
        var statement = PortCallStatement.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), ActorId, ArrivalUtc);
        var draftToken = statement.ConcurrencyToken;
        var finalizedAtUtc = ArrivalUtc.AddDays(1);

        var entries = statement.Finalize(Snapshot(), ActorId, ActorName, finalizedAtUtc);

        Assert.Equal(PortCallStatement.FinalizedStatus, statement.Status);
        Assert.Equal(ActorId, statement.FinalizedByUserId);
        Assert.Equal(ActorName, statement.FinalizedByName);
        Assert.Equal(finalizedAtUtc, statement.FinalizedAtUtc);
        Assert.Equal("VOY-1", statement.VoyageNumber);
        Assert.Equal("Port of Bar", statement.PortName);
        Assert.Equal("MEBAR", statement.PortUnLocode);
        Assert.Equal("Europe/Podgorica", statement.PortTimeZone);
        Assert.Equal("Berth 3", statement.BerthName);
        Assert.Equal(ArrivalUtc, statement.ActualArrivalUtc);
        Assert.Equal(ArrivalUtc.AddHours(8), statement.ActualDepartureUtc);
        Assert.Equal(new string('A', 64), statement.SourceVersion);
        Assert.NotEqual(draftToken, statement.ConcurrencyToken);

        Assert.Equal([1, 2], entries.Select(entry => entry.Position));
        Assert.All(entries, entry => Assert.Equal(statement.Id, entry.PortCallStatementId));
        Assert.Equal(PortCallLogSource.StopArrival, entries[0].Source);
        Assert.Equal(PortCallActivityType.AllFast, entries[1].Label);
        Assert.Equal(2, entries[1].SourceRevision);
    }

    [Fact]
    public void A_finalized_statement_cannot_be_finalized_again()
    {
        var statement = PortCallStatement.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), ActorId, ArrivalUtc);
        statement.Finalize(Snapshot(), ActorId, ActorName, ArrivalUtc.AddDays(1));

        Assert.Throws<PortCallStatementAlreadyFinalizedException>(() =>
            statement.Finalize(Snapshot(), ActorId, ActorName, ArrivalUtc.AddDays(2)));
    }

    private static PortCallStatementSnapshot Snapshot() =>
        new(
            "VOY-1",
            1,
            "Port of Bar",
            "MEBAR",
            "Europe/Podgorica",
            "Berth 3",
            ArrivalUtc,
            ArrivalUtc.AddHours(8),
            new string('A', 64),
            [
                new PortCallLogEntry(
                    PortCallLogSource.StopArrival,
                    Guid.NewGuid(),
                    null,
                    0,
                    "Actual arrival (ATA)",
                    ArrivalUtc,
                    null,
                    null,
                    null),
                new PortCallLogEntry(
                    PortCallLogSource.ManualActivity,
                    Guid.NewGuid(),
                    2,
                    1,
                    PortCallActivityType.AllFast,
                    ArrivalUtc.AddMinutes(20),
                    null,
                    "Lines ashore",
                    null),
            ]);
}
