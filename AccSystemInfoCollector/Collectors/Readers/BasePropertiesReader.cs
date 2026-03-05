using System.Diagnostics;
using System.Net.NetworkInformation;

namespace TEMS.ACC.Collectors.Readers;

public abstract class BasePropertiesReader
{
    protected static async Task<string> ReadFileAsync(string path)
    {
        try
        {
            return await File.ReadAllTextAsync(path);
        }
        catch
        {
            return string.Empty;
        }
    }

    protected static async Task<string> ExecuteCommandAsync(string command, string args)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output.Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    protected static List<string> GetMacAddressesBase()
    {
        return NetworkInterface
            .GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => string.Join(":", n.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2"))))
            .Where(mac => mac.Length > 0 && mac != "00:00:00:00:00:00")
            .Distinct()
            .ToList();
    }

    protected static string GetHostnameBase() => Environment.MachineName;
}