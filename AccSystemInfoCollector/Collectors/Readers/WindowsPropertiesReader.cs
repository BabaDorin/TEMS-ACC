using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using System.Text.Json;
using TEMS.ACC.Models;
using TEMS.ACC.Helpers;

namespace TEMS.ACC.Collectors.Readers;

[SupportedOSPlatform("windows")]
public sealed class WindowsPropertiesReader : BasePropertiesReader, ISystemPropertiesReader
{
    // Shortcut for PowerShell commands
    private Task<string> PS(string cmd, int timeout = 10) =>
        RunAsync("powershell", $"-Command \"{cmd}\"", timeout);

    private Task<string> PSJson(string cmd, int timeout = 10) =>
        PS($"{cmd} | ConvertTo-Json", timeout);

    //Identity

    public Task<Result<string>> GetSerialNumberAsync() => Result<string>.From(async () =>
        (await PS("(Get-WmiObject Win32_BIOS).SerialNumber")).NullIfEmpty() ?? "Unknown");

    public Task<Result<string>> GetUuidAsync() => Result<string>.From(async () =>
        (await PS("(Get-WmiObject Win32_ComputerSystemProduct).UUID")).NullIfEmpty() ?? "Unknown");

    public Result<string> GetHostname() => Result<string>.From(GetHostnameBase);
    public Result<List<string>> GetMacAddresses() => Result<List<string>>.From(GetMacAddressesBase);

    //CPU

    public Task<Result<(string, string, int, int, string, double, double)>> GetCpuInfoAsync() =>
    Result<(string, string, int, int, string, double, double)>.From(async () =>
    {
        var json = await PSJson("Get-WmiObject Win32_Processor | Select-Object Manufacturer,Name,NumberOfCores,NumberOfLogicalProcessors,AddressWidth,MaxClockSpeed");
        var el = JsonArray(json).First();
        var cores = JsonInt(el, "NumberOfCores");
        var logical = JsonInt(el, "NumberOfLogicalProcessors");
        var mhz = (double)JsonInt(el, "MaxClockSpeed");
        var arch = JsonInt(el, "AddressWidth") == 64 ? "x86_64" : "x86";
        return (JsonStr(el, "Manufacturer"), JsonStr(el, "Name"), cores, logical, arch, Math.Round(mhz / 1000.0, 2), 0);
    });

    //RAM

    public Task<Result<double>> GetRamTotalGbAsync() => Result<double>.From(async () =>
    {
        var raw = await PS("(Get-WmiObject Win32_ComputerSystem).TotalPhysicalMemory");
        return long.TryParse(raw, out var bytes) ? BytesToGb(bytes) : 0;
    });

    public Task<Result<List<RamSlot>>> GetRamSlotsAsync() => Result<List<RamSlot>>.From(async () =>
    {
        var json = await PSJson("Get-WmiObject Win32_PhysicalMemory | Select-Object DeviceLocator,Capacity,SMBIOSMemoryType,Speed,Manufacturer,PartNumber");
        return JsonArray(json).Select(el =>
        {
            int.TryParse(JsonStr(el, "SMBIOSMemoryType"), out var memType);
            int.TryParse(JsonStr(el, "Speed"), out var speed);
            return new RamSlot
            {
                Locator = JsonStr(el, "DeviceLocator"),
                SizeGb = Math.Round((double)JsonLong(el, "Capacity") / 1024 / 1024 / 1024, 0),
                Type = memType switch { 26 => "DDR4", 34 => "DDR5", 24 => "DDR3", _ => "Unknown" },
                SpeedMhz = speed,
                Manufacturer = JsonStr(el, "Manufacturer").Trim(),
                PartNumber = JsonStr(el, "PartNumber").Trim()
            };
        }).ToList();
    });

    public Task<Result<(int, int)>> GetRamSlotCountAsync() => Result<(int, int)>.From(async () =>
    {
        var total = await PS("(Get-WmiObject Win32_PhysicalMemoryArray).MemoryDevices");
        var used = await PS("(Get-WmiObject Win32_PhysicalMemory).Count");
        int.TryParse(total, out var t); int.TryParse(used, out var u);
        return (t, u);
    });

    //Storage

    public Task<Result<List<StorageDrive>>> GetStorageDrivesAsync() => Result<List<StorageDrive>>.From(async () =>
    {
        var json = await PSJson("Get-PhysicalDisk | Select-Object FriendlyName,Size,MediaType");
        return JsonArray(json).Select(el => new StorageDrive
        {
            Model = JsonStr(el, "FriendlyName"),
            SizeGb = BytesToGb(JsonLong(el, "Size")),
            Type = JsonStr(el, "MediaType")
        }).ToList();
    });

    //GPU

    public Task<Result<List<GpuInfo>>> GetGpusAsync() => Result<List<GpuInfo>>.From(async () =>
    {
        // nvidia-smi
        var nv = await RunAsync("nvidia-smi", "--query-gpu=name,memory.total --format=csv,noheader");
        if (!string.IsNullOrWhiteSpace(nv))
            return nv.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).Select(line =>
            {
                var p = line.Split(',');
                return new GpuInfo { Model = p[0].Trim(), VramMb = long.TryParse(RegexMatch(p.Length > 1 ? p[1] : "", @"\d+"), out var v) ? v : 0 };
            }).ToList();

        // Fallback: WMI (not accurate for > 4GB, but better than nothing)
        var json = await PSJson("Get-WmiObject Win32_VideoController | Select-Object Name,AdapterRAM,AdapterDACType");
        return JsonArray(json).Select(el => new GpuInfo
        {
            Model = JsonStr(el, "Name"),
            VramMb = JsonLong(el, "AdapterRAM") / 1024 / 1024
        }).ToList();
    });

    //Network

    public Task<Result<List<NetworkAdapterInfo>>> GetNetworkAdaptersAsync() =>
    Result<List<NetworkAdapterInfo>>.From(() => Task.FromResult(
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(iface =>
                !iface.Name.Contains("WFP", StringComparison.OrdinalIgnoreCase) &&
                !iface.Name.Contains("QoS", StringComparison.OrdinalIgnoreCase) &&
                !iface.Name.Contains("Filter", StringComparison.OrdinalIgnoreCase) &&
                !iface.Name.Contains("Pseudo", StringComparison.OrdinalIgnoreCase) &&
                !iface.Name.Contains("Miniport", StringComparison.OrdinalIgnoreCase) &&
                iface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(iface => new NetworkAdapterInfo
            {
                Name = iface.Name,
                MacAddress = string.Join(":", iface.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2"))),
                IpAddresses = iface.GetIPProperties().UnicastAddresses
                                   .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) // только IPv4
                                   .Select(a => a.Address.ToString()).ToList(),
                Type = iface.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Ethernet => "Ethernet",
                    NetworkInterfaceType.Wireless80211 => "WiFi",
                    NetworkInterfaceType.Tunnel => "Tunnel",
                    _ => iface.NetworkInterfaceType.ToString()
                },
                SpeedMbps = iface.Speed > 0 ? iface.Speed / 1_000_000 : 0,
                Driver = "Unknown",
                IsUp = iface.OperationalStatus == OperationalStatus.Up
            }).ToList()));

    //OS

    public Task<Result<(string, string, DateTime, string)>> GetOsInfoAsync() => Result<(string, string, DateTime, string)>.From(async () =>
    {
        var json = await PSJson("Get-WmiObject Win32_OperatingSystem | Select-Object Caption,Version,LastBootUpTime");
        var el = JsonArray(json).First();
        var lastBoot = DateTime.TryParse(JsonStr(el, "LastBootUpTime"), out var b) ? b : DateTime.UtcNow;
        var user = (await PS("(Get-WmiObject Win32_ComputerSystem).UserName")).Split('\\').Last().NullIfEmpty() ?? "Unknown";
        return (JsonStr(el, "Caption"), JsonStr(el, "Version"), lastBoot, user);
    });

    public Task<Result<(string, string, string, string, int)>> GetSoftwareInfoAsync() => Result<(string, string, string, string, int)>.From(async () =>
    {
        var locale = System.Globalization.CultureInfo.CurrentCulture.Name;
        var winget = await PS("winget list --disable-interactivity 2>$null | Measure-Object -Line | Select-Object -ExpandProperty Lines", timeout: 20);
        
        int.TryParse(winget.Trim(), out var total);
        var packages = Math.Max(0, total - 2);
        return ("PowerShell", "Win32", "Windows Shell", locale, packages);
    });

    //System

    public Task<Result<(string, string, string, string)>> GetSystemInfoAsync() => Result<(string, string, string, string)>.From(async () =>
    {
        var cs = JsonArray(await PSJson("Get-WmiObject Win32_ComputerSystem | Select-Object Manufacturer,Model")).First();
        var bios = JsonArray(await PSJson("Get-WmiObject Win32_BIOS | Select-Object SMBIOSBIOSVersion")).First();
        var board = JsonArray(await PSJson("Get-WmiObject Win32_BaseBoard | Select-Object Manufacturer,Product")).First();
        return (JsonStr(cs, "Manufacturer"), JsonStr(cs, "Model"), JsonStr(bios, "SMBIOSBIOSVersion"),
                $"{JsonStr(board, "Manufacturer")} {JsonStr(board, "Product")}".Trim());
    });

    //Displays

    public Task<Result<List<DisplayInfo>>> GetDisplaysAsync() => Result<List<DisplayInfo>>.From(async () =>
    {
        var json = await PSJson("Get-WmiObject Win32_VideoController | Select-Object CurrentHorizontalResolution,CurrentVerticalResolution");
        var first = true;
        return JsonArray(json).Select(el =>
        {
            var d = new DisplayInfo { Resolution = $"{JsonInt(el, "CurrentHorizontalResolution")}x{JsonInt(el, "CurrentVerticalResolution")}", IsPrimary = first };
            first = false;
            return d;
        }).ToList();
    });

    //Security

    public Task<Result<SecurityInfo>> GetSecurityInfoAsync() => Result<SecurityInfo>.From(async () =>
    {
        var s = new SecurityInfo();

        s.SecureBootEnabled = (await PS("Confirm-SecureBootUEFI 2>$null", timeout: 5)).Trim().ToLower() == "true";

        var tpmJson = await PS("Get-WmiObject -Namespace 'root/cimv2/security/microsofttpm' -Class Win32_Tpm | Select-Object SpecVersion | ConvertTo-Json 2>$null", timeout: 5);
        if (!string.IsNullOrWhiteSpace(tpmJson))
        {
            var spec = JsonStr(JsonArray(tpmJson).First(), "SpecVersion");
            s.TpmVersion = spec.StartsWith("2") ? "2.0" : spec.StartsWith("1") ? "1.2" : "None";
        }

        s.DiskEncryptionEnabled = (await PS("(Get-BitLockerVolume -MountPoint C: 2>$null).ProtectionStatus", timeout: 10)).Trim() == "On";
        s.FirewallActive = int.TryParse((await PS("(Get-NetFirewallProfile | Where-Object { $_.Enabled -eq 'True' }).Count", timeout: 5)), out var fw) && fw > 0;
        s.AppArmorStatus = "N/A (Windows Defender)";

        var failed = await PS("(Get-WinEvent -FilterHashtable @{LogName='Security';Id=4625;StartTime=(Get-Date).AddHours(-24)} -ErrorAction SilentlyContinue | Measure-Object).Count", timeout: 10);
        int.TryParse(failed, out var failedCount);
        s.FailedLoginAttempts = failedCount;

        s.OpenPorts = (await PS("Get-NetTCPConnection -State Listen | Select-Object -ExpandProperty LocalPort | Sort-Object -Unique", timeout: 5))
            .Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).Distinct().OrderBy(p => p).ToList();

        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        s.SshKeysPresent = Directory.Exists(sshDir) && Directory.GetFiles(sshDir, "*.pub").Length > 0;

        var updates = await PS("(New-Object -ComObject Microsoft.Update.Session).CreateUpdateSearcher().Search('IsInstalled=0').Updates.Count 2>$null", timeout: 30);
        int.TryParse(updates, out var updateCount);
        s.PendingSecurityUpdates = updateCount;

        return s;
    });

    //Virtualization

    public Task<Result<VirtualizationInfo>> GetVirtualizationInfoAsync() => Result<VirtualizationInfo>.From(async () =>
    {
        var model = (await PS("(Get-WmiObject Win32_ComputerSystem).Model")).ToLower();
        var info = new VirtualizationInfo();

        if (model.Contains("virtual") || model.Contains("vmware") || model.Contains("virtualbox"))
        {
            info.IsVirtualMachine = true;
            info.HypervisorType = model.Contains("vmware") ? "VMware" : model.Contains("virtualbox") ? "VirtualBox" : "Hyper-V";
        }

        var wsl = await PS("[System.Environment]::GetEnvironmentVariable('WSL_DISTRO_NAME')");
        if (!string.IsNullOrWhiteSpace(wsl)) { info.IsContainer = true; info.HypervisorType = "WSL"; }

        return info;
    });

    //Metrics

    private long _lastNetSent, _lastNetRecv;

    public double GetCpuLoadPercent() => Result<double>.From(() =>
    {
        using var c = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        c.NextValue(); Thread.Sleep(100);
        return Math.Round((double)c.NextValue(), 2);
    }).GetValueOrDefault(0);

    public List<double> GetPerCoreCpuPercent() => Result<List<double>>.From(() =>
        Enumerable.Range(0, Environment.ProcessorCount).Select(i =>
        {
            using var c = new PerformanceCounter("Processor", "% Processor Time", i.ToString());
            c.NextValue(); Thread.Sleep(10);
            return Math.Round((double)c.NextValue(), 1);
        }).ToList()).GetValueOrDefault([]);

    public double GetCpuTemperature() => Result<double>.From(() =>
    {
        var raw = RunAsync("powershell", "-Command \"Get-WmiObject MSAcpi_ThermalZoneTemperature -Namespace root/wmi | Select-Object -ExpandProperty CurrentTemperature -First 1\"").Result;
        return double.TryParse(raw, out var t) ? Math.Round((t - 2732) / 10.0, 1) : 0;
    }).GetValueOrDefault(0);

    public (double, double, double) GetLoadAverage() { var c = GetCpuLoadPercent(); return (c, c, c); }

    public double GetRamUsedGb() => Result<double>.From(() =>
    {
        using var c = new PerformanceCounter("Memory", "Available MBytes");
        return Math.Round((double)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024 / 1024 - (double)c.NextValue() / 1024, 2);
    }).GetValueOrDefault(0);

    public double GetSwapUsedGb() => Result<double>.From(() =>
    {
        using var c = new PerformanceCounter("Paging File", "% Usage", "_Total");
        c.NextValue(); Thread.Sleep(100);
        return Math.Round((double)c.NextValue() / 100.0 * 4.0, 2);
    }).GetValueOrDefault(0);

    public double GetDiskFreeGb() =>
        Result<double>.From(() => BytesToGb(new DriveInfo("C").AvailableFreeSpace)).GetValueOrDefault(0);

    public (double, double) GetDiskIo() => Result<(double, double)>.From(() =>
    {
        using var r = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
        using var w = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
        r.NextValue(); w.NextValue(); Thread.Sleep(100);
        return (Math.Round((double)r.NextValue() / 1024 / 1024, 2),
                Math.Round((double)w.NextValue() / 1024 / 1024, 2));
    }).GetValueOrDefault((0, 0));

    public (long, long) GetNetworkBytes() => Result<(long, long)>.From(() =>
    {
        var ifaces = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
        var (sent, recv) = (ifaces.Sum(n => n.GetIPv4Statistics().BytesSent), ifaces.Sum(n => n.GetIPv4Statistics().BytesReceived));
        var result = (sent - _lastNetSent, recv - _lastNetRecv);
        (_lastNetSent, _lastNetRecv) = (sent, recv);
        return result;
    }).GetValueOrDefault((0, 0));

    public int GetActiveNetworkConnections() =>
        Result<int>.From(() => IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Length).GetValueOrDefault(0);

    public (double, double) GetGpuMetrics() => Result<(double, double)>.From(() =>
    {
        var nv = RunAsync("nvidia-smi", "--query-gpu=utilization.gpu,temperature.gpu --format=csv,noheader").Result;
        if (string.IsNullOrWhiteSpace(nv)) return (0, 0);
        var p = nv.Split(',');
        return (double.TryParse(RegexMatch(p[0], @"\d+"), out var u) ? u : 0,
                p.Length > 1 && double.TryParse(p[1].Trim(), out var t) ? t : 0);
    }).GetValueOrDefault((0, 0));

    public (double, int) GetBatteryInfo() => Result<(double, int)>.From(() =>
    {
        var json = RunAsync("powershell", "-Command \"Get-WmiObject Win32_Battery | Select-Object EstimatedChargeRemaining | ConvertTo-Json\"").Result;
        var el = JsonArray(json).First();
        return (el.TryGetProperty("EstimatedChargeRemaining", out var v) ? v.GetDouble() : 0, 0);
    }).GetValueOrDefault((0, 0));

    public List<ProcessInfo> GetTopCpuProcesses(int count = 5) =>
        Result<List<ProcessInfo>>.From(() => Process.GetProcesses()
            .OrderByDescending(p => p.TotalProcessorTime.TotalMilliseconds).Take(count)
            .Select(p => new ProcessInfo { Name = p.ProcessName, RamMb = Math.Round((double)p.WorkingSet64 / 1024 / 1024, 1) })
            .ToList()).GetValueOrDefault([]);

    public List<ProcessInfo> GetTopRamProcesses(int count = 5) =>
        Result<List<ProcessInfo>>.From(() => Process.GetProcesses()
            .OrderByDescending(p => p.WorkingSet64).Take(count)
            .Select(p => new ProcessInfo { Name = p.ProcessName, RamMb = Math.Round((double)p.WorkingSet64 / 1024 / 1024, 1) })
            .ToList()).GetValueOrDefault([]);
}