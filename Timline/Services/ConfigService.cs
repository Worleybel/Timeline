using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Timline.Models;

namespace Timline.Services;

public static class ConfigService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Timline");

    private static readonly string File =
        Path.Combine(Dir, "config.json");

    // 枚举（如 WeeklyRestDays 的 DayOfWeek）存字符串，避免 Sunday=0 的数字歧义
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppConfig Load()
    {
        try
        {
            if (System.IO.File.Exists(File))
            {
                var config = JsonSerializer.Deserialize<AppConfig>(System.IO.File.ReadAllText(File), JsonOpts);
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
