"""只读观察正常战斗，比较纯规则命中时刻与共享播放器命中排程。"""
import json
import time
from capture_skill_review import connect, call, ROOT, OUT

def main():
    connect()
    rows={}
    deadline=time.monotonic()+240
    max_error=0
    ready=False
    while time.monotonic()<deadline:
        try:
            state=call("unity_combat_get_state")
            view=call("unity_combat_get_view")
            ready=True
        except (RuntimeError,ValueError,KeyError):
            if ready: break
            time.sleep(.4)
            continue
        skills={s["id"]:s for s in state.get("skills",[])}
        for visual in view["presentation"]["skillTimeline"].get("active",[]):
            skill=skills.get(visual["skillInstanceId"])
            if not skill or visual["visualImpactAt"] is None: continue
            error=abs(skill["impactAt"]-skill["startedAt"]-visual["visualImpactAt"])
            key=f'{state["wave"]}:{skill["id"]}:{skill["timelineId"]}'
            rows[key]={"wave":state["wave"],"rule":skill,"visual":visual,"differenceSeconds":error}
            max_error=max(max_error,error)
        if rows:
            (OUT/"runtime-timing.json").write_text(json.dumps({"samples":list(rows.values()),"maxDifferenceSeconds":max_error,
                "scope":"normal Unity gameplay; comparison of rule and presentation schedules, not device performance"},ensure_ascii=False,indent=2),encoding="utf-8")
        time.sleep(.10)
    assert rows,"没有采集到正常战斗技能"
    assert max_error <= .05, f"规则与表现命中排程偏差超过一个规则步长：{max_error}"
    print(json.dumps({"samples":len(rows),"maxDifferenceSeconds":max_error,"timelines":sorted({r["rule"]["timelineId"] for r in rows.values()})},ensure_ascii=False),flush=True)

if __name__=="__main__":main()
