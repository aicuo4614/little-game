"""开发包只读观测 + ADB 真实触屏入口回归；不注入玩法状态。"""
import argparse
import json
import time
from pathlib import Path
import mobile_test as mobile

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--serial')
    parser.add_argument('--level',type=int,choices=(1,2),default=1)
    args=parser.parse_args()
    adb=mobile.find_adb();serial=mobile.choose_device(adb,args.serial)
    command=[adb,'-s',serial]
    report=mobile.ROOT/'Reports'/'Mobile'/time.strftime('%Y%m%d-%H%M%S-touch')
    report.mkdir(parents=True,exist_ok=True)
    records=[]
    def observe():
        deadline=time.monotonic()+8
        while time.monotonic()<deadline:
            raw=mobile.run(command+['shell','cat',f'/sdcard/Android/data/{mobile.PACKAGE}/files/observation.json'],check=False)
            try:
                data=json.loads(raw)
                if not data['state']['animating']:
                    records.append(data);return data
            except (ValueError,KeyError): pass
            time.sleep(.2)
        raise RuntimeError('手机观测未就绪')
    def screenshot(name):
        (report/(name+'.png')).write_bytes(mobile.run(command+['exec-out','screencap','-p'],binary=True))
    def pixel(point,data):
        return [str(round(point['x']*data['view']['width'])),str(round((1-point['y'])*data['view']['height']))]
    def tap(point):
        data=observe();mobile.run(command+['shell','input','tap']+pixel(point,data));time.sleep(1)
        return observe()
    def swipe(point,target):
        data=observe();mobile.run(command+['shell','input','swipe']+pixel(point,data)+pixel(target,data)+['450']);time.sleep(1.1)
        return observe()
    def part(id,data=None):
        return next(p for p in (data or observe())['view']['parts'] if p['id']==id)
    def button(id):
        data=observe();point=next(p for p in data['view']['hud']['buttons'] if p['id']==id)
        assert point['visible'] and point['interactable'];return tap(point)
    def flip():return button('flip')
    def restart():return button('restart')
    def double(id):
        data=observe();point=part(id,data);xy=pixel(point,data)
        mobile.run(command+['shell','input','tap']+xy)
        mobile.run(command+['shell','input','tap']+xy)
        time.sleep(1.3);return observe()
    try:
        data=observe()
        if data['state'].get('level',1)!=args.level:button('switch_level')
        restart();data=observe();screenshot('front')
        assert data['view']['height']>data['view']['width']
        assert data['view']['interactionVersion']=='double-click-disassemble'
        assert data['view']['workspaceBounds']['clearance']>.01
        initial=data['state']['orientation'];center=data['view']['center']
        data=swipe(center,{'x':center['x']+.1,'y':center['y']+.04})
        assert data['state']['orientation']!=initial and data['state']['moves']==0
        restart();data=flip();assert data['state']['backVisible'];screenshot('back')
        for id in ('screw_a','cover'):
            point=part(id);tap(point);before=observe()['state']['orientation']
            data=swipe(point,{'x':point['x']+.08,'y':point['y']})
            assert data['state']['orientation']!=before and data['state']['moves']==0
            restart();flip()
        data=double('cover');assert not data['state']['accepted'] and data['state']['failedPart']=='cover'
        order=[p['id'] for p in observe()['state']['parts']]
        for index,id in enumerate(order):
            data=double(id);assert data['state']['moves']==index+1,data
            slot=next(s for s in data['view']['collection'] if s['part']==id);assert slot['filled']
            if id=='cover':screenshot('open')
        assert data['state']['phase']=='won';screenshot('complete')
        time.sleep(5);data=observe()
        (report/'performance.json').write_text(json.dumps(data['performance'],ensure_ascii=False,indent=2),encoding='utf-8')
        data=restart();assert data['state']['moves']==0 and not any(s['filled'] for s in data['view']['collection'])
        screenshot('restarted')
        logs=mobile.run(command+['logcat','-d','--pid='+mobile.run(command+['shell','pidof',mobile.PACKAGE]).strip(),'-t','500'])
        (report/'logcat.txt').write_text(logs,encoding='utf-8')
        errors=[line for line in logs.splitlines() if 'E Unity' in line or 'FATAL EXCEPTION' in line or 'Fatal signal' in line]
        assert not errors,errors
        print(f'真机触屏回归通过，报告：{report}')
    finally:
        (report/'observations.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf-8')

if __name__=='__main__':main()
