"""使用正常招募、部署和共用触屏入口验收 HUD 与伤害统计。"""
import json
import time
import verify_combat as combat

combat.OUT = combat.ROOT / "Reports/Combat/hud-rework-20261127"


def main():
    combat.OUT.mkdir(parents=True, exist_ok=True)
    combat.mobile.connect_editor()
    tool = combat.mobile.mcp.tool
    catalog = json.loads(tool("unity_extension_catalog", extensionId="little-game.combat"))
    operations = {o["name"] for o in catalog["extensions"][0]["operations"]}
    assert not operations & {"aim", "focus", "protect", "cancel_aim"}
    combat.action("restart", seed=173)
    waves = []
    for wave in range(1, 11):
        for _ in range(combat.state()["spins"]):
            combat.action("spin")
            combat.wait_recruit_anim()
            combat.arrange()
        before = combat.state()
        assert before["deployedCount"] > 0
        if wave == 1:
            combat.capture("preparation")
        previous_total = sum(r["totalDamage"] for r in before["damageStatistics"])
        sequence = before["lastEventSequence"]
        combat.action("start_wave")
        combat.action("set_speed", speed=2)
        if wave == 1:
            combat.wait(lambda s: bool(s["damageStatistics"]) or s["phase"] != "Fighting")
            if combat.state()["phase"] == "Fighting":
                combat.click(combat.button("pause"))
                stopped = combat.state()
                time.sleep(.25)
                assert stopped["battleTime"] == combat.state()["battleTime"]
                combat.capture("fighting")
                combat.click(combat.button("pause"))
        result = combat.wait(lambda s: s["phase"] != "Fighting", timeout=90)
        assert result["phase"] != "Lost", result
        events = combat.call("get_events", after=sequence)["events"]
        ally_ids = {u["id"] for u in result["units"]}
        actual = sum(e["Amount"] for e in events if e["Type"] in ("hit", "skill_tick") and e["Source"] in ally_ids)
        wave_damage = sum(r["waveDamage"] for r in result["damageStatistics"])
        total_damage = sum(r["totalDamage"] for r in result["damageStatistics"])
        assert abs(wave_damage - actual) < .05, (wave, wave_damage, actual)
        assert abs(total_damage - previous_total - wave_damage) < .05
        time.sleep(.5)  # 等待收尾技能运镜和 HUD 布局提交
        combat.click(combat.button("damage_run"))
        time.sleep(.15)
        assert combat.view()["panels"]["damageScope"] == "run"
        combat.click(combat.button("damage_wave"))
        time.sleep(.15)
        assert combat.view()["panels"]["damageScope"] == "wave"
        view = combat.view()
        alignment = view["panels"]["layout"]
        assert abs(alignment["damageTop"] - alignment["headerBottom"]) < .0001
        assert abs(alignment["synergyTop"] - alignment["headerBottom"]) < .0001
        assert not {b["id"] for b in view["buttons"]} & {"aim_focus", "aim_protect", "cancel_aim"}
        expected_progress = max(0, wave - 1) / 9
        assert abs(view["panels"]["progress"] - expected_progress) < .001
        if wave in (1, 5, 10):
            combat.capture("wave-result-" + str(wave))
        waves.append({"wave": wave, "actualDamage": actual, "waveDamage": wave_damage, "totalDamage": total_damage, "view": view["panels"]})
        print("HUD wave", wave, result["phase"], "damage", round(wave_damage, 2), flush=True)
        if result["phase"] == "Reward":
            combat.action("choose_reward", reward="Reinforcements" if "Reinforcements" not in result["rewards"] else "WideStorm")
        if wave < 10:
            combat.action("next_wave")
    assert combat.state()["phase"] == "Won"
    combat.action("restart", seed=173)
    assert combat.state()["damageStatistics"] == []
    console = {level: tool("unity_get_console_logs", logType=level, maxEntries=30) for level in ("Error", "Exception", "Assert")}
    assert all("0 条" in value for value in console.values()), console
    (combat.OUT / "unity-validation.json").write_text(json.dumps({"waves": waves, "restartCleared": True, "console": console}, ensure_ascii=False, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
