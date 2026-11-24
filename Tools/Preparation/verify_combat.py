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
    shutil.copyfile(path, OUT / f'{name}.png')
def wait(predicate, timeout=65):
    end = time.monotonic() + timeout
    while True:
        s = state()
        if predicate(s): return s
        assert time.monotonic() < end, s
        time.sleep(.15)
def arrange():
    for _ in range(12):
        s = state(); changed = False
        groups = {}
        for u in s['units']: groups.setdefault((u['kind'],u['star']), []).append(u)
        for (kind, star), units in groups.items():
            units.sort(key=lambda u:(u['lane'] < 0,u['id']))
            if star < 3 and len(units) >= 3 and (sum(u['lane'] < 0 for u in units) >= 1 or len(units) >= 4):
                action('merge', unitId=units[0]['id'], otherId=units[-1]['id'], thirdId=units[-2]['id']); changed = True; break
        s = state()
        for u in s['units']:
            if u['star'] >= 2 and u['branch'] == 'None':
                branch = {'Shield':'Bastion','Archer':'Pierce','Storm':'StormPool','Frost':'DeepFreeze'}[u['kind']]
                action('choose_branch', unitId=u['id'], branch=branch); changed = True
        s = state()
        for u in sorted((u for u in s['units'] if u['lane'] < 0), key=lambda u:-u['star']):
            cur = state()
            deployed_units = [t for t in cur['units'] if t['lane'] >= 0]
            if len(deployed_units) < cur['maxDeployed']:
                occupied = {(t['lane'],t['row']) for t in deployed_units}
                free = next(((l,r) for r in range(3) for l in range(3) if (l,r) not in occupied), None)
                if free:
                    action('deploy', unitId=u['id'], lane=free[0], row=free[1]); changed = True
            else:
                weakest = min((t for t in deployed_units if t['star'] < u['star']), key=lambda t: t['star'], default=None)
                if weakest:
                    action('deploy', unitId=u['id'], lane=weakest['lane'], row=weakest['row']); changed = True
        s = state()
        if s['pending']:
            spare = next((u for u in s['units'] if u['lane'] < 0 and u['star'] == 1), None) or next((u for u in s['units'] if u['lane'] < 0), None)
            if spare: action('recycle',unitId=spare['id']); changed = True
        if not changed: break

def main():
    OUT.mkdir(exist_ok=True, parents=True)
    mobile.connect_editor()
    m = mobile.mcp
    assert '最近编译成功' in m.tool('unity_get_compilation_status')
    tool_names = [t['name'] for t in m.rpc('tools/list')['tools'] if t['name'].startswith('unity_combat_')]
    assert len(tool_names) == 16, tool_names
    m.tool('unity_exit_play_mode')
    m.tool('unity_run_menu_item', menuPath='Little Game/Combat/Open Combat')
    m.tool('unity_enter_play_mode')
    catalog = json.loads(m.tool('unity_extension_catalog'))
    assert catalog['projectPath'] == 'E:/Project/little-game/Assets'
    assert any(e['id']=='little-game.combat' and e['available'] for e in catalog['extensions'])
    assert not any(e['id'] in ('little-game.puzzle','little-game.structure') for e in catalog['extensions'])
    action('restart',seed=173)
    direct = json.loads(m.tool('unity_combat_get_state'))
    assert direct['wave'] == 1 and direct['phase'] == 'Preparation' and direct['layout'] == 'horizontal-free-deploy'
    event_wait = json.loads(m.tool('unity_combat_wait_for_event',eventType='run_started',after=0,timeoutMs=500))
    assert event_wait['matched'], event_wait
    time.sleep(.25)
    capture('combat-preparation')
    environment = view()['presentation']['environment']
    assert environment['missingModels'] == 0 and environment['missingMaterials'] == 0 and environment['shaderSupported']
    assert environment['colliders'] == 0 and environment['staticBatchedRenderers'] > 0 and environment['infiniteScrolling']
    assert environment['scrollOffset'] > 0, environment
    checks.append('横向无限滚动遗迹大道模型/Shader完整、无蓝格、静态合批、无碰撞、准备期持续行军滚动通过')
    checks.append('正确工程、编译、场景、Play和战斗工具发现；旧玩法扩展已移除')

    # 拖动场上单位：拖拽中显现白色整体布阵框；拖出白框外松手 -> 红Tip提示失败且保持原位
    archer = next(u for u in state()['units'] if u['kind'] == 'Archer' and u['lane'] >= 0)
    archer_pt = next(t for t in view()['troops'] if t['id'] == archer['id'])
    orig_x, orig_z = archer['homeX'], archer['homeZ']
    out_of_box = {'x': 0.84, 'y': 0.56}
    pointer('begin', archer_pt); pointer('move', out_of_box); time.sleep(.08)
    capture('combat-white-box-drag')
    pointer('end', out_of_box); time.sleep(.12)
    out_tip = view()
    assert not out_tip['modal'] and out_tip['tip']['active'] and out_tip['tip']['isError'], out_tip['tip']
    archer_after = next(u for u in state()['units'] if u['id'] == archer['id'])
    assert abs(archer_after['homeX'] - orig_x) < 0.05 and abs(archer_after['homeZ'] - orig_z) < 0.05, (archer_after, orig_x, orig_z)
    checks.append('拖拽显示整体白色布阵框，拖出白框外松手红Tip提示并失败回退通过')

    # 选中不可合成的弓手：头顶泡泡只显示「召回候补」，绝不显示无用的「三兵合成」按钮！
    archer_pt = next(t for t in view()['troops'] if t['id'] == archer['id'])
    click(archer_pt)
    nomerge_view = view()
    assert nomerge_view['selected'] == archer['id'], nomerge_view
    nomerge_ids = {b['id'] for b in nomerge_view['buttons']}
    assert 'recall' in nomerge_ids and 'merge' not in nomerge_ids, ('不可合成时不应出现三兵合成按钮', nomerge_ids)
    capture('combat-selected-nomerge')
    click({'x': 0.85, 'y': 0.65})
    assert view()['selected'] == 0
    checks.append('不可合成单位头顶泡泡动态隐藏三兵合成按钮，只显示紧凑召回按钮通过')

    # UI按钮取消、无move但远处松手均不触发。
    p = button('spin'); before = state()['spins']
    pointer('begin',p); pointer('cancel',p); pointer('end',p)
    assert state()['spins'] == before
    pointer('begin',p); pointer('end',{'x':p['x']+.2,'y':p['y']+.1})
    assert state()['spins'] == before
    click(p); assert state()['spins'] == before - 1
    checks.append('共用pointer取消和远处松手不误转盘；正常点击扣一次')

    # 候补仅支持拖动上阵：点击不弹面板；满员拖空格拒绝并弹红色悬浮Tip（不弹对话框）；拖已占格替换并自动选中显示含三兵合成的头顶泡泡。
    s=state(); reserve=next(u for u in s['units'] if u['lane']<0)
    assert s['maxDeployed'] == 6 and s['deployedCount'] == 6 and s['winRowMid'], s
    p=button('unit_'+str(reserve['id']))
    click(p)
    assert view()['selected'] == 0, '点击候补不应弹出选中面板'
    empty_cell=next(c for c in view()['cells'] if c['lane']==0 and c['row']==2)
    occupied_cell=next(c for c in view()['cells'] if c['lane']==0 and c['row']==1)
    pointer('begin',p); pointer('move',{'x': 0.5, 'y': 0.25}); time.sleep(.08)
    capture('combat-dragging')
    pointer('move',empty_cell); pointer('cancel',empty_cell); pointer('end',empty_cell)
    assert next(u for u in state()['units'] if u['id']==reserve['id'])['lane']==-1
    pointer('begin',p); pointer('move',empty_cell); pointer('end',empty_cell); time.sleep(.15)
    assert next(u for u in state()['units'] if u['id']==reserve['id'])['lane']==-1, '满人口拖入空格应拒绝并弹回'
    tip_view = view()
    assert not tip_view['modal'] and tip_view['tip']['active'] and tip_view['tip']['isError'], ('无效操作应使用红色悬浮Tip而非弹窗', tip_view['tip'])
    capture('combat-red-tip')
    pointer('begin',p); pointer('move',occupied_cell); pointer('end',occupied_cell); time.sleep(.15)
    assert next(u for u in state()['units'] if u['id']==reserve['id'])['row']==1
    sel_view = view()
    assert sel_view['selected'] == reserve['id'], sel_view
    sel_ids = {b['id'] for b in sel_view['buttons']}
    assert 'merge' in sel_ids and 'recall' in sel_ids, sel_ids
    capture('combat-selected')
    click({'x': 0.85, 'y': 0.65})
    assert view()['selected'] == 0
    checks.append('候补点击不弹面板、满员拖空格弹红色悬浮Tip+抛物线弹回、拖动替换自动选中动态头顶泡泡通过')

    # 真实合成与分支，拒绝重复素材。
    s=state(); shields=[u['id'] for u in s['units'] if u['kind']=='Shield']
    bad=call('merge',unitId=shields[0],otherId=shields[0],thirdId=shields[1]); assert not bad['accepted']
    action('merge',unitId=shields[0],otherId=shields[1],thirdId=shields[2])
    assert not call('start_wave')['accepted']
    action('choose_branch',unitId=shields[0],branch='Bastion')
    assert not call('choose_branch',unitId=shields[0],branch='WarCry')['accepted']
    checks.append('正常合成、素材唯一性、开战前必须选分支以及分支锁定通过')
    capture('combat-merged')

    # 重新开正常战斗，瞄准、暂停、护盾保护、一次消耗。
    action('restart',seed=173); action('set_speed',speed=1); action('start_wave')
    wait(lambda s: len(s['enemies'])>0)
    action('aim',kind='Protect'); a=state()['battleTime']; time.sleep(.3); assert state()['battleTime']==a
    action('cancel_aim'); action('aim',kind='Protect'); action('protect',lane=0)
    assert state()['tactics']==0 and not call('aim',kind='Focus')['accepted']
    time.sleep(.6)
    action('pause',paused=True); a=state()['battleTime']; time.sleep(.2); assert state()['battleTime']==a
    capture('combat-fighting')
    assert not call('deploy',unitId=1,lane=1,row=0)['accepted']
    action('pause',paused=False)
    wait(lambda s:s['phase']!='Fighting')
    checks.append('实际横向自动战斗、自由转向交锋、暂停、瞄准取消、护盾消耗一次及战中拒绝布阵通过')

    # 正常操作打完12波远征，不调数值、不跳波。
    action('restart',seed=173); action('set_speed',speed=2)
    seen_boss=False; seen_reward=False; captures=set()
    end=time.monotonic()+300
    while True:
        assert time.monotonic()<end,'远征超时'
        s=state()
        if s['phase'] in ('Won','Lost'): break
        if s['phase']=='Preparation':
            arrange()
            while state()['spins']>0:
                if state()['pending']: arrange()
                action('spin'); arrange()
            arrange(); s=state()
            if s['wave'] in (6,12):
                seen_boss=True
                capture('combat-boss-preparation-'+str(s['wave']))
            action('start_wave')
        elif s['phase']=='Reward':
            seen_reward=True
            capture('combat-reward-'+str(s['wave']))
            action('choose_reward',reward='Reinforcements' if 'Reinforcements' not in s['rewards'] else 'WideStorm')
        elif s['phase']=='WaveResult':
            action('next_wave')
            if 'Reinforcements' in state()['rewards']: assert state()['spins']==4
        else:
            if s['wave'] in (6,12) and s['bossTelegraph']['lane']>=0 and s['wave'] not in captures:
                action('pause',paused=True); capture('combat-boss-warning-'+str(s['wave'])); captures.add(s['wave'])
                action('pause',paused=False)
                if s['tactics']>0: action('aim',kind='Protect'); action('protect',lane=s['bossTelegraph']['lane'])
            time.sleep(.15)
    assert s['phase']=='Won' and s['maxDeployed']==9, s
    assert seen_boss and seen_reward
    capture('combat-victory')
    checks.append('不跳波完整12波横向远征获胜；人口自然升至9、奖励唯一、每波4转及首领横向预警通过')

    # 无城墙模式下单弱兵防守团灭失败，再正常重开。
    action('restart',seed=173)
    s0 = state()
    keep_archer = next(u['id'] for u in s0['units'] if u['kind'] == 'Archer')
    for u in s0['units']:
        if u['id'] != keep_archer: action('recall',unitId=u['id'])
    action('set_speed',speed=2)
    end=time.monotonic()+120
    while state()['phase']!='Lost':
        assert time.monotonic()<end
        s=state()
        if s['phase']=='Preparation': action('start_wave')
        elif s['phase']=='WaveResult': action('next_wave')
        elif s['phase']=='Reward': action('choose_reward',reward='LongFreeze')
        else: time.sleep(.15)
    assert state()['aliveTroops'] == 0
    capture('combat-defeat')
    action('restart',seed=173); assert state()['aliveTroops']==6 and state()['wave']==1
    checks.append('无城墙模式下单兵阵亡团灭判负及重开复位通过')

    console={}
    for level in ('Error','Exception','Assert','Warning'):
        logs=m.tool('unity_get_console_logs',logType=level,maxEntries=25);console[level]=logs
        assert logs.startswith('Console 日志 0 条'),(level,logs)
    event_info=call('get_events',after=0)
    checks.append('Fantasy RPG换肤各阶段sprite完整、按钮边界/触屏高度/无重叠及共用输入通过')
    result={'checks':checks,'nativeTools':tool_names,'eventWait':event_wait,'catalog':catalog,'console':console,'lastState':state(),'events':event_info,'records':records,'uiLayouts':ui_layouts}
    (OUT/'unity-verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'passed':checks},ensure_ascii=False),flush=True)
    m.tool('unity_exit_play_mode')

if __name__=='__main__':
    try: main()
    except Exception:
        OUT.mkdir(exist_ok=True,parents=True)
        (OUT/'unity-verification-partial.json').write_text(json.dumps({'checks':checks,'records':records},ensure_ascii=False,indent=2),encoding='utf-8')
        raise
