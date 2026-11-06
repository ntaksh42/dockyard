using System.Runtime.InteropServices;
using System.Text;
using Dockyard.Infrastructure.Internal.Logging;

namespace Dockyard.Infrastructure.Internal.RemoteDesktop;

/// <summary>
/// Passwords of Remote Desktop connections in the Windows Credential Manager (generic credentials of the
/// user), so they are never written to Dockyard's own files.
/// </summary>
internal static class CredentialStore
{
    private const string TargetPrefix = "Dockyard:RemoteDesktop:";
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public static string? Read(string connectionName)
    {
        if (!CredRead(Target(connectionName), CredTypeGeneric, 0, out var pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(pointer);
            return credential.CredentialBlobSize == 0
                ? null
                : Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / sizeof(char));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    /// <summary>Saves the password; null or empty deletes it.</summary>
    public static void Write(string connectionName, string? password)
    {
        var target = Target(connectionName);
        if (string.IsNullOrEmpty(password))
        {
            if (!CredDelete(target, CredTypeGeneric, 0) && Marshal.GetLastWin32Error() != ErrorNotFound)
                Log.Error("Delete remote desktop password", new System.ComponentModel.Win32Exception());
            return;
        }
        var blob = Encoding.Unicode.GetBytes(password);
        var buffer = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, buffer, blob.Length);
            var credential = new CREDENTIAL
            {
                Type = CredTypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = buffer,
                Persist = CredPersistLocalMachine,
                UserName = connectionName,
            };
            if (!CredWrite(ref credential, 0)) Log.Error("Save remote desktop password", new System.ComponentModel.Win32Exception());
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string Target(string connectionName) => TargetPrefix + connectionName;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public int Persist;
        public uint AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, int type, int flags, out nint credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(nint buffer);
}
