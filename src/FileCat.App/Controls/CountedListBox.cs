using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Threading;

namespace FileCat.App.Controls;

/// <summary>
/// VoiceOver says a row's position and the list's size, as in "(2 of 5)", only for a row whose parent is its list: it
/// counted FileCat's file rows but read an ACL row as a bare "table row", because Avalonia's list box reaches its rows
/// through its template's scroll viewer and panel. Under the macOS bridge this list's rows are its own children, in
/// item order, and each row names the list as its parent; elsewhere it is an ordinary list box.
/// </summary>
internal class CountedListBox : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    protected override AutomationPeer OnCreateAutomationPeer() =>
        NativeLiveRegionBridges.UseMacBridge ? new ListPeer(this) : base.OnCreateAutomationPeer();

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new Row();

    private sealed class Row : ListBoxItem
    {
        protected override Type StyleKeyOverride => typeof(ListBoxItem);

        protected override AutomationPeer OnCreateAutomationPeer() =>
            NativeLiveRegionBridges.UseMacBridge ? new RowPeer(this) : base.OnCreateAutomationPeer();
    }

    private sealed class ListPeer : ListBoxAutomationPeer
    {
        private bool _queued;

        public ListPeer(CountedListBox owner) : base(owner)
        {
            // Rows come and go while the list is laid out; list them again afterwards, once per change burst.
            owner.ContainerPrepared += (_, _) => RowsChanged();
            owner.ContainerIndexChanged += (_, _) => RowsChanged();
            owner.ContainerClearing += (_, _) => RowsChanged();
        }

        private void RowsChanged()
        {
            if (_queued) return;
            _queued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _queued = false;
                InvalidateChildren();
            }, DispatcherPriority.Loaded);
        }

        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore()
        {
            var rows = new List<AutomationPeer>(Owner.ItemCount);
            for (int index = 0; index < Owner.ItemCount; index++)
                if (Owner.ContainerFromIndex(index) is { IsVisible: true } row) rows.Add(GetOrCreate(row));
            return rows;
        }
    }

    private sealed class RowPeer(ListBoxItem owner) : ListItemAutomationPeer(owner)
    {
        protected override AutomationPeer? GetParentCore() =>
            ItemsControl.ItemsControlFromItemContainer(Owner) is { } list ? CreatePeerForElement(list) : base.GetParentCore();
    }
}
