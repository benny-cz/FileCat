// Records the macOS accessibility focus that VoiceOver follows for one test-owned process.
//
//   mac-ax-focus-observer observe PID OUT.jsonl STOP_FILE  — append one JSON line per AXFocusedUIElementChanged
//   mac-ax-focus-observer focused PID                      — print the application's current AXFocusedUIElement
//
// Each record holds the focused element's role, title, description, value and help, the attributes VoiceOver
// speaks. The observer only reads; it never performs an action or changes a value.
import ApplicationServices
import Foundation

func text(_ element: AXUIElement, _ attribute: String) -> String {
    var value: CFTypeRef?
    guard AXUIElementCopyAttributeValue(element, attribute as CFString, &value) == .success, let value else { return "" }
    if let string = value as? String { return string }
    if let number = value as? NSNumber { return number.stringValue }
    return ""
}

func describe(_ element: AXUIElement) -> [String: String] {
    var pid: pid_t = 0
    AXUIElementGetPid(element, &pid)
    return ["role": text(element, kAXRoleAttribute), "subrole": text(element, kAXSubroleAttribute),
            "title": text(element, kAXTitleAttribute), "description": text(element, kAXDescriptionAttribute),
            "value": text(element, kAXValueAttribute), "help": text(element, kAXHelpAttribute), "pid": String(pid)]
}

func json(_ object: [String: String]) -> String {
    let data = try! JSONSerialization.data(withJSONObject: object, options: [.sortedKeys])
    return String(data: data, encoding: .utf8)!
}

let arguments = CommandLine.arguments
guard arguments.count >= 3, let pid = pid_t(arguments[2]) else {
    FileHandle.standardError.write("usage: mac-ax-focus-observer observe PID OUT STOP | focused PID\n".data(using: .utf8)!)
    exit(2)
}
let application = AXUIElementCreateApplication(pid)

if arguments[1] == "focused" {
    var value: CFTypeRef?
    guard AXUIElementCopyAttributeValue(application, kAXFocusedUIElementAttribute as CFString, &value) == .success,
          let value, CFGetTypeID(value) == AXUIElementGetTypeID() else {
        print("{}")
        exit(0)
    }
    print(json(describe(value as! AXUIElement)))
    exit(0)
}

guard arguments[1] == "observe", arguments.count == 5 else { exit(2) }
let output = FileHandle(forWritingAtPath: arguments[3]) ?? {
    FileManager.default.createFile(atPath: arguments[3], contents: nil)
    return FileHandle(forWritingAtPath: arguments[3])!
}()
let stop = arguments[4]
var observer: AXObserver?
let callback: AXObserverCallback = { _, element, notification, refcon in
    var record = describe(element)
    record["notification"] = notification as String
    record["time"] = String(format: "%.3f", Date().timeIntervalSince1970)
    let handle = Unmanaged<FileHandle>.fromOpaque(refcon!).takeUnretainedValue()
    handle.write((json(record) + "\n").data(using: .utf8)!)
}
guard AXObserverCreate(pid, callback, &observer) == .success, let observer else {
    FileHandle.standardError.write("AXObserverCreate failed\n".data(using: .utf8)!)
    exit(1)
}
let refcon = Unmanaged.passUnretained(output).toOpaque()
var registered: [String: String] = [:]
// A process that is still starting answers kAXErrorCannotComplete; retry until its accessibility is available.
let registrationDeadline = Date().addingTimeInterval(30)
for notification in [kAXFocusedUIElementChangedNotification, kAXFocusedWindowChangedNotification] {
    var status = AXObserverAddNotification(observer, application, notification as CFString, refcon)
    while status == .cannotComplete && Date() < registrationDeadline {
        Thread.sleep(forTimeInterval: 0.25)
        status = AXObserverAddNotification(observer, application, notification as CFString, refcon)
    }
    registered[notification] = String(status.rawValue)
}
CFRunLoopAddSource(CFRunLoopGetCurrent(), AXObserverGetRunLoopSource(observer), .defaultMode)
registered["notification"] = "observing"
registered["time"] = String(format: "%.3f", Date().timeIntervalSince1970)
output.write((json(registered) + "\n").data(using: .utf8)!)
let deadline = Date().addingTimeInterval(300)
while !FileManager.default.fileExists(atPath: stop) && Date() < deadline {
    CFRunLoopRunInMode(.defaultMode, 0.1, false)
}
