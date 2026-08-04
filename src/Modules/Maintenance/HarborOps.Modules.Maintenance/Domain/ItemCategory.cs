using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class ItemCategory
{
    internal const int CategoryNameMaxLength = 100;
    internal const int DescriptionMaxLength = 500;

    private ItemCategory()
    {
    }

    private ItemCategory(Guid id, string categoryName, string? description)
    {
        Id = id;
        CategoryName = categoryName;
        Description = description;
    }

    public Guid Id { get; private set; }

    public string CategoryName { get; private set; } = null!;

    public string? Description { get; private set; }

    internal static ItemCategory Create(string categoryName, string? description)
    {
        return new ItemCategory(Guid.NewGuid(), categoryName, description);
    }
}
