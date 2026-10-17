#!/bin/sh

# Copyright (C) 2026 Hyprism Launcher
# SPDX-License-Identifier: GPL-3.0-only

set -eu

GITHUB_RELEASE_API_URL="https://api.github.com/repos/hyprismteam/Hyprism/releases/latest"
GITHUB_RELEASE_BASE_URL="https://github.com/hyprismteam/Hyprism/releases/download"
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

for command in awk ditto hdiutil mktemp shasum tr; do
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

RELEASE_JSON_PATH="$TEMP_DIR/release.json"
echo "[*] Fetching release metadata"
download "$GITHUB_RELEASE_API_URL" "$RELEASE_JSON_PATH"

# Read only the release tag and asset fields, keeping nested objects separate.
RELEASE_METADATA="$(awk -v prefix="Hyprism-mac-arm64-" -v suffix=".dmg" '
{
    while (match($0, /"([^"\\]|\\.)*"|[][{}:,]|[^][{}:,[:space:]]+/)) {
        token = substr($0, RSTART, RLENGTH)
        $0 = substr($0, RSTART + RLENGTH)
        if (closed) invalid = 1
        if (token == "{" || token == "[") {
            container[++depth] = token
            key[depth] = ""
        } else if (token == "}" || token == "]") {
            if (depth < 1 || (token == "}" && container[depth] != "{") ||
                (token == "]" && container[depth] != "[")) invalid = 1
            if (token == "}" && depth == 3 && key[1] == "\"assets\"" && container[2] == "[") {
                if (asset_name in digests) invalid = 1
                digests[asset_name] = asset_digest
                asset_name = asset_digest = ""
            }
            if (--depth == 0) closed = 1
        } else if (previous == ":") {
            value = substr(token, 2, length(token) - 2)
            if (token !~ /^"/) value = ""
            if (depth == 1 && key[1] == "\"tag_name\"") version = value
            if (depth == 3 && key[1] == "\"assets\"" && container[2] == "[") {
                if (key[3] == "\"name\"") asset_name = value
                if (key[3] == "\"digest\"") asset_digest = value
            }
        } else if (token ~ /^"/) {
            key[depth] = token
        }
        previous = token
    }
}
END {
    if (invalid || depth != 0 || !closed) exit 1
    sub(/^v/, "", version)
    print version " " digests[prefix version suffix]
}
' "$RELEASE_JSON_PATH")"
VERSION="${RELEASE_METADATA%% *}"
case "$VERSION" in
    ''|*[!0-9A-Za-z.+-]*)
        echo "The release metadata contains an invalid version" >&2
        exit 1
        ;;
esac

ARCHIVE_NAME="Hyprism-mac-arm64-${VERSION}.dmg"
ARCHIVE_URL="${GITHUB_RELEASE_BASE_URL}/v${VERSION}/${ARCHIVE_NAME}"
ARCHIVE_PATH="$TEMP_DIR/$ARCHIVE_NAME"
EXPECTED_SHA="${RELEASE_METADATA#* }"
case "$EXPECTED_SHA" in
    sha256:*) EXPECTED_SHA="${EXPECTED_SHA#sha256:}" ;;
    *)
        echo "The release asset has no valid SHA-256 digest" >&2
        exit 1
        ;;
esac
case "$EXPECTED_SHA" in
    ''|*[!0-9A-Fa-f]*)
        echo "The release asset has no valid SHA-256 digest" >&2
        exit 1
        ;;
esac
if [ "${#EXPECTED_SHA}" -ne 64 ]; then
    echo "The release asset has no valid SHA-256 digest" >&2
    exit 1
fi

echo "[*] Downloading Hyprism Launcher $VERSION"
download "$ARCHIVE_URL" "$ARCHIVE_PATH"

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
