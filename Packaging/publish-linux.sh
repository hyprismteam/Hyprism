#!/usr/bin/env bash

# Copyright (C) 2026 Hyprism Launcher
# SPDX-License-Identifier: GPL-3.0-only

# Publishes Linux packages for the Hyprism Launcher and its dedicated Local Node
# process. Native package targets share one self-contained publish output. The
# Nix target delegates the build to the repository flake
set -euo pipefail

PACKAGING_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$PACKAGING_DIR/.." && pwd)"
PROJECT_FILE="$PROJECT_ROOT/Sources/Hyprism.Desktop/Hyprism.Desktop.csproj"
ASSETS_DIR="$PACKAGING_DIR/linux"
FLAKE_DIR="$ASSETS_DIR/flake"
APP_ID="io.github.hyprismteam.HyPrism"
APP_NAME="Hyprism"
APP_EXECUTABLE="Hyprism Launcher"
ICON_ASSET="$PROJECT_ROOT/Sources/Hyprism.Desktop/Assets/Images/logo.svg"
RUNTIME="linux-x64"
FLATPAK_BRANCH="stable"
TARGETS=()
OUTPUT_DIR="$PROJECT_ROOT/dist"
APPIMAGETOOL_BIN="${APPIMAGETOOL:-}"

usage() {
    cat <<'EOF'
Usage: ./Packaging/publish-linux.sh <target> [<target>...] [options]

Targets:
  all       Build every native Linux package supported by this host
  deb       Build a Debian package
  rpm       Build an RPM package
  appimage  Build an AppImage
  flatpak   Build a Flatpak bundle
  tar       Build a tar.xz archive
  nix       Build the Nix flake package

Options:
  --output <directory>      Artifact directory, defaults to dist
  --appimagetool <path>     appimagetool executable or AppImage
  --help                    Show this help

Examples:
  ./Packaging/publish-linux.sh all
  ./Packaging/publish-linux.sh deb rpm tar --output ./dist
  ./Packaging/publish-linux.sh nix
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --output)
            OUTPUT_DIR="${2:?--output requires a directory}"
            shift 2
            ;;
        --appimagetool)
            APPIMAGETOOL_BIN="${2:?--appimagetool requires a path}"
            shift 2
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        all|deb|rpm|appimage|flatpak|tar|nix)
            TARGETS+=("$1")
            shift
            ;;
        *)
            echo "Unknown publish target: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

if [[ ${#TARGETS[@]} -eq 0 ]]; then
    TARGETS=(all)
fi

if [[ "$(uname -s)" != "Linux" ]]; then
    echo "Linux packages must be published on a Linux host" >&2
    exit 1
fi

for target in "${TARGETS[@]}"; do
    if [[ "$target" == all ]]; then
        TARGETS=(deb rpm appimage flatpak tar)
        break
    fi
done

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        echo "Required command is unavailable: $1" >&2
        exit 1
    fi
}

install_icon_asset() {
    local destination="$1"

    # Flatpak requires square icon dimensions. Keep the original viewBox so the artwork is preserved
    install -Dm644 "$ICON_ASSET" "$destination"
    sed -E -i '0,/<svg[[:space:]]/{s/(<svg[^>]*width=")([^"]+)("[^>]*height=")[^"]+/\1\2\3\2/}' \
        "$destination"
}

contains_target() {
    local expected="$1"
    local target
    for target in "${TARGETS[@]}"; do
        [[ "$target" == "$expected" ]] && return 0
    done
    return 1
}

package_versions() {
    local normalized="${VERSION#v}"
    normalized="${normalized//[^0-9A-Za-z.+~:-]/-}"
    if [[ ! "$normalized" =~ ^[0-9] ]]; then
        DEB_VERSION="0.0.0+${normalized//-/.}"
        RPM_VERSION="0.0.0"
        RPM_RELEASE="${normalized//-/.}"
    else
        DEB_VERSION="${normalized/-/~}"
        RPM_VERSION="${normalized%%[-+~]*}"
        RPM_RELEASE="${normalized#"$RPM_VERSION"}"
        RPM_RELEASE="${RPM_RELEASE#[-+~]}"
    fi

    [[ -n "$RPM_RELEASE" ]] || RPM_RELEASE="1"
}

contains_native_target=false
for target in "${TARGETS[@]}"; do
    case "$target" in
        deb|rpm|appimage|flatpak|tar)
            contains_native_target=true
            break
            ;;
    esac
done

if contains_target nix; then
    require_command nix
fi

if [[ "$contains_native_target" == true ]]; then
    require_command dotnet
    require_command tar
    VERSION="$(dotnet msbuild "$PROJECT_FILE" -nologo -getProperty:Version | tail -n 1 | tr -d '\r')"
    if [[ -z "$VERSION" ]]; then
        echo "Hyprism.Desktop.csproj does not define a Version property" >&2
        exit 1
    fi
    package_versions
fi

if contains_target deb; then
    require_command dpkg-deb
fi
if contains_target rpm; then
    require_command rpmbuild
fi
if contains_target flatpak; then
    require_command flatpak
    require_command flatpak-builder
fi
if contains_target appimage; then
    if [[ -z "$APPIMAGETOOL_BIN" ]]; then
        APPIMAGETOOL_BIN="$(command -v appimagetool || true)"
    fi
    if [[ -z "$APPIMAGETOOL_BIN" || ! -x "$APPIMAGETOOL_BIN" ]]; then
        echo "AppImage publishing requires appimagetool. Set APPIMAGETOOL or pass --appimagetool" >&2
        exit 1
    fi
fi

BUILD_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/hyprism-publish.XXXXXX")"
PUBLISH_DIR="$BUILD_ROOT/publish"
trap 'rm -rf "$BUILD_ROOT"' EXIT
if [[ "$contains_native_target" == true ]]; then
    mkdir -p "$OUTPUT_DIR"
fi

if [[ "$contains_native_target" == true ]]; then
    echo "Publishing $APP_NAME $VERSION for $RUNTIME"
    dotnet publish "$PROJECT_FILE" \
        --configuration Release \
        --runtime "$RUNTIME" \
        --self-contained true \
        -p:PublishReadyToRun=true \
        --output "$PUBLISH_DIR"

    test -x "$PUBLISH_DIR/$APP_EXECUTABLE"
    test -x "$PUBLISH_DIR/Hyprism.LocalNode"
fi

install_desktop_assets() {
    local root="$1"
    install -Dm644 "$ASSETS_DIR/$APP_ID.desktop" \
        "$root/usr/share/applications/$APP_ID.desktop"
    install_icon_asset \
        "$root/usr/share/icons/hicolor/scalable/apps/$APP_ID.svg"
}

create_system_payload() {
    local root="$1"
    install -d "$root/opt/hyprism" "$root/usr/bin"
    cp -a "$PUBLISH_DIR/." "$root/opt/hyprism/"
    ln -s "/opt/hyprism/$APP_EXECUTABLE" "$root/usr/bin/hyprism"
    install_desktop_assets "$root"
}

build_tar() {
    tar -C "$PUBLISH_DIR" -cJf "$OUTPUT_DIR/Hyprism-linux-x64-$VERSION.tar.xz" .
}

build_nix() {
    local store_path
    store_path="$(cd "$FLAKE_DIR" && nix build .#hyprism --no-link --print-out-paths --print-build-logs)"
    echo "Built Nix package at $store_path"
}

build_deb() {
    local root="$BUILD_ROOT/deb"
    create_system_payload "$root"
    install -d "$root/DEBIAN"
    cat >"$root/DEBIAN/control" <<EOF
Package: hyprism
Version: $DEB_VERSION
Section: games
Priority: optional
Architecture: amd64
Maintainer: Hyprism Team
Description: Native Avalonia launcher for Hytale
EOF
    dpkg-deb --root-owner-group --build "$root" "$OUTPUT_DIR/Hyprism-linux-x64-$VERSION.deb"
}

build_rpm() {
    local topdir="$BUILD_ROOT/rpm"
    local source_root="$topdir/SOURCES/hyprism-$RPM_VERSION"
    install -d "$topdir/BUILD" "$topdir/RPMS" "$topdir/SOURCES" "$topdir/SPECS" "$topdir/SRPMS"
    create_system_payload "$source_root"
    tar -C "$topdir/SOURCES" -czf "$topdir/SOURCES/hyprism-$RPM_VERSION.tar.gz" "hyprism-$RPM_VERSION"
    cat >"$topdir/SPECS/hyprism.spec" <<EOF
Name: hyprism
Version: $RPM_VERSION
Release: $RPM_RELEASE%{?dist}
Summary: Native Avalonia launcher for Hytale
License: GPL-3.0-only
BuildArch: x86_64
Source0: %{name}-%{version}.tar.gz

%description
Native Avalonia launcher for Hytale

%prep
%setup -q

%install
mkdir -p %{buildroot}
cp -a . %{buildroot}/

%files
/opt/hyprism
/usr/bin/hyprism
/usr/share/applications/$APP_ID.desktop
/usr/share/icons/hicolor/scalable/apps/$APP_ID.svg
EOF
    rpmbuild --define "_topdir $topdir" -bb "$topdir/SPECS/hyprism.spec"
    cp "$topdir/RPMS/x86_64/"*.rpm "$OUTPUT_DIR/Hyprism-linux-x64-$VERSION.rpm"
}

build_appimage() {
    local app_dir="$BUILD_ROOT/Hyprism.AppDir"
    install -d "$app_dir/usr/lib/hyprism" "$app_dir/usr/share/applications" "$app_dir/usr/share/icons/hicolor/scalable/apps"
    cp -a "$PUBLISH_DIR/." "$app_dir/usr/lib/hyprism/"
    install -Dm644 "$ASSETS_DIR/$APP_ID.desktop" "$app_dir/$APP_ID.desktop"
    install_icon_asset "$app_dir/$APP_ID.svg"
    cat >"$app_dir/AppRun" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
    exec "$(dirname "$0")/usr/lib/hyprism/Hyprism Launcher" "$@"
EOF
    chmod +x "$app_dir/AppRun"
    ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN="${APPIMAGE_EXTRACT_AND_RUN:-1}" \
        "$APPIMAGETOOL_BIN" "$app_dir" "$OUTPUT_DIR/Hyprism-linux-x64-$VERSION.AppImage"
}

build_flatpak() {
    local root="$BUILD_ROOT/flatpak"
    local manifest="$ASSETS_DIR/flatpak/$APP_ID.yml"
    install -d "$root/repo" "$root/source"
    cp -a "$PUBLISH_DIR" "$root/source/publish"
    cp "$ASSETS_DIR/$APP_ID.desktop" "$root/source/$APP_ID.desktop"
    install_icon_asset "$root/source/$APP_ID.svg"
    cp "$manifest" "$root/source/manifest.yml"
    (
        cd "$root/source"
        flatpak-builder --force-clean \
            --default-branch="$FLATPAK_BRANCH" \
            --repo="$root/repo" \
            build manifest.yml
    )
    flatpak build-bundle "$root/repo" \
        "$OUTPUT_DIR/Hyprism-linux-x64-$VERSION.flatpak" \
        "$APP_ID" "$FLATPAK_BRANCH"
}

for target in "${TARGETS[@]}"; do
    echo "Building $target package"
    case "$target" in
        deb) build_deb ;;
        rpm) build_rpm ;;
        appimage) build_appimage ;;
        flatpak) build_flatpak ;;
        tar) build_tar ;;
        nix) build_nix ;;
    esac
done

if [[ "$contains_native_target" == true ]]; then
    echo "Published artifacts to $OUTPUT_DIR"
else
    echo "Built the Nix package through the repository flake"
fi
