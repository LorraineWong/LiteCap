#!/bin/bash
# Builds LiteCap.app for this Mac with the free Xcode Command Line Tools.
# Usage:  cd LiteCapMac && ./build.sh
set -euo pipefail
cd "$(dirname "$0")"

if ! xcrun --find swiftc >/dev/null 2>&1; then
  echo "Swift compiler not found. Install the Command Line Tools first:"
  echo "    xcode-select --install"
  exit 1
fi

ARCH="$(uname -m)"                      # arm64 (Apple silicon) or x86_64 (Intel)
SDK="$(xcrun --sdk macosx --show-sdk-path)"
APP="LiteCap.app"

echo "==> Compiling for $ARCH"
rm -rf build "$APP"
mkdir -p build
xcrun swiftc -O \
  -sdk "$SDK" \
  -target "$ARCH-apple-macos13.0" \
  -module-name LiteCap \
  Sources/*.swift \
  -o build/LiteCap 2>&1 | tee build/compile.log

echo "==> Packaging $APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp build/LiteCap "$APP/Contents/MacOS/LiteCap"
cp Info.plist "$APP/Contents/Info.plist"
cp Resources/MenuIcon.png Resources/MenuIcon@2x.png "$APP/Contents/Resources/"
iconutil -c icns Resources/AppIcon.iconset -o "$APP/Contents/Resources/AppIcon.icns"

echo "==> Signing (ad-hoc, for this Mac only)"
codesign --force --deep --sign - "$APP"

echo ""
echo "Done: $(pwd)/$APP"
echo "Next: move it to Applications and open it:"
echo "    mv -f \"$APP\" /Applications/ && open /Applications/$APP"
