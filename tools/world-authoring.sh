#!/usr/bin/env bash
set -euo pipefail

project_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
command_name="${1:-audit}"
output_path="${2:-res://../shots/world-authoring.svg}"

case "$command_name" in
  audit)
    exec godot-mono --headless --path "$project_dir" -- --world-audit
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
	verify-production-terrain)
	atlas_point="${2:?usage: $0 verify-production-terrain <global-x,z>}"
	exec godot-mono --headless --path "$project_dir" -- --world-audit \
	  --verify-production-terrain "$atlas_point"
	;;
	audit-production-terrain)
	exec godot-mono --headless --path "$project_dir" -- --world-audit \
	  --audit-production-terrain
	;;
	verify-production-playability)
		atlas_point="${2:?usage: $0 verify-production-playability <global-x,z> [land|water]}"
	playability_mode="${3:-land}"
	if [[ "$playability_mode" != "land" && "$playability_mode" != "water" ]]; then
		echo "playability mode must be land or water" >&2
		exit 64
	fi
		exec godot-mono --headless --path "$project_dir" -- \
		  --terrain-focus "$atlas_point" --playability-smoke "$playability_mode"
		;;
	review-production-terrain)
		atlas_point="${2:?usage: $0 review-production-terrain <global-x,z>}"
		exec godot-mono --path "$project_dir" -- --terrain-focus "$atlas_point"
		;;
	capture-production-terrain)
		atlas_point="${2:?usage: $0 capture-production-terrain <global-x,z> [output] [shot-names]}"
		atlas_slug="${atlas_point/,/-}"
		terrain_shots="${3:-res://../shots/production-terrain-${atlas_slug}}"
		args=(--terrain-focus "$atlas_point" --shots "$terrain_shots")
		if [[ $# -ge 4 ]]; then args+=(--only "$4"); fi
		exec godot-mono --path "$project_dir" --fullscreen -- "${args[@]}"
		;;
	verify-atlas-walking-handoff)
	exec godot-mono --headless --path "$project_dir" -- --world-audit \
	  --verify-atlas-walking-handoff
	;;
	verify-camera-obstruction)
	exec godot-mono --headless --path "$project_dir" \
	  --script res://tools/camera-obstruction-smoke.gd
	;;
	verify-atlas-map-transport)
	exec godot-mono --headless --path "$project_dir" \
	  --script res://tools/atlas-map-transport-smoke.gd
	;;
	verify-camera-auto-zoom)
	exec godot-mono --headless --path "$project_dir" \
	  --script res://tools/camera-auto-zoom-smoke.gd
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
echo "usage: $0 audit | atlas-preview [output] | atlas-topology-preview [output] | atlas-map-preview [output] | preview-atlas-domain <domain-id> [output] | preview-site-plan <site-id> [output] [--runtime-facing] | reference-top-grid [output] [source-pixel-x,y ...] | reference-plan-overlay [output] | verify-production-terrain <global-x,z> | audit-production-terrain | verify-production-playability <global-x,z> [land|water] | review-production-terrain <global-x,z> | capture-production-terrain <global-x,z> [output] [shot-names] | verify-atlas-walking-handoff | verify-camera-obstruction | verify-atlas-map-transport | verify-camera-auto-zoom | review-site <site-id> | capture-site <site-id> [output] [shot-names]" >&2
    exit 64
    ;;
esac
