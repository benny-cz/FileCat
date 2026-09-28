using System.Runtime.InteropServices;

namespace FileCat.PrivilegedHost;

/// <summary>
/// The consent window (ADR-14). UAC identifies only this program, so the broker itself shows exactly which steps it
/// will run; Cancel is the default, so a reflexive Enter never approves a plan.
/// </summary>
internal static unsafe partial class ConsentDialog
{
    private const int RunButton = 100, IdCancel = 2, IdOk = 1;
    private const uint AllowCancellation = 0x0008, ExpandedByDefault = 0x0080, SizeToContent = 0x01000000;
    private const uint CancelButtonFlag = 0x0008, OkButtonFlag = 0x0001;
    private static readonly nint ShieldIcon = unchecked((ushort)-4), ErrorIcon = unchecked((ushort)-2);

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TaskDialogButton
    {
        public int Id;
        public nint Text;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct TaskDialogConfig
    {
        public uint Size;
        public nint Parent, Instance;
        public uint Flags, CommonButtons;
        public nint WindowTitle, MainIcon, MainInstruction, Content;
        public uint ButtonCount;
        public nint Buttons;
        public int DefaultButton;
        public uint RadioButtonCount;
        public nint RadioButtons;
        public int DefaultRadioButton;
        public nint VerificationText, ExpandedInformation, ExpandedControlText, CollapsedControlText, FooterIcon, Footer, Callback, CallbackData;
        public uint Width;
    }

    /// <summary>True only when the user chose "Run as administrator".</summary>
    public static bool Ask(string instruction, string content, string steps, string footer)
    {
        var strings = new List<nint>();
        nint S(string s)
        {
            var p = Marshal.StringToHGlobalUni(s);
            strings.Add(p);
            return p;
        }
        try
        {
            var button = new TaskDialogButton { Id = RunButton, Text = S("Run as administrator") };
            var config = new TaskDialogConfig
            {
                Size = (uint)sizeof(TaskDialogConfig),
                Flags = AllowCancellation | ExpandedByDefault | SizeToContent,
                CommonButtons = CancelButtonFlag,
                WindowTitle = S("FileCat — administrator operation"),
                MainIcon = ShieldIcon,
                MainInstruction = S(instruction),
                Content = S(content),
                ButtonCount = 1,
                Buttons = (nint)(&button),
                DefaultButton = IdCancel,
                ExpandedInformation = S(steps),
                ExpandedControlText = S("Hide the steps"),
                CollapsedControlText = S("Show the steps"),
                Footer = S(footer),
            };
            int pressed;
            int hr = TaskDialogIndirect(&config, &pressed, null, null);
            if (hr != 0) return FallbackAsk(instruction + "\n\n" + content + "\n\n" + steps + "\n\n" + footer);
            return pressed == RunButton;
        }
        catch (EntryPointNotFoundException)
        {
            return FallbackAsk(instruction + "\n\n" + content + "\n\n" + steps + "\n\n" + footer);
        }
        finally
        {
            foreach (var p in strings) Marshal.FreeHGlobal(p);
        }
    }

    /// <summary>Reports why nothing was run (the user just approved UAC and deserves an answer).</summary>
    public static void Refuse(string reason)
    {
        var strings = new List<nint>();
        nint S(string s)
        {
            var p = Marshal.StringToHGlobalUni(s);
            strings.Add(p);
            return p;
        }
        try
        {
            var config = new TaskDialogConfig
            {
                Size = (uint)sizeof(TaskDialogConfig),
                Flags = AllowCancellation | SizeToContent,
                CommonButtons = OkButtonFlag,
                WindowTitle = S("FileCat — administrator operation"),
                MainIcon = ErrorIcon,
                MainInstruction = S("Nothing was run"),
                Content = S(reason),
                DefaultButton = IdOk,
            };
            int pressed;
            if (TaskDialogIndirect(&config, &pressed, null, null) != 0) MessageBox(0, "Nothing was run.\n\n" + reason, "FileCat", 0x10);
        }
        catch (EntryPointNotFoundException)
        {
            MessageBox(0, "Nothing was run.\n\n" + reason, "FileCat", 0x10);
        }
        finally
        {
            foreach (var p in strings) Marshal.FreeHGlobal(p);
        }
    }

    private static bool FallbackAsk(string text)
    {
        const uint YesNo = 0x4, Warning = 0x30, SecondDefault = 0x100, TopMost = 0x40000;
        return MessageBox(0, text + "\n\nRun these steps as administrator?", "FileCat — administrator operation", YesNo | Warning | SecondDefault | TopMost) == 6;
    }

    [LibraryImport("comctl32.dll")]
    private static partial int TaskDialogIndirect(TaskDialogConfig* config, int* button, int* radioButton, int* verificationChecked);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint owner, string text, string caption, uint type);
}
