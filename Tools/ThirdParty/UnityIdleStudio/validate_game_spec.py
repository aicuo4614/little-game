#!/usr/bin/env python3
import argparse
import json
import re
import sys
from pathlib import Path

ALLOWED_MODES = {"2d", "iso2d", "iso3d"}
ALLOWED_MAP_MODES = {"tile_mode", "scene_mode", "side_scroll_mode", "grid_mode", "room_chunk_mode", "baked_scene_mode", "facility_cutaway_mode"}
ALLOWED_VIEW_STYLES = {"2d_side_cutaway", "2d_top_down", "iso2d", "iso3d"}
ALLOWED_ISO3D_TOOLS = {"blender_mcp", "unity_mcp_primitives", "user_supplied_pack"}
ALLOWED_DEPLOY_TARGETS = {"build_only", "usb_device"}

# view_style is what the user picked; mode is the coarser value the art/task
# scripts branch on. They must agree (see references/contracts.md).
VIEW_STYLE_TO_MODE = {
    "2d_side_cutaway": "2d",
    "2d_top_down": "2d",
    "iso2d": "iso2d",
    "iso3d": "iso3d",
}

PACKAGE_SEGMENT_RE = re.compile(r"^[a-z][a-z0-9_]*$")


def get_path(data, dotted):
    cur = data
    for part in dotted.split("."):
        if not isinstance(cur, dict) or part not in cur:
            return None
        cur = cur[part]
    return cur


def validate(data):
    errors = []
    warnings = []
    required = [
        "project.name",
        "project.unity_project_path",
        "presentation.mode",
        "loop.primary_resource",
        "loop.currency",
        "loop.steps",
        "monetization.ads_enabled",
        "vertical_slice",
    ]
    for key in required:
        value = get_path(data, key)
        if value is None or value == "" or value == []:
            errors.append(f"missing required field: {key}")

    mode = get_path(data, "presentation.mode")
    if mode is not None and mode not in ALLOWED_MODES:
        errors.append(f"presentation.mode must be one of {sorted(ALLOWED_MODES)}")

    steps = get_path(data, "loop.steps")
    if steps is not None and not isinstance(steps, list):
        errors.append("loop.steps must be a list")

    ads = get_path(data, "monetization.ads_enabled")
    if ads is not None and not isinstance(ads, bool):
        errors.append("monetization.ads_enabled must be boolean")
    if ads is True and not get_path(data, "monetization.ad_placements"):
        warnings.append("ads enabled but monetization.ad_placements is not set -- pick placements from references/ad-placement-playbook.md")

    if get_path(data, "meta") is None:
        warnings.append("meta section not set -- record long-term intent (prestige/collections) per references/longterm-progression.md, even if disabled")

    if not get_path(data, "presentation.style_manifest"):
        warnings.append("presentation.style_manifest not set -- the style contract (references/style-guide-contract.md) should be locked before production art")

    if mode == "iso3d" and not get_path(data, "presentation.camera"):
        warnings.append("iso3d should document an orthographic/fixed camera policy")

    if ads is True and not get_path(data, "monetization.rewarded"):
        warnings.append("ads enabled but no rewarded placements are declared")

    map_mode = get_path(data, "map.map_mode")
    if map_mode is not None and map_mode not in ALLOWED_MAP_MODES:
        errors.append(f"map.map_mode must be one of {sorted(ALLOWED_MAP_MODES)}")
    if map_mode is None:
        warnings.append("map.map_mode not set; see references/map-pipeline.md before generating foundation art")

    if not get_path(data, "presentation.camera_angle"):
        warnings.append("presentation.camera_angle not set; ask via references/input-form.md before locking art direction")

    view_style = get_path(data, "presentation.view_style")
    if view_style is not None and view_style not in ALLOWED_VIEW_STYLES:
        errors.append(f"presentation.view_style must be one of {sorted(ALLOWED_VIEW_STYLES)}")
    if view_style is None:
        warnings.append("presentation.view_style not set; run the discovery interview in references/discovery-interview.md")

    # mode and view_style must agree -- a spec saying mode=2d but
    # view_style=iso3d would send the art pipeline and the task planner in
    # two different directions.
    if view_style in VIEW_STYLE_TO_MODE and mode is not None and mode in ALLOWED_MODES:
        expected_mode = VIEW_STYLE_TO_MODE[view_style]
        if mode != expected_mode:
            errors.append(
                f"presentation.mode '{mode}' does not match presentation.view_style "
                f"'{view_style}' (expected mode '{expected_mode}' -- see references/contracts.md)"
            )

    # The iso3d tool question is mandatory whether iso3d arrived via
    # view_style or only via mode (older/partial specs may set mode only).
    if view_style == "iso3d" or mode == "iso3d":
        iso3d_tool = get_path(data, "presentation.iso3d_tool")
        if not iso3d_tool:
            errors.append("presentation is iso3d but presentation.iso3d_tool is empty -- ask the mandatory follow-up in references/input-form.md, never default it")
        elif iso3d_tool not in ALLOWED_ISO3D_TOOLS:
            errors.append(f"presentation.iso3d_tool must be one of {sorted(ALLOWED_ISO3D_TOOLS)}")

    if view_style == "2d_side_cutaway":
        map_mode = get_path(data, "map.map_mode")
        if map_mode is not None and map_mode != "facility_cutaway_mode":
            warnings.append("presentation.view_style is 2d_side_cutaway but map.map_mode is not facility_cutaway_mode -- see references/map-pipeline.md")
    elif view_style is not None and get_path(data, "map.map_mode") == "facility_cutaway_mode":
        warnings.append(f"map.map_mode is facility_cutaway_mode but presentation.view_style is '{view_style}' -- facility_cutaway_mode is meant for 2d_side_cutaway (see references/map-pipeline.md)")

    deploy_target = get_path(data, "deploy.target")
    if deploy_target is not None and deploy_target not in ALLOWED_DEPLOY_TARGETS:
        errors.append(f"deploy.target must be one of {sorted(ALLOWED_DEPLOY_TARGETS)}")

    if deploy_target == "usb_device":
        pkg = get_path(data, "deploy.package_name")
        if not pkg:
            errors.append("deploy.target is usb_device but deploy.package_name is empty -- ask the user, never invent it")
        else:
            segments = pkg.split(".")
            bad = (
                pkg != pkg.lower()
                or len(segments) < 2
                or any(not s or not PACKAGE_SEGMENT_RE.match(s) for s in segments)
            )
            if bad:
                errors.append(f"deploy.package_name '{pkg}' is not a valid reverse-DNS package id (run scripts/validate_package_name.py)")

    anim_phase = get_path(data, "animation.phase")
    if anim_phase is not None and anim_phase not in {"static_transform_first", "animated"}:
        errors.append("animation.phase must be 'static_transform_first' or 'animated'")
    requested_clips = get_path(data, "animation.requested_clips") or []
    verified = get_path(data, "animation.verified_on_device")
    if requested_clips and not verified:
        errors.append("animation.requested_clips is non-empty but animation.verified_on_device is false -- run the Visual Verification Gate (references/animation-and-effects-pipeline.md Phase B) before generating animation frames")
    if anim_phase == "animated" and not verified:
        errors.append("animation.phase is 'animated' but animation.verified_on_device is false -- the Phase B gate must pass before the animated phase")

    return errors, warnings


def main():
    parser = argparse.ArgumentParser(description="Validate Unity Idle Studio GameSpec JSON")
    parser.add_argument("spec", type=Path)
    args = parser.parse_args()
    try:
        data = json.loads(args.spec.read_text(encoding="utf-8"))
    except Exception as exc:
        print(f"ERROR: cannot read JSON: {exc}", file=sys.stderr)
        return 2

    errors, warnings = validate(data)
    for warning in warnings:
        print(f"WARNING: {warning}")
    for error in errors:
        print(f"ERROR: {error}")

    if errors:
        return 1
    print("OK: GameSpec is structurally valid")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

