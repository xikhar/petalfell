#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
output_dir="$project_dir/build/linux"
output_file="$output_dir/Petalfell.x86_64"

godot_bin="$(command -v godot-mono || command -v godot4-mono || true)"
if [ -z "$godot_bin" ]; then
  echo "Godot Mono was not found on PATH (expected godot-mono or godot4-mono)." >&2
  exit 1
fi

engine_version="$($godot_bin --version)"
if [[ "$engine_version" != *.mono* ]]; then
  echo "Petalfell requires a Godot Mono build; found: $engine_version" >&2
  exit 1
fi

template_version="${engine_version%%.mono*}.mono"
godot_data_dir="${XDG_DATA_HOME:-$HOME/.local/share}/godot"
template_root="$godot_data_dir/export_templates"
template_dir="$template_root/$template_version"

if [ ! -f "$template_dir/linux_release.x86_64" ]; then
  bundled_template_dir=""

  # NixOS installs Mono export templates in the Nix store. Link the matching
  # immutable directory into the location Godot expects instead of copying it.
  for candidate in \
    /nix/store/*godot-export-templates-mono-bin-*/share/godot/export_templates/"$template_version"; do
    if [ -f "$candidate/linux_release.x86_64" ]; then
      bundled_template_dir="$candidate"
      break
    fi
  done

  if [ -z "$bundled_template_dir" ]; then
    echo "Mono export templates for Godot $template_version were not found." >&2
    echo "Install the matching Godot Mono export templates, then run this command again." >&2
    exit 1
  fi

  if [ -e "$template_dir" ] || [ -L "$template_dir" ]; then
    echo "The existing template path is incompatible: $template_dir" >&2
    echo "It must contain linux_release.x86_64 for Godot $template_version." >&2
    exit 1
  fi

  mkdir -p "$template_root"
  ln -s "$bundled_template_dir" "$template_dir"
  echo "Linked Godot Mono export templates: $template_dir"
fi

"$project_dir/tools/setup-nuget.sh"
mkdir -p "$output_dir"

export_log="$(mktemp /tmp/petalfell-linux-export.XXXXXX.log)"
trap 'rm -f "$export_log"' EXIT

set +e
"$godot_bin" \
  --headless \
  --path "$project_dir" \
  --export-release "Linux" \
  "$output_file" 2>&1 | tee "$export_log"
export_status="${PIPESTATUS[0]}"
set -e

if [ "$export_status" -ne 0 ] || grep -q '^ERROR:' "$export_log"; then
  echo "Linux export failed; see the Godot errors above." >&2
  exit 1
fi

chmod +x "$output_file"
echo "Linux build ready: $output_file"
echo "Run it with: $project_dir/tools/run-linux.sh"
