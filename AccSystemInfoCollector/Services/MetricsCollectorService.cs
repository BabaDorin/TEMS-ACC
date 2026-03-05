using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TEMS.ACC.ApiClient;
using TEMS.ACC.Collectors;
using TEMS.ACC.Models;
using TEMS.ACC.Storage;

namespace TEMS.ACC.Services;

public sealed class MetricsCollectorService(
    ILogger<MetricsCollectorService> logger,
    ISystemInfoCollector collector,
    ITemsApiClient apiClient,
    IMetricsStore store,
    IOptions<TemsConfiguration> config) : BackgroundService
{
    private readonly TimeSpan _sampleInterval = TimeSpan.FromSeconds(config.Value.MetricsSampleIntervalSeconds);
    private readonly TimeSpan _batchInterval  = TimeSpan.FromMinutes(config.Value.MetricsBatchIntervalMinutes);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MetricsCollectorService started. Sample: {S}s · Batch: {B}min",
            _sampleInterval.TotalSeconds, _batchInterval.TotalMinutes);

        var samplingTask = RunSamplingLoopAsync(stoppingToken);
        var batchingTask = RunBatchingLoopAsync(stoppingToken);

        await Task.WhenAll(samplingTask, batchingTask);
    }

    private async Task RunSamplingLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_sampleInterval);
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var sample = collector.CollectMetricsSample();
                store.AddSample(sample);

                logger.LogDebug(
                    "📊 CPU {Cpu}% | RAM {Ram} GB | Disk {Disk} GB free | NET ↑{Sent} ↓{Recv} B | BAT {Bat}%",
                    sample.CpuLoadPercent, sample.RamUsedGb, sample.DiskFreeGb,
                    sample.NetworkBytesSent, sample.NetworkBytesReceived,
                    sample.BatteryHealthPercent);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Error during metrics sampling"); }
        }
    }

    private async Task RunBatchingLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_batchInterval);
        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            await SendBatchAsync(ct);
        }
    }

    private async Task SendBatchAsync(CancellationToken ct)
    {
        var samples = store.GetAndClearSamples();
        if (samples.Count == 0)
        {
            logger.LogDebug("No metrics samples to send.");
            return;
        }

        var batch = new MetricsBatch
        {
            BatchStart = samples.Min(s => s.Timestamp),
            BatchEnd   = samples.Max(s => s.Timestamp),
            Samples    = samples
        };

        logger.LogInformation("Sending batch of {Count} samples ({Start} → {End})",
            batch.Samples.Count, batch.BatchStart, batch.BatchEnd);

        var result = await apiClient.SendMetricsAsync(batch);

        if (result.IsSuccess)
        {
            logger.LogInformation("✓ Batch sent successfully.");
        }
        else
        {
            foreach (var s in samples) store.AddSample(s);
            logger.LogWarning("✗ Batch send failed: {Error}. Samples retained ({Count}).", result.Error, samples.Count);
        }
    }
}