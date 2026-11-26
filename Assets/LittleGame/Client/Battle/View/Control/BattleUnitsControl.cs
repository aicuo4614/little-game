using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Combat;
using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 战场单位管理子系统（对齐 Demo BattleUnitsControl）：
    /// 维护全部纯 C# BattleUnit 的创建、对象池复用、逐帧同步与波后清理。
    /// </summary>
    public sealed class BattleUnitsControl : BattleControlBase
    {
        private readonly Dictionary<int, BattleUnit> units = new Dictionary<int, BattleUnit>(24);
        private readonly HashSet<int> activeIds = new HashSet<int>();
        private readonly List<int> removeBuffer = new List<int>(16);

        public IReadOnlyDictionary<int, BattleUnit> Units => units;

        public bool TryGet(int unitId, out BattleUnit unit) => units.TryGetValue(unitId, out unit);

        public BattleUnit FindUnit(int unitId) => units.TryGetValue(unitId, out var u) ? u : null;

        public List<BattleUnit> GetAllUnits() => new List<BattleUnit>(units.Values);

        public void Sync(BattleSession session, bool marching, float deltaTime)
        {
            if (session == null) return;
            activeIds.Clear();
            bool fighting = session.Phase == RunPhase.Fighting;
            bool paused = session.Paused || session.Aim != AimKind.None;
            float battleSpeed = Math.Max(1, session.Speed);

            var troops = session.Troops;
            for (int i = 0; i < troops.Count; i++)
            {
                var t = troops[i];
                if (!t.Deployed) continue;
                activeIds.Add(t.Id);
                var unit = GetOrCreate(t.Id, (int)t.Kind, false, false);
                float scale = t.Star == 3 ? 1.18f : t.Star == 2 ? 1.08f : 1f;
                unit.SyncFromRule(
                    new Vector3(t.X, 0.11f, t.Z),
                    t.FacingYaw,
                    t.Hp / Mathf.Max(1f, t.MaxHp),
                    t.Star == 1 ? "★" : t.Star == 2 ? "★★" : "★★★",
                    t.Shield > 0f,
                    scale,
                    t.Alive || !fighting,
                    !fighting,
                    fighting,
                    paused,
                    controlled: false,
                    star: t.Star,
                    forceWalk: !fighting && (marching || t.NeedsRegroup) && !paused,
                    movementSpeed: fighting ? Mathf.Sqrt(t.Vx * t.Vx + t.Vz * t.Vz) : -1f,
                    battleSpeed: battleSpeed);
            }

            if (fighting)
            {
                var enemies = session.Enemies;
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i];
                    activeIds.Add(e.Id);
                    var unit = GetOrCreate(e.Id, (int)e.Kind, true, e.Boss);
                    bool frozen = e.Alive && e.FrozenUntil > session.BattleTime;
                    unit.SyncFromRule(
                        new Vector3(e.X, 0.11f, e.Z),
                        e.FacingYaw,
                        e.Hp / Mathf.Max(1f, e.MaxHp),
                        e.Boss ? BattleDefinitions.Name(e.Kind) : "",
                        frozen,
                        1f,
                        e.Alive,
                        false,
                        e.Alive && !frozen,
                        paused,
                        controlled: frozen,
                        star: 1,
                        forceWalk: false,
                        movementSpeed: Mathf.Sqrt(e.Vx * e.Vx + e.Vz * e.Vz),
                        battleSpeed: battleSpeed);
                }
            }

            removeBuffer.Clear();
            foreach (var kv in units)
            {
                if (!activeIds.Contains(kv.Key))
                {
                    removeBuffer.Add(kv.Key);
                }
            }
            for (int i = 0; i < removeBuffer.Count; i++)
            {
                RemoveUnit(removeBuffer[i]);
            }

            float scaledDt = paused ? 0f : deltaTime * battleSpeed;
            foreach (var kv in units)
            {
                kv.Value.Tick(scaledDt);
            }
        }

        public Vector2 UnitScreen(int id)
        {
            if (units.TryGetValue(id, out var unit) && unit != null && !unit.IsEnded)
            {
                return unit.TargetControl.GetScreenPosition();
            }
            return new Vector2(-1000f, -1000f);
        }

        public int HitUnit(BattleSession session, Vector2 screen, bool enemy)
        {
            float maxDist = Mathf.Max(23f, Screen.width * 0.065f);
            int bestId = 0;
            float bestDist = maxDist;
            if (enemy)
            {
                var enemies = session.Enemies;
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (!enemies[i].Alive) continue;
                    float d = Vector2.Distance(screen, UnitScreen(enemies[i].Id));
                    if (d < bestDist) { bestDist = d; bestId = enemies[i].Id; }
                }
            }
            else
            {
                var troops = session.Troops;
                for (int i = 0; i < troops.Count; i++)
                {
                    if (!troops[i].Deployed) continue;
                    float d = Vector2.Distance(screen, UnitScreen(troops[i].Id));
                    if (d < bestDist) { bestDist = d; bestId = troops[i].Id; }
                }
            }
            return bestId;
        }

        public IEnumerable<object> SnapshotActors() => units.Values.Select(u => u.Snapshot());

        private BattleUnit GetOrCreate(int id, int kind, bool isEnemy, bool isBoss)
        {
            string visualKey = kind + "/" + isEnemy + "/" + isBoss;
            if (units.TryGetValue(id, out var existing))
            {
                if (existing != null && !existing.IsEnded && existing.VisualKey == visualKey)
                {
                    return existing;
                }
                RemoveUnit(id);
            }

            var root = new GameObject((isEnemy ? "Enemy_" : "Troop_") + id).transform;
            root.SetParent(Owner.WorldRoot, false);
            var unit = new BattleUnit(root, Owner.PrefabPool);
            unit.Bind(
                id,
                kind,
                isEnemy,
                isBoss,
                visualKey,
                Owner.CameraControl.Camera,
                Owner.RingMesh,
                Owner.GetSharedMaterial,
                BattleView.ColorFor);
            units[id] = unit;
            return unit;
        }

        private void RemoveUnit(int id)
        {
            if (!units.TryGetValue(id, out var unit)) return;
            units.Remove(id);
            unit.End();
            unit.ReleaseResources();
        }

        public override void End()
        {
            foreach (var kv in units)
            {
                kv.Value.End();
            }
        }

        public override void ReleaseResources()
        {
            var all = new List<BattleUnit>(units.Values);
            units.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                all[i].ReleaseResources();
            }
        }
    }
}
