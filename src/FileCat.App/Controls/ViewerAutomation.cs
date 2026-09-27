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
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    protected override string GetClassNameCore() => "TextViewer";

    protected override string? GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : "Text viewer";

    protected override bool IsKeyboardFocusableCore() => true;

    public bool IsReadOnly => true;

    public string? Value => owner.GetSelectedOrVisibleText();

    public void SetValue(string? value) => throw new InvalidOperationException("The viewer is read-only.");

    public void AnnounceContent() => RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, null, Value);
}

/// <summary>The hex view as a read-only document whose value describes the cursor byte and its row.</summary>
internal sealed class HexViewAutomationPeer(HexView owner) : ControlAutomationPeer(owner), IValueProvider
{
    private string? _announced;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    protected override string GetClassNameCore() => "HexView";

    protected override string? GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : "Hex view";

    protected override bool IsKeyboardFocusableCore() => true;

    public bool IsReadOnly => true;

    public string? Value => owner.DescribeCursor();

    public void SetValue(string? value) => throw new InvalidOperationException("The hex view is read-only.");

    public void AnnounceCursor()
    {
        var value = Value;
        if (value == _announced) return;
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, _announced, value);
        _announced = value;
    }
}
