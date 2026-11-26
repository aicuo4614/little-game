"""通过同一 MCP 采集技能竖屏评审图；不修改或保存技能配置。"""
import json
import sys
import time
from pathlib import Path
from PIL import Image, ImageDraw
import smoke_mcp as mcp

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Reports/Skills/20261126"

def call(name, **args):
    result = mcp.tool(name, **args)
    try:
        return json.loads(result)
    except ValueError:
        raise RuntimeError(result)

def command(command, **args):
    return call("unity_skill_editor_command", command=command, arguments=args)

def connect():
    mcp.rpc("initialize", {"protocolVersion": "2025-11-25", "capabilities": {},
        "clientInfo": {"name": "skill-portrait-review", "version": "1"}})
    mcp.rpc("notifications/initialized", notify=True)
    deadline = time.monotonic() + 60
    while True:
        try:
            project = call("unity_get_project_info")
            if not project["isCompiling"]: break
        except (RuntimeError, ValueError):
            if time.monotonic() >= deadline: raise
        time.sleep(.5)
    assert Path(project["dataPath"]).resolve() == (ROOT / "Assets").resolve(), project

def sheet(records, name, columns=4):
    canvas = Image.new("RGB", (columns * 270, ((len(records) + columns - 1)//columns) * 500), "#222833")
    draw = ImageDraw.Draw(canvas)
    for i, rec in enumerate(records):
        with Image.open(rec["capture"]["path"]) as image:
            canvas.paste(image.resize((270, 480)), ((i % columns)*270, (i//columns)*500 + 20))
        draw.text(((i%columns)*270+4, (i//columns)*500+4), rec["id"] + " @ " + str(round(rec["time"],3)), fill="white")
    canvas.save(OUT / name)

def main():
    connect()
    call("unity_skill_editor_open")
    call("unity_skill_editor_set_preview", casterTeam="TeamA", quality="Low")
    OUT.mkdir(parents=True, exist_ok=True)
    ids = sys.argv[1:] or [p.stem for p in sorted((ROOT/"Assets/Package_Unit/Timeline/Base").glob("*.json"))]
    results = []
    for skill in ids:
        doc = command("load", path=skill, discardChanges=True)
        command("preview_stage", mode="Detail")
        hits = [c for c in doc["clips"] if c["type"] == "PlayHit"]
        timing = hits[0]["contentStart"] if hits else 0.15
        time = timing + 0.055
        call("unity_skill_editor_seek", time=time)
        capture = command("capture", width=540)
        validation = command("validate")
        rec = {"id": skill, "time": time, "capture": capture, "validation": validation, "document": doc}
        results.append(rec)
        print(skill, "particles", sum(p["particleCount"] for p in capture["state"]["particles"]), flush=True)
        (OUT/"review.json").write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
    sheet(results, "review.png")
    call("unity_skill_editor_stop")

if __name__ == "__main__":
    main()
