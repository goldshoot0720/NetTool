#!/bin/bash
# 把 Tidegate.App 打包成 macOS 的 .app（自包含，含圖標與 Info.plist）
# 用法：scripts/package-macos.sh [osx-arm64|osx-x64]

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_ROOT="${DOTNET_ROOT:-/opt/homebrew/opt/dotnet/libexec}"
export PATH="$DOTNET_ROOT:$PATH"

ARCH="${1:-osx-arm64}"
OUT="$ROOT/build/Tidegate.app"
STAGE="$ROOT/build/.publish"
ICON="$ROOT/src/Tidegate.App/Assets/Tidegate.icns"

# 1. 圖標不存在就先畫一張
if [ ! -f "$ICON" ]; then
  ICONSET="$ROOT/build/Tidegate.iconset"
  rm -rf "$ICONSET"
  swift "$ROOT/scripts/make-icon.swift" "$ICONSET"
  iconutil -c icns "$ICONSET" -o "$ICON"
fi

# 2. 自包含發佈（不依賴使用者機器上的 .NET）
rm -rf "$STAGE" "$OUT"
dotnet publish "$ROOT/src/Tidegate.App/Tidegate.App.csproj" \
  -c Release -r "$ARCH" --self-contained true -o "$STAGE" --nologo -v q

# 3. 組裝 bundle
mkdir -p "$OUT/Contents/MacOS" "$OUT/Contents/Resources"
cp -R "$STAGE"/. "$OUT/Contents/MacOS/"
cp "$ICON" "$OUT/Contents/Resources/Tidegate.icns"
printf 'APPL????' > "$OUT/Contents/PkgInfo"

cat > "$OUT/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Tidegate</string>
    <key>CFBundleDisplayName</key>
    <string>潮閘 Tidegate</string>
    <key>CFBundleIdentifier</key>
    <string>com.tidegate.app</string>
    <key>CFBundleExecutable</key>
    <string>Tidegate.App</string>
    <key>CFBundleIconFile</key>
    <string>Tidegate</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundleVersion</key>
    <string>1</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>13.0</string>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.utilities</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
    <key>NSHumanReadableCopyright</key>
    <string>潮閘 Tidegate</string>
</dict>
</plist>
PLIST

chmod +x "$OUT/Contents/MacOS/Tidegate.App"

# 4. 本機臨時簽章，避免 Gatekeeper 每次詢問
codesign --force --deep --sign - "$OUT" >/dev/null 2>&1 || \
  echo "warning: ad-hoc codesign skipped" >&2

rm -rf "$STAGE"
echo "built: $OUT"
