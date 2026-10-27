using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Timline.Models;
using Timline.Services;

namespace Timline.Windows;

public partial class SettingsWindow : Window
{
    // 药丸显示顺序：周一在前，符合中文周习惯
    private static readonly (DayOfWeek Day, string Label)[] WeekOrder =
    [
        (DayOfWeek.Monday, "一"), (DayOfWeek.Tuesday, "二"), (DayOfWeek.Wednesday, "三"),
        (DayOfWeek.Thursday, "四"), (DayOfWeek.Friday, "五"), (DayOfWeek.Saturday, "六"),
        (DayOfWeek.Sunday, "日"),
    ];

    private static readonly SolidColorBrush DimBrush = new(Color.FromRgb(0x8B, 0x94, 0x9E));

    private readonly AppConfig _config;
    private readonly Action _onSaved;

    /// <summary>自定义假期的工作副本：点"保存"才写回配置，"取消"直接丢弃。</summary>
    private readonly List<HolidayDay> _workingCustom = new();
    private readonly Dictionary<DayOfWeek, ToggleButton> _dayChips = new();

    public SettingsWindow(AppConfig config, Action onSaved)
    {
        InitializeComponent();
        _config = config;
        _onSaved = onSaved;

        TpWorkStart.Value = ParseOr(config.WorkStart, 9, 0);
        TpLunchStart.Value = ParseOr(config.LunchStart, 12, 0);
        TpLunchEnd.Value = ParseOr(config.LunchEnd, 13, 0);
        TpWorkEnd.Value = ParseOr(config.WorkEnd, 18, 0);
        OpacitySlider.Value = Math.Clamp(config.OpacityLevel * 100, 20, 100);
        BlurCheck.IsChecked = config.EnableBlur;
        FollowStatutoryCheck.IsChecked = !config.IgnoreStatutoryHolidays;

        InitWeekChips(config.WeeklyRestDays);
        _workingCustom.AddRange(config.CustomDays.OrderBy(d => d.Date));
        RebuildCustomList();

        if (App.ScreenshotSettings)
        {
            Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                new Action(SaveScreenshotAndExit));
        }
    }

    private static TimeOnly ParseOr(string value, int hour, int minute) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : new TimeOnly(hour, minute);

    // ---------- 每周休息日 ----------

    private void InitWeekChips(IEnumerable<DayOfWeek> restDays)
    {
        var rest = restDays.ToHashSet();
        foreach (var (day, label) in WeekOrder)
        {
            var chip = new ToggleButton
            {
                Content = label,
                Style = (Style)Resources["DayChip"],
                IsChecked = rest.Contains(day),
            };
            WeekDayPanel.Children.Add(chip);
            _dayChips[day] = chip;
        }
    }

    // ---------- 自定义假期 ----------

    private void OnAddCustomClick(object sender, RoutedEventArgs e)
    {
        var date = DpNew.Value;
        var isOff = RbOff.IsChecked == true;
        if (_workingCustom.Any(x => x.Date == date))
        {
            var overwrite = MessageBox.Show(this,
                $"{date:yyyy-MM-dd} 已有自定义设置，要覆盖吗？", "Timline",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes) return;
            _workingCustom.RemoveAll(x => x.Date == date);
        }

        var name = TbName.Text.Trim();
        if (name.Length == 0) name = isOff ? "自定义假期" : "自定义上班";
        _workingCustom.Add(new HolidayDay(name, date, isOff));
        _workingCustom.Sort((a, b) => a.Date.CompareTo(b.Date));
        TbName.Clear();
        RebuildCustomList();
    }

    private void RemoveCustom(DateOnly date)
    {
        _workingCustom.RemoveAll(x => x.Date == date);
        RebuildCustomList();
    }

    private void RebuildCustomList()
    {
        CustomListPanel.Children.Clear();
        if (_workingCustom.Count == 0)
        {
            CustomListPanel.Children.Add(new TextBlock
            {
                Text = "暂无。添加的日期将优先于法定节假日数据",
                FontSize = 11,
                Foreground = DimBrush,
                Margin = new Thickness(0, 2, 0, 2),
            });
            return;
        }
        foreach (var day in _workingCustom)
            CustomListPanel.Children.Add(BuildCustomRow(day));
    }

    private UIElement BuildCustomRow(HolidayDay day)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dateText = new TextBlock
        {
            Text = $"{day.Date:yyyy-MM-dd} {WeekLabel(day.Date.DayOfWeek)}",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var nameText = new TextBlock
        {
            Text = day.Name,
            FontSize = 11,
            Foreground = DimBrush,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var badge = new Border
        {
            Background = new SolidColorBrush(day.IsOffDay
                ? Color.FromRgb(0x23, 0x86, 0x36)
                : Color.FromRgb(0x9E, 0x6A, 0x03)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = day.IsOffDay ? "假" : "班", FontSize = 11, Foreground = Brushes.White },
        };
        var remove = new Button
        {
            Content = "✕",
            Style = (Style)Resources["LinkBtn"],
            VerticalAlignment = VerticalAlignment.Center,
        };
        remove.Click += (_, _) => RemoveCustom(day.Date);

        Grid.SetColumn(dateText, 0);
        Grid.SetColumn(nameText, 1);
        Grid.SetColumn(badge, 2);
        Grid.SetColumn(remove, 3);
        row.Children.Add(dateText);
        row.Children.Add(nameText);
        row.Children.Add(badge);
        row.Children.Add(remove);
        return row;
    }

    private static string WeekLabel(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        DayOfWeek.Sunday => "周日",
        _ => "",
    };

    // ---------- 截图与保存 ----------

    private void SaveScreenshotAndExit()
    {
        try
        {
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),
                (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(this);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var stream = System.IO.File.Create(App.ScreenshotPath!);
            encoder.Save(stream);
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (TpWorkEnd.Value <= TpWorkStart.Value)
        {
            MessageBox.Show(this, "下班时间需要晚于上班时间", "Timline");
            return;
        }

        var restDays = _dayChips.Where(kv => kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();
        if (restDays.Count == 0)
        {
            MessageBox.Show(this, "每周至少需要保留一个休息日", "Timline");
            return;
        }

        _config.WorkStart = TpWorkStart.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        _config.LunchStart = TpLunchStart.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        _config.LunchEnd = TpLunchEnd.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        _config.WorkEnd = TpWorkEnd.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        _config.OpacityLevel = OpacitySlider.Value / 100.0;
        _config.EnableBlur = BlurCheck.IsChecked == true;
        _config.WeeklyRestDays = restDays;
        _config.IgnoreStatutoryHolidays = FollowStatutoryCheck.IsChecked != true;
        _config.CustomDays = _workingCustom;

        ConfigService.Save(_config);
        _onSaved();
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
