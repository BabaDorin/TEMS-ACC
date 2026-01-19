using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using TemsACC.Configuration;
using TemsACC.Models;

namespace TemsACC.ApiClient;

public class TemsApiClient : ITemsApiClient
{
    private readonly ILogger<TemsApiClient> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigurationManager _configManager;
    private readonly JsonSerializerOptions _jsonOptions;

    public TemsApiClient(
        ILogger<TemsApiClient> logger,
        IHttpClientFactory httpClientFactory,
        ConfigurationManager configManager)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _configManager = configManager;
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
    }

    public async Task SendPropertiesAsync(SystemProperties properties)
    {
        try
        {
            var config = _configManager.Config;
            var url = config.ApiUrl.Contains("httpbin.org")
                ? config.ApiUrl
                : $"{config.ApiUrl.TrimEnd('/')}/managed-assets/{config.AssetId}/properties";

            _logger.LogInformation("Sending properties to {Url}", url);

            var json = JsonSerializer.Serialize(properties, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };

            if (!string.IsNullOrEmpty(config.ApiKey))
            {
                request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
            }

            var response = await client.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Properties sent successfully. Status: {StatusCode}", response.StatusCode);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to send properties. Status: {StatusCode}, Response: {Response}", 
                    response.StatusCode, errorContent);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while sending properties");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send properties");
            throw;
        }
    }

    public async Task SendMetricsAsync(MetricsBatch batch)
    {
        try
        {
            var config = _configManager.Config;
            var url = config.ApiUrl.Contains("httpbin.org")
                ? config.ApiUrl
                : $"{config.ApiUrl.TrimEnd('/')}/managed-assets/{config.AssetId}/metrics";

            _logger.LogInformation("Sending metrics batch to {Url} with {Count} samples", url, batch.Samples.Count);

            var json = JsonSerializer.Serialize(batch, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };

            if (!string.IsNullOrEmpty(config.ApiKey))
            {
                request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
            }

            var response = await client.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Metrics batch sent successfully. Status: {StatusCode}", response.StatusCode);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to send metrics. Status: {StatusCode}, Response: {Response}", 
                    response.StatusCode, errorContent);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while sending metrics");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send metrics");
            throw;
        }
    }

    public async Task SendPingAsync()
    {
        try
        {
            var config = _configManager.Config;
            var url = $"{config.ApiUrl.TrimEnd('/')}/managed-assets/{config.AssetId}/ping";

            var client = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, url);

            if (!string.IsNullOrEmpty(config.ApiKey))
            {
                request.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
            }

            var response = await client.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Ping sent successfully");
            }
            else
            {
                _logger.LogWarning("Failed to send ping. Status: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send ping");
            throw;
        }
    }
}