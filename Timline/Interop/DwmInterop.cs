using System.Runtime.InteropServices;

namespace Timline.Interop;

/// <summary>DWM 窗口效果：圆角窗口与亚克力模糊（尽力而为，失败静默忽略）。</summary>
internal static class DwmInterop
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

    public static void ApplyRoundedCorners(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
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
