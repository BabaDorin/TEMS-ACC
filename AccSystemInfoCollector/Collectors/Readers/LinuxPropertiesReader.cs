using System.Diagnostics;
using System.Net.NetworkInformation;
using TEMS.ACC.Models;
using TEMS.ACC.Helpers;

namespace TEMS.ACC.Collectors.Readers;

public sealed class LinuxPropertiesReader : BasePropertiesReader, ISystemPropertiesReader
{
    //Identity

    public Task<Result<string>> GetSerialNumberAsync() => Result<string>.From(async () =>
        (await ReadAsync("/sys/class/dmi/id/product_serial")).NullIfEmpty()
        ?? (await RunAsync("dmidecode", "-s system-serial-number")).NullIfEmpty()
        ?? "Unknown");

    public Task<Result<string>> GetUuidAsync() => Result<string>.From(async () =>
        (await ReadAsync("/sys/class/dmi/id/product_uuid")).NullIfEmpty()
        ?? (await RunAsync("dmidecode", "-s system-uuid")).NullIfEmpty()
        ?? "Unknown");

    public Result<string> GetHostname() => Result<string>.From(GetHostnameBase);
    public Result<List<string>> GetMacAddresses() => Result<List<string>>.From(GetMacAddressesBase);

    //CPU

    public Task<Result<(string, string, int, int, string, double, double)>> GetCpuInfoAsync() =>
        Result<(string, string, int, int, string, double, double)>.From(async () =>
        {
            var info = (await ReadAsync("/proc/cpuinfo")).Split('\n');
            string Line(string key) => info.FirstOrDefault(l => l.StartsWith(key))?.Split(':').Last().Trim() ?? "Unknown";

            var model = Line("model name");
            var vendor = Line("vendor_id");
            var logical = info.Count(l => l.StartsWith("processor"));
            int.TryParse(Line("cpu cores"), out var cores);

            var arch = await RunAsync("uname", "-m");
            var maxKhz = await ReadAsync("/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq");
            var minKhz = await ReadAsync("/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_min_freq");

            double maxGhz = long.TryParse(maxKhz, out var mx) ? Math.Round(mx / 1_000_000.0, 2) : 0;
            double minGhz = long.TryParse(minKhz, out var mn) ? Math.Round(mn / 1_000_000.0, 2) : 0;

            return (vendor, model, cores > 0 ? cores : logical, logical, arch, maxGhz, minGhz);
        });

    //RAM

    public Task<Result<double>> GetRamTotalGbAsync() => Result<double>.From(async () =>
    {
        var line = (await ReadAsync("/proc/meminfo")).Split('\n').FirstOrDefault(l => l.StartsWith("MemTotal:")) ?? "";
        return long.TryParse(RegexMatch(line, @"\d+"), out var kb) ? Math.Round(kb / 1024.0 / 1024.0, 2) : 0;
    });

    public Task<Result<List<RamSlot>>> GetRamSlotsAsync() => Result<List<RamSlot>>.From(async () =>
    {
        var output = await RunAsync("dmidecode", "-t memory");
        return output.Split("Memory Device")
            .Where(b => b.Contains("Size:") && !b.Contains("No Module"))
            .Select(b =>
            {
                var lines = b.Split('\n');
                string Get(string key) => lines.FirstOrDefault(l => l.TrimStart().StartsWith(key))?.Split(':').Last().Trim() ?? "Unknown";

                var sizeStr = Get("Size:");
                if (sizeStr is "No Module Installed" or "Unknown") return null;

                double gb = sizeStr.EndsWith("GB") && double.TryParse(sizeStr.Replace("GB", "").Trim(), out var g) ? g
                          : sizeStr.EndsWith("MB") && double.TryParse(sizeStr.Replace("MB", "").Trim(), out var m) ? Math.Round(m / 1024.0, 2) : 0;
                int.TryParse(RegexMatch(Get("Speed:"), @"\d+"), out var speed);

                return new RamSlot
                {
                    Locator = Get("Locator:"),
                    SizeGb = gb,
                    Type = Get("Type:"),
                    SpeedMhz = speed,
                    Manufacturer = Get("Manufacturer:"),
                    PartNumber = Get("Part Number:").Trim()
                };
            })
            .Where(s => s is not null)
            .Cast<RamSlot>()
            .ToList();
    });

    public Task<Result<(int, int)>> GetRamSlotCountAsync() => Result<(int, int)>.From(async () =>
    {
        var output = await RunAsync("dmidecode", "-t memory");
        return (System.Text.RegularExpressions.Regex.Matches(output, "Memory Device").Count,
                System.Text.RegularExpressions.Regex.Matches(output, @"Size: \d+").Count);
    });

    //Storage 

    public Task<Result<List<StorageDrive>>> GetStorageDrivesAsync() => Result<List<StorageDrive>>.From(async () =>
    {
        var json = await RunAsync("lsblk", "-d -o NAME,MODEL,SIZE,ROTA --json");
        return JsonArray(json)
            .Where(d => !(d.TryGetProperty("name", out var n) && n.GetString()?.StartsWith("loop") == true))
            .Select(d => new StorageDrive
            {
                Model = (JsonStr(d, "model")).Trim().NullIfEmpty() ?? "Unknown",
                SizeGb = ParseSizeToGb(JsonStr(d, "size")),
                Type = JsonStr(d, "name").StartsWith("nvme") ? "NVMe"
                       : JsonStr(d, "rota") == "0" ? "SSD" : "HDD"
            }).ToList();
    });

    //GPU

    public Task<Result<List<GpuInfo>>> GetGpusAsync() => Result<List<GpuInfo>>.From(async () =>
    {
        var nv = await RunAsync("nvidia-smi", "--query-gpu=name,memory.total --format=csv,noheader");
        if (!string.IsNullOrWhiteSpace(nv))
            return nv.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).Select(line =>
            {
                var p = line.Split(',');
                return new GpuInfo { Model = p[0].Trim(), VramMb = long.TryParse(RegexMatch(p.Length > 1 ? p[1] : "", @"\d+"), out var v) ? v : 0 };
            }).ToList();

        return (await RunAsync("lspci", "-v")).Split("\n\n")
            .Where(b => b.Contains("VGA") || b.Contains("3D") || b.Contains("Display"))
            .Select(b =>
            {
                var model = b.Split('\n').First();
                model = model.Contains(':') ? model.Split(':').Last().Trim() : model;
                var memLine = b.Split('\n').FirstOrDefault(l => l.Contains("Memory") && l.Contains("prefetchable"));
                long vram = 0;
                if (memLine is not null && System.Text.RegularExpressions.Regex.Match(memLine, @"\[size=(\d+)([MG])") is { Success: true } m)
                    vram = m.Groups[2].Value == "G" ? long.Parse(m.Groups[1].Value) * 1024 : long.Parse(m.Groups[1].Value);
                return new GpuInfo { Model = model, VramMb = vram };
            }).ToList();
    });

    //Network

    public Task<Result<List<NetworkAdapterInfo>>> GetNetworkAdaptersAsync() => Result<List<NetworkAdapterInfo>>.From(async () =>
    {
        var adapters = new List<NetworkAdapterInfo>();
        foreach (var iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            var driver = (await RunAsync("readlink", $"/sys/class/net/{iface.Name}/device/driver")).Split('/').LastOrDefault() ?? "Unknown";
            adapters.Add(new NetworkAdapterInfo
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
                Driver = driver.NullIfEmpty() ?? "Unknown",
                IsUp = iface.OperationalStatus == OperationalStatus.Up
            });
        }
        return adapters;
    });

    //OS

    public Task<Result<(string, string, DateTime, string)>> GetOsInfoAsync() => Result<(string, string, DateTime, string)>.From(async () =>
    {
        var name = (await ReadAsync("/etc/os-release")).Split('\n')
                        .FirstOrDefault(l => l.StartsWith("PRETTY_NAME="))?.Split('=').Last().Trim('"', '\r') ?? "Linux";
        var version = await RunAsync("uname", "-r");
        var uptime = (await ReadAsync("/proc/uptime")).Split(' ')[0];
        var lastBoot = double.TryParse(uptime, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var u) ? DateTime.UtcNow.AddSeconds(-u) : DateTime.UtcNow;
        var lastUser = (await RunAsync("last", "-1 --time-format iso")).Split('\n').FirstOrDefault()?.Split(' ').FirstOrDefault() ?? "Unknown";
        return (name, version, lastBoot, lastUser);
    });

    public Task<Result<(string, string, string, string, int)>> GetSoftwareInfoAsync() => Result<(string, string, string, string, int)>.From(async () =>
    {
        var shell = Environment.GetEnvironmentVariable("SHELL")?.Split('/').Last() ?? "Unknown";
        var de = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "Unknown";
        var locale = (await RunAsync("locale", "")).Split('\n').FirstOrDefault(l => l.StartsWith("LANG="))?.Split('=').Last() ?? "Unknown";
        var display = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is not null ? "Wayland"
                    : Environment.GetEnvironmentVariable("DISPLAY") is not null ? "X11" : "Unknown";

        int packages = 0;
        foreach (var (cmd, args) in new[] { ("pacman", "-Q"), ("dpkg", "--list"), ("rpm", "-qa") })
        {
            var out_ = await RunAsync(cmd, args);
            if (!string.IsNullOrWhiteSpace(out_)) { packages = out_.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l)); break; }
        }
        return (shell, display, de, locale, packages);
    });

    //System

    public Task<Result<(string, string, string, string)>> GetSystemInfoAsync() => Result<(string, string, string, string)>.From(async () =>
        (
            (await ReadAsync("/sys/class/dmi/id/sys_vendor")).NullIfEmpty() ?? "Unknown",
            (await ReadAsync("/sys/class/dmi/id/product_name")).NullIfEmpty() ?? "Unknown",
            (await ReadAsync("/sys/class/dmi/id/bios_version")).NullIfEmpty() ?? "Unknown",
            $"{await ReadAsync("/sys/class/dmi/id/board_vendor")} {await ReadAsync("/sys/class/dmi/id/board_name")}".Trim().NullIfEmpty() ?? "Unknown"
        ));

    //Displays

    public Task<Result<List<DisplayInfo>>> GetDisplaysAsync() => Result<List<DisplayInfo>>.From(async () =>
        (await RunAsync("xrandr", "--query")).Split('\n')
            .Where(l => l.Contains(" connected"))
            .Select(l => new DisplayInfo
            {
                Resolution = RegexMatch(l, @"\d{3,4}x\d{3,4}", 0).NullIfEmpty() ?? "Unknown",
                IsPrimary = l.Contains("primary")
            }).ToList());

    //Security

    public Task<Result<SecurityInfo>> GetSecurityInfoAsync() => Result<SecurityInfo>.From(async () =>
    {
        var s = new SecurityInfo();
        var sb = await ReadAsync("/sys/firmware/efi/efivars/SecureBoot-8be4df61-93ca-11d2-aa0d-00e098032b8c");
        s.SecureBootEnabled = sb.Length >= 5 && sb[4] == '\x01';

        var tpmDirs = Directory.Exists("/sys/class/tpm") ? Directory.GetDirectories("/sys/class/tpm") : [];
        s.TpmVersion = tpmDirs.Length > 0 ? (await ReadAsync(Path.Combine(tpmDirs[0], "tpm_version_major"))).NullIfEmpty() ?? "Unknown" : "None";

        s.DiskEncryptionEnabled = (await RunAsync("lsblk", "-o TYPE")).Contains("crypt");
        s.FirewallActive = (await RunAsync("ufw", "status")).Contains("active");
        s.AppArmorStatus = (await ReadAsync("/sys/kernel/security/apparmor/profiles")).NullIfEmpty() is null ? "disabled" : "enforcing";

        var journal = await RunAsync("journalctl", "_COMM=sshd --since '24h ago' -q --no-pager", timeout: 5);
        s.FailedLoginAttempts = journal.Split('\n').Count(l => l.Contains("Failed password") || l.Contains("Invalid user"));

        s.OpenPorts = (await RunAsync("ss", "-tlnp")).Split('\n').Skip(1)
            .Select(l => RegexMatch(l, @":(\d+)\s")).Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct().OrderBy(x => x).ToList();

        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        s.SshKeysPresent = Directory.Exists(sshDir) && Directory.GetFiles(sshDir, "*.pub").Length > 0;

        s.PendingSecurityUpdates = (await RunAsync("checkupdates", "", timeout: 15)).Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));
        return s;
    });

    //Virtualization

    public Task<Result<VirtualizationInfo>> GetVirtualizationInfoAsync() => Result<VirtualizationInfo>.From(async () =>
    {
        var cgroup = await ReadAsync("/proc/1/cgroup");
        if (cgroup.Contains("docker") || cgroup.Contains("containerd") || File.Exists("/.dockerenv"))
            return new VirtualizationInfo { IsContainer = true, HypervisorType = "Docker" };

        var virt = (await RunAsync("systemd-detect-virt", "")).ToLower();
        if (virt is "none" or "") return new VirtualizationInfo();

        return new VirtualizationInfo
        {
            IsVirtualMachine = true,
            HypervisorType = virt switch { "kvm" => "KVM", "vmware" => "VMware", "oracle" => "VirtualBox", "microsoft" or "hyperv" => "Hyper-V", "xen" => "Xen", _ => virt }
        };
    });

    //Metrics

    private double _lastGlobalIdle, _lastGlobalTotal;
    private double[] _lastCoreIdle = [], _lastCoreTotal = [];
    private long _lastDiskRead, _lastDiskWrite, _lastNetSent, _lastNetRecv;

    public double GetCpuLoadPercent() => Result<double>.From(() =>
    {
        var nums = File.ReadAllText("/proc/stat").Split('\n')
            .First(l => l.StartsWith("cpu "))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(s => double.TryParse(s, out var n) ? n : 0).ToArray();
        var (idle, total) = (nums[3], nums.Sum());
        var (di, dt) = (idle - _lastGlobalIdle, total - _lastGlobalTotal);
        (_lastGlobalIdle, _lastGlobalTotal) = (idle, total);
        return dt > 0 ? Math.Round((1.0 - di / dt) * 100.0, 2) : 0;
    }).GetValueOrDefault(0);

    public List<double> GetPerCoreCpuPercent() => Result<List<double>>.From(() =>
    {
        var lines = File.ReadAllText("/proc/stat").Split('\n')
            .Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^cpu\d+")).ToArray();

        if (_lastCoreIdle.Length != lines.Length)
        {
            _lastCoreIdle = new double[lines.Length];
            _lastCoreTotal = new double[lines.Length];
        }

        return lines.Select((line, i) =>
        {
            var nums = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                           .Select(s => double.TryParse(s, out var n) ? n : 0).ToArray();
            var (idle, total) = (nums[3], nums.Sum());
            var (di, dt) = (idle - _lastCoreIdle[i], total - _lastCoreTotal[i]);
            (_lastCoreIdle[i], _lastCoreTotal[i]) = (idle, total);
            return dt > 0 ? Math.Round((1.0 - di / dt) * 100.0, 1) : 0;
        }).ToList();
    }).GetValueOrDefault([]);

    public double GetCpuTemperature() => Result<double>.From(() =>
    {
        var zone = Directory.GetFiles("/sys/class/thermal/", "temp", SearchOption.AllDirectories).FirstOrDefault();
        return zone is not null && long.TryParse(File.ReadAllText(zone).Trim(), out var t) ? Math.Round(t / 1000.0, 1) : 0;
    }).GetValueOrDefault(0);

    public (double, double, double) GetLoadAverage() => Result<(double, double, double)>.From(() =>
    {
        var p = File.ReadAllText("/proc/loadavg").Split(' ');
        double P(string s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        return (P(p[0]), P(p[1]), P(p[2]));
    }).GetValueOrDefault((0, 0, 0));

    public double GetRamUsedGb() => Result<double>.From(() =>
    {
        var lines = File.ReadAllText("/proc/meminfo").Split('\n');
        long Get(string key) => lines.Where(l => l.StartsWith(key))
            .Select(l => long.TryParse(RegexMatch(l, @"\d+"), out var v) ? v : 0).FirstOrDefault();
        return Math.Round((Get("MemTotal:") - Get("MemAvailable:")) / 1024.0 / 1024.0, 2);
    }).GetValueOrDefault(0);

    public double GetSwapUsedGb() => Result<double>.From(() =>
    {
        var lines = File.ReadAllText("/proc/meminfo").Split('\n');
        long Get(string key) => lines.Where(l => l.StartsWith(key))
            .Select(l => long.TryParse(RegexMatch(l, @"\d+"), out var v) ? v : 0).FirstOrDefault();
        return Math.Round((Get("SwapTotal:") - Get("SwapFree:")) / 1024.0 / 1024.0, 2);
    }).GetValueOrDefault(0);

    public double GetDiskFreeGb() =>
        Result<double>.From(() => BytesToGb(new DriveInfo("/").AvailableFreeSpace)).GetValueOrDefault(0);

    public (double, double) GetDiskIo() => Result<(double, double)>.From(() =>
    {
        long total(int col) => File.ReadAllText("/proc/diskstats").Split('\n')
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length >= 14 && System.Text.RegularExpressions.Regex.IsMatch(p[2], @"^(sd[a-z]|nvme\d+n\d+|hd[a-z])$"))
            .Sum(p => long.TryParse(p[col], out var v) ? v : 0);

        var (r, w) = (total(5), total(9));
        var result = (Math.Max(0, Math.Round((r - _lastDiskRead) * 512.0 / 1024 / 1024, 2)),
                      Math.Max(0, Math.Round((w - _lastDiskWrite) * 512.0 / 1024 / 1024, 2)));
        (_lastDiskRead, _lastDiskWrite) = (r, w);
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

    public (double, double) GetGpuMetrics() => Result<(double, double)>.From(() =>
    {
        var out_ = RunAsync("nvidia-smi", "--query-gpu=utilization.gpu,temperature.gpu --format=csv,noheader").Result;
        if (string.IsNullOrWhiteSpace(out_)) return (0, 0);
        var p = out_.Split(',');
        return (double.TryParse(RegexMatch(p[0], @"\d+"), out var u) ? u : 0,
                p.Length > 1 && double.TryParse(p[1].Trim(), out var t) ? t : 0);
    }).GetValueOrDefault((0, 0));

    public (double, int) GetBatteryInfo() => Result<(double, int)>.From(() =>
    {
        var bat = Directory.GetDirectories("/sys/class/power_supply/")
            .First(d => File.ReadAllText(Path.Combine(d, "type")).Trim() == "Battery");
        double.TryParse(File.ReadAllText(Path.Combine(bat, "capacity")).Trim(), out var cap);
        int.TryParse(File.ReadAllText(Path.Combine(bat, "cycle_count")).Trim(), out var cycles);
        return (cap, cycles);
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