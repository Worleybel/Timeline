using System.Globalization;
using Timline.Models;

namespace Timline.Services;

public sealed record Snapshot(
    string TodayCaption,
    string TodayTime,
    double Remaining,
    string WeekText,
    string HolidayText,
    double? LunchFraction = null);

/// <summary>根据工作时间配置 + 节假日日历，计算今日/本周剩余工时与假期信息。</summary>
public sealed class CountdownCalculator
{
    private readonly AppConfig _config;
    private readonly HolidayService _holidays;

    public CountdownCalculator(AppConfig config, HolidayService holidays)
    {
        _config = config;
        _holidays = holidays;
    }

    private TimeOnly Get(string value, int defaultHour, int defaultMinute) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : new TimeOnly(defaultHour, defaultMinute);

    private TimeOnly WorkStart => Get(_config.WorkStart, 9, 0);
    private TimeOnly LunchStart => Get(_config.LunchStart, 12, 0);
    private TimeOnly LunchEnd => Get(_config.LunchEnd, 13, 0);
    private TimeOnly WorkEnd => Get(_config.WorkEnd, 18, 0);

    /// <summary>一个完整工作日的净工时（扣除午休）。</summary>
    public TimeSpan FullDay
    {
        get
        {
            var span = WorkEnd - WorkStart - (LunchEnd - LunchStart);
            if (span <= TimeSpan.Zero) span = TimeSpan.FromHours(8); // 配置不合理时兜底 8 小时
            return span;
        }
    }

    /// <summary>今日剩余净工时（当前处于午休时不计入）。</summary>
    public TimeSpan TodayRemainingWork(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        if (!_holidays.IsWorkday(today)) return TimeSpan.Zero;

        var t = now.TimeOfDay;
        var ws = WorkStart.ToTimeSpan(); var we = WorkEnd.ToTimeSpan();
        var ls = LunchStart.ToTimeSpan(); var le = LunchEnd.ToTimeSpan();
        if (t <= ws) return FullDay;
        if (t >= we) return TimeSpan.Zero;

        var span = we - t;
        var overlapStart = t > ls ? t : ls;
        var overlapEnd = we < le ? we : le;
        var lunchOverlap = overlapEnd > overlapStart ? overlapEnd - overlapStart : TimeSpan.Zero;
        return span - lunchOverlap;
    }

    /// <summary>本周（周一至今日所在周结束）剩余净工时，含调休安排。</summary>
    public TimeSpan WeekRemaining(DateTime now)
    {
        var total = TodayRemainingWork(now);
        var date = DateOnly.FromDateTime(now).AddDays(1);
        while (date.DayOfWeek != DayOfWeek.Monday) // 走到下周一为止 = 覆盖本周剩余
        {
            if (_holidays.IsWorkday(date)) total += FullDay;
            date = date.AddDays(1);
        }
        return total;
    }

    public string FormatWork(TimeSpan span)
    {
        if (span <= TimeSpan.Zero) return "0";
        var full = FullDay;
        var days = (int)(span.Ticks / full.Ticks);
        var rest = TimeSpan.FromTicks(span.Ticks - days * full.Ticks);
        if (days > 0)
            return $"{days} 天 {rest.Hours} 小时{(rest.Minutes > 0 ? $" {rest.Minutes} 分" : "")}";
        return $"{(int)rest.TotalHours} 小时 {rest.Minutes} 分";
    }

    /// <summary>下一假期描述：假期中显示第几天，否则显示距下次假期天数。</summary>
    public string NextHolidayText(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var offs = _holidays.AllHolidays();
        if (offs.Count == 0)
        {
            if (!_holidays.HasData) return "假期数据加载中…";
            return _holidays.IgnoringStatutory ? "未跟随法定假期" : "年度假期已过完";
        }

        var dates = offs.Select(x => x.Date).ToHashSet();
        if (dates.Contains(today))
        {
            var start = today;
            while (dates.Contains(start.AddDays(-1))) start = start.AddDays(-1);
            var end = today;
            while (dates.Contains(end.AddDays(1))) end = end.AddDays(1);
            var name = offs.FirstOrDefault(x => x.Date == start)?.Name
                       ?? offs.First(x => x.Date == today).Name;
            var index = today.DayNumber - start.DayNumber + 1;
            var total = end.DayNumber - start.DayNumber + 1;
            return $"{name} · 第 {index}/{total} 天";
        }

        var next = offs.FirstOrDefault(x => x.Date > today);
        if (next is null) return "年度假期已过完";
        var runStart = next.Date;
        while (dates.Contains(runStart.AddDays(-1))) runStart = runStart.AddDays(-1);
        var runName = offs.FirstOrDefault(x => x.Date == runStart)?.Name ?? next.Name;
        var days = next.Date.DayNumber - today.DayNumber;
        return $"距 {runName} {days} 天";
    }

    public const string CaptionBeforeWork = "今日 · 距上班";
    public const string CaptionAtWork = "今日 · 距下班";
    public const string CaptionLunch = "今日 · 午休中";
    public const string CaptionOffWork = "今日 · 已下班";
    public const string CaptionRest = "今日 · 休息中";

    public Snapshot GetSnapshot(DateTime now)
    {
        var week = FormatWork(WeekRemaining(now));
        var holiday = NextHolidayText(now);
        var today = DateOnly.FromDateTime(now);

        // 进度条为"减少模式"：Remaining 表示今日剩余工时的比例，满条=刚开始上班，空条=下班
        if (_holidays.IsRestDay(today))
        {
            return new Snapshot(
                CaptionRest,
                _holidays.IsSpecialHoliday(today) ? "假期愉快 🎉" : "周末愉快 🏖",
                0, week, holiday);
        }

        var t = now.TimeOfDay;
        var ws = WorkStart.ToTimeSpan();
        var we = WorkEnd.ToTimeSpan();
        var ls = LunchStart.ToTimeSpan();
        var le = LunchEnd.ToTimeSpan();
        if (t < ws)
            return new Snapshot(CaptionBeforeWork, FormatCountdown(ws - t), 1, week, holiday);
        if (t >= we)
            return new Snapshot(CaptionOffWork, "🎉 下班啦", 0, week, holiday);

        var remaining = Math.Clamp((we - t).Ticks / (double)(we - ws).Ticks, 0, 1);

        // 午休时段：庆祝态，TodayTime 只含数字（"午休剩余"/"分钟"由 UI 拼接），并带上午休剩余比例供配色
        if (t >= ls && t < le)
        {
            var lunchLeft = (int)Math.Ceiling((le - t).TotalMinutes);
            var lunchFraction = Math.Clamp((le - t).Ticks / (double)(le - ls).Ticks, 0, 1);
            return new Snapshot(CaptionLunch, $"{lunchLeft}", remaining, week, holiday, lunchFraction);
        }

        return new Snapshot(CaptionAtWork, FormatCountdown(we - t), remaining, week, holiday);
    }

    public static string FormatCountdown(TimeSpan ts) =>
        $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
}
