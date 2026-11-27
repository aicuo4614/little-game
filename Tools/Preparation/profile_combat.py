"""固定 seed、正常招募部署的十波 Unity 无限帧率采样；不连接手机。"""
import json
import shutil
import sys
import time
import verify_combat as c

def main():
    label = sys.argv[1] if len(sys.argv) > 1 else "repeat"
    c.mobile.connect_editor()
    tool = c.mobile.mcp.tool
    tool("unity_enter_play_mode") if "Stopped" in tool("unity_get_play_mode_state") else None
    tool("unity_run_menu_item", menuPath="Little Game/Combat/Set Portrait Preview")
    time.sleep(.3)
    dimensions = c.view()
    assert (dimensions["width"], dimensions["height"]) == (540, 960), "必须使用与基线相同的竖屏分辨率"
    tool("unity_run_menu_item", menuPath="Little Game/Combat/Verify Pool Prewarm") if label == "after-verified" else None
    c.action("restart", seed=173)
    c.call("profile_capture", action="start")
    results = []
    initial_pool = c.view()["presentation"]["framework"]["pool"]
    try:
        for wave in range(1, 11):
            for _ in range(c.state()["spins"]):
                c.action("spin")
                c.wait_recruit_anim()
                c.arrange()
            before = c.state()
            results.append({"wave": wave, "units": before["units"]})
            c.action("set_speed", speed=2)
            c.action("start_wave")
            if label != "before-repeat" and wave in (5, 8, 10):
                time.sleep(.5)
                for sort in ("selfMs", "gcAlloc"):
                    hierarchy = tool("unity_profiler_get_hierarchy", topN=30, sortBy=sort)
                    (c.ROOT / ("Reports/Performance/20261127/" + label + "-wave-" + str(wave) + "-" + sort + ".json")).write_text(hierarchy, encoding="utf-8")
            end = c.wait(lambda s: s["phase"] != "Fighting", timeout=90)
            print(label, wave, end["phase"], flush=True)
            assert end["phase"] != "Lost"
            if end["phase"] == "Reward":
                c.action("choose_reward", reward="Reinforcements" if "Reinforcements" not in end["rewards"] else "WideStorm")
            if wave < 10:
                c.action("next_wave")
        assert c.state()["phase"] == "Won"
        final_pool = c.view()["presentation"]["framework"]["pool"]
        results.append({"initialPool": initial_pool, "finalPool": final_pool})
    finally:
        result = c.call("profile_capture", action="stop")
        out = c.ROOT / "Reports/Performance/20261127"
        shutil.copyfile(result["path"], out / (label + ".json"))
        (out / (label + "-conditions.json")).write_text(json.dumps({"seed": 173, "speed": 2, "waves": results}, ensure_ascii=False, indent=2), encoding="utf-8")
        print(result, flush=True)

if __name__ == "__main__":
    main()
