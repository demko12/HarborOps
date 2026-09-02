using System.Reflection;
using HarborOps.Modules.Notifications.Application;
using Microsoft.Extensions.DependencyInjection;

namespace HarborOps.Modules.Notifications;

public static class NotificationsModule
{
    public const string Schema = "notifications";
    public static Assembly Assembly { get; } = typeof(NotificationsModule).Assembly;
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        services.AddScoped<NotificationService>();
        services.AddSingleton(TimeProvider.System);
        services.AddControllers().AddApplicationPart(Assembly);
        return services;
    }
}
