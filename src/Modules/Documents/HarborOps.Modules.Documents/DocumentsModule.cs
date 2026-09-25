using System.Reflection;
using HarborOps.Modules.Documents.Application;
using HarborOps.Modules.Documents.Contracts;
using HarborOps.Modules.Documents.Infrastructure;
using HarborOps.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HarborOps.Modules.Documents;

public static class DocumentsModule
{
    public const string Schema = "documents";

    public static Assembly Assembly { get; } = typeof(DocumentsModule).Assembly;

    public static IServiceCollection AddDocumentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<RegisterDocumentService>();
        services.AddScoped<DocumentLifecycleService>();
        services.AddScoped<DocumentDistributionService>();
        services.AddScoped<DocumentDistributionInvalidationService>();
        services.AddScoped<RegisterDocumentTypeService>();
        services.AddScoped<IGetExpiringDocumentsQuery, GetExpiringDocumentsQuery>();
        services.AddScoped<IExpiryAlertCandidateQuery, ExpiryAlertCandidateQuery>();
        services.AddScoped<IGetDocumentTypesQuery, GetDocumentTypesQuery>();
        services.AddScoped<IGetDocumentsQuery, GetDocumentsQuery>();
        services.AddScoped<IOperationsDashboardDocumentsQuery, OperationsDashboardDocumentsQuery>();
        services.AddScoped<IReportsDocumentsExpiryQuery, ReportsDocumentsExpiryQuery>();
        services.TryAddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IGetRetentionPoliciesQuery, GetRetentionPoliciesQuery>();
        services.AddScoped<RetentionPolicyAdminService>();
        services.AddScoped<IGetRetentionQueueQuery, GetRetentionQueueQuery>();
        services.AddScoped<DocumentRetentionService>();
        services.AddScoped<LegalHoldService>();
        services.AddScoped<DispositionRequestService>();
        services.AddScoped<IGetDocumentRetentionStateQuery, GetDocumentRetentionStateQuery>();

        services.Configure<DocumentFileValidationOptions>(
            configuration.GetSection(DocumentFileValidationOptions.SectionName));
        services.Configure<LocalDiskDocumentFileStorageOptions>(
            configuration.GetSection(LocalDiskDocumentFileStorageOptions.SectionName));

        services.AddScoped<IDocumentFileStorage, LocalDiskDocumentFileStorage>();
        services.AddScoped<IUploadDocumentFileService, UploadDocumentFileService>();
        services.AddScoped<IReplaceDocumentFileService, ReplaceDocumentFileService>();
        services.AddScoped<IGetDocumentFileQuery, GetDocumentFileQuery>();
        services.AddScoped<IGetDocumentByIdQuery, GetDocumentByIdQuery>();
        services.AddScoped<IGetDocumentRequirementsQuery, GetDocumentRequirementsQuery>();
        services.AddScoped<IGetDocumentReadinessQuery, GetDocumentReadinessQuery>();
        services.AddScoped<IGetVesselReadinessOverviewQuery, GetVesselReadinessOverviewQuery>();
        services.AddScoped<DocumentRequirementService>();

        services.AddControllers()
            .AddApplicationPart(Assembly);

        return services;
    }
}
