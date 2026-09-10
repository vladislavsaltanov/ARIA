#!/bin/sh
set -e
cd "$(dirname "$0")/.."
RID="${1:-osx-arm64}"

sh native/aria-shim/build.sh "$RID"

dotnet publish src/ARIA.App -c Release -r "$RID" --self-contained \
  -o "publish/$RID/stage" /p:PublishTrimmed=false

xattr -cr "publish/$RID/stage"
find "publish/$RID/stage" -name "*.dylib" -exec codesign -f -s - {} \;
codesign -f -s - "publish/$RID/stage/ARIA.App"

APP="publish/$RID/ARIA.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "publish/$RID/stage/" "$APP/Contents/MacOS/"
mv "$APP/Contents/MacOS/ARIA.App" "$APP/Contents/MacOS/ARIA"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key><string>ARIA</string>
  <key>CFBundleIdentifier</key><string>io.aria.showplayer</string>
  <key>CFBundleName</key><string>ARIA</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>0.1.0</string>
</dict>
</plist>
PLIST

rm -rf "publish/$RID/stage"

echo "publish complete: $APP"
echo "run: $APP/Contents/MacOS/ARIA --selftest"
