using System.Reflection;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Modules.Documents;

public static class DocumentsModule
{
    public const string Schema = "documents";

    public static Assembly Assembly { get; } = typeof(DocumentsModule).Assembly;

    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        services.AddScoped<RegisterDocumentService>();
        services.AddScoped<RegisterDocumentTypeService>();
        services.AddScoped<IGetExpiringDocumentsQuery, GetExpiringDocumentsQuery>();
        services.AddScoped<IGetDocumentTypesQuery, GetDocumentTypesQuery>();
        services.TryAddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
