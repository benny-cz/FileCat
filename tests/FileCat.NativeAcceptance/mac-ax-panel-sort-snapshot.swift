// Bounded, direct macOS AX snapshot for the isolated per-panel Sort acceptance run.
// Usage: mac-ax-panel-sort-snapshot FILECAT_EXE PID
import Foundation
import ApplicationServices

func attribute(_ element: AXUIElement, _ name: CFString) -> CFTypeRef? {
    var value: CFTypeRef?
    return AXUIElementCopyAttributeValue(element, name, &value) == .success ? value : nil
}

func label(_ value: CFTypeRef?) -> String {
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
    fputs("usage: mac-ax-panel-sort-snapshot FILECAT_EXE PID\n", stderr)
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
func scan(_ element: AXUIElement, _ depth: Int) {
    guard depth <= 20 && visited < 3000 else { return }
    visited += 1
    let title = label(attribute(element, kAXTitleAttribute as CFString))
    let description = label(attribute(element, kAXDescriptionAttribute as CFString))
    let name = title.isEmpty ?
        (description.isEmpty ? label(attribute(element, kAXValueAttribute as CFString)) : description) : title
    if name.hasPrefix("Sort files:") || name.hasPrefix("Sort by ") ||
       name.hasPrefix("Marked ") || name.hasPrefix("0 folders") || name == "Filter" ||
       name.contains(", Unlocked tab, ") ||
       ["a.txt", "b.txt", "c.txt", "d.txt", "second", "e.txt", "f.txt"].contains(name) {
        if let bounds = frame(element) {
            nodes.append(["role": label(attribute(element, kAXRoleAttribute as CFString)),
                          "name": name, "title": title, "description": description,
                          "bounds": bounds,
                          "selected": (attribute(element, kAXSelectedAttribute as CFString) as? NSNumber)?.boolValue ?? false,
                          "focused": (attribute(element, kAXFocusedAttribute as CFString) as? NSNumber)?.boolValue ?? false])
        }
    }
    let children = attribute(element, kAXChildrenAttribute as CFString) as? [AXUIElement] ?? []
    for child in children.prefix(60) { scan(child, depth + 1) }
}
for window in windows.prefix(5) { scan(window, 0) }
let result: [String: Any] = ["visited": visited, "windows": windows.count, "nodes": nodes]
let data = try JSONSerialization.data(withJSONObject: result, options: [.sortedKeys])
print(String(data: data, encoding: .utf8)!)
