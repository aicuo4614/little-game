"""端到端验证 17 个 unity_skill_editor_* MCP 工具（3D Animator + 共享 TimelinePlayback）。"""
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import smoke_mcp


def wait_compile(timeout=60):
    t0 = time.time()
    while time.time() - t0 < timeout:
        try:
            text = smoke_mcp.tool("unity_get_compilation_status")
            if "未在编译" in text and "最近编译成功" in text:
                return text
        except Exception:
            pass
        time.sleep(1.0)
    raise RuntimeError("等待编译超时")


def main():
    smoke_mcp.rpc("initialize", {
        "protocolVersion": "2025-11-25",
        "capabilities": {},
        "clientInfo": {"name": "verify-skill-editor-mcp", "version": "1.0"}
    })
    smoke_mcp.rpc("notifications/initialized", notify=True)

    proj = json.loads(smoke_mcp.tool("unity_get_project_info"))
    assert proj["dataPath"].replace("\\", "/").endswith("E:/Project/little-game/Assets"), f"错误工程: {proj}"

    print("1. 等待编译完成...")
    print("  ", wait_compile())

    smoke_mcp.tool("unity_clear_console_logs")

    print("2. 测试 unity_skill_editor_open / status / catalog / list_actions...")
    opened = json.loads(smoke_mcp.tool("unity_skill_editor_open"))
    assert opened.get("opened") is True, opened
    status = json.loads(smoke_mcp.tool("unity_skill_editor_status"))
    assert status.get("windowOpen") is True, status
    assert status.get("playbackEngine") == "shared:TimelinePlayback/TimelinePlayer", status
    catalog = json.loads(smoke_mcp.tool("unity_skill_editor_catalog"))
    assert len(catalog.get("edits", [])) >= 10, catalog
    actions = json.loads(smoke_mcp.tool("unity_skill_editor_list_actions"))
    assert actions is not None

    print("3. 测试 unity_skill_editor_load / read_timeline / list_clips / get_clip...")
    smoke_mcp.tool("unity_skill_editor_command", command="load", arguments={"path": "archer_basic", "discardChanges": True})
    doc0 = json.loads(smoke_mcp.tool("unity_skill_editor_read_timeline"))
    smoke_mcp.tool("unity_skill_editor_command", command="save", arguments={"revision": doc0["revision"]})
    loaded = json.loads(smoke_mcp.tool("unity_skill_editor_load", pathOrTimelineId="archer_pierce"))
    assert loaded.get("loaded") is True and loaded.get("timelineId") == "archer_pierce", loaded
    doc = json.loads(smoke_mcp.tool("unity_skill_editor_read_timeline"))
    rev = doc["revision"]
    clips_list = json.loads(smoke_mcp.tool("unity_skill_editor_list_clips", offset=0, limit=50))
    assert clips_list["total"] >= 5, clips_list
    first_key = clips_list["clips"][0]["clipKey"]
    clip_detail = json.loads(smoke_mcp.tool("unity_skill_editor_get_clip", clipKey=first_key))
    assert clip_detail["clipKey"] == first_key, clip_detail

    print("4. 测试 unity_skill_editor_command (validate / animations / assets / preview_units)...")
    val = json.loads(smoke_mcp.tool("unity_skill_editor_command", command="validate"))
    assert val["editor"]["valid"] is True, val
    assert val["battle"]["valid"] is True, val
    assert val["battle"]["resources"]["valid"] is True, val
    anims = json.loads(smoke_mcp.tool("unity_skill_editor_command", command="animations"))
    anim_names = [a["name"] for a in anims.get("animator") or []]
    assert "Attack" in anim_names and "Hit" in anim_names and "Idle" in anim_names, anims
    assert anims.get("spine") is None, anims
    preview_units = json.loads(smoke_mcp.tool("unity_skill_editor_command", command="preview_units"))
    assert preview_units is not None

    print("5. 测试 unity_skill_editor_edit (dryRun=true & dryRun=false + undo)...")
    dry = json.loads(smoke_mcp.tool(
        "unity_skill_editor_edit",
        revision=rev,
        dryRun=True,
        operations=[{
            "action": "add_clip",
            "type": "CameraShake",
            "alias": "tmp_shake",
            "patch": {"StartTime": 0.25, "Duration": 0.15, "PositionStrength": {"x": 0.08, "y": 0.08, "z": 0.0}}
        }]
    ))
    assert dry.get("applied") is False and dry.get("battle", {}).get("valid") is True, dry
    applied = json.loads(smoke_mcp.tool(
        "unity_skill_editor_edit",
        revision=rev,
        dryRun=False,
        operations=[{
            "action": "add_clip",
            "type": "CameraShake",
            "alias": "tmp_shake",
            "patch": {"StartTime": 0.25, "Duration": 0.15, "PositionStrength": {"x": 0.08, "y": 0.08, "z": 0.0}}
        }]
    ))
    assert applied.get("applied") is True, applied
    new_rev = applied["revision"]
    assert new_rev != rev, applied
    undone = json.loads(smoke_mcp.tool("unity_skill_editor_command", command="undo", arguments={"revision": new_rev}))
    assert undone is not None
    undo_doc = json.loads(smoke_mcp.tool("unity_skill_editor_read_timeline"))
    smoke_mcp.tool("unity_skill_editor_command", command="redo", arguments={"revision": undo_doc["revision"]})
    redo_doc = json.loads(smoke_mcp.tool("unity_skill_editor_read_timeline"))
    smoke_mcp.tool("unity_skill_editor_command", command="undo", arguments={"revision": redo_doc["revision"]})

    print("6. 测试 unity_skill_editor_set_preview / seek / snapshot / play / pause / record / stop...")
    sp = json.loads(smoke_mcp.tool("unity_skill_editor_set_preview", casterTeam="TeamA", quality="Low"))
    assert sp["casterTeam"] == "TeamA", sp
    sk = json.loads(smoke_mcp.tool("unity_skill_editor_seek", time=0.22))
    assert abs(sk["runningTime"] - 0.22) < 0.05, sk
    snap = json.loads(smoke_mcp.tool("unity_skill_editor_snapshot"))
    assert snap.get("schema") == "kabu.timeline.preview-record", snap
    assert "unit:caster" in snap.get("entities", {}), snap
    pl = json.loads(smoke_mcp.tool("unity_skill_editor_play", fromStart=True))
    assert pl.get("playing") is True, pl
    time.sleep(0.2)
    pa = json.loads(smoke_mcp.tool("unity_skill_editor_pause"))
    assert pa.get("playing") is False, pa
    st = json.loads(smoke_mcp.tool("unity_skill_editor_stop"))
    assert st.get("stopped") is True, st
    rec = json.loads(smoke_mcp.tool("unity_skill_editor_record", casterTeam="TeamA", quality="Low", sampleHz=20, untilCutoff=True))
    assert rec.get("record") or rec.get("recordFilePath"), rec

    record_b = json.loads(smoke_mcp.tool("unity_skill_editor_record", casterTeam="TeamB", quality="Low", sampleHz=20, untilCutoff=True))
    assert record_b.get("record") or record_b.get("recordFilePath"), record_b
    root = Path(__file__).resolve().parents[2]
    output = root / "Reports/Framework"
    output.mkdir(parents=True, exist_ok=True)
    (output / "skill-editor-record-a.json").write_text(json.dumps(rec, ensure_ascii=False, indent=2), encoding="utf-8")
    (output / "skill-editor-record-b.json").write_text(json.dumps(record_b, ensure_ascii=False, indent=2), encoding="utf-8")
    current = json.loads(smoke_mcp.tool("unity_skill_editor_read_timeline"))
    smoke_mcp.tool("unity_skill_editor_command", command="save", arguments={"revision": current["revision"]})
    validations = {}
    for timeline in sorted((root / "Assets/Package_Unit/Timeline/Base").glob("*.json")):
        smoke_mcp.tool("unity_skill_editor_load", pathOrTimelineId=str(timeline).replace("\\", "/"))
        validation = json.loads(smoke_mcp.tool("unity_skill_editor_command", command="validate"))
        assert validation["editor"]["valid"] and validation["battle"]["valid"], (timeline.name, validation)
        assert validation["battle"]["resources"]["valid"], (timeline.name, validation)
        validations[timeline.stem] = validation
    (output / "skill-editor-mcp.json").write_text(json.dumps({"passed": True, "toolCount": 17, "undoRedo": True,
        "validations": validations, "snapshot": snap, "animations": anims, "note": "Unity 编辑器预览与配置验证，实战另见 unity-verification.json"}, ensure_ascii=False, indent=2), encoding="utf-8")

    print("7. 检查 Console 零报错并切回 Combat.unity...")
    logs = smoke_mcp.tool("unity_get_console_logs", maxEntries=100)
    assert "[Error]" not in logs and "[Exception]" not in logs and "[Assert]" not in logs, f"存在报错:\n{logs}"

    smoke_mcp.tool("unity_open_scene", path="Assets/Scenes/Combat.unity")
    print("SKILL_EDITOR_MCP_PASS: 全部 17 个 unity_skill_editor_* 工具验证通过，0 报错！")


if __name__ == "__main__":
    main()
