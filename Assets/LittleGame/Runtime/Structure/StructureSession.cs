using System;
using System.Collections.Generic;

namespace LittleGame.Structure
{
    // 约束运动规则：重力矩驱动的单自由度摆，不依赖帧率或 Unity 刚体求解器。
    public sealed class StructureSession
    {
        public float Angle {get;private set;}
        public float AngularVelocity {get;private set;}
        public float UnlockProgress {get;private set;}
        public float ExtractProgress {get;private set;}
        public bool Unlocking {get;private set;}
        public bool Unlocked {get;private set;}
        public bool Extracting {get;private set;}
        public bool Won {get;private set;}
        public bool Accepted {get;private set;}
        public int Revision {get;private set;}
        public int Moves {get;private set;}
        public string Message {get;private set;}="旋开右上旋钮，观察盖板怎样下摆";
        public string Blocker {get;private set;}="";
        public string Selected {get;private set;}="";
        public string LastAction {get;private set;}="";
        public bool Settled => Unlocked && Math.Abs(Angle-StructureDefinition.Equilibrium)<.0087f && Math.Abs(AngularVelocity)<.0087f;
        public bool PathClear => StructureDefinition.ExitClear(Angle);
        public bool SafeToExtract => Unlocked && PathClear && Math.Abs(AngularVelocity)<.14f;
        private float accumulator;
        private readonly Queue<object> events=new Queue<object>();
        private long sequence;
        private void Event(string kind){events.Enqueue(new {sequence=++sequence,kind,angle=Angle});while(events.Count>24)events.Dequeue();}
        public void Command(string action,string part="")
        {
            Revision++;LastAction=action;Accepted=false;Blocker="";
            if(action=="restart")
            {
                Angle=AngularVelocity=UnlockProgress=ExtractProgress=accumulator=0;
                Unlocking=Unlocked=Extracting=Won=false;Moves=0;Selected="";
                Message="旋开右上旋钮，观察盖板怎样下摆";Accepted=true;Event("restart");return;
            }
            if(action=="clear_selection"){Selected="";Accepted=true;return;}
            if(StructureDefinition.Find(part)==null){Message="没有这个部件";return;}
            if(action=="select"){Selected=part;Accepted=true;return;}
            if(action!="interact"){Message="未知操作";return;}
            if(Won){Message="目标已取出，可以重开再试";return;}
            if(part=="knob")
            {
                if(Unlocked||Unlocking){Message="旋钮已松开";return;}
                Unlocking=true;Accepted=true;Moves++;Message="拧出旋钮，放入侧架";Event("unlock_started");return;
            }
            if(part=="gate")
            {Blocker=Unlocked?"hinge":"knob";Message=Unlocked?"盖板仍挂在左上销轴上，会受重力下摆":"右上旋钮仍锁住盖板";Event("blocked");return;}
            if(part=="module")
            {
                if(Extracting){Message="模块正在沿导轨抽出";return;}
                if(!SafeToExtract)
                {Blocker=!Unlocked?"knob":"gate";Message=!Unlocked?"先解除旋钮锁定":!PathClear?"盖板挡住了抽出通路":"盖板还在摆动，稍等它慢下来";Event("blocked");return;}
                Extracting=true;Accepted=true;Moves++;Message="已握住拉柄，沿导轨抽出";Event("extract_started");return;
            }
            Message="这是固定支架，无需拆除";Blocker=part;
        }
        public void Advance(float dt)
        {
            if(float.IsNaN(dt)||float.IsInfinity(dt)||dt<0)return;
            accumulator+=Math.Min(dt,.25f);
            const float step=1f/120;
            while(accumulator>=step){Tick(step);accumulator-=step;}
        }
        private void Tick(float dt)
        {
            if(Unlocking)
            {
                UnlockProgress=Math.Min(1,UnlockProgress+dt/.75f);
                if(UnlockProgress>=1){Unlocking=false;Unlocked=true;Message="锁定解除，盖板受重力下摆";Event("hinge_released");}
            }
            if(Unlocked)
            {
                bool wasSettled=Settled;
                if(!wasSettled)
                {
                    float x=StructureDefinition.GateOffset.X*(float)Math.Cos(Angle)-StructureDefinition.GateOffset.Y*(float)Math.Sin(Angle);
                    float inertia=(StructureDefinition.Width*StructureDefinition.Width+StructureDefinition.Height*StructureDefinition.Height)/3;
                    float acceleration=-9.81f*x/inertia-2.6f*AngularVelocity;
                    AngularVelocity+=acceleration*dt;Angle+=AngularVelocity*dt;
                    float lower=-85*(float)Math.PI/180;
                    if(Angle<lower){Angle=lower;AngularVelocity=Math.Max(0,AngularVelocity)*.15f;}
                    if(Angle>0){Angle=0;AngularVelocity=Math.Min(0,AngularVelocity)*.15f;}
                    if(Settled){Angle=StructureDefinition.Equilibrium;AngularVelocity=0;Event("hinge_settled");if(!Extracting&&!Won)Message="通路已打开，点击绿色模块取出";}
                }
            }
            if(Extracting)
            {
                ExtractProgress=Math.Min(1,ExtractProgress+dt/1.05f);
                if(ExtractProgress>=1){Extracting=false;Won=true;Message="模块完整取出 · 委托完成";Event("won");}
            }
        }
        public object Snapshot() => new {
            phase=Won?"won":Extracting?"extracting":Unlocking?"unlocking":!Unlocked?"locked":Settled?"ready":"swinging",
            accepted=Accepted,lastAction=LastAction,revision=Revision,moves=Moves,selected=Selected,message=Message,blocker=Blocker,
            unlocked=Unlocked,unlocking=Unlocking,unlockProgress=UnlockProgress,extractProgress=ExtractProgress,
            angleDegrees=Angle*180/Math.PI,angularVelocityDegrees=AngularVelocity*180/Math.PI,settled=Settled,
            pathClear=PathClear,safeToExtract=SafeToExtract,won=Won,moduleHeld=Extracting||Won,
            gravity=new{x=0,y=-9.81,z=0},motionModel="fixed-step constrained pendulum; gravity torque + damping; not free rigidbody",
            availableActions=new[]{new{action="interact",part="knob",ready=!Unlocked&&!Unlocking&&!Won},new{action="interact",part="module",ready=SafeToExtract&&!Extracting&&!Won},new{action="restart",part="",ready=true}},events=events.ToArray()
        };
    }
}
