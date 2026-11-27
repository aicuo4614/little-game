using System;
using System.Collections.Generic;
using LittleGame.Combat;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.AI;

namespace LittleGame.Client.Battle
{
    /// <summary>唯一实战导航后端：原生NavMeshAgent路径/避让，规则层拥有战斗状态与实际位置。</summary>
    public sealed class BattleNavMeshNavigation : IBattleNavigation, IDisposable
    {
        private sealed class Proxy
        {
            public GameObject Root;
            public NavMeshAgent Agent;
            public NavMeshObstacle Obstacle;
            public readonly NavMeshPath Path = new NavMeshPath();
            public readonly Vector3[] CornerBuffer = new Vector3[32];
            public readonly List<NavPoint> Corners = new List<NavPoint>(32);
            public readonly List<NavPoint> Remaining = new List<NavPoint>(32);
            public BattleNavigation.AgentInput Input;
            public Vector3 LastRule, Goal, PlannedTarget;
            public bool HasGoal, Holding, Airborne;
            public float HoldFor, RetryAt, BlockedFor;
            public int Corner, Target;
        }
        private static readonly ProfilerMarker Marker = new ProfilerMarker("LittleGame.NavMeshBridge");
        private readonly GameObject root;
        private readonly Dictionary<int, Proxy> agents = new Dictionary<int, Proxy>(32);
        private readonly Stack<Proxy> pool = new Stack<Proxy>(32);
        private readonly HashSet<int> active = new HashSet<int>();
        private readonly List<int> removals = new List<int>(32);
        private readonly List<NavMeshData> meshes = new List<NavMeshData>();
        private readonly List<NavMeshDataInstance> instances = new List<NavMeshDataInstance>();
        private readonly List<int> types = new List<int>();
        private readonly List<NavMeshBuildSource> sources;
        private float time;
        private int speed = 1;
        private bool paused, wasFighting, disposed;
        public string Backend => "unity_navmesh_agent_attack_slots";
        public int AgentCount => agents.Count;
        public int PathRequests { get; private set; }
        public int FailedPaths { get; private set; }
        public double BuildMs { get; private set; }

        public BattleNavMeshNavigation(IList<BattleNavObstacleData> obstacles = null, Vector3 offset = default)
        {
            root = new GameObject("BattleNavMeshWorld");
            root.transform.position = offset;
            sources = new List<NavMeshBuildSource> {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(offset + new Vector3((BattleDefinitions.FieldMinX + BattleDefinitions.FieldMaxX) * .5f, -.1f, 0), Quaternion.identity, Vector3.one),
                    size = new Vector3(BattleDefinitions.FieldMaxX - BattleDefinitions.FieldMinX + 2.5f, .2f, BattleDefinitions.FieldMaxZ - BattleDefinitions.FieldMinZ + 2.5f), area = 0 }
            };
            if (obstacles != null)
                foreach (var o in obstacles) sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.ModifierBox,
                    transform = Matrix4x4.TRS(offset + new Vector3((o.MinX + o.MaxX) * .5f, .5f, (o.MinZ + o.MaxZ) * .5f), Quaternion.identity, Vector3.one),
                    size = new Vector3(o.MaxX-o.MinX, 2f, o.MaxZ-o.MinZ), area = 1 });
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Build(.25f, offset); Build(.4f, offset); Build(.5f, offset); Build(.65f, offset); Build(1.15f, offset);
            BuildMs = timer.Elapsed.TotalMilliseconds;
        }

        private void Build(float radius, Vector3 offset)
        {
            var settings = NavMesh.CreateSettings();
            types.Add(settings.agentTypeID);
            settings.agentRadius = radius; settings.agentHeight = 2f; settings.agentClimb = .1f; settings.agentSlope = 0f;
            settings.overrideVoxelSize = true; settings.voxelSize = .08f;
            settings.overrideTileSize = true; settings.tileSize = 128; settings.minRegionArea = .01f;
            var ground = sources[0];
            ground.size = new Vector3(BattleDefinitions.FieldMaxX - BattleDefinitions.FieldMinX + radius * 2f, .2f, BattleDefinitions.FieldMaxZ - BattleDefinitions.FieldMinZ + radius * 2f);
            sources[0] = ground;
            var mesh = NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(offset + new Vector3(.5f, 0, 0), new Vector3(16, 4, 10)), Vector3.zero, Quaternion.identity);
            if (!mesh) throw new InvalidOperationException("战斗NavMesh生成失败。");
            meshes.Add(mesh); instances.Add(NavMesh.AddNavMeshData(mesh));
        }

        public void PrepareFrame(bool fighting, bool isPaused, int battleSpeed)
        {
            if (wasFighting && !fighting) Reset();
            wasFighting = fighting; paused = isPaused || !fighting; speed = Math.Max(1, battleSpeed);
            foreach (var p in agents.Values)
            {
                if (!p.Agent.enabled || !p.Agent.isOnNavMesh) continue;
                p.Agent.speed = paused ? 0f : p.Input.MaxSpeed * speed;
                p.Agent.acceleration = 60f * speed * speed;
                p.Agent.isStopped = paused || p.Holding || !p.HasGoal;
                if (paused || p.Holding) p.Agent.nextPosition = World(p.Input.X, p.Input.Z);
            }
        }

        public void Reset()
        {
            foreach (var p in agents.Values) Recycle(p);
            agents.Clear(); active.Clear(); time = 0; PathRequests = FailedPaths = 0;
        }

        public void Step(IList<BattleNavigation.AgentInput> inputs, float dt, List<BattleNavigation.AgentOutput> outputs)
        {
            using var sample = Marker.Auto();
            time += dt; outputs.Clear(); active.Clear();
            for (int i=0; i<inputs.Count; i++) active.Add(inputs[i].Id);
            removals.Clear();
            foreach (var pair in agents) if (!active.Contains(pair.Key)) removals.Add(pair.Key);
            foreach (int id in removals) { Recycle(agents[id]); agents.Remove(id); }
            // 先同步全部身体，再决定攻击落点，避免读取上一个逻辑步的占位。
            for (int i=0; i<inputs.Count; i++)
            {
                var input = inputs[i];
                if (!agents.TryGetValue(input.Id, out var p))
                {
                    p = Create(input); agents.Add(input.Id,p);
                }
                p.Input = input;
            }
            for (int i=0; i<inputs.Count; i++) Sync(agents[inputs[i].Id],dt,outputs);
        }

        private Proxy Create(BattleNavigation.AgentInput input)
        {
            Proxy p;
            if (pool.Count > 0) p = pool.Pop();
            else
            {
                p = new Proxy { Root = new GameObject("NavAgent") };
                p.Root.transform.SetParent(root.transform,false);
                p.Root.SetActive(false);
                p.Agent = p.Root.AddComponent<NavMeshAgent>();
                p.Obstacle = p.Root.AddComponent<NavMeshObstacle>();
                p.Obstacle.enabled = false;
            }
            p.Input = input; p.Root.name = "NavAgent_"+input.Id;
            p.Obstacle.enabled = false; p.Agent.enabled = false;
            p.Root.transform.position = World(input.X,input.Z);
            p.Agent.agentTypeID = Type(input.Radius);
            p.Agent.radius = input.Radius;
            p.Agent.height = 2f; p.Agent.baseOffset = 0f;
            p.Agent.updatePosition = false; p.Agent.updateRotation = false; p.Agent.updateUpAxis = false;
            p.Agent.autoRepath = false; p.Agent.autoBraking = true; p.Agent.stoppingDistance = .035f;
            p.Agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            p.Agent.avoidancePriority = 30 + input.Id % 30;
            p.Agent.speed = input.MaxSpeed * speed; p.Agent.acceleration = 60f*speed*speed;
            p.Root.SetActive(true); p.Agent.enabled=true;
            if (!p.Agent.Warp(World(input.X,input.Z))) throw new InvalidOperationException("单位出生点无法绑定NavMesh："+input.Id);
            p.LastRule = World(input.X,input.Z); p.HoldFor=p.RetryAt=p.BlockedFor=0;
            p.HasGoal=p.Holding=p.Airborne=false; p.Corner=0; p.Corners.Clear();
            return p;
        }

        private void Sync(Proxy p,float dt,List<BattleNavigation.AgentOutput> outputs)
        {
            var a=p.Input; var state=a.State; var origin=World(a.X,a.Z); var position=origin;
            bool hold=a.HoldPosition || a.TargetId==0;
            if (a.Airborne)
            {
                p.Obstacle.enabled=false; p.Agent.enabled=false; p.Airborne=true; p.HasGoal=false;
                state.ClearNativePath(); state.SetMotion(NavigationStatus.Holding,"airborne",0);
                Emit(p,origin,dt,false,outputs); return;
            }
            if (hold)
            {
                p.HoldFor = p.Holding ? p.HoldFor+dt : 0f;
                if (p.Agent.enabled && p.Agent.isOnNavMesh)
                { p.Agent.isStopped=true; p.Agent.nextPosition=origin; p.Agent.velocity=Vector3.zero; }
                // 连续站定才成为全局阻挡；移动单位仅参与原生局部避让，不反复切洞。
                if (p.HoldFor >= .65f && !p.Obstacle.enabled)
                {
                    p.Agent.enabled=false;
                    p.Root.transform.position=origin;
                    p.Obstacle.shape=NavMeshObstacleShape.Capsule; p.Obstacle.radius=a.Radius;
                    p.Obstacle.height=2f; p.Obstacle.center=new Vector3(0,1,0);
                    p.Obstacle.carving=true; p.Obstacle.carveOnlyStationary=true;
                    p.Obstacle.carvingTimeToStationary=.15f; p.Obstacle.carvingMoveThreshold=.2f;
                    p.Obstacle.enabled=true;
                }
                p.Holding=true; p.HasGoal=false; p.BlockedFor=0;
                state.ClearNativePath(); state.SetMotion(NavigationStatus.Holding,"",0);
                Emit(p,origin,dt,false,outputs); return;
            }
            if (!p.Agent.enabled)
            {
                p.Obstacle.enabled=false; p.Root.transform.position=origin; p.Agent.enabled=true;
                // 切洞撤销有一帧延迟，下一次更新再绑定；失败不强行传送到别处。
                if (!p.Agent.Warp(origin)) { p.Agent.enabled=false; Wait(p,state,"navmesh_rebinding",dt); Emit(p,origin,dt,true,outputs);return; }
                p.HasGoal=false;
            }
            if (!p.Agent.isOnNavMesh) { Wait(p,state,"off_navmesh",dt); Emit(p,origin,dt,true,outputs);return; }
            if (p.Airborne || (origin-p.LastRule).sqrMagnitude>.04f)
            { p.Agent.Warp(origin); p.HasGoal=false; }
            else if (!p.Holding) position=p.Agent.nextPosition;
            p.Airborne=p.Holding=false; p.HoldFor=0;
            // 大帧不能让一次固定步长出现非配置的高速位移。
            position=Vector3.MoveTowards(origin,position,a.MaxSpeed*dt);
            position.x=Mathf.Clamp(position.x,root.transform.position.x+BattleDefinitions.FieldMinX,root.transform.position.x+BattleDefinitions.FieldMaxX);
            position.z=Mathf.Clamp(position.z,root.transform.position.z+BattleDefinitions.FieldMinZ,root.transform.position.z+BattleDefinitions.FieldMaxZ);
            p.Agent.nextPosition=position; p.Root.transform.position=position;
            var target=World(a.TargetX,a.TargetZ);
            bool need=!p.HasGoal || p.Target!=a.TargetId || (target-p.PlannedTarget).sqrMagnitude>.2025f
                || p.Agent.isPathStale || p.Agent.pathStatus!=NavMeshPathStatus.PathComplete || p.BlockedFor>.65f || ((position-p.Goal).sqrMagnitude<.0225f && p.BlockedFor>.2f);
            if (need && time>=p.RetryAt)
            {
                p.RetryAt=time+.3f;
                if (!Plan(p,position,target))
                {
                    p.Agent.isStopped=true; p.HasGoal=false;
                    Wait(p,state,"attack_slot_unavailable",dt); Emit(p,position,dt,true,outputs);return;
                }
            }
            p.Agent.speed=a.MaxSpeed*speed; p.Agent.acceleration=60f*speed*speed;
            p.Agent.isStopped=paused || !p.HasGoal;
            float distance=(position-origin).sqrMagnitude;
            bool waiting=distance<.000004f;
            p.BlockedFor=waiting ? p.BlockedFor+dt : 0f;
            state.SetMotion(waiting?NavigationStatus.Waiting:NavigationStatus.Moving,
                waiting ? (p.HasGoal?"native_avoidance_wait":"attack_slot_unavailable") : "",p.BlockedFor);
            p.Remaining.Clear();
            while(p.Corner<p.Corners.Count && (position-World(p.Corners[p.Corner].X,p.Corners[p.Corner].Z)).sqrMagnitude<.04f) p.Corner++;
            for(int i=p.Corner;i<p.Corners.Count;i++) p.Remaining.Add(p.Corners[i]);
            state.SetRemainingPath(p.Remaining);
            Emit(p,position,dt,waiting,outputs);
        }

        private bool Plan(Proxy p,Vector3 from,Vector3 target)
        {
            var a=p.Input;
            float radius=Mathf.Max(a.Radius+a.TargetRadius+.04f,a.AttackReach-.10f);
            if(radius>=a.AttackReach) return false;
            float bearing=Mathf.Atan2(from.z-target.z,from.x-target.x);
            Vector3 best=default; float bestScore=float.MaxValue;
            var filter=new NavMeshQueryFilter {agentTypeID=Type(a.Radius),areaMask=NavMesh.AllAreas};
            for(int i=0;i<24;i++)
            {
                float angle=bearing+i*Mathf.PI/12f;
                var point=target+new Vector3(Mathf.Cos(angle)*radius,0,Mathf.Sin(angle)*radius);
                var local=point-root.transform.position;
                if(local.x<BattleDefinitions.FieldMinX || local.x>BattleDefinitions.FieldMaxX || local.z<BattleDefinitions.FieldMinZ || local.z>BattleDefinitions.FieldMaxZ) continue;
                bool occupied=false;
                foreach(var other in agents.Values)
                {
                    if(other==p || other.Input.Airborne) continue;
                    float r=a.Radius+other.Input.Radius+.015f;
                    if((point-World(other.Input.X,other.Input.Z)).sqrMagnitude<r*r) {occupied=true;break;}
                    if(other.HasGoal && other.Input.IsTroop==a.IsTroop && other.Target==a.TargetId && (point-other.Goal).sqrMagnitude<r*r) {occupied=true;break;}
                }
                if(occupied || !NavMesh.SamplePosition(point,out var hit,.10f,filter)) continue;
                float score=(hit.position-from).sqrMagnitude;
                if(score<bestScore) {best=hit.position;bestScore=score;}
            }
            if(bestScore==float.MaxValue) return false;
            PathRequests++;
            if(!p.Agent.CalculatePath(best,p.Path) || p.Path.status!=NavMeshPathStatus.PathComplete || !p.Agent.SetPath(p.Path))
            { FailedPaths++;return false; }
            p.Goal=best;p.Target=a.TargetId;p.PlannedTarget=target;p.HasGoal=true;
            p.Corner=0;p.Corners.Clear();
            int count=p.Path.GetCornersNonAlloc(p.CornerBuffer);
            for(int i=0;i<count;i++) {var point=p.CornerBuffer[i]-root.transform.position;p.Corners.Add(new NavPoint(point.x,point.z));}
            var goal=best-root.transform.position;
            a.State.SetNativePath(a.TargetId,new NavPoint(goal.x,goal.z),p.Corners);
            return true;
        }
        private void Wait(Proxy p,BattleNavigationState state,string reason,float dt)
        {p.BlockedFor+=dt;state.ClearNativePath();state.SetMotion(NavigationStatus.Waiting,reason,p.BlockedFor);}
        private void Emit(Proxy p,Vector3 world,float dt,bool waiting,List<BattleNavigation.AgentOutput> output)
        {
            var point=world-root.transform.position;var a=p.Input;
            output.Add(new BattleNavigation.AgentOutput {Id=a.Id,IsTroop=a.IsTroop,Waiting=waiting,
                NewX=point.x,NewZ=point.z,NewVx=(point.x-a.X)/dt,NewVz=(point.z-a.Z)/dt});
            p.LastRule=world;
        }
        private Vector3 World(float x,float z)=>root.transform.position+new Vector3(x,0,z);
        private int Type(float radius)=>types[radius<=.25f?0:radius<=.4f?1:radius<=.5f?2:radius<=.65f?3:4];
        private void Recycle(Proxy p)
        {p.Obstacle.enabled=false;p.Agent.enabled=false;p.Root.SetActive(false);p.HasGoal=false;pool.Push(p);}
        public object Snapshot()
        {
            var actors=new List<object>();
            foreach(var pair in agents)
            {
                var p=pair.Value;bool bound=p.Agent.enabled && p.Agent.isOnNavMesh;
                var position=(bound?p.Agent.nextPosition:p.Root.transform.position)-root.transform.position;
                actors.Add(new {id=pair.Key,bound,carving=p.Obstacle.enabled,holding=p.Holding,hasGoal=p.HasGoal,
                    x=position.x,z=position.z,goalX=p.Goal.x-root.transform.position.x,goalZ=p.Goal.z-root.transform.position.z,
                    pathStatus=bound?p.Agent.pathStatus.ToString():"unbound",blockedFor=p.BlockedFor});
            }
            return new {backend=Backend,agents=agents.Count,pooled=pool.Count,pathRequests=PathRequests,failedPaths=FailedPaths,
                buildMs=BuildMs,nativeAvoidance="HighQualityRVO",meshTypes=types.Count,actors};
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;Reset();
            foreach(var p in pool)p.Path.ClearCorners();
            pool.Clear();foreach(var instance in instances)instance.Remove();
            foreach(var mesh in meshes)UnityEngine.Object.Destroy(mesh);
            foreach(int type in types)NavMesh.RemoveSettings(type);
            UnityEngine.Object.Destroy(root);
        }
    }
    public readonly struct BattleNavObstacleData
    {
        public readonly float MinX,MaxX,MinZ,MaxZ;
        public BattleNavObstacleData(float minX,float maxX,float minZ,float maxZ){MinX=minX;MaxX=maxX;MinZ=minZ;MaxZ=maxZ;}
    }
}
