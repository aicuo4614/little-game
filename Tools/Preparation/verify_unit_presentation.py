"""只读观察正常玩法回放，验证新角色、Shader和动作确实在运行。"""
from pathlib import Path
import json
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Tools/Mobile'))
import mobile_test as mobile


def main():
    mobile.connect_editor()
    models = {}
    captures = set()
    deadline = time.monotonic() + 210
    expected = {'Unit_' + role for role in ['Shield', 'Archer', 'Storm', 'Frost', 'Grunt', 'Runner', 'Armored', 'Healer', 'Giant']}
    output = ROOT / 'Reports/Art/unit-runtime-verification.json'
    while time.monotonic() < deadline:
        try:
            view = json.loads(mobile.mcp.tool('unity_extension_call', extensionId='little-game.combat', operation='get_view', arguments={}, allowWrite=True))
        except RuntimeError:
            break
        for actor in view['presentation']['actors']:
            if not actor['active']:
                continue
            entry = models.setdefault(actor['model'], {'actions': [], 'poses': [], 'times': [], 'materialsSupported': True})
            if actor['action'] not in entry['actions']:
                entry['actions'].append(actor['action'])
            pose = actor['upperArm']
            if pose not in entry['poses'] and len(entry['poses']) < 5:
                entry['poses'].append(pose)
            if len(entry['times']) < 20:
                entry['times'].append(actor['actionTime'])
            for renderer in actor['renderers']:
                entry['materialsSupported'] &= renderer['supported'] and all(s == 'Roco/Character' for s in renderer['shaders'])
        interesting = {a['model'] for a in view['presentation']['actors'] if a['active']} & {'Unit_Giant', 'Unit_Runner', 'Unit_Frost', 'Unit_Healer'}
        if interesting - captures:
            capture = json.loads(mobile.mcp.tool('unity_extension_call', extensionId='little-game.combat', operation='capture_view', arguments={}, allowWrite=True))
            source = Path(capture['path'])
            for _ in range(20):
                if source.exists(): break
                time.sleep(.05)
            import shutil
            name = sorted(interesting - captures)[0]
            shutil.copyfile(source, ROOT / ('Reports/Art/normal-play-' + name + '.png'))
            captures |= interesting
        result = {'scope': '正常规则回放期间只读采样；未生成敌人、跳波或改数值', 'models': models, 'missingModels': sorted(expected - models.keys()), 'captures': sorted(captures)}
        output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
        if not result['missingModels'] and all(len(v['poses']) >= 2 for v in models.values()):
            break
        time.sleep(.35)
    print(json.dumps({'models': sorted(models), 'missingModels': sorted(expected - models.keys())}, ensure_ascii=False), flush=True)
    assert expected <= models.keys(), '正常回放未观察到全部九类角色'
    assert all(v['materialsSupported'] for v in models.values()), '单位材质缺失或Shader不可用'


if __name__ == '__main__':
    main()
