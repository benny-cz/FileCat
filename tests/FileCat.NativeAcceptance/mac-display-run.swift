// Run one native acceptance command in a supported macOS display mode, then restore the original mode.
// Usage: swift mac-display-run.swift WIDTH HEIGHT PIXEL_WIDTH PIXEL_HEIGHT COMMAND [ARG ...]
// Run as the logged-in desktop user. The child is awaited before restoration.
import CoreGraphics
import Foundation
import Darwin

func run() -> Int32 {
    let args = CommandLine.arguments
    guard args.count >= 6,
          let width = Int(args[1]), let height = Int(args[2]),
          let pixelWidth = Int(args[3]), let pixelHeight = Int(args[4]),
          width > 0, height > 0, pixelWidth > 0, pixelHeight > 0 else {
        fputs("Usage: mac-display-run.swift WIDTH HEIGHT PIXEL_WIDTH PIXEL_HEIGHT COMMAND [ARG ...]\n", stderr)
        return 2
    }
    let display = CGMainDisplayID()
    guard let original = CGDisplayCopyDisplayMode(display),
          let available = CGDisplayCopyAllDisplayModes(display, nil) else {
        fputs("The active display or its modes are unavailable.\n", stderr)
        return 2
    }
    var requested: CGDisplayMode?
    for case let mode as CGDisplayMode in available as NSArray {
        if mode.width == width && mode.height == height &&
            mode.pixelWidth == pixelWidth && mode.pixelHeight == pixelHeight {
            requested = mode
            break
        }
    }
    guard let requested else {
        fputs("The requested logical and physical display mode is unsupported.\n", stderr)
        return 2
    }
    guard CGDisplaySetDisplayMode(display, requested, nil) == .success else {
        fputs("Could not activate the requested display mode.\n", stderr)
        return 3
    }

    var childStatus: Int32 = 3
    if let active = CGDisplayCopyDisplayMode(display),
       active.width == width && active.height == height &&
       active.pixelWidth == pixelWidth && active.pixelHeight == pixelHeight {
        let child = Process()
        child.executableURL = URL(fileURLWithPath: args[5])
        child.arguments = Array(args.dropFirst(6))
        do {
            try child.run()
            child.waitUntilExit()
            childStatus = child.terminationStatus
        } catch {
            fputs("Could not launch the acceptance command: \(error)\n", stderr)
        }
    } else {
        fputs("The requested display mode did not become active.\n", stderr)
    }

    let restored = CGDisplaySetDisplayMode(display, original, nil)
    guard restored == .success, let current = CGDisplayCopyDisplayMode(display),
          current.width == original.width && current.height == original.height &&
          current.pixelWidth == original.pixelWidth && current.pixelHeight == original.pixelHeight else {
        fputs("The original display mode could not be verified after the test.\n", stderr)
        return 4
    }
    print("Restored display mode: \(current.width)x\(current.height) logical, \(current.pixelWidth)x\(current.pixelHeight) pixels")
    return childStatus
}

exit(run())
