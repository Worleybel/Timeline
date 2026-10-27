using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Timline.Interop;
using Timline.Models;
using Timline.Services;
using Timline.Windows;
using Hardcodet.Wpf.TaskbarNotification;

namespace Timline;

public partial class MainWindow : Window
{
    private const double TrackWidth = 214;

    // 设计尺寸：Viewbox 等比缩放的基准，WM_SIZING 钩子据此锁定窗口宽高比。
    // 高度=内容自然高度+约 12px 的弹性间隔——弹性行过大时进度条与剩余工时之间会出现大空隙
    private const double DesignWidth = 252;
    private const double DesignHeight = 154;

    // 与进度条渐变一致的三个主题色（红→黄→绿）
    private static readonly Color BarRed = Color.FromRgb(0xF8, 0x51, 0x49);
    private static readonly Color BarYellow = Color.FromRgb(0xD2, 0x99, 0x22);
    private static readonly Color BarGreen = Color.FromRgb(0x3F, 0xB9, 0x50);

    private readonly SolidColorBrush _timeBrush = new(Color.FromRgb(0xE6, 0xED, 0xF3));

    private readonly AppConfig _config;
    private readonly CountdownCalculator _calc;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();
        _config = ConfigService.Load();
        _calc = new CountdownCalculator(_config, HolidayService.Instance);

        HolidayService.Instance.EnsureLoadedInBackground(
            () => Dispatcher.BeginInvoke(UpdateDisplay));

        ApplyCardStyle();
        ApplySavedSize();
        PositionWindow();

        _timer.Tick += (_, _) => UpdateDisplay();
        _timer.Start();
        UpdateDisplay();

        Closing += (_, _) =>
        {
            _config.WindowX = Left;
            _config.WindowY = Top;
            _config.WindowWidth = Width;
            _config.WindowHeight = Height;
            ConfigService.Save(_config);
        };

        if (App.ScreenshotSettings)
        {
            Loaded += (_, _) => OpenSettings();
        }
        else if (App.ScreenshotPath is not null)
        {
            Loaded += (_, _) => Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle, new Action(SaveScreenshot));
        }
    }

    private void SaveScreenshot()
    {
        try
        {
            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, PixelFormats.Pbgra32);
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        DwmInterop.ApplyRoundedCorners(hwnd);
        ApplyBlurVisuals();
        ((HwndSource)PresentationSource.FromVisual(this)!).AddHook(WndProc);
    }

    // ---------- 大小调节 ----------

    private const int WM_SIZING = 0x0214;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>按启动配置恢复卡片大小：宽度取配置值，高度按设计比推导——
    /// 旧版按 178 高保存的尺寸会自动迁移到新比例，避免恢复后 Viewbox 留白。</summary>
    private void ApplySavedSize()
    {
        Width = Math.Clamp(_config.WindowWidth ?? DesignWidth, MinWidth, MaxWidth);
        Height = Width * DesignHeight / DesignWidth;
    }

    /// <summary>
    /// 拖拽边缘/角落缩放时锁定宽高比，保证 Viewbox 内容始终铺满窗口不变形：
    /// 拖角落按对角等比缩放；拖左右边宽度跟随、高度以未拖动侧为锚；
    /// 拖上下边高度跟随（宽度不变的话高度会被还原，表现为拖不动）、宽度以水平中心为锚对称扩展。
    /// 坐标是物理像素，宽高比与 DPI 无关，直接按比例换算即可。
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOP = 3, WMSZ_TOPLEFT = 4,
                  WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6, WMSZ_BOTTOMLEFT = 7, WMSZ_BOTTOMRIGHT = 8;

        if (msg != WM_SIZING) return IntPtr.Zero;
        var edge = wParam.ToInt32();
        if (edge is not (WMSZ_LEFT or WMSZ_RIGHT or WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT
                         or WMSZ_BOTTOM or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT)) return IntPtr.Zero;

        var rect = Marshal.PtrToStructure<NativeRect>(lParam);

        if (edge is WMSZ_TOP or WMSZ_BOTTOM)
        {
            var height = (int)Math.Max(rect.Bottom - rect.Top, 60);
            var width = (int)Math.Round(height * DesignWidth / DesignHeight);
            var centerX = (rect.Left + rect.Right) / 2;
            rect.Left = centerX - width / 2;
            rect.Right = rect.Left + width;
        }
        else
        {
            var width = (int)Math.Max(rect.Right - rect.Left, MinWidth);
            var height = (int)Math.Round(width * DesignHeight / DesignWidth);

            if (edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT) rect.Top = rect.Bottom - height;
            else rect.Bottom = rect.Top + height;

            if (edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT) rect.Left = rect.Right - width;
            else rect.Right = rect.Left + width;
        }

        Marshal.StructureToPtr(rect, lParam, fDeleteOld: false);
        handled = true;
        return IntPtr.Zero;
    }

    private void OnResetSizeClick(object sender, RoutedEventArgs e)
    {
        Width = DesignWidth;
        Height = DesignHeight;
        ConfigService.Save(_config);
    }

    // ---------- 显示 ----------

    /// <summary>剩余比例 → 主题色：剩余越多越红，越少越绿（与进度条渐变一致）。</summary>
    private static Color RemainingThemeColor(double remaining)
    {
        static Color Lerp(Color a, Color b, double k) => Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * k),
            (byte)Math.Round(a.G + (b.G - a.G) * k),
            (byte)Math.Round(a.B + (b.B - a.B) * k));

        var t = Math.Clamp(1 - remaining, 0, 1);
        return t < 0.5
            ? Lerp(BarRed, BarYellow, t * 2)
            : Lerp(BarYellow, BarGreen, (t - 0.5) * 2);
    }

    private string _lastState = "";

    private void UpdateDisplay()
    {
        var snapshot = _calc.GetSnapshot(DateTime.Now);
        TodayCaption.Text = snapshot.TodayCaption;
        TodayTime.Text = snapshot.TodayTime;

        // 裁剪整轨渐变，只露出右侧 fillWidth 部分：左端红色先被"消耗"
        var fillWidth = TrackWidth * snapshot.Remaining;
        TodayProgressFill.Clip = fillWidth > 0.5
            ? new RectangleGeometry(new Rect(TrackWidth - fillWidth, 0, fillWidth, 5), 2.5, 2.5)
            : new RectangleGeometry(new Rect(0, 0, 0, 5));

        // 配色来源：午休时用"午休剩余比例取反"（剩得多=绿、快结束=红），其余用全天剩余比例
        var colorSource = snapshot.LunchFraction is { } lunchFraction ? 1 - lunchFraction : snapshot.Remaining;
        var theme = RemainingThemeColor(colorSource);
        _timeBrush.Color = theme;
        TodayTime.Foreground = _timeBrush;
        TimeGlow.Color = theme;

        // 剩余百分比：不足 1% 但非零时用小数形式
        var pct = snapshot.Remaining * 100;
        RemainingPct.Text = pct <= 0 ? "0%"
            : pct < 1 ? pct.ToString("0.##", CultureInfo.InvariantCulture) + "%"
            : $"{Math.Round(pct)}%";

        WeekValue.Text = snapshot.WeekText;
        HolidayValue.Text = snapshot.HolidayText;

        var offWork = snapshot.TodayCaption == CountdownCalculator.CaptionOffWork;
        var lunch = snapshot.TodayCaption == CountdownCalculator.CaptionLunch;
        SetCardMode(offWork, lunch);
        NotifyStateChange(_lastState, snapshot.TodayCaption);
        _lastState = snapshot.TodayCaption;
    }

    /// <summary>检测状态跳变并弹系统通知；状态未变化或启动时已处于的状态不通知。</summary>
    private void NotifyStateChange(string from, string to)
    {
        if (from == to) return;
        var message = (from, to) switch
        {
            (CountdownCalculator.CaptionBeforeWork, CountdownCalculator.CaptionAtWork)
                => "☀️ 上班时间到，新的一天开始啦！",
            (CountdownCalculator.CaptionLunch, CountdownCalculator.CaptionAtWork)
                => "☕ 午休结束，继续加油！",
            (CountdownCalculator.CaptionAtWork or CountdownCalculator.CaptionBeforeWork,
             CountdownCalculator.CaptionLunch)
                => "🍜 午休时间到，好好休息一下~",
            (CountdownCalculator.CaptionBeforeWork or CountdownCalculator.CaptionAtWork,
             CountdownCalculator.CaptionOffWork)
                => "🎉 下班啦，今天辛苦了！",
            _ => null,
        };
        if (message is not null)
        {
            TrayIcon.ShowBalloonTip("Timline", message, BalloonIcon.Info);
        }
    }

    private void SetCardMode(bool offWork, bool lunch)
    {
        var celebrate = offWork || lunch; // 庆祝态：隐藏百分比与进度条、垂直水平居中
        var align = celebrate ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        TimeRow.HorizontalAlignment = align;
        // 庆祝态：跨过进度条行和弹性空隙行，在标题与【本周剩余】之间垂直居中
        Grid.SetRowSpan(TimeRow, celebrate ? 3 : 1);
        TimeRow.VerticalAlignment = celebrate ? VerticalAlignment.Center : VerticalAlignment.Stretch;
        TodayTime.FontSize = offWork ? 36 : lunch ? 26 : 30;
        TodayCaption.HorizontalAlignment = align;
        RemainingPct.Visibility = celebrate ? Visibility.Collapsed : Visibility.Visible;
        ProgressBorder.Visibility = celebrate ? Visibility.Collapsed : Visibility.Visible;
        LunchPrefix.Visibility = lunch ? Visibility.Visible : Visibility.Collapsed;
        LunchSuffix.Visibility = lunch ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyCardStyle()
    {
        var alpha = (byte)Math.Clamp((int)(255 * _config.OpacityLevel), 40, 255);
        if (_config.EnableBlur) alpha = (byte)(alpha * 0.55); // 有模糊衬托时降低卡片自身浓度
        RootCard.Background = new SolidColorBrush(Color.FromArgb(alpha, 22, 27, 34));
    }

    private void ApplyBlurVisuals()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (_config.EnableBlur) DwmInterop.EnableAcrylic(hwnd, 0x99, 22, 27, 34);
        else DwmInterop.DisableAcrylic(hwnd);
    }

    private void PositionWindow()
    {
        if (_config.WindowX is double x && _config.WindowY is double y &&
            x >= SystemParameters.VirtualScreenLeft - Width &&
            y >= SystemParameters.VirtualScreenTop - Height &&
            x <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 40 &&
            y <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40)
        {
            Left = x;
            Top = y;
        }
        else
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - Width - 14;
            Top = work.Bottom - Height - 10;
        }
    }

    // ---------- 窗口交互 ----------

    private void RootCard_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            OpenSettings();
        }
        else
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
        e.Handled = true;
    }

    // ---------- 菜单 ----------

    private void Menu_OnOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            switch (item.Header as string)
            {
                case "窗口置顶":
                    item.IsChecked = Topmost;
                    break;
                case "开机自启":
                    item.IsChecked = AutoStartService.IsEnabled;
                    break;
                case "亚克力模糊":
                    item.IsChecked = _config.EnableBlur;
                    break;
                case "透明度":
                    foreach (var sub in item.Items.OfType<MenuItem>())
                    {
                        sub.IsChecked = sub.Tag is string s &&
                                        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) &&
                                        Math.Abs(v - _config.OpacityLevel) < 0.01;
                    }
                    break;
            }
        }
    }

    private void OnToggleTopmost(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            Topmost = !Topmost;
            item.IsChecked = Topmost;
        }
    }

    private void OnToggleAutoStart(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            if (AutoStartService.IsEnabled) AutoStartService.Disable();
            else AutoStartService.Enable();
            item.IsChecked = AutoStartService.IsEnabled;
        }
    }

    private void OnOpacityClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string s } &&
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            _config.OpacityLevel = Math.Clamp(v, 0.2, 1.0);
            ApplyCardStyle();
            ConfigService.Save(_config);
        }
    }

    private void OnToggleBlur(object sender, RoutedEventArgs e)
    {
        _config.EnableBlur = !_config.EnableBlur;
        ApplyCardStyle();
        ApplyBlurVisuals();
        ConfigService.Save(_config);
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        var window = new SettingsWindow(_config, () =>
        {
            ApplyCardStyle();
            ApplyBlurVisuals();
            UpdateDisplay();
        })
        { Owner = this };
        window.Show();
        window.Activate();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        TrayIcon.Dispose();
        Application.Current.Shutdown();
    }

    private void TrayIcon_OnTrayMouseDoubleClick(object sender, RoutedEventArgs e) => OpenSettings();
}
