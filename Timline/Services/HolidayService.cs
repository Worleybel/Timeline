using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Timline.Models;

namespace Timline.Services;

/// <summary>
/// 法定节假日数据服务。数据来自 NateScarlet/holiday-cn（每年一份 JSON，含法定假日与调休补班日），
/// 本地缓存在 %APPDATA%\Timline\holidays\，每 7 天尝试刷新；支持用户自定义假期/调休与每周休息规律覆盖，
/// 无任何数据时按每周休息规律（默认双休）判断。
/// </summary>
public sealed class HolidayService
{
    public static HolidayService Instance { get; } = new();

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };

    // 依次尝试多个镜像：jsdelivr 两个节点 + GitHub 原始地址
    private static readonly string[] UrlTemplates =
    {
        "https://cdn.jsdelivr.net/gh/NateScarlet/holiday-cn@master/{0}.json",
        "https://testingcf.jsdelivr.net/gh/NateScarlet/holiday-cn@master/{0}.json",
        "https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{0}.json",
    };

    private readonly object _gate = new();
    private readonly Dictionary<int, List<HolidayDay>> _years = new();

    // 用户自定义覆盖：按日期 > 法定数据 > 每周休息规律（IsWorkday/IsRestDay 统一在此裁决）
    private readonly Dictionary<DateOnly, HolidayDay> _custom = new();
    private HashSet<DayOfWeek> _weeklyRest = [DayOfWeek.Saturday, DayOfWeek.Sunday];
    private bool _ignoreStatutory;

    private readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Timline", "holidays");

    public bool HasData
    {
        get { lock (_gate) return _years.Values.Any(l => l.Count > 0); }
    }

    /// <summary>用户是否选择不跟随法定节假日数据（用于假期文案区分"没数据"与"被忽略"）。</summary>
    public bool IgnoringStatutory
    {
        get { lock (_gate) return _ignoreStatutory; }
    }

    /// <summary>同步加载本地缓存（快，只在启动时调用一次）。</summary>
    public void EnsureLoaded()
    {
        foreach (var year in YearsToLoad()) LoadLocal(year);
    }

    /// <summary>先同步加载缓存，再后台刷新过期数据；刷新成功后回调（UI 线程需自行调度）。</summary>
    public void EnsureLoadedInBackground(Action? refreshed = null)
    {
        EnsureLoaded();
        Task.Run(async () =>
        {
            var changed = false;
            foreach (var year in YearsToLoad())
            {
                if (await RefreshAsync(year)) changed = true;
            }
            if (changed) refreshed?.Invoke();
        });
    }

    private static int[] YearsToLoad() => new[] { DateTime.Today.Year, DateTime.Today.Year + 1 };

    /// <summary>
    /// 注入用户自定义假期配置（启动时与设置保存后调用）：
    /// CustomDays 按日期覆盖一切；WeeklyRestDays 为无数据时的兜底规律；IgnoreStatutoryHolidays 关闭法定数据。
    /// </summary>
    public void ApplyUserOverrides(AppConfig config)
    {
        lock (_gate)
        {
            _custom.Clear();
            foreach (var day in config.CustomDays) _custom[day.Date] = day;
            var weekly = config.WeeklyRestDays.ToHashSet();
            // 每天都是工作日不成立：留空则退回双休
            _weeklyRest = weekly.Count > 0 ? weekly : [DayOfWeek.Saturday, DayOfWeek.Sunday];
            _ignoreStatutory = config.IgnoreStatutoryHolidays;
        }
    }

    /// <summary>该日期是否有法定节假日数据（法定假日或调休补班日）。调用方需已持有 _gate。</summary>
    private bool TryGetStatutory(DateOnly date, out HolidayDay day)
    {
        foreach (var list in _years.Values)
        {
            var hit = list.FirstOrDefault(x => x.Date == date);
            if (hit is not null)
            {
                day = hit;
                return true;
            }
        }
        day = null!;
        return false;
    }

    /// <summary>是否为工作日：按日期自定义 &gt; 法定调休安排 &gt; 每周休息规律。</summary>
    public bool IsWorkday(DateOnly date)
    {
        lock (_gate)
        {
            if (_custom.TryGetValue(date, out var custom)) return !custom.IsOffDay;
            if (!_ignoreStatutory && TryGetStatutory(date, out var special)) return !special.IsOffDay;
            return !_weeklyRest.Contains(date.DayOfWeek);
        }
    }

    /// <summary>是否为休息日（自定义假期、法定假日或每周休息日）。</summary>
    public bool IsRestDay(DateOnly date) => !IsWorkday(date);

    /// <summary>今天是否在"假期"中（法定或自定义假日，区别于普通每周休息日），用于文案。</summary>
    public bool IsSpecialHoliday(DateOnly date)
    {
        lock (_gate)
        {
            if (_custom.TryGetValue(date, out var custom)) return custom.IsOffDay;
            if (!_ignoreStatutory && TryGetStatutory(date, out var special)) return special.IsOffDay;
            return false;
        }
    }

    /// <summary>所有假期（含日期与名称），按日期排序；自定义条目覆盖同日期的法定条目。</summary>
    public IReadOnlyList<HolidayDay> AllHolidays()
    {
        lock (_gate)
        {
            var map = new Dictionary<DateOnly, HolidayDay>();
            if (!_ignoreStatutory)
            {
                foreach (var day in _years.Values.SelectMany(x => x).Where(x => x.IsOffDay))
                    map[day.Date] = day;
            }
            foreach (var day in _custom.Values.Where(x => x.IsOffDay))
                map[day.Date] = day;
            return map.Values.OrderBy(x => x.Date).ToList();
        }
    }

    private void LoadLocal(int year)
    {
        try
        {
            var path = Path.Combine(_dir, $"{year}.json");
            if (!System.IO.File.Exists(path)) return;
            var days = Parse(System.IO.File.ReadAllText(path));
            if (days.Count > 0)
            {
                lock (_gate) _years[year] = days;
            }
        }
        catch
        {
            // 缓存损坏则忽略，等后台刷新
        }
    }

    private async Task<bool> RefreshAsync(int year)
    {
        var path = Path.Combine(_dir, $"{year}.json");
        var isStale = !System.IO.File.Exists(path) ||
                      System.IO.File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-7);
        if (!isStale)
        {
            LoadLocal(year);
            return false;
        }

        foreach (var template in UrlTemplates)
        {
            try
            {
                var url = string.Format(CultureInfo.InvariantCulture, template, year);
                var json = await Http.GetStringAsync(url);
                var days = Parse(json);
                if (days.Count == 0) continue;

                Directory.CreateDirectory(_dir);
                await System.IO.File.WriteAllTextAsync(path, json);
                lock (_gate) _years[year] = days;
                return true;
            }
            catch
            {
                // 换下一个镜像
            }
        }
        return false;
    }

    internal static List<HolidayDay> Parse(string json)
    {
        var list = new List<HolidayDay>();
        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.GetProperty("days").EnumerateArray())
        {
            var name = el.GetProperty("name").GetString() ?? "";
            var date = DateOnly.Parse(el.GetProperty("date").GetString() ?? "");
            list.Add(new HolidayDay(name, date, el.GetProperty("isOffDay").GetBoolean()));
        }
        return list;
    }
}
