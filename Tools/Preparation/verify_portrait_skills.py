"""技能竖屏、双阵营、画质、有效材质和停止清理的真实 Unity MCP 回归。"""
import json
from capture_skill_review import ROOT, OUT, connect, call, command
import smoke_mcp

def main():
    connect()
    call("unity_skill_editor_open")
    rows=[]
    for file in sorted((ROOT/"Assets/Package_Unit/Timeline/Base").glob("*.json")):
        command("load",path=file.stem,discardChanges=True)
        for team in ("TeamA","TeamB"):
            for quality in ("Low","HQ"):
                call("unity_skill_editor_set_preview",casterTeam=team,quality=quality)
                command("preview_stage",mode="Battle")
                call("unity_skill_editor_seek",time=.01)
                doc=call("unity_skill_editor_read_timeline")
                hit=next((c for c in doc["clips"] if c["type"]=="PlayHit"),None)
                timing=hit["contentStart"]+.03 if hit else .20
                call("unity_skill_editor_seek",time=timing)
                validation=command("validate")
                assert validation["editor"]["valid"] and validation["battle"]["valid"] and validation["battle"]["resources"]["valid"],validation
                capture=command("capture",width=360)
                diag=capture["state"]
                assert diag["camera"]["orthographic"] and abs(diag["aspect"]-9/16)<.00001,diag
                for unit in diag["units"]:
                    x,y,z=unit["viewport"]
                    assert .02<x<.98 and .02<y<.98 and z>0,unit
                call("unity_skill_editor_stop")
                stopped=command("diagnostics")
                assert not any(p["active"] and p["particleCount"] for p in stopped["particles"]),stopped
                rows.append({"id":file.stem,"team":team,"quality":quality,"capture":capture["path"],"validation":validation,"cleanup":True})
        (OUT/"portrait-matrix.json").write_text(json.dumps({"passed":True,"cases":rows},ensure_ascii=False,indent=2),encoding="utf-8")
        print(file.stem,"4 cases passed",flush=True)
    assets=command("assets",type="Prefab",query="fx_",limit=200)
    assert assets["total"]==40 and all(x["address"] for x in assets["items"]),assets
    records=[]
    for skill in ("shield_basic","archer_gale","storm_pool","frost_freeze"):
        command("load",path=skill,discardChanges=True)
        for team in ("TeamA","TeamB"):
            record=call("unity_skill_editor_record",casterTeam=team,quality="Low",sampleHz=30,untilCutoff=True)
            records.append({"id":skill,"team":team,"record":record})
    (OUT/"representative-records.json").write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding="utf-8")
    call("unity_skill_editor_set_preview",casterTeam="TeamA",quality="Low")
    command("load",path="shield_basic",discardChanges=True)
    command("preview_stage",mode="Battle")
    smoke_mcp.tool("unity_save_scene")
    print("PORTRAIT_SKILLS_PASS",len(rows),"cases; 8 recordings; runtime-only asset addresses",flush=True)

if __name__=="__main__":main()
