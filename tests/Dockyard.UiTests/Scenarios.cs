using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using Microsoft.Win32;
using static Dockyard.UiTests.Win32;

namespace Dockyard.UiTests;

/// <summary>
/// "--features" checks the menus, saved layouts, settings and restore paths end to end; "--stress" hammers a
/// group with fast shortcuts, forced exits, window churn and repeated opening and closing. Both rewrite the
/// settings and saved layouts for the run and put the user's files back afterwards.
/// </summary>
internal static partial class Program
{
    private static readonly string[] DataFiles = ["settings.json", "layouts.json", "last-groups.json"];
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string DataFile(string name) => Path.Combine(Path.GetDirectoryName(LogPath)!, name);

    private static Dictionary<string, string?> BackUpData() =>
        DataFiles.ToDictionary(f => f, f => File.Exists(DataFile(f)) ? File.ReadAllText(DataFile(f)) : null);

    private static void RestoreData(Dictionary<string, string?> saved)
    {
        foreach (var (file, text) in saved)
            if (text is null) File.Delete(DataFile(file));
            else File.WriteAllText(DataFile(file), text);
    }

    /// <summary>Settings for the run: the client-caption fixture plays an app with controls in its title bar.</summary>
    private static void WriteTestSettings()
    {
        File.WriteAllText(DataFile("settings.json"), """{ "TabBarPlacement": "Top", "TitleBarApps": ["Dockyard.UiTests"] }""");
        File.Delete(DataFile("layouts.json"));
        File.Delete(DataFile("last-groups.json"));
    }

    // ---- features -----------------------------------------------------------------------------------

    private static void RunFeatures()
    {
        var a = Spawn("dyt-a", ProcessWindowStyle.Normal);
        var b = Spawn("dyt-b", ProcessWindowStyle.Normal);
        var c = Spawn("dyt-c", ProcessWindowStyle.Normal);

        Step("build a group", () =>
        {
            Invoke(FindIn(_frame, "Add Window…"));
            Pick(a);
            Settle();
            AddVia(a, "Add as Tab…", b);
            Settle();
            AddVia(b, "Split Right…", c);
        }, [a, b, c], [b, c]);

        Step("rename from the caption menu", () =>
        {
            CaptionMenu(_frame, "Rename Group");
            Thread.Sleep(500);
            TypeLetters("qa");
            Press(0x0D);
        }, [a, b, c], [b, c], extra: () => Title(_frame).StartsWith("[qa]") ? null : $"title is '{Title(_frame)}'");

        Step("saving twice under one name keeps one layout", () =>
        {
            CaptionMenu(_frame, "Save Layout");
            Thread.Sleep(600);
            CaptionMenu(_frame, "Save Layout");
        }, [a, b, c], [b, c], extra: () => Layouts().Count(l => Name(l) == "qa") == 1 &&
            Layouts().First(l => Name(l) == "qa")["TabBarPlacement"]?.GetValue<string>() == "Top" ? null : "layouts.json: " + ReadData("layouts.json"));

        Step("the + menu lists the shortcuts and layouts", () =>
        {
            // UI Automation does not expose the key text (InputGestureText); that is checked on screen.
            OpenPlus(b);
            MenuElement("Keyboard Shortcuts", "Next tab");
            CloseMenus();
            OpenPlus(b);
            MenuElement("Open Layout", "qa");
            CloseMenus();
        }, [a, b, c], [b, c]);

        Step("Ctrl+Alt+Left switches to the previous tab", () =>
        {
            FocusByClick(b);
            Keys(0x11, 0x12, 0x25);
        }, [a, b, c], [a, c], extra: () => GetForegroundWindow() == a.Hwnd ? null : $"focus on '{Title(GetForegroundWindow())}'");

        Step("Ctrl+Alt+Shift+Right moves to the next pane", () => Keys(0x11, 0x12, 0x10, 0x27), [a, b, c], [a, c],
            extra: () => GetForegroundWindow() == c.Hwnd ? null : $"focus on '{Title(GetForegroundWindow())}'");

        Step("Ctrl+Alt+W takes the window out", () => Keys(0x11, 0x12, 0x57), [a, b], [a], extra: () => Restored(c));
        Step("add it back", () => AddVia(a, "Split Right…", c), [a, b, c], [a, c]);

        var x = SpawnClientCaption(false);
        Step("tab bar over the title bar for this group", () =>
        {
            AddVia(c, "Split Down…", x);
            Settle();
            CaptionMenu(_frame, "Tab Bar", "Over the Title Bar");
        }, [], [], extra: () =>
        {
            if (FindWindow(h => Pid(h) == _app.Id && IsWindowVisible(h) && Title(h) == "Dockyard Tabs") == 0) return "no tab overlay";
            var top = Visible(_frame).Top + Caption;
            if (Math.Abs(Visible(a.Hwnd).Top - top) > 3) return $"dyt-a top {Visible(a.Hwnd).Top}, expected {top} (tabs over its title bar)";
            // The fixture is listed as an app with controls in its title bar: its pane keeps the tab row above.
            var gap = Visible(x.Hwnd).Top - Visible(c.Hwnd).Bottom;
            return Math.Abs(gap - (4 + TabBar)) > 4 ? $"title-bar app sits {gap}px below the pane above, expected {4 + TabBar}" : null;
        });
        Step("the placement is saved with the layout", () => CaptionMenu(_frame, "Save Layout"), [], [],
            extra: () => Layouts().First(l => Name(l) == "qa")["TabBarPlacement"]?.GetValue<string>() == "TitleBar" ? null : "not saved");
        Step("back to tabs above", () =>
        {
            ClickTabClose(x);
            CaptionMenu(_frame, "Tab Bar", "Above the Window");
        }, [a, b, c], [a, c], extra: () => Restored(x));
        CaptionMenu(_frame, "Save Layout");

        Step("manage layouts: rename and delete", () =>
        {
            TrayMenu("Manage Layouts…");
            var window = Wait(() => AppWindow("Saved Layouts"), "Saved Layouts window");
            Select(Wait(() => Raw(window).FirstOrDefault(e => e.Current.ControlType == ControlType.ListItem && e.Current.Name.Contains("qa")), "qa in list"));
            var box = Raw(window).First(e => e.Current.ControlType == ControlType.Edit);
            ((ValuePattern)box.GetCurrentPattern(ValuePattern.Pattern)).SetValue("qb");
            Invoke(Raw(window).First(e => e.Current.Name == "Rename"));
            Thread.Sleep(500);
            if (Layouts().All(l => Name(l) != "qb")) throw new Exception("not renamed: " + ReadData("layouts.json"));
            Invoke(Raw(window).First(e => e.Current.Name == "Delete"));
            var ok = Wait(() => AppElements().SelectMany(Raw).FirstOrDefault(e => e.Current.Name == "OK" && e.Current.ControlType == ControlType.Button), "confirmation");
            Invoke(ok);
            Thread.Sleep(500);
            Invoke(Raw(window).First(e => e.Current.Name == "Close"));
        }, [a, b, c], [a, c], extra: () => Layouts().Count == 0 ? null : "not deleted: " + ReadData("layouts.json"));

        CaptionMenu(_frame, "Save Layout");
        var frameBefore = _frame;
        Step("close the group, then open the layout from a new group's + menu", () =>
        {
            Invoke(FindIn(_frame, "✕"));
            Thread.Sleep(800);
            TrayMenu("New Group");
            _frame = Wait(() => FindWindow(h => h != frameBefore && Pid(h) == _app.Id && IsWindowVisible(h) && Title(h).StartsWith("[Group")), "new frame");
            var empty = _frame;
            OpenPlusOfEmptyFrame();
            Invoke(MenuElement("Open Layout", "qa"));
            if (_frame != empty) throw new Exception("frame changed");
        }, [a, b, c], [a, c], extra: () => Title(_frame).StartsWith("[qa]") ? null : $"title '{Title(_frame)}'");

        // Session file: written once a drag is over, not during it.
        Step("no session writes while dragging the frame", () =>
        {
            Thread.Sleep(1500);
            var before = File.GetLastWriteTimeUtc(SessionPath);
            var f = Visible(_frame);
            SetCursorPos(f.Left + 300, f.Top + 15);
            Thread.Sleep(100);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            for (var i = 0; i < 60; i++)
            {
                SetCursorPos(f.Left + 300 + (i % 20) * 3, f.Top + 15 + (i % 10) * 2);
                Thread.Sleep(40);
            }
            var during = File.GetLastWriteTimeUtc(SessionPath);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
            if (during != before) throw new Exception("session file written during the drag");
            Thread.Sleep(1500);
            if (File.GetLastWriteTimeUtc(SessionPath) == before) throw new Exception("session file not written after the drag");
        }, [a, b, c], [a, c]);

        Step("launch a missing app when opening a layout", () =>
        {
            AddLayout("""{ "Name": "launch", "Bounds": { "Left": 300, "Top": 200, "Right": 1100, "Bottom": 800 },""" +
                """ "Layout": { "Tabs": [ { "ProcessName": "charmap", "Title": "x", "Path": "C:\\Windows\\System32\\charmap.exe" } ] } }""");
            SetOption("When opening a layout, start apps that are not open", true);
            TrayMenu("Open Layout", "launch");
        }, [], [], extra: () =>
        {
            var frame = FindWindow(h => Pid(h) == _app.Id && IsWindowVisible(h) && Title(h).StartsWith("[launch]"));
            if (frame == 0) return "no [launch] group";
            var map = FindWindow(h => IsWindowVisible(h) && Process.GetProcessById((int)Pid(h)).ProcessName == "charmap");
            if (map == 0) return "Character Map not started";
            var f = Visible(frame);
            var m = Visible(map);
            return m.Left >= f.Left && m.Right <= f.Right + 3 && m.Top >= f.Top + Caption && m.Bottom <= f.Bottom + 3 ? null : "Character Map is not in the group";
        });
        var launched = FindWindow(h => Pid(h) == _app.Id && IsWindowVisible(h) && Title(h).StartsWith("[launch]"));
        if (launched != 0) PostMessage(launched, 0x0010, 0, 0);
        Thread.Sleep(800);
        foreach (var p in Process.GetProcessesByName("charmap")) p.Kill();
        SetOption("When opening a layout, start apps that are not open", false);

        Step("start with Windows writes and removes the Run entry", () =>
        {
            SetOption("Start Dockyard when Windows starts", true);
            if (Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue("Dockyard") is not string command || !command.Contains("Dockyard.exe"))
                throw new Exception("no Run entry");
            SetOption("Start Dockyard when Windows starts", false);
            if (Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue("Dockyard") is not null) throw new Exception("Run entry left");
        }, [a, b, c], [a, c]);

        // After a restart of the PC the windows are new: the last groups are matched like a layout.
        SetOption("When Dockyard starts, open the groups from last time", true);
        Thread.Sleep(1500);
        Subject[] again = [];
        Step("open the last groups with new windows of the same apps", () =>
        {
            _app.Kill();
            _app.WaitForExit();
            foreach (var s in new[] { a, b, c }) Process.GetProcessById((int)Pid(s.Hwnd)).Kill();
            Thread.Sleep(500);
            again = [Spawn("dyt-a", ProcessWindowStyle.Normal), Spawn("dyt-b", ProcessWindowStyle.Normal), Spawn("dyt-c", ProcessWindowStyle.Normal)];
            _app = Process.Start(_exe)!;
            // A group from the session file opens first and closes again when its windows are gone.
            _frame = Wait(() => FindWindow(h => Pid(h) == _app.Id && IsWindowVisible(h) && Title(h).StartsWith("[qa] dyt")), "[qa] frame with windows");
        }, [], [], extra: () => again.Length == 3 ? ConsistentGroup(again, 2) : "not started");
        SetOption("When Dockyard starts, open the groups from last time", false);

        // A window let go by one group should not be taken by another group's waiting tab.
        Step("closing a minimized group does not feed another group's waiting tabs", () =>
        {
            TrayMenu("Open Layout", "qa");
            var second = Wait(() => FindWindow(h => h != _frame && Pid(h) == _app.Id && IsWindowVisible(h) && Title(h).StartsWith("[qa]")), "second [qa]");
            ShowWindow(_frame, 6 /* SW_MINIMIZE */);
            Thread.Sleep(800);
            PostMessage(_frame, 0x0010, 0, 0);
            Thread.Sleep(1500);
            // With no windows the frame's title is the group name alone.
            var title = Title(second);
            PostMessage(second, 0x0010, 0, 0);
            if (title != "[qa]") throw new Exception($"the other group took a window: '{title}'");
        }, [], [], extra: () => again.Select(s => IsWindowVisible(s.Hwnd) ? null : $"{s.Name} hidden").FirstOrDefault(p => p is not null));
    }

    // ---- stress -------------------------------------------------------------------------------------

    private static void RunStress()
    {
        var a = Spawn("dyt-a", ProcessWindowStyle.Normal);
        var b = Spawn("dyt-b", ProcessWindowStyle.Normal);
        var c = Spawn("dyt-c", ProcessWindowStyle.Normal);
        var d = Spawn("dyt-d", ProcessWindowStyle.Normal);
        var e = Spawn("dyt-e", ProcessWindowStyle.Normal);
        Step("build two panes", () =>
        {
            Invoke(FindIn(_frame, "Add Window…"));
            Pick(a);
            Settle();
            AddVia(a, "Add as Tab…", b);
            Settle();
            AddVia(b, "Add as Tab…", c);
            Settle();
            AddVia(c, "Split Right…", d);
            Settle();
            AddVia(d, "Split Down…", e);
        }, [a, b, c, d, e], [c, d, e]);
        Subject[] all = [a, b, c, d, e];
        var random = new Random(42);
        byte[][] combos = [[0x11, 0x12, 0x25], [0x11, 0x12, 0x27], [0x11, 0x12, 0x10, 0x25], [0x11, 0x12, 0x10, 0x27], [0x11, 0x12, 0x10, 0x26], [0x11, 0x12, 0x10, 0x28]];

        Step("300 random shortcuts as fast as they come", () =>
        {
            FocusByClick(c);
            for (var i = 0; i < 300; i++)
            {
                Keys(combos[random.Next(combos.Length)]);
                Thread.Sleep(random.Next(0, 25));
            }
        }, [], [], extra: () => ConsistentGroup(all, 3));

        // Auto-repeat of a held key: about 30 presses a second, without releases in between.
        Step("Ctrl+Alt+Right held for two seconds stops when released", () =>
        {
            FocusByClick(ActiveIn(all) == a.Hwnd ? a : ActiveIn(all) == b.Hwnd ? b : c);
            keybd_event(0x11, 0, 0, 0);
            keybd_event(0x12, 0, 0, 0);
            for (var i = 0; i < 60; i++)
            {
                keybd_event(0x27, 0, 1, 0);
                Thread.Sleep(33);
            }
            keybd_event(0x27, 0, 3, 0);
            keybd_event(0x12, 0, 2, 0);
            keybd_event(0x11, 0, 2, 0);
            Thread.Sleep(700);
            var after = string.Join(",", ZOrder().Take(8));
            Thread.Sleep(1500);
            if (string.Join(",", ZOrder().Take(8)) != after) throw new Exception("tabs still switching 0.7s after the key was released");
        }, [], [], extra: () => ConsistentGroup(all, 3));

        Step("a member exits in the middle of the shortcuts", () =>
        {
            FocusByClick(a.Hwnd == ActiveIn(all) ? a : d);
            for (var i = 0; i < 120; i++)
            {
                if (i == 60) Process.GetProcessById((int)Pid(b.Hwnd)).Kill();
                Keys(combos[random.Next(combos.Length)]);
                Thread.Sleep(random.Next(0, 20));
            }
        }, [], [], extra: () => ConsistentGroup([a, c, d, e], 3));
        all = [a, c, d, e];

        for (var round = 1; round <= 5; round++)
        {
            Thread.Sleep(1200);
            Step($"forced exit and restart, round {round}", () =>
            {
                _app.Kill();
                _app.WaitForExit();
                _app = Process.Start(_exe)!;
                _frame = WaitForFrame();
                Thread.Sleep(500);
                for (var i = 0; i < 40; i++) Keys(combos[random.Next(combos.Length)]);
            }, [], [], extra: () => ConsistentGroup(all, 3));
        }

        Step("splitter dragged back and forth 20 times", () =>
        {
            for (var i = 0; i < 20; i++)
            {
                var r = Visible(d.Hwnd);
                var x = r.Left - 2;
                Drag(x, r.Top + r.Height / 2, x + (i % 2 == 0 ? -250 : 250), r.Top + r.Height / 2, steps: 8);
            }
        }, [], [], extra: () => ConsistentGroup(all, 3));

        Step("tab bar placement cycled 9 times", () =>
        {
            string[] labels = ["Over the Title Bar", "Vertical, Left", "Above the Window"];
            for (var i = 0; i < 9; i++) CaptionMenu(_frame, "Tab Bar", labels[i % 3]);
        }, [], [], extra: () => ConsistentGroup(all, 3));

        // Windows of a saved layout's app appearing in a burst: only as many as there are waiting tabs join.
        var stormFrame = (nint)0;
        var fixtures = new List<Subject>();
        Step("six windows appear at once for three waiting tabs", () =>
        {
            AddLayout("""{ "Name": "storm", "Bounds": { "Left": 200, "Top": 150, "Right": 1200, "Bottom": 850 }, "Layout": { "Split": "Horizontal",""" +
                """ "First": { "Tabs": [ { "ProcessName": "Dockyard.UiTests", "Title": "dyt-client-caption" } ] },""" +
                """ "Second": { "Tabs": [ { "ProcessName": "Dockyard.UiTests", "Title": "x" }, { "ProcessName": "Dockyard.UiTests", "Title": "y" } ] } } }""");
            TrayMenu("Open Layout", "storm");
            stormFrame = Wait(() => FindWindow(h => Pid(h) == _app.Id && IsWindowVisible(h) && Title(h).StartsWith("[storm]")), "[storm] frame");
            // Started a little apart: several WPF windows starting at the very same moment sometimes crash in
            // D3D12 on this machine, with or without Dockyard running.
            var started = new List<Process>();
            for (var i = 0; i < 6; i++)
            {
                started.Add(Process.Start(Path.Combine(AppContext.BaseDirectory, "Dockyard.UiTests.exe"), "--client-caption")!);
                Thread.Sleep(150);
            }
            Spawned.AddRange(started);
            foreach (var p in started)
            {
                var hwnd = Wait(() => p.HasExited ? -1 : FindWindow(h => Pid(h) == p.Id && IsWindowVisible(h) && Title(h) == "dyt-client-caption"), "fixture", 30);
                if (hwnd != -1) fixtures.Add(new Subject("dyt-client-caption", hwnd, WindowRect(hwnd)));
            }
        }, [], [], extra: () =>
        {
            // One tab per member, plus the active title in the caption.
            var inGroup = AllIn(stormFrame, "dyt-client-caption").Count(t => t.Current.ControlType == ControlType.Text) - 1;
            var expected = Math.Min(3, fixtures.Count(s => IsWindow(s.Hwnd)));
            return inGroup == expected ? null : $"{inGroup} of {fixtures.Count} joined, expected {expected}";
        });
        Step("closing the storm group lets them go", () => PostMessage(stormFrame, 0x0010, 0, 0), [], [],
            extra: () => fixtures.Select(s => IsWindowVisible(s.Hwnd) ? null : "a fixture is hidden").FirstOrDefault(p => p is not null));

        CaptionMenu(_frame, "Save Layout");
        var name = Title(_frame)[1..Title(_frame).IndexOf(']')];
        Step("close and reopen the layout 8 times", () =>
        {
            for (var i = 0; i < 8; i++)
            {
                PostMessage(_frame, 0x0010, 0, 0);
                Wait(() => all.All(s => Restored(s) is null) ? "ok" : null, "windows released", 6);
                TrayMenu("Open Layout", name);
                var old = _frame;
                _frame = Wait(() => FindWindow(h => h != old && Pid(h) == _app.Id && IsWindowVisible(h) && Title(h).StartsWith($"[{name}]")), "reopened frame");
                Wait(() => ConsistentGroup(all, 3) is null ? "ok" : null, "group rebuilt", 6);
            }
        }, [], [], extra: () => ConsistentGroup(all, 3));

        Step("close the group", () => PostMessage(_frame, 0x0010, 0, 0), [], [],
            extra: () => all.Select(Restored).FirstOrDefault(p => p is not null) ?? (File.Exists(SessionPath) ? "session file left" : null));
    }

    /// <summary>Lets windows reach their panes before the next step looks for them by position.</summary>
    private static void Settle() => Thread.Sleep(1000);

    // ---- checks -------------------------------------------------------------------------------------

    /// <summary>
    /// Invariants that hold whatever tab is active: every member is inside the frame, exactly
    /// <paramref name="panes"/> of them are in front of the frame and do not overlap, the rest are behind it.
    /// </summary>
    private static string? ConsistentGroup(Subject[] members, int panes)
    {
        var active = members.Where(m => ZOrder().IndexOf(m.Hwnd) is var i && i >= 0 && i < ZOrder().IndexOf(_frame)).ToArray();
        if (active.Length != panes) return $"{active.Length} tabs in front of the frame, expected {panes}: " + string.Join(", ", active.Select(m => m.Name));
        return Inspect(members, active, []);
    }

    private static nint ActiveIn(Subject[] members) =>
        members.Select(m => m.Hwnd).FirstOrDefault(h => ZOrder().IndexOf(h) < ZOrder().IndexOf(_frame));

    /// <summary>Whether the window lies within the frame's surface.</summary>
    private static bool Covered(nint frame, nint window)
    {
        var f = Visible(frame);
        var w = Visible(window);
        return IsWindowVisible(window) && w.Left >= f.Left - 3 && w.Right <= f.Right + 3 && w.Top >= f.Top + Caption - 3 && w.Bottom <= f.Bottom + 3;
    }

    private static string ReadData(string name) => File.Exists(DataFile(name)) ? File.ReadAllText(DataFile(name)) : "";

    private static List<JsonNode> Layouts() =>
        (JsonNode.Parse(ReadData("layouts.json") is { Length: > 0 } text ? text : "[]") as JsonArray)!.Select(n => n!).ToList();

    private static string? Name(JsonNode layout) => layout["Name"]?.GetValue<string>();

    private static void AddLayout(string json)
    {
        var layouts = (JsonNode.Parse(ReadData("layouts.json") is { Length: > 0 } text ? text : "[]") as JsonArray)!;
        layouts.Add(JsonNode.Parse(json));
        File.WriteAllText(DataFile("layouts.json"), layouts.ToJsonString());
    }

    // ---- driving menus and settings -----------------------------------------------------------------

    /// <summary>Right-clicks the frame's caption and picks the item at <paramref name="path"/> (submenus first).</summary>
    private static void CaptionMenu(nint frame, params string[] path)
    {
        Activate(frame);
        Thread.Sleep(150);
        var f = Visible(frame);
        SetCursorPos(f.Right - 260, f.Top + Caption / 2);
        Thread.Sleep(80);
        mouse_event(0x08 /* RIGHTDOWN */, 0, 0, 0, 0);
        Thread.Sleep(40);
        mouse_event(0x10 /* RIGHTUP */, 0, 0, 0, 0);
        Invoke(MenuElement(path));
        Thread.Sleep(300);
    }

    private static void OpenPlus(Subject inPane)
    {
        var r = Visible(inPane.Hwnd);
        Invoke(AllIn(_frame, "+").Where(p => Math.Abs(p.Current.BoundingRectangle.Bottom - r.Top) < 12)
            .OrderBy(p => Math.Abs(p.Current.BoundingRectangle.Right - r.Right)).First());
    }

    private static void OpenPlusOfEmptyFrame() => Invoke(Wait(() => AllIn(_frame, "+").FirstOrDefault(), "+"));

    /// <summary>The menu item at the end of <paramref name="path"/>, expanding the submenus on the way.</summary>
    private static AutomationElement MenuElement(params string[] path)
    {
        AutomationElement? item = null;
        foreach (var name in path)
        {
            if (item is not null) ((ExpandCollapsePattern)item.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
            item = Wait(() => AppElements().SelectMany(w => w.FindAll(TreeScope.Descendants, Named(name)).Cast<AutomationElement>())
                .FirstOrDefault(e => e.Current.ControlType == ControlType.MenuItem), $"menu item {name}");
        }
        return item!;
    }

    private static void CloseMenus()
    {
        Press(0x1B);
        Press(0x1B);
        Thread.Sleep(200);
    }

    /// <summary>Opens the tray icon's menu through the taskbar's hidden-icons flyout and picks an item.</summary>
    private static void TrayMenu(params string[] path)
    {
        var taskbar = AutomationElement.RootElement.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"));
        var chevron = taskbar.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"))
            .Cast<AutomationElement>().First(e => e.Current.Name.Contains("非表示") || e.Current.Name.Contains("hidden", StringComparison.OrdinalIgnoreCase));
        List<AutomationElement>? Icons() => AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition).Cast<AutomationElement>()
            .Where(w => w.Current.ClassName.Contains("Overflow"))
            .SelectMany(w => w.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>())
            .Where(e => e.Current.Name.StartsWith("Dockyard")).ToList() is { Count: > 0 } found ? found : null;
        // The button toggles the flyout, which may have been left open: press it again if nothing shows up.
        List<AutomationElement>? icons = null;
        for (var attempt = 0; attempt < 3 && icons is null; attempt++)
        {
            Invoke(chevron);
            try { icons = Wait(Icons, "Dockyard tray icon", 3); }
            catch (TimeoutException) when (attempt < 2) { }
        }
        // An icon of a Dockyard that was killed stays listed until the pointer passes over it: try each one.
        // By keyboard (the menu key): a notification such as "Saved layout" can sit over the icon for a while.
        foreach (var icon in icons!.AsEnumerable().Reverse())
        {
            icon.SetFocus();
            Thread.Sleep(200);
            Keys(0x10, 0x79);
            Thread.Sleep(600);
            if (AppElements().Any(w => w.Current.ClassName.StartsWith("WindowsForms"))) break;
        }
        AutomationElement? item = null;
        foreach (var name in path)
        {
            if (item is not null)
            {
                if (item.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand)) ((ExpandCollapsePattern)expand).Expand();
                else Invoke(item);
                Thread.Sleep(300);
            }
            item = Wait(() => AppElements().SelectMany(Raw).FirstOrDefault(e => e.Current.Name == name && e.Current.ControlType == ControlType.MenuItem), $"tray item {name}");
        }
        Invoke(item!);
        Thread.Sleep(400);
    }

    /// <summary>Sets a check box in the settings window (opened from the tray), then closes the window.</summary>
    private static void SetOption(string label, bool on)
    {
        TrayMenu("Settings…");
        var window = Wait(() => AppWindow("Dockyard Settings"), "settings window");
        var box = Wait(() => Raw(window).FirstOrDefault(e => e.Current.Name == label && e.Current.IsEnabled), $"check box {label}");
        var toggle = (TogglePattern)box.GetCurrentPattern(TogglePattern.Pattern);
        if ((toggle.Current.ToggleState == ToggleState.On) != on) toggle.Toggle();
        Thread.Sleep(600);
        Invoke(Raw(window).First(e => e.Current.Name == "Close"));
        Thread.Sleep(300);
    }

    private static AutomationElement? AppWindow(string title) =>
        AppElements().FirstOrDefault(w => w.Current.Name == title && w.Current.NativeWindowHandle is var h && h != 0 && IsWindowVisible(h));

    // ---- keyboard -----------------------------------------------------------------------------------

    /// <summary>Presses the keys in order and releases them in reverse, like a person typing a shortcut.</summary>
    private static void Keys(params byte[] keys)
    {
        foreach (var k in keys) keybd_event(k, 0, IsExtended(k) ? 1u : 0, 0);
        Thread.Sleep(15);
        foreach (var k in keys.Reverse()) keybd_event(k, 0, (IsExtended(k) ? 1u : 0) | 2, 0);
    }

    private static bool IsExtended(byte key) => key is >= 0x21 and <= 0x28;

    private static void Press(byte key) => Keys(key);

    private static void TypeLetters(string text)
    {
        foreach (var ch in text.ToUpperInvariant()) Press((byte)ch);
    }
}
