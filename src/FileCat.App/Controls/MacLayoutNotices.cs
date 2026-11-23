using System.Runtime.InteropServices;

namespace FileCat.App.Controls;

/// <summary>
/// Avalonia.Native 12.1 tells VoiceOver about every change to an element's children with AXLayoutChanged and the
/// changed elements under NSAccessibilityUIElementsKey (AvnAccessibilityElement raiseChildrenChanged, AvnView
/// raiseAccessibilityChildrenChanged). VoiceOver moves its cursor to elements listed there and, keeping keyboard focus
/// with its cursor, asks to focus them: choosing a group in the group editor showed the set-ID consent check box, and
/// VoiceOver took the keyboard from the chooser to it. These two methods are replaced, through the Objective-C runtime,
/// by ones that refresh the same children and post AXLayoutChanged without the list. Each method must exist with the
/// expected signature, as must the refresh it calls; otherwise nothing is replaced and Avalonia's behavior stays.
/// </summary>
internal static unsafe class MacLayoutNotices
{
    internal const string ElementClass = "AvnAccessibilityElement";
    internal const string ViewClass = "AvnView";
    internal const string ElementNotice = "raiseChildrenChanged";
    internal const string ElementRefresh = "recalculateChildren";
    internal const string ViewNotice = "raiseAccessibilityChildrenChanged";
    // Avalonia's own spelling.
    internal const string ViewRefresh = "recalculateAccessibiltyChildren";
    // - (void)method with no arguments.
    internal const string VoidNoArguments = "v16@0:8";

    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private static IntPtr s_elementClass, s_elementRefresh, s_viewRefresh, s_parent, s_isElement, s_isKindOf, s_layoutChanged;
    private static bool s_installed;

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] private static extern IntPtr class_getInstanceMethod(IntPtr cls, IntPtr selector);
    [DllImport(ObjC)] private static extern IntPtr method_getTypeEncoding(IntPtr method);
    [DllImport(ObjC)] private static extern IntPtr method_setImplementation(IntPtr method, IntPtr implementation);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte SendByte(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern byte SendByte(IntPtr receiver, IntPtr selector, IntPtr argument);
    [DllImport(AppKit)] private static extern void NSAccessibilityPostNotification(IntPtr element, IntPtr notification);

    /// <summary>Replaces both notices under the macOS bridge; false, with Avalonia's behavior kept, if anything differs.</summary>
    internal static bool Install()
    {
        if (s_installed) return true;
        if (!OperatingSystem.IsMacOS() || !NativeLiveRegionBridges.UseMacBridge) return false;
        try
        {
            IntPtr element = objc_getClass(ElementClass), view = objc_getClass(ViewClass);
            if (element == IntPtr.Zero || view == IntPtr.Zero) return Refuse("its classes are missing");
            IntPtr elementNotice = Method(element, ElementNotice), elementRefresh = Method(element, ElementRefresh);
            IntPtr viewNotice = Method(view, ViewNotice), viewRefresh = Method(view, ViewRefresh);
            if (elementNotice == IntPtr.Zero || elementRefresh == IntPtr.Zero || viewNotice == IntPtr.Zero || viewRefresh == IntPtr.Zero)
                return Refuse("its methods are missing or have other signatures");
            s_elementClass = element;
            s_elementRefresh = sel_registerName(ElementRefresh);
            s_viewRefresh = sel_registerName(ViewRefresh);
            s_parent = sel_registerName("accessibilityParent");
            s_isElement = sel_registerName("isAccessibilityElement");
            s_isKindOf = sel_registerName("isKindOfClass:");
            IntPtr text = Marshal.StringToCoTaskMemUTF8("AXLayoutChanged");
            try { s_layoutChanged = Send(Send(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), text), sel_registerName("retain")); }
            finally { Marshal.FreeCoTaskMem(text); }
            if (s_layoutChanged == IntPtr.Zero) return Refuse("the notification name could not be made");
            method_setImplementation(elementNotice, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&ElementChildrenChanged);
            method_setImplementation(viewNotice, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&ViewChildrenChanged);
            s_installed = true;
            Core.Diagnostics.AppLog.Info("Layout notices on macOS post AXLayoutChanged without the changed elements.");
            return true;
        }
        catch (Exception ex)
        {
            return Refuse(ex.Message);
        }
    }

    private static IntPtr Method(IntPtr cls, string name)
    {
        IntPtr method = class_getInstanceMethod(cls, sel_registerName(name));
        return method != IntPtr.Zero && Marshal.PtrToStringUTF8(method_getTypeEncoding(method)) == VoidNoArguments ? method : IntPtr.Zero;
    }

    private static bool Refuse(string reason)
    {
        Core.Diagnostics.AppLog.Warn("Layout notices on macOS keep Avalonia's behavior: " + reason);
        return false;
    }

    [UnmanagedCallersOnly]
    private static void ElementChildrenChanged(IntPtr self, IntPtr command)
    {
        try
        {
            Send(self, s_elementRefresh);
            // As Avalonia does, on the nearest element that is in the accessibility tree.
            IntPtr target = self;
            for (int depth = 0; depth < 64 && target != IntPtr.Zero && SendByte(target, s_isKindOf, s_elementClass) != 0 &&
                 SendByte(target, s_isElement) == 0; depth++)
                target = Send(target, s_parent);
            if (target != IntPtr.Zero) NSAccessibilityPostNotification(target, s_layoutChanged);
        }
        catch { }
    }

    [UnmanagedCallersOnly]
    private static void ViewChildrenChanged(IntPtr self, IntPtr command)
    {
        try
        {
            Send(self, s_viewRefresh);
            NSAccessibilityPostNotification(self, s_layoutChanged);
        }
        catch { }
    }
}
