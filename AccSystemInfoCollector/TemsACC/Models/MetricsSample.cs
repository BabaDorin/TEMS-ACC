namespace TemsACC.Models;

public class MetricsSample
{
    public DateTime Timestamp { get; set; }
    public double CpuLoadPercent { get; set; }
    public double RamUsedGb { get; set; }
    public double DiskFreeGb { get; set; }
    public long NetworkBytesSent { get; set; }
    public long NetworkBytesReceived { get; set; }
    public int BatteryHealthPercent { get; set; }
    public int BatteryCycleCount { get; set; }
}