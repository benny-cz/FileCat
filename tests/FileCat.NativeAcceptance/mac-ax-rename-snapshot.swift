// Bounded direct AX snapshot for an isolated macOS bulk-rename review.
// Usage: mac-ax-rename-snapshot FILECAT_EXE PID
import Foundation
import ApplicationServices

func attribute(_ element: AXUIElement, _ name: CFString) -> CFTypeRef? {
    var value: CFTypeRef?
    return AXUIElementCopyAttributeValue(element, name, &value) == .success ? value : nil
}

func string(_ value: CFTypeRef?) -> String {
    guard let value else { return "" }
    return String(describing: value)
}

func frame(_ element: AXUIElement) -> [Double]? {
    guard let position = attribute(element, kAXPositionAttribute as CFString),
          let size = attribute(element, kAXSizeAttribute as CFString),
          CFGetTypeID(position) == AXValueGetTypeID(),
          CFGetTypeID(size) == AXValueGetTypeID() else { return nil }
    var point = CGPoint.zero
    var extent = CGSize.zero
    guard AXValueGetValue(position as! AXValue, .cgPoint, &point),
          AXValueGetValue(size as! AXValue, .cgSize, &extent) else { return nil }
    return [point.x, point.y, extent.width, extent.height]
}

guard CommandLine.arguments.count == 3, let pid = Int32(CommandLine.arguments[2]) else {
    fputs("usage: mac-ax-rename-snapshot FILECAT_EXE PID\n", stderr)
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
var nodes: [[String: Any]] = []
var visited = 0
let included: Set<String> = ["AXWindow", "AXRow", "AXButton", "AXCheckBox",
                             "AXTextField", "AXTextArea", "AXComboBox", "AXPopUpButton",
                             "AXStaticText", "AXList", "AXGroup"]
func scan(_ element: AXUIElement, _ depth: Int) {
    guard depth <= 22 && visited < 5000 else { return }
    visited += 1
    let role = string(attribute(element, kAXRoleAttribute as CFString))
    let title = string(attribute(element, kAXTitleAttribute as CFString))
    let description = string(attribute(element, kAXDescriptionAttribute as CFString))
    let value = string(attribute(element, kAXValueAttribute as CFString))
    let name = title.isEmpty ? (description.isEmpty ? value : description) : title
    if included.contains(role), let bounds = frame(element) {
        nodes.append(["role": role, "name": name, "title": title,
                      "description": description, "value": value, "bounds": bounds,
                      "enabled": (attribute(element, kAXEnabledAttribute as CFString) as? NSNumber)?.boolValue ?? false,
                      "selected": (attribute(element, kAXSelectedAttribute as CFString) as? NSNumber)?.boolValue ?? false,
                      "focused": (attribute(element, kAXFocusedAttribute as CFString) as? NSNumber)?.boolValue ?? false])
    }
    let children = attribute(element, kAXChildrenAttribute as CFString) as? [AXUIElement] ?? []
    for child in children.prefix(80) { scan(child, depth + 1) }
    if role == "AXList" && children.isEmpty {
        let rows = attribute(element, kAXRowsAttribute as CFString) as? [AXUIElement] ?? []
        for row in rows.prefix(80) { scan(row, depth + 1) }
    }
}
for window in windows.prefix(5) { scan(window, 0) }
let result: [String: Any] = ["visited": visited, "windows": windows.count, "nodes": nodes]
let data = try JSONSerialization.data(withJSONObject: result, options: [.sortedKeys])
print(String(data: data, encoding: .utf8)!)
