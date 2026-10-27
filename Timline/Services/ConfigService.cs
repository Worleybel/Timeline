using System.IO;
using System.Text.Json;
using Timline.Models;

namespace Timline.Services;

public static class ConfigService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Timline");

    private static readonly string File =
        Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppConfig Load()
    {
        try
        {
            if (System.IO.File.Exists(File))
            {
                var config = JsonSerializer.Deserialize<AppConfig>(System.IO.File.ReadAllText(File));
                if (config is not null) return config;
            }
        }
        catch
        {
            // 配置损坏时回退到默认值
        }
        return new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(config, JsonOpts));
        }
        catch
        {
            // 保存失败不影响运行
        }
    }
}
