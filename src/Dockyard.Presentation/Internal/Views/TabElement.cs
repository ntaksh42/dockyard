using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace Dockyard.Presentation.Internal.Views;

/// <summary>
/// A pane's row (or column) of tabs. For UI Automation it is a tab control holding one selected tab, so screen
/// readers announce the tabs as a set and know which one the pane shows.
/// </summary>
internal sealed class TabStrip : StackPanel
{
    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer(TabStrip strip) : FrameworkElementAutomationPeer(strip), ISelectionProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tab;
        protected override string GetClassNameCore() => "DockyardTabStrip";

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Selection ? this : base.GetPattern(patternInterface);

        public bool CanSelectMultiple => false;
        public bool IsSelectionRequired => true;

        public IRawElementProviderSimple[] GetSelection() =>
            strip.Children.OfType<TabElement>().Where(t => t.IsSelected)
                .Select(t => ProviderFromPeer(CreatePeerForElement(t))).ToArray();
    }
}

/// <summary>
/// A tab in a group's tab bar. Drawn as a plain border, so it gets an automation peer of its own: a tab item named
/// after the window, which UI Automation clients can select to switch to it, as a click does.
/// </summary>
/// <param name="announce">The pane has just switched to this tab: tell UI Automation clients it is now selected.</param>
internal sealed class TabElement(string title, bool selected, bool announce, Action select) : Border
{
    private readonly Action _select = select;

    public string Title { get; } = title;
    public bool IsSelected { get; } = selected;

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        // Tab bars are rebuilt when the active tab changes, so the event comes from the new tab once it is shown.
        if (announce)
            Loaded += (_, _) =>
            {
                if (AutomationPeer.ListenerExists(AutomationEvents.SelectionItemPatternOnElementSelected))
                    UIElementAutomationPeer.CreatePeerForElement(this)?.RaiseAutomationEvent(AutomationEvents.SelectionItemPatternOnElementSelected);
            };
    }

    private sealed class Peer(TabElement tab) : FrameworkElementAutomationPeer(tab), ISelectionItemProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TabItem;
        protected override string GetNameCore() => tab.Title;
        protected override string GetClassNameCore() => "DockyardTab";

        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.SelectionItem ? this : base.GetPattern(patternInterface);

        public bool IsSelected => tab.IsSelected;

        public IRawElementProviderSimple? SelectionContainer =>
            tab.Parent is TabStrip strip && CreatePeerForElement(strip) is { } peer ? ProviderFromPeer(peer) : null;

        public void Select() => tab._select();

        // A pane shows exactly one tab: no second one can join the selection, nor can the shown one leave it.
        public void AddToSelection()
        {
            if (!tab.IsSelected) throw new InvalidOperationException("A pane shows one tab at a time.");
        }

        public void RemoveFromSelection()
        {
            if (tab.IsSelected) throw new InvalidOperationException("A pane always shows one of its tabs.");
        }
    }
}
