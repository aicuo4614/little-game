"""通过真实 Unity MCP 和玩家共用输入验证英雄体系与第一关，运行前确认 Unity 指向本工程。"""
import json
import time

import verify_combat as combat

combat.OUT = combat.ROOT / "Reports/Combat/hero-rework-20261127/final"


def play():
    pending_events = set()
    waves = []
    for wave in range(1, 11):
        before = combat.state()
        assert before["wave"] == wave and before["phase"] == "Preparation", before
        giants = [e for e in before["forecast"] if e["kind"] == "Giant"]
        assert len(giants) == (1 if wave in (5, 10) else 0)
        if giants:
            assert len(before["forecast"]) == 1
        if wave == 1:
            combat.action("spin")
            unit = combat.state()["units"][0]
            assert unit["job"] == "Warrior" and unit["heroId"] in ("Knight", "Skeleton_Warrior"), unit
            combat.wait_recruit_anim()
            draft = combat.view()
            tile = next(b for b in draft["buttons"] if b["id"] == "unit_" + str(unit["id"]))
            combat.pointer("begin", tile)
            combat.pointer("move", {"x": .38, "y": .52})
            combat.pointer("cancel", {"x": .38, "y": .52})
            assert combat.state()["deployedCount"] == 0
            combat.click(tile)
            combat.click(tile)
            combat.wait(lambda state: state["deployedCount"] == 1, timeout=8)
        else:
            for _ in range(before["spins"]):
                combat.arrange()
                if combat.state()["pending"]:
                    break
                combat.action("spin")
                combat.wait_recruit_anim()
            combat.arrange()

        setup = combat.state()
        assert setup["deployedCount"] > 0, setup
        assert all(t["combat"]["mana"] == 0 for t in setup["units"]), setup
        sequence = setup["lastEventSequence"]
        started = combat.action("start_wave")
        assert started["spawned"] == (1 if wave in (1, 5, 10) else sum(1 for e in before["forecast"] if e["batch"] == 1))
        actors = combat.view()["presentation"]["actors"]
        assert actors and all(a["hasAnimator"] and a["hasUnitMono"] and a["model"].startswith("Unit_") for a in actors), actors
        if wave in (1, 5, 10):
            combat.wait(lambda state: state["battleTime"] >= 1.5 or state["phase"] != "Fighting", timeout=25)
            combat.capture(f"wave-{wave:02d}")
        combat.action("set_speed", speed=2)
        result = combat.wait(lambda state: state["phase"] != "Fighting")
        events = combat.call("get_events", after=sequence)["events"]
        kinds = {event.get("Type", event.get("type")) for event in events}
        pending_events.update(kinds)
        assert result["phase"] != "Lost", {"wave": wave, "units": result["units"], "events": events[-20:]}
        assert all(u["combat"]["mana"] == 0 for u in result["units"]), result
        assert "spawn_batch" in kinds
        waves.append({"wave": wave, "phase": result["phase"], "spawned": result["spawned"],
                      "duration": round(result["battleTime"], 2), "events": sorted(kinds),
                      "heroes": [{"id": t["heroId"], "star": t["star"]} for t in result["units"]],
                      "synergies": result["synergies"], "models": sorted({actor["role"] for actor in actors})})
        print(f'第 {wave} 波 {result["phase"]}，共出怪 {result["spawned"]}', flush=True)
        if result["phase"] == "Reward":
            reward = "Reinforcements" if "Reinforcements" not in result["rewards"] else "WideStorm"
            combat.action("choose_reward", reward=reward)
        if wave < 10:
            combat.action("next_wave")
    assert combat.state()["phase"] == "Won"
    assert {"boss_telegraph", "assassin_landed", "cast_started", "run_won"} <= pending_events, pending_events
    return waves


def main():
    combat.OUT.mkdir(parents=True, exist_ok=True)
    combat.mobile.connect_editor()
    tool = combat.mobile.mcp.tool
    info = json.loads(tool("unity_get_project_info"))
    assert info["dataPath"] == "E:/Project/little-game/Assets", info
    assert "最近编译成功" in tool("unity_get_compilation_status")
    available = {item["name"] for item in combat.mobile.mcp.rpc("tools/list")["tools"]}
    assert "unity_combat_get_definitions" in available and "unity_combat_choose_branch" not in available
    definitions = combat.call("get_definitions")
    assert len([unit for unit in definitions["units"] if unit["grade"] == "Hero"]) == 8
    assert [row["wave"] for row in definitions["stage"] if row["boss"]] == [5, 10]
    combat.action("restart", seed=173)
    assert not combat.call("start_wave")["accepted"]
    waves = play()
    combat.capture("victory")
    reset = combat.action("restart", seed=173)
    assert reset["wave"] == 1 and reset["phase"] == "Preparation" and not reset["units"]
    console = {level: tool("unity_get_console_logs", logType=level, maxEntries=50) for level in ("Error", "Exception", "Assert", "Warning")}
    report = {"platform": "WindowsEditor", "waves": waves, "restart": True, "console": console}
    (combat.OUT / "unity-validation.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    assert all("0 条" in console[level] for level in ("Error", "Exception", "Assert")), console


if __name__ == "__main__":
    main()
