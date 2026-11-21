"""两关正常输入、依赖、回收动画安全平面和格子中心对齐验证。"""
import json
import sys
import time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools'/'Mobile'))
import mobile_test as mobile

def main():
    mobile.connect_editor();m=mobile.mcp;events=[];stages=set()
    def call(op,**args):
        result=json.loads(m.tool('unity_extension_call',extensionId='little-game.puzzle',operation=op,arguments=args,allowWrite=True))
        events.append(dict(operation=op,arguments=args,result=result));return result
    def view():time.sleep(.03);return call('get_view')
    def double(id):
        p=next(p for p in view()['parts'] if p['id']==id)
        for _ in range(2):
            call('pointer',phase='begin',x=p['x'],y=p['y']);state=call('pointer',phase='end',x=p['x'],y=p['y'])
        return state
    def wait(id):
        deadline=time.monotonic()+7
        while True:
            s=call('get_state');v=call('get_view');p=next(p for p in v['parts'] if p['id']==id);stage=p['motion']['stage'];stages.add(stage)
            if stage=='clear_body':assert p['motion']['outwardClearance']>.1,p
            if stage=='transfer':assert p['motion']['frontClearance']>.25,p
            assert time.monotonic()<deadline
            if not s['animating']:break
            time.sleep(.035)
        time.sleep(.06);v=view();p=next(p for p in v['parts'] if p['id']==id);slot=next(p for p in v['collection'] if p['part']==id)
        assert slot['filled'] and p['motion']['stage']=='parked'
        assert abs(p['visualCenter']['x']-slot['center']['x'])<.0002,(p,slot)
        assert abs(p['visualCenter']['y']-slot['center']['y'])<.0002,(p,slot)
    try:
        assert '最近编译成功' in m.tool('unity_get_compilation_status')
        m.tool('unity_enter_play_mode');call('load_level',level=1);call('flip')
        assert not call('next_level')['accepted']
        for id in ('screw_a','screw_b','cover','cable','battery','board_screw','board','chassis'):
            s=call('disassemble',part=id);assert s['accepted']
            if id=='chassis':assert s['phase']=='animating' and s['recoveredCount']==7
            wait(id)
        assert call('get_state')['phase']=='won';s=call('next_level');assert s['level']==2 and s['totalParts']==15
        print('第一关完成、落位中心及正常进入第二关通过。',flush=True)
        v=view();assert len(v['parts'])==15 and len(v['collection'])==15
        assert v['workspaceBounds']['clearance']>.015,v['workspaceBounds']
        for row in (0,1):
            slots=[s for s in v['collection'] if s['row']==row]
            assert len(slots)==(8 if row==0 else 7)
            assert max(s['center']['y'] for s in slots)-min(s['center']['y'] for s in slots)<.00001
            assert all(slots[i]['center']['x']<slots[i+1]['center']['x'] for i in range(len(slots)-1))
        assert not call('load_level',level=3)['accepted'];assert call('get_state')['level']==2
        call('flip');s=double('cover');assert not s['accepted'] and s['blocker']=='screw_a'
        time.sleep(.7)
        for id in ('screw_b','screw_a','case_screw_c','cover'):
            s=double(id);assert s['accepted'] and s['animating'],s
            if id=='cover':
                before=s['orientation'];r=call('rotate',delta=90);assert not r['accepted'] and r['orientation']==before
                assert not call('load_level',level=1)['accepted']
            wait(id)
        print('第二关双击开箱、动画中锁定旋转与关卡选择通过。',flush=True)
        for id,blocker in [('power_unit','cable'),('gpu','cable'),('ram','ram_clip'),('cooler','cable'),('board','power_unit')]:
            s=call('disassemble',part=id);assert not s['accepted'] and s['blocker']==blocker,(id,s)
        time.sleep(.7)
        for id in ('cable','power_unit','gpu_screw','gpu','ram_clip','ram','cooler_screw','cooler','board_screw','board','chassis'):
            s=double(id);assert s['accepted'] and s['animating'],(id,s)
            if id=='chassis':
                assert s['phase']=='animating' and s['recoveredCount']==14,s
                v=view();slot=next(p for p in v['collection'] if p['part']=='chassis');assert not slot['filled']
            wait(id)
        s=call('get_state');assert s['phase']=='won' and s['moves']==15 and s['recoveredCount']==15
        assert not call('next_level')['accepted']
        call('restart');v=view();assert call('get_state')['level']==2 and not any(s['filled'] for s in v['collection'])
        call('load_level',level=1);assert len(view()['collection'])==8
        assert {'release','clear_body','transfer','settle','parked'}<=stages,stages
        for category in ('Error','Exception','Assert','Warning'):
            assert m.tool('unity_get_console_logs',logType=category).startswith('Console 日志 0 条'),category
        print(f'两关、15件依赖、玩家双击入口、分段安全路径、中心对齐和重开通过，共{len(events)}条请求。',flush=True)
    finally:
        (ROOT/'Reports/levels-verification.json').write_text(json.dumps(events,ensure_ascii=False,indent=2),encoding='utf-8')
        print(m.tool('unity_exit_play_mode'))
if __name__=='__main__':main()
