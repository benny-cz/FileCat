import Foundation
import CoreGraphics
import ImageIO
import UniformTypeIdentifiers

let root = URL(fileURLWithPath: NSHomeDirectory(), isDirectory: true)
    .appendingPathComponent("FileCatValidation", isDirectory: true)
let output = root.appendingPathComponent("native-screen-\(UUID().uuidString).png")

guard CGPreflightScreenCaptureAccess() || CGRequestScreenCaptureAccess() else {
    print("screenCaptureAccess=false")
    exit(2)
}
var count: UInt32 = 0
guard CGGetActiveDisplayList(0, nil, &count) == .success, count > 0 else {
    print("activeDisplayUnavailable")
    exit(3)
}
guard let image = CGDisplayCreateImage(CGMainDisplayID()),
      let destination = CGImageDestinationCreateWithURL(output as CFURL, UTType.png.identifier as CFString, 1, nil) else {
    print("captureUnavailable")
    exit(4)
}
CGImageDestinationAddImage(destination, image, nil)
guard CGImageDestinationFinalize(destination) else {
    print("imageWriteFailed")
    exit(5)
}
print("capture=\(output.path) width=\(image.width) height=\(image.height)")
