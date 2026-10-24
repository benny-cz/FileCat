// Native Mac acceptance for the shared overlay dialog close control.
// Usage: mac-ax-dialog-label FILECAT_EXE PID REPORT.json
// Open an isolated FileCat overlay dialog before invoking it.
import Foundation
import ApplicationServices

struct Rect: Codable {
    let x: Double
    let y: Double
    let width: Double
    let height: Double

    func contains(_ other: Rect) -> Bool {
        other.x >= x && other.y >= y && other.x + other.width <= x + width &&
        other.y + other.height <= y + height
    }
}

struct Match: Codable {
    let role: String
    let title: String
    let help: String
    let enabled: Bool
    let bounds: Rect
    let window: Rect
}

func attribute(_ element: AXUIElement, _ name: CFString) -> CFTypeRef? {
    var value: CFTypeRef?
    return AXUIElementCopyAttributeValue(element, name, &value) == .success ? value : nil
}

func label(_ value: CFTypeRef?) -> String {
    guard let value else { return "" }
    return String(describing: value)
}

func bounds(_ element: AXUIElement) -> Rect? {
    guard let position = attribute(element, kAXPositionAttribute as CFString),
          let size = attribute(element, kAXSizeAttribute as CFString),
          CFGetTypeID(position) == AXValueGetTypeID(),
          CFGetTypeID(size) == AXValueGetTypeID() else { return nil }
    var point = CGPoint.zero
    var extent = CGSize.zero
    guard AXValueGetValue(position as! AXValue, .cgPoint, &point),
          AXValueGetValue(size as! AXValue, .cgSize, &extent) else { return nil }
    return Rect(x: point.x, y: point.y, width: extent.width, height: extent.height)
}

func executablePath(_ pid: Int32) throws -> String {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: "/bin/ps")
    process.arguments = ["-p", String(pid), "-o", "command="]
    let output = Pipe()
    process.standardOutput = output
    process.standardError = Pipe()
    try process.run()
    let data = output.fileHandleForReading.readDataToEndOfFile()
    process.waitUntilExit()
    guard process.terminationStatus == 0,
          let command = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines),
          !command.isEmpty else { throw NSError(domain: "FileCatAX", code: 1, userInfo: [NSLocalizedDescriptionKey: "The exact test process is unavailable."]) }
    return command
}

guard CommandLine.arguments.count == 4, let pid = Int32(CommandLine.arguments[2]) else {
    fputs("usage: mac-ax-dialog-label FILECAT_EXE PID REPORT.json\n", stderr)
    exit(2)
}
let expected = URL(fileURLWithPath: CommandLine.arguments[1]).standardizedFileURL.path
let report = URL(fileURLWithPath: CommandLine.arguments[3]).standardizedFileURL
let validation = FileManager.default.homeDirectoryForCurrentUser
    .appendingPathComponent("FileCatValidation", isDirectory: true).standardizedFileURL.path + "/"
guard expected.hasPrefix(validation), expected.hasSuffix("/Contents/MacOS/FileCat"),
      report.path.hasPrefix(validation), FileManager.default.fileExists(atPath: expected) else {
    fputs("The executable or report is outside the isolated validation root.\n", stderr)
    exit(2)
}

var result: [String: Any] = ["passed": false, "pid": pid, "executable": expected]
do {
    let command = try executablePath(pid)
    guard command == expected || command.hasPrefix(expected + " ") else {
        throw NSError(domain: "FileCatAX", code: 2,
                      userInfo: [NSLocalizedDescriptionKey: "The PID belongs to a different executable."])
    }
    guard AXIsProcessTrusted() else {
        throw NSError(domain: "FileCatAX", code: 3,
                      userInfo: [NSLocalizedDescriptionKey: "Accessibility access is unavailable."])
    }
    let app = AXUIElementCreateApplication(pid)
    guard let windows = attribute(app, kAXWindowsAttribute as CFString) as? [AXUIElement],
          !windows.isEmpty else {
        throw NSError(domain: "FileCatAX", code: 4,
                      userInfo: [NSLocalizedDescriptionKey: "The test app has no accessible window."])
    }
    var matches: [Match] = []
    var visited = 0
    var copyApproval = false
    var moveApproval = false
    var deleteApproval = false
    var destinationField = false
    func scan(_ element: AXUIElement, _ window: Rect, _ depth: Int) {
        guard depth <= 20 && visited < 3000 else { return }
        visited += 1
        let title = label(attribute(element, kAXTitleAttribute as CFString))
        let role = label(attribute(element, kAXRoleAttribute as CFString))
        if role == "AXButton" && title == "Copy" {
            copyApproval = copyApproval ||
                ((attribute(element, kAXEnabledAttribute as CFString) as? NSNumber)?.boolValue ?? false)
        }
        if role == "AXButton" && title == "Move" {
            moveApproval = moveApproval ||
                ((attribute(element, kAXEnabledAttribute as CFString) as? NSNumber)?.boolValue ?? false)
        }
        if role == "AXButton" && title == "Delete" {
            deleteApproval = deleteApproval ||
                ((attribute(element, kAXEnabledAttribute as CFString) as? NSNumber)?.boolValue ?? false)
        }
        if role == "AXTextField" && title == "Destination" { destinationField = true }
        if title == "Close dialog", let frame = bounds(element) {
            let help = label(attribute(element, kAXHelpAttribute as CFString))
            let enabled = (attribute(element, kAXEnabledAttribute as CFString) as? NSNumber)?.boolValue ?? false
            matches.append(Match(role: role, title: title, help: help,
                                 enabled: enabled, bounds: frame, window: window))
        }
        let children = attribute(element, kAXChildrenAttribute as CFString) as? [AXUIElement] ?? []
        for child in children.prefix(50) { scan(child, window, depth + 1) }
    }
    for window in windows.prefix(5) {
        if let frame = bounds(window) { scan(window, frame, 0) }
    }
    result["visited"] = visited
    result["copyApproval"] = copyApproval
    result["moveApproval"] = moveApproval
    result["deleteApproval"] = deleteApproval
    result["destinationField"] = destinationField
    result["matches"] = matches.map { match in
        ["role": match.role, "title": match.title, "help": match.help,
         "enabled": match.enabled,
         "bounds": ["x": match.bounds.x, "y": match.bounds.y,
                    "width": match.bounds.width, "height": match.bounds.height],
         "window": ["x": match.window.x, "y": match.window.y,
                    "width": match.window.width, "height": match.window.height]] as [String: Any]
    }
    guard matches.count == 1, let close = matches.first,
          close.role == "AXButton", close.enabled,
          close.help.contains("Cancel this dialog without applying changes"),
          close.bounds.width >= 28, close.bounds.width <= 60,
          close.bounds.height >= 28, close.bounds.height <= 60,
          close.window.contains(close.bounds), visited < 3000 else {
        throw NSError(domain: "FileCatAX", code: 5,
                      userInfo: [NSLocalizedDescriptionKey: "The close dialog control lacks a unique, enabled, labelled pointer target."])
    }
    result["passed"] = true
} catch {
    result["error"] = error.localizedDescription
}

let data = try JSONSerialization.data(withJSONObject: result, options: [.prettyPrinted, .sortedKeys])
try data.write(to: report, options: .atomic)
print(String(data: data, encoding: .utf8)!)
exit((result["passed"] as? Bool) == true ? 0 : 1)
