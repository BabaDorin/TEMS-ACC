namespace TEMS.ACC.Models;

public sealed class MetricsSample
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    //CPU
    public double CpuLoadPercent { get; set; }
    public List<double> CpuPerCorePercent { get; set; } = [];
    public double CpuTemperatureCelsius { get; set; }
    public double LoadAverage1m { get; set; }
    public double LoadAverage5m { get; set; }
    public double LoadAverage15m { get; set; }

    //RAM
    public double RamUsedGb { get; set; }
    public double SwapUsedGb { get; set; }

    //Disk
    public double DiskFreeGb { get; set; }
    public double DiskReadMbps { get; set; }
    public double DiskWriteMbps { get; set; }

    //Network
    public long NetworkBytesSent { get; set; }
    public long NetworkBytesReceived { get; set; }
    public int ActiveNetworkConnections { get; set; }

    //GPU
    public double GpuUsagePercent { get; set; }
    public double GpuTemperatureCelsius { get; set; }

    //Battery
    public double BatteryHealthPercent { get; set; }
    public int BatteryCycleCount { get; set; }

    //Processes
    public List<ProcessInfo> TopCpuProcesses { get; set; } = [];
    public List<ProcessInfo> TopRamProcesses { get; set; } = [];
}

public sealed class ProcessInfo
{
    public string Name { get; set; } = string.Empty;
    public double CpuPercent { get; set; }
    public double RamMb { get; set; }
}