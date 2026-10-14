using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using static Dockyard.UiTests.Win32;

namespace Dockyard.UiTests;

/// <summary>
/// The shortcuts while a remote desktop client has the focus. No remote computer is needed: a fixture run as
/// "mstsc.exe" does what the real client does, installing a low-level keyboard hook when it gets the focus and
/// taking every key, modifiers included.
/// </summary>
internal static partial class Program
{
    private const string FakeRdpTitle = "dyt-rdp";

    private delegate nint LowLevelProc(int code, nint message, nint data);

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int hook, LowLevelProc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);

    private static void RunRemoteDesktop()
    {
        var a = Spawn("dyt-a", ProcessWindowStyle.Normal);
        var r = SpawnFakeRdp();

        Step("build a group with the remote desktop client", () =>
        {
            Invoke(FindIn(_frame, "Add Window…"));
            Pick(a);
            Settle();
            AddVia(a, "Split Right…", r);
        }, [a, r], [a, r]);

        Step("the client takes the keys while it has the focus", () =>
        {
            FocusByClick(r);
            Press(0x41);
        }, [a, r], [a, r], extra: () => Title(r.Hwnd).Contains("last=41") ? null : $"client title '{Title(r.Hwnd)}'");

        Step("Ctrl+Alt+Shift+Left leaves the client for the left pane", () => Keys(0x11, 0x12, 0x10, 0x25), [a, r], [a, r],
            extra: () => GetForegroundWindow() == a.Hwnd ? null : $"focus on '{Title(GetForegroundWindow())}'");

        Step("Ctrl+Alt+Shift+Right goes back to the client", () => Keys(0x11, 0x12, 0x10, 0x27), [a, r], [a, r],
            extra: () => GetForegroundWindow() == r.Hwnd ? null : $"focus on '{Title(GetForegroundWindow())}'");

        Step("the client gets the keys again, the shortcut's arrow excepted", () =>
        {
            Thread.Sleep(500);
            Press(0x42);
        }, [a, r], [a, r], extra: () => Title(r.Hwnd).Contains("last=42") ? null : $"client title '{Title(r.Hwnd)}'");
    }

    /// <summary>Starts this program as "mstsc.exe", the process name Dockyard knows as a remote desktop client.</summary>
    private static Subject SpawnFakeRdp()
    {
        // The app host finds Dockyard.UiTests.dll next to itself whatever it is called.
        var exe = Path.Combine(AppContext.BaseDirectory, "mstsc.exe");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Dockyard.UiTests.exe"), exe, overwrite: true);
        var process = Process.Start(exe, "--fake-rdp")!;
        Spawned.Add(process);
        var hwnd = Wait(() => FindWindow(h => Pid(h) == process.Id && IsWindowVisible(h) && Title(h).StartsWith(FakeRdpTitle)), "fake remote desktop client");
        return new Subject(FakeRdpTitle, hwnd, WindowRect(hwnd));
    }

    /// <summary>
    /// The fixture: hooks the keyboard while active and takes every key, the way mstsc sends them to the remote
    /// computer. The title counts the key presses it took and shows the last one in hex.
    /// </summary>
    private static int RunFakeRdp()
    {
        var window = new System.Windows.Window
        {
            Title = FakeRdpTitle,
            Width = 600,
            Height = 400,
            Left = 300,
            Top = 200,
            Background = System.Windows.Media.Brushes.SteelBlue,
        };
        var taken = 0;
        nint hook = 0;
        LowLevelProc proc = (code, message, data) =>
        {
            if (code < 0) return 0;
            if (message is 0x0100 or 0x0104)
            {
                var key = Marshal.ReadInt32(data);
                taken++;
                window.Title = $"{FakeRdpTitle} n={taken} last={key:X2}";
            }
            return 1;
        };
        window.Activated += (_, _) =>
        {
            if (hook == 0) hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, proc, 0, 0);
        };
        window.Deactivated += (_, _) =>
        {
            if (hook != 0) UnhookWindowsHookEx(hook);
            hook = 0;
        };
        return new System.Windows.Application().Run(window);
    }
}
