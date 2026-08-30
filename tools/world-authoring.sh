#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
command_name="${1:-preview}"
output_path="${2:-res://../shots/world-authoring.svg}"

case "$command_name" in
  audit)
    exec godot-mono --headless --path "$project_dir" -- --world-audit
    ;;
  preview)
    exec godot-mono --headless --path "$project_dir" -- --world-audit --world-preview "$output_path"
    ;;
  preview-domain)
    domain_id="${2:?usage: $0 preview-domain <domain-id> [preview-output]}"
    domain_output="${3:-res://../shots/world-authoring-${domain_id}.svg}"
    exec godot-mono --headless --path "$project_dir" -- --world-audit --world-domain "$domain_id" --world-preview "$domain_output"
    ;;
  atlas-preview)
	atlas_output="${2:-res://../shots/world-atlas.svg}"
	exec godot-mono --headless --path "$project_dir" -- --world-audit --atlas-preview "$atlas_output"
	;;
	atlas-topology-preview)
	atlas_topology_output="${2:-res://../shots/world-atlas-topology.svg}"
	exec godot-mono --headless --path "$project_dir" -- --world-audit --atlas-topology-preview "$atlas_topology_output"
	;;
	atlas-map-preview)
	atlas_map_output="${2:-res://../shots/world-atlas-map.png}"
	exec godot-mono --headless --path "$project_dir" -- --world-audit --atlas-map-preview "$atlas_map_output"
	;;
	preview-atlas-domain)
	domain_id="${2:?usage: $0 preview-atlas-domain <domain-id> [preview-output]}"
	domain_output="${3:-res://../shots/world-atlas-${domain_id}.svg}"
	exec godot-mono --headless --path "$project_dir" -- --world-audit --atlas-domain "$domain_id" \
	  --atlas-topology-preview "$domain_output"
	;;
	preview-site-plan)
		site_id="${2:?usage: $0 preview-site-plan <site-id> [svg-output] [--runtime-facing]}"
		site_plan_output="${3:-$project_dir/../shots/site-plan-${site_id}.svg}"
		args=("$site_id" "$site_plan_output")
		if [[ "${4:-}" == "--runtime-facing" ]]; then args+=(--runtime-facing); fi
		exec python3 "$project_dir/tools/reference-site-plan.py" "${args[@]}"
		;;
	reference-top-grid)
		reference_grid_output="${2:-$project_dir/../shots/reference-10-top-grid.svg}"
		args=("$reference_grid_output")
		if [[ $# -ge 3 ]]; then
			for sample in "${@:3}"; do args+=(--sample "$sample"); done
		fi
		exec python3 "$project_dir/tools/reference-top-grid.py" "${args[@]}"
		;;
	reference-plan-overlay)
		reference_plan_output="${2:-$project_dir/../shots/reference-10-plan-overlay.svg}"
		exec python3 "$project_dir/tools/reference-plan-overlay.py" "$reference_plan_output"
		;;
	compile-sector)
	sector_address="${2:?usage: $0 compile-sector <x,z> [artifact-output] [preview-output]}"
	sector_output="${3:-res://content/chapter_01/derived/sector-${sector_address/,/-}.pfs}"
	sector_preview="${4:-res://../shots/atlas-sector-${sector_address/,/-}.png}"
	exec godot-mono --headless --path "$project_dir" -- --world-audit --compile-sector "$sector_address" \
	  --sector-output "$sector_output" --sector-preview "$sector_preview"
	;;
	verify-sector)
	sector_address="${2:?usage: $0 verify-sector <x,z>}"
	exec godot-mono --headless --path "$project_dir" -- --world-audit --verify-sector "$sector_address"
	;;
	verify-wilderness)
	sector_address="${2:?usage: $0 verify-wilderness <x,z>}"
	exec godot-mono --headless --path "$project_dir" -- --world-audit --verify-wilderness "$sector_address"
	;;
	verify-atlas-handoff)
	atlas_point="${2:?usage: $0 verify-atlas-handoff <global-x,z> [derived-directory]}"
	atlas_output="${3:-res://content/chapter_01/derived}"
	exec godot-mono --headless --path "$project_dir" -- --world-audit \
	  --verify-atlas-handoff "$atlas_point" --atlas-output "$atlas_output"
	;;
	verify-atlas-walking-handoff)
	exec godot-mono --headless --path "$project_dir" -- --world-audit \
	  --verify-atlas-walking-handoff
	;;
	compile-atlas)
	atlas_output="${2:-res://content/chapter_01/derived}"
	exec godot-mono --headless --path "$project_dir" -- --world-audit --compile-atlas \
	  --atlas-output "$atlas_output"
	;;
	verify-atlas)
	atlas_output="${2:-res://content/chapter_01/derived}"
	exec godot-mono --headless --path "$project_dir" -- --world-audit --verify-atlas \
	  --atlas-output "$atlas_output"
	;;
	audit-atlas-hydrology)
	atlas_output="${2:-res://content/chapter_01/derived}"
	exec godot-mono --headless --path "$project_dir" -- --world-audit \
	  --audit-atlas-hydrology --atlas-output "$atlas_output"
	;;
	sample-atlas)
	atlas_point="${2:?usage: $0 sample-atlas <global-x,z>}"
	exec godot-mono --headless --path "$project_dir" -- --world-audit --sample-atlas "$atlas_point"
	;;
	review-sector)
	sector_address="${2:?usage: $0 review-sector <x,z> [global-focus-x,z]}"
	shift 2
	args=(--review-sector "$sector_address")
	if [[ $# -gt 0 ]]; then args+=(--review-focus "$1"); fi
	exec godot-mono --path "$project_dir" -- "${args[@]}"
	;;
	capture-sector)
	sector_address="${2:?usage: $0 capture-sector <x,z> [output] [global-focus-x,z]}"
	sector_slug="${sector_address/,/-}"
	sector_shots="${3:-res://../shots/atlas-runtime-${sector_slug}}"
	args=(--review-sector "$sector_address" --shots "$sector_shots")
	if [[ $# -ge 4 ]]; then args+=(--review-focus "$4"); fi
		exec godot-mono --path "$project_dir" --fullscreen -- "${args[@]}"
		;;
	review-domain)
		domain_id="${2:?usage: $0 review-domain <domain-id> [global-focus-x,z]}"
		shift 2
		args=(--review-domain "$domain_id")
		if [[ $# -gt 0 ]]; then args+=(--review-focus "$1"); fi
		exec godot-mono --path "$project_dir" -- "${args[@]}"
		;;
	capture-domain)
		domain_id="${2:?usage: $0 capture-domain <domain-id> [output] [global-focus-x,z]}"
		domain_shots="${3:-res://../shots/atlas-domain-${domain_id}}"
		args=(--review-domain "$domain_id" --shots "$domain_shots")
		if [[ $# -ge 4 ]]; then args+=(--review-focus "$4"); fi
		exec godot-mono --path "$project_dir" --fullscreen -- "${args[@]}"
		;;
	review-site)
		site_id="${2:?usage: $0 review-site <site-id>}"
		exec godot-mono --path "$project_dir" -- --review-site "$site_id"
		;;
	capture-site)
		site_id="${2:?usage: $0 capture-site <site-id> [output] [shot-names]}"
		site_shots="${3:-res://../shots/atlas-site-${site_id}}"
		args=(--review-site "$site_id" --shots "$site_shots")
		if [[ $# -ge 4 ]]; then args+=(--only "$4"); fi
		exec godot-mono --path "$project_dir" --fullscreen -- "${args[@]}"
		;;
  *)
echo "usage: $0 audit | preview [output] | preview-domain <domain-id> [output] | atlas-preview [output] | atlas-topology-preview [output] | atlas-map-preview [output] | preview-atlas-domain <domain-id> [output] | preview-site-plan <site-id> [output] [--runtime-facing] | reference-top-grid [output] [source-pixel-x,y ...] | reference-plan-overlay [output] | sample-atlas <global-x,z> | compile-sector <x,z> [artifact] [preview] | verify-sector <x,z> | verify-wilderness <x,z> | verify-atlas-handoff <global-x,z> [derived-directory] | verify-atlas-walking-handoff | compile-atlas [output-directory] | verify-atlas [output-directory] | audit-atlas-hydrology [output-directory] | review-sector <x,z> [global-focus] | capture-sector <x,z> [output] [global-focus] | review-domain <domain-id> [global-focus] | capture-domain <domain-id> [output] [global-focus] | review-site <site-id> | capture-site <site-id> [output] [shot-names]" >&2
    exit 64
    ;;
esac
