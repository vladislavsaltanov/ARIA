#!/bin/sh
set -e
cd "$(dirname "$0")/.."
RID="${1:-osx-arm64}"

sh native/aria-shim/build.sh "$RID"

dotnet publish src/ARIA.App -c Release -r "$RID" --self-contained \
  -o "publish/$RID/stage" /p:PublishTrimmed=false

for r in publish/$RID/stage/runtimes/*/; do
  if [ "$r" != "publish/$RID/stage/runtimes/$RID/" ]; then
    rm -rf "$r"
  fi
done

case "$RID" in
osx-*)
  xattr -cr "publish/$RID/stage"
  case "$RID" in osx-arm64) THIN=arm64 ;; osx-x64) THIN=x86_64 ;; esac
  if [ -n "${THIN:-}" ] && command -v lipo >/dev/null 2>&1; then
    find "publish/$RID/stage" -name "*.dylib" | while IFS= read -r d; do
      if file "$d" | grep -q "universal binary"; then
        lipo -thin "$THIN" -output "$d.thin" "$d" && mv "$d.thin" "$d"
      fi
    done
  fi
  find "publish/$RID/stage" -name "*.dylib" -exec codesign -f -s - {} \;
  codesign -f -s - "publish/$RID/stage/ARIA.App"
  APP="publish/$RID/ARIA.app"
  rm -rf "$APP"
  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
  cp -R "publish/$RID/stage/" "$APP/Contents/MacOS/"
  mv "$APP/Contents/MacOS/ARIA.App" "$APP/Contents/MacOS/ARIA"
  cat >"$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key><string>ARIA</string>
  <key>CFBundleIdentifier</key><string>io.aria.showplayer</string>
  <key>CFBundleName</key><string>ARIA</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>0.3.0</string>
</dict>
</plist>
PLIST
  rm -rf "publish/$RID/stage"
  echo "publish complete: $APP"
  echo "run: $APP/Contents/MacOS/ARIA --selftest"
  ;;
linux-*)
  mv "publish/$RID/stage/ARIA.App" "publish/$RID/stage/aria"
  rm -rf "publish/$RID/ARIA"
  mkdir -p "publish/$RID/ARIA"
  cp -R "publish/$RID/stage/" "publish/$RID/ARIA/"
  rm -rf "publish/$RID/stage"
  tar -czf "publish/$RID/aria-$RID.tar.gz" -C "publish/$RID" ARIA
  echo "publish complete: publish/$RID/ARIA/ + publish/$RID/aria-$RID.tar.gz"
  echo "run: publish/$RID/ARIA/aria --selftest"
  ;;
win-*)
  rm -rf "publish/$RID/ARIA"
  mkdir -p "publish/$RID/ARIA"
  cp -R "publish/$RID/stage/" "publish/$RID/ARIA/"
  rm -rf "publish/$RID/stage"
  (cd "publish/$RID" && ditto -c -k --sequesterRsrc ARIA "aria-$RID.zip")
  echo "publish complete: publish/$RID/ARIA/ + publish/$RID/aria-$RID.zip"
  printf 'run: publish\\%s\\ARIA\\ARIA.App.exe --selftest\n' "$RID"
  ;;
*)
  echo "unknown RID: $RID" >&2
  exit 1
  ;;
esac
