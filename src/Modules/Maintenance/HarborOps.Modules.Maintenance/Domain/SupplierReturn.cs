using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class SupplierReturn
{
    internal const int MaxLines = 100;
    internal const int NotesMaxLength = 1000;
    internal const int ReasonMaxLength = 500;

    private SupplierReturn()
    {
    }

    private SupplierReturn(
        Guid id,
        Guid purchaseOrderId,
        Guid supplierId,
        string? notes,
        SupplierReturnStatus status,
        Guid createdByUserId,
        DateTime createdUtc,
        Guid concurrencyToken)
    {
        Id = id;
        PurchaseOrderId = purchaseOrderId;
        SupplierId = supplierId;
        Notes = notes;
        Status = status;
        CreatedByUserId = createdByUserId;
        CreatedUtc = createdUtc;
        ConcurrencyToken = concurrencyToken;
    }

    public Guid Id { get; private set; }

    public Guid PurchaseOrderId { get; private set; }

    public Guid SupplierId { get; private set; }

    public string? Notes { get; private set; }

    public SupplierReturnStatus Status { get; private set; }

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

    internal static SupplierReturn Create(
        PurchaseOrder purchaseOrder,
        string? notes,
        Guid createdByUserId,
        DateTime utcNow)
    {
        EnsureReceived(purchaseOrder);

        // The supplier always comes from the purchase order, so a return cannot be redirected elsewhere.
        return new SupplierReturn(
            Guid.NewGuid(),
            purchaseOrder.Id,
            purchaseOrder.SupplierId,
            NormalizeNotes(notes),
            SupplierReturnStatus.Draft,
            createdByUserId,
            utcNow,
            Guid.NewGuid());
    }

    internal static void EnsureReceived(PurchaseOrder purchaseOrder)
    {
        if (purchaseOrder.Status != PurchaseOrderStatus.Received)
        {
            throw new SupplierReturnInvariantException(
                $"Purchase order '{purchaseOrder.Id}' is {purchaseOrder.Status}; only received orders can be returned.");
        }
    }

    internal void ChangeNotes(string? notes, Guid actorUserId, DateTime utcNow)
    {
        EnsureDraft();

        Notes = NormalizeNotes(notes);
        RecordChange(actorUserId, utcNow);
    }

    internal void RecordChange(Guid actorUserId, DateTime utcNow)
    {
        EnsureDraft();

        LastModifiedByUserId = actorUserId;
        LastModifiedUtc = utcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void MarkPosted(IReadOnlyList<SupplierReturnLine> lines, Guid actorUserId, DateTime utcNow)
    {
        EnsureDraft();

        if (lines.Count == 0)
        {
            throw new SupplierReturnInvariantException($"Supplier return '{Id}' has no lines to post.");
        }

        if (lines.Count > MaxLines)
        {
            throw new SupplierReturnInvariantException(
                $"Supplier return '{Id}' has more than {MaxLines} lines.");
        }

        if (lines.Any(line => line.SupplierReturnId != Id))
        {
            throw new SupplierReturnInvariantException(
                $"Supplier return '{Id}' can only post its own lines.");
        }

        Status = SupplierReturnStatus.Posted;
        PostedByUserId = actorUserId;
        PostedUtc = utcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void Cancel(string reason, Guid actorUserId, DateTime utcNow)
    {
        EnsureDraft();

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new SupplierReturnInvariantException("A cancellation reason is required.");
        }

        Status = SupplierReturnStatus.Cancelled;
        CancellationReason = reason.Trim();
        CancelledByUserId = actorUserId;
        CancelledUtc = utcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    internal void EnsureDraft()
    {
        if (Status != SupplierReturnStatus.Draft)
        {
            throw new SupplierReturnNotDraftException(Id, Status);
        }
    }

    private static string? NormalizeNotes(string? notes)
    {
        return string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
