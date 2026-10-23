// Read-only native hit-test for the isolated macOS acceptance runner.
// Usage: swift mac-ax-hit-test.swift PID X Y
import ApplicationServices
import Foundation

guard CommandLine.arguments.count == 4,
      let pid = pid_t(CommandLine.arguments[1]),
      let x = Float(CommandLine.arguments[2]),
      let y = Float(CommandLine.arguments[3]) else {
    fputs("Expected PID and screen coordinates.\n", stderr)
    exit(2)
}

let app = AXUIElementCreateApplication(pid)
var hit: AXUIElement?
let result = AXUIElementCopyElementAtPosition(app, x, y, &hit)
guard result == .success, let element = hit else {
    print("AX hit-test error: \(result.rawValue)")
    exit(1)
}

func attribute(_ name: CFString) -> String {
    var value: CFTypeRef?
    guard AXUIElementCopyAttributeValue(element, name, &value) == .success,
          let value else { return "" }
    return String(describing: value)
}

print("role=\(attribute(kAXRoleAttribute as CFString)) " +
      "name=\(attribute(kAXTitleAttribute as CFString)) " +
      "description=\(attribute(kAXDescriptionAttribute as CFString)) " +
      "value=\(attribute(kAXValueAttribute as CFString))")
