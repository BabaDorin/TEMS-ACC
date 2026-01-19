namespace TemsACC.Models;

public class SystemProperties
{
    public string SerialNumber { get; set; } = string.Empty;
    public List<string> MacAddresses { get; set; } = new();
    public string Uuid { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public CpuInfo Cpu { get; set; } = new();
    public RamInfo Ram { get; set; } = new();
    public List<StorageInfo> Storage { get; set; } = new();
    public List<GpuInfo> Gpu { get; set; } = new();
    public OsInfo Os { get; set; } = new();
    public DateTime CollectedAt { get; set; }
}

public class CpuInfo
{
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Cores { get; set; }
    public int LogicalProcessors { get; set; }
    public string Architecture { get; set; } = string.Empty;
}

public class RamInfo
{
    public double TotalCapacityGb { get; set; }
}

public class StorageInfo
{
    public string Model { get; set; } = string.Empty;
    public double SizeGb { get; set; }
    public string Type { get; set; } = string.Empty;
}

public class GpuInfo
{
    public string Model { get; set; } = string.Empty;
    public double VramMb { get; set; }
}

public class OsInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTime LastBootTime { get; set; }
    public string LastLoggedInUser { get; set; } = string.Empty;
}