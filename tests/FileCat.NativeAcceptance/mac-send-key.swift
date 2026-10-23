// Native input for the isolated macOS clipboard handoff acceptance test.
// System Events can synthesize an extra Command+A before Command+X on this VM;
// CGEvent posts only the requested chord. The frontmost-app guard prevents a
// delayed test from changing another application's selection or clipboard.
import ApplicationServices
import AppKit
import CoreGraphics
import Foundation

guard CommandLine.arguments.count == 2,
      let operation = ["cut": CGKeyCode(7), "copy": CGKeyCode(8),
                       "paste": CGKeyCode(9), "find": CGKeyCode(3),
                       "checksum": CGKeyCode(40)][CommandLine.arguments[1]] else {
    fputs("Usage: mac-send-key cut|copy|paste|find|checksum\n", stderr)
    exit(2)
}
guard AXIsProcessTrusted() else {
    fputs("Accessibility is required for native keyboard input\n", stderr)
    exit(3)
}
let frontmost = NSWorkspace.shared.frontmostApplication
let expected = CommandLine.arguments[1] == "copy" ? "com.apple.finder" : "io.github.benny-cz.filecat.validation"
guard frontmost?.bundleIdentifier == expected else {
    fputs("The expected isolated app is not frontmost (expected \(expected), got \(frontmost?.bundleIdentifier ?? "none"))\n", stderr)
    exit(4)
}
guard let source = CGEventSource(stateID: .hidSystemState),
      let down = CGEvent(keyboardEventSource: source, virtualKey: operation, keyDown: true),
      let up = CGEvent(keyboardEventSource: source, virtualKey: operation, keyDown: false) else {
    fputs("Cannot construct the keyboard event\n", stderr)
    exit(5)
}
let modifier: CGEventFlags = ["find", "checksum"].contains(CommandLine.arguments[1]) ? .maskControl : .maskCommand
down.flags = modifier
up.flags = modifier
down.post(tap: .cghidEventTap)
usleep(50_000)
up.post(tap: .cghidEventTap)
print(CommandLine.arguments[1])
