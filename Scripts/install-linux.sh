#!/bin/sh

# Copyright (C) 2026 Hyprism Launcher
# SPDX-License-Identifier: GPL-3.0-only

set -eu

GITHUB_RELEASE_API_URL="https://api.github.com/repos/hyprismteam/Hyprism/releases/latest"
GITHUB_RELEASE_BASE_URL="https://github.com/hyprismteam/Hyprism/releases/download"
GITHUB_ICON_URL="https://raw.githubusercontent.com/hyprismteam/Hyprism/main/Sources/Hyprism.Desktop/Assets/Images/logo.svg"
APP_ID="io.github.hyprismteam.Hyprism"
APP_EXECUTABLE="Hyprism Launcher"
LOCAL_NODE_EXECUTABLE="Hyprism.LocalNode"

OS_NAME="$(uname -s 2>/dev/null || printf 'unknown')"
ARCH_NAME="$(uname -m 2>/dev/null || printf 'unknown')"

if [ "$OS_NAME" != "Linux" ]; then
    case "$OS_NAME" in
        Darwin*)
            echo "Hyprism is installed on macOS from the Apple Silicon DMG in Releases" >&2
            ;;
        *)
            echo "This installer supports Linux x64 only" >&2
            ;;
    esac
    exit 1
fi

case "$ARCH_NAME" in
    x86_64|amd64) ;;
    *)
        echo "This release supports Linux x64 only, detected: $ARCH_NAME" >&2
        exit 1
        ;;
esac

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

for command in awk sha256sum; do
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
cleanup() {
    rm -rf "$TEMP_DIR"
}
trap cleanup EXIT HUP INT TERM

RELEASE_JSON_PATH="$TEMP_DIR/release.json"
echo "[*] Fetching release metadata"
download "$GITHUB_RELEASE_API_URL" "$RELEASE_JSON_PATH"

# Read only the release tag and asset fields, keeping nested objects separate.
RELEASE_METADATA="$(awk -v prefix="Hyprism-linux-x64-" -v suffix=".tar.xz" '
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

ARCHIVE_NAME="Hyprism-linux-x64-${VERSION}.tar.xz"
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
if ! printf '%s\n' "$EXPECTED_SHA" | grep -Eq '^[0-9A-Fa-f]{64}$'; then
    echo "The release asset has no valid SHA-256 digest" >&2
    exit 1
fi

echo "[*] Downloading Hyprism Launcher $VERSION"
download "$ARCHIVE_URL" "$ARCHIVE_PATH"

ACTUAL_SHA="$(sha256sum "$ARCHIVE_PATH" | awk '{ print $1 }')"
if [ "$(printf '%s' "$EXPECTED_SHA" | tr '[:upper:]' '[:lower:]')" != "$ACTUAL_SHA" ]; then
    echo "The downloaded archive failed SHA-256 verification" >&2
    exit 1
fi

DATA_DIR="${XDG_DATA_HOME:-$HOME/.local/share}"
BIN_DIR="$HOME/.local/bin"
INSTALL_DIR="$DATA_DIR/hyprism"
VERSION_DIR="$INSTALL_DIR/$VERSION"
APPLICATIONS_DIR="$DATA_DIR/applications"
ICONS_DIR="$DATA_DIR/icons/hicolor/scalable/apps"
TARGET_PATH="$VERSION_DIR/$APP_EXECUTABLE"
BIN_LINK="$BIN_DIR/hyprism"
ICON_PATH="$ICONS_DIR/$APP_ID.svg"

mkdir -p "$VERSION_DIR" "$BIN_DIR" "$APPLICATIONS_DIR" "$ICONS_DIR"
tar -xJf "$ARCHIVE_PATH" -C "$VERSION_DIR"

if [ ! -f "$TARGET_PATH" ]; then
    echo "The archive does not contain '$APP_EXECUTABLE'" >&2
    exit 1
fi
if [ ! -f "$VERSION_DIR/$LOCAL_NODE_EXECUTABLE" ]; then
    echo "The archive does not contain '$LOCAL_NODE_EXECUTABLE'" >&2
    exit 1
fi
chmod +x "$TARGET_PATH"
chmod +x "$VERSION_DIR/$LOCAL_NODE_EXECUTABLE"

if [ -e "$BIN_LINK" ] && [ ! -L "$BIN_LINK" ]; then
    echo "Cannot replace non-symlink at $BIN_LINK" >&2
    exit 1
fi
TEMP_LINK="$BIN_DIR/.hyprism-link.$$"
rm -f "$TEMP_LINK"
ln -s "$TARGET_PATH" "$TEMP_LINK"
mv -f "$TEMP_LINK" "$BIN_LINK"

download "$GITHUB_ICON_URL" "$TEMP_DIR/$APP_ID.svg"
cp "$TEMP_DIR/$APP_ID.svg" "$ICON_PATH"

DESKTOP_FILE="$APPLICATIONS_DIR/$APP_ID.desktop"
cat > "$DESKTOP_FILE" <<EOF
[Desktop Entry]
Type=Application
Name=Hyprism Launcher
Comment=Native Hytale launcher
Exec="$BIN_LINK"
Icon=$ICON_PATH
Terminal=false
Categories=Game;Utility;
StartupNotify=true
EOF
chmod 644 "$DESKTOP_FILE"

echo "[+] Installed Hyprism Launcher $VERSION"
echo "[+] Launch with: $BIN_LINK"
echo "[+] Created application entry: $DESKTOP_FILE"

case ":${PATH:-}:" in
    *":$BIN_DIR:"*) ;;
    *)
        echo "[!] $BIN_DIR is not in PATH"
        echo "    Add it for future shells with: export PATH=\"$BIN_DIR:\$PATH\""
        ;;
esac
