using System.Reflection;
using HarborOps.Modules.Documents.Application;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Modules.Documents;

public static class DocumentsModule
{
    public const string Schema = "documents";

    public static Assembly Assembly { get; } = typeof(DocumentsModule).Assembly;

    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        services.AddScoped<RegisterDocumentService>();
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
