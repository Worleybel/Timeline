using System.IO;
using System.Windows;

namespace Timline;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    /// <summary>非空时表示本次启动仅为输出截图（开发验证用）。</summary>
    public static string? ScreenshotPath { get; private set; }

    /// <summary>截图目标是设置窗口而非主控件。</summary>
    public static bool ScreenshotSettings { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && e.Args[0] is "--screenshot" or "--screenshot-settings")
        {
            ScreenshotSettings = e.Args[0] == "--screenshot-settings";
            ScreenshotPath = e.Args.Length > 1
                ? e.Args[1]
                : Path.Combine(Path.GetTempPath(),
                    ScreenshotSettings ? "timline-settings.png" : "timline-widget.png");
        }

        // 防止开机自启 + 手动打开时出现两个控件；截图模式豁免——否则控件正在运行时无法出图验证
        if (ScreenshotPath is null)
        {
            _singleInstanceMutex = new Mutex(true, @"Timline::SingleInstance", out var createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }
        }
        base.OnStartup(e);
    }
}
