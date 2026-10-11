using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using static Dockyard.UiTests.Win32;

namespace Dockyard.UiTests;

/// <summary>
/// End-to-end check of the main cases against a real Dockyard.exe and real console windows.
/// Each step drives the UI the way a user would, then waits for the group to satisfy its invariants.
/// </summary>
internal static partial class Program
{
    private const int Margin = 4, Caption = 24, TabBar = 24;
    private static readonly string LogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dockyard", "dockyard.log");
    private static readonly string SessionPath = Path.Combine(Path.GetDirectoryName(LogPath)!, "session.json");
    private static string _exe = "";

    private static Process _app = null!;
    private static nint _frame;
    private static readonly List<Process> Spawned = [];
    /// <summary>Windows opened through the shell (no process handle to kill); closed with WM_CLOSE.</summary>
    private static readonly List<nint> SpawnedWindows = [];
    private static readonly List<string> Failures = [];
    private static bool _dragOnly;

    private sealed record Subject(string Name, nint Hwnd, RECT Original);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--client-caption")) return RunClientCaption(args.Contains("--manual-move"));
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Dockyard.slnx"))) root = Path.GetDirectoryName(root)!;
        _dragOnly = args.Contains("--drag-only") || args.Contains("--external-only");
        _exe = Path.Combine(root, _dragOnly ? @"src\Dockyard.Exe\bin\DragValidation\Dockyard.exe" : @"src\Dockyard.Exe\bin\Debug\net10.0-windows10.0.19041.0\Dockyard.exe");
        if (!_dragOnly && File.Exists(LogPath)) File.Delete(LogPath);
        // Left by a Dockyard that went down with grouped windows: it would restore those groups instead of
        // opening the empty frame the test starts from.
        if (File.Exists(SessionPath))
        {
            Console.WriteLine($"{SessionPath} exists: start Dockyard once and close its groups first.");
            return 1;
        }
        var features = args.Contains("--features");
        var stress = args.Contains("--stress");
        var backup = features || stress || args.Contains("--perf") ? BackUpData() : null;
        if (backup is not null) WriteTestSettings();

        _app = Process.Start(_exe)!;
        try
        {
            _frame = WaitForFrame();
            SetWindowPos(_frame, 0, 100, 60, 1400, 850, SWP_NOZORDER);
            if (args.Contains("--external-only")) RunExternalDrag();
            else if (_dragOnly) RunDockDrag();
            else if (features) RunFeatures();
            else if (stress) RunStress();
            else if (args.Contains("--perf")) RunPerf();
            else Run();
        }
        catch (Exception ex)
        {
            Failures.Add($"aborted: {ex.Message}");
        }
        finally
        {
            if (!_app.HasExited) _app.Kill();
            foreach (var p in Spawned) if (!p.HasExited) p.Kill();
            foreach (var h in SpawnedWindows) PostMessage(h, 0x0010 /* WM_CLOSE */, 0, 0);
            // Lists only the test windows, all gone now; left behind it would stop the next run.
            File.Delete(SessionPath);
            if (backup is not null)
            {
                RestoreData(backup);
                using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (run?.GetValue("Dockyard") is string command && command.Contains(_exe)) run.DeleteValue("Dockyard");
            }
        }

        Console.WriteLine();
        Console.WriteLine(Failures.Count == 0 ? "ALL PASSED" : $"{Failures.Count} FAILED:\n  " + string.Join("\n  ", Failures));
        return Failures.Count == 0 ? 0 : 1;
    }

    private static void RunDockDrag()
    {
        var a = Spawn("dyt-drag-a", ProcessWindowStyle.Normal);
        var b = Spawn("dyt-drag-b", ProcessWindowStyle.Normal);
        void Tab(Subject subject, int x, int y, bool cancel = false)
        {
            Activate(_frame);
            Thread.Sleep(200);
            var tab = TabText(subject).Current.BoundingRectangle;
            Drag((int)(tab.Left + tab.Width / 2), (int)(tab.Top + tab.Height / 2), x, y, cancel: cancel);
        }
        Step("Shift external drag into empty frame", () => DragExternal(a), [a], [a]);
        Step("Shift external drag adds a tab", () => DragExternal(b), [a, b], [b]);
        var frame = Visible(_frame);
        Step("Escape cancels tab docking", () => Tab(a, frame.Right - 30, frame.Top + frame.Height / 2, true), [a, b], [b]);
        Step("tab drag splits right", () => Tab(a, frame.Right - 30, frame.Top + frame.Height / 2), [a, b], [a, b]);
        Step("tab drag merges panes", () =>
        {
            var r = Visible(b.Hwnd);
            Tab(a, r.Left + r.Width / 2, r.Top + r.Height / 2);
        }, [a, b], [a]);
        Step("tab drag outside releases original bounds", () => Tab(a, frame.Left - 40, frame.Top + 100),
            [b], [b], extra: () => Restored(a));
        Step("close releases external drag at pre-drag bounds", () => ClickTabClose(b), [], [], extra: () => Restored(b));
        RunExternalDrag();
    }

    private static void DragExternal(Subject subject, bool shift = true)
    {
        Activate(subject.Hwnd);
        Thread.Sleep(200);
        var r = Visible(subject.Hwnd);
        var f = Visible(_frame);
        if (shift) keybd_event(0x10, 0, 0, 0);
        try { Drag(r.Left + r.Width / 2, r.Top + 12, f.Left + f.Width / 2, f.Top + f.Height / 2); }
        finally { if (shift) keybd_event(0x10, 0, 2, 0); }
    }

    private static void RunExternalDrag()
    {
        foreach (var manual in new[] { false, true })
        {
            var custom = SpawnClientCaption(manual);
            Step($"Shift drag from client caption (manual={manual})", () => DragExternal(custom), [custom], [custom]);
            Step("release at pre-drag bounds", () => ClickTabClose(custom), [], [], extra: () => Restored(custom));
            Step("drag without Shift does not dock", () => DragExternal(custom, false), [], [],
                extra: () => AllIn(_frame, custom.Name).Any() ? "window was docked without Shift" : null);
        }
    }

    private static int RunClientCaption(bool manual)
    {
        var window = new System.Windows.Window
        {
            Title = manual ? "dyt-manual-caption" : "dyt-client-caption",
            Width = 600,
            Height = 400,
            Left = 300,
            Top = 200,
            WindowStyle = System.Windows.WindowStyle.None,
            Background = System.Windows.Media.Brushes.White,
        };
        var caption = new System.Windows.Controls.Border
        {
            Height = 30,
            Background = System.Windows.Media.Brushes.SlateGray,
            Child = new System.Windows.Controls.TextBlock { Text = window.Title },
        };
        var start = new System.Windows.Point();
        double left = 0, top = 0;
        caption.MouseLeftButtonDown += (_, e) =>
        {
            if (!manual) { window.DragMove(); return; }
            start = window.PointToScreen(e.GetPosition(window));
            left = window.Left;
            top = window.Top;
            caption.CaptureMouse();
        };
        caption.MouseMove += (_, e) =>
        {
            if (!manual || !caption.IsMouseCaptured) return;
            var point = window.PointToScreen(e.GetPosition(window));
            var scale = System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX;
            window.Left = left + (point.X - start.X) / scale;
            window.Top = top + (point.Y - start.Y) / scale;
        };
        caption.MouseLeftButtonUp += (_, _) => caption.ReleaseMouseCapture();
        var content = new System.Windows.Controls.DockPanel();
        System.Windows.Controls.DockPanel.SetDock(caption, System.Windows.Controls.Dock.Top);
        content.Children.Add(caption);
        content.Children.Add(new System.Windows.Controls.Border());
        window.Content = content;
        return new System.Windows.Application().Run(window);
    }

    private static Subject SpawnClientCaption(bool manual)
    {
        var name = manual ? "dyt-manual-caption" : "dyt-client-caption";
        var process = Process.Start(Path.Combine(AppContext.BaseDirectory, "Dockyard.UiTests.exe"),
            manual ? "--client-caption --manual-move" : "--client-caption")!;
        Spawned.Add(process);
        var hwnd = Wait(() => FindWindow(h => Pid(h) == process.Id && IsWindowVisible(h) && Title(h) == name), "client-caption fixture");
        return new Subject(name, hwnd, WindowRect(hwnd));
    }

    private static void Run()
    {
        var a = Spawn("dyt-a", ProcessWindowStyle.Normal);
        var b = Spawn("dyt-b", ProcessWindowStyle.Minimized);
        var c = Spawn("dyt-c", ProcessWindowStyle.Maximized);
        var d = Spawn("dyt-d", ProcessWindowStyle.Normal);
        var e = Spawn("dyt-e", ProcessWindowStyle.Normal); // a window outside the group

        // The case reported from real use: Task Manager runs elevated, so Dockyard cannot move it. The picker
        // must refuse it instead of adding a pane that stays blank. Checked first: the managed UIA client used
        // here stops seeing dialog contents after many dialogs.
        var tm = FindWindow(h => Title(h) == "タスク マネージャー" && IsWindowVisible(h));
        if (tm != 0)
        {
            var task = new Subject("タスク マネージャー", tm, WindowRect(tm));
            Step("Task Manager is refused when it runs elevated", () =>
            {
                Invoke(FindIn(_frame, "Add Window…"));
                var item = PickerItem(task);
                if (!item.Current.Name.Contains("administrator")) throw new Exception("not marked; this machine runs it unelevated");
                Select(item);
                Thread.Sleep(300);
                var add = PickerButton("Add");
                if (add.Current.IsEnabled) throw new Exception("Add stays enabled for a window that cannot be added");
                Invoke(PickerButton("Cancel"));
            }, members: [], active: [], extra: () => FindWindow(h => Pid(h) == _app.Id && Title(h) == "Choose a Window" && IsWindowVisible(h)) != 0
                ? "picker still open" : null);
        }

        // Regression: Win11 apps such as Paint ignore SetWindowPos(HWND_TOP) from another process, and the
        // group used to sink behind other apps right after such a window was added. Consoles do not show it.
        // Done first: the managed UIA client used here stops seeing dialog contents after many dialogs.
        if (SpawnPaint() is { } paint)
        {
            var front = Spawn("dyt-f", ProcessWindowStyle.Normal); // in front of everything, Paint included
            Step("add a Win11 app while another app is in front", () =>
            {
                Invoke(FindIn(_frame, "Add Window…"));
                Pick(paint);
            }, members: [paint], active: [paint], below: [front]);
            Step("remove the Win11 app again", () => ClickTabClose(paint), members: [], active: [],
                extra: () => Restored(paint));
        }

        Step("add a window to the empty group", () =>
        {
            Invoke(FindIn(_frame, "Add Window…"));
            Pick(a);
        }, members: [a], active: [a], below: [e]);

        Step("add a minimized window as a tab", () => AddVia(a, "Add as Tab…", b), members: [a, b], active: [b]);

        Step("switch tabs by clicking a tab", () => ClickTab(a), members: [a, b], active: [a]);

        Step("add a maximized window split to the right", () => AddVia(a, "Split Right…", c), members: [a, b, c], active: [a, c]);

        Step("split below", () => AddVia(c, "Split Down…", d), members: [a, b, c, d], active: [a, c, d]);

        Step("click the frame while another app is in front", () =>
        {
            var f = Visible(_frame);
            SetWindowPos(e.Hwnd, 0, f.Left + 200, f.Top + 100, 700, 500, SWP_NOZORDER);
            FocusByClick(e);
            Click(f.Left + 400, f.Top + Caption / 2);
        }, members: [a, b, c, d], active: [a, c, d], below: [e]);

        Step("click inside a member while another app is in front", () =>
        {
            FocusByClick(e);
            // The title bar: a click in a console's text area starts a QuickEdit selection without activating it.
            var r = Visible(d.Hwnd);
            Click(r.Left + r.Width / 2, r.Top + 12);
            Thread.Sleep(300);
            if (GetForegroundWindow() != d.Hwnd) throw new Exception("the click did not activate dyt-d");
        }, members: [a, b, c, d], active: [a, c, d], below: [e]);
        Spawned.First(p => p.Id == Pid(e.Hwnd)).Kill();

        var before = Visible(_frame);
        Step("drag the frame by its title bar", () =>
        {
            var f = Visible(_frame);
            Drag(f.Left + 500, f.Top + Caption / 2, f.Left + 350, f.Top + Caption / 2 + 60);
        }, members: [a, b, c, d], active: [a, c, d]);
        Expect(Visible(_frame).Left < before.Left - 100, "frame did not move with the title-bar drag");

        Step("resize the frame", () =>
        {
            var f = Visible(_frame);
            SetWindowPos(_frame, 0, f.Left, f.Top, f.Width + 150, f.Height + 80, SWP_NOZORDER);
        }, members: [a, b, c, d], active: [a, c, d]);

        var widthBefore = Visible(a.Hwnd).Width;
        Step("drag a member's edge to move the divider", () =>
        {
            var r = Visible(a.Hwnd);
            Drag(r.Right + 2, r.Top + r.Height / 2, r.Right + 152, r.Top + r.Height / 2);
        }, members: [a, b, c, d], active: [a, c, d]);
        Expect(Math.Abs(Visible(a.Hwnd).Width - (widthBefore + 150)) < 30,
            $"divider drag: width {widthBefore} -> {Visible(a.Hwnd).Width}, expected about {widthBefore + 150}");

        Step("drag the divider far past the neighbour's minimum", () =>
        {
            var r = Visible(a.Hwnd);
            Drag(r.Right + 2, r.Top + r.Height / 2, Visible(_frame).Right - 10, r.Top + r.Height / 2);
        }, members: [a, b, c, d], active: [a, c, d]);

        Step("minimize the frame", () => Invoke(FindIn(_frame, "Minimize")), members: [], active: [],
            extra: () => new[] { a, b, c, d }.Where(s => IsWindowVisible(s.Hwnd)).Select(s => $"{s.Name} still visible").FirstOrDefault());

        Step("restore the frame", () =>
        {
            ShowWindow(_frame, SW_RESTORE);
            Activate(_frame);
        }, members: [a, b, c, d], active: [a, c, d]);

        Step("maximize the frame", () => Invoke(FindIn(_frame, "Maximize")), members: [a, b, c, d], active: [a, c, d]);
        Step("un-maximize the frame", () => Invoke(FindIn(_frame, "Restore")), members: [a, b, c, d], active: [a, c, d]);

        Step("a member's app exits", () => Spawned.First(p => p.Id == Pid(d.Hwnd)).Kill(), members: [a, b, c], active: [a, c]);

        Step("remove an inactive tab with ×", () => ClickTabClose(b), members: [a, c], active: [a, c],
            extra: () => Restored(b));


        // The session file is written once the layout has settled.
        Thread.Sleep(1500);
        Step("restart after a forced exit restores the group", () =>
        {
            _app.Kill();
            _app.WaitForExit();
            _app = Process.Start(_exe)!;
            _frame = WaitForFrame();
        }, members: [a, c], active: [a, c]);

        Step("close the group", () => Invoke(FindIn(_frame, "Close")), members: [], active: [],
            extra: () => Restored(a) ?? Restored(c) ?? (File.Exists(SessionPath) ? "session file left after closing" : null));
    }

    private static nint WaitForFrame() =>
        Wait(() => FindWindow(h => Pid(h) == _app.Id && IsWindowVisible(h) && Title(h).StartsWith("[")), "group frame");

    // ---- steps and checks -----------------------------------------------------------------------

    private static void Step(string name, Action act, Subject[] members, Subject[] active, Subject[]? below = null, Func<string?>? extra = null)
    {
        Console.Write(Environment.GetEnvironmentVariable("DOCKYARD_TRACE") == "1" ? $"- {DateTime.Now:HH:mm:ss.fff} {name} ... " : $"- {name} ... ");
        try
        {
            act();
        }
        catch (Exception ex)
        {
            var at = ex.StackTrace?.Split(Environment.NewLine).FirstOrDefault(l => l.Contains("Dockyard.UiTests"))?.Trim();
            Fail(name, $"action threw: {ex.Message} ({at})");
            return;
        }

        string? problem = null;
        var deadline = DateTime.Now.AddSeconds(4);
        do
        {
            Thread.Sleep(250);
            problem = (members.Length > 0 ? Inspect(members, active, below ?? []) : null) ?? extra?.Invoke() ?? LogProblem();
        } while (problem is not null && DateTime.Now < deadline);

        if (problem is null) Console.WriteLine("ok");
        else Fail(name, problem + "\n      z-order: " + string.Join(" > ", ZOrder().Take(8).Select(h => h == _frame ? "FRAME" : Title(h))));
    }

    private static void Fail(string step, string problem)
    {
        Console.WriteLine($"FAIL: {problem}");
        Failures.Add($"{step}: {problem}");
        // Leave the screen as it is for a while so the failure can be looked at.
        if (Environment.GetEnvironmentVariable("DYT_PAUSE") == "1") Thread.Sleep(30000);
    }

    private static void Expect(bool condition, string problem)
    {
        if (condition) return;
        Console.WriteLine($"  FAIL: {problem}");
        Failures.Add(problem);
    }

    /// <summary>The group's invariants. Returns a description of the first violation, or null.</summary>
    private static string? Inspect(Subject[] members, Subject[] active, Subject[] below)
    {
        if (_app.HasExited) return "Dockyard exited";
        if (!IsWindowVisible(_frame) || IsIconic(_frame)) return "frame not visible";
        // A maximized frame overhangs the screen; what the user sees is the monitor's work area.
        var f = IsZoomed(_frame) ? WorkArea(_frame) : Visible(_frame);
        int left = f.Left + Margin, right = f.Right - Margin, top = f.Top + Caption + TabBar, bottom = f.Bottom - Margin;

        foreach (var m in members)
        {
            if (!IsWindow(m.Hwnd)) return $"{m.Name} is gone";
            if (IsIconic(m.Hwnd)) return $"{m.Name} is minimized";
            if (IsZoomed(m.Hwnd)) return $"{m.Name} is maximized";
            if (!IsWindowVisible(m.Hwnd)) return $"{m.Name} is hidden";
            var r = Visible(m.Hwnd);
            if (r.Left < left - 3 || r.Right > right + 3 || r.Top < top - 3 || r.Bottom > bottom + 3)
                return $"{m.Name} at {r} is outside the frame's content area ({left},{top})-({right},{bottom})";
        }

        var z = ZOrder();
        var frameIndex = z.IndexOf(_frame);
        foreach (var m in active)
            if (z.IndexOf(m.Hwnd) > frameIndex) return $"{m.Name} is behind the frame (blank pane)";
        foreach (var m in members.Except(active))
            if (z.IndexOf(m.Hwnd) < frameIndex) return $"inactive tab {m.Name} is in front of the frame";
        var topActive = active.Min(m => z.IndexOf(m.Hwnd));
        for (var i = topActive; i < frameIndex; i++)
            if (active.All(m => m.Hwnd != z[i]) && Overlaps(Visible(z[i]), f))
                return $"'{Title(z[i])}' sits between the tabs and the frame";
        foreach (var o in below)
            if (z.IndexOf(o.Hwnd) < topActive || z.IndexOf(o.Hwnd) < frameIndex) return $"{o.Name} is still in front of the group";

        foreach (var m1 in active)
            foreach (var m2 in active)
                if (m1 != m2 && Area(Intersect(Visible(m1.Hwnd), Visible(m2.Hwnd))) > 3 * Math.Max(Visible(m1.Hwnd).Height, Visible(m1.Hwnd).Width))
                    return $"{m1.Name} and {m2.Name} overlap";

        var covered = active.Sum(m => (double)Area(Visible(m.Hwnd)));
        var content = (double)(right - left) * (bottom - top);
        if (covered < content * 0.85) return $"panes cover only {covered / content:P0} of the frame";
        return null;
    }

    private static string? Restored(Subject s)
    {
        var r = Visible(s.Hwnd);
        if (!IsWindowVisible(s.Hwnd) || IsIconic(s.Hwnd)) return $"{s.Name} not shown after release";
        var w = new RECT();
        GetWindowRect(s.Hwnd, out w);
        return Math.Abs(w.Left - s.Original.Left) > 3 || Math.Abs(w.Top - s.Original.Top) > 3 ||
               Math.Abs(w.Width - s.Original.Width) > 3 || Math.Abs(w.Height - s.Original.Height) > 3
            ? $"{s.Name} released at {w}, expected original {s.Original}"
            : null;
    }

    private static string? LogProblem() =>
        !_dragOnly && File.Exists(LogPath) && new FileInfo(LogPath).Length > 0 ? "error log: " + File.ReadLines(LogPath).First() : null;

    private static bool Overlaps(RECT a, RECT b) => Area(Intersect(a, b)) > 0;

    private static RECT Intersect(RECT a, RECT b) => new()
    {
        Left = Math.Max(a.Left, b.Left), Top = Math.Max(a.Top, b.Top),
        Right = Math.Min(a.Right, b.Right), Bottom = Math.Min(a.Bottom, b.Bottom),
    };

    private static long Area(RECT r) => r.Width > 0 && r.Height > 0 ? (long)r.Width * r.Height : 0;

    // ---- driving the UI -------------------------------------------------------------------------

    private static Subject Spawn(string name, ProcessWindowStyle style)
    {
        var p = Process.Start(new ProcessStartInfo("conhost.exe", $"cmd.exe /k title {name}")
        {
            UseShellExecute = true,
            WindowStyle = style,
        })!;
        // A console window reports the attached client (cmd), not conhost, as its owner process.
        var hwnd = Wait(() => FindWindow(h => Title(h) == name && IsWindowVisible(h)), name);
        Spawned.Add(Process.GetProcessById((int)Pid(hwnd)));
        Spawned.Add(p);
        Thread.Sleep(300);
        // What a release should bring back: the restored bounds, also for windows that start minimized/maximized.
        var original = IsIconic(hwnd) || IsZoomed(hwnd) ? NormalBounds(hwnd) : WindowRect(hwnd);
        return new Subject(name, hwnd, original);
    }

    /// <summary>
    /// Brings another window forward the way a user does, by clicking it. SetForegroundWindow alone can raise
    /// it without moving the focus, which no user action produces.
    /// </summary>
    private static void FocusByClick(Subject s)
    {
        Activate(s.Hwnd);
        Thread.Sleep(200);
        var r = Visible(s.Hwnd);
        Click(r.Left + r.Width / 2, r.Top + 12);
        Thread.Sleep(400);
        if (GetForegroundWindow() != s.Hwnd) throw new Exception($"could not focus {s.Name}");
    }

    /// <summary>Opens a new Paint window, or returns null when Paint is not available.</summary>
    private static Subject? SpawnPaint()
    {
        var before = new HashSet<nint>();
        EnumWindows((h, _) => { before.Add(h); return true; }, 0);
        try
        {
            Process.Start(new ProcessStartInfo("mspaint.exe") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        var hwnd = Wait(() => FindWindow(h => !before.Contains(h) && IsWindowVisible(h) && Title(h).EndsWith("ペイント")), "Paint", 15);
        Thread.Sleep(1500); // let Paint finish laying itself out
        SpawnedWindows.Add(hwnd);
        return new Subject(Title(hwnd), hwnd, WindowRect(hwnd));
    }

    private static RECT WindowRect(nint hwnd)
    {
        GetWindowRect(hwnd, out var r);
        return r;
    }

    /// <summary>Opens the pane's "+" menu (the pane holding <paramref name="inPane"/>), picks an item, then a window.</summary>
    private static void AddVia(Subject inPane, string menuItem, Subject pick)
    {
        OpenPickerVia(inPane, menuItem);
        Pick(pick);
    }

    private static void OpenPickerVia(Subject inPane, string menuItem)
    {
        var r = Visible(inPane.Hwnd);
        var plus = AllIn(_frame, "Add Window or Layout")
            .Where(p => Math.Abs(p.Current.BoundingRectangle.Bottom - r.Top) < 12)
            .OrderBy(p => Math.Abs(p.Current.BoundingRectangle.Right - r.Right))
            .First();
        Invoke(plus);
        var item = Wait(() => AppElements().SelectMany(w => w.FindAll(TreeScope.Descendants, Named(menuItem)).Cast<AutomationElement>()).FirstOrDefault(),
            $"menu item {menuItem}");
        Invoke(item);
    }

    private static void Pick(Subject s)
    {
        Select(PickerItem(s));
        Invoke(PickerButton("Add"));
    }

    private static void Select(AutomationElement item) =>
        ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();

    private static AutomationElement PickerItem(Subject s)
    {
        var items = () => Raw(PickerElement()).Where(e => e.Current.ControlType == ControlType.ListItem).ToList();
        AutomationElement item;
        try
        {
            item = Wait(() => items().FirstOrDefault(i => i.Current.Name.StartsWith(s.Name + " ")), $"{s.Name} in picker", 5);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"{s.Name} not in picker; listed: " + string.Join(" | ", items().Select(i => i.Current.Name)));
        }
        return item;
    }

    // Looked up fresh each time: an element obtained from the dialog's own handle sometimes exposes no list items.
    // UIA lists an owned dialog as a child of its owner, so look under the frame first.
    /// <summary>All descendants through the raw view; FindAll on the control view sometimes comes back empty.</summary>
    private static IEnumerable<AutomationElement> Raw(AutomationElement root)
    {
        for (var c = TreeWalker.RawViewWalker.GetFirstChild(root); c is not null; c = TreeWalker.RawViewWalker.GetNextSibling(c))
        {
            yield return c;
            foreach (var d in Raw(c)) yield return d;
        }
    }

    private static AutomationElement PickerButton(string name)
    {
        var all = Raw(PickerElement()).ToList();
        return all.FirstOrDefault(e => e.Current.Name == name)
            ?? throw new Exception($"no '{name}' in picker; raw: " + string.Join(" | ", all.Select(e => $"{e.Current.ControlType.ProgrammaticName}:{e.Current.Name}")));
    }

    // Elements of pickers closed earlier can linger there, so only take the one backed by a live window.
    private static AutomationElement PickerElement() => Wait(() =>
        AutomationElement.FromHandle(_frame).FindAll(TreeScope.Children, Named("Choose a Window")).Cast<AutomationElement>()
            .Concat(AutomationElement.RootElement.FindAll(TreeScope.Children, Named("Choose a Window")).Cast<AutomationElement>())
            .FirstOrDefault(e => e.Current.NativeWindowHandle is var h && h != 0 && IsWindow(h) && IsWindowVisible(h) && Pid(h) == _app.Id),
        "picker");

    private static AutomationElement TabText(Subject s)
    {
        var r = Visible(s.Hwnd);
        return AllIn(_frame, s.Name)
            .Where(t => t.Current.ControlType == ControlType.Text)
            .OrderBy(t => Math.Abs(t.Current.BoundingRectangle.Bottom - r.Top) + Math.Abs(t.Current.BoundingRectangle.Left - r.Left) / 4)
            .First();
    }

    private static void ClickTab(Subject s)
    {
        var b = TabText(s).Current.BoundingRectangle;
        Click((int)(b.Left + b.Width / 2), (int)(b.Top + b.Height / 2));
    }

    private static void ClickTabClose(Subject s)
    {
        var t = TabText(s).Current.BoundingRectangle;
        var close = AllIn(_frame, "×")
            .Where(x => Math.Abs(x.Current.BoundingRectangle.Top + x.Current.BoundingRectangle.Height / 2 - (t.Top + t.Height / 2)) < 8 &&
                        x.Current.BoundingRectangle.Left >= t.Right - 2)
            .OrderBy(x => x.Current.BoundingRectangle.Left)
            .First();
        Invoke(close);
    }

    private static AutomationElement FindIn(nint hwnd, string name) =>
        Wait(() => AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, Named(name)), $"'{name}'");

    private static IEnumerable<AutomationElement> AllIn(nint hwnd, string name) =>
        AutomationElement.FromHandle(hwnd).FindAll(TreeScope.Descendants, Named(name)).Cast<AutomationElement>();

    private static IEnumerable<AutomationElement> AppElements() =>
        AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, _app.Id))
            .Cast<AutomationElement>();

    private static PropertyCondition Named(string name) => new(AutomationElement.NameProperty, name);

    private static void Invoke(AutomationElement e) => ((InvokePattern)e.GetCurrentPattern(InvokePattern.Pattern)).Invoke();

    private static T Wait<T>(Func<T?> find, string what, double seconds = 10)
    {
        var deadline = DateTime.Now.AddSeconds(seconds);
        while (true)
        {
            var found = find();
            if (found is not null && !(found is nint h && h == 0)) return found;
            if (DateTime.Now > deadline) throw new TimeoutException($"timed out waiting for {what}");
            Thread.Sleep(100);
        }
    }
}
