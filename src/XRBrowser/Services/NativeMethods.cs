using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace XRBrowser.Services;

public static class NativeMethods
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaMicaEffect = 1029;

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out NativePoint point);

    [DllImport("dwmapi.dll")]
    public static extern int DwmFlush();

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
        IntPtr hdcSrc, int xSrc, int ySrc, int rop);

    public static System.Windows.Media.Imaging.BitmapSource? CaptureScreen(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        var memoryDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, width, height);
        var previous = SelectObject(memoryDc, bitmap);
        try
        {
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, 0x00CC0020))
                return null;
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, IntPtr.Zero, Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            SelectObject(memoryDc, previous);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private const int WcaAccentPolicy = 19;

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    public static void ApplyDarkFrame(Window window, bool dark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            var value = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));

            var corner = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
        }
        catch
        {
            // Older Windows versions don't support these attributes.
        }
    }

    public static void SetCaptionColors(Window window, uint border, uint caption)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var b = unchecked((int)border);
            var c = unchecked((int)caption);
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref b, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref c, sizeof(int));
        }
        catch { }
    }

    public static void ClearAcrylic(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var accent = new AccentPolicy { AccentState = 0, AccentFlags = 0, GradientColor = 0 };
            var size = Marshal.SizeOf<AccentPolicy>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WcaAccentPolicy,
                    SizeOfData = size,
                    Data = ptr
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        catch { }
    }

    public static bool ApplyMica(Window window, bool enabled)
    {
        if (!IsWindows11OrGreater()) return false;

        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;

            if (enabled)
            {
                var backdrop = 3; // DWMSBT_TRANSIENTWINDOW (Acrylic blur)
                var result = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
                if (result != 0)
                {
                    var legacyMica = 1;
                    result = DwmSetWindowAttribute(hwnd, DwmwaMicaEffect, ref legacyMica, sizeof(int));
                }
                if (result != 0) return false;

                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                return true;
            }

            var none = 1; // DWMSBT_NONE
            DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref none, sizeof(int));
            var legacyOff = 0;
            DwmSetWindowAttribute(hwnd, DwmwaMicaEffect, ref legacyOff, sizeof(int));
            var zero = default(Margins);
            DwmExtendFrameIntoClientArea(hwnd, ref zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void ApplyAcrylic(IntPtr hwnd, Color tint, byte opacity)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            // ACCENT_ENABLE_ACRYLICBLURBEHIND. GradientColor is AABBGGRR.
            var color = (opacity << 24) |
                        (tint.B << 16) |
                        (tint.G << 8) |
                        tint.R;
            var accent = new AccentPolicy
            {
                AccentState = 4,
                AccentFlags = 2,
                GradientColor = color,
                AnimationId = 0
            };
            var accentSize = Marshal.SizeOf<AccentPolicy>();
            var accentPtr = Marshal.AllocHGlobal(accentSize);
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WcaAccentPolicy,
                    SizeOfData = accentSize,
                    Data = accentPtr
                };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
        catch
        {
            // Acrylic is undocumented and unavailable on some builds.
        }
    }

    public static bool IsWindows11OrGreater()
        => Environment.OSVersion.Version.Build >= 22000;
}
