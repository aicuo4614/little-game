"""复用正常玩法/共用输入完整验收，额外采集FSM、速度及实际模型动作。不会调试刷兵或改数值。"""
import json
import time
from pathlib import Path
import verify_combat as combat

OUT = Path(__file__).resolve().parents[2] / 'Reports/Combat/astar-navigation-20261125'
samples = []
original_call = combat.call
last_sample_at = 0.0


def observed_call(operation, **arguments):
    global last_sample_at
    result = original_call(operation, **arguments)
    if operation == 'get_state' and result['phase'] == 'Fighting' and time.monotonic() - last_sample_at >= .15:
        last_sample_at = time.monotonic()
        actors = original_call('get_view')['presentation']['actors']
        samples.append({
            'wave': result['wave'], 'time': result['battleTime'], 'paused': result['paused'],
            'speed': result['speed'],
            'units': [{key: u[key] for key in ('id', 'kind', 'x', 'z', 'vx', 'vz', 'targetId', 'aiState', 'hp', 'navigation')}
                      for u in result['units'] if u['lane'] >= 0],
            'enemies': [{key: e[key] for key in ('id', 'x', 'z', 'vx', 'vz', 'targetId', 'aiState', 'hp', 'navigation')}
                        for e in result['enemies']],
            'actors': [{key: a[key] for key in ('id', 'x', 'z', 'facingYaw', 'action', 'actionTime',
                                               'movementSpeed', 'motionPaused', 'forceWalk', 'hitStopped')} for a in actors],
        })
    return result


if __name__ == '__main__':
    OUT.mkdir(parents=True, exist_ok=True)
    combat.OUT = OUT
    combat.call = observed_call
    passed = False
    try:
        combat.main()
        passed = True
    finally:
        stationary_walk = []
        frozen_walk = []
        waiting_samples = 0
        resumed_ids = set()
        previously_waiting = set()
        routed_samples = 0
        cross_frame_samples = []
        for sample in samples:
            actors = {a['id']: a for a in sample['actors']}
            for unit in sample['units']:
                nav = unit['navigation']
                if nav['status'] in ('Waiting', 'NoPath'):
                    waiting_samples += 1
                    previously_waiting.add(unit['id'])
                    assert unit['vx'] == 0 and unit['vz'] == 0, unit
                elif nav['status'] == 'Moving' and unit['id'] in previously_waiting:
                    resumed_ids.add(unit['id'])
                if nav['remainingCorners'] > 1: routed_samples += 1
                actor = actors.get(unit['id'])
                if unit['hp'] <= 0 or not actor or actor['action'] != 'Walk':
                    continue
                flag = {'wave': sample['wave'], 'time': sample['time'], 'id': unit['id']}
                # 动画与规则查询可能跨帧。用模型同次同步的速度判断，受击顿帧单独登记。
                if unit['vx'] ** 2 + unit['vz'] ** 2 < 1e-6 and actor['movementSpeed'] > .001:
                    cross_frame_samples.append(flag)
                if 0 <= actor['movementSpeed'] < .001 and not actor['forceWalk']:
                    if actor['hitStopped'] or actor['motionPaused']:
                        frozen_walk.append(flag)
                    else:
                        stationary_walk.append(flag)
        report = {'completed': passed and not stationary_walk, 'gameplayCompleted': passed, 'platform': 'WindowsEditor',
                  'sampleCount': len(samples), 'stationaryWalkSamples': stationary_walk,
                  'frozenWalkSamples': frozen_walk, 'crossFrameSamples': cross_frame_samples,
                  'waitingSamples': waiting_samples, 'resumedUnitIds': sorted(resumed_ids), 'multiCornerSamples': routed_samples,
                  'note': '采样观察，不能证明未采样帧完全无抖动；不是手机性能或真机验证。', 'samples': samples}
        (OUT / 'movement-observations.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
        print(json.dumps({'completed': passed and not stationary_walk, 'sampleCount': len(samples),
                          'stationaryWalkSamples': len(stationary_walk), 'frozenWalkSamples': len(frozen_walk),
                          'crossFrameSamples': len(cross_frame_samples), 'waitingSamples': waiting_samples, 'resumedUnitIds': sorted(resumed_ids), 'multiCornerSamples': routed_samples}, ensure_ascii=False), flush=True)
        if passed and stationary_walk:
            raise AssertionError('存在无暂停/受击停顿的原地走路样本，请检查movement-observations.json')
