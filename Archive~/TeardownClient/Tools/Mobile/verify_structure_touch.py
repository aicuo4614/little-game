"""通过 Android 实际触屏与只读观测验证结构样板，不注入玩法状态。"""
import argparse
import json
import time
from pathlib import Path
import mobile_test as mobile

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--serial')
    args=parser.parse_args()
    adb=mobile.find_adb();serial=mobile.choose_device(adb,args.serial)
    command=[adb,'-s',serial]
    report=mobile.ROOT/'Reports/Mobile'/time.strftime('%Y%m%d-%H%M%S-structure-touch')
    report.mkdir(parents=True,exist_ok=True)
    records=[]
    def observe():
        end=time.monotonic()+10
        while time.monotonic()<end:
            raw=mobile.run(command+['shell','cat',f'/sdcard/Android/data/{mobile.PACKAGE}/files/observation.json'],check=False)
            try:
                value=json.loads(raw)
                if value.get('sample')=='structure':
                    records.append(value);return value
            except ValueError: pass
            time.sleep(.2)
        raise RuntimeError('结构样板观测未就绪，请先运行 mobile_test.py 安装启动最新包')
    def point(id,button=False):
        data=observe();items=data['view']['hud']['buttons'] if button else data['view']['parts']
        item=next(p for p in items if p['id']==id)
        return (item if button else item['screen']),data['view']
    def tap(id,button=False):
        p,v=point(id,button)
        mobile.run(command+['shell','input','tap',str(round(p['x']*v['width'])),str(round((1-p['y'])*v['height']))])
        time.sleep(.65)
        return observe()
    def wait(predicate):
        end=time.monotonic()+15
        while time.monotonic()<end:
            data=observe()
            if predicate(data['state']):return data
            time.sleep(.2)
        raise RuntimeError('等待玩法状态超时')
    def capture(name):
        (report/(name+'.png')).write_bytes(mobile.run(command+['exec-out','screencap','-p'],binary=True))
    try:
        tap('restart',True);assert observe()['state']['phase']=='locked';capture('locked')
        # 从旋钮起拖只环绕相机，不解锁，也不改变装置姿态。
        p,v=point('knob');before=observe()['view']
        x=round(p['x']*v['width']);y=round((1-p['y'])*v['height'])
        mobile.run(command+['shell','input','swipe',str(x),str(y),str(x+round(v['width']*.1)),str(y+round(v['height']*.04)),'350'])
        time.sleep(.7);data=observe()
        assert not data['state']['unlocked'] and not data['state']['unlocking']
        assert data['view']['deviceRotation']==before['deviceRotation'] and data['view']['yaw']!=before['yaw']
        tap('reset_view',True);tap('knob')
        wait(lambda s:s['unlocked']);capture('swinging')
        wait(lambda s:s['settled']);capture('ready')
        assert observe()['state']['safeToExtract']
        tap('guides',True);assert observe()['view']['debugVisible'];capture('guides');tap('guides',True)
        tap('module');wait(lambda s:s['won']);capture('complete')
        tap('restart',True);assert observe()['state']['phase']=='locked'
        pid=mobile.run(command+['shell','pidof',mobile.PACKAGE]).strip()
        logs=mobile.run(command+['logcat','-d','--pid='+pid.split()[0],'-t','500'])
        (report/'logcat.txt').write_text(logs,encoding='utf-8')
        assert not any(x in logs for x in ('E Unity','FATAL EXCEPTION','Fatal signal'))
        (report/'result.json').write_text(json.dumps(dict(passed=True,serial=serial,platform='Android / ADB real touch',samplingNote='Development APK; readonly observation every 0.5s; not Douyin runtime'),ensure_ascii=False,indent=2),encoding='utf-8')
        print(f'结构样板真机触屏验证通过：{report}')
    finally:
        (report/'observations.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf-8')

if __name__=='__main__':main()
