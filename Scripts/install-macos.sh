#!/bin/sh

# Copyright (C) 2026 Hyprism Launcher
# SPDX-License-Identifier: GPL-3.0-only

set -eu

GITHUB_RELEASE_BASE_URL="https://github.com/hyprismteam/Hyprism/releases/latest/download"
APP_EXECUTABLE="Hyprism Launcher"
APP_BUNDLE_NAME="Hyprism Launcher.app"
LOCAL_NODE_EXECUTABLE="Hyprism.LocalNode"

if [ "$(uname -s 2>/dev/null || printf 'unknown')" != "Darwin" ]; then
    echo "This installer supports macOS only" >&2
    exit 1
fi

ARCH_NAME="$(uname -m 2>/dev/null || printf 'unknown')"
if [ "$ARCH_NAME" != "arm64" ]; then
    echo "This release supports macOS Apple Silicon only, detected: $ARCH_NAME" >&2
    exit 1
fi

if [ -z "${HOME:-}" ]; then
    echo "HOME is not set" >&2
    exit 1
fi

if command -v curl >/dev/null 2>&1; then
    DOWNLOAD_TOOL="curl"
elif command -v wget >/dev/null 2>&1; then
    DOWNLOAD_TOOL="wget"
else
    echo "The installer requires curl or wget" >&2
    exit 1
fi

for command in awk ditto head hdiutil mktemp shasum sed tr; do
    if ! command -v "$command" >/dev/null 2>&1; then
        echo "The installer requires $command" >&2
        exit 1
    fi
done

download() {
    url="$1"
    destination="$2"

    if [ "$DOWNLOAD_TOOL" = "curl" ]; then
        curl --fail --location --silent --show-error --retry 3 --retry-delay 1 \
            --output "$destination" "$url"
    else
        wget -qO "$destination" "$url"
    fi
}

TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/hyprism-install.XXXXXX")"
MOUNT_POINT="$TEMP_DIR/mount"
MOUNTED=0
cleanup() {
    if [ "$MOUNTED" -eq 1 ]; then
        hdiutil detach "$MOUNT_POINT" -force >/dev/null 2>&1 || true
    fi
    rm -rf "$TEMP_DIR"
}
trap cleanup EXIT HUP INT TERM
mkdir "$MOUNT_POINT"

VERSION_JSON_PATH="$TEMP_DIR/version.json"
echo "[*] Fetching release metadata"
download "${GITHUB_RELEASE_BASE_URL}/version.json" "$VERSION_JSON_PATH"

VERSION="$(sed -n 's/^[[:space:]]*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$VERSION_JSON_PATH" | head -n 1)"
case "$VERSION" in
    ''|*[!0-9A-Za-z.+-]*)
        echo "The release metadata contains an invalid version" >&2
        exit 1
        ;;
esac

ARCHIVE_NAME="Hyprism-mac-arm64-${VERSION}.dmg"
ARCHIVE_URL="${GITHUB_RELEASE_BASE_URL}/${ARCHIVE_NAME}"
CHECKSUM_URL="${ARCHIVE_URL}.sha256"
ARCHIVE_PATH="$TEMP_DIR/$ARCHIVE_NAME"
CHECKSUM_PATH="$TEMP_DIR/$ARCHIVE_NAME.sha256"

echo "[*] Downloading Hyprism Launcher $VERSION"
download "$ARCHIVE_URL" "$ARCHIVE_PATH"
download "$CHECKSUM_URL" "$CHECKSUM_PATH"

EXPECTED_SHA="$(awk 'NF { print $1; exit }' "$CHECKSUM_PATH")"
case "$EXPECTED_SHA" in
    ''|*[!0-9A-Fa-f]*)
        echo "The release checksum is invalid" >&2
        exit 1
        ;;
esac
if [ "${#EXPECTED_SHA}" -ne 64 ]; then
    echo "The release checksum is invalid" >&2
    exit 1
fi

ACTUAL_SHA="$(shasum -a 256 "$ARCHIVE_PATH" | awk '{ print $1 }')"
if [ "$(printf '%s' "$EXPECTED_SHA" | tr '[:upper:]' '[:lower:]')" != "$ACTUAL_SHA" ]; then
    echo "The downloaded disk image failed SHA-256 verification" >&2
    exit 1
fi

hdiutil attach "$ARCHIVE_PATH" \
    -nobrowse \
    -readonly \
    -mountpoint "$MOUNT_POINT" \
    >/dev/null
MOUNTED=1

SOURCE_APP="$MOUNT_POINT/$APP_BUNDLE_NAME"
if [ ! -d "$SOURCE_APP" ]; then
    echo "The disk image does not contain '$APP_BUNDLE_NAME'" >&2
    exit 1
fi
if [ ! -x "$SOURCE_APP/Contents/MacOS/$APP_EXECUTABLE" ]; then
    echo "The application does not contain '$APP_EXECUTABLE'" >&2
    exit 1
fi
if [ ! -x "$SOURCE_APP/Contents/MacOS/$LOCAL_NODE_EXECUTABLE" ]; then
    echo "The application does not contain '$LOCAL_NODE_EXECUTABLE'" >&2
    exit 1
fi

STAGED_APP="$TEMP_DIR/$APP_BUNDLE_NAME"
ditto "$SOURCE_APP" "$STAGED_APP"
hdiutil detach "$MOUNT_POINT" >/dev/null
MOUNTED=0

INSTALL_DIR="${HYPRISM_INSTALL_DIR:-$HOME/Applications}"
TARGET_APP="$INSTALL_DIR/$APP_BUNDLE_NAME"
mkdir -p "$INSTALL_DIR"
if [ -e "$TARGET_APP" ] || [ -L "$TARGET_APP" ]; then
    rm -rf "$TARGET_APP"
fi
mv "$STAGED_APP" "$TARGET_APP"

echo "[+] Installed Hyprism Launcher $VERSION"
echo "[+] Application: $TARGET_APP"
echo "[+] Launch with: open \"$TARGET_APP\""
