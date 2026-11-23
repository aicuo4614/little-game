using System;
using System.Numerics;

namespace LittleGame.Structure
{
    // 模型、空间检查和观察工具共用的米制结构数据。观察相机不能改变这些坐标。
    public sealed class StructuralPart
    {
        public readonly string Id, Name, Color, Connection;
        public readonly Vector3 Center, Size;
        public StructuralPart(string id,string name,Vector3 center,Vector3 size,string color,string connection="fixed")
        { Id=id;Name=name;Center=center;Size=size;Color=color;Connection=connection; }
    }

    public static class StructureDefinition
    {
        public static readonly Vector3 Hinge=new Vector3(-1.35f,3.35f,-.55f);
        public const float Width=2.7f,Height=1.8f,Thickness=.12f,ExtractDistance=1.65f;
        public static readonly Vector3 GateOffset=new Vector3(Width/2,-Height/2,0);
        public static readonly Vector3 ModuleHome=new Vector3(.65f,2.82f,.02f);
        public static readonly StructuralPart[] Parts={
            new StructuralPart("base","支架底座",new Vector3(0,-.12f,.1f),new Vector3(3.5f,.24f,2.3f),"#344957"),
            new StructuralPart("left_post","左立柱",new Vector3(-1.55f,1.72f,.35f),new Vector3(.22f,3.44f,.28f),"#627D89"),
            new StructuralPart("right_post","右立柱",new Vector3(1.55f,1.72f,.35f),new Vector3(.22f,3.44f,.28f),"#627D89"),
            new StructuralPart("crossbar","上横梁",new Vector3(0,3.5f,.35f),new Vector3(3.32f,.2f,.28f),"#627D89"),
            new StructuralPart("back","模块安装背板",new Vector3(.65f,2.82f,.6f),new Vector3(1.3f,.9f,.12f),"#344957"),
            new StructuralPart("module_mount","背板吊架",new Vector3(.65f,3.25f,.45f),new Vector3(.2f,.5f,.4f),"#627D89"),
            new StructuralPart("rail_left","左导轨",new Vector3(.04f,2.49f,.0f),new Vector3(.28f,.11f,.95f),"#C6D0CC"),
            new StructuralPart("rail_right","右导轨",new Vector3(1.26f,2.49f,.0f),new Vector3(.28f,.11f,.95f),"#C6D0CC"),
            new StructuralPart("lock_socket","旋钮锁孔",new Vector3(1.16f,3.12f,-.32f),new Vector3(.3f,.3f,.24f),"#526A75"),
            new StructuralPart("lock_mount","锁孔支座",new Vector3(1.37f,3.12f,-.02f),new Vector3(.36f,.2f,.6f),"#627D89"),
            new StructuralPart("knob_dock","旋钮停靠座",new Vector3(1.98f,3.12f,-.85f),new Vector3(.48f,.5f,.18f),"#526A75"),
            new StructuralPart("dock_arm","停靠架纵梁",new Vector3(1.98f,3.12f,-.1f),new Vector3(.12f,.12f,1.5f),"#627D89"),
            new StructuralPart("dock_mount","停靠架横梁",new Vector3(1.75f,3.12f,.35f),new Vector3(.6f,.12f,.12f),"#627D89"),
            new StructuralPart("hinge","悬挂销轴",new Vector3(Hinge.X,Hinge.Y,-.05f),new Vector3(.24f,.24f,1.25f),"#D8C79C"),
            new StructuralPart("gate","悬挂盖板",Hinge+GateOffset,new Vector3(Width,Height,Thickness),"#D98D51","revolute_z"),
            new StructuralPart("knob","锁紧旋钮",new Vector3(1.16f,3.12f,-.71f),new Vector3(.4f,.4f,.4f),"#E7C16A","thread_lock"),
            new StructuralPart("module","目标模块",ModuleHome,new Vector3(1.05f,.55f,.70f),"#49A89A","prismatic_minus_z")
        };
        public static StructuralPart Find(string id) => Array.Find(Parts,p=>p.Id==id);
        public static float Equilibrium => -(float)Math.Atan2(GateOffset.X,-GateOffset.Y);
        public static Vector3 GateCenter(float angle) => Hinge+Vector3.Transform(GateOffset,Quaternion.CreateFromAxisAngle(Vector3.UnitZ,angle));

        // OBB 对 AABB 的分离轴检查。实体边界不使用扩大后的触摸命中体。
        public static bool GateOverlaps(Vector3 center,Vector3 size,float angle,float margin=0)
        {
            var q=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,angle);
            var a=new[]{Vector3.Transform(Vector3.UnitX,q),Vector3.Transform(Vector3.UnitY,q),Vector3.UnitZ};
            var b=new[]{Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ};
            var extent=new Vector3(Width,Height,Thickness)*.5f;
            var other=size*.5f+new Vector3(margin);
            var d=center-GateCenter(angle);
            foreach(var axis in a) if(Separated(axis,d,a,extent,b,other))return false;
            foreach(var axis in b) if(Separated(axis,d,a,extent,b,other))return false;
            foreach(var x in a)foreach(var y in b)
                if(Separated(Vector3.Cross(x,y),d,a,extent,b,other))return false;
            return true;
        }
        private static bool Separated(Vector3 axis,Vector3 d,Vector3[] a,Vector3 e,Vector3[] b,Vector3 f)
        {
            if(axis.LengthSquared()<.000001f)return false;
            float ra=Math.Abs(Vector3.Dot(axis,a[0]))*e.X+Math.Abs(Vector3.Dot(axis,a[1]))*e.Y+Math.Abs(Vector3.Dot(axis,a[2]))*e.Z;
            float rb=Math.Abs(Vector3.Dot(axis,b[0]))*f.X+Math.Abs(Vector3.Dot(axis,b[1]))*f.Y+Math.Abs(Vector3.Dot(axis,b[2]))*f.Z;
            return Math.Abs(Vector3.Dot(d,axis))>ra+rb;
        }
        public static bool ExitClear(float angle)
        {
            var module=Find("module");
            // 直线抽取的扫掠体是一个连续盒体，检查整个通路而不只检查端点。
            return !GateOverlaps(ModuleHome-new Vector3(0,0,ExtractDistance*.5f),
                module.Size+new Vector3(0,0,ExtractDistance),angle,.025f);
        }
    }
}
