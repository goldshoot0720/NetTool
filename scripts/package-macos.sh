#!/bin/bash
# 把 Tidegate.App 打包成 macOS 的 .app（自包含，含圖標與 Info.plist）
# 用法：scripts/package-macos.sh [osx-arm64|osx-x64]
# 環境變數：VERSION（預設 1.0.0）寫進 Info.plist

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# 本機用 Homebrew 的 dotnet；CI 由 setup-dotnet 準備，路徑不一定存在。
if [ -z "${DOTNET_ROOT:-}" ] && [ -x /opt/homebrew/opt/dotnet/libexec/dotnet ]; then
  export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
  export PATH="$DOTNET_ROOT:$PATH"
fi

ARCH="${1:-osx-arm64}"
VERSION="${VERSION:-1.0.0}"
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

# 2. 自包含發佈成單一檔案（不依賴使用者機器上的 .NET）
rm -rf "$STAGE" "$OUT"
dotnet publish "$ROOT/src/Tidegate.App/Tidegate.App.csproj" \
  -c Release -r "$ARCH" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none \
  -o "$STAGE" --nologo -v q

# 3. 組裝 bundle：MacOS/ 裡只放一個可執行檔
mkdir -p "$OUT/Contents/MacOS" "$OUT/Contents/Resources"
cp "$STAGE/Tidegate.App" "$OUT/Contents/MacOS/Tidegate"
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
    <string>Tidegate</string>
    <key>CFBundleIconFile</key>
    <string>Tidegate</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
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

chmod +x "$OUT/Contents/MacOS/Tidegate"

# 版本號寫進 Info.plist（CI 會帶入 tag 版本）
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$OUT/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $VERSION" "$OUT/Contents/Info.plist"

# 4. 本機臨時簽章，避免 Gatekeeper 每次詢問
codesign --force --deep --sign - "$OUT" >/dev/null 2>&1 || \
  echo "warning: ad-hoc codesign skipped" >&2

rm -rf "$STAGE"
echo "built: $OUT"
