#!/usr/bin/env bash

# Copyright (C) 2026 Hyprism Launcher
# SPDX-License-Identifier: GPL-3.0-only


# This script fetches the latest Linux x64 tarball from the
# Hyprism GitHub releases, computes its sha256 checksum and
# updates the Flathub manifest at
# Properties/linux/flathub/io.github.hyprismteam.Hyprism.module.yml
# with the concrete download URL and corresponding sha256.
#
# Dependencies: curl, jq, sha256sum (coreutils), sed

set -euo pipefail

MANIFEST="$(dirname "$0")/../Properties/linux/flathub/io.github.hyprismteam.Hyprism.yml"

# query GitHub API for the latest release
API="https://api.github.com/repos/hyprismteam/Hyprism/releases/latest"

# also fetch the SHA of the tip of the main branch
# we can use the GitHub API which doesn't require a local repo
MAIN_SHA=$(curl -s "https://api.github.com/repos/hyprismteam/Hyprism/branches/main" \
    | jq -r .commit.sha)
if [[ -z "$MAIN_SHA" || "$MAIN_SHA" == "null" ]]; then
    echo "error: unable to fetch main branch SHA" >&2
    exit 1
fi

# build final manifest by combining header and module fragments
# header comes from flatpak yaml, modules from flathub module file
# FLATPAK="$(dirname "$MANIFEST")/../flatpak/io.github.hyprismteam.Hyprism.yml"
# MODULE_FILE="$(dirname "$MANIFEST")/io.github.hyprismteam.Hyprism.module.yml"

# if [[ ! -f "$FLATPAK" ]]; then
#     echo "error: flatpak header file not found: $FLATPAK" >&2
#     exit 1
# fi
# if [[ ! -f "$MODULE_FILE" ]]; then
#     echo "error: module file not found: $MODULE_FILE" >&2
#     exit 1
# fi

# # capture header (everything before the modules: directive)
# # use sed to stop before printing the modules: line
# header=$(sed '/^modules:/q' "$FLATPAK")
# # remove any trailing modules: if accidentally included
# header=$(printf '%s' "$header" | sed '/^modules:$/d')

# capture modules section from module file
# replace placeholders of commit hashes
sed -i -e "s|HYPRISM_MAIN_BRANCH|$MAIN_SHA|" "$MANIFEST"

# # write combined manifest
# {
#     printf '%s
# ' "$header"
#     printf 'modules:
# '
#     printf '%s
# ' "$modules"
# } >"$MANIFEST"

# echo "Manifest regenerated and updated: $MANIFEST"

echo "Generating nuget sources file for Flathub manifest..."

runtime=$(grep -m1 '^runtime:' "$MANIFEST" | awk '{print $NF}')
# the manifest keeps the runtime version in a separate field; pull that out so we can
# use it when installing the SDK extension locally.  strip surrounding quotes if any.
runtime_version=$(grep -m1 '^runtime-version:' "$MANIFEST" | awk '{print $NF}' | tr -d "'\"")

# the manifest lists the full extension id (e.g. org.freedesktop.Sdk.Extension.dotnet10)
# but the Python generator expects just the numeric version ("10").
raw_dotnet=$(grep -m1 'org.freedesktop.Sdk.Extension.dotnet' "$MANIFEST" | awk '{print $NF}')
# strip prefix to obtain the version number
# shell parameter expansion removes everything up to the last ".dotnet".
dotnet=${raw_dotnet##*.dotnet}

echo "Extracted runtime: $runtime"
echo "Extracted dotnet extension id: $raw_dotnet"
echo "Using dotnet version: $dotnet"

flatpak remote-add --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo

if ! flatpak install -y --user flathub org.freedesktop.Sdk.Extension.dotnet$dotnet//$runtime_version; then
  echo "Warning: extension install failed, retrying with runtime ref format"
  flatpak install -y --user flathub runtime/org.freedesktop.Sdk.Extension.dotnet$dotnet/x86_64/$runtime_version
fi

# When invoking the generator we also need to tell it which freedesktop runtime
# version to use; otherwise it defaults to 24.08 and will restore packages inside
# the wrong runtime (see the earlier error message).  Use the version extracted
# from the manifest.
# NOTE: the Flatpak "runtime" field (e.g. org.freedesktop.Platform) is *not* a
# .NET runtime identifier, so we don't pass it to the generator.  The CLI's
# `--runtime` option is intended for dotnet RIDs such as linux-x64.
# compute absolute path to the project file (script may be run from any cwd)
project_file="$(dirname "${BASH_SOURCE[0]}")/../Sources/Hyprism.Launcher/Hyprism.Launcher.csproj"

echo "Executing: python3 Properties/linux/flathub/flatpak-dotnet-generator.py \
    --only-arches x86_64 \
    --freedesktop \"$runtime_version\" \
    --dotnet \"$dotnet\" \
    Properties/linux/flathub/nuget-sources.json \"$project_file\""

python3 Properties/linux/flathub/flatpak-dotnet-generator.py \
    --only-arches x86_64 \
    --freedesktop "$runtime_version" \
    --dotnet "$dotnet" \
    Properties/linux/flathub/nuget-sources.json "$project_file"

if ! command -v pipx >/dev/null 2>&1; then
  echo "pipx not found; installing via python3 -m pip install --user pipx"
  python3 -m pip install --user pipx
  export PATH="$HOME/.local/bin:$PATH"
fi

pipx install --force git+https://github.com/flatpak/flatpak-builder-tools.git#subdirectory=node

if ! command -v flatpak-node-generator >/dev/null 2>&1; then
  echo "error: flatpak-node-generator not found after installation" >&2
  exit 1
fi

flatpak-node-generator --output "Properties/linux/flathub/generated-sources.json" npm Frontend/package-lock.json

if [ ! -f Properties/linux/flathub/nuget-sources.json ] || [ ! -f Properties/linux/flathub/generated-sources.json ]; then
  echo "error: failed to generate required Flathub source JSON files" >&2
  ls -lah Properties/linux/flathub || true
  exit 1
fi
