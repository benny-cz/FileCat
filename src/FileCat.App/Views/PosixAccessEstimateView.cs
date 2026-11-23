using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.Core.Permissions;

namespace FileCat.App.Views;

internal sealed class PosixAccessEstimateView : StackPanel
{
    // Linux limits a process to 65536 supplementary groups.
    private const int GroupLimit = 65536;
    private readonly TextBox _user = new() { MaxLength = 10 };
    private readonly TextBox _group = new() { MaxLength = 10 };
    private readonly TextBox _supplementary = new() { MaxLength = GroupLimit * 12 };
    private readonly CheckBox _complete = new() { Content = "I supplied every supplementary group" };
    private readonly TextBlock _problem = Line("");
    private readonly StackPanel _results = new() { Spacing = 4 };

    internal PosixAccessEstimateView(PermissionSnapshot snapshot, PosixAclEvaluation evaluator)
    {
        bool directory = snapshot.Binding?.Identity.Kind is PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot;
        (string Right, byte Bits)[] checks = directory
            ? [("List names", 4), ("Write permission on this directory", 2), ("Search known names", 1), ("Write and search together", 3)]
            : [("Read content", 4), ("Write content", 2), ("Execute permission", 1)];
        Spacing = 7;
        Children.Add(Line(Formatters.SafeName(snapshot.Subject.DisplayPath)));
        Children.Add(Line("Rules observed " + snapshot.Access.ObservedAt.ToLocalTime().ToString("G")));
        AddField(_user, "User ID");
        AddField(_group, "Effective group ID (optional)");
        AddField(_supplementary, "Supplementary group IDs, separated by commas");
        Children.Add(_complete);
        AutomationProperties.SetLiveSetting(_problem, AutomationLiveSetting.Polite);
        Children.Add(_problem);
        AutomationProperties.SetName(_results, "POSIX ACL estimates");
        Children.Add(_results);
        Children.Add(Line("Actual access is unknown. Parent permissions, mount restrictions, capabilities and other system policy can affect the result."));
        _complete.IsCheckedChanged += (_, _) => Refresh();
        Refresh();

        void AddField(TextBox field, string label)
        {
            AutomationProperties.SetName(field, label);
            Children.Add(new Label { Content = label, Target = field });
            Children.Add(field);
            field.PropertyChanged += (_, change) =>
            {
                if (change.Property == TextBox.TextProperty) Refresh();
            };
        }

        void Refresh()
        {
            _results.Children.Clear();
            _problem.IsVisible = true;
            if (!PosixAclEvaluation.TryParseId(_user.Text?.Trim(), out uint uid))
            {
                _problem.Text = "Enter a numeric user ID from 0 to 4294967294.";
                return;
            }
            var groups = new HashSet<uint>();
            bool hasEffectiveGroup = !string.IsNullOrWhiteSpace(_group.Text);
            if (hasEffectiveGroup)
            {
                if (!PosixAclEvaluation.TryParseId(_group.Text?.Trim(), out uint gid))
                {
                    _problem.Text = "Enter a numeric effective group ID from 0 to 4294967294.";
                    return;
                }
                groups.Add(gid);
            }
            if (!string.IsNullOrWhiteSpace(_supplementary.Text))
            {
                string[] ids = _supplementary.Text.Split(',', StringSplitOptions.TrimEntries);
                if (ids.Length > GroupLimit)
                {
                    _problem.Text = "Enter at most 65536 supplementary group IDs.";
                    return;
                }
                foreach (string id in ids)
                {
                    if (!PosixAclEvaluation.TryParseId(id, out uint gid))
                    {
                        _problem.Text = "Separate numeric supplementary group IDs with commas.";
                        return;
                    }
                    groups.Add(gid);
                }
            }
            _problem.IsVisible = false;
            foreach (var (right, bits) in checks)
            {
                var estimate = evaluator.Evaluate(bits, uid, groups, hasEffectiveGroup && _complete.IsChecked == true);
                var rule = estimate.AccessClass switch
                {
                    PosixAccessClass.Owner => "owner entry",
                    PosixAccessClass.NamedUser => "named user entry and mask",
                    PosixAccessClass.MatchingGroup => "matching group entries and any mask",
                    PosixAccessClass.Other => "others entry",
                    _ => "group membership is incomplete"
                };
                string result = estimate.Granted switch { true => "ACL grants", false => "ACL denies", null => "ACL result unknown" };
                _results.Children.Add(Line($"{right} · {result} · {rule}"));
            }
        }
    }

    private static TextBlock Line(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };
}
