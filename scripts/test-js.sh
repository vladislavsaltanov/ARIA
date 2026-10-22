#!/bin/sh
set -e
cd "$(dirname "$0")/.."
node --test "tests/js/*.test.mjs"
