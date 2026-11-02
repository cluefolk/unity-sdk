#!/bin/sh
# Replaces Native~/Cluefolk.xcframework with one built from an ios-sdk checkout
# of the same version as package.json. Unity ignores folders ending in "~", so
# the framework needs no .meta files; the iOS build step copies it into Xcode.
#
# Usage: Tools~/update-ios-framework.sh <path to ios-sdk>
set -eu
ios=$(cd "${1:?usage: Tools~/update-ios-framework.sh <path to ios-sdk>}" && pwd)
cd "$(dirname "$0")/.."
version=$(python3 -c 'import json; print(json.load(open("package.json"))["version"])')
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
"$ios/Scripts/build-xcframework.sh" "$version" "$work"
rm -rf Native~/Cluefolk.xcframework
mkdir -p Native~
cp -R "$work/Cluefolk.xcframework" Native~/
echo "Native~/Cluefolk.xcframework $version"
