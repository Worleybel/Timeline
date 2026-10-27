using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Timline.Models;

namespace Timline.Services;

/// <summary>
/// 法定节假日数据服务。数据来自 NateScarlet/holiday-cn（每年一份 JSON，含法定假日与调休补班日），
/// 本地缓存在 %APPDATA%\Timline\holidays\，每 7 天尝试刷新，离线时直接用缓存，无缓存时退化为周一至周五。
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

    private readonly string _dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Timline", "holidays");

    public bool HasData
    {
        get { lock (_gate) return _years.Values.Any(l => l.Count > 0); }
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

    /// <summary>该日期是否有节假日数据（法定假日或调休补班日）。</summary>
    public bool TryGetSpecial(DateOnly date, out HolidayDay day)
    {
        lock (_gate)
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
        }
        day = null!;
        return false;
    }

    /// <summary>是否为工作日：有数据看法定的调休安排，否则按周一至周五。</summary>
    public bool IsWorkday(DateOnly date) =>
        TryGetSpecial(date, out var special)
            ? !special.IsOffDay
            : date.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday;

    /// <summary>是否为休息日（周末或法定假日）。</summary>
    public bool IsRestDay(DateOnly date) =>
        TryGetSpecial(date, out var special)
            ? special.IsOffDay
            : date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>今天是否在法定假期中（区别于普通周末）。</summary>
    public bool IsSpecialHoliday(DateOnly date) =>
        TryGetSpecial(date, out var special) && special.IsOffDay;

    /// <summary>所有法定假日（含日期与名称），按日期排序。</summary>
    public IReadOnlyList<HolidayDay> AllHolidays()
    {
        lock (_gate)
        {
            return _years.Values
                .SelectMany(x => x)
                .Where(x => x.IsOffDay)
                .OrderBy(x => x.Date)
                .ToList();
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
