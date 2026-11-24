import json
import time
from pathlib import Path
from smoke_mcp import rpc, tool, headers


def main():
    init = rpc("initialize", {
        "protocolVersion": "2025-11-25", "capabilities": {},
        "clientInfo": {"name": "puzzle-verification", "version": "2"},
    })
    headers["MCP-Protocol-Version"] = init["protocolVersion"]
    rpc("notifications/initialized", notify=True)
    project = json.loads(tool("unity_get_project_info"))
    assert Path(project["dataPath"]).resolve() == Path(__file__).resolve().parents[2] / "Assets"
    assert project["activeScene"]["path"] == "Assets/Scenes/Game.unity"
    assert not project["isPlaying"] and not project["isCompiling"]
    assert "最近编译成功" in tool("unity_get_compilation_status")
    catalog=json.loads(tool("unity_extension_catalog",extensionId="little-game.puzzle"))
    operations={item["name"] for item in catalog["extensions"][0]["operations"]}
    assert "disassemble" in operations and not operations.intersection({"tap","drag"})
    records = []

    def act(operation, expected=True, wait=True, **arguments):
        state = json.loads(tool(
            "unity_extension_call", extensionId="little-game.puzzle",
            operation=operation, arguments=arguments, allowWrite=True,
        ))
        assert state["accepted"] == expected, state
        records.append({"operation": operation, "arguments": arguments, "state": state})
        deadline = time.monotonic() + 6
        while wait and state["animating"]:
            assert time.monotonic() < deadline, state
            time.sleep(.06)
            state = json.loads(tool("unity_extension_call", extensionId="little-game.puzzle", operation="get_state"))
        return state

    try:
        assert json.loads(tool("unity_enter_play_mode"))["isPlaying"]
        act("disassemble", expected=False, part="screw_a")
        state = act("get_state", expected=False)
        initial = state["orientation"]
        def same_orientation(left, right):
            dot = sum(left[key] * right[key] for key in ("x", "y", "z", "w"))
            return abs(abs(dot) - 1) < .0001
        state = act("orbit", fromX=0, fromY=0, toX=.5, toY=.3)
        assert not same_orientation(initial, state["orientation"])
        state = act("orbit", fromX=.5, fromY=.3, toX=0, toY=0)
        assert same_orientation(initial, state["orientation"])
        for axis in ("delta", "tilt", "roll"):
            args = {"delta": 0, axis: 360}
            state = act("rotate", **args)
            assert same_orientation(initial, state["orientation"])
        state = act("rotate", delta=0, tilt=137)
        assert not same_orientation(initial, state["orientation"])
        state = act("rotate", delta=0, tilt=-137)
        assert same_orientation(initial, state["orientation"])
        for _ in range(12):
            state = act("rotate", delta=71, tilt=-53, roll=29)
            norm = sum(value * value for value in state["orientation"].values())
            assert abs(norm - 1) < .0001
        act("restart")
        state = act("flip")
        assert state["backVisible"]
        act("disassemble", expected=False, part="cable")
        state = act("disassemble", expected=False, part="cover")
        assert state["coverAttached"] and state["moves"] == 0
        act("select", part="screw_a")
        state = act("disassemble", wait=False, part="screw_a")
        assert state["animating"] and state["animationPart"] == "screw_a"
        act("disassemble", expected=False, part="screw_b")
        act("disassemble", expected=False, part="screw_a")
        act("disassemble", expected=False, part="cover")
        act("disassemble", part="screw_b")
        state = act("disassemble", part="cover")
        assert not state["coverAttached"] and state["stage"] == "disconnect_battery"
        act("disassemble", expected=False, part="battery")
        act("disassemble", expected=False, part="board")
        act("disassemble", part="cable")
        act("disassemble", expected=False, part="board")
        act("disassemble", part="battery")
        act("disassemble", expected=False, part="board")
        before = act("get_state", expected=False)["orientation"]
        state = act("rotate", delta=0, roll=123)
        assert state["backVisible"] and not same_orientation(before, state["orientation"])
        act("rotate", delta=0, roll=-123)
        act("flip")
        state = act("get_state")
        assert not state["backVisible"]
        act("disassemble", expected=False, part="board_screw")
        act("flip")
        act("disassemble", part="board_screw")
        state = act("disassemble", part="board")
        assert state["phase"] == "playing" and state["moves"] == 7
        state=act("disassemble",part="chassis")
        assert state["phase"]=="won" and state["moves"]==8 and state["recoveredCount"]==8
        act("disassemble", expected=False, part="board")
        time.sleep(.1)
        view=json.loads(tool("unity_extension_call",extensionId="little-game.puzzle",operation="get_view"))
        assert all(slot["filled"] for slot in view["collection"])
        assert all(view["collection"][i]["x"]<view["collection"][i+1]["x"] for i in range(7))
        state = act("restart")
        assert state["moves"] == 0 and state["coverAttached"] and state["batteryAttached"]
        assert state["screwAAttached"] and state["screwBAttached"] and not state["backVisible"]
        assert state["selected"] == "" and same_orientation(initial, state["orientation"])
        for level in ("Error", "Exception", "Assert", "Warning"):
            logs = tool("unity_get_console_logs", logType=level)
            assert logs.startswith("Console 日志 0 条"), logs
        report = Path(__file__).resolve().parents[2] / "Reports" / "gameplay-verification.json"
        report.write_text(json.dumps(records, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"通过 {len(records)} 次正常玩法操作，旋转、遮挡、拆卸、完成和重开通过。")
    finally:
        print(tool("unity_exit_play_mode"))


if __name__ == "__main__":
    main()

