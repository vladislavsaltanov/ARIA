#!/bin/sh
set -e
cd "$(dirname "$0")"
RID="${1:-osx-arm64}"
OUT="../../runtimes/$RID/native"
mkdir -p "$OUT"

case "$RID" in
osx-arm64)
  cc -O2 -shared -fPIC -arch arm64 \
    -o "$OUT/libaria_shim.dylib" aria_shim.c stb_vorbis.c \
    -framework CoreFoundation -framework CoreAudio -framework AudioUnit -framework AudioToolbox -framework Carbon
  codesign -f -s - "$OUT/libaria_shim.dylib"
  cp "$OUT/libaria_shim.dylib" ./libaria_shim.dylib
  ;;
osx-x64)
  cc -O2 -shared -fPIC -arch x86_64 \
    -o "$OUT/libaria_shim.dylib" aria_shim.c stb_vorbis.c \
    -framework CoreFoundation -framework CoreAudio -framework AudioUnit -framework AudioToolbox -framework Carbon
  codesign -f -s - "$OUT/libaria_shim.dylib"
  ;;
linux-x64)
  zig cc -O2 -shared -fPIC -target x86_64-linux-gnu -s \
    -o "$OUT/libaria_shim.so" aria_shim.c stb_vorbis.c \
    -lpthread -ldl -lm
  ;;
win-x64)
  x86_64-w64-mingw32-gcc -O2 -shared \
    -o "$OUT/aria_shim.dll" aria_shim.c stb_vorbis.c \
    -lole32 -lwinmm -luuid -lversion -ladvapi32
  ;;
*)
  echo "unknown RID: $RID (want osx-arm64, osx-x64, linux-x64, win-x64)" >&2
  exit 1
  ;;
esac

echo "shim built: $OUT"
