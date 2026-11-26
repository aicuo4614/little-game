"""核对3D新建模板、截图状态约束，并保存最终竖屏编辑器场景与证据。"""
import json
import shutil
from capture_skill_review import connect, call, command, OUT
import smoke_mcp

def main():
    connect()
    baseline_warnings=smoke_mcp.tool("unity_get_console_logs",logType="Warning",maxEntries=30)
    smoke_mcp.tool("unity_clear_console_logs")
    call("unity_skill_editor_open")
    call("unity_skill_editor_set_preview",casterTeam="TeamA",quality="Low")
    old=call("unity_skill_editor_read_timeline")
    new=command("new",revision=old["revision"],timelineId="portrait_new_template_probe",discardChanges=True)
    assert [c["type"] for c in new["clips"]]==["SetCastTargets","SetHitTargets","PlayAnimator"],new
    assert new["previewCasterAddress"]=="Archer/Archer",new
    validation=command("validate")
    assert validation["battle"]["valid"] and validation["battle"]["resources"]["valid"],validation
    command("load",path="shield_basic",discardChanges=True)
    call("unity_skill_editor_play",fromStart=True)
    rejected=False
    try:command("capture")
    except RuntimeError:rejected=True
    assert rejected,"播放过程中应拒绝定点截图"
    call("unity_skill_editor_stop")
    command("preview_stage",mode="Detail")
    call("unity_skill_editor_seek",time=.15)
    detail=command("capture")
    shutil.copyfile(detail["path"],OUT/"shield-contact-detail.png")
    command("preview_stage",mode="Battle")
    call("unity_skill_editor_seek",time=.15)
    battle=command("capture")
    shutil.copyfile(battle["path"],OUT/"shield-contact-battle.png")
    doc=call("unity_skill_editor_read_timeline")
    if doc["hasUnsavedChanges"]:command("save",revision=doc["revision"])
    scene=smoke_mcp.tool("unity_save_scene")
    console={level:smoke_mcp.tool("unity_get_console_logs",logType=level,maxEntries=20) for level in ("Error","Warning","Exception","Assert")}
    assert all(value.startswith("Console 日志 0 条") for value in console.values()),console
    result={"new3DTemplate":True,"newTemplateValidation":validation,"captureDuringPlaybackRejected":rejected,
        "finalStatus":call("unity_skill_editor_status"),"scene":scene,"console":console,
        "warningsBeforeScenario":baseline_warnings,"consoleScope":"fresh editor preview scenario after compilation; baseline warnings recorded separately",
        "detailCapture":detail["path"],"battleCapture":battle["path"]}
    (OUT/"final-editor-check.json").write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
    print("FINAL_EDITOR_PASS: 3D template; fixed portrait captures; saved scene; console zero; left shield_basic Battle at 0.15s",flush=True)

if __name__=="__main__":main()
