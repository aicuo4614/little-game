"""结构样板正常玩法、玩家输入和结构空间专项验证。"""
import json
import shutil
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Mobile'))
import mobile_test as mobile

def main():
    mobile.connect_editor()
    m = mobile.mcp
    output = ROOT / 'Reports'
    records = []
    checks = []
    def call(operation, **arguments):
        result = json.loads(m.tool('unity_extension_call', extensionId='little-game.structure', operation=operation, arguments=arguments, allowWrite=True))
        records.append(dict(operation=operation, arguments=arguments, result=result))
        return result
    def state(): return call('get_state')
    def view(): return call('get_view')
    def part(name): return next(p for p in view()['parts'] if p['id'] == name)
    def pointer(phase, point): return call('pointer', phase=phase, x=point['x'], y=point['y'])
    def click(point):
        pointer('begin', point)
        return pointer('end', point)
    def button(name): return next(p for p in view()['hud']['buttons'] if p['id'] == name)
    def capture(name):
        result = call('capture_view')
        shutil.copyfile(result['path'], output / f'structure-{name}.png')
    def wait(predicate, timeout=12):
        end = time.monotonic() + timeout
        while True:
            s = state()
            if predicate(s): return s
            assert time.monotonic() < end, s
            time.sleep(.08)
    success = False
    try:
        assert '最近编译成功' in m.tool('unity_get_compilation_status')
        m.tool('unity_exit_play_mode')
        m.tool('unity_run_menu_item', menuPath='Little Game/Open Structure Sample')
        m.tool('unity_enter_play_mode')
        catalog = json.loads(m.tool('unity_extension_catalog', extensionId='little-game.structure'))
        assert catalog['projectPath'] == 'E:/Project/little-game/Assets'
        assert catalog['extensions'][0]['available']
        geometry = call('get_structure')
        assert not geometry['sweep']['collisions'], geometry
        assert not geometry['geometryEnvelopeErrors'], geometry
        assert geometry['sweep']['minGateBottom'] > .10
        assert geometry['sweep']['samples'] == 851
        checks.append('851 个摆角采样无非预期实体交叉；盖板最低点在底座上方')
        call('restart')
        s = call('interact', part='module')
        assert not s['accepted'] and s['blocker'] == 'knob' and not s['pathClear']
        s = call('interact', part='gate')
        assert not s['accepted'] and s['blocker'] == 'knob'
        assert part('module')['hitAtCenter'] == 'gate'
        call('restart')
        capture('locked')
        checks.append('锁定时模块被遮挡、抽取拒绝；盖板保留锁定')
        # 取消和无 move 的远距离松手均不能解锁。
        p = part('knob')['screen']
        pointer('begin', p); pointer('cancel', p); pointer('end', p)
        assert not state()['unlocking']
        pointer('begin', p); pointer('end', dict(x=p['x']+.08, y=p['y']))
        assert not state()['unlocking']
        before = view()
        pointer('begin', p)
        moved = dict(x=p['x']+.10, y=p['y']+.04)
        pointer('move', moved); pointer('end', moved)
        after = view()
        assert after['yaw'] != before['yaw']
        assert before['deviceRotation'] == after['deviceRotation']
        for old, new in zip(before['parts'], after['parts']): assert old['position'] == new['position']
        assert not state()['unlocking']
        click(button('reset_view'))
        checks.append('取消、远距离松手和起拖不误解锁；观察只移动相机')
        # 旋钮正常点击，退出期间盖板不能提前运动。
        s = click(part('knob')['screen'])
        assert s['accepted'] and s['unlocking'] and s['moves'] == 1
        time.sleep(.18)
        s = state()
        assert not s['unlocked'] and s['angleDegrees'] == 0
        capture('unlocking')
        # 运动中重开，锁定/姿态/进度全部复原。
        click(button('restart'))
        s = state()
        assert s['phase'] == 'locked' and s['unlockProgress'] == 0 and s['moves'] == 0
        assert abs(part('knob')['position']['x']-1.16)<.0001
        checks.append('玩家点击解锁；旋钮完全退出前盖板不动；旋钮退出中可重开')
        click(part('knob')['screen'])
        s = wait(lambda s: s['unlocked'] and s['angleDegrees'] < -4)
        assert s['angularVelocityDegrees'] < 0
        assert not s['settled']
        capture('swinging')
        s = call('interact', part='module')
        assert not s['accepted'] and s['blocker'] == 'gate'
        click(button('restart'))
        assert state()['phase'] == 'locked'
        checks.append('释放后真实推进重力矩摆动；运动过快时拒绝抽取；摆动中可重开')
        click(part('knob')['screen'])
        samples = []
        end = time.monotonic() + 12
        while True:
            s = state(); samples.append(s)
            assert -85.001 <= s['angleDegrees'] <= 0
            if s['settled']: break
            assert time.monotonic() < end
            time.sleep(.08)
        assert s['safeToExtract'] and s['pathClear']
        assert abs(s['angleDegrees'] - geometry['equilibriumDegrees']) < .01
        assert any(x['angularVelocityDegrees'] < -5 for x in samples)
        assert part('module')['hitAtCenter'] == 'module'
        click(button('guides')); assert view()['debugVisible']
        capture('structure-guides')
        click(button('guides')); assert not view()['debugVisible']
        capture('ready')
        s = click(part('module')['screen'])
        assert s['accepted'] and s['phase'] == 'extracting' and s['moduleHeld']
        time.sleep(.22);capture('extracting')
        click(button('restart'))
        assert state()['phase'] == 'locked' and state()['extractProgress'] == 0
        checks.append('阻尼收敛到重心平衡角、通路打开；结构显示可切换；抽取中重开复位')
        # 从头正常输入完整通关，不跳过阶段。
        click(part('knob')['screen']);wait(lambda s:s['settled'])
        s = click(part('module')['screen'])
        assert s['accepted'] and not s['won']
        s = wait(lambda s:s['won'])
        assert s['moves'] == 2 and s['extractProgress'] == 1
        assert abs(part('module')['position']['z'] - (.02 - geometry['exitDistance'])) < .0001
        capture('complete')
        assert not call('interact', part='module')['accepted']
        click(button('restart'));assert state()['phase'] == 'locked'
        checks.append('正常输入从开始到胜利、最终位移、重复操作拒绝与胜利后重开')
        # 场景入口往返：旧会话和样板均能启动。
        click(button('legacy'))
        end = time.monotonic()+10
        while True:
            old = json.loads(m.tool('unity_extension_catalog', extensionId='little-game.puzzle'))
            if old['extensions'][0]['available']: break
            assert time.monotonic()<end;time.sleep(.1)
        old_view = json.loads(m.tool('unity_extension_call', extensionId='little-game.puzzle', operation='get_view'))
        assert next(b for b in old_view['hud']['buttons'] if b['id']=='structure_sample')['visible']
        m.tool('unity_extension_call',extensionId='little-game.puzzle',operation='open_structure',allowWrite=True)
        time.sleep(.2)
        wait(lambda s:s['phase']=='locked')
        checks.append('样板与原版三关入口可正常往返')
        logs={}
        for category in ('Error','Exception','Assert','Warning'):
            logs[category]=m.tool('unity_get_console_logs',logType=category,maxEntries=15)
            assert logs[category].startswith('Console 日志 0 条'), logs
        success=True
        print(f'结构样板通过，{len(checks)}组关键检查，{len(records)}条请求。', flush=True)
    finally:
        (output/'structure-verification.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf-8')
        (output/'structure-verification-summary.json').write_text(json.dumps(dict(passed=success,checks=checks,requests=len(records),platform='Unity Editor / shared pointer input',mobileVerified=False),ensure_ascii=False,indent=2),encoding='utf-8')
        print(m.tool('unity_exit_play_mode'),flush=True)

if __name__ == '__main__': main()
