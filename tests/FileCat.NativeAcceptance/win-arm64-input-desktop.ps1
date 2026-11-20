$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or
    $env:RUNNER_OS -ne 'Windows' -or $env:RUNNER_ARCH -ne 'ARM64' -or ![IO.Path]::IsPathRooted($env:RUNNER_TEMP)) {
    throw 'Use the dedicated hosted ARM64 input desktop probe.'
}
$receipt = Join-Path $env:RUNNER_TEMP 'filecat-arm64-input-desktop.json'
if (Test-Path -LiteralPath $receipt) { throw 'Use a fresh probe destination.' }
Add-Type -ReferencedAssemblies @('System.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll') -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

public static class FileCatInputDesktopProbe
{
    public sealed class Observation
    {
        public string Before, PrivateDesktop, Foreground, After, Error;
        public bool Activated, Focused, KeyReceived, Restored;
    }

    public static Observation Check()
    {
        var result = new Observation();
        var worker = new Thread(() => CheckOnFreshThread(result)) { IsBackground = true };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        if (!worker.Join(TimeSpan.FromSeconds(20))) throw new TimeoutException("The desktop probe did not retire.");
        return result;
    }

    private static void CheckOnFreshThread(Observation result)
    {
        const uint switchDesktop = 0x100;
        const uint readObjects = 1;
        const uint privateAccess = switchDesktop | readObjects | 2 | 4 | 0x80; // Create window/menu and write objects.
        IntPtr threadDesktop = GetThreadDesktop(GetCurrentThreadId());
        IntPtr original = IntPtr.Zero, created = IntPtr.Zero;
        try
        {
            original = OpenInputDesktop(0, false, switchDesktop | readObjects);
            if (original == IntPtr.Zero) throw new Win32Exception();
            result.Before = DesktopName(original);
            if (!string.Equals(result.Before, "Default", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The active input desktop is not the ordinary Default desktop.");
            result.PrivateDesktop = "FileCatInput-" + Guid.NewGuid().ToString("N");
            created = CreateDesktopW(result.PrivateDesktop, IntPtr.Zero, IntPtr.Zero, 0, privateAccess, IntPtr.Zero);
            if (created == IntPtr.Zero || !SetThreadDesktop(created)) throw new Win32Exception();
            if (!SwitchDesktop(created)) throw new Win32Exception();
            result.Activated = true;
            using (var form = new Form { Text = "FileCat keyboard test", ShowInTaskbar = false, Width = 340, Height = 150 })
            using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
            {
                var input = new TextBox { Dock = DockStyle.Top, AccessibleName = "FileCat keyboard test" };
                form.Controls.Add(input);
                form.ActiveControl = input;
                input.KeyDown += (sender, args) =>
                {
                    result.KeyReceived = result.Focused && args.KeyCode == Keys.F8;
                    if (result.KeyReceived) form.Close();
                };
                var watch = System.Diagnostics.Stopwatch.StartNew();
                timer.Tick += (sender, args) =>
                {
                    if (watch.Elapsed >= TimeSpan.FromSeconds(5)) { form.Close(); return; }
                    if (result.Focused || GetForegroundWindow() != form.Handle || !input.Focus()) return;
                    result.Foreground = InputDesktopName();
                    if (result.Foreground != result.PrivateDesktop || GetForegroundWindow() != form.Handle) return;
                    result.Focused = true;
                    keybd_event((byte)Keys.F8, 0, 0, UIntPtr.Zero);
                    keybd_event((byte)Keys.F8, 0, 2, UIntPtr.Zero);
                };
                form.Shown += (sender, args) => { form.Activate(); timer.Start(); };
                form.ShowDialog();
            }
        }
        catch (Exception failure) { result.Error = failure.Message; }
        finally
        {
            if (result.Activated && !SwitchDesktop(original)) result.Error = "Could not restore the original input desktop.";
            result.After = InputDesktopName();
            result.Restored = result.Before != null && result.After == result.Before;
            if (created != IntPtr.Zero)
            {
                SetThreadDesktop(threadDesktop);
                CloseDesktop(created);
            }
            if (original != IntPtr.Zero) CloseDesktop(original);
        }
    }

    private static string InputDesktopName()
    {
        IntPtr desktop = OpenInputDesktop(0, false, 1);
        if (desktop == IntPtr.Zero) return null;
        try { return DesktopName(desktop); }
        finally { CloseDesktop(desktop); }
    }

    private static string DesktopName(IntPtr desktop)
    {
        var name = new StringBuilder(256);
        uint bytes;
        return GetUserObjectInformationW(desktop, 2, name, 512, out bytes) ? name.ToString() : null;
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SwitchDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr CreateDesktopW(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool GetUserObjectInformationW(IntPtr obj, int index, StringBuilder value, uint length, out uint needed);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
'@
. (Join-Path $PSScriptRoot '../../eng/validation/Enter-MediumIntegrityTestProcess.ps1')
$probe = [FileCatInputDesktopProbe]::Check()
[ordered]@{
    NativeArchitecture = $env:RUNNER_ARCH
    Session = (Get-Process -Id $PID).SessionId
    Integrity = [FileCatCiTokenIntegrity]::IntegrityRid()
    Observation = $probe
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $receipt
Get-Content -LiteralPath $receipt
if ($probe.Activated -and !$probe.Restored) { throw 'The original input desktop could not be verified after the probe.' }
