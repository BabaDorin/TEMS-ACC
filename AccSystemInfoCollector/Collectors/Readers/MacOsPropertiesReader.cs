using System.Diagnostics;
using System.Net.NetworkInformation;
using TEMS.ACC.Models;
using TEMS.ACC.Helpers;

namespace TEMS.ACC.Collectors.Readers;

public sealed class MacOsPropertiesReader : BasePropertiesReader, ISystemPropertiesReader
{
    private Task<string> Profiler(string dataType) => RunAsync("system_profiler", dataType);

    //Identity
    public Task<Result<string>> GetSerialNumberAsync() => Result<string>.From(async () =>
        (await Profiler("SPHardwareDataType")).Lines().Find("Serial Number")?.After(':').Trim() ?? "Unknown");

    public Task<Result<string>> GetUuidAsync() => Result<string>.From(async () =>
        (await Profiler("SPHardwareDataType")).Lines().Find("Hardware UUID", "Provisioning UDID")?.After(':').Trim() ?? "Unknown");

    public Result<string> GetHostname() => Result<string>.From(GetHostnameBase);
    public Result<List<string>> GetMacAddresses() => Result<List<string>>.From(GetMacAddressesBase);

    // CPU
    public Task<Result<(string, string, int, int, string, double, double)>> GetCpuInfoAsync() =>
        Result<(string, string, int, int, string, double, double)>.From(async () =>
        {
            var lines = (await Profiler("SPHardwareDataType")).Lines();
            var chip = lines.Find("Chip:", "Processor Name:")?.After(':').Trim() ?? "Unknown";
            int.TryParse(lines.Find("Total Number of Cores:")?.After(':').Trim().Split(' ')[0], out var cores);
            var speedStr = lines.Find("Processor Speed:")?.After(':').Trim() ?? "";
            double maxGhz = speedStr.Contains("GHz") && double.TryParse(speedStr.Replace("GHz", "").Trim(),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var g) ? g : 0;
            var arch = await RunAsync("uname", "-m");
            return ("Apple", chip, cores, cores, arch, maxGhz, 0);
        });

    //RAM
    public Task<Result<double>> GetRamTotalGbAsync() => Result<double>.From(async () =>
    {
        var mem = (await Profiler("SPHardwareDataType")).Lines().Find("Memory:")?.After(':').Trim() ?? "0 GB";
        var p = mem.Split(' ');
        if (!double.TryParse(p[0], out var val)) return 0;
        return p.Length > 1 && p[1].Equals("GB", StringComparison.OrdinalIgnoreCase) ? val
             : p.Length > 1 && p[1].Equals("MB", StringComparison.OrdinalIgnoreCase) ? Math.Round(val / 1024.0, 2) : 0;
    });

    public Task<Result<List<RamSlot>>> GetRamSlotsAsync() => Result<List<RamSlot>>.From(async () =>
        (await Profiler("SPMemoryDataType")).Blocks().Where(b => b.Contains("Size:"))
            .Select(b =>
            {
                var lines = b.Split('\n');
                string Get(string k) => lines.FirstOrDefault(l => l.TrimStart().StartsWith(k))?.After(':').Trim() ?? "Unknown";
                var sizeStr = Get("Size:");
                if (sizeStr is "Empty" or "Unknown") return null;
                double gb = sizeStr.EndsWith("GB") && double.TryParse(sizeStr.Replace("GB", "").Trim(), out var g) ? g
                          : sizeStr.EndsWith("MB") && double.TryParse(sizeStr.Replace("MB", "").Trim(), out var m) ? Math.Round(m / 1024.0, 2) : 0;
                int.TryParse(RegexMatch(Get("Speed:"), @"\d+"), out var speed);
                return new RamSlot { Locator = Get("BANK"), SizeGb = gb, Type = Get("Type:"), SpeedMhz = speed, Manufacturer = Get("Manufacturer:"), PartNumber = Get("Part Number:") };
            })
            .Where(s => s is not null).Cast<RamSlot>().ToList());

    public Task<Result<(int, int)>> GetRamSlotCountAsync() => Result<(int, int)>.From(async () =>
    {
        var output = await Profiler("SPMemoryDataType");
        var total = System.Text.RegularExpressions.Regex.Matches(output, "BANK").Count;
        var used = output.Blocks().Count(b => b.Contains("Size:") && !b.Contains("Empty"));
        return (total > 0 ? total : used, used);
    });

    //Storage
    public Task<Result<List<StorageDrive>>> GetStorageDrivesAsync() => Result<List<StorageDrive>>.From(async () =>
        (await Profiler("SPStorageDataType")).Blocks().Where(b => b.Contains("Capacity:"))
            .Select(b =>
            {
                var lines = b.Split('\n');
                string Get(string k) => lines.FirstOrDefault(l => l.TrimStart().StartsWith(k))?.After(':').Trim() ?? "Unknown";
                var medium = Get("Medium Type:");
                return new StorageDrive
                {
                    Model = Get("Volume Name:").NullIfEmpty() ?? Get("Physical Drive:"),
                    SizeGb = ParseSizeToGb(Get("Capacity:")),
                    Type = medium.Contains("SSD") || medium.Contains("Flash") ? "SSD" : medium.Contains("HDD") ? "HDD" : "SSD"
                };
            }).ToList());

    //GPU
    public Task<Result<List<GpuInfo>>> GetGpusAsync() => Result<List<GpuInfo>>.From(async () =>
        (await Profiler("SPDisplaysDataType")).Blocks().Where(b => b.Contains("Chipset Model:"))
            .Select(b =>
            {
                var lines = b.Split('\n');
                return new GpuInfo
                {
                    Model = lines.FirstOrDefault(l => l.Contains("Chipset Model:"))?.After(':').Trim() ?? "Unknown",
                    VramMb = ParseVramToMb(lines.FirstOrDefault(l => l.Contains("VRAM"))?.After(':').Trim() ?? "0 MB")
                };
            }).ToList());

    //Network
    public Task<Result<List<NetworkAdapterInfo>>> GetNetworkAdaptersAsync() =>
        Result<List<NetworkAdapterInfo>>.From(() => Task.FromResult(
            NetworkInterface.GetAllNetworkInterfaces().Select(iface => new NetworkAdapterInfo
            {
                Name = iface.Name,
                MacAddress = string.Join(":", iface.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2"))),
                IpAddresses = iface.GetIPProperties().UnicastAddresses.Select(a => a.Address.ToString()).ToList(),
                Type = iface.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Ethernet => "Ethernet",
                    NetworkInterfaceType.Wireless80211 => "WiFi",
                    NetworkInterfaceType.Loopback => "Loopback",
                    _ => iface.NetworkInterfaceType.ToString()
                },
                SpeedMbps = iface.Speed > 0 ? iface.Speed / 1_000_000 : 0,
                Driver = "Unknown",
                IsUp = iface.OperationalStatus == OperationalStatus.Up
            }).ToList()));

    //OS
    public Task<Result<(string, string, DateTime, string)>> GetOsInfoAsync() => Result<(string, string, DateTime, string)>.From(async () =>
    {
        var sw = (await RunAsync("sw_vers", "")).Lines();
        var name = sw.Find("ProductName:")?.After(':').Trim() ?? "macOS";
        var version = sw.Find("ProductVersion:")?.After(':').Trim() ?? "Unknown";
        var boot = await RunAsync("sysctl", "-n kern.boottime");
        var lastBoot = long.TryParse(RegexMatch(boot, @"sec = (\d+)"), out var epoch)
            ? DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime : DateTime.UtcNow;
        var lastUser = (await RunAsync("last", "-1")).Split('\n').FirstOrDefault()?.Split(' ').FirstOrDefault() ?? "Unknown";
        return ($"{name} {version}", version, lastBoot, lastUser);
    });

    public Task<Result<(string, string, string, string, int)>> GetSoftwareInfoAsync() => Result<(string, string, string, string, int)>.From(async () =>
    {
        var shell = Environment.GetEnvironmentVariable("SHELL")?.Split('/').Last() ?? "Unknown";
        var locale = (await RunAsync("defaults", "read NSGlobalDomain AppleLocale")).NullIfEmpty() ?? "Unknown";
        var brew = await RunAsync("brew", "list --formula");
        var packages = string.IsNullOrWhiteSpace(brew) ? 0 : brew.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));
        return (shell, "Quartz Compositor", "macOS Aqua", locale, packages);
    });

    //System
    public Task<Result<(string, string, string, string)>> GetSystemInfoAsync() => Result<(string, string, string, string)>.From(async () =>
    {
        var lines = (await Profiler("SPHardwareDataType")).Lines();
        return ("Apple",
                lines.Find("Model Name:")?.After(':').Trim() ?? "Unknown",
                lines.Find("Boot ROM Version:")?.After(':').Trim() ?? "Unknown",
                lines.Find("Model Identifier:")?.After(':').Trim() ?? "Unknown");
    });

    //Displays
    public Task<Result<List<DisplayInfo>>> GetDisplaysAsync() => Result<List<DisplayInfo>>.From(async () =>
    {
        var first = true;
        return (await Profiler("SPDisplaysDataType")).Blocks()
            .Select(b =>
            {
                var resLine = b.Split('\n').FirstOrDefault(l => l.Contains("Resolution:"));
                if (resLine is null) return null;
                var d = new DisplayInfo { Resolution = resLine.After(':').Trim().Split('@')[0].Trim().Replace(" x ", "x"), IsPrimary = first };
                first = false;
                return d;
            })
            .Where(d => d is not null).Cast<DisplayInfo>().ToList();
    });

    //Security
    public Task<Result<SecurityInfo>> GetSecurityInfoAsync() => Result<SecurityInfo>.From(async () =>
    {
        var s = new SecurityInfo();
        var sb = await Profiler("SPiBridgeDataType");
        s.SecureBootEnabled = sb.Contains("Full Security") || sb.Contains("Reduced Security");
        s.TpmVersion = sb.Contains("Apple T2") ? "T2 (Apple)" : "Secure Enclave";
        s.DiskEncryptionEnabled = (await RunAsync("fdesetup", "status")).Contains("On");
        s.FirewallActive = (await RunAsync("defaults", "read /Library/Preferences/com.apple.alf globalstate")).Trim() is "1" or "2";
        s.AppArmorStatus = "N/A (macOS)";
        s.OpenPorts = (await RunAsync("lsof", "-nP -iTCP -sTCP:LISTEN")).Split('\n').Skip(1)
            .Select(l => RegexMatch(l, @":(\d+)\s*\(")).Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct().OrderBy(x => x).ToList();
        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        s.SshKeysPresent = Directory.Exists(sshDir) && Directory.GetFiles(sshDir, "*.pub").Length > 0;
        s.PendingSecurityUpdates = (await RunAsync("softwareupdate", "-l")).Split('\n').Count(l => l.TrimStart().StartsWith("*"));
        return s;
    });

    //Virtualization
    public Task<Result<VirtualizationInfo>> GetVirtualizationInfoAsync() => Result<VirtualizationInfo>.From(async () =>
    {
        var info = new VirtualizationInfo();
        if ((await RunAsync("sysctl", "-n kern.hv_vmm_present")).Trim() == "1")
            info.IsVirtualMachine = true;
        if (File.Exists("/.dockerenv")) { info.IsContainer = true; info.HypervisorType = "Docker"; }
        return info;
    });

    //Metrics
    private long _lastDiskRead, _lastDiskWrite, _lastNetSent, _lastNetRecv;

    public double GetCpuLoadPercent() => Result<double>.From(() =>
    {
        var output = RunAsync("bash", "-c \"top -l 1 -s 0 | grep 'CPU usage'\"").Result;
        var m = System.Text.RegularExpressions.Regex.Match(output, @"([\d.]+)% user.*?([\d.]+)% sys");
        if (!m.Success) return 0;
        double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var user);
        double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sys);
        return Math.Round(user + sys, 2);
    }).GetValueOrDefault(0);

    public List<double> GetPerCoreCpuPercent() => [];
    public double GetCpuTemperature() => 0;

    public (double, double, double) GetLoadAverage() => Result<(double, double, double)>.From(() =>
    {
        var m = System.Text.RegularExpressions.Regex.Match(
            RunAsync("sysctl", "-n vm.loadavg").Result, @"{ ([\d.]+) ([\d.]+) ([\d.]+) }");
        if (!m.Success) return (0, 0, 0);
        double P(int i) => double.TryParse(m.Groups[i].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        return (P(1), P(2), P(3));
    }).GetValueOrDefault((0, 0, 0));

    public double GetRamUsedGb() => Result<double>.From(() =>
    {
        var lines = RunAsync("vm_stat", "").Result.Split('\n');
        long Get(string key)
        {
            var l = lines.FirstOrDefault(x => x.Contains(key));
            return l is not null && long.TryParse(RegexMatch(l, @"\d+"), out var v) ? v : 0;
        }
        return Math.Round((Get("Pages active:") + Get("Pages wired down:") + Get("Pages occupied by compressor:")) * 4096L / 1024.0 / 1024.0 / 1024.0, 2);
    }).GetValueOrDefault(0);

    public double GetSwapUsedGb() => Result<double>.From(() =>
    {
        var m = System.Text.RegularExpressions.Regex.Match(RunAsync("sysctl", "-n vm.swapusage").Result, @"used = ([\d.]+)M");
        return m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mb)
            ? Math.Round(mb / 1024.0, 2) : 0;
    }).GetValueOrDefault(0);

    public double GetDiskFreeGb() =>
        Result<double>.From(() => BytesToGb(new DriveInfo("/").AvailableFreeSpace)).GetValueOrDefault(0);

    public (double, double) GetDiskIo() => Result<(double, double)>.From(() =>
    {
        var line = RunAsync("iostat", "-d -K disk0").Result.Split('\n')
            .LastOrDefault(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("KB"));
        if (line is null) return (0, 0);
        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 3) return (0, 0);
        long.TryParse(p[1], out var read); long.TryParse(p[2], out var write);
        var result = (Math.Max(0, Math.Round((double)(read - _lastDiskRead) / 1024, 2)),
                      Math.Max(0, Math.Round((double)(write - _lastDiskWrite) / 1024, 2)));
        (_lastDiskRead, _lastDiskWrite) = (read, write);
        return result;
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
        Result<int>.From(() => System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Length)
        .GetValueOrDefault(0);

    public (double, double) GetGpuMetrics() => (0, 0);

    public (double, int) GetBatteryInfo() => Result<(double, int)>.From(() =>
    {
        var pct = RegexMatch(RunAsync("pmset", "-g batt").Result, @"(\d+)%;");
        var cycle = RegexMatch(RunAsync("system_profiler", "SPPowerDataType").Result, @"Cycle Count: (\d+)");
        double.TryParse(pct, out var p); int.TryParse(cycle, out var c);
        return (p, c);
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