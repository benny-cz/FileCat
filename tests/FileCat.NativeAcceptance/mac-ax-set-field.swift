// Set a named field in one isolated FileCat validation bundle through macOS AX.
// Usage: mac-ax-set-field FILECAT_EXE PID NAME leftmost|rightmost [VALUE]
// With VALUE, update an AXTextField; without VALUE, focus/select an AXRow.
import Foundation
import ApplicationServices

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

guard CommandLine.arguments.count == 5 || CommandLine.arguments.count == 6,
      let pid = Int32(CommandLine.arguments[2]),
      ["leftmost", "rightmost"].contains(CommandLine.arguments[4]) else {
    fputs("usage: mac-ax-set-field FILECAT_EXE PID NAME leftmost|rightmost [VALUE]\n", stderr)
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
let requested = CommandLine.arguments[3]
let rowFocus = CommandLine.arguments.count == 5
var matches: [(element: AXUIElement, x: Double)] = []
var visited = 0
func scan(_ element: AXUIElement, _ depth: Int) {
    guard depth <= 22 && visited < 5000 else { return }
    visited += 1
    let role = text(attribute(element, kAXRoleAttribute as CFString))
    if role == (rowFocus ? "AXRow" : "AXTextField") {
        let title = text(attribute(element, kAXTitleAttribute as CFString))
        let description = text(attribute(element, kAXDescriptionAttribute as CFString))
        let name = title.isEmpty ? description : title
        if name == requested, let x = xPosition(element) { matches.append((element, x)) }
    }
    let children = attribute(element, kAXChildrenAttribute as CFString) as? [AXUIElement] ?? []
    for child in children.prefix(80) { scan(child, depth + 1) }
}
for window in windows.prefix(5) { scan(window, 0) }
guard !matches.isEmpty, visited < 5000 else {
    fputs("The exact native field was absent or the AX tree was too large.\n", stderr)
    exit(3)
}
let chosen = CommandLine.arguments[4] == "rightmost"
    ? matches.max(by: { $0.x < $1.x })!
    : matches.min(by: { $0.x < $1.x })!
let focused = AXUIElementSetAttributeValue(chosen.element, kAXFocusedAttribute as CFString, kCFBooleanTrue)
if rowFocus {
    let selected = AXUIElementSetAttributeValue(chosen.element, kAXSelectedAttribute as CFString, kCFBooleanTrue)
    guard focused == .success || selected == .success else {
        fputs("AX row focus failed: focus=\(focused.rawValue) selected=\(selected.rawValue)\n", stderr)
        exit(4)
    }
    print("focused row name=\(requested) matches=\(matches.count) x=\(chosen.x) focus=\(focused.rawValue) selected=\(selected.rawValue)")
} else {
    let set = AXUIElementSetAttributeValue(chosen.element, kAXValueAttribute as CFString,
                                          CommandLine.arguments[5] as CFString)
    guard set == .success else {
        fputs("AX value update failed: \(set.rawValue); focus result: \(focused.rawValue)\n", stderr)
        exit(4)
    }
    print("set name=\(requested) matches=\(matches.count) x=\(chosen.x) focus=\(focused.rawValue)")
}
