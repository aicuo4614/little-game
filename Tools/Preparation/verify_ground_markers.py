"""正常招募/选中/战斗回放，检查未选中单位的装饰圈已去除，并保留真实状态提示。"""
import json
import time
from pathlib import Path
import verify_combat as combat

OUT = Path(__file__).resolve().parents[2] / 'Reports/Combat/ground-markers-20261125'


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    combat.OUT = OUT
    combat.mobile.connect_editor()
    m = combat.mobile.mcp
    assert '最近编译成功' in m.tool('unity_get_compilation_status')
    m.tool('unity_exit_play_mode')
    m.tool('unity_run_menu_item', menuPath='Little Game/Combat/Open Combat')
    m.tool('unity_enter_play_mode')
    checkpoints = []

    def inspect(label):
        v = combat.view()
        actors = v['presentation']['actors']
        assert actors, label
        assert all(marker['name'] == 'StatusRing' for a in actors for marker in a['groundMarkers']), label
        checkpoints.append({'label': label, 'selected': v['selected'], 'actors': actors})
        combat.capture(label)
        return v

    try:
        combat.action('restart', seed=173)
        for _ in range(3):
            combat.click(combat.button('spin'))
            combat.wait_recruit_anim()
        combat.arrange()
        time.sleep(.15)
        v = inspect('unselected')
        assert v['selected'] == 0
        assert not any(marker['active'] for a in v['presentation']['actors'] for marker in a['groundMarkers'])
        unit = v['troops'][0]
        combat.click(unit)
        v = inspect('selected')
        assert v['selected'] == unit['id']
        combat.click(combat.button('start_wave'))
        combat.wait(lambda s: s['phase'] == 'Fighting' and s['enemies'])
        combat.action('aim', kind='Protect')
        combat.action('protect', lane=1)
        time.sleep(.3)
        v = inspect('fighting-protected')
        assert any(marker['active'] for a in v['presentation']['actors'] for marker in a['groundMarkers'])
        result = combat.wait(lambda s: s['phase'] != 'Fighting')
        assert result['phase'] == 'WaveResult', result['phase']
        time.sleep(.15)
        inspect('wave-result')
        console = {level: m.tool('unity_get_console_logs', logType=level, maxEntries=25)
                   for level in ('Error', 'Exception', 'Assert', 'Warning')}
        assert all(log.startswith('Console 日志 0 条') for log in console.values()), console
        report = {'passed': True, 'project': 'E:/Project/little-game', 'platform': 'WindowsEditor',
                  'gameplayChanged': False, 'console': console, 'result': result,
                  'checkpoints': checkpoints, 'note': '仅Unity表现验证，未重打APK。'}
        (OUT / 'verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
        print(json.dumps({'passed': True, 'checkpoints': [p['label'] for p in checkpoints], 'console': console}, ensure_ascii=False), flush=True)
    finally:
        m.tool('unity_exit_play_mode')


if __name__ == '__main__':
    main()
