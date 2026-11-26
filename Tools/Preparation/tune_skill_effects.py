"""逐技能通过 revision/dryRun/apply/save 调校；保存修改前后文档供复核。"""
import json
import time
from pathlib import Path
from capture_skill_review import ROOT, OUT, connect, call, command

def scale(value): return {"x": value, "y": value, "z": value}

def main():
    connect()
    reports = []
    for path in sorted((ROOT/"Assets/Package_Unit/Timeline/Base").glob("*.json")):
        skill = path.stem
        doc = command("load", path=skill, discardChanges=True)
        operations = []
        clips = doc["clips"]
        bullet = next((c for c in clips if c["type"] == "PlayBullet"), None)
        hit = next((c for c in clips if c["type"] == "PlayHit"), None)
        for clip in clips:
            key, kind = clip["clipKey"], clip["type"]
            patch = {}
            if kind == "SceneDarken":
                operations.append({"action": "remove_clip", "key": key})
                continue
            if kind == "CameraShake":
                strength = 0.10 if skill == "giant_slam" else 0.035
                patch = {"PositionStrength": {"x": strength, "y": strength*0.6, "z": 0},
                    "Duration": 0.16 if skill == "giant_slam" else 0.12, "Vibrato": 3, "Randomness": 35}
            if kind == "PlayAnimator":
                times = {"shield_basic": .12, "shield_skill": .13, "shield_bastion": .13, "shield_warcry": .24}
                if skill in times:
                    for event in clip["events"]:
                        if event["name"] == "hit":
                            operations.append({"action":"patch_event", "key":event["key"], "patch":{"Time":times[skill]}})
            if kind == "PlayHit":
                size = .65 if skill.startswith("shield_") else .6
                if skill == "giant_slam": size = .8
                patch = {"UnitNodePointData":{"OffsetScale":scale(size)}, "AffectedByHitStop":False}
                if skill == "healer_heal":
                    patch.update(AnimatorParamName="", Duration=.4)
                    patch["UnitNodePointData"].update(OffsetNodeType=1, OffsetPos={"x":0,"y":.05,"z":0})
            if kind == "PlayBullet":
                patch = {"StartData":{"OffsetNodeType":40,"OffsetScale":scale(.7)},
                    "EndData":{"OffsetNodeType":43,"OffsetPos":{"x":0,"y":0,"z":0}}}
                if skill == "archer_gale": patch.update(TrajectoryType=1, ArcHeightFactor=0)
            if kind == "PlayEffect":
                patch = {"AffectedByHitStop":False}
                point = {"OffsetScale":scale(.35), "UseOriginalRotation":True,
                    "OverlayCharacterFacing":True, "NeedSetParent":False, "DontFollow":True}
                if skill in ("shield_basic","shield_skill"):
                    point.update(OffsetNodeType=43, OffsetScale=scale(1), OffsetPos={"x":0,"y":0,"z":.36})
                    patch["Duration"] = .16
                elif skill == "shield_bastion":
                    point.update(OffsetNodeType=43, OffsetScale=scale(.75)); patch["Duration"] = .45
                elif skill == "shield_warcry":
                    point.update(OffsetNodeType=1, OffsetScale=scale(.7)); patch["Duration"] = .45
                elif skill == "storm_pool":
                    point.update(OffsetNodeType=1, OffsetScale=scale(.65), OffsetPos={"x":0,"y":.03,"z":0})
                    patch.update(Subject=2, PreKey=bullet["clipKey"], StartTime=0, Duration=.55)
                elif skill == "frost_freeze":
                    point.update(OffsetNodeType=1, OffsetScale=scale(.5), OffsetPos={"x":0,"y":.03,"z":0})
                    patch.update(Subject=2, PreKey=bullet["clipKey"], StartTime=0, Duration=.4)
                elif skill == "frost_icearmor":
                    point.update(OffsetNodeType=43, OffsetScale=scale(.22)); patch["Duration"] = .3
                elif skill in ("archer_skill", "archer_gale"):
                    point.update(OffsetNodeType=43, OffsetScale=scale(.30 if skill == "archer_skill" else .45))
                    patch.update(Subject=2, PreKey=bullet["clipKey"], StartTime=0, Duration=.3)
                elif skill == "storm_tick":
                    point.update(OffsetNodeType=43, OffsetScale=scale(.45)); patch.update(Subject=2, Duration=.2)
                elif skill == "tactic_focus":
                    point.update(OffsetNodeType=1, OffsetScale=scale(.6), NeedSetParent=True, DontFollow=False)
                    patch.update(Subject=2)
                elif skill in ("tactic_protect", "unit_upgrade", "giant_telegraph"):
                    point.update(OffsetNodeType=1, OffsetScale=scale(.8 if skill == "giant_telegraph" else .65))
                elif skill == "giant_slam":
                    point.update(OffsetNodeType=1, OffsetScale=scale(.7)); patch["Duration"] = .4
                else:
                    point.update(OffsetNodeType=40, OffsetScale=scale(.16 if skill == "frost_basic" else .25))
                    patch["Duration"] = .16
                patch["PointData"] = point
            if "PreKey" in patch:
                operations.append({"action":"connect", "from":patch.pop("PreKey"), "to":key})
            if patch: operations.append({"action":"patch_clip", "key":key, "patch":patch})
        if skill.startswith("shield_"):
            event = next(e["key"] for c in clips for e in c["events"] if e["name"] == "hit")
            if not any(c["type"] == "HitStop" for c in clips):
                operations.append({"action":"add_clip", "type":"HitStop", "alias":"impact_stop", "patch":{
                    "Subject":0, "Duration":.035 if skill == "shield_basic" else .05}})
                operations.append({"action":"connect", "from":event, "to":"$impact_stop"})
        time.sleep(.15)
        revision = call("unity_skill_editor_read_timeline")["revision"]
        dry = call("unity_skill_editor_edit", revision=revision, dryRun=True, operations=operations)
        assert dry["battle"]["valid"], dry
        applied = call("unity_skill_editor_edit", revision=revision, dryRun=False, operations=operations)
        assert applied["applied"], applied
        updated = call("unity_skill_editor_read_timeline")
        validation = command("validate")
        assert validation["editor"]["valid"] and validation["battle"]["valid"], validation
        saved = command("save", revision=updated["revision"])
        reports.append({"id":skill, "before":doc, "operations":operations, "after":saved, "validation":validation})
        OUT.mkdir(parents=True,exist_ok=True)
        (OUT/"tuning.json").write_text(json.dumps(reports,ensure_ascii=False,indent=2),encoding="utf-8")
        print("saved",skill,"resources",validation["battle"]["resources"]["valid"],flush=True)

if __name__ == "__main__": main()
