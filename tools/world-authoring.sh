#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
command_name="${1:-preview}"
output_path="${2:-res://../shots/world-authoring.svg}"

case "$command_name" in
  audit)
    exec godot-mono --path "$project_dir" -- --world-audit
    ;;
  preview)
    exec godot-mono --path "$project_dir" -- --world-audit --world-preview "$output_path"
    ;;
  preview-domain)
    domain_id="${2:?usage: $0 preview-domain <domain-id> [preview-output]}"
    domain_output="${3:-res://../shots/world-authoring-${domain_id}.svg}"
    exec godot-mono --path "$project_dir" -- --world-audit --world-domain "$domain_id" --world-preview "$domain_output"
    ;;
  atlas-preview)
	atlas_output="${2:-res://../shots/world-atlas.svg}"
	exec godot-mono --path "$project_dir" -- --world-audit --atlas-preview "$atlas_output"
	;;
	compile-sector)
	sector_address="${2:?usage: $0 compile-sector <x,z> [artifact-output] [preview-output]}"
	sector_output="${3:-res://content/chapter_01/derived/sector-${sector_address/,/-}.pfs}"
	sector_preview="${4:-res://../shots/atlas-sector-${sector_address/,/-}.png}"
	exec godot-mono --path "$project_dir" -- --world-audit --compile-sector "$sector_address" \
	  --sector-output "$sector_output" --sector-preview "$sector_preview"
	;;
	verify-sector)
	sector_address="${2:?usage: $0 verify-sector <x,z>}"
	exec godot-mono --path "$project_dir" -- --world-audit --verify-sector "$sector_address"
	;;
  *)
	echo "usage: $0 audit | preview [output] | preview-domain <domain-id> [output] | atlas-preview [output] | compile-sector <x,z> [artifact] [preview] | verify-sector <x,z>" >&2
    exit 64
    ;;
esac
