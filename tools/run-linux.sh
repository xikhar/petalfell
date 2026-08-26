#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
binary="$project_dir/build/linux/Petalfell.x86_64"

if [ ! -x "$binary" ]; then
  echo "No Linux build found. Build it first with:" >&2
  echo "  $project_dir/tools/build-linux.sh" >&2
  exit 1
fi

# Portable Godot exports use dlopen for the Linux display, audio, and graphics
# libraries. Regular distributions expose those through the system linker;
# NixOS keeps them isolated in the Nix store. Reuse the closure of the matching
# installed Godot Mono package so the exported binary can find X11, Wayland,
# Vulkan, PulseAudio, and their dependencies without changing the host system.
if [ -e /etc/NIXOS ]; then
  godot_bin="$(command -v godot-mono || command -v godot4-mono || true)"
  if [ -z "$godot_bin" ] || ! command -v nix-store >/dev/null 2>&1; then
    echo "Running this export on NixOS requires godot-mono and nix-store on PATH." >&2
    exit 1
  fi

  godot_executable="$(readlink -f "$godot_bin")"
  godot_store_path="$(dirname "$(dirname "$godot_executable")")"
  runtime_library_path=""

  while IFS= read -r store_path; do
    for library_dir in "$store_path/lib" "$store_path/lib64"; do
      if [ -d "$library_dir" ]; then
        runtime_library_path="${runtime_library_path:+$runtime_library_path:}$library_dir"
      fi
    done
  done < <(nix-store -qR "$godot_store_path")

  if [ -z "$runtime_library_path" ]; then
    echo "Could not resolve the Godot runtime libraries from $godot_store_path." >&2
    exit 1
  fi

  export LD_LIBRARY_PATH="$runtime_library_path${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
fi

exec "$binary" "$@"
