using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace NestorBridge.Configuration;

/// <summary>
/// Loads /data/options.json with snake_case property names as injected by HA Supervisor.
/// </summary>
public static class OptionsJsonLoader
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true
  };

  public static IConfigurationBuilder AddHaOptionsJson(
      this IConfigurationBuilder builder,
      string? path = null)
  {
    // Allow overriding the path via env var for local development
    path ??= Environment.GetEnvironmentVariable("OPTIONS_JSON_PATH") ?? "/data/options.json";

    if (File.Exists(path))
    {
      builder.AddJsonFile(path, optional: false, reloadOnChange: false);
    }

    return builder;
  }
}
