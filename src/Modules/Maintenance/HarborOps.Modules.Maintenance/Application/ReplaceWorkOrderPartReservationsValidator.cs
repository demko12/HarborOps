using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;

namespace HarborOps.Modules.Maintenance.Application;

internal static class ReplaceWorkOrderPartReservationsValidator
{
    internal const string ReservationsField = "reservations";

    internal static IReadOnlyDictionary<string, string[]> Validate(
        ReplaceWorkOrderPartReservationsRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (request.Reservations is null)
        {
            errors[ReservationsField] = ["Required."];

            return errors;
        }

        if (request.Reservations.Any(entry =>
            entry.InventoryItemId is null || entry.InventoryItemId.Value == Guid.Empty))
        {
            errors[ReservationsField] = ["Each entry must specify a non-empty inventoryItemId."];

            return errors;
        }

        if (request.Reservations.Any(entry => entry.Quantity is null || entry.Quantity.Value <= 0))
        {
            errors[ReservationsField] = ["Each quantity must be greater than zero."];

            return errors;
        }

        if (request.Reservations
            .Select(entry => entry.InventoryItemId!.Value)
            .Distinct()
            .Count() != request.Reservations.Count)
        {
            errors[ReservationsField] = ["Must not contain duplicate inventoryItemId values."];
        }

        return errors;
    }
}
