using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using TEMS.ACC.Models;

namespace TEMS.ACC.Collectors.Readers;

public sealed class WindowsPropertiesReader : BasePropertiesReader, ISystemPropertiesReader
{
    // Static properties
    public async Task<string> GetSerialNumberAsync() =>
        (await ExecuteCommandAsync("powershell", "-Command \"(Get-WmiObject Win32_BIOS).SerialNumber\""))
        .Trim().NullIfEmpty() ?? "Unknown";

    public async Task<string> GetUuidAsync() =>
        (await ExecuteCommandAsync("powershell", "-Command \"(Get-WmiObject Win32_ComputerSystemProduct).UUID\""))
        .Trim().NullIfEmpty() ?? "Unknown";

    public string GetHostname() => GetHostnameBase();
    public List<string> GetMacAddresses() => GetMacAddressesBase();

    public async Task<(string Manufacturer, string Model, int Cores, int LogicalProcessors, string Architecture, double MaxGhz, double MinGhz)> GetCpuInfoAsync()
    {
        var output = await ExecuteCommandAsync("powershell",
            "-Command \"Get-WmiObject Win32_Processor | Select-Object Manufacturer,Name,NumberOfCores,NumberOfLogicalProcessors,AddressWidth,MaxClockSpeed | ConvertTo-Json\"");
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement[0] : doc.RootElement;

            var manufacturer = root.GetProperty("Manufacturer").GetString() ?? "Unknown";
            var name         = root.GetProperty("Name").GetString() ?? "Unknown";
            int.TryParse(root.GetProperty("NumberOfCores").ToString(), out var cores);
            int.TryParse(root.GetProperty("NumberOfLogicalProcessors").ToString(), out var logical);
            var width = root.GetProperty("AddressWidth").ToString();
            var arch  = width == "64" ? "x86_64" : "x86";
            double.TryParse(root.GetProperty("MaxClockSpeed").ToString(), out var mhz);

            return (manufacturer, name, cores, logical, arch, Math.Round(mhz / 1000.0, 2), 0);
        }
        catch { return ("Unknown", "Unknown", 0, 0, "Unknown", 0, 0); }
    }

    public async Task<double> GetRamTotalGbAsync()
    {
        var output = await ExecuteCommandAsync("powershell",
            "-Command \"(Get-WmiObject Win32_ComputerSystem).TotalPhysicalMemory\"");
        if (long.TryParse(output.Trim(), out var bytes))
            return Math.Round((double)bytes / 1024 / 1024 / 1024, 2);
        return 0;
    }

    public async Task<List<RamSlot>> GetRamSlotsAsync()
    {
        var slots = new List<RamSlot>();
        var output = await ExecuteCommandAsync("powershell",
            "-Command \"Get-WmiObject Win32_PhysicalMemory | Select-Object DeviceLocator,Capacity,MemoryType,SMBIOSMemoryType,Speed,Manufacturer,PartNumber | ConvertTo-Json\"");
        try
        {
            using var doc = JsonDocument.Parse(output);
            var arr = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray()
                : Enumerable.Repeat(doc.RootElement, 1).AsEnumerable().GetEnumerator() is var _ ? doc.RootElement.EnumerateArray() : doc.RootElement.EnumerateArray();

            foreach (var item in doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray()
                : Enumerable.Repeat(doc.RootElement, 1))
            {
                var capacity = item.GetProperty("Capacity").GetInt64();
                int.TryParse(item.GetProperty("Speed").ToString(), out var speed);

                // SMBIOSMemoryType: 26=DDR4, 34=DDR5
                int.TryParse(item.GetProperty("SMBIOSMemoryType").ToString(), out var memType);
                var type = memType switch { 26 => "DDR4", 34 => "DDR5", 24 => "DDR3", _ => "Unknown" };

                slots.Add(new RamSlot
                {
                    Locator      = item.GetProperty("DeviceLocator").GetString() ?? "Unknown",
                    SizeGb       = Math.Round((double)capacity / 1024 / 1024 / 1024, 0),
                    Type         = type,
                    SpeedMhz     = speed,
                    Manufacturer = item.GetProperty("Manufacturer").GetString()?.Trim() ?? "Unknown",
                    PartNumber   = item.GetProperty("PartNumber").GetString()?.Trim() ?? "Unknown"
                });
            }
        }
        catch { }
        return slots;
    }

    public async Task<(int Total, int Used)> GetRamSlotCountAsync()
    {
        var totalOutput = await ExecuteCommandAsync("powershell",
            "-Command \"(Get-WmiObject Win32_PhysicalMemoryArray).MemoryDevices\"");
        var usedOutput = await ExecuteCommandAsync("powershell",
            "-Command \"(Get-WmiObject Win32_PhysicalMemory).Count\"");
        int.TryParse(totalOutput.Trim(), out var total);
        int.TryParse(usedOutput.Trim(), out var used);
        return (total, used);
    }

    public async Task<List<StorageDrive>> GetStorageDrivesAsync()
    {
        var drives = new List<StorageDrive>();
        var output = await ExecuteCommandAsync("powershell",
            "-Command \"Get-PhysicalDisk | Select-Object FriendlyName,Size,MediaType | ConvertTo-Json\"");
        try
        {
            using var doc = JsonDocument.Parse(output);
            foreach (var item in doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray()
                : Enumerable.Repeat(doc.RootElement, 1))
            {
                drives.Add(new StorageDrive
                {
                    Model  = item.GetProperty("FriendlyName").GetString() ?? "Unknown",
                    SizeGb = Math.Round((double)item.GetProperty("Size").GetInt64() / 1024 / 1024 / 1024, 2),
                    Type   = item.GetProperty("MediaType").GetString() ?? "Unknown"
                });
            }
        }
        catch { }
        return drives;
    }

    public async Task<List<GpuInfo>> GetGpusAsync()
    {
        var gpus = new List<GpuInfo>();
        var output = await ExecuteCommandAsync("powershell",
            "-Command \"Get-WmiObject Win32_VideoController | Select-Object Name,AdapterRAM | ConvertTo-Json\"");
        try
        {
            using var doc = JsonDocument.Parse(output);
            foreach (var item in doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray()
                : Enumerable.Repeat(doc.RootElement, 1))
            {
                gpus.Add(new GpuInfo
                {
                    Model  = item.GetProperty("Name").GetString() ?? "Unknown",
                    VramMb = item.GetProperty("AdapterRAM").GetInt64() / 1024 / 1024
                });
            }
        }
        catch { }
        return gpus;
    }

    public async Task<List<NetworkAdapterInfo>> GetNetworkAdaptersAsync()
    {
        var adapters = new List<NetworkAdapterInfo>();

        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            var mac = string.Join(":", iface.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
            var ips = iface.GetIPProperties().UnicastAddresses.Select(a => a.Address.ToString()).ToList();
            var type = iface.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Ethernet      => "Ethernet",
                NetworkInterfaceType.Wireless80211 => "WiFi",
                NetworkInterfaceType.Loopback      => "Loopback",
                _ => iface.NetworkInterfaceType.ToString()
            };

            adapters.Add(new NetworkAdapterInfo
            {
                Name        = iface.Name,
                MacAddress  = mac,
                IpAddresses = ips,
                Type        = type,
                SpeedMbps   = iface.Speed > 0 ? iface.Speed / 1_000_000 : 0,
                Driver      = "Unknown",
                IsUp        = iface.OperationalStatus == OperationalStatus.Up
            });
        }

        return adapters;
    }

    public async Task<(string Name, string Version, DateTime LastBoot, string LastUser)> GetOsInfoAsync()
    {
        var output = await ExecuteCommandAsync("powershell",
            "-Command \"Get-WmiObject Win32_OperatingSystem | Select-Object Caption,Version,LastBootUpTime | ConvertTo-Json\"");
        try
        {
            using var doc = JsonDocument.Parse(output);
            var name     = doc.RootElement.GetProperty("Caption").GetString() ?? "Windows";
            var version  = doc.RootElement.GetProperty("Version").GetString() ?? "Unknown";
            var bootStr  = doc.RootElement.GetProperty("LastBootUpTime").GetString() ?? "";
            var lastBoot = DateTime.TryParse(bootStr, out var b) ? b : DateTime.UtcNow;

            var lastUserOutput = await ExecuteCommandAsync("powershell",
                "-Command \"(Get-WmiObject Win32_ComputerSystem).UserName\"");
            var lastUser = lastUserOutput.Trim().Split('\\').Last().NullIfEmpty() ?? "Unknown";

            return (name, version, lastBoot, lastUser);
        }
        catch { return ("Windows", "Unknown", DateTime.UtcNow, "Unknown"); }
    }

    public async Task<(string Shell, string DisplayServer, string DesktopEnv, string Locale, int PackageCount)> GetSoftwareInfoAsync()
    {
        var locale = System.Globalization.CultureInfo.CurrentCulture.Name;

        // Winget installed packages count
        int packages = 0;
        var winget = await ExecuteCommandAsync("powershell",
            "-Command \"winget list 2>$null | Measure-Object -Line | Select-Object -ExpandProperty Lines\"");
        int.TryParse(winget.Trim(), out packages);

        return ("PowerShell", "Win32", "Windows Shell", locale, packages);
    }

    public async Task<(string Manufacturer, string Model, string BiosVersion, string Motherboard)> GetSystemInfoAsync()
    {
        string manufacturer = "Unknown", model = "Unknown", biosVer = "Unknown", motherboard = "Unknown";

        try
        {
            var cs = await ExecuteCommandAsync("powershell",
                "-Command \"Get-WmiObject Win32_ComputerSystem | Select-Object Manufacturer,Model | ConvertTo-Json\"");
            using var d1 = JsonDocument.Parse(cs);
            manufacturer = d1.RootElement.GetProperty("Manufacturer").GetString() ?? "Unknown";
            model        = d1.RootElement.GetProperty("Model").GetString() ?? "Unknown";
        }
        catch { }

        try
        {
            var bios = await ExecuteCommandAsync("powershell",
                "-Command \"Get-WmiObject Win32_BIOS | Select-Object SMBIOSBIOSVersion | ConvertTo-Json\"");
            using var d2 = JsonDocument.Parse(bios);
            biosVer = d2.RootElement.GetProperty("SMBIOSBIOSVersion").GetString() ?? "Unknown";
        }
        catch { }

        try
        {
            var board = await ExecuteCommandAsync("powershell",
                "-Command \"Get-WmiObject Win32_BaseBoard | Select-Object Manufacturer,Product | ConvertTo-Json\"");
            using var d3 = JsonDocument.Parse(board);
            motherboard = $"{d3.RootElement.GetProperty("Manufacturer").GetString()} {d3.RootElement.GetProperty("Product").GetString()}".Trim();
        }
        catch { }

        return (manufacturer, model, biosVer, motherboard);
    }

    public async Task<List<DisplayInfo>> GetDisplaysAsync()
    {
        var displays = new List<DisplayInfo>();
        var output = await ExecuteCommandAsync("powershell",
            "-Command \"Get-WmiObject Win32_VideoController | Select-Object CurrentHorizontalResolution,CurrentVerticalResolution | ConvertTo-Json\"");
        try
        {
            using var doc = JsonDocument.Parse(output);
            var first = true;
            foreach (var item in doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray()
                : Enumerable.Repeat(doc.RootElement, 1))
            {
                var h = item.GetProperty("CurrentHorizontalResolution").GetInt32();
                var v = item.GetProperty("CurrentVerticalResolution").GetInt32();
                displays.Add(new DisplayInfo { Resolution = $"{h}x{v}", IsPrimary = first });
                first = false;
            }
        }
        catch { }
        return displays;
    }

    public async Task<SecurityInfo> GetSecurityInfoAsync()
    {
        var security = new SecurityInfo();

        // Secure Boot
        var sbOutput = await ExecuteCommandAsync("powershell",
            "-Command \"Confirm-SecureBootUEFI 2>$null\"");
        security.SecureBootEnabled = sbOutput.Trim().ToLower() == "true";

        // TPM
        var tpmOutput = await ExecuteCommandAsync("powershell",
            "-Command \"Get-WmiObject -Namespace 'root/cimv2/security/microsofttpm' -Class Win32_Tpm | Select-Object SpecVersion | ConvertTo-Json\"");
        try
        {
            using var doc = JsonDocument.Parse(tpmOutput);
            var spec = doc.RootElement.GetProperty("SpecVersion").GetString() ?? "";
            security.TpmVersion = spec.StartsWith("2") ? "2.0" : spec.StartsWith("1") ? "1.2" : "Unknown";
        }
        catch { security.TpmVersion = "None"; }

        // BitLocker (disk encryption)
        var blOutput = await ExecuteCommandAsync("powershell",
            "-Command \"Get-BitLockerVolume -MountPoint C: 2>$null | Select-Object -ExpandProperty ProtectionStatus\"");
        security.DiskEncryptionEnabled = blOutput.Trim() == "On";

        // Firewall
        var fwOutput = await ExecuteCommandAsync("powershell",
            "-Command \"(Get-NetFirewallProfile | Where-Object Enabled -eq 'True').Count\"");
        security.FirewallActive = int.TryParse(fwOutput.Trim(), out var fwCount) && fwCount > 0;

        security.AppArmorStatus = "N/A (Windows Defender)";

        // Failed logins (last 24h)
        var failedOutput = await ExecuteCommandAsync("powershell",
            "-Command \"Get-WinEvent -FilterHashtable @{LogName='Security';Id=4625;StartTime=(Get-Date).AddHours(-24)} -ErrorAction SilentlyContinue | Measure-Object | Select-Object -ExpandProperty Count\"");
        int.TryParse(failedOutput.Trim(), out var failed);
        security.FailedLoginAttempts = failed;

        // Open ports
        var portsOutput = await ExecuteCommandAsync("powershell",
            "-Command \"Get-NetTCPConnection -State Listen | Select-Object -ExpandProperty LocalPort | Sort-Object -Unique\"");
        security.OpenPorts = portsOutput.Split('\n')
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        // SSH keys
        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        security.SshKeysPresent = Directory.Exists(sshDir) && Directory.GetFiles(sshDir, "*.pub").Length > 0;

        // Pending Windows updates
        var updatesOutput = await ExecuteCommandAsync("powershell",
            "-Command \"(New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher().Search('IsInstalled=0').Updates.Count 2>$null\"");
        int.TryParse(updatesOutput.Trim(), out var updates);
        security.PendingSecurityUpdates = updates;

        return security;
    }

    public async Task<VirtualizationInfo> GetVirtualizationInfoAsync()
    {
        var info = new VirtualizationInfo();

        var output = await ExecuteCommandAsync("powershell",
            "-Command \"(Get-WmiObject Win32_ComputerSystem).Model\"");
        var model = output.Trim().ToLower();

        if (model.Contains("virtual") || model.Contains("vmware") || model.Contains("virtualbox") || model.Contains("hyper-v"))
        {
            info.IsVirtualMachine = true;
            info.HypervisorType = model.Contains("vmware")     ? "VMware"
                                 : model.Contains("virtualbox") ? "VirtualBox"
                                 : model.Contains("hyper-v")    ? "Hyper-V"
                                 : "Unknown";
        }

        // Docker / WSL
        var wslOutput = await ExecuteCommandAsync("powershell",
            "-Command \"[System.Environment]::GetEnvironmentVariable('WSL_DISTRO_NAME')\"");
        if (!string.IsNullOrWhiteSpace(wslOutput.Trim()))
        {
            info.IsContainer = true;
            info.HypervisorType = "WSL";
        }

        return info;
    }

    // Metrics

    public double GetCpuLoadPercent()
    {
        try
        {
            using var counter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
            counter.NextValue();
            Thread.Sleep(100);
            return Math.Round((double)counter.NextValue(), 2);
        }
        catch { return 0; }
    }

    public List<double> GetPerCoreCpuPercent()
    {
        var result = new List<double>();
        try
        {
            var coreCount = Environment.ProcessorCount;
            for (int i = 0; i < coreCount; i++)
            {
                using var counter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", i.ToString());
                counter.NextValue();
                Thread.Sleep(10);
                result.Add(Math.Round((double)counter.NextValue(), 1));
            }
        }
        catch { }
        return result;
    }

    public double GetCpuTemperature()
    {
        try
        {
            var output = ExecuteCommandAsync("powershell",
                "-Command \"Get-WmiObject MSAcpi_ThermalZoneTemperature -Namespace root/wmi | Select-Object -ExpandProperty CurrentTemperature -First 1\"").Result;
            if (double.TryParse(output.Trim(), out var raw))
                return Math.Round((raw - 2732) / 10.0, 1);
        }
        catch { }
        return 0;
    }

    public (double m1, double m5, double m15) GetLoadAverage()
    {
        var cpu = GetCpuLoadPercent();
        return (cpu, cpu, cpu);
    }

    public double GetRamUsedGb()
    {
        try
        {
            using var available = new System.Diagnostics.PerformanceCounter("Memory", "Available MBytes");
            var availableMb = (double)available.NextValue();
            var totalBytes  = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return Math.Round((double)totalBytes / 1024 / 1024 / 1024 - availableMb / 1024.0, 2);
        }
        catch { return 0; }
    }

    public double GetSwapUsedGb()
    {
        try
        {
            using var counter = new System.Diagnostics.PerformanceCounter("Paging File", "% Usage", "_Total");
            counter.NextValue();
            Thread.Sleep(100);
            var pct = (double)counter.NextValue() / 100.0;
            return Math.Round(pct * 4.0, 2);
        }
        catch { return 0; }
    }

    public double GetDiskFreeGb()
    {
        try { return Math.Round((double)new DriveInfo("C").AvailableFreeSpace / 1024 / 1024 / 1024, 2); }
        catch { return 0; }
    }

    private long _lastDiskRead, _lastDiskWrite;

    public (double ReadMbps, double WriteMbps) GetDiskIo()
    {
        try
        {
            using var readCounter  = new System.Diagnostics.PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
            using var writeCounter = new System.Diagnostics.PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
            readCounter.NextValue(); writeCounter.NextValue();
            Thread.Sleep(100);
            var readMbps  = Math.Round((double)readCounter.NextValue()  / 1024 / 1024, 2);
            var writeMbps = Math.Round((double)writeCounter.NextValue() / 1024 / 1024, 2);
            return (readMbps, writeMbps);
        }
        catch { return (0, 0); }
    }

    private long _lastNetSent, _lastNetRecv;

    public (long Sent, long Received) GetNetworkBytes()
    {
        try
        {
            var ifaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
            var sent = ifaces.Sum(n => n.GetIPv4Statistics().BytesSent);
            var recv = ifaces.Sum(n => n.GetIPv4Statistics().BytesReceived);
            var ds = sent - _lastNetSent; var dr = recv - _lastNetRecv;
            _lastNetSent = sent; _lastNetRecv = recv;
            return (ds, dr);
        }
        catch { return (0, 0); }
    }

    public int GetActiveNetworkConnections()
    {
        try { return IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Length; }
        catch { return 0; }
    }

    public (double UsagePercent, double TemperatureCelsius) GetGpuMetrics()
    {
        try
        {
            // Try nvidia-smi first
            var nvOutput = ExecuteCommandAsync("nvidia-smi",
                "--query-gpu=utilization.gpu,temperature.gpu --format=csv,noheader").Result;
            if (!string.IsNullOrWhiteSpace(nvOutput))
            {
                var parts = nvOutput.Split(',');
                var usage = double.TryParse(System.Text.RegularExpressions.Regex.Match(parts[0], @"\d+").Value, out var u) ? u : 0;
                var temp  = parts.Length > 1 && double.TryParse(parts[1].Trim(), out var t) ? t : 0;
                return (usage, temp);
            }

            // Fallback: WMI GPU load
            var wmiOutput = ExecuteCommandAsync("powershell",
                "-Command \"Get-WmiObject Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine 2>$null | Select-Object -ExpandProperty UtilizationPercentage -First 1\"").Result;
            if (double.TryParse(wmiOutput.Trim(), out var load))
                return (load, 0);
        }
        catch { }
        return (0, 0);
    }

    public (double HealthPercent, int CycleCount) GetBatteryInfo()
    {
        try
        {
            var output = ExecuteCommandAsync("powershell",
                "-Command \"Get-WmiObject Win32_Battery | Select-Object EstimatedChargeRemaining,FullChargeCapacity,DesignCapacity | ConvertTo-Json\"").Result;
            using var doc = JsonDocument.Parse(output);
            var pct = doc.RootElement.GetProperty("EstimatedChargeRemaining").GetDouble();

            // Cycle count not available via WMI — would need ACPI
            return (pct, 0);
        }
        catch { return (0, 0); }
    }

    public List<ProcessInfo> GetTopCpuProcesses(int count = 5) => GetTopProcesses(count, byCpu: true);
    public List<ProcessInfo> GetTopRamProcesses(int count = 5) => GetTopProcesses(count, byCpu: false);

    private static List<ProcessInfo> GetTopProcesses(int count, bool byCpu) =>
        Process.GetProcesses()
            .OrderByDescending(p => { try { return byCpu ? p.TotalProcessorTime.TotalMilliseconds : (double)p.WorkingSet64; } catch { return 0.0; } })
            .Take(count)
            .Select(p => { try { return new ProcessInfo { Name = p.ProcessName, RamMb = Math.Round((double)p.WorkingSet64 / 1024 / 1024, 1) }; } catch { return null!; } })
            .Where(p => p is not null)
            .ToList();
}