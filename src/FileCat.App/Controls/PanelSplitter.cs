using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace FileCat.App.Controls;

/// <summary>
/// A panel boundary that screen readers present as a slider whose value is the first side's share in percent. As a
/// thumb whose name carried the share, NVDA and Orca read the whole resize strip after every arrow press and never the
/// boundary itself; a focused slider's value change is what every desktop reader speaks, once and briefly.
/// </summary>
internal sealed class PanelSplitter : GridSplitter
{
    private int _share = 50;
    private PanelSplitterAutomationPeer? _peer;

    protected override Type StyleKeyOverride => typeof(GridSplitter);

    /// <summary>The first side's share of the two panels beside the boundary, in percent.</summary>
    internal int Share
    {
        get => _share;
        set
        {
            if (_share == value) return;
            int previous = _share;
            _share = value;
            _peer?.RaiseShareChanged(previous, value);
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => _peer = new PanelSplitterAutomationPeer(this);
}

/// <summary>Arrow keys move the boundary; assistive technology reads the share but does not set it.</summary>
internal sealed class PanelSplitterAutomationPeer(PanelSplitter owner) : ControlAutomationPeer(owner), IRangeValueProvider
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

    protected override string GetClassNameCore() => "PanelSplitter";

    public bool IsReadOnly => true;
    public double Minimum => 0;
    public double Maximum => 100;
    public double Value => owner.Share;
    public double SmallChange => 1;
    public double LargeChange => 4;

    public void SetValue(double value) => throw new InvalidOperationException("Move the panel boundary with the arrow keys.");

    internal void RaiseShareChanged(int previous, int value) =>
        RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, (double)previous, (double)value);
}
