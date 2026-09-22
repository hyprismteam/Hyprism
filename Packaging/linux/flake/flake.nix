# Copyright (C) 2026 Hyprism Launcher
# SPDX-License-Identifier: GPL-3.0-only

{
  description = "Hyprism, a native Hytale launcher";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs =
    { self, nixpkgs }:
    let
      systems = [ "x86_64-linux" ];
      forAllSystems = nixpkgs.lib.genAttrs systems;
      versionSource = builtins.replaceStrings [ "\n" "\r" " " "\t" ] [ "" "" "" "" ] (
        builtins.readFile (../../.. + "/Sources/Hyprism.Desktop/Hyprism.Desktop.csproj")
      );
      versionMatch = builtins.match ".*<Version>([0-9A-Za-z.+-]+)</Version>.*" versionSource;
      version =
        if versionMatch == null then
          throw "Unable to read the HyPrism version from Hyprism.Desktop.csproj"
        else
          builtins.elemAt versionMatch 0;
    in
    {
      packages = forAllSystems (
        system:
        let
          pkgs = import nixpkgs { inherit system; };
          inherit (pkgs) lib;
          runtimeLibraries = with pkgs; [
            expat
            fontconfig
            freetype
            glib
            icu
            libglvnd
            libice
            libsm
            libx11
            libxcomposite
            libxcursor
            libxext
            libxi
            libxkbcommon
            libxrandr
            libxtst
            openssl
            wayland
            zlib
          ];
          hyprism = pkgs.buildDotnetModule {
            pname = "hyprism";
            inherit version;
            src = ../../..;

            projectFile = "Sources/Hyprism.Desktop/Hyprism.Desktop.csproj";
            nugetDeps = ./nix/deps.json;
            runtimeId = "linux-x64";
            dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
            dotnet-runtime = pkgs.dotnetCorePackages.runtime_10_0;
            dotnetFlags = [ "-p:LauncherAppHostName=Hyprism" ];
            executables = [ "Hyprism" "Hyprism.LocalNode" ];

            nativeBuildInputs = [ pkgs.autoPatchelfHook ];
            buildInputs = runtimeLibraries;
            runtimeDeps = runtimeLibraries;

            postInstall = ''
              install -d "$out/bin"
              install -Dm644 Packaging/linux/io.github.hyprismteam.HyPrism.desktop \
                "$out/share/applications/io.github.hyprismteam.HyPrism.desktop"
              install -Dm644 Sources/Hyprism.Desktop/Assets/Images/logo.svg \
                "$out/share/icons/hicolor/scalable/apps/io.github.hyprismteam.HyPrism.svg"
              # Keep the source viewBox and make the packaged icon viewport square
              sed -E -i '0,/<svg[[:space:]]/{s/(<svg[^>]*width=")([^"]+)("[^>]*height=")[^"]+/\1\2\3\2/}' \
                "$out/share/icons/hicolor/scalable/apps/io.github.hyprismteam.HyPrism.svg"
            '';

            postFixup = ''
              mv "$out/bin/Hyprism" "$out/bin/Hyprism Launcher"
              ln -s "Hyprism Launcher" "$out/bin/hyprism"
            '';

            meta = {
              homepage = "https://github.com/hyprismteam/HyPrism";
              description = "Native Avalonia launcher for Hytale";
              license = lib.licenses.gpl3Only;
              mainProgram = "hyprism";
              platforms = [ "x86_64-linux" ];
            };
          };
        in
        {
          default = hyprism;
          inherit hyprism;
        }
      );

      apps = forAllSystems (
        system:
        let
          package = self.packages.${system}.hyprism;
        in
        {
          default = {
            type = "app";
            program = "${package}/bin/hyprism";
          };
          hyprism = {
            type = "app";
            program = "${package}/bin/hyprism";
          };
        }
      );

      checks = forAllSystems (system: {
        hyprism = self.packages.${system}.hyprism;
      });

      formatter = forAllSystems (system: nixpkgs.legacyPackages.${system}.nixfmt-rfc-style);
    };
}
