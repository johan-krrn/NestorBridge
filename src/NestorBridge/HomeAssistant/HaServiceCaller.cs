using Microsoft.Extensions.Logging;
using NestorBridge.HomeAssistant.Models;

namespace NestorBridge.HomeAssistant;

/// <summary>
/// High-level API for calling HA services, wrapping the raw WebSocket call_service.
/// </summary>
public sealed class HaServiceCaller
{
  private readonly IHaWebSocketClient _client;
  private readonly ILogger<HaServiceCaller> _logger;

  public HaServiceCaller(IHaWebSocketClient client, ILogger<HaServiceCaller> logger)
  {
    _client = client;
    _logger = logger;
  }

  /// <summary>
  /// Execute a generic cloud command against HA.
  /// Splits entity_id to extract domain, maps action to service name.
  /// Returns the HA context id from the result for ack purposes.
  /// </summary>
  public async Task<(bool Success, string? ContextId, string? Error)> ExecuteCommandAsync(
      CloudCommand command, CancellationToken cancellationToken)
  {
    var entityId = command.TargetEntityId;
    var dotIdx = entityId.IndexOf('.');
    if (dotIdx < 0)
      return (false, null, $"Invalid entity_id format: {entityId}");

    var domain = entityId[..dotIdx];
    var service = command.Action;

    _logger.LogInformation("Calling HA service {Domain}.{Service} on {Entity}",
        domain, service, entityId);

    try
    {
      var result = await _client.CallServiceAsync(
          domain, service, entityId, command.Parameters, cancellationToken);

      if (result.Success == true)
      {
        _logger.LogInformation("Service call succeeded for {Entity}", entityId);
        // TODO(ben): extract context id from result if present
        return (true, null, null);
      }

      var error = result.Error?.Message ?? "Unknown HA error";
      _logger.LogError("Service call failed for {Entity}: {Error}", entityId, error);
      return (false, null, error);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Exception calling service {Domain}.{Service} on {Entity}",
          domain, service, entityId);
      return (false, null, ex.Message);
    }
  }
}
