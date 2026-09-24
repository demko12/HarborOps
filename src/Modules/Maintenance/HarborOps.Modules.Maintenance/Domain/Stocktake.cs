using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class Stocktake
{
    internal const int MaxLines = 200;
    internal const int NotesMaxLength = 1000;
    internal const int ReasonMaxLength = 500;

    private Stocktake()
    {
    }

    private Stocktake(
        Guid id,
        string? notes,
        StocktakeStatus status,
        Guid createdByUserId,
        DateTime createdUtc,
        Guid concurrencyToken)
    {
        Id = id;
        Notes = notes;
        Status = status;
        CreatedByUserId = createdByUserId;
        CreatedUtc = createdUtc;
        ConcurrencyToken = concurrencyToken;
    }

    public Guid Id { get; private set; }

    public string? Notes { get; private set; }

    public StocktakeStatus Status { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public Guid? LastModifiedByUserId { get; private set; }

    public DateTime? LastModifiedUtc { get; private set; }

    public Guid? PostedByUserId { get; private set; }

    public DateTime? PostedUtc { get; private set; }

    public Guid? CancelledByUserId { get; private set; }

    public DateTime? CancelledUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    public Guid ConcurrencyToken { get; private set; }

    internal static Stocktake Create(string? notes, Guid createdByUserId, DateTime utcNow)
    {
        return new Stocktake(
            Guid.NewGuid(),
            notes,
            StocktakeStatus.Draft,
            createdByUserId,
            utcNow,
            Guid.NewGuid());
    }

    internal void RecordChange(Guid actorUserId, DateTime utcNow)
    {
        EnsureDraft();

        LastModifiedByUserId = actorUserId;
        LastModifiedUtc = utcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void MarkPosted(IReadOnlyList<StocktakeLine> lines, Guid actorUserId, DateTime utcNow)
    {
        EnsureDraft();

        foreach (var line in lines)
        {
            line.EnsureReadyToPost();
        }

        Status = StocktakeStatus.Posted;
        PostedByUserId = actorUserId;
        PostedUtc = utcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void Cancel(string reason, Guid actorUserId, DateTime utcNow)
    {
        EnsureDraft();

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new StocktakeInvariantException("A cancellation reason is required.");
        }

        Status = StocktakeStatus.Cancelled;
        CancellationReason = reason.Trim();
        CancelledByUserId = actorUserId;
        CancelledUtc = utcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void EnsureDraft()
    {
        if (Status != StocktakeStatus.Draft)
        {
            throw new StocktakeNotDraftException(Id, Status);
        }
    }
}
