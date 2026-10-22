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
    private static readonly MethodInfo SetVisibleAsync = typeof(PlatformServices).Assembly
        .GetType("Dockyard.Infrastructure.Internal.Win32.TitleBarVisibility")!.GetMethod("SetVisibleAsync")!;
    private delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);
    private static readonly WindowProcedure TestWindowProcedure = DefWindowProc;

    [Fact]
    public async Task ASlowWindow_DoesNotBlockTheCaller_AndItsRestoreFollowsItsHide()
    {
        using var window = new PausedWindow();
        var hide = await RequestVisibilityAsync(window.Handle, false);
        var restore = await RequestVisibilityAsync(window.Handle, true);

        Assert.False(hide.IsCompleted);
        Assert.False(restore.IsCompleted);
        window.Resume();

        Assert.True(await hide.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await restore.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(Caption, (long)GetWindowLongPtr(window.Handle, -16) & Caption);
        Assert.Equal(0, GetProp(window.Handle, "Dockyard.OriginalCaption"));
    }

    [Fact]
    public async Task ASlowWindow_DoesNotHoldUpTitleBarsInOtherWindows()
    {
        using var slow = new PausedWindow();
        var pending = await RequestVisibilityAsync(slow.Handle, false);
        using var responsive = new PausedWindow();
        responsive.Resume();

        var hide = await RequestVisibilityAsync(responsive.Handle, false);
        Assert.True(await hide.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, (long)GetWindowLongPtr(responsive.Handle, -16) & Caption);
        var restore = await RequestVisibilityAsync(responsive.Handle, true);
        Assert.True(await restore.WaitAsync(TimeSpan.FromSeconds(5)));

        slow.Resume();
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        var restoreSlow = await RequestVisibilityAsync(slow.Handle, true);
        Assert.True(await restoreSlow.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static async Task<Task<bool>> RequestVisibilityAsync(nint window, bool visible)
    {
        // If the API regresses to a synchronous native call, fail without leaving the test thread stuck.
        var request = Task.Factory.StartNew(() =>
            (Task<bool>)SetVisibleAsync.Invoke(null, [new WindowHandle(window), visible])!,
            CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
        return await request.WaitAsync(TimeSpan.FromSeconds(2));
    }

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

    /// <summary>A hidden standard window whose owner initially does not pump messages.</summary>
    private sealed class PausedWindow : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new();
        private readonly ManualResetEventSlim _resume = new();
        private readonly Thread _thread;
        private uint _threadId;

        public nint Handle { get; private set; }

        public PausedWindow()
        {
            _thread = new Thread(() =>
            {
                _threadId = GetCurrentThreadId();
                Handle = CreateWindowEx(0, "STATIC", "Caption async regression test", 0x00CF0000,
                    0, 0, 300, 200, 0, 0, GetModuleHandle(null), 0);
                _ready.Set();
                _resume.Wait();
                while (GetMessage(out var message, 0, 0, 0) > 0) DispatchMessage(ref message);
                DestroyWindow(Handle);
            }) { IsBackground = true };
            _thread.Start();
            Assert.True(_ready.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotEqual(0, Handle);
        }

        public void Resume() => _resume.Set();

        public void Dispose()
        {
            _resume.Set();
            PostThreadMessage(_threadId, 0x0012 /* WM_QUIT */, 0, 0);
            Assert.True(_thread.Join(TimeSpan.FromSeconds(5)));
            _ready.Dispose();
            _resume.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X, Y;
        public uint Private;
    }

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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint GetProp(nint window, string name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern int GetMessage(out Message message, nint window, uint minimum, uint maximum);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref Message message);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
}
