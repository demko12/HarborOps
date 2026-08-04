using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class Equipment
{
    internal const int NameMaxLength = 200;
    internal const int CategoryMaxLength = 100;
    internal const int ManufacturerMaxLength = 100;
    internal const int SerialNumberMaxLength = 100;

    private Equipment()
    {
    }

    private Equipment(
        Guid id,
        Guid vesselId,
        string name,
        string category,
        string? manufacturer,
        string? serialNumber)
    {
        Id = id;
        VesselId = vesselId;
        Name = name;
        Category = category;
        Manufacturer = manufacturer;
        SerialNumber = serialNumber;
    }

    public Guid Id { get; private set; }

    public Guid VesselId { get; private set; }

    public string Name { get; private set; } = null!;

    public string Category { get; private set; } = null!;

    public string? Manufacturer { get; private set; }

    public string? SerialNumber { get; private set; }

    internal static Equipment Register(
        Guid vesselId,
        string name,
        string category,
        string? manufacturer,
        string? serialNumber)
    {
        return new Equipment(
            Guid.NewGuid(),
            vesselId,
            name,
            category,
            manufacturer,
            serialNumber);
    }


}
