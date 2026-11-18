using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FileCat.NativeAcceptance
{
    // Also compiled by Windows PowerShell, whose Add-Type compiler uses C# 5.
    public static class WindowsEfsBackupWizardDriver
    {
        private const uint PressWizardButton = 0x471;
        private const uint ClickButton = 0xf5;
        private const uint AbortIfHung = 2;

        public static void Drive(IntPtr owner, bool complete, string path, string password)
        {
            uint process;
            if (GetWindowThreadProcessId(owner, out process) == 0)
                throw new InvalidOperationException("The qualification window has closed.");
            var wizard = WaitFor(() => OwnedWindow(owner, process, "Back up EFS key"), "parented export wizard");
            try
            {
                if (!complete)
                {
                    Press(wizard, PressWizardButton, 5);
                    return;
                }

                WaitFor(() => Control(wizard, 100, "Welcome to the Certificate Export Wizard"), "welcome page");
                Press(wizard, PressWizardButton, 1);
                WaitFor(() => Control(wizard, 100, "Export File Format"), "private-key format page");
                if (IsWindowEnabled(WaitFor(() => Control(wizard, 108), "private-key deletion checkbox")) ||
                    IsWindowEnabled(WaitFor(() => Control(wizard, 103), "public certificate-only format")))
                    throw new InvalidOperationException("The wizard did not enforce private-key export and retention.");
                Press(wizard, PressWizardButton, 1);
                WaitFor(() => Control(wizard, 100, "Security"), "security page");
                Press(WaitFor(() => Control(wizard, 1029), "password option"), ClickButton, 0);
                WriteText(WaitFor(() => Control(wizard, 101, enabled: true), "password field"), password);
                WriteText(WaitFor(() => Control(wizard, 102, enabled: true), "password confirmation"), password);
                Press(wizard, PressWizardButton, 1);
                WaitFor(() => Control(wizard, 100, "File to Export"), "backup destination page");
                WriteText(WaitFor(() => Control(wizard, 101), "destination input"), path);
                Press(wizard, PressWizardButton, 1);
                WaitFor(() => Control(wizard, 100, "Completing the Certificate Export Wizard"), "completion page");
                Press(wizard, PressWizardButton, 2);
                var completion = WaitFor(() => IsWindow(wizard) ? OwnedWindow(wizard, process, string.Empty) : wizard, "export completion");
                if (completion != wizard)
                    Press(WaitFor(() => Control(completion, 2, "OK"), "result acknowledgment"), ClickButton, 0);
            }
            catch
            {
                if (OwnedWindow(owner, process, "Back up EFS key") == wizard)
                    PostMessage(wizard, PressWizardButton, new IntPtr(5), IntPtr.Zero);
                throw;
            }
        }

        private static void Press(IntPtr window, uint message, int value)
        {
            if (!PostMessage(window, message, new IntPtr(value), IntPtr.Zero))
                throw new InvalidOperationException("The owned wizard could not receive input.");
        }

        private static void WriteText(IntPtr window, string value)
        {
            IntPtr result;
            // WM_SETTEXT reaches the owned control when the fixture and the app are separate processes.
            if (SendText(window, 0x0c, IntPtr.Zero, value, AbortIfHung, 1000, out result) == IntPtr.Zero || result == IntPtr.Zero)
                throw new InvalidOperationException("The owned wizard rejected the input text.");
        }

        private static IntPtr OwnedWindow(IntPtr owner, uint process, string title)
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows((window, _) =>
            {
                uint actualProcess;
                if (GetWindow(window, 4) != owner || GetWindowThreadProcessId(window, out actualProcess) == 0 ||
                    actualProcess != process || !IsWindowVisible(window) || title.Length != 0 && WindowText(window) != title)
                    return true;
                result = window;
                return false;
            }, IntPtr.Zero);
            return result;
        }

        private static IntPtr Control(IntPtr window, int id, string text = "", bool enabled = false)
        {
            uint process;
            if (GetWindowThreadProcessId(window, out process) == 0) return IntPtr.Zero;
            IntPtr result = IntPtr.Zero;
            EnumChildWindows(window, (child, _) =>
            {
                uint actualProcess;
                if (GetWindowThreadProcessId(child, out actualProcess) == 0 || actualProcess != process ||
                    !IsWindowVisible(child) || GetDlgCtrlID(child) != id || enabled && !IsWindowEnabled(child) ||
                    text.Length != 0 && WindowText(child) != text) return true;
                result = child;
                return false;
            }, IntPtr.Zero);
            return result;
        }

        private static IntPtr WaitFor(Func<IntPtr> locate, string description)
        {
            var time = Stopwatch.StartNew();
            do
            {
                var window = locate();
                if (window != IntPtr.Zero) return window;
                Thread.Sleep(25);
            } while (time.Elapsed < TimeSpan.FromSeconds(5));
            throw new TimeoutException("Could not reach the wizard's " + description + ".");
        }

        private static string WindowText(IntPtr window)
        {
            var caption = new StringBuilder(256);
            IntPtr result;
            TryReadText(window, 0x0d, new IntPtr(caption.Capacity), caption, AbortIfHung, 1000, out result);
            return caption.ToString();
        }

        private delegate bool EnumerateWindow(IntPtr window, IntPtr state);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr window);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumerateWindow callback, IntPtr state);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumerateWindow callback, IntPtr state);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr value, IntPtr extra);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr SendText(IntPtr window, uint message, IntPtr value, string text, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr TryReadText(IntPtr window, uint message, IntPtr value, StringBuilder text, uint flags, uint timeout, out IntPtr result);
    }
}
