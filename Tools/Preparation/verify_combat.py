"""通过真实Unity MCP、正常玩法和玩家共用输入验证横向无限远征客户端。"""
import json
import shutil
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Mobile'))
import mobile_test as mobile

OUT = ROOT / 'Reports/Combat'
records, checks = [], []
ui_layouts = {}

def call(operation, **arguments):
    value = json.loads(mobile.mcp.tool('unity_extension_call', extensionId='little-game.combat', operation=operation, arguments=arguments, allowWrite=True))
    if operation not in ('get_state', 'get_view', 'get_events'):
        records.append({'operation': operation, 'arguments': arguments, 'result': value})
    return value

def state(): return call('get_state')
def action(operation, **arguments):
    result = call(operation, **arguments)
    assert result.get('accepted'), (operation, arguments, result.get('reason'))
    return result['state']
def view():
    value = call('get_view')
    assert value['skin']['missingSprites'] == 0, value['skin']
    buttons = [b for b in value['buttons'] if not value['modal'] or b['id'].startswith('confirm_')]
    for b in buttons:
        assert b['width'] > 0 and b['height'] > 0
        assert b['x'] - b['width'] / 2 >= -.001 and b['x'] + b['width'] / 2 <= 1.001, b
        assert b['y'] - b['height'] / 2 >= -.001 and b['y'] + b['height'] / 2 <= 1.001, b
        assert b['height'] * 960 >= 43, ('触屏高度不足', b)
    for i, a in enumerate(buttons):
        for b in buttons[i + 1:]:
            overlap_x = (a['width'] + b['width']) / 2 - abs(a['x'] - b['x'])
            overlap_y = (a['height'] + b['height']) / 2 - abs(a['y'] - b['y'])
            assert overlap_x < .001 or overlap_y < .001, ('按钮区域重叠', a, b)
    key = ','.join(sorted(b['id'] for b in buttons))
    ui_layouts[key] = {'skin': value['skin'], 'modal': value['modal'], 'buttons': buttons}
    return value
def pointer(phase, point): return call('pointer', phase=phase, x=point['x'], y=point['y'])
def click(point):
    pointer('begin', point)
    pointer('end', point)
    time.sleep(.08)
def button(name):
    return next(p for p in view()['buttons'] if p['id'] == name)
def capture(name):
    view()
    result = call('capture_view')
    path = Path(result['path'])
    deadline = time.monotonic() + 8
    while not path.exists():
        assert time.monotonic() < deadline, '截图未写入'
        time.sleep(.1)
    dst = OUT / f'{name}.png'
    try:
        shutil.copyfile(path, dst)
    except OSError:
        shutil.copyfile(path, OUT / f'{name}-{time.time_ns()}.png')
def wait(predicate, timeout=65):
    end = time.monotonic() + timeout
    while True:
        s = state()
        if predicate(s): return s
        assert time.monotonic() < end, s
        time.sleep(.15)
def wait_recruit_anim(timeout=4.0):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        if not view().get('recruitAnimating', False):
            return
        time.sleep(.08)
def arrange():
    for _ in range(12):
        s = state(); changed = False
        for u in sorted((u for u in s['units'] if u['lane'] < 0), key=lambda u: -u['star']):
            cur = state()
            deployed_units = [t for t in cur['units'] if t['lane'] >= 0]
            if len(deployed_units) < cur['maxDeployed']:
                occupied = {(t['lane'], t['row']) for t in deployed_units}
                free = next(((l, r) for r in range(3) for l in range(3) if (l, r) not in occupied), None)
                if free:
                    action('deploy', unitId=u['id'], lane=free[0], row=free[1]); changed = True
            else:
                weakest = min((t for t in deployed_units if t['star'] < u['star']), key=lambda t: t['star'], default=None)
                if weakest:
                    action('deploy', unitId=u['id'], lane=weakest['lane'], row=weakest['row']); changed = True
        s = state()
        if s['pending']:
            spare = next((u for u in s['units'] if u['lane'] < 0 and u['star'] == 1), None) or next((u for u in s['units'] if u['lane'] < 0), None)
            if spare: action('recycle', unitId=spare['id']); changed = True
        if not changed: break

def main():
    OUT.mkdir(exist_ok=True, parents=True)
    mobile.connect_editor()
    m = mobile.mcp
    assert '最近编译成功' in m.tool('unity_get_compilation_status')
    tool_names = [t['name'] for t in m.rpc('tools/list')['tools'] if t['name'].startswith('unity_combat_')]
    assert len(tool_names) == 13, tool_names
    m.tool('unity_exit_play_mode')
    m.tool('unity_run_menu_item', menuPath='Little Game/Combat/Open Combat')
    m.tool('unity_enter_play_mode')
    m.tool('unity_run_menu_item', menuPath='Little Game/Combat/Set Portrait Preview')
    time.sleep(.2)
    deadline = time.monotonic() + 30
    while True:
        catalog = json.loads(m.tool('unity_extension_catalog'))
        if any(e['id'] == 'little-game.combat' and e['available'] for e in catalog['extensions']): break
        assert time.monotonic() < deadline, catalog
        time.sleep(.2)
    assert catalog['projectPath'] == 'E:/Project/little-game/Assets'
    assert any(e['id'] == 'little-game.combat' and e['available'] for e in catalog['extensions'])
    assert not any(e['id'] in ('little-game.puzzle', 'little-game.structure') for e in catalog['extensions'])

    framework = view()['presentation']['framework']
    assert framework['aotStarted'] and framework['mainLaunched'] and framework['mvcInitialized'] and framework['battleModuleOpen']
    assert framework['qualityInitialized'] and all(p['manifestReady'] for p in framework['packages']), framework
    definitions = call('get_definitions')
    boss_waves = {wave['wave'] for wave in definitions['stage'] if wave['boss']}
    assert definitions['rules']['rvoActive'] is True and definitions['rules']['blockedWait'] is True, definitions
    assert call('get_navigation')['algorithm'] == 'unity_navmesh_agent_attack_slots'
    action('restart', seed=173)
    direct = json.loads(m.tool('unity_combat_get_state'))
    assert direct['wave'] == 1 and direct['phase'] == 'Preparation' and direct['deployedCount'] == 0 and len(direct['units']) == 0, direct
    assert not call('start_wave')['accepted'], '开局零单位不应允许直接开战'
    event_wait = json.loads(m.tool('unity_combat_wait_for_event', eventType='run_started', after=0, timeoutMs=500))
    assert event_wait['matched'], event_wait
    time.sleep(.25)
    capture('combat-preparation')
    camera = view()['presentation']
    assert camera['cameraMode'] == 'fixed-heading-horizontal-follow'
    assert camera['environment']['entrance'] == 'roadside-ruined-gate'
    empty_start, empty_end = {'x': .35, 'y': .55}, {'x': .73, 'y': .55}
    pointer('begin', empty_start); pointer('move', empty_end); pointer('end', empty_end)
    fixed = view()['presentation']
    assert fixed['cameraYaw'] == camera['cameraYaw'] == 0
    assert fixed['cameraX'] == camera['cameraX'] == fixed['cameraBaseX']
    assert view()['selected'] == 0
    checks.append('布阵空地横滑保持固定机位与选中状态通过')
    checks.append('开局零单位（全靠老虎机招募）与禁止空阵开战通过')

    # 1. 共用 pointer 取消/防误触；点击招募 -> 动画期间候补图标先隐藏，落地后才显现！
    p = button('spin'); before = state()['spins']
    pointer('begin', p); pointer('cancel', p); pointer('end', p)
    assert state()['spins'] == before
    pointer('begin', p); pointer('end', {'x': p['x'] + .2, 'y': p['y'] + .1})
    assert state()['spins'] == before
    click(p); assert state()['spins'] == before - 1
    mid_view = view()
    assert mid_view.get('recruitAnimating', False), '招募后应进入动画期'
    assert not any(b['id'].startswith('unit_') for b in mid_view['buttons']), '飞行动画结束前候补图标不应提前出现'
    wait_recruit_anim()
    after_fly_view = view()
    u1_btn = next((b for b in after_fly_view['buttons'] if b['id'].startswith('unit_')), None)
    assert u1_btn is not None, '飞行动画落地后候补图标应立即显现'
    u1_id = u1_btn['unitId']
    checks.append('招募动画落地前隐藏图标、抵达候补槽瞬间显现通过')

    # 再转两次获得弓手与雷法，等待动画落地
    click(button('spin')); wait_recruit_anim()
    click(button('spin')); wait_recruit_anim()
    assert len([b for b in view()['buttons'] if b['id'].startswith('unit_')]) == 3, '三转后候补区应完整显示3名单位'

    # 2. 双击候补军团卡片 -> 自动寻找合适空位上场
    p_res = button('unit_' + str(u1_id))
    pointer('begin', p_res); pointer('end', p_res); pointer('begin', p_res); pointer('end', p_res); time.sleep(.12)
    u1_state = next(u for u in state()['units'] if u['id'] == u1_id)
    assert u1_state['lane'] >= 0, ('双击候补卡片应自动上场', u1_state)
    checks.append('双击候补军团卡片自动寻位上场通过')

    # 3. Tap 即选中（仅 begin 即选中）、绝不弹出按钮面板；拖出白框外不变红并吸附最近合法点A（带剪影预览）
    u1_pt = next(t for t in view()['troops'] if t['id'] == u1_id)
    pointer('begin', u1_pt); time.sleep(.06)
    tap_view = view()
    assert tap_view['selected'] == u1_id
    tap_ids = {b['id'] for b in tap_view['buttons']}
    assert 'recall' not in tap_ids and 'merge' not in tap_ids and not any(i.startswith('branch_') for i in tap_ids)
    out_of_box = {'x': 0.84, 'y': 0.56}
    pointer('move', out_of_box); time.sleep(.10)
    capture('combat-clamp-silhouette-drag')
    pointer('end', out_of_box); time.sleep(.12)
    u1_after = next(u for u in state()['units'] if u['id'] == u1_id)
    box = state()['deployBox']
    assert abs(u1_after['homeX'] - box['maxX']) < 0.08
    checks.append('tap即选中、无按钮面板、拖出白框吸附点A(剪影预览)通过')
    edge = {'x': .055, 'y': .60}
    u1_pt = next(t for t in view()['troops'] if t['id'] == u1_id)
    pointer('begin', u1_pt); pointer('move', edge); pointer('cancel', edge); pointer('end', edge)
    assert next(u for u in state()['units'] if u['id'] == u1_id)['homeX'] == u1_after['homeX']
    pointer('begin', u1_pt); pointer('move', edge); pointer('end', edge)
    edge_unit = next(u for u in state()['units'] if u['id'] == u1_id)
    assert abs(edge_unit['homeX'] - box['minX']) < .15, edge_unit
    click({'x': .75, 'y': .55})
    assert view()['selected'] == 0
    edge_unit_screen = next(t for t in view()['troops'] if t['id'] == u1_id)
    pointer('begin', edge_unit_screen)
    assert view()['selected'] == u1_id
    pointer('end', edge_unit_screen)
    capture('combat-deploy-left-edge')
    checks.append('竖屏左边缘部署、取消和重新命中通过')

    # 把剩余 2 名候补也上阵，验证拖回候补召回
    arrange()
    u_to_recall = state()['units'][1]['id']
    pt_rec = next(t for t in view()['troops'] if t['id'] == u_to_recall)
    reserve_area = view()['reserveDropArea']
    pointer('begin', pt_rec); pointer('move', reserve_area); time.sleep(.08)
    pointer('end', reserve_area); time.sleep(.12)
    assert next(u for u in state()['units'] if u['id'] == u_to_recall)['lane'] == -1
    checks.append('拖动场上单位到候补区直接召回通过')

    # 全部上阵开战，验证战斗、血条从右到左、固定移速与RVO2避障
    arrange()
    action('set_speed', speed=1)
    action('start_wave')
    wait(lambda s: len(s['enemies']) > 0)
    # 等首批敌人进入清晰可见的战场，再走真实暂停按钮输入。
    deadline = time.monotonic() + 8
    while not any(.67 < p['x'] < .96 and .32 < p['y'] < .81 for p in view()['enemies']):
        assert time.monotonic() < deadline, '未观察到右侧敌人入场'
        time.sleep(.05)
    click(button('pause'))
    assert state()['paused']
    paused_view = view()
    a = state()['battleTime']; time.sleep(.3); assert state()['battleTime'] == a
    assert view()['presentation']['cameraX'] == paused_view['presentation']['cameraX']
    assert view()['presentation']['cameraYaw'] == 0
    capture('combat-enemy-entrance')
    action('pause', paused=False)
    checks.append('右侧敌军入场可见，暂停锁住相机平移与朝向通过')
    time.sleep(2.0)
    action('pause', paused=True); a = state()['battleTime']; time.sleep(.2); assert state()['battleTime'] == a
    capture('combat-fighting')
    assert not call('deploy', unitId=u1_id, lane=1, row=0)['accepted']
    action('pause', paused=False)
    wait(lambda s: s['phase'] != 'Fighting')
    time.sleep(1.3)
    returned = view()['presentation']
    assert abs(returned['cameraX'] - returned['cameraBaseX']) < .015, returned['cameraX']
    checks.append('真实战斗、固定移速A*导航、暂停/伤害统计及战中拒绝布阵通过')

    # 从开局 0 单位正常招募，打通配置中的全部波次。
    action('restart', seed=173); action('set_speed', speed=2)
    seen_boss = False; seen_reward = False; captures = set()
    end = time.monotonic() + 300
    while True:
        assert time.monotonic() < end, '远征超时'
        s = state()
        if s['phase'] in ('Won', 'Lost'): break
        if s['phase'] == 'Preparation':
            arrange()
            while state()['spins'] > 0:
                if state()['pending']: arrange()
                action('spin'); arrange()
            arrange(); s = state()
            if s['wave'] in boss_waves:
                seen_boss = True
                capture('combat-boss-preparation-' + str(s['wave']))
            action('start_wave')
        elif s['phase'] == 'Reward':
            seen_reward = True
            capture('combat-reward-' + str(s['wave']))
            action('choose_reward', reward='Reinforcements' if 'Reinforcements' not in s['rewards'] else 'WideStorm')
        elif s['phase'] == 'WaveResult':
            action('next_wave')
            if 'Reinforcements' in state()['rewards']: assert state()['spins'] == 4
        else:
            if s['wave'] in boss_waves and s['bossTelegraph']['lane'] >= 0 and s['wave'] not in captures:
                action('pause', paused=True)
                assert view()['presentation']['cameraX'] <= .85, 'boss telegraph leaves the 540x960 frame'
                capture('combat-boss-warning-' + str(s['wave'])); captures.add(s['wave'])
                action('pause', paused=False)
            time.sleep(.15)
    assert s['phase'] == 'Won' and s['maxDeployed'] == 9, s
    assert seen_boss and seen_reward
    assert captures & boss_waves, '未采集到首领预警'
    capture('combat-victory')
    checks.append(f'从零单位起手完整{s["totalWaves"]}波横向远征获胜通过')

    # 单兵团灭判负与重开
    action('restart', seed=173)
    action('spin'); action('spin')
    keep_archer = next(u['id'] for u in state()['units'] if u['kind'] == 'Archer')
    action('deploy', unitId=keep_archer, lane=1, row=1)
    action('set_speed', speed=2)
    end = time.monotonic() + 120
    while state()['phase'] != 'Lost':
        assert time.monotonic() < end
        s = state()
        if s['phase'] == 'Preparation': action('start_wave')
        elif s['phase'] == 'WaveResult': action('next_wave')
        elif s['phase'] == 'Reward': action('choose_reward', reward='LongFreeze')
        else: time.sleep(.15)
    assert state()['aliveTroops'] == 0
    capture('combat-defeat')
    action('restart', seed=173); assert state()['deployedCount'] == 0 and state()['wave'] == 1
    checks.append('单兵阵亡团灭判负及重开归零通过')

    console = {}
    for level in ('Error', 'Exception', 'Assert', 'Warning'):
        logs = m.tool('unity_get_console_logs', logType=level, maxEntries=25); console[level] = logs
        assert logs.startswith('Console 日志 0 条'), (level, logs)
    event_info = call('get_events', after=0)
    checks.append('UI换肤sprite完整、按钮边界/触屏高度/无重叠及Console零报错通过')
    result = {'framework': framework, 'checks': checks, 'nativeTools': tool_names, 'eventWait': event_wait, 'catalog': catalog, 'console': console, 'lastState': state(), 'events': event_info, 'records': records, 'uiLayouts': ui_layouts}
    (OUT / 'unity-verification.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'passed': checks}, ensure_ascii=False), flush=True)
    m.tool('unity_exit_play_mode')

if __name__ == '__main__':
    try: main()
    except Exception:
        OUT.mkdir(exist_ok=True, parents=True)
        (OUT / ('unity-verification-partial-' + str(time.time_ns()) + '.json')).write_text(json.dumps({'checks': checks, 'records': records}, ensure_ascii=False, indent=2), encoding='utf-8')
        raise
