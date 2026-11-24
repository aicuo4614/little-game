"""以正常招募/布阵打到第六波，核查单位呈现与暂停；不使用调试推进。"""
from pathlib import Path
import json
import sys
import time
import shutil

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Preparation'))
import verify_combat as combat


def main():
    combat.mobile.connect_editor()
    m = combat.mobile.mcp
    m.tool('unity_clear_console_logs')
    m.tool('unity_enter_play_mode')
    combat.action('restart', seed=173)
    combat.action('set_speed', speed=2)
    deadline = time.monotonic() + 150
    observations = {}
    pause_verified = False
    while time.monotonic() < deadline:
        s = combat.state()
        if s['phase'] == 'Preparation':
            combat.arrange()
            while combat.state()['spins'] > 0:
                combat.action('spin'); combat.arrange()
            combat.action('start_wave')
        elif s['phase'] == 'Reward':
            combat.action('choose_reward', reward='Reinforcements')
        elif s['phase'] == 'WaveResult':
            combat.action('next_wave')
        elif s['phase'] == 'Fighting':
            for actor in combat.view()['presentation']['actors']:
                if actor['active']:
                    observations.setdefault(actor['model'], []).append({'action': actor['action'], 'time': actor['actionTime'], 'pose': actor['upperArm']})
                    assert all(r['supported'] and all(shader == 'Roco/Character' for shader in r['shaders']) for r in actor['renderers'])
            if s['wave'] == 6 and s['battleTime'] > 1:
                combat.action('pause', paused=True)
                time.sleep(.15)
                before = combat.view()['presentation']['actors']; tick = combat.state()['battleTime']
                time.sleep(.3)
                after = combat.view()['presentation']['actors']
                assert combat.state()['battleTime'] == tick
                motion_before = [(a['id'], a['action'], a['actionTime']) for a in before]
                motion_after = [(a['id'], a['action'], a['actionTime']) for a in after]
                assert motion_before == motion_after, ('暂停后动作仍推进', motion_before, motion_after)
                pause_verified = True
                result = combat.call('capture_view')
                source = Path(result['path'])
                for _ in range(30):
                    if source.exists(): break
                    time.sleep(.05)
                shutil.copyfile(source, ROOT / 'Reports/Art/units-boss-final.png')
                combat.action('pause', paused=False)
                break
            time.sleep(.15)
        else:
            raise AssertionError('尚未到首领即结束：' + s['phase'])
    assert pause_verified, '未抵达正常第六波'
    console = {level: m.tool('unity_get_console_logs', logType=level, maxEntries=20) for level in ['Error', 'Warning', 'Exception', 'Assert']}
    assert all(text.startswith('Console 日志 0 条') for text in console.values()), console
    result = {'pauseAndResume': pause_verified, 'normalWave': combat.state()['wave'], 'observations': observations, 'console': console}
    (ROOT / 'Reports/Art/unit-final-verification.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    m.tool('unity_exit_play_mode')
    print('正常招募/部署至第六波、九类Shader检查、暂停冻结动作、恢复及Console检查通过。', flush=True)


if __name__ == '__main__':
    main()
