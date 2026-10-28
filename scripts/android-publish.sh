#!/bin/sh
set -e
cd "$(dirname "$0")/.."
. scripts/android-env.sh

SIGNING_DIR="${ARIA_SIGNING_DIR:-android/signing}"
STORE="$SIGNING_DIR/aria-release.jks"
PROPS="$SIGNING_DIR/keystore.properties"
ALIAS="aria-monitor"

BUILD_TOOLS="$(ls -d "$ANDROID_HOME"/build-tools/*/ 2>/dev/null | sort -V | tail -1)"
if [ -z "$BUILD_TOOLS" ] || [ ! -x "${BUILD_TOOLS}apksigner" ]; then
  echo "android-publish: Android build-tools not found in $ANDROID_HOME" >&2
  exit 1
fi

if [ ! -f "$PROPS" ] || [ ! -f "$STORE" ]; then
  if [ -f "$STORE" ] || [ -f "$PROPS" ]; then
    echo "android-publish: $STORE and $PROPS must exist together; restore both or remove both" >&2
    exit 1
  fi
  mkdir -p "$SIGNING_DIR"
  chmod 700 "$SIGNING_DIR"
  PASSWORD="$(openssl rand -base64 24 | tr -d '/+=' | cut -c1-24)"
  keytool -genkeypair -keystore "$STORE" -storetype PKCS12 -alias "$ALIAS" \
    -keyalg RSA -keysize 4096 -validity 10950 \
    -storepass "$PASSWORD" -keypass "$PASSWORD" \
    -dname "CN=ARIA Monitor, O=ARIA, C=RU" >/dev/null
  printf 'storeFile=aria-release.jks\nstorePassword=%s\nkeyAlias=%s\nkeyPassword=%s\n' \
    "$PASSWORD" "$ALIAS" "$PASSWORD" >"$PROPS"
  chmod 600 "$STORE" "$PROPS"
  echo "android-publish: created a NEW signing key in $SIGNING_DIR - back it up, a lost key means the app cannot be updated in place"
fi

export ARIA_KEYSTORE_PROPERTIES="$(cd "$(dirname "$PROPS")" && pwd)/$(basename "$PROPS")"

(cd android && ./gradlew :app:assembleRelease "$@")

VERSION="$(sed -n 's/.*versionName = "\(.*\)".*/\1/p' android/app/build.gradle.kts | head -1)"
SRC="android/app/build/outputs/apk/release/app-release.apk"
OUT_DIR="publish/android"
OUT="$OUT_DIR/ARIA-Monitor-$VERSION.apk"

"${BUILD_TOOLS}apksigner" verify --verbose "$SRC" | grep -q 'Verified using v2 scheme.*true' || {
  echo "android-publish: APK is not signed (v2 verification failed)" >&2
  exit 1
}
"${BUILD_TOOLS}zipalign" -c -P 16 4 "$SRC" || {
  echo "android-publish: APK is not zip-aligned" >&2
  exit 1
}

mkdir -p "$OUT_DIR"
cp "$SRC" "$OUT"

echo "publish complete: $OUT"
echo "sha256: $(shasum -a 256 "$OUT" | cut -d' ' -f1)"
"${BUILD_TOOLS}apksigner" verify --print-certs "$OUT" | grep 'certificate SHA-256'
echo "install: adb install -r $OUT"
