"""通过玩家共用pointer入口验证双击、旋转、失败反馈和回收位。"""
import json
import sys
import time
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools'/'Mobile'))
import mobile_test as mobile

def main():
    mobile.connect_editor();m=mobile.mcp
    assert '最近编译成功' in m.tool('unity_get_compilation_status')
    events=[]
    def call(operation,**arguments):
        result=json.loads(m.tool('unity_extension_call',extensionId='little-game.puzzle',operation=operation,arguments=arguments,allowWrite=True))
        events.append(dict(operation=operation,arguments=arguments,result=result));return result
    def view():
        time.sleep(.035);return call('get_view')
    def point(id):return next(p for p in view()['parts'] if p['id']==id)
    def pointer(phase,p):return call('pointer',phase=phase,x=p['x'],y=p['y'])
    def click(p):pointer('begin',p);return pointer('end',p)
    def double(id):
        p=point(id);click(p);return click(p)
    def wait():
        deadline=time.monotonic()+5
        while call('get_state')['animating']:
            assert time.monotonic()<deadline;time.sleep(.06)
        time.sleep(.06)
    def drag(p):
        q=dict(x=p['x']+.07,y=p['y']+.04)
        pointer('begin',p);pointer('move',q);return pointer('end',q)
    def reset():call('restart');call('flip');time.sleep(.06)
    try:
        m.tool('unity_enter_play_mode');reset()
        v=view();assert v['interactionVersion']=='double-click-disassemble'
        assert v['workspaceBounds']['clearance']>.01
        assert 'pullVector' not in v and 'previewLift' not in v
        order=['screw_a','screw_b','cover','cable','battery','board_screw','board','chassis']
        assert [s['part'] for s in v['collection']]==order
        assert all(v['collection'][i]['x']<v['collection'][i+1]['x'] for i in range(7))
        # 单击螺丝只选中，并实际改变发光强度。
        s=click(point('screw_a'));assert s['moves']==0 and s['selected']=='screw_a' and s['screwAAttached']
        a=point('screw_a')['emission'];time.sleep(.18);b=point('screw_a')['emission'];assert a!=b and b['g']>b['r']
        # 慢速两次点击不拆卸。
        time.sleep(.4);s=click(point('screw_a'));assert s['moves']==0
        # 换部件不能拼成双击。
        s=click(point('screw_b'));assert s['moves']==0 and s['selected']=='screw_b'
        # 所有部件上起拖只旋转，取消不拆卸，背景拖动忽略。
        for id in ('screw_a','cover'):
            reset();p=point(id);click(p);before=call('get_state')['orientation'];s=drag(p)
            assert s['moves']==0 and s['orientation']!=before
        reset();p=point('cover');click(p);pointer('begin',p);pointer('cancel',p)
        s=click(p);assert s['moves']==0 and s['lastAction']=='select'
        before=call('get_state')['orientation'];drag(dict(x=.9,y=.5));assert call('get_state')['orientation']==before
        # 双击失败：记录被操作件和阻挡件，实际红闪并抖动，结束后回到装配位。
        reset();origin=point('cover')['worldPosition'];s=double('cover')
        assert not s['accepted'] and s['failedPart']=='cover' and s['blocker']=='screw_a' and s['failureSequence']==1
        v=view();p=next(p for p in v['parts'] if p['id']=='cover')
        assert v['feedback']['failureActive'] and p['emission']['r']>p['emission']['g']*3
        assert p['worldPosition']!=origin
        time.sleep(.7);p=point('cover');assert all(abs(p['worldPosition'][k]-origin[k])<.0001 for k in origin)
        # 正常双击完整回收；动画中不允许继续拆卸，落位才变绿。
        reset()
        for index,id in enumerate(order):
            s=double(id);assert s['accepted'] and s['animating'] and s['moves']==index+1,s
            if id=='cover':
                early=view();slot=next(x for x in early['collection'] if x['part']==id)
                if call('get_state')['animating']:assert not slot['filled']
                denied=call('disassemble',part='cable');assert not denied['accepted']
            wait();v=view();slot=next(x for x in v['collection'] if x['part']==id);p=next(x for x in v['parts'] if x['id']==id)
            assert slot['filled'] and p['parked']
            assert all(abs(p['worldPosition'][k]-slot['worldPosition'][k])<.0001 for k in ('x','y','z'))
            if id=='cover':
                parked=p['worldPosition'];call('rotate',delta=20);assert point('cover')['worldPosition']==parked;call('rotate',delta=-20)
        assert call('get_state')['phase']=='won'
        call('restart');v=view();assert not any(s['filled'] for s in v['collection']) and not any(p['parked'] for p in v['parts'])
        assert call('get_state')['moves']==0
        for level in ('Error','Exception','Assert','Warning'):
            assert m.tool('unity_get_console_logs',logType=level).startswith('Console 日志 0 条'),level
        print(f'双击输入、选中呼吸、失败红闪抖动、8格顺序归位、完整回收及重开通过，共{len(events)}条回放请求。')
    finally:
        (ROOT/'Reports'/'double-click-verification.json').write_text(json.dumps(events,ensure_ascii=False,indent=2),encoding='utf-8')
        print(m.tool('unity_exit_play_mode'))

if __name__=='__main__':main()
