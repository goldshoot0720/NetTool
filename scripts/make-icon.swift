import AppKit
import Foundation

// 潮閘 Tidegate 圖標產生器：輸出 macOS iconset（含 @2x）
// 用法：swift scripts/make-icon.swift <輸出目錄>

let outDir = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "/tmp/tidegate.iconset"
try? FileManager.default.createDirectory(atPath: outDir, withIntermediateDirectories: true)

let limeTop = NSColor(srgbRed: 0.894, green: 0.969, blue: 0.549, alpha: 1)
let limeBottom = NSColor(srgbRed: 0.827, green: 0.945, blue: 0.396, alpha: 1)
let ink = NSColor(srgbRed: 0.090, green: 0.125, blue: 0.086, alpha: 1)
let deep = NSColor(srgbRed: 0.110, green: 0.149, blue: 0.129, alpha: 1)

func render(_ size: Int) -> Data? {
    guard let rep = NSBitmapImageRep(
        bitmapDataPlanes: nil,
        pixelsWide: size,
        pixelsHigh: size,
        bitsPerSample: 8,
        samplesPerPixel: 4,
        hasAlpha: true,
        isPlanar: false,
        colorSpaceName: .deviceRGB,
        bytesPerRow: 0,
        bitsPerPixel: 0
    ) else { return nil }

    rep.size = NSSize(width: size, height: size)
    NSGraphicsContext.saveGraphicsState()
    guard let ctx = NSGraphicsContext(bitmapImageRep: rep) else {
        NSGraphicsContext.restoreGraphicsState()
        return nil
    }
    NSGraphicsContext.current = ctx
    let cg = ctx.cgContext
    cg.setAllowsAntialiasing(true)
    cg.setShouldAntialias(true)
    cg.interpolationQuality = .high
    cg.clear(CGRect(x: 0, y: 0, width: size, height: size))

    let s = CGFloat(size)
    // macOS 圖標留白約 10%
    let inset = s * 0.098
    let body = CGRect(x: inset, y: inset, width: s - inset * 2, height: s - inset * 2)
    let radius = body.width * 0.226

    // 底色：深墨綠圓角方，帶一點上亮下暗
    let shell = NSBezierPath(roundedRect: body, xRadius: radius, yRadius: radius)
    NSGradient(starting: NSColor(srgbRed: 0.149, green: 0.204, blue: 0.173, alpha: 1),
               ending: deep)?.draw(in: shell, angle: -90)

    // 內部檸檬綠圓角方
    let pad = body.width * 0.135
    let plate = body.insetBy(dx: pad, dy: pad)
    let platePath = NSBezierPath(roundedRect: plate, xRadius: plate.width * 0.26, yRadius: plate.width * 0.26)
    NSGradient(starting: limeTop, ending: limeBottom)?.draw(in: platePath, angle: -90)

    // 閘門意象：兩根立柱 + 橫樑，疊在字後面會太雜，改用字形為主
    let fontSize = plate.height * 0.62
    let font = NSFont(name: "PingFang TC Semibold", size: fontSize)
        ?? NSFont(name: "PingFang SC Semibold", size: fontSize)
        ?? NSFont.systemFont(ofSize: fontSize, weight: .bold)
    let text = NSAttributedString(string: "閘", attributes: [
        .font: font,
        .foregroundColor: ink,
    ])
    let bounds = text.size()
    text.draw(at: NSPoint(
        x: plate.midX - bounds.width / 2,
        y: plate.midY - bounds.height / 2 + plate.height * 0.01
    ))

    ctx.flushGraphics()
    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])
}

let entries: [(String, Int)] = [
    ("icon_16x16.png", 16),
    ("icon_16x16@2x.png", 32),
    ("icon_32x32.png", 32),
    ("icon_32x32@2x.png", 64),
    ("icon_128x128.png", 128),
    ("icon_128x128@2x.png", 256),
    ("icon_256x256.png", 256),
    ("icon_256x256@2x.png", 512),
    ("icon_512x512.png", 512),
    ("icon_512x512@2x.png", 1024),
]

for (name, size) in entries {
    guard let data = render(size) else {
        FileHandle.standardError.write("failed: \(name)\n".data(using: .utf8)!)
        exit(1)
    }
    let url = URL(fileURLWithPath: outDir).appendingPathComponent(name)
    try data.write(to: url)
}

print("wrote \(entries.count) images to \(outDir)")
