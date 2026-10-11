using System.Runtime.InteropServices;

namespace Dockyard.Infrastructure.Internal.Launching;

/// <summary>Whether Dockyard runs from its MSIX package (installer/) or from the build output.</summary>
internal static class AppPackage
{
    private const int AppModelErrorNoPackage = 15700;

    public static bool IsPackaged
    {
        get
        {
            var length = 0u;
            return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint length, char[]? name);
}
