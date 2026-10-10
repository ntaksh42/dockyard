using Dockyard.Domain;
using Dockyard.Infrastructure;

namespace Dockyard.App.Controllers.Tests;

/// <summary>
/// Drives a controller through random sequences of user and system actions and checks the group's invariants
/// once things have settled. Seeds are fixed; the seed, step and action are in every assertion message.
/// </summary>
public class GroupControllerPropertyTests
{
    private const nint Frame = 1000, Other = 900;

    public static IEnumerable<object[]> Seeds => Enumerable.Range(1, 40).Select(i => new object[] { i });

    private sealed class World
    {
        public readonly FakeWindowSystem Ws = new();
        public readonly FakeView View = new(Frame);
        public readonly ManualScheduler Scheduler = new();
        public readonly GroupController C;
        public readonly Random Rng;
        public nint Next = 1;
        /// <summary>Bounds each window had when it was created, which is what releasing it must restore.</summary>
        public readonly Dictionary<nint, PixelRect> Created = [];

        public World(int seed)
        {
            Rng = new Random(seed);
            Ws.AddToZOrder(Frame);
            Ws.Add(Other, new PixelRect(0, 0, 800, 600), "other app");
            C = new GroupController(View, Ws, Scheduler);
            View.Connect(C, Scheduler);
            C.OnSurfaceResized();
        }

        public List<nint> Members => C.Members.ToList();
        public List<Pane> Panes => C.Layout!.Panes.Select(p => p.Pane).ToList();
        public List<nint> Actives => Panes.Select(p => p.ActiveWindow).OfType<nint>().ToList();
        public T Pick<T>(IReadOnlyList<T> items) => items[Rng.Next(items.Count)];

        public nint NewWindow()
        {
            var h = Next++;
            var bounds = new PixelRect(Rng.Next(0, 300), Rng.Next(0, 300), Rng.Next(400, 900), Rng.Next(400, 700));
            Ws.Add(h, bounds, $"w{h}");
            Created[h] = bounds;
            if (Rng.Next(5) == 0) Ws.Windows[h].MinWidth = Rng.Next(100, 500);
            if (Rng.Next(5) == 0) Ws.Windows[h].MinHeight = Rng.Next(80, 300);
            if (Rng.Next(6) == 0) Ws.Windows[h].Minimized = true;
            else if (Rng.Next(6) == 0) Ws.Windows[h].Maximized = true;
            return h;
        }

        /// <summary>The user drags a member: activates it, then moves it or pulls one or two of its edges.</summary>
        public string UserDrag(nint w)
        {
            Ws.BringToTop(w);
            C.OnForeground(w);
            C.OnMoveSizeStart(w);
            var r = Ws.GetVisibleBounds(w);
            int d() => Rng.Next(-200, 201);
            var moved = Rng.Next(4) switch
            {
                0 => Offset(r, d(), d()),                                                                   // title bar
                1 => r with { Right = Math.Max(r.Left + 1, r.Right + d()), Bottom = Math.Max(r.Top + 1, r.Bottom + d()) }, // bottom-right corner
                2 => r with { Left = Math.Min(r.Right - 1, r.Left + d()) },                                 // left edge
                _ => r with { Top = Math.Min(r.Bottom - 1, r.Top + d()) },                                  // top edge
            };
            Ws.Windows[w].Bounds = FakeWindowSystem.BoundsFor(moved);
            C.OnLocationChanged(w);
            Scheduler.RunPosted();
            C.OnMoveSizeEnd(w);
            return $"drag {w} to {moved}";
        }

        private static PixelRect Offset(PixelRect r, int dx, int dy) => new(r.Left + dx, r.Top + dy, r.Right + dx, r.Bottom + dy);

        public string Step()
        {
            var members = Members;
            var action = members.Count == 0 ? Rng.Next(2) : Rng.Next(13);
            switch (action)
            {
                case 0:
                {
                    var w = NewWindow();
                    C.Adopt(w, Pick(Panes), null);
                    return $"adopt {w} as tab";
                }
                case 1:
                {
                    var w = NewWindow();
                    var side = (DockSide)Rng.Next(4);
                    var pane = Pick(Panes);
                    // An empty root pane only takes tabs (its "+" splits need an existing window).
                    C.Adopt(w, pane, pane.Windows.Count == 0 ? null : side);
                    return $"adopt {w} split {side}";
                }
                case 2:
                {
                    var w = Pick(members);
                    C.Remove(w);
                    return $"remove {w}";
                }
                case 3:
                {
                    var w = Pick(members);
                    C.ActivateTab(w);
                    return $"tab click {w}";
                }
                case 4:
                {
                    // Also reaches inactive tabs, as Alt+Tab does.
                    var w = Pick(members);
                    Ws.BringToTop(w);
                    C.OnForeground(w);
                    return $"foreground {w}";
                }
                case 5:
                {
                    var w = Pick(members);
                    Ws.Close(w);
                    C.OnDestroyed(w);
                    return $"app {w} exits";
                }
                case 6:
                {
                    if (C.Layout!.Splitters.Count == 0) return "no splitter";
                    var s = Pick(C.Layout.Splitters);
                    var horizontal = s.Split.Orientation == Orientation.Horizontal;
                    var from = horizontal ? s.SplitBounds.X : s.SplitBounds.Y;
                    var len = horizontal ? s.SplitBounds.Width : s.SplitBounds.Height;
                    C.DragSplitter(s, from - 100 + Rng.NextDouble() * (len + 200));
                    return "drag splitter";
                }
                case 7:
                {
                    View.Surface = new Size(Rng.Next(50, 2400), Rng.Next(50, 1400));
                    C.OnSurfaceResized();
                    return $"resize frame to {View.Surface}";
                }
                case 8:
                {
                    View.Origin = (Rng.Next(-500, 2000), Rng.Next(-300, 1000));
                    C.OnFrameMoved();
                    return $"move frame to {View.Origin}";
                }
                case 9:
                {
                    return UserDrag(Pick(Actives));
                }
                case 10:
                {
                    Ws.BringToTop(Other);
                    if (C.OnFrameZChanging(new FrameZChange(ZPlacement.Top)) is { } after) Ws.Stack(after, [Frame]);
                    else Ws.BringToTop(Frame);
                    return "frame clicked";
                }
                case 11:
                {
                    View.Minimized = true;
                    C.OnFrameMinimized();
                    View.Minimized = false;
                    C.OnFrameRestored();
                    return "minimize and restore frame";
                }
                default:
                {
                    var w = Pick(members);
                    Ws.Windows[w].Title = $"t{Rng.Next(1000)}";
                    C.OnTitleChanged(w);
                    return $"retitle {w}";
                }
            }
        }

        /// <summary>Lets deferred work and the settle timer run until nothing is pending.</summary>
        public void Settle()
        {
            for (var i = 0; i < 6; i++)
            {
                Scheduler.RunPosted();
                Scheduler.FireTimers();
            }
        }

        public void Check(string at)
        {
            var layout = C.Layout!;
            var members = Members;
            Assert.True(new HashSet<nint>(members).SetEquals(layout.Panes.SelectMany(p => p.Pane.Windows)), $"{at}: members differ from layout");
            Assert.True(new HashSet<nint>(members).SetEquals(View.LastRender!.Titles.Keys), $"{at}: rendered tabs differ");

            var frameZ = Ws.Z(Frame);
            var actives = Actives;
            foreach (var p in layout.Panes)
            {
                var target = View.ToScreen(p.Content);
                foreach (var w in p.Pane.Windows)
                {
                    var win = Ws.Windows[w];
                    Assert.True(win.Visible && !win.Minimized && !win.Maximized, $"{at}: {w} hidden, minimized or maximized");
                    var r = Ws.GetVisibleBounds(w);
                    Assert.True(Near(r.Left, target.Left) && Near(r.Top, target.Top) && Near(r.Right, target.Right) && Near(r.Bottom, target.Bottom),
                        $"{at}: {w} at {r}, pane content at {target}");
                    if (actives.Contains(w)) Assert.True(Ws.Z(w) < frameZ, $"{at}: active {w} behind the frame");
                    else Assert.True(Ws.Z(w) > frameZ, $"{at}: inactive {w} in front of the frame");
                }
            }
        }

        // A window with a minimum size may overflow its pane by the layout's rounding (one unit).
        private static bool Near(int a, int b) => Math.Abs(a - b) <= 1;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void RandomUse_KeepsEveryMemberPlacedAndStacked(int seed)
    {
        var world = new World(seed);
        for (var step = 0; step < 150; step++)
        {
            var action = world.Step();
            world.Settle();
            world.Check($"seed {seed} step {step} ({action})");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Closing_AfterRandomUse_PutsEveryWindowBack(int seed)
    {
        var world = new World(seed);
        for (var step = 0; step < 60; step++)
        {
            world.Step();
            world.Settle();
        }
        var remaining = world.Members;

        world.C.OnClosing();

        Assert.Empty(world.C.Members);
        foreach (var w in remaining)
        {
            Assert.True(world.Ws.Windows[w].Visible, $"seed {seed}: {w} hidden after close");
            Assert.Equal(world.Created[w], world.Ws.GetBounds(w));
        }
    }
}
