using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class UnitOfMeasure
{
    internal const int UnitNameMaxLength = 100;
    internal const int AbbreviationMaxLength = 20;

    private UnitOfMeasure()
    {
    }

    private UnitOfMeasure(Guid id, string unitName, string abbreviation)
    {
        Id = id;
        UnitName = unitName;
        Abbreviation = abbreviation;
    }

    public Guid Id { get; private set; }

    public string UnitName { get; private set; } = null!;

    public string Abbreviation { get; private set; } = null!;

    internal static UnitOfMeasure Create(string unitName, string abbreviation)
    {
        return new UnitOfMeasure(Guid.NewGuid(), unitName, abbreviation);
    }
}
