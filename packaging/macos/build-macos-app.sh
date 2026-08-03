#!/usr/bin/env bash
# Assembles a macOS .app bundle from a `dotnet publish` output directory.
#
# Usage: build-macos-app.sh <publish-dir> <output.app> [version]
#
# The published output already contains the self-contained apphost, managed dlls,
# native libraries and the bundled rclone binary; we just wrap them in the bundle
# layout macOS expects and drop in an Info.plist.
set -euo pipefail

PUBLISH_DIR="${1:?publish directory required}"
APP_PATH="${2:?output .app path required}"
VERSION="${3:-0.0.0-dev}"
EXECUTABLE="EmuShelf"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ ! -e "$PUBLISH_DIR/$EXECUTABLE" ]; then
  echo "Published executable '$EXECUTABLE' not found in $PUBLISH_DIR" >&2
  exit 1
fi

rm -rf "$APP_PATH"
mkdir -p "$APP_PATH/Contents/MacOS" "$APP_PATH/Contents/Resources"

# .NET self-contained apps expect their files beside the apphost, so the entire
# publish output (managed dlls, native libs, rclone, licences) goes into MacOS/.
cp -R "$PUBLISH_DIR/." "$APP_PATH/Contents/MacOS/"
chmod +x "$APP_PATH/Contents/MacOS/$EXECUTABLE"

sed "s/@VERSION@/$VERSION/g" "$SCRIPT_DIR/Info.plist" > "$APP_PATH/Contents/Info.plist"

echo "Built $APP_PATH (version $VERSION)"
