namespace TEMS.ACC.Models;

public sealed class SystemProperties
{
    //Identity
    public string SerialNumber { get; set; } = "Unknown";
    public string Uuid { get; set; } = "Unknown";
    public string Hostname { get; set; } = "Unknown";
    public List<string> MacAddresses { get; set; } = [];

    //CPU
    public string CpuManufacturer { get; set; } = "Unknown";
    public string CpuModel { get; set; } = "Unknown";
    public int CpuCores { get; set; }
    public int CpuLogicalProcessors { get; set; }
    public string CpuArchitecture { get; set; } = "Unknown";
    public double CpuMaxFrequencyGhz { get; set; }
    public double CpuMinFrequencyGhz { get; set; }

    //RAM
    public double RamTotalGb { get; set; }
    public List<RamSlot> RamSlots { get; set; } = [];
    public int RamSlotsTotal { get; set; }
    public int RamSlotsUsed { get; set; }

    //Storage
    public List<StorageDrive> Drives { get; set; } = [];

    //GPU
    public List<GpuInfo> Gpus { get; set; } = [];

    //Network
    public List<NetworkAdapterInfo> NetworkAdapters { get; set; } = [];

    //OS
    public string OsName { get; set; } = "Unknown";
    public string OsVersion { get; set; } = "Unknown";
    public DateTime LastBootTime { get; set; }
    public string LastLoggedInUser { get; set; } = "Unknown";
    public string Timezone { get; set; } = TimeZoneInfo.Local.Id;
    public TimeSpan Uptime { get; set; }
    public string Shell { get; set; } = "Unknown";
    public string DisplayServer { get; set; } = "Unknown"; // X11 / Wayland
    public string DesktopEnvironment { get; set; } = "Unknown";
    public string Locale { get; set; } = "Unknown";
    public int InstalledPackagesCount { get; set; }

    //System board
    public string SystemManufacturer { get; set; } = "Unknown";
    public string SystemModel { get; set; } = "Unknown";
    public string BiosVersion { get; set; } = "Unknown";
    public string Motherboard { get; set; } = "Unknown";

    //Security
    public SecurityInfo Security { get; set; } = new();

    //Virtualization
    public VirtualizationInfo Virtualization { get; set; } = new();

    //Displays
    public List<DisplayInfo> Displays { get; set; } = [];

    public DateTime CollectedAt { get; set; } = DateTime.UtcNow;
}

//Sub-models

public sealed class RamSlot
{
    public string Locator { get; set; } = "Unknown";
    public double SizeGb { get; set; }
    public string Type { get; set; } = "Unknown";   // DDR4 / DDR5
    public int SpeedMhz { get; set; }
    public string Manufacturer { get; set; } = "Unknown";
    public string PartNumber { get; set; } = "Unknown";
}

public sealed class StorageDrive
{
    public string Model { get; set; } = "Unknown";
    public double SizeGb { get; set; }
    public string Type { get; set; } = "Unknown";   // SSD / HDD / NVMe
}

public sealed class GpuInfo
{
    public string Model { get; set; } = "Unknown";
    public long VramMb { get; set; }
}

public sealed class NetworkAdapterInfo
{
    public string Name { get; set; } = "Unknown";
    public string MacAddress { get; set; } = "Unknown";
    public List<string> IpAddresses { get; set; } = [];
    public string Type { get; set; } = "Unknown";   // Ethernet / WiFi / Loopback
    public long SpeedMbps { get; set; }
    public string Driver { get; set; } = "Unknown";
    public bool IsUp { get; set; }
}

public sealed class SecurityInfo
{
    public bool SecureBootEnabled { get; set; }
    public string TpmVersion { get; set; } = "Unknown"; // None / 1.2 / 2.0
    public bool DiskEncryptionEnabled { get; set; }     // LUKS
    public bool FirewallActive { get; set; }
    public string AppArmorStatus { get; set; } = "Unknown"; // enforcing / permissive / disabled
    public int FailedLoginAttempts { get; set; }
    public int PendingSecurityUpdates { get; set; }
    public List<string> OpenPorts { get; set; } = [];
    public bool SshKeysPresent { get; set; }
}

public sealed class VirtualizationInfo
{
    public bool IsVirtualMachine { get; set; }
    public string HypervisorType { get; set; } = "None"; // KVM / VMware / VirtualBox / Hyper-V / Docker / None
    public bool IsContainer { get; set; }
}

public sealed class DisplayInfo
{
    public string Resolution { get; set; } = "Unknown";
    public bool IsPrimary { get; set; }
}