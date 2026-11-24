"""玩家双指入口、通用命中下限、遮挡与合成画面回归。"""
import argparse
import json
import shutil
import sys
import time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools'/'Mobile'))
import mobile_test as mobile

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir',type=Path,default=ROOT/'Reports')
    output=parser.parse_args().output_dir
    output.mkdir(parents=True,exist_ok=True)
    mobile.connect_editor();m=mobile.mcp;records=[]
    def call(op,**args):
        r=json.loads(m.tool('unity_extension_call',extensionId='little-game.puzzle',operation=op,arguments=args,allowWrite=True));records.append(dict(operation=op,arguments=args,result=r));return r
    def view():time.sleep(.04);return call('get_view')
    def part(id):return next(p for p in view()['parts'] if p['id']==id)
    def pointer(phase,p):return call('pointer',phase=phase,x=p['x'],y=p['y'])
    def click(p):pointer('begin',p);return pointer('end',p)
    def pinch(phase,a,b):return call('pinch',phase=phase,x1=a[0],y1=a[1],x2=b[0],y2=b[1])
    def zoom(factor,center=None):
        if center is None:center=view()['center']['y']
        pinch('begin',(.43,center),(.57,center))
        s=pinch('move',(.5-.07*factor,center),(.5+.07*factor,center));pinch('end',(.3,center),(.7,center));view();return s
    def capture(name):
        v=view()
        r=call('capture_view',width=v['width'],height=v['height']);shutil.copyfile(r['path'],output/('inspection-'+name+'.png'))
    def wait():
        deadline=time.monotonic()+6
        while call('get_state')['animating']:
            assert time.monotonic()<deadline;time.sleep(.06)
        view()
    try:
        assert '最近编译成功' in m.tool('unity_get_compilation_status')
        m.tool('unity_enter_play_mode');call('load_level',level=1);call('flip')
        original=call('get_state');v=view();baseline=v['collection'];orientation=original['orientation']
        assert all(min(p['target']['modelSize'].values())>=.69999 for p in v['parts'])
        def layout(v):
            w,t=v['workspace'],v['tray']
            assert t['footerTop']<w['yMin']<w['yMax']<t['bottom']<t['top']<t['headerBottom']
            assert all(t['bottom']<slot['center']['y']<t['top'] for slot in v['collection'])
        layout(v)
        capture('back-baseline')
        assert abs(zoom(1.6)['zoom']-1.6)<.001
        s=call('get_state');assert s['moves']==0 and s['orientation']==orientation
        v=view();assert v['workspaceBounds']['visibleClearance']>.01
        for left,right in zip(baseline,v['collection']):assert left['center']==right['center'] and left['displayScale']==right['displayScale']
        capture('zoom-1_6')
        assert zoom(4)['zoom']==2.4
        capture('zoom-max')
        assert zoom(.2)['zoom']==1
        # 双指移动焦点，不旋转；取消后缩放保持，不能拼成双击。
        p=part('screw_a');click(p)
        pinch('begin',(.4,.43),(.6,.43));pinch('move',(.35,.46),(.65,.46))
        s=call('get_state');assert s['zoom']>1 and abs(s['pan']['y'])>0
        pinch('cancel',(.35,.46),(.65,.46));s=click(part('screw_a'));assert s['moves']==0
        call('restart');call('flip');view()
        # 在原螺丝网格外、扩大范围内轻点与双击，必须仍命中该螺丝。
        p=part('screw_a');rect=p['target']['rect'];offset=dict(x=p['x']+(rect['xMax']-rect['xMin'])*.36,y=p['y'])
        s=click(offset);assert s['selected']=='screw_a' and s['moves']==0,s
        assert view()['hit']['method']=='minimum_target'
        s=click(offset);assert s['accepted'] and s['animating'] and s['moves']==1,s
        wait()
        # 回收期间仍可用玩家的起拖与双指入口，世界中的其余部件和回收位保持固定。
        assert zoom(1.5)['zoom']>1.4
        p=part('screw_b');click(p);s=click(p);assert s['animating']
        v=view();attached=next(p for p in v['parts'] if p['id']=='cover')
        cameraRotation=v['inspectionCameraRotation'];rootRotation=v['rootRotation']
        start=v['center']
        pointer('begin',start)
        s=pointer('move',dict(x=start['x']+.11,y=start['y']+.06))
        assert s['animating'] and s['orientation']!=orientation and s['moves']==2
        pointer('cancel',start)
        z=s['zoom'];pinch('begin',(.4,.43),(.6,.43));s=pinch('move',(.36,.43),(.64,.43))
        assert s['accepted'] and s['animating'] and s['zoom']>z and s['moves']==2,s
        pinch('end',(.36,.43),(.64,.43))
        v=view()
        assert v['inspectionCameraRotation']==cameraRotation and v['rootRotation']!=rootRotation
        assert v['rotationTarget']=='item_root'
        observed=next(p for p in v['parts'] if p['id']=='cover')
        assert max(abs(attached['worldScale'][k]-observed['worldScale'][k]) for k in ('x','y','z'))<.00001
        assert attached['worldPosition']!=observed['worldPosition']
        assert v['collection'][0]['center']==baseline[0]['center']
        capture('orbit-and-zoom-during-recovery')
        wait()
        slot=next(x for x in view()['collection'] if x['part']=='screw_b');p=part('screw_b')
        assert slot['filled'] and max(abs(p['visualCenter'][k]-slot['center'][k]) for k in ('x','y'))<.0002
        capture('recovered-zoom')
        call('load_level',level=2);call('flip');view()
        layout(view())
        assert call('get_state')['zoom']==1
        # 内部代理不能穿过侧板；有扩大命中范围也不能拆到隐藏内存。
        s=click(part('ram'));assert s['selected']!='ram' and s['moves']==0
        for id in ('screw_a','screw_b','case_screw_c','cover'):
            assert call('disassemble',part=id)['accepted'];wait()
        # 相邻螺丝重叠时保留真实命中，不能被相邻扩大代理抢走。
        for id in ('board_screw','cooler_screw'):
            s=click(part(id));assert s['selected']==id,s
            time.sleep(.4)
        assert zoom(1.7,center=.39)['zoom']>1.6
        capture('computer-zoom')
        assert zoom(2,center=.39)['zoom']==2.4
        for id in ('cable','power_unit','gpu_screw','gpu','ram_clip','ram','cooler_screw','cooler','board_screw','board','chassis'):
            s=call('disassemble',part=id);assert s['accepted'] and s['animating'],(id,s)
            if id=='chassis':
                deadline=time.monotonic()+5
                while call('get_state')['animationProgress']<.55:
                    assert time.monotonic()<deadline;time.sleep(.035)
                capture('max-zoom-chassis-flight')
            wait()
        assert call('get_state')['phase']=='won';capture('computer-complete')
        call('restart');v=view();assert call('get_state')['zoom']==1 and call('get_state')['pan']==dict(x=0,y=0)
        s=call('load_level',level=3);assert s['device']=='红色行李箱' and s['backVisible']
        v=view();layout(v);capture('suitcase-closed')
        assert not call('disassemble',part='cover')['accepted']
        p=part('latch');click(p);assert click(p)['animating']
        wait()
        p=part('cover');click(p);assert click(p)['animating']
        wait();capture('suitcase-open')
        assert not call('disassemble',part='divider')['accepted']
        assert not call('disassemble',part='photo')['accepted']
        for id in ('pouch','scarf','bra','divider','photo','chassis'):
            p=part(id)
            assert p['target']['visible'],id
            click(p);s=click(p);assert s['animating'] and s['animationPart']==id,(id,s)
            wait()
            if id=='scarf':capture('suitcase-personal-items')
            v=view();slot=next(x for x in v['collection'] if x['part']==id)
            p=next(x for x in v['parts'] if x['id']==id)
            assert slot['filled'] and max(abs(p['visualCenter'][k]-slot['center'][k]) for k in ('x','y'))<.0002
        assert call('get_state')['phase']=='won';capture('suitcase-complete')
        assert call('restart')['recoveredCount']==0
        for category in ('Error','Exception','Assert','Warning'):
            assert m.tool('unity_get_console_logs',logType=category).startswith('Console 日志 0 条'),category
        print(f'双指缩放/移动、边界、取消、碰撞命中、遮挡、动画期间旋转缩放、三关布局与行李箱双击通关通过，共{len(records)}条请求。',flush=True)
    finally:
        (output/'inspection-verification.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf-8')
        print(m.tool('unity_exit_play_mode'))
if __name__=='__main__':main()
