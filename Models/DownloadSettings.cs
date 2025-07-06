using System;
using System.IO;
using System.Text.Json;

namespace ZeniqaDownloadManager.Models
{
    public class DownloadSettings
    {
        public int MaxConcurrentChunks { get; set; } = 8;
        public int BufferSize { get; set; } = 8192; // bytes
        public int TimeoutSeconds { get; set; } = 180; // seconds
        public int MaxRetries { get; set; } = 5;
        public int RetryDelayMs { get; set; } = 1000;
        public int MaxRetryDelayMs { get; set; } = 16000;
        public bool EnableConnectionPooling { get; set; } = true;
        public bool EnableCompression { get; set; } = true;
        public int ConnectionLimit { get; set; } = 16;

        public static string SettingsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ZeniqaDownloadManager", "settings.json");

        public static DownloadSettings Load()
        {
            try
            {
                var path = SettingsFilePath;
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<DownloadSettings>(json) ?? new DownloadSettings();
                }
            }
            catch { }
            return new DownloadSettings();
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir!);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
    }
} 