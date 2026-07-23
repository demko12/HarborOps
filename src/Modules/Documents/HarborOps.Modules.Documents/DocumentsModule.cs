using System.Reflection;

namespace HarborOps.Modules.Documents;

public static class DocumentsModule
{
    public const string Schema = "documents";

    public static Assembly Assembly { get; } = typeof(DocumentsModule).Assembly;
}
