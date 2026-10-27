namespace Timline.Models;

/// <summary>用户配置，持久化在 %APPDATA%\Timline\config.json。</summary>
public class AppConfig
{
    public string WorkStart { get; set; } = "09:00";
    public string LunchStart { get; set; } = "12:00";
    public string LunchEnd { get; set; } = "13:00";
    public string WorkEnd { get; set; } = "18:00";

    /// <summary>按日期自定义的假期/上班日，优先于法定节假日数据。</summary>
    public List<HolidayDay> CustomDays { get; set; } = new();

    /// <summary>每周固定休息日（无法定数据时的兜底规律），默认双休。由 ConfigService 的枚举转换器存为字符串。</summary>
    public List<DayOfWeek> WeeklyRestDays { get; set; } = [DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>完全不跟随国家法定节假日数据（假期全靠自定义与每周规律）。</summary>
    public bool IgnoreStatutoryHolidays { get; set; }

    /// <summary>卡片背景不透明度 0.2 ~ 1.0。</summary>
    public double OpacityLevel { get; set; } = 0.85;

    /// <summary>是否启用亚克力模糊背景（实验性，效果依系统而定）。</summary>
    public bool EnableBlur { get; set; }

    public double? WindowX { get; set; }
    public double? WindowY { get; set; }

    /// <summary>卡片大小（DIP），拖拽窗口边缘/角落调整；为空时使用默认尺寸。</summary>
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
}
