using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Monitoring;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Controllers;

[ApiController]
[Route("api/monitoring")]
public class MonitoringWebhookController : ControllerBase
{
    private readonly IViolationMonitoringOrchestrator _monitoringOrchestrator;
    private readonly ViolationMonitoringOptions _options;

    public MonitoringWebhookController(
        IViolationMonitoringOrchestrator monitoringOrchestrator,
        IOptions<ViolationMonitoringOptions> options)
    {
        _monitoringOrchestrator = monitoringOrchestrator;
        _options = options.Value;
    }

    [HttpPost("instant-alert")]
    public async Task<IActionResult> ReceiveInstantAlert(
        [FromBody] InstantViolationAlertPayload payload,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.InstantAlertApiKey))
        {
            var requestKey = Request.Headers["X-Monitoring-Key"].ToString();
            if (!string.Equals(requestKey, _options.InstantAlertApiKey, StringComparison.Ordinal))
            {
                return Unauthorized(new { success = false, message = "Monitoring key không hợp lệ." });
            }
        }

        if (payload == null || string.IsNullOrWhiteSpace(payload.Label))
        {
            return BadRequest(new { success = false, message = "Payload instant alert không hợp lệ." });
        }

        var result = await _monitoringOrchestrator.PublishInstantAlertAsync(payload, cancellationToken);
        return Ok(new
        {
            success = true,
            result.ViolationId,
            result.TrackId,
            result.ViolationType,
            result.Severity,
            result.DetectedAtUtc,
            result.TelegramAttempted
        });
    }
}
