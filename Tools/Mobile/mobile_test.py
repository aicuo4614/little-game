"""由智能体执行的 Android 构建、安装、启动与取证工具。"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tools" / "Preparation"))
import smoke_mcp as mcp

PACKAGE = "com.littlegame.teardown.dev"
STATUS = ROOT / "Library" / "MobileTesting" / "build-status.json"


def run(command, timeout=60, binary=False, check=True):
    result = subprocess.run(
        [str(part) for part in command], capture_output=True,
        text=not binary, encoding=None if binary else "utf-8",
        errors=None if binary else "replace", timeout=timeout,
        creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0,
    )
    if check and result.returncode:
        error = result.stderr if result.stderr else result.stdout
        raise RuntimeError(f"命令失败 {command[0]}\n{error}")
    return result.stdout


def connect_editor():
    deadline = time.monotonic() + 90
    last_error = ""
    while time.monotonic() < deadline:
        try:
            mcp.headers.pop("Mcp-Session-Id", None)
            init = mcp.rpc("initialize", {
                "protocolVersion": "2025-11-25", "capabilities": {},
                "clientInfo": {"name": "little-game-mobile", "version": "1.0"},
            })
            mcp.headers["MCP-Protocol-Version"] = init["protocolVersion"]
            mcp.rpc("notifications/initialized", notify=True)
            raw = mcp.tool("unity_get_project_info")
            project = json.loads(raw)
            if project["isCompiling"]:
                last_error = "Unity 正在编译"
                time.sleep(2)
                continue
        except (OSError, ValueError, RuntimeError) as error:
            last_error = str(error)
            time.sleep(2)
            continue
        if Path(project["dataPath"]).resolve() != ROOT / "Assets":
            raise RuntimeError("MCP 指向了其他 Unity 工程")
        return project
    raise RuntimeError(f"Unity MCP 未就绪：{last_error}")


def find_adb():
    override = os.environ.get("LITTLE_GAME_ADB")
    candidates = [
        Path(override) if override else Path(""),
        Path("D:/Work/Unity/2022.3.62f3c1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"),
    ]
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    import shutil
    found = shutil.which("adb")
    if found:
        return Path(found)
    raise RuntimeError("找不到 ADB，可通过 LITTLE_GAME_ADB 指定 Unity SDK 的 adb.exe")


def choose_device(adb, serial):
    output = run([adb, "devices", "-l"])
    devices = []
    for line in output.splitlines()[1:]:
        fields = line.split()
        if len(fields) >= 2 and not line.startswith("*"):
            devices.append((fields[0], fields[1]))
    if serial:
        matches = [device for device in devices if device[0] == serial]
        if not matches:
            raise RuntimeError("指定的手机未连接")
        devices = matches
    ready = [device[0] for device in devices if device[1] == "device"]
    if len(ready) == 1:
        return ready[0]
    if len(ready) > 1:
        raise RuntimeError("检测到多台手机，请用 --serial 指定本次测试手机")
    if any(state == "unauthorized" for _, state in devices):
        raise RuntimeError("手机尚未授权 USB 调试，请解锁手机并点击允许")
    raise RuntimeError("没有可用手机，请连接数据线、开启 USB 调试并检查是否离线")


def read_status():
    if not STATUS.exists():
        return {}
    return json.loads(STATUS.read_text(encoding="utf-8-sig"))


def build_apk(timeout):
    project = connect_editor()
    if project["isPlaying"] or project["isCompiling"]:
        raise RuntimeError("请先退出 Play 并等待编译结束")
    previous = read_status().get("id")
    message = mcp.tool("unity_run_menu_item", menuPath="Little Game/Mobile/Build Test APK")
    current = read_status()
    if current.get("id") == previous or not current.get("id"):
        raise RuntimeError(f"构建任务未创建：{message}")
    job = current["id"]
    deadline = time.monotonic() + timeout
    last_state = None
    while time.monotonic() < deadline:
        current = read_status()
        if current.get("id") != job:
            raise RuntimeError("构建任务被另一次请求替换")
        state = current.get("state")
        if state != last_state:
            print(current.get("message", state), flush=True)
            last_state = state
        if state == "failed":
            raise RuntimeError(current["message"])
        if state == "succeeded":
            apk = Path(current["apk"])
            if current["packageId"] != PACKAGE or not apk.is_file() or apk.stat().st_size != current["bytes"]:
                raise RuntimeError("构建产物校验失败")
            return apk
        time.sleep(2)
    raise RuntimeError("构建等待超时，Unity 可能仍在构建，请查看 Logs/unity-demo-baseline.log 和构建状态")


def deploy(adb, serial, apk):
    command = [adb, "-s", serial]
    report_dir = ROOT / "Reports" / "Mobile" / time.strftime("%Y%m%d-%H%M%S")
    report_dir.mkdir(parents=True, exist_ok=True)
    print("正在安装手机测试包", flush=True)
    installed = run(command + ["install", "-r", str(apk)], timeout=180)
    (report_dir / "install.txt").write_text(installed, encoding="utf-8")
    if "Success" not in installed:
        raise RuntimeError(installed)
    activity = run(command + [
        "shell", "cmd", "package", "resolve-activity", "--brief",
        "-a", "android.intent.action.MAIN", "-c", "android.intent.category.LAUNCHER", PACKAGE,
    ]).strip().splitlines()[-1]
    if not activity.startswith(PACKAGE + "/"):
        raise RuntimeError(f"无法解析游戏启动入口：{activity}")
    run(command + ["shell", "am", "force-stop", PACKAGE])
    started = run(command + ["shell", "am", "start", "-W", "-n", activity])
    (report_dir / "launch.txt").write_text(started, encoding="utf-8")
    if "Status: ok" not in started:
        raise RuntimeError(f"游戏启动失败：{started}")
    time.sleep(5)
    pid = run(command + ["shell", "pidof", PACKAGE], check=False).strip()
    if not pid:
        logs = run(command + ["logcat", "-d", "-t", "300"])
        (report_dir / "launch-failure.txt").write_text(logs, encoding="utf-8")
        raise RuntimeError(f"启动后游戏进程已退出，日志位于 {report_dir}")
    logs = run(command + ["logcat", "-d", "--pid=" + pid.split()[0], "-t", "500"])
    (report_dir / "logcat.txt").write_text(logs, encoding="utf-8")
    screen = run(command + ["exec-out", "screencap", "-p"], binary=True)
    if not screen.startswith(b"\x89PNG\r\n\x1a\n"):
        raise RuntimeError("未取得有效的手机截图")
    (report_dir / "screen.png").write_bytes(screen)
    foreground = run(command + ["shell", "dumpsys", "activity", "activities"])
    resumed = [line for line in foreground.splitlines() if "ResumedActivity" in line or "topResumedActivity" in line]
    runtime_errors = [
        line for line in logs.splitlines()
        if "E Unity" in line or "FATAL EXCEPTION" in line or "Fatal signal" in line
    ]
    evidence = {
        "packageId": PACKAGE, "apk": str(apk), "pid": pid,
        "foreground": resumed, "launched": True, "runtimeErrors": runtime_errors, "screenshot": str(report_dir / "screen.png"),
    }
    (report_dir / "result.json").write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding="utf-8")
    if not any(PACKAGE in line for line in resumed):
        raise RuntimeError(f"游戏进程存在，但未确认前台显示。请检查手机锁屏或系统弹窗，截图在 {report_dir}")
    if runtime_errors:
        raise RuntimeError(f"手机已启动，但检测到运行错误，日志与截图位于 {report_dir}")
    print(f"游戏已安装并打开，PID {pid}，截图和日志位于 {report_dir}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", help="多台手机时指定设备序列号")
    parser.add_argument("--build-only", action="store_true", help="只构建 APK")
    parser.add_argument("--install-built", action="store_true", help="安装最近一次成功构建的 APK")
    parser.add_argument("--timeout", type=int, default=1800, help="构建等待秒数")
    args = parser.parse_args()
    os.chdir(ROOT)
    adb = find_adb()
    serial = None
    if not args.build_only:
        serial = choose_device(adb, args.serial)
        abi = run([adb, "-s", serial, "shell", "getprop", "ro.product.cpu.abilist"])
        if "arm64-v8a" not in abi:
            raise RuntimeError("当前测试包需要 ARM64 手机")
    if args.install_built:
        status = read_status()
        if status.get("state") != "succeeded":
            raise RuntimeError("没有可安装的成功构建产物")
        apk = Path(status["apk"])
        if status.get("packageId") != PACKAGE or not apk.is_file() or apk.stat().st_size != status["bytes"]:
            raise RuntimeError("已构建 APK 校验失败")
    else:
        apk = build_apk(args.timeout)
    print(f"APK 已就绪：{apk}", flush=True)
    if serial:
        deploy(adb, serial, apk)


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, subprocess.TimeoutExpired) as error:
        print(f"手机验证未完成：{error}", file=sys.stderr)
        sys.exit(1)
