namespace HarborOps.Modules.Documents.Application;

internal sealed class DuplicateDocumentTypeNameException(string name)
    : Exception($"A document type with name '{name}' already exists.")
{
    internal string Name { get; } = name;
}
