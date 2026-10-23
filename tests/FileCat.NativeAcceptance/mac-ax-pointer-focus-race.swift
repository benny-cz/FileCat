// Click the exact source tab, then issue the stale AX focus request that macOS sometimes sends
// to the previous panel's Filter field. Restricted to an owned FileCatValidation app and PID.
// Usage: mac-ax-pointer-focus-race FILECAT_EXE PID SOURCE_TAB_X SOURCE_TAB_Y
import ApplicationServices
import Foundation

func attribute(_ element: AXUIElement, _ name: CFString) -> CFTypeRef? {
    var value: CFTypeRef?
    return AXUIElementCopyAttributeValue(element, name, &value) == .success ? value : nil
}

func text(_ value: CFTypeRef?) -> String {
    guard let value else { return "" }
    return String(describing: value)
}

func xPosition(_ element: AXUIElement) -> Double? {
    guard let position = attribute(element, kAXPositionAttribute as CFString),
          CFGetTypeID(position) == AXValueGetTypeID() else { return nil }
    var point = CGPoint.zero
    return AXValueGetValue(position as! AXValue, .cgPoint, &point) ? point.x : nil
}

guard CommandLine.arguments.count == 5,
      let pid = pid_t(CommandLine.arguments[2]),
      let x = Double(CommandLine.arguments[3]),
      let y = Double(CommandLine.arguments[4]) else {
    fputs("Expected owned executable, PID and source-tab screen coordinates.\n", stderr)
    exit(2)
}
let expected = URL(fileURLWithPath: CommandLine.arguments[1]).standardizedFileURL.path
let validation = FileManager.default.homeDirectoryForCurrentUser
    .appendingPathComponent("FileCatValidation", isDirectory: true).standardizedFileURL.path + "/"
guard expected.hasPrefix(validation), expected.hasSuffix("/Contents/MacOS/FileCat"),
      FileManager.default.fileExists(atPath: expected), AXIsProcessTrusted() else {
    fputs("The owned executable or accessibility permission is unavailable.\n", stderr)
    exit(2)
}
let process = Process()
process.executableURL = URL(fileURLWithPath: "/bin/ps")
process.arguments = ["-p", String(pid), "-o", "command="]
let output = Pipe()
process.standardOutput = output
process.standardError = Pipe()
try process.run()
let command = String(data: output.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8)?
    .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
process.waitUntilExit()
guard process.terminationStatus == 0,
      command == expected || command.hasPrefix(expected + " ") else {
    fputs("The PID does not belong to the isolated test executable.\n", stderr)
    exit(2)
}

let app = AXUIElementCreateApplication(pid)
let windows = attribute(app, kAXWindowsAttribute as CFString) as? [AXUIElement] ?? []
var matches: [(element: AXUIElement, x: Double)] = []
var visited = 0
func scan(_ element: AXUIElement, _ depth: Int) {
    guard depth <= 22 && visited < 5000 else { return }
    visited += 1
    if text(attribute(element, kAXRoleAttribute as CFString)) == "AXTextField" {
        let title = text(attribute(element, kAXTitleAttribute as CFString))
        let description = text(attribute(element, kAXDescriptionAttribute as CFString))
        if (title.isEmpty ? description : title) == "Filter", let x = xPosition(element) {
            matches.append((element, x))
        }
    }
    let children = attribute(element, kAXChildrenAttribute as CFString) as? [AXUIElement] ?? []
    for child in children.prefix(80) { scan(child, depth + 1) }
}
for window in windows.prefix(5) { scan(window, 0) }
guard matches.count >= 2, visited < 5000,
      let previousPanelFilter = matches.max(by: { $0.x < $1.x })?.element else {
    fputs("The two exact panel filters were unavailable.\n", stderr)
    exit(3)
}

let point = CGPoint(x: x, y: y)
guard let down = CGEvent(mouseEventSource: nil, mouseType: .leftMouseDown,
                         mouseCursorPosition: point, mouseButton: .left),
      let up = CGEvent(mouseEventSource: nil, mouseType: .leftMouseUp,
                       mouseCursorPosition: point, mouseButton: .left) else {
    fputs("Could not create native pointer events.\n", stderr)
    exit(4)
}
down.post(tap: .cghidEventTap)
up.post(tap: .cghidEventTap)
usleep(35_000)
let result = AXUIElementSetAttributeValue(previousPanelFilter, kAXFocusedAttribute as CFString,
                                          kCFBooleanTrue)
guard result == .success else {
    fputs("The stale accessibility focus request failed: \(result.rawValue).\n", stderr)
    exit(4)
}
print("clicked source tab, then requested stale right Filter focus after 35 ms; AX result=\(result.rawValue)")
