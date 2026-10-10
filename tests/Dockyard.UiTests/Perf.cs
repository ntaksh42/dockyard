using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using static Dockyard.UiTests.Win32;

namespace Dockyard.UiTests;

/// <summary>
/// "--perf" measures how closely the member windows follow the frame while it is dragged, resized and
/// maximized, and how much processor time Dockyard spends doing it. Prints numbers rather than pass/fail.
/// </summary>
internal static partial class Program
{
    private static void RunPerf()
    {
        // WPF windows rather than consoles: a console snaps its size to whole character cells, which shows up
        // as a few pixels off on every resize whatever Dockyard does.
        var a = SpawnClientCaption(false);
        var b = SpawnClientCaption(true);
        Step("build two panes", () =>
        {
            Invoke(FindIn(_frame, "Add Window…"));
            Pick(a);
            Settle();
            AddVia(a, "Split Right…", b);
        }, [a, b], [a, b]);
        Settle();

        // a | b side by side: what stays fixed relative to the frame while it moves or is resized from the right
        // edge — the outer edges of the panes and the gap between them. Taken once everything has settled.
        int[] Shape()
        {
            RECT f = Visible(_frame), l = Visible(a.Hwnd), r = Visible(b.Hwnd);
            return [l.Left - f.Left, l.Top - f.Top, f.Bottom - l.Bottom, r.Top - f.Top, f.Bottom - r.Bottom, f.Right - r.Right, r.Left - l.Right];
        }
        int[] shape = [];
        int Error() => Shape().Zip(shape, (now, then) => Math.Abs(now - then)).Max();
        var blanks = new List<double>();

        // Drags with the real mouse, sampling how far the members are from where they belong after every step.
        void Measure(string name, int x1, int y1, int x2, int y2)
        {
            shape = Shape();
            var start = Visible(_frame);
            var errors = new List<int>();
            blanks.Clear();
            var cpu = _app.TotalProcessorTime;
            var clock = Stopwatch.StartNew();
            SetCursorPos(x1, y1);
            Thread.Sleep(100);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            Thread.Sleep(100);
            const int steps = 60;
            for (var i = 1; i <= steps; i++)
            {
                SetCursorPos(x1 + (x2 - x1) * i / steps, y1 + (y2 - y1) * i / steps);
                Thread.Sleep(16);
                errors.Add(Error());
                blanks.Add(Blank());
            }
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
            var settled = SettleTime(() => Error() <= 2);
            _app.Refresh();
            var end = Visible(_frame);
            Report($"{name} (frame moved {end.Left - start.Left},{end.Top - start.Top}, grew {end.Width - start.Width})",
                errors, settled, (_app.TotalProcessorTime - cpu).TotalMilliseconds, clock.ElapsedMilliseconds);
            ReportBlank(blanks);
        }

        // The baseline: nothing happening, then the pointer moving over the desktop away from the group.
        Thread.Sleep(2000);
        _app.Refresh();
        var idle = _app.TotalProcessorTime;
        Thread.Sleep(2000);
        _app.Refresh();
        Console.WriteLine($"  nothing happening: Dockyard CPU {(_app.TotalProcessorTime - idle).TotalMilliseconds:F0} ms in 2 s");
        idle = _app.TotalProcessorTime;
        for (var i = 0; i < 120; i++)
        {
            SetCursorPos(1700 + i % 40 * 3, 1100 - i % 30 * 3);
            Thread.Sleep(16);
        }
        _app.Refresh();
        Console.WriteLine($"  pointer moving elsewhere: Dockyard CPU {(_app.TotalProcessorTime - idle).TotalMilliseconds:F0} ms in 2 s");

        var f = Visible(_frame);
        Measure("frame drag (caption)", f.Left + 300, f.Top + 15, f.Left + 700, f.Top + 215);
        Settle();
        f = Visible(_frame);
        Measure("frame resize (right edge)", f.Right - 2, f.Top + f.Height / 2, f.Right + 300, f.Top + f.Height / 2);
        Settle();
        f = Visible(_frame);
        Measure("frame resize (top edge, the frame's own border)", f.Left + f.Width / 2, f.Top + 2, f.Left + f.Width / 2, f.Top - 150);
        Settle();

        // Dragging the splitter: only the frame's own drawing changes, and both panes.
        f = Visible(_frame);
        var r = Visible(a.Hwnd);
        var cpu = _app.TotalProcessorTime;
        blanks.Clear();
        SetCursorPos(r.Right + 2, r.Top + r.Height / 2);
        Thread.Sleep(80);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        for (var i = 1; i <= 60; i++)
        {
            SetCursorPos(r.Right + 2 - 250 * i / 60, r.Top + r.Height / 2);
            Thread.Sleep(16);
            blanks.Add(Blank());
        }
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        _app.Refresh();
        Console.WriteLine($"  splitter drag: Dockyard CPU {(_app.TotalProcessorTime - cpu).TotalMilliseconds:F0} ms");
        ReportBlank(blanks);
        Settle();

        // Maximize and restore: how long until the members fill their panes again.
        foreach (var label in new[] { "maximize", "restore" })
        {
            cpu = _app.TotalProcessorTime;
            Invoke(FindIn(_frame, "☐"));
            // Watched for a whole second: a pane can look right, then go blank again.
            var clock = Stopwatch.StartNew();
            int samples = 0, bad = 0;
            long lastBad = -1;
            string? seen = null;
            blanks.Clear();
            while (clock.ElapsedMilliseconds < 1000)
            {
                samples++;
                blanks.Add(Blank());
                if (Inspect([a, b], [a, b], []) is { } problem)
                {
                    bad++;
                    lastBad = clock.ElapsedMilliseconds;
                    seen ??= problem;
                }
                Thread.Sleep(5);
            }
            _app.Refresh();
            Console.WriteLine($"  {label}: windows misplaced in {bad} of {samples} samples, last at {lastBad} ms ({seen ?? "-"}), Dockyard CPU {(_app.TotalProcessorTime - cpu).TotalMilliseconds:F0} ms");
            ReportBlank(blanks);
            Settle();
        }
    }

    /// <summary>
    /// What the user sees: the share of the frame's pane area showing the empty-pane colour, that is covered
    /// neither by a window nor by anything drawn in its place.
    /// </summary>
    private static double Blank()
    {
        var f = IsZoomed(_frame) ? WorkArea(_frame) : Visible(_frame);
        int left = f.Left + Margin + 2, top = f.Top + Caption + TabBar + 2, right = f.Right - Margin - 2, bottom = f.Bottom - Margin - 2;
        if (right - left < 16 || bottom - top < 16) return 0;
        using var bitmap = new Bitmap(right - left, bottom - top, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(left, top, 0, 0, bitmap.Size);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[data.Stride];
            int samples = 0, blank = 0;
            for (var y = 0; y < bitmap.Height; y += 8)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                for (var x = 0; x < bitmap.Width; x += 8)
                {
                    samples++;
                    // #1F2328, the empty-pane colour (BGRA in memory).
                    if (Math.Abs(row[x * 4 + 2] - 0x1F) <= 2 && Math.Abs(row[x * 4 + 1] - 0x23) <= 2 && Math.Abs(row[x * 4] - 0x28) <= 2) blank++;
                }
            }
            return (double)blank / samples;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void ReportBlank(List<double> blanks)
    {
        if (blanks.Count == 0) return;
        Console.WriteLine($"    blank panes on screen: mean {blanks.Average():P1}, max {blanks.Max():P1}, " +
            $"{blanks.Count(b => b > 0.01) * 100.0 / blanks.Count:F0}% of {blanks.Count} looks showed more than 1%");
    }

    private static long SettleTime(Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 3000 && !done()) Thread.Sleep(5);
        return clock.ElapsedMilliseconds;
    }

    private static void Report(string name, List<int> errors, long settled, double cpu, long elapsed)
    {
        errors.Sort();
        var over = errors.Count(e => e > 2) * 100.0 / errors.Count;
        Console.WriteLine($"  {name}: lag mean {errors.Average():F1}px, p95 {errors[(int)(errors.Count * 0.95)]}px, max {errors[^1]}px, " +
            $"{over:F0}% of samples off by more than 2px, settled {settled} ms after release, Dockyard CPU {cpu:F0} ms in {elapsed} ms");
    }
}
