using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Timline.Models;
using Timline.Services;

namespace Timline.Windows;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly Action _onSaved;

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

        _config.WorkStart = TpWorkStart.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        _config.LunchStart = TpLunchStart.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        _config.LunchEnd = TpLunchEnd.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        _config.WorkEnd = TpWorkEnd.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
        _config.OpacityLevel = OpacitySlider.Value / 100.0;
        _config.EnableBlur = BlurCheck.IsChecked == true;

        ConfigService.Save(_config);
        _onSaved();
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();
}
