using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using TEMS.ACC.Models;

namespace TEMS.ACC.Collectors.Readers;

public abstract class BasePropertiesReader
{
    protected async Task<string> RunAsync(string cmd, string args, int timeout = 10) =>
        (await ExecuteCommandAsync(cmd, args, timeout)).GetValueOrDefault(string.Empty);

    protected async Task<string> ReadAsync(string path) =>
        (await ReadFileAsync(path)).GetValueOrDefault(string.Empty);

    private static Task<Result<string>> ReadFileAsync(string path) =>
        Result<string>.From(async () => await File.ReadAllTextAsync(path));

    private static Task<Result<string>> ExecuteCommandAsync(string cmd, string args, int timeout = 10) =>
        Result<string>.From(async () =>
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            return output.Trim();
        });

    //Shared helpers

    protected static string GetHostnameBase() => Environment.MachineName;

    protected static List<string> GetMacAddressesBase() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                     && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => string.Join(":", n.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2"))))
            .Where(mac => mac is { Length: > 0 } && mac != "00:00:00:00:00:00")
            .Distinct()
            .ToList();

    //JSON helpers

    protected static string JsonStr(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) ? v.GetString() ?? string.Empty : string.Empty;

    protected static long JsonLong(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.TryGetInt64(out var n) ? n : 0;

    protected static int JsonInt(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : 0;

    protected static IEnumerable<JsonElement> JsonArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToList()
            : [doc.RootElement];
    }

    //Size helpers

    protected static double BytesToGb(long bytes) =>
        Math.Round((double)bytes / 1024 / 1024 / 1024, 2);

    protected static double ParseSizeToGb(string s)
    {
        s = s.Trim().ToUpperInvariant().Split('(')[0].Trim();
        if (s.EndsWith("T") && double.TryParse(s[..^1], out var t)) return t * 1024;
        if (s.EndsWith("G") && double.TryParse(s[..^1], out var g)) return g;
        if (s.EndsWith("M") && double.TryParse(s[..^1], out var m)) return Math.Round(m / 1024.0, 2);
        if (s.Contains("TB") && double.TryParse(s.Replace("TB", "").Trim(), out var tb)) return tb * 1024;
        if (s.Contains("GB") && double.TryParse(s.Replace("GB", "").Trim(), out var gb)) return gb;
        if (s.Contains("MB") && double.TryParse(s.Replace("MB", "").Trim(), out var mb)) return Math.Round(mb / 1024.0, 2);
        return 0;
    }

    protected static long ParseVramToMb(string s)
    {
        s = s.Trim().ToUpperInvariant();
        if (s.Contains("GB") && long.TryParse(s.Replace("GB", "").Trim(), out var g)) return g * 1024;
        if (s.Contains("MB") && long.TryParse(s.Replace("MB", "").Trim(), out var m)) return m;
        return 0;
    }

    //Regex helper 

    protected static string RegexMatch(string input, string pattern, int group = 1)
    {
        var m = System.Text.RegularExpressions.Regex.Match(input, pattern);
        return m.Success ? m.Groups[group].Value : string.Empty;
    }
}