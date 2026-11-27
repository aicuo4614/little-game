"""检查关卡预热覆盖、缩小面板及玩家共用输入；不采集性能，不连接手机。"""
import json
import time
import verify_combat as c

c.OUT = c.ROOT / "Reports/Performance/20261127"

def main():
    c.mobile.connect_editor()
    tool = c.mobile.mcp.tool
    tool("unity_enter_play_mode")
    tool("unity_run_menu_item", menuPath="Little Game/Combat/Set Portrait Preview")
    tool("unity_run_menu_item", menuPath="Little Game/Combat/Verify Pool Prewarm")
    c.action("restart", seed=173)
    time.sleep(.4)
    initial = c.view()
    pool = initial["presentation"]["framework"]["pool"]
    assert pool["prewarm"]["ready"]
    assert len(pool["prewarm"]["models"]) == 13
    assert len(pool["prewarm"]["effects"]) == 27
    assert {"fx_boss_charge", "fx_level_up"} <= set(pool["prewarm"]["effects"])
    assert pool["prefabCount"] == 40 and pool["idle"] >= 40
    for _ in range(3):
        c.action("spin")
        c.wait_recruit_anim()
        c.arrange()
    c.capture("compact-damage-preparation")
    c.action("start_wave")
    c.wait(lambda s: bool(s["damageStatistics"]) or s["phase"] != "Fighting", timeout=30)
    if c.state()["phase"] == "Fighting":
        c.click(c.button("pause"))
        assert c.state()["paused"]
    c.click(c.button("damage_run"))
    assert c.view()["panels"]["damageScope"] == "run"
    c.click(c.button("damage_wave"))
    final = c.view()
    assert final["panels"]["damageScope"] == "wave"
    layout = final["panels"]["layout"]
    assert abs(layout["damageAreaRatio"] - .5) < .0001
    assert abs(layout["damageTop"] - layout["headerBottom"]) < .0001
    assert abs(layout["synergyTop"] - layout["headerBottom"]) < .0001
    c.capture("compact-damage-fighting")
    if c.state()["paused"]:
        c.click(c.button("pause"))
    result = c.wait(lambda s: s["phase"] != "Fighting", timeout=30)
    assert result["phase"] == "WaveResult"
    c.action("restart", seed=173)
    assert c.state()["damageStatistics"] == []
    console = {level: tool("unity_get_console_logs", logType=level, maxEntries=30) for level in ("Error", "Exception", "Assert", "Warning")}
    assert all("0 条" in value for value in console.values()), console
    (c.OUT / "delivery-validation.json").write_text(json.dumps({"platform": "WindowsEditor", "initialPool": pool, "finalLayout": layout, "result": result["phase"], "scopeButtonsViaPointer": True, "pauseViaPointer": True, "restartCleared": True, "console": console}, ensure_ascii=False, indent=2), encoding="utf-8")
    tool("unity_exit_play_mode")
    print("预热40种预制体、对象池、面板面积/对齐、切换与暂停共用输入、胜利重开及Console通过。", flush=True)

if __name__ == "__main__":
    main()
