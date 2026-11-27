using System;
using System.Collections.Generic;
using LittleGame.Combat;

static class NavigationTests
{
    public static void Run() => throw new NotSupportedException("原生NavMesh需在Unity执行 Little Game/Combat/Verify Native Navigation；旧Roy测试已归档，不代表当前后端。");
    public static void HeapInvariant() => throw new NotSupportedException("Roy队列已退出实战；旧测试见Archive~/NavigationRoy。");

    /// <summary>
    /// 仅供无Unity的纯规则测试（CombatTests）驱动位移推进；不代表实战导航表现或避障验收。
    /// 实战唯一导航后端是 Unity 原生 BattleNavMeshNavigation。
    /// </summary>
    internal sealed class TestHarnessNavigationStub : IBattleNavigation
    {
        public string Backend => "test_harness_straight_stub";
        public void Reset() { }
        public void Step(IList<BattleNavigation.AgentInput> inputs, float dt, List<BattleNavigation.AgentOutput> outputs)
        {
            outputs.Clear();
            for (int i = 0; i < inputs.Count; i++)
            {
                var a = inputs[i];
                float nx = a.X, nz = a.Z;
                bool waiting = false;
                if (a.Airborne)
                {
                    a.State.ClearNativePath();
                    a.State.SetMotion(NavigationStatus.Holding, "airborne", 0);
                }
                else if (a.HoldPosition || a.TargetId == 0)
                {
                    a.State.ClearNativePath();
                    a.State.SetMotion(NavigationStatus.Holding, "", 0);
                }
                else
                {
                    float dx = a.TargetX - a.X, dz = a.TargetZ - a.Z;
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    float stop = Math.Max(0.05f, a.AttackReach - 0.06f);
                    if (dist > stop && a.MaxSpeed > 0f)
                    {
                        float step = Math.Min(dist - stop, a.MaxSpeed * dt);
                        nx = Math.Clamp(a.X + dx / dist * step, BattleDefinitions.FieldMinX, BattleDefinitions.FieldMaxX);
                        nz = Math.Clamp(a.Z + dz / dist * step, BattleDefinitions.FieldMinZ, BattleDefinitions.FieldMaxZ);
                        a.State.SetMotion(NavigationStatus.Moving, "", 0);
                    }
                    else
                    {
                        waiting = true;
                        a.State.SetMotion(NavigationStatus.Waiting, "stub_arrived", dt);
                    }
                }
                outputs.Add(new BattleNavigation.AgentOutput
                {
                    Id = a.Id, IsTroop = a.IsTroop, Waiting = waiting,
                    NewX = nx, NewZ = nz, NewVx = (nx - a.X) / dt, NewVz = (nz - a.Z) / dt
                });
            }
        }
    }

    internal static BattleSession AttachStub(BattleSession session)
    {
        session.AttachNavigation(new TestHarnessNavigationStub());
        return session;
    }
}
