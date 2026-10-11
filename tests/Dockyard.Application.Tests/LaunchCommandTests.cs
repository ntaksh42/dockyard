using System.Runtime.InteropServices;

namespace Dockyard.Application.Tests;

public class LaunchCommandTests
{
    [Fact]
    public void Format_QuotesTheName()
    {
        Assert.Equal("--layout \"Dev work\"", LaunchCommand.Format(new LayoutName("Dev work")));
    }

    [Theory]
    [InlineData("Dev")]
    [InlineData("Dev work")]
    [InlineData("say \"hi\"")]
    [InlineData(@"C:\temp\")]
    [InlineData(@"back\\slash\""quote")]
    public void Format_CanBeReadBackByTheSystemsCommandLineRules(string name)
    {
        var args = NativeCommandLine.Split(LaunchCommand.Format(new LayoutName(name)));

        Assert.Equal(new LayoutName(name), LaunchCommand.ParseLayout(args));
    }

    [Theory]
    [InlineData("--layout", "Dev", "Dev")]
    [InlineData("--LAYOUT", "Dev", "Dev")]
    [InlineData("--other", "Dev", null)]
    [InlineData("--layout", "", null)]
    public void ParseLayout_TakesTheArgumentAfterTheOption(string option, string value, string? expected)
    {
        Assert.Equal(expected, LaunchCommand.ParseLayout([option, value])?.Value);
    }

    [Fact]
    public void ParseLayout_WithNoArgumentAfterTheOption_AsksForNothing()
    {
        Assert.Null(LaunchCommand.ParseLayout(["--layout"]));
        Assert.Null(LaunchCommand.ParseLayout([]));
    }
}

/// <summary>
/// Splits a command line the way Windows does for a program's arguments, to check that what
/// <see cref="LaunchCommand.Format"/> writes into a shortcut is what the program gets.
/// </summary>
internal static class NativeCommandLine
{
    public static string[] Split(string commandLine)
    {
        // The first argument is the program's own name.
        var argv = CommandLineToArgvW("program " + commandLine, out var count);
        try
        {
            return Enumerable.Range(1, count - 1)
                .Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * nint.Size))!)
                .ToArray();
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
