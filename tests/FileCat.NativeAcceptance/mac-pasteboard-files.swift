// Read only file URL identities and the generation from the logged-in guest's
// clipboard. The caller must first publish its own isolated test sentinel.
import AppKit
import Foundation

let board = NSPasteboard.general
let urls = board.readObjects(forClasses: [NSURL.self],
                             options: [.urlReadingFileURLsOnly: true]) as? [NSURL] ?? []
let filePaths = urls.compactMap { $0.isFileURL ? $0.path : nil }
let result: [String: Any] = ["changeCount": board.changeCount, "filePaths": filePaths]
let json = try JSONSerialization.data(withJSONObject: result, options: [.sortedKeys])
FileHandle.standardOutput.write(json)
