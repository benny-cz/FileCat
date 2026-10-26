// Bounded direct Accessibility actions for an isolated FileCat modal test.
// Usage: mac-ax-modal-control FILECAT_EXE PID snapshot|focus-row|press-button|resize-window [ARGS]
import ApplicationServices
import Foundation

func attribute(_ element: AXUIElement, _ key: CFString) -> CFTypeRef? {
    var value: CFTypeRef?
    return AXUIElementCopyAttributeValue(element, key, &value) == .success ? value : nil
}

func string(_ value: CFTypeRef?) -> String {
    guard let value else { return "" }
    return String(describing: value)
}

func ordinate(_ element: AXUIElement) -> Double? {
    guard let value = attribute(element, kAXPositionAttribute as CFString),
          CFGetTypeID(value) == AXValueGetTypeID() else { return nil }
    var point = CGPoint.zero
    return AXValueGetValue(value as! AXValue, .cgPoint, &point) ? point.y : nil
}

guard CommandLine.arguments.count >= 4,
      let pid = Int32(CommandLine.arguments[2]),
      ["snapshot", "focus-row", "press-button", "resize-window"].contains(CommandLine.arguments[3]) else {
    fputs("usage: mac-ax-modal-control FILECAT_EXE PID snapshot|focus-row|press-button|resize-window [ARGS]\n", stderr)
    exit(2)
}
let mode = CommandLine.arguments[3]
guard (mode == "snapshot" && CommandLine.arguments.count == 4) ||
      (mode == "focus-row" && CommandLine.arguments.count == 5) ||
      (mode == "press-button" && CommandLine.arguments.count == 6 &&
       ["top", "bottom"].contains(CommandLine.arguments[5])) ||
      (mode == "resize-window" && CommandLine.arguments.count == 6) else {
    fputs("Invalid native modal action arguments.\n", stderr)
    exit(2)
}

let expected = URL(fileURLWithPath: CommandLine.arguments[1]).standardizedFileURL.path
let validation = FileManager.default.homeDirectoryForCurrentUser
    .appendingPathComponent("FileCatValidation", isDirectory: true).standardizedFileURL.path + "/"
guard expected.hasPrefix(validation), expected.hasSuffix("/Contents/MacOS/FileCat"),
      FileManager.default.fileExists(atPath: expected), AXIsProcessTrusted() else {
    fputs("The owned executable or Accessibility permission is unavailable.\n", stderr)
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
      (command == expected || command.hasPrefix(expected + " ")) else {
    fputs("The PID does not belong to the isolated executable.\n", stderr)
    exit(2)
}

let app = AXUIElementCreateApplication(pid)
let windows = attribute(app, kAXWindowsAttribute as CFString) as? [AXUIElement] ?? []
if mode == "resize-window" {
    let main = windows.filter {
        string(attribute($0, kAXTitleAttribute as CFString)).contains(" — FileCat — ")
    }
    guard main.count == 1,
          let width = Double(CommandLine.arguments[4]),
          let height = Double(CommandLine.arguments[5]),
          (640...1400).contains(width), (420...1000).contains(height) else {
        let titles = windows.map { string(attribute($0, kAXTitleAttribute as CFString)) }
        fputs("The owned Mac main window or bounded requested size is unavailable: titles=\(titles) size=\(CommandLine.arguments[4]),\(CommandLine.arguments[5]).\n", stderr)
        exit(3)
    }
    var requested = CGSize(width: width, height: height)
    guard let value = AXValueCreate(.cgSize, &requested),
          AXUIElementSetAttributeValue(main[0], kAXSizeAttribute as CFString, value) == .success,
          let actualValue = attribute(main[0], kAXSizeAttribute as CFString),
          CFGetTypeID(actualValue) == AXValueGetTypeID() else {
        fputs("The isolated Mac window did not accept a native resize.\n", stderr)
        exit(4)
    }
    var actual = CGSize.zero
    guard AXValueGetValue(actualValue as! AXValue, .cgSize, &actual),
          abs(actual.width - width) <= 2, abs(actual.height - height) <= 2 else {
        fputs("The isolated Mac window did not settle at the requested size.\n", stderr)
        exit(4)
    }
    print("{\"width\":\(actual.width),\"height\":\(actual.height)}")
    exit(0)
}
var visited = 0
var nodes: [[String: Any]] = []
var matches: [(element: AXUIElement, y: Double)] = []
func scan(_ element: AXUIElement, _ depth: Int) {
    guard depth <= 22 && visited < 5000 else { return }
    visited += 1
    let role = string(attribute(element, kAXRoleAttribute as CFString))
    let title = string(attribute(element, kAXTitleAttribute as CFString))
    let description = string(attribute(element, kAXDescriptionAttribute as CFString))
    let value = string(attribute(element, kAXValueAttribute as CFString))
    let name = title.isEmpty ? (description.isEmpty ? value : description) : title
    if let y = ordinate(element) {
        if mode == "snapshot" && ["AXButton", "AXRow", "AXTextField", "AXStaticText"].contains(role) {
            nodes.append(["role": role, "name": name, "y": y])
        }
        if mode != "snapshot" && name == CommandLine.arguments[4] &&
            role == (mode == "focus-row" ? "AXRow" : "AXButton") {
            matches.append((element, y))
        }
    }
    let children = attribute(element, kAXChildrenAttribute as CFString) as? [AXUIElement] ?? []
    for child in children.prefix(80) { scan(child, depth + 1) }
    if role == "AXList" && children.isEmpty {
        let rows = attribute(element, kAXRowsAttribute as CFString) as? [AXUIElement] ?? []
        for row in rows.prefix(80) { scan(row, depth + 1) }
    }
}
for window in windows.prefix(5) { scan(window, 0) }
guard !windows.isEmpty && visited < 5000 else {
    fputs("The native window tree is absent or exceeds the test bound.\n", stderr)
    exit(3)
}
if mode == "snapshot" {
    let result: [String: Any] = ["windows": windows.count, "visited": visited, "nodes": nodes]
    let data = try JSONSerialization.data(withJSONObject: result, options: [.sortedKeys])
    print(String(data: data, encoding: .utf8)!)
} else {
    guard !matches.isEmpty else {
        fputs("The exact requested AX control is absent.\n", stderr)
        exit(3)
    }
    let chosen = mode == "press-button" && CommandLine.arguments[5] == "bottom"
        ? matches.max(by: { $0.y < $1.y })!
        : matches.min(by: { $0.y < $1.y })!
    let outcome: AXError
    if mode == "focus-row" {
        let focus = AXUIElementSetAttributeValue(chosen.element, kAXFocusedAttribute as CFString, kCFBooleanTrue)
        let select = AXUIElementSetAttributeValue(chosen.element, kAXSelectedAttribute as CFString, kCFBooleanTrue)
        outcome = focus == .success ? focus : select
    } else {
        outcome = AXUIElementPerformAction(chosen.element, kAXPressAction as CFString)
    }
    guard outcome == .success else {
        fputs("The exact AX action failed: \(outcome.rawValue).\n", stderr)
        exit(4)
    }
    print("acted=\(mode) name=\(CommandLine.arguments[4]) matches=\(matches.count) y=\(chosen.y)")
}
