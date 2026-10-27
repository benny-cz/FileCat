// Query the native preference that FileCat reads; keep setup separate from verification.
import AppKit

if CommandLine.arguments.contains("--watch") {
    let notification = NSWorkspace.shared.notificationCenter.addObserver(
        forName: NSWorkspace.accessibilityDisplayOptionsDidChangeNotification,
        object: NSWorkspace.shared, queue: nil) { _ in
            print("notification \(NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? "true" : "false")")
            fflush(stdout)
        }
    defer { NSWorkspace.shared.notificationCenter.removeObserver(notification) }
    for sample in 0..<25 {
        print("\(sample) \(NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? "true" : "false")")
        fflush(stdout)
        RunLoop.current.run(until: Date().addingTimeInterval(1))
    }
} else {
    print(NSWorkspace.shared.accessibilityDisplayShouldReduceMotion ? "true" : "false")
}
