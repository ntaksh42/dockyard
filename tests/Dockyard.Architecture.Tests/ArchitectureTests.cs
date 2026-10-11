using System.Reflection;

namespace Dockyard.Architecture.Tests;

public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(global::Dockyard.Domain.Pane).Assembly;
    private static readonly Assembly Application = typeof(global::Dockyard.Application.Workspace).Assembly;
    private static readonly Assembly Infrastructure = typeof(global::Dockyard.Infrastructure.PlatformServices).Assembly;
    private static readonly Assembly Presentation = typeof(global::Dockyard.Presentation.WpfShell).Assembly;

    private static string[] Exported(Assembly assembly) =>
        assembly.GetExportedTypes().Select(t => t.FullName!).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static string[] Sorted(string[] names) => names.OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static string[] DockyardReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Dockyard.")).OrderBy(n => n).ToArray();

    // ---- what each assembly exposes ------------------------------------------------------------------

    [Fact]
    public void Domain_ExposesOnlyItsVocabulary() => Assert.Equal(Sorted(PublicApi.Domain), Exported(Domain));

    [Fact]
    public void Application_ExposesOnlyTheWorkspaceAndItsPorts() => Assert.Equal(Sorted(PublicApi.Application), Exported(Application));

    [Fact]
    public void Infrastructure_ExposesOnlyThePlatformServices() => Assert.Equal(Sorted(PublicApi.Infrastructure), Exported(Infrastructure));

    [Fact]
    public void Presentation_ExposesOnlyTheShell() => Assert.Equal(Sorted(PublicApi.Presentation), Exported(Presentation));

    [Theory]
    [InlineData("Application")]
    [InlineData("Infrastructure")]
    [InlineData("Presentation")]
    public void PublicTypes_SitInTheRootNamespace(string layer)
    {
        var assembly = layer switch { "Application" => Application, "Infrastructure" => Infrastructure, _ => Presentation };
        var elsewhere = assembly.GetExportedTypes().Where(t => t.Namespace != $"Dockyard.{layer}").Select(t => t.FullName).ToArray();
        Assert.Empty(elsewhere);
    }

    // ---- who may reference whom ----------------------------------------------------------------------

    [Fact]
    public void Domain_ReferencesNoOtherLayer() => Assert.Empty(DockyardReferences(Domain));

    [Fact]
    public void Application_ReferencesOnlyDomain() =>
        Assert.True(DockyardReferences(Application).All(n => n == "Dockyard.Domain"), string.Join(", ", DockyardReferences(Application)));

    [Fact]
    public void Infrastructure_ReferencesOnlyApplicationAndDomain() =>
        Assert.True(DockyardReferences(Infrastructure).All(n => n is "Dockyard.Application" or "Dockyard.Domain"),
            string.Join(", ", DockyardReferences(Infrastructure)));

    [Fact]
    public void Presentation_ReferencesOnlyApplicationAndDomain() =>
        Assert.True(DockyardReferences(Presentation).All(n => n is "Dockyard.Application" or "Dockyard.Domain"),
            string.Join(", ", DockyardReferences(Presentation)));

    // ---- what the inner layers may use ---------------------------------------------------------------

    private static readonly string[] WindowsOnly =
        ["PresentationFramework", "PresentationCore", "WindowsBase", "System.Windows.Forms", "System.Drawing", "Microsoft.Windows.SDK.NET", "WinRT.Runtime"];

    [Theory]
    [InlineData("Domain")]
    [InlineData("Application")]
    public void InnerLayers_DoNotTouchWindowsOrWpf(string layer)
    {
        var assembly = layer == "Domain" ? Domain : Application;
        var used = assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(WindowsOnly.Contains).ToArray();
        Assert.Empty(used);
    }
}
