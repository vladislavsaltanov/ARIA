#!/bin/sh

aria_jdk=""
for candidate in \
    "$(/usr/libexec/java_home -v 21 2>/dev/null)" \
    "/opt/homebrew/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home" \
    "/usr/local/opt/openjdk@21/libexec/openjdk.jdk/Contents/Home"; do
    if [ -n "$candidate" ] && grep -q '^JAVA_VERSION="21\.' "$candidate/release" 2>/dev/null; then
        aria_jdk=$candidate
        break
    fi
done

if [ -z "$aria_jdk" ]; then
    echo "android-env: JDK 21 not found (brew install openjdk@21)" >&2
    unset aria_jdk candidate
    return 1 2>/dev/null || exit 1
fi

export JAVA_HOME=$aria_jdk
export ANDROID_HOME=$HOME/Library/Android/sdk
export CMAKE=$ANDROID_HOME/cmake/3.22.1/bin/cmake
export PATH="$JAVA_HOME/bin:$ANDROID_HOME/cmake/3.22.1/bin:$ANDROID_HOME/platform-tools:$PATH"

unset aria_jdk candidate
