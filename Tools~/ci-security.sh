#!/bin/sh
# Hosted-runner fallback for source/security regressions. This compiles against
# the explicit stubs in SecurityTests.cs; it is not a Unity Editor compilation.
set -eu

cd "$(dirname "$0")/.."
project="Tools~/Cluefolk.SecurityTests.csproj"
framework="Native~/Cluefolk.xcframework"
manifest="$framework.integrity"
output="$PWD/.validation/hosted"
version=$(python3 -c 'import json; print(json.load(open("package.json"))["version"])')
widget=$(sed -n 's/^widget=//p' "$manifest")

test -n "$widget"
sh -n Tools~/ci-security.sh Tools~/update-ios-framework.sh
PYTHONPYCACHEPREFIX="$PWD/.validation/pycache" \
  python3 -m py_compile Tools~/ios-framework-integrity.py \
  Tools~/test-security.py Tools~/verify-csharp.py

dotnet restore "$project"
for configuration in EDITOR ANDROID IOS; do
  dotnet build "$project" --configuration "$configuration" --no-restore \
    --output "$output/$configuration"
done
dotnet "$output/EDITOR/Cluefolk.SecurityTests.dll" \
  "$framework" "$manifest" "$version"

# Independently exercise the Python implementation used by the release script.
python3 Tools~/ios-framework-integrity.py verify \
  "$framework" "$manifest" "$version" "$widget"

echo "Hosted .NET security harness and artifact integrity checks passed."
echo "The hosted EDITOR/ANDROID/IOS source builds use explicit test stubs."
echo "They do not replace Tools~/verify-csharp.py or player/device acceptance."
echo "Verified only the checked-in XCFramework $version; unpublished native fixes still require release propagation."
