using System.Text.Json;

namespace TemsACC.Configuration;

public class ConfigurationManager
{
    private readonly string _configPath;
    private TemsACC.Models.Configuration? _config;

    public ConfigurationManager()
    {
        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".tems"
        );
        Directory.CreateDirectory(appDataFolder);
        _configPath = Path.Combine(appDataFolder, "config.json");
        LoadConfiguration();
    }

    public TemsACC.Models.Configuration Config => _config ?? throw new InvalidOperationException("Configuration not loaded");

    private void LoadConfiguration()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                var defaultConfig = new TemsACC.Models.Configuration
                {
                    ApiUrl = "https://api.tems.example.com",
                    AssetId = "ASSET-ID-HERE",
                    ApiKey = ""
                };
                SaveConfiguration(defaultConfig);
                _config = defaultConfig;
                Console.WriteLine($"Configuration file created at: {_configPath}");
                Console.WriteLine("Please edit the configuration file and restart the service.");
            }
            else
            {
                var json = File.ReadAllText(_configPath);
                _config = JsonSerializer.Deserialize<TemsACC.Models.Configuration>(json);
                Console.WriteLine($"Configuration loaded from: {_configPath}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load configuration: {ex.Message}");
            throw;
        }
    }

    private void SaveConfiguration(TemsACC.Models.Configuration config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }
}