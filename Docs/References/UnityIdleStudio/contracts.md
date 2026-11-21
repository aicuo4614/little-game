# Contracts

## GameSpec

Use the JSON template in `assets/game-spec.template.json`.

Required architectural fields:

- `project.name`
- `project.unity_project_path`
- `presentation.mode`
- `loop.primary_resource`
- `loop.currency`
- `loop.steps`
- `monetization.ads_enabled`
- `vertical_slice`

Allowed presentation modes:

- `2d`
- `iso2d`
- `iso3d`

Additional fields introduced for form intake, map pipeline, identity, and deploy (see `references/input-form.md`, `references/discovery-interview.md`, `references/map-pipeline.md`, `references/app-identity.md`, `references/unity-mcp-deploy.md`):

- `presentation.view_style` — the concrete style the user picked: `2d_side_cutaway`, `2d_top_down`, `iso2d`, `iso3d`. Maps down to the coarser `presentation.mode` used by the art/task-selection scripts:
  - `2d_side_cutaway` -> `mode: "2d"`, `map.map_mode: "facility_cutaway_mode"`
  - `2d_top_down` -> `mode: "2d"`, `map.map_mode` typically `scene_mode` or `grid_mode`
  - `iso2d` -> `mode: "iso2d"`
  - `iso3d` -> `mode: "iso3d"`, requires `presentation.iso3d_tool`
- `presentation.iso3d_tool` — required when `view_style == iso3d`: `blender_mcp`, `unity_mcp_primitives`, or `user_supplied_pack`. Never defaulted; always asked explicitly (`references/input-form.md`).
- `presentation.camera_angle` — one of the options in `references/input-form.md` (`side_fixed`, `top_down`, `isometric_45`, `fixed_3quarter`, or a documented custom value)
- `map.map_mode` — one of `tile_mode`, `scene_mode`, `side_scroll_mode`, `grid_mode`, `room_chunk_mode`, `baked_scene_mode`, `facility_cutaway_mode`
- `map.grid_manifest`, `map.placement_manifest` — paths to the metadata files from `references/map-pipeline.md`
- `identity.app_name`, `identity.icon_style`, `identity.icon_manifest`
- `deploy.target` — `build_only` or `usb_device`
- `deploy.package_name` — required and validated (`scripts/validate_package_name.py`) when `deploy.target == usb_device`
- `pipeline.stage` — current stage for `references/single-control-ui.md`, one of `STAGE_PLAN`, `STAGE_ART`, `STAGE_MAP`, `STAGE_CORE_BUILD`, `STAGE_ICON`, `STAGE_DEVICE_DEPLOY`, `STAGE_VISUAL_VERIFY`, `STAGE_ANIMATE`, `STAGE_EFFECTS`, `STAGE_QC`, `STAGE_DONE` (each task card from `scripts/build_agent_plan.py` carries a matching `stage` field)
- `animation.phase` — `static_transform_first` (default; single static asset + transform-only movement) or `animated` (frame animation wired in). Never jump straight to `animated`.
- `animation.verified_on_device` — must be `true` (set only after the Visual Verification Gate passes, `references/animation-and-effects-pipeline.md` Phase B) before `animation.requested_clips` may be non-empty.
- `animation.requested_clips` — animation clips approved to generate, each produced via the Animation Request Contract in `references/animation-and-effects-pipeline.md` Phase C.
- `effects.requested_moments` — effect moments the user explicitly selected (`references/animation-and-effects-pipeline.md` Phase D). Empty is a valid, complete answer; never populated without an explicit user answer.
- `effects.style_reference` — optional free-text style note the user gave for effects.
- `presentation.style_manifest` — path to the locked style manifest (`references/style-guide-contract.md`, template `assets/style-manifest.template.json`). Locked by STYLE-001 before any production art.
- `monetization.ad_placements` — path to the ad placement config (`references/ad-placement-playbook.md`, template `assets/ad-placements.template.json`). Every rewarded placement in the game exists as an entry here.
- `meta.prestige_enabled`, `meta.prestige_currency`, `meta.collections_enabled`, `meta.collection_theme_line`, `meta.daily_loop`, `meta.events_cadence` — long-term retention intent (`references/longterm-progression.md`). Set consciously during the interview follow-up, not by omission; the save schema plans for enabled layers even though they ship post-slice.

Run `scripts/validate_game_spec.py` after every intake update; it enforces the `usb_device` + `package_name` pairing, the `iso3d` + `iso3d_tool` pairing, and warns on missing `map.map_mode`/`camera_angle`/`view_style`.

## GameBrief.md

Before `GameSpec.json` exists, `docs/GAME-BRIEF.md` is the source of truth for what has and hasn't been decided (see `references/discovery-interview.md`). It is written in US English regardless of interview language (see `references/language-policy.md`) and every field is tagged `[confirmed]` or `[open]`. Do not generate `GameSpec.json` while required fields are still `[open]`.

## Task Card

Every task node uses the shape in `assets/agent-task.template.json`.

Required fields:

```json
{
  "id": "CORE-001",
  "agent": "Unity Architecture AI",
  "goal": "...",
  "stage": "STAGE_CORE_BUILD",
  "inputs": [],
  "outputs": [],
  "dependencies": [],
  "owned_paths": [],
  "acceptance": [],
  "evidence": [],
  "status": "ready"
}
```

## Asset Manifest

Use one stable entry per runtime asset:

```json
{
  "asset_id": "env.checkout_counter.001",
  "kind": "prop",
  "presentation_mode": "iso3d",
  "source": "Assets/Art/Blender/checkout_counter.blend",
  "runtime": "Assets/Game/Prefabs/Art/CheckoutCounter.prefab",
  "temporary": false,
  "pivot": "ground_center",
  "footprint": [2.0, 1.0],
  "sockets": ["customer_anchor", "worker_anchor"],
  "notes": ""
}
```

Never derive identity from filename alone. `asset_id` is the stable contract.

## Placeholder Manifest

For `iso3d`, blockout placeholders must record:

```json
{
  "asset_id": "env.checkout_counter.001",
  "placeholder_path": "Assets/Game/Prefabs/Blockout/CheckoutCounter_PLACEHOLDER.prefab",
  "shape_class": "box",
  "dimensions_m": [2.0, 1.0, 1.1],
  "origin": "ground_center",
  "required_sockets": ["customer_anchor", "worker_anchor"],
  "collider_intent": "box_static",
  "replacement_status": "pending"
}
```

The Blender Agent must model to this contract unless the Architecture AI approves a contract change.

## Semantic Action IDs

All interactive controls use stable action IDs independent from localized text.

Examples:

- `upgrade_worker_speed`
- `upgrade_storage_capacity`
- `unlock_area_02`
- `claim_offline`
- `claim_offline_rewarded`
- `activate_income_boost_rewarded`

Use these IDs for analytics and routing.

## Handoff

Every completed task handoff includes:

```text
Task ID:
Owner:
Artifacts:
Manifest changes:
Evidence:
Known limitations:
Next owner:
```

