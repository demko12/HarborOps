using System.Security.Claims;
using HarborOps.Modules.Notifications.Application;
using HarborOps.Modules.Notifications.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace HarborOps.Modules.Notifications.Presentation;

[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController(NotificationService service) : ControllerBase
{
    private const string PermissionClaimType = "harborops:permission";
    [HttpGet]
    public async Task<ActionResult<NotificationsPage>> Get([FromQuery] GetNotificationsRequest request, CancellationToken cancellationToken)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        try { return Ok(await service.GetAsync(request, userId, ReadCapabilities(), cancellationToken)); }
        catch (ArgumentException exception) { return ValidationProblem(detail: exception.Message); }
    }

    [HttpPost("{notificationId:guid}/acknowledge")]
    public async Task<IActionResult> Acknowledge(Guid notificationId, CancellationToken cancellationToken)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        return await service.AcknowledgeAsync(notificationId, userId, ReadCapabilities(), cancellationToken) ? NoContent() : NotFound();
    }

    [HttpPost("acknowledge-all")]
    public async Task<IActionResult> AcknowledgeAll(CancellationToken cancellationToken)
    {
        if (!TryUser(out var userId)) return Unauthorized();
        await service.AcknowledgeAllAsync(userId, ReadCapabilities(), cancellationToken);
        return NoContent();
    }

    private bool TryUser(out Guid userId) => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    private HashSet<string> ReadCapabilities() => User.FindAll(PermissionClaimType).Select(x => x.Value.Split('.')[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
