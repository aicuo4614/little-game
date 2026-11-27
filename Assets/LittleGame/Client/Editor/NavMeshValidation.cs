using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LittleGame.Combat;
using LittleGame.Client.Battle;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace LittleGame.Client.Editor
{
    /// <summary>隔离原生导航几何/状态测试；不改真实存档与战斗数值，不能代替正常十波验收。</summary>
    public static class NavMeshValidation
    {
        private const string Output="Reports/Combat/navmesh-20261127/validation.json";
        private static readonly List<object> results=new List<object>();
        private static string current;
        private static int testIndex;
        private static bool running;
        private static BattleNavigation.AgentInput Agent(int id,float x,float z,float radius,bool hold=false)=>new BattleNavigation.AgentInput {
            Id=id,IsTroop=true,X=x,Z=z,Radius=radius,MaxSpeed=2.4f,TargetId=hold?0:99,TargetX=3.5f,TargetZ=0,
            TargetRadius=.4f,AttackReach=1.3f,HoldPosition=hold,State=new BattleNavigationState() };
        [MenuItem("Little Game/Combat/Verify Native Navigation")]
        public static async void Run()
        {
            if(!Application.isPlaying || running)throw new InvalidOperationException("需要Play且未在验证。");
            running=true;results.Clear();testIndex=0;Write("running",null);
            try
            {
                current="stationary_wall_detour";
                var units=new List<BattleNavigation.AgentInput>{Agent(1,-3,0,.4f)};
                for(int i=-1;i<=1;i++)units.Add(Agent(10+i,0,i*1.4f,.5f,true));
                float detour=0;
                using(var nav=Create())
                {
                    await Simulate(nav,units,7,()=>detour=Mathf.Max(detour,Mathf.Abs(units[0].Z)));
                    Require(units[0].X>.8f && detour>2.1f,"绕过站定墙");
                    Require(units[1].X==0 && units[2].X==0 && units[3].X==0,"站定单位不被推移");
                    Result(new {scenario=current,passed=true,x=units[0].X,maxAbsZ=detour});
                }
                current="blocked_then_release";
                units=new List<BattleNavigation.AgentInput>{Agent(1,-3,0,.4f)};
                for(int i=-2;i<=2;i++)units.Add(Agent(20+i,0,i*1.2f,.5f,true));
                using(var nav=Create())
                {
                    await Simulate(nav,units,3);
                    float x=units[0].X,z=units[0].Z;
                    await Simulate(nav,units,.6f);
                    Require(Mathf.Abs(units[0].X-x)<.05f && Mathf.Abs(units[0].Z-z)<.05f,"封死后站定等待");
                    units.RemoveAll(a=>a.Id==20);
                    await Simulate(nav,units,4);
                    Require(units[0].X>.8f,"撤销阻挡后恢复 x="+units[0].X+" z="+units[0].Z+" nav="+JsonConvert.SerializeObject(nav.Snapshot()));
                    Result(new {scenario=current,passed=true,waitingX=x,finalX=units[0].X});
                }
                current="opposing";
                units=new List<BattleNavigation.AgentInput>{Agent(1,-3,-.15f,.4f),Agent(2,3,.15f,.4f)};
                var right=units[1];right.TargetId=98;right.TargetX=-3.5f;units[1]=right;
                float gap=float.MaxValue;
                using(var nav=Create())
                {
                    await Simulate(nav,units,5,()=>gap=Mathf.Min(gap,Distance(units[0],units[1])));
                    Require(units[0].X>1 && units[1].X< -1,"相向错身通过");
                    Require(gap>=.74f,"相向不互穿");
                    Result(new {scenario=current,passed=true,minimumGap=gap});
                }
                current="freeze_pause_resume";
                units=new List<BattleNavigation.AgentInput>{Agent(1,-3,0,.4f)};
                using(var nav=Create())
                {
                    await Simulate(nav,units,.7f);
                    var unit=units[0];unit.HoldPosition=true;units[0]=unit;
                    float held=unit.X;await Simulate(nav,units,.9f);
                    Require(Mathf.Abs(units[0].X-held)<.001f,"冻结绝对站定");
                    unit=units[0];unit.HoldPosition=false;units[0]=unit;
                    await Simulate(nav,units,1);
                    Require(units[0].X>held+.2f,"解冻恢复");
                    float before=units[0].X;
                    nav.PrepareFrame(true,true,2);await NextFrame();await Task.Delay(150);
                    Require(units[0].X==before,"暂停不写规则位移");
                    await Simulate(nav,units,.5f);
                    Require(units[0].X>=before,"暂停继续衔接");
                    Result(new {scenario=current,passed=true});
                }
                var obstacles=new List<BattleNavObstacleData>{new BattleNavObstacleData(0,.3f,-4,-.5f),new BattleNavObstacleData(0,.3f,.5f,4)};
                current="body_size";
                using(var nav=Create(obstacles))
                {
                    var giant=Agent(1,-3,0,1.15f);giant.AttackReach=1.6f;giant.TargetRadius=.1f;
                    units=new List<BattleNavigation.AgentInput>{giant};
                    await Simulate(nav,units,2);
                    Require(units[0].X<0,"首领不能走过窄道");
                    nav.Reset();units=new List<BattleNavigation.AgentInput>{Agent(1,-3,0,.25f)};
                    await Simulate(nav,units,4);
                    Require(units[0].X>.7f,"普通体型能过同一窄道");
                    Result(new {scenario=current,passed=true});
                }
                current="boss_surround";
                units=new List<BattleNavigation.AgentInput>();
                for(int i=0;i<6;i++){var a=Agent(i+1,-3-i/3,i%3*1.6f-1.6f,.5f);a.AttackReach=2.05f;a.TargetRadius=1.15f;units.Add(a);}
                var boss=Agent(99,3.5f,0,1.15f,true);boss.IsTroop=false;units.Add(boss);
                using(var nav=Create())
                {
                    await Simulate(nav,units,7,()=>{
                        for(int i=0;i<6;i++)if(Distance(units[i],units[6])<2.01f){var a=units[i];a.HoldPosition=true;units[i]=a;}
                    });
                    for(int i=0;i<6;i++)Require(Distance(units[i],units[6])<=2.08f,"围攻单位抵达射程 "+i);
                    Result(new {scenario=current,passed=true,agents=7});
                }
                Write("passed",null);
            }
            catch(Exception error){Write("failed",error.ToString());Debug.LogException(error);}
            finally{running=false;}
        }
        private static BattleNavMeshNavigation Create(IList<BattleNavObstacleData> obstacles=null)
            =>new BattleNavMeshNavigation(obstacles,new Vector3(1000+testIndex++*50,0,0));
        private static async Task Simulate(BattleNavMeshNavigation nav,List<BattleNavigation.AgentInput> units,float duration,Action inspect=null)
        {
            var output=new List<BattleNavigation.AgentOutput>(64);
            float elapsed=0,accumulator=0;int last=Time.frameCount;
            while(elapsed<duration)
            {
                await NextFrame();
                if(last==Time.frameCount)continue;last=Time.frameCount;
                float dt=Mathf.Min(Time.unscaledDeltaTime,.1f);elapsed+=dt;accumulator+=dt;
                nav.PrepareFrame(true,false,1);
                while(accumulator>=.05f)
                {
                    accumulator-=.05f;nav.Step(units,.05f,output);
                    for(int i=0;i<units.Count;i++)
                    {
                        var a=units[i];var o=output[i];
                        Require(Mathf.Abs(o.NewVx*.05f-(o.NewX-a.X))<.0001f,"速度等于实际位移/步长");
                        a.X=o.NewX;a.Z=o.NewZ;units[i]=a;
                    }
                    inspect?.Invoke();
                }
            }
        }
        private static async Task NextFrame(){int frame=Time.frameCount;while(Application.isPlaying && Time.frameCount==frame)await Task.Delay(2);if(!Application.isPlaying)throw new OperationCanceledException("Play停止");}
        private static float Distance(BattleNavigation.AgentInput a,BattleNavigation.AgentInput b)=>Vector2.Distance(new Vector2(a.X,a.Z),new Vector2(b.X,b.Z));
        private static void Require(bool pass,string message){if(!pass)throw new InvalidOperationException(current+": "+message);}
        private static void Result(object result){results.Add(result);Write("running",null);}
        private static void Write(string state,string error){Directory.CreateDirectory(Path.GetDirectoryName(Output));File.WriteAllText(Output,JsonConvert.SerializeObject(new {state,scenario=current,results,error,platform="WindowsEditor",note="隔离几何/状态验证，不是正常玩法或手机验证"},Formatting.Indented));}
    }
}
