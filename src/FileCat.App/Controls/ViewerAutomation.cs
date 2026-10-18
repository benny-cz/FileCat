using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace FileCat.App.Controls;

/// <summary>
/// The text viewer as a read-only document (TV-10). Its value is the selection, or the visible rows when nothing
/// is selected, so a screen reader can read what is on screen; scrolling raises a value change.
/// </summary>
internal sealed class TextViewerAutomationPeer(TextViewer owner) : ControlAutomationPeer(owner), IValueProvider
{
    private string? _announced = owner.SemanticValue, _status = owner.SemanticStatus;
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    protected override string GetClassNameCore() => "TextViewer";

    protected override string? GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : "Text viewer";

    protected override bool IsKeyboardFocusableCore() => true;

    public bool IsReadOnly => true;

    public string? Value => owner.SemanticValue;
    protected override string? GetHelpTextCore() => owner.SemanticStatus;
    protected override string? GetItemStatusCore() => owner.SemanticStatus;

    public void SetValue(string? value) => throw new InvalidOperationException("The viewer is read-only.");

    public void AnnounceContent()
    {
        var value = Value;
        var status = owner.SemanticStatus;
        var previousValue = _announced;
        var previousStatus = _status;
        _announced = value; _status = status;
        if (value != previousValue) RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, previousValue, value);
        if (Value != value || owner.SemanticStatus != status) return;
        if (status != previousStatus)
        {
            RaisePropertyChangedEvent(AutomationElementIdentifiers.HelpTextProperty, previousStatus, status);
            if (Value != value || owner.SemanticStatus != status) return;
            RaisePropertyChangedEvent(AutomationElementIdentifiers.ItemStatusProperty, previousStatus, status);
        }
    }
}

/// <summary>The hex view as a read-only document whose value describes the cursor byte and its row.</summary>
internal sealed class HexViewAutomationPeer(HexView owner) : ControlAutomationPeer(owner), IValueProvider
{
    private string? _announced = owner.DescribeCursor(), _status = owner.SemanticStatus;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    protected override string GetClassNameCore() => "HexView";

    protected override string? GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : "Hex view";

    protected override bool IsKeyboardFocusableCore() => true;

    public bool IsReadOnly => true;

    public string? Value => owner.DescribeCursor();
    protected override string? GetHelpTextCore() => owner.SemanticStatus;
    protected override string? GetItemStatusCore() => owner.SemanticStatus;

    public void SetValue(string? value) => throw new InvalidOperationException("The hex view is read-only.");

    public void AnnounceCursor()
    {
        var value = Value;
        var status = owner.SemanticStatus;
        var previousValue = _announced;
        var previousStatus = _status;
        _announced = value; _status = status;
        if (value != previousValue) RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, previousValue, value);
        if (Value != value || owner.SemanticStatus != status) return;
        if (status != previousStatus)
        {
            RaisePropertyChangedEvent(AutomationElementIdentifiers.HelpTextProperty, previousStatus, status);
            if (Value != value || owner.SemanticStatus != status) return;
            RaisePropertyChangedEvent(AutomationElementIdentifiers.ItemStatusProperty, previousStatus, status);
        }
    }
}

/// <summary>Bounded, read-only semantics for the drawn binary comparison surface.</summary>
internal sealed class HexCompareViewAutomationPeer(HexCompareView owner) : ControlAutomationPeer(owner), IValueProvider
{
    private string? _announced = owner.SemanticValue;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;
    protected override string GetClassNameCore() => "HexCompareView";
    protected override string? GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : "Bytes of both files";
    protected override bool IsKeyboardFocusableCore() => true;
    public bool IsReadOnly => true;
    public string? Value => owner.SemanticValue;
    public void SetValue(string? value) => throw new InvalidOperationException("The comparison is read-only.");

    public void AnnounceSelection()
    {
        var value = Value;
        if (value == _announced) return;
        var previous = _announced;
        _announced = value;
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, previous, value);
    }
}
