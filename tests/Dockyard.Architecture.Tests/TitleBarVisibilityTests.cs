using System.Reflection;
using System.Runtime.InteropServices;
using Dockyard.Domain;
using Dockyard.Infrastructure;

namespace Dockyard.Architecture.Tests;

public sealed class TitleBarVisibilityTests
{
    private const long Caption = 0x00C00000;
    private static readonly MethodInfo SetVisible = typeof(PlatformServices).Assembly
        .GetType("Dockyard.Infrastructure.Internal.Win32.TitleBarVisibility")!.GetMethod("SetVisible")!;
    private delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);
    private static readonly WindowProcedure TestWindowProcedure = DefWindowProc;

    [Theory]
    [InlineData("Dockyard.StandardCaptionTest", false)]
    [InlineData("WinUIDesktopWin32WindowClass", true)]
    [InlineData("Chrome_WidgetWin_1", true)]
    public void Hide_PreservesCustomHeadersAndRestoresEarlierChanges(string className, bool customHeader)
    {
        // Hidden windows with real Win32 styles; no mouse input or visible desktop changes.
        var windowClass = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Instance = GetModuleHandle(null),
            WindowProc = Marshal.GetFunctionPointerForDelegate(TestWindowProcedure),
            ClassName = className,
        };
        Assert.NotEqual((ushort)0, RegisterClassEx(ref windowClass));
        nint window = 0;
        try
        {
            window = CreateWindowEx(0, className, "Caption regression test", 0x00CF0000, 0, 0, 300, 200, 0, 0, windowClass.Instance, 0);
            Assert.NotEqual(0, window);
            var original = (long)GetWindowLongPtr(window, -16);

            Assert.Equal(!customHeader, ChangeVisibility(window, false));
            Assert.Equal(customHeader ? original : original & ~Caption, (long)GetWindowLongPtr(window, -16));

            // Reproduce a caption hidden by the prototype before custom headers were excluded.
            Assert.True(SetProp(window, "Dockyard.OriginalCaption", (nint)Caption));
            SetWindowLongPtr(window, -16, (nint)(original & ~Caption));
            Assert.Equal(customHeader, ChangeVisibility(window, false));
            Assert.Equal(customHeader ? original : original & ~Caption, (long)GetWindowLongPtr(window, -16));

            ChangeVisibility(window, true);
            Assert.Equal(original, (long)GetWindowLongPtr(window, -16));
        }
        finally
        {
            if (window != 0)
            {
                ChangeVisibility(window, true);
                DestroyWindow(window);
            }
            UnregisterClass(className, windowClass.Instance);
        }
    }

    private static bool ChangeVisibility(nint window, bool visible) =>
        (bool)SetVisible.Invoke(null, [new WindowHandle(window), visible])!;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public nint WindowProc;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background, MenuName;
        public string? ClassName;
        public nint SmallIcon;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassEx(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, nint instance);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetProp(nint window, string name, nint value);
}
