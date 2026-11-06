using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Dockyard.Application;
using Dockyard.Domain;
using Dockyard.Infrastructure.Internal.Logging;

namespace Dockyard.Infrastructure.Internal.RemoteDesktop;

/// <summary>
/// <see cref="IRemoteDesktops"/> with the sessions on a thread of their own. The UI thread also runs the
/// low-level keyboard and mouse hooks, which every app's input waits on: drawing a remote screen there would
/// slow the whole desktop down. The window events Dockyard ignores for its own process are reported here
/// instead, on the thread that created this service.
/// </summary>
internal sealed class RemoteDesktopService : IRemoteDesktops, IDisposable
{
    private readonly Action<WindowEvent, WindowHandle> _report;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private readonly ConcurrentDictionary<nint, RemoteDesktopConnection> _open = new();
    private Control? _thread;

    /// <param name="report">Raises a window event for a session window, on the UI thread.</param>
    public RemoteDesktopService(Action<WindowEvent, WindowHandle> report)
    {
        _report = report;
    }

    /// <summary>Whether the window is a session opened here. Safe to call from any thread.</summary>
    public bool IsSession(nint window) => _open.ContainsKey(window);

    public RemoteDesktopConnection? ConnectionOf(WindowHandle window) => _open.GetValueOrDefault(window.Value);

    public WindowHandle Open(RemoteDesktopConnection connection)
    {
        var password = CredentialStore.Read(connection.Name);
        return (WindowHandle)SessionThread().Invoke(() =>
        {
            if (!RdpClientHost.IsAvailable) return WindowHandle.None;
            try
            {
                var window = new RemoteDesktopWindow(connection, password);
                var handle = window.Handle;
                _open[handle] = connection;
                window.TextChanged += (_, _) => Report(WindowEvent.TitleChanged, handle);
                window.HandleDestroyed += (_, _) =>
                {
                    _open.TryRemove(handle, out _);
                    Report(WindowEvent.Destroyed, handle);
                };
                window.Show();
                return new WindowHandle(handle);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                Log.Error($"Open remote desktop {connection.Host}", ex);
                return WindowHandle.None;
            }
        });
    }

    public void Close(WindowHandle window)
    {
        if (!_open.ContainsKey(window.Value)) return;
        _thread?.BeginInvoke(() => (Control.FromHandle(window.Value) as Form)?.Close());
    }

    public void SavePassword(string connectionName, string? password) => CredentialStore.Write(connectionName, password);

    public void RenamePassword(string oldName, string newName)
    {
        if (CredentialStore.Read(oldName) is not { } password) return;
        CredentialStore.Write(newName, password);
        CredentialStore.Write(oldName, null);
    }

    private void Report(WindowEvent evt, nint window)
    {
        if (_ui is null) _report(evt, new WindowHandle(window));
        else _ui.Post(_ => _report(evt, new WindowHandle(window)), null);
    }

    /// <summary>Starts the sessions' thread on first use and returns a control to run work on it.</summary>
    private Control SessionThread()
    {
        if (_thread is not null) return _thread;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            System.Windows.Forms.Application.ThreadException += (_, e) => Log.Error("Remote desktop", e.Exception);
            _thread = new Control();
            _thread.CreateControl();
            ready.Set();
            System.Windows.Forms.Application.Run();
        })
        {
            IsBackground = true,
            Name = "Remote Desktop",
        };
        // ActiveX controls live in a single-threaded apartment.
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return _thread!;
    }

    public void Dispose()
    {
        _thread?.BeginInvoke(() =>
        {
            foreach (var form in System.Windows.Forms.Application.OpenForms.OfType<RemoteDesktopWindow>().ToList()) form.Close();
            System.Windows.Forms.Application.ExitThread();
        });
    }
}
