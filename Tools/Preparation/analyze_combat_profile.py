"""汇总相同种子、正常流程的 Unity 无限帧率采样；不把编辑器耗时当手机帧率。"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Reports/Performance/20261127"

def main():
    label = sys.argv[1] if len(sys.argv) > 1 else "after-verified"
    before = json.loads((OUT / "before-repeat.json").read_text(encoding="utf-8"))
    after = json.loads((OUT / (label + ".json")).read_text(encoding="utf-8"))
    assert (before["width"], before["height"]) == (after["width"], after["height"]), "前后分辨率不同，不能作完整帧耗时对比"
    conditions = [json.loads((OUT / (name + "-conditions.json")).read_text(encoding="utf-8")) for name in ("before-repeat", label)]
    def formations(condition):
        return [[(u["id"], u["heroId"], u["star"], u["lane"], u["row"], u["homeX"], u["homeZ"]) for u in w["units"]] for w in condition["waves"] if "wave" in w]
    same = formations(conditions[0]) == formations(conditions[1])
    assert same, "前后阵容不一致，不能作相同阵容对比"
    def overall(data):
        frames = [f for f in data["frames"] if f["phase"] == 1]
        # Newtonsoft 默认把枚举写作整数；兼容字符串形式。
        if not frames:
            frames = [f for f in data["frames"] if f["phase"] == "Fighting"]
        return {"frames": len(frames), "meanMainMs": sum(f["mainMs"] for f in frames) / len(frames),
                "maxMainMs": max(f["mainMs"] for f in frames), "maxRulesMs": max(f["rulesMs"] for f in frames),
                "framesAbove16_67Ms": sum(f["mainMs"] > 16.67 for f in frames)}
    pairs = [{"wave": b["wave"], "before": b, "after": a} for b, a in zip(before["summary"], after["summary"])]
    result = {"platform": "WindowsEditor", "resolution": [after["width"], after["height"]],
              "targetFrameRate": -1, "vSyncCount": 0, "seed": 173, "speed": 2,
              "sameFormations": same, "before": overall(before), "after": overall(after), "waves": pairs,
              "limits": ["Profiler开启、非Deep Profile，主线程计数含编辑器与Profiler开销", "全局GC计数包含编辑器，业务分配用Hierarchy单独核查", "Render Thread recorder无效(-1)，本轮未取得GPU耗时", "不代表手机帧率或卡顿已完全解决"]}
    (OUT / "comparison.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({k: v for k, v in result.items() if k != "waves"}, ensure_ascii=False, indent=2))
    print("wave beforeRulesPeak afterRulesPeak afterNavPeak")
    for pair in pairs:
        print(pair["wave"], round(pair["before"]["maxRulesMs"], 3), round(pair["after"]["maxRulesMs"], 3), round(pair["after"].get("maxNavigationMs", -1), 3))

if __name__ == "__main__":
    main()
