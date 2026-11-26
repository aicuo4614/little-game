"""从零招募并自然升到二星，分别选择两组分支；不刷兵、不改数值或跳波。"""
import json
import time
import sys
from pathlib import Path
import verify_combat as v

OUT=Path(__file__).resolve().parents[2]/"Reports/Skills/20261126"
BRANCHES=[{"Shield":"Bastion","Archer":"Pierce","Storm":"StormPool","Frost":"DeepFreeze"},
          {"Shield":"WarCry","Archer":"Gale","Storm":"ChainLightning","Frost":"IceArmor"}]
EXPECTED=[{"shield_bastion","archer_pierce","storm_pool","frost_freeze"},
          {"shield_warcry","archer_gale","storm_chain","frost_icearmor"}]

def main():
    v.mobile.connect_editor()
    m=v.mobile.mcp
    resume="--resume" in sys.argv
    if not resume:
        m.tool("unity_exit_play_mode")
        m.tool("unity_run_menu_item",menuPath="Little Game/Combat/Open Combat")
        m.tool("unity_enter_play_mode")
    deadline=time.monotonic()+30
    while True:
        try:
            v.state(); break
        except Exception:
            if time.monotonic()>deadline:raise
            time.sleep(.3)
    reports=[]
    only_first="--first-only" in sys.argv
    if only_first and (OUT/"branch-runtime.json").exists():
        reports=[r for r in json.loads((OUT/"branch-runtime.json").read_text(encoding="utf-8"))["runs"] if r["pass"]!=1]
    for index,branches in enumerate(BRANCHES):
        if only_first and index!=0: continue
        if not resume:
            v.action("restart",seed=173);v.action("set_speed",speed=2)
        last=0;impacts=set();selected=[];selected_units=set();timing={};max_error=0;deadline=time.monotonic()+300
        if resume:
            current=v.state()
            for unit in current["units"]:
                if unit["star"]>=2 and unit["branch"]==branches[unit["kind"]]:
                    selected_units.add(unit["id"])
                    selected.append({"kind":unit["kind"],"branch":unit["branch"],"observedAtWave":current["wave"],"alreadySelected":True})
            print("resumed",current["wave"],"selected",selected,flush=True)
        while time.monotonic()<deadline:
            state=v.state()
            events=v.call("get_events",after=last)
            last=events["lastSequence"]
            for event in events["events"]:
                if event["Type"]=="cast_impact" and event.get("TimelineId") in EXPECTED[index] and event["Source"] in selected_units:
                    impacts.add(event["TimelineId"])
            if state["phase"]=="Preparation":
                v.arrange()
                while v.state()["spins"]>0:
                    v.action("spin");v.arrange()
                for unit in v.state()["units"]:
                    if unit["star"]>=2 and unit["branch"]=="None":
                        v.action("choose_branch",unitId=unit["id"],branch=branches[unit["kind"]])
                        selected_units.add(unit["id"])
                        selected.append({"kind":unit["kind"],"branch":branches[unit["kind"]],"wave":state["wave"]})
                print("pass",index+1,"wave",state["wave"],"branches",[x["branch"] for x in selected],flush=True)
                v.action("start_wave")
            elif state["phase"]=="Reward":
                v.action("choose_reward",reward="Reinforcements" if "Reinforcements" not in state["rewards"] else "WideStorm")
            elif state["phase"]=="WaveResult":v.action("next_wave")
            elif state["phase"] in ("Won","Lost"):break
            else:
                # 此处观察时间轴；换波时UI仍可能处于布局帧，UI边界另由完整验收脚本检查。
                view=v.call("get_view")
                skills={s["id"]:s for s in state.get("skills",[])}
                for visual in view["presentation"]["skillTimeline"].get("active",[]):
                    skill=skills.get(visual["skillInstanceId"])
                    if skill and visual["visualImpactAt"] is not None:
                        error=abs(skill["impactAt"]-skill["startedAt"]-visual["visualImpactAt"])
                        timing[skill["id"]]={"rule":skill,"visual":visual,"differenceSeconds":error}
                        max_error=max(max_error,error)
                time.sleep(.12)
            if EXPECTED[index]<=impacts:break
        assert EXPECTED[index]<=impacts,{"selected":selected,"impacts":sorted(impacts),"phase":v.state()["phase"]}
        assert max_error<=.05,max_error
        assert {s["kind"] for s in selected}==set(branches),selected
        report={"pass":index+1,"selected":selected,"impacts":sorted(impacts),"sourceSelectionChecked":True,"timingSamples":len(timing),
                "maxTimingDifferenceSeconds":max_error,"timing":list(timing.values())}
        reports.append(report);reports.sort(key=lambda r:r["pass"])
        (OUT/"branch-runtime.json").write_text(json.dumps({"passed":True,"runs":reports},ensure_ascii=False,indent=2),encoding="utf-8")
        print("BRANCH_PASS",index+1,sorted(impacts),"max timing delta",max_error,flush=True)
    console={}
    for level in ("Error","Warning","Exception","Assert"):
        console[level]=m.tool("unity_get_console_logs",logType=level,maxEntries=20)
        assert console[level].startswith("Console 日志 0 条"),console
    (OUT/"branch-runtime.json").write_text(json.dumps({"passed":True,"runs":reports,"console":console},ensure_ascii=False,indent=2),encoding="utf-8")
    m.tool("unity_exit_play_mode")
    print("ALL_EIGHT_BRANCHES_PASS",flush=True)

if __name__=="__main__":main()
