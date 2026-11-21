import json
from pathlib import Path
import re
import urllib.request

url = "http://127.0.0.1:3833/mcp"
headers = {"Accept": "application/json, text/event-stream", "Content-Type": "application/json"}
seq = 0


def rpc(method, params=None, notify=False):
    global seq
    seq += 1
    data = {"jsonrpc": "2.0", "method": method}
    if params is not None:
        data["params"] = params
    if not notify:
        data["id"] = seq
    with urllib.request.urlopen(urllib.request.Request(url, json.dumps(data).encode(), headers), timeout=65) as response:
        session = response.headers.get("Mcp-Session-Id")
        if session:
            headers["Mcp-Session-Id"] = session
        if notify:
            return
        if response.headers.get_content_type() == "application/json":
            envelope = json.load(response)
            if "error" in envelope:
                raise RuntimeError(envelope["error"])
            return envelope["result"]
        data_lines = []
        for line in response:
            if line.startswith(b"data:"):
                data_lines.append(line[5:].strip())
            elif not line.strip() and data_lines:
                # ASP.NET 可能先发送一个空的 SSE 事件。
                if not any(data_lines):
                    data_lines.clear()
                    continue
                envelope = json.loads(b"\n".join(data_lines))
                if envelope.get("id") == seq:
                    break
                data_lines.clear()
        else:
            raise RuntimeError(f"MCP 响应中没有请求 {seq} 的结果")
        if "error" in envelope:
            raise RuntimeError(envelope["error"])
        return envelope["result"]


def tool(tool_name, **arguments):
    value = rpc("tools/call", {"name": tool_name, "arguments": arguments})
    if value.get("isError"):
        raise RuntimeError(value)
    return value["content"][0]["text"]


def main():
    init = rpc("initialize", {
        "protocolVersion": "2025-11-25",
        "capabilities": {},
        "clientInfo": {"name": "little-game-preparation", "version": "1"},
    })
    headers["MCP-Protocol-Version"] = init["protocolVersion"]
    rpc("notifications/initialized", notify=True)

    project = json.loads(tool("unity_get_project_info"))
    assets = Path(__file__).resolve().parents[2] / "Assets"
    if Path(project["dataPath"]).resolve() != assets:
        raise RuntimeError(f"MCP 指向了另一个项目：{project['dataPath']}")
    if project["isPlaying"] or project["isCompiling"]:
        raise RuntimeError("请在编辑器停止且编译结束后执行冒烟验证")
    scene = project["activeScene"]
    if scene["path"] != "Assets/Scenes/Preparation.unity" or scene["isDirty"]:
        raise RuntimeError("请打开已保存的 Preparation 场景")

    status = tool("unity_get_compilation_status")
    print(status)
    if "最近编译成功" not in status:
        raise RuntimeError("Unity 未报告编译成功")

    camera = tool("unity_find_game_object", name="Main Camera")
    camera_match = re.search(r"instanceId\s*[:：]\s*(-?\d+)", camera)
    if not camera_match:
        raise RuntimeError(camera)

    try:
        play = json.loads(tool("unity_enter_play_mode"))
        if not play["isPlaying"] or play["framesAdvanced"] <= 0:
            raise RuntimeError(play)
        print(f"Play 帧推进：{play['framesAdvanced']}")

        capture = json.loads(tool(
            "unity_capture_camera", cameraInstanceId=int(camera_match[1]), width=540, height=960,
        ))
        if not Path(capture["path"]).is_file():
            raise RuntimeError(capture)
        print(f"竖屏相机截图：{capture['path']}")

        for level in ("Error", "Exception", "Assert", "Warning"):
            logs = tool("unity_get_console_logs", logType=level, maxEntries=15)
            print(f"{level} {logs}")
            if level != "Warning" and not logs.startswith("Console 日志 0 条"):
                raise RuntimeError(f"Console 存在 {level} 或读取失败")
    finally:
        print(tool("unity_exit_play_mode"))
        state = tool("unity_get_play_mode_state")
        print(state)
        if "Stopped" not in state:
            raise RuntimeError("Unity 未回到编辑模式")


if __name__ == "__main__":
    main()
