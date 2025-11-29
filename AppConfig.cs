using System;
using System.IO;
using System.Text.Json;

public class AppConfig
{
    // VRChat
    public string VrcUsername { get; set; } = "";
    public string VrcPassword { get; set; } = "";
    public string VrcGroupId { get; set; } = "";
    public string TotpSecret { get; set; } = "";

    // Discord (Webhook URL を想定)
    public string DiscordWebhookUrl { get; set; } = "";

    // 監視間隔（分）
    public int IntervalMinutes { get; set; } = 15;

    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Vrchan");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return new AppConfig();

            var json = File.ReadAllText(ConfigPath);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json);
            return cfg ?? new AppConfig();
        }
        catch
        {
            // 壊れていたら初期値で作り直し
            return new AppConfig();
        }
    }

    public void Save()
    {
        if (!Directory.Exists(ConfigDir))
        {
            Directory.CreateDirectory(ConfigDir);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(ConfigPath, json);
    }

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(VrcUsername)
               && !string.IsNullOrWhiteSpace(VrcPassword)
               && !string.IsNullOrWhiteSpace(VrcGroupId)
               && !string.IsNullOrWhiteSpace(TotpSecret)
               && !string.IsNullOrWhiteSpace(DiscordWebhookUrl);
    }
}
