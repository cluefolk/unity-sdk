#!/bin/sh
# Replaces Native~/Cluefolk.xcframework with one built from an ios-sdk checkout
# of the same version as package.json. Unity ignores folders ending in "~", so
# the framework needs no .meta files; the iOS build step copies it into Xcode.
#
# Usage: Tools~/update-ios-framework.sh <path to ios-sdk> <expected widget digest>
set -eu
usage="usage: Tools~/update-ios-framework.sh <path to ios-sdk> <expected widget digest>"
ios=$(cd "${1:?$usage}" && pwd)
expected_widget=${2:?$usage}
cd "$(dirname "$0")/.."
version=$(python3 -c 'import json; print(json.load(open("package.json"))["version"])')
integrity="$PWD/Tools~/ios-framework-integrity.py"

git -C "$ios" rev-parse --is-inside-work-tree >/dev/null 2>&1 || {
  echo "iOS SDK path is not a Git checkout: $ios" >&2
  exit 1
}
if [ -n "$(git -C "$ios" status --porcelain --untracked-files=normal)" ]; then
  echo "iOS SDK checkout is dirty; commit or clean it before building a release artifact." >&2
  exit 1
fi
metadata=$(python3 "$integrity" source "$ios")
source_version=$(printf '%s\n' "$metadata" | sed -n '1p')
source_widget=$(printf '%s\n' "$metadata" | sed -n '2p')
if [ "$source_version" != "$version" ]; then
  echo "iOS SDK version $source_version does not match Unity package $version." >&2
  exit 1
fi
if [ "$source_widget" != "$expected_widget" ]; then
  echo "iOS widget digest $source_widget does not match expected $expected_widget." >&2
  exit 1
fi
source_commit=$(git -C "$ios" rev-parse HEAD)
tag_commit=$(git -C "$ios" rev-parse -q --verify "refs/tags/$version^{commit}" || true)
if [ -z "$tag_commit" ] || [ "$tag_commit" != "$source_commit" ]; then
  echo "iOS SDK checkout must be exactly at tag $version." >&2
  exit 1
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
"$ios/Scripts/build-xcframework.sh" "$version" "$work"
if [ -n "$(git -C "$ios" status --porcelain --untracked-files=normal)" ]; then
  echo "Building changed the iOS SDK checkout; refusing to install the artifact." >&2
  exit 1
fi
if [ "$(git -C "$ios" rev-parse HEAD)" != "$source_commit" ]; then
  echo "iOS SDK checkout changed commits while building; refusing the artifact." >&2
  exit 1
fi

staged="$work/staged/Cluefolk.xcframework"
mkdir -p "$work/staged"
cp -R "$work/Cluefolk.xcframework" "$staged"
python3 "$integrity" write "$staged" "$work/Cluefolk.xcframework.integrity" \
  "$version" "$expected_widget"
python3 "$integrity" verify "$staged" "$work/Cluefolk.xcframework.integrity" \
  "$version" "$expected_widget"

rm -rf Native~/Cluefolk.xcframework
mkdir -p Native~
cp -R "$staged" Native~/
cp "$work/Cluefolk.xcframework.integrity" Native~/Cluefolk.xcframework.integrity
python3 "$integrity" verify Native~/Cluefolk.xcframework \
  Native~/Cluefolk.xcframework.integrity "$version" "$expected_widget"
echo "Native~/Cluefolk.xcframework $version widget=$expected_widget sha256=$(sed -n 's/^sha256=//p' Native~/Cluefolk.xcframework.integrity)"
