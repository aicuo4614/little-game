"""采集维修台关键画面；通过正常玩法完成拆卸。"""
import json
import re
import shutil
import sys
import time
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Tools'/'Mobile'))
import mobile_test as mobile

def main():
    mobile.connect_editor()
    m=mobile.mcp
    assert '最近编译成功' in m.tool('unity_get_compilation_status')
    def call(operation,wait=True,expected=True,**arguments):
        state=json.loads(m.tool('unity_extension_call',extensionId='little-game.puzzle',operation=operation,arguments=arguments,allowWrite=True))
        if operation in ("disassemble","load_level"):assert state["accepted"]==expected,state
        deadline=time.monotonic()+8
        while wait and state.get('animating'):
            assert time.monotonic()<deadline
            time.sleep(.08)
            state=json.loads(m.tool('unity_extension_call',extensionId='little-game.puzzle',operation='get_state'))
        return state
    m.tool('unity_enter_play_mode')
    try:
        camera=int(re.search(r'instanceId\s*[:：]\s*(-?\d+)',m.tool('unity_find_game_object',name='Main Camera')).group(1))
        def capture(name):
            time.sleep(.2)
            result=json.loads(m.tool('unity_capture_camera',cameraInstanceId=camera,width=1080,height=1920))
            shutil.copyfile(result['path'],ROOT/'Reports'/('workbench-'+name+'.png'))
        def midflight(name):
            deadline=time.monotonic()+5
            while call('get_state',wait=False)['animationProgress']<.52:
                assert time.monotonic()<deadline
                time.sleep(.03)
            capture(name)
        capture('front')
        call('flip');capture('back')
        call('select',part='cover');capture('selected')
        call('disassemble',part='cover',expected=False);capture('blocked')
        time.sleep(.7)
        call('disassemble',part='screw_a');call('disassemble',part='screw_b')
        call('disassemble',part='cover',wait=False);capture('flight');midflight('transfer')
        call('get_state');capture('open')
        call('disassemble',part='cable');call('disassemble',part='battery')
        call('disassemble',part='board_screw');call('disassemble',part='board');capture('empty-chassis')
        call('disassemble',part='chassis',wait=False);midflight('last-flight')
        call('get_state');capture('complete')
        call('load_level',level=2);capture('computer-front')
        call('flip');capture('computer-back')
        for id in ('screw_a','screw_b','case_screw_c','cover'):call('disassemble',part=id)
        capture('computer-open')
        for id in ('cable','power_unit','gpu_screw','gpu','ram_clip','ram','cooler_screw','cooler','board_screw','board','chassis'):
            if id=='chassis':
                call('disassemble',part=id,wait=False);midflight('computer-last-flight');call('get_state')
            else:call('disassemble',part=id)
        capture('computer-complete')
        print('已采集两关关键画面：正面、背面、失败、外扩飞行、开盖和完成。')
    finally:
        print(m.tool('unity_exit_play_mode'))

if __name__=='__main__': main()
