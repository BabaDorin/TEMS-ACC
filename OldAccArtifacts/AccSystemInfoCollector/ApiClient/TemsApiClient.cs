using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TEMS.ACC.Configuration;
using TEMS.ACC.Models;

namespace TEMS.ACC.ApiClient;

public sealed class TemsApiClient(
    ILogger<TemsApiClient> logger,
    IHttpClientFactory httpClientFactory,
    IOptions<TemsConfiguration> config) : ITemsApiClient
{
    private readonly TemsConfiguration _config = config.Value;
    private readonly string _errorLogPath = GetErrorLogPath();

    public async Task<Result<string>> SendPropertiesAsync(SystemProperties properties)
    {
        var url = $"{_config.ApiUrl.TrimEnd('/')}/managed-assets/{_config.AssetId}/properties";
        return await PostAsync("SendProperties", url, properties);
    }

    public async Task<Result<string>> SendMetricsAsync(MetricsBatch batch)
    {
        var url = $"{_config.ApiUrl.TrimEnd('/')}/managed-assets/{_config.AssetId}/metrics";
        return await PostAsync("SendMetrics", url, batch);
    }

    public async Task<Result<string>> SendPingAsync()
    {
        var url = $"{_config.ApiUrl.TrimEnd('/')}/managed-assets/{_config.AssetId}/ping";
        return await PostAsync("SendPing", url, new { timestamp = DateTime.UtcNow });
    }

    private async Task<Result<string>> PostAsync<T>(string operation, string url, T payload)
    {
        return await Result<string>.From(async () =>
        {
            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_config.ApiKey}");
            client.DefaultRequestHeaders.Add("X-TEMS-Asset", _config.AssetId);

            var json = JsonSerializer.Serialize(payload, JsonSerializerConfig.Default);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            logger.LogDebug("[{Op}] POST {Url}", operation, url);
            var response = await client.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                logger.LogInformation("[{Op}] ✓ {StatusCode}", operation, response.StatusCode);
                return body;
            }

            var error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            await LogErrorAsync(operation, url, error, json);
            throw new HttpRequestException(error);
        });
    }

    private async Task LogErrorAsync(string operation, string url, string error, string requestBody)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_errorLogPath)!);
            var entry = $"""
                [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ══ API ERROR ══════════════════════════
                Operation : {operation}
                URL       : {url}
                Error     : {error}
                Body      : {requestBody[..Math.Min(requestBody.Length, 500)]}
                ═══════════════════════════════════════════════════════════════

                """;
            await File.AppendAllTextAsync(_errorLogPath, entry);
            logger.LogWarning("[{Op}] ✗ {Error} → logged to {Path}", operation, error, _errorLogPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write error log");
        }
    }

    private static string GetErrorLogPath()
    {
        var dir = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TEMS")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".tems");
        return Path.Combine(dir, "api-errors.log");
    }
}