using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Dockyard.Application;

namespace Dockyard.Infrastructure.Internal;

/// <summary><see cref="ILayoutShortcuts"/> that writes .lnk files through the shell's IShellLink.</summary>
internal sealed class LayoutShortcuts : ILayoutShortcuts
{
    /// <summary>The alias installer/AppxManifest.xml declares: the package's own exe path cannot be started from a shortcut.</summary>
    private const string PackageAlias = "dockyard.exe";

    public string? CreateOnDesktop(string layoutName)
    {
        try
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var path = Path.Combine(desktop, $"Dockyard - {FileSafe(layoutName)}.lnk");
            var exe = Environment.ProcessPath!;
            var link = (IShellLink)new ShellLink();
            link.SetPath(AppPackage.IsPackaged
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", PackageAlias)
                : exe);
            link.SetArguments(LaunchCommand.Format(layoutName));
            link.SetDescription($"Open the Dockyard layout \"{layoutName}\"");
            // The alias is an empty file with no icon of its own.
            link.SetIconLocation(exe, 0);
            ((IPersistFile)link).Save(path, false);
            return path;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            Log.Error($"Create shortcut for {layoutName}", ex);
            return null;
        }
    }

    private static string FileSafe(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    /// <summary>Only the methods used are named; the others keep their place, which is what the call goes by.</summary>
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, nint findData, uint flags);
        void GetIDList(out nint pidl);
        void SetIDList(nint pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(nint window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
