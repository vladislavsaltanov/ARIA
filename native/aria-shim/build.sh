#!/bin/sh
set -e
cd "$(dirname "$0")"
RID="${1:-osx-arm64}"
cc -O2 -shared -fPIC -o libaria_shim.dylib aria_shim.c stb_vorbis.c \
  -framework CoreFoundation -framework CoreAudio -framework AudioUnit -framework AudioToolbox -framework Carbon
codesign -f -s - libaria_shim.dylib
mkdir -p "../../runtimes/$RID/native"
cp libaria_shim.dylib "../../runtimes/$RID/native/"
