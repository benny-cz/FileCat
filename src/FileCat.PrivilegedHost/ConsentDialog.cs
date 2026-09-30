using System.Runtime.InteropServices;

namespace FileCat.PrivilegedHost;

/// <summary>
/// The consent window (ADR-14). UAC identifies only this program, so the broker itself shows exactly which steps it
/// will run; Cancel is the default, so a reflexive Enter never approves a plan.
/// </summary>
internal static unsafe partial class ConsentDialog
{
    private const int RunButton = 100, EarlierButton = 101, LaterButton = 102, IdCancel = 2, IdOk = 1;
    private const uint AllowCancellation = 0x0008, ExpandedByDefault = 0x0080, SizeToContent = 0x01000000;
    private const uint TdnCreated = 0, TdnButtonClicked = 2, TdmSetElementText = 0x0400 + 108, TdmEnableButton = 0x0400 + 111;
    // TASKDIALOG_ELEMENTS: TDE_CONTENT 0, TDE_EXPANDED_INFORMATION 1, TDE_FOOTER 2, TDE_MAIN_INSTRUCTION 3.
    private const nint TdeExpandedInformation = 1;

    // The pages of steps the open consent window moves through (one window at a time in this process).
    private static nint[] _pages = [];
    private static int _page;
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

    /// <summary>
    /// True only when the user chose "Run as administrator". <paramref name="pages"/> hold every step the plan runs; a
    /// plan of several pages gets Earlier and Later buttons, so no step is ever left out of view (release issue I17).
    /// </summary>
    public static bool Ask(string instruction, string content, IReadOnlyList<string> pages, string footer)
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
            _pages = pages.Select(S).ToArray();
            _page = 0;
            bool paged = pages.Count > 1;
            var buttons = paged
                ? stackalloc TaskDialogButton[]
                {
                    new() { Id = EarlierButton, Text = S("Earlier steps") },
                    new() { Id = LaterButton, Text = S("Later steps") },
                    new() { Id = RunButton, Text = S("Run as administrator") },
                }
                : stackalloc TaskDialogButton[] { new() { Id = RunButton, Text = S("Run as administrator") } };
            fixed (TaskDialogButton* first = buttons)
            {
                var config = new TaskDialogConfig
                {
                    Size = (uint)sizeof(TaskDialogConfig),
                    Flags = AllowCancellation | ExpandedByDefault | SizeToContent,
                    CommonButtons = CancelButtonFlag,
                    WindowTitle = S("FileCat — administrator operation"),
                    MainIcon = ShieldIcon,
                    MainInstruction = S(instruction),
                    Content = S(content),
                    ButtonCount = (uint)buttons.Length,
                    Buttons = (nint)first,
                    DefaultButton = IdCancel,
                    ExpandedInformation = _pages[0],
                    ExpandedControlText = S("Hide the steps"),
                    CollapsedControlText = S("Show the steps"),
                    Footer = S(footer),
                    Callback = paged ? (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint, int>)&OnNotify : 0,
                };
                int pressed;
                int hr = TaskDialogIndirect(&config, &pressed, null, null);
                if (hr != 0) return FallbackAsk(instruction, content, pages, footer);
                return pressed == RunButton;
            }
        }
        catch (EntryPointNotFoundException)
        {
            return FallbackAsk(instruction, content, pages, footer);
        }
        finally
        {
            _pages = [];
            foreach (var p in strings) Marshal.FreeHGlobal(p);
        }
    }

    /// <summary>Earlier and Later show another page of steps and keep the window open (S_FALSE).</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int OnNotify(nint window, uint notification, nint wParam, nint lParam, nint data)
    {
        if (notification == TdnCreated)
        {
            ShowPage(window);
            return 0;
        }
        if (notification != TdnButtonClicked || (int)wParam is not (EarlierButton or LaterButton)) return 0;
        _page = Math.Clamp(_page + ((int)wParam == LaterButton ? 1 : -1), 0, _pages.Length - 1);
        ShowPage(window);
        return 1;
    }

    private static void ShowPage(nint window)
    {
        SendMessage(window, TdmSetElementText, TdeExpandedInformation, _pages[_page]);
        SendMessage(window, TdmEnableButton, EarlierButton, _page > 0 ? 1 : 0);
        SendMessage(window, TdmEnableButton, LaterButton, _page < _pages.Length - 1 ? 1 : 0);
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

    /// <summary>
    /// Without the task dialog a message box asks; it cannot move through pages, so a plan that does not fit on one is
    /// refused rather than approved unseen.
    /// </summary>
    private static bool FallbackAsk(string instruction, string content, IReadOnlyList<string> pages, string footer)
    {
        const uint YesNo = 0x4, Warning = 0x30, SecondDefault = 0x100, TopMost = 0x40000;
        if (pages.Count > 1)
        {
            MessageBox(0, $"Nothing was run.\n\n{instruction}\n\n{content}\n\nThis window cannot show all of its steps, so it does not ask for approval.",
                "FileCat — administrator operation", 0x10);
            return false;
        }
        return MessageBox(0, $"{instruction}\n\n{content}\n\n{pages[0]}\n\n{footer}\n\nRun these steps as administrator?", "FileCat — administrator operation",
            YesNo | Warning | SecondDefault | TopMost) == 6;
    }

    [LibraryImport("comctl32.dll")]
    private static partial int TaskDialogIndirect(TaskDialogConfig* config, int* button, int* radioButton, int* verificationChecked);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint owner, string text, string caption, uint type);
}
