using System.Runtime.InteropServices;

namespace Timline.Interop;

/// <summary>DWM 窗口效果：圆角窗口与亚克力模糊（尽力而为，失败静默忽略）。</summary>
internal static class DwmInterop
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWA_BORDER_COLOR = 34;

    /// <summary>关闭系统窗口描边（Win11 默认给无边框窗口画 1px 边框线）。</summary>
    private static readonly int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    public static void ApplyRoundedCorners(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            // 角偏好显式设回默认：ROUND 会让 DWM 给窗口叠加系统阴影与描边（运行时实测），
            // 阴影罩层是系统小圆角、与卡片 16px 圆角不一致；卡片圆角由 WPF 自绘，DWM 别插手
            var corner = DWMWCP_DEFAULT;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            var noBorder = DwmColorNone;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));
        }
        catch
        {
        }
    }

    public static void EnableAcrylic(IntPtr hwnd, byte alpha, byte r, byte g, byte b) =>
        SetAccent(hwnd, ACCENT_ENABLE_ACRYLICBLURBEHIND, (uint)((alpha << 24) | (r << 16) | (g << 8) | b));

    public static void DisableAcrylic(IntPtr hwnd) => SetAccent(hwnd, ACCENT_DISABLED, 0);

    private static void SetAccent(IntPtr hwnd, int state, uint color)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var accent = new AccentPolicy { AccentState = state, GradientColor = color };
            var size = Marshal.SizeOf<AccentPolicy>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, fDeleteOld: false);
                var data = new WindowCompositionAttribData { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttribData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttribData data);
}
