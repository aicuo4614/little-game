using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Combat;
using UnityEngine;

namespace LittleGame.Client.Units
{
    /// <summary>
    /// 战场单位表现注册表（零每帧 LINQ/HashSet 分配）：
    /// 负责全部 CombatUnitActor 的创建、复用、状态同步、清理与屏幕拾取；
    /// 波次结束后立即清理已死亡敌人表现，杜绝最后一只怪在波后滚动路面残留。
    /// </summary>
    public sealed class CombatUnitRegistry
    {
        private readonly Dictionary<int, CombatUnitActor> actors = new Dictionary<int, CombatUnitActor>(24);
        private readonly HashSet<int> activeIds = new HashSet<int>();
        private readonly List<int> removeBuffer = new List<int>(16);
        private readonly Transform parent;
        private readonly Camera camera;
        private readonly Mesh ringMesh;
        private readonly Func<string, Color, bool, Material> getMaterial;
        private readonly Func<int, Color> colorForTroop;

        public CombatUnitRegistry(
            Transform parent,
            Camera camera,
            Mesh ringMesh,
            Func<string, Color, bool, Material> getMaterial,
            Func<int, Color> colorForTroop)
        {
            this.parent = parent;
            this.camera = camera;
            this.ringMesh = ringMesh;
            this.getMaterial = getMaterial;
            this.colorForTroop = colorForTroop;
        }

        public bool TryGet(int unitId, out CombatUnitActor actor) => actors.TryGetValue(unitId, out actor);

        public void Sync(BattleSession session, bool marching = false)
        {
            activeIds.Clear();
            bool fighting = session.Phase == RunPhase.Fighting;
            bool paused = session.Paused || session.Aim != AimKind.None;

            var troops = session.Troops;
            for (int i = 0; i < troops.Count; i++)
            {
                var t = troops[i];
                if (!t.Deployed) continue;
                activeIds.Add(t.Id);
                var actor = GetOrCreate(t.Id, (int)t.Kind, false, false);
                float scale = t.Star == 3 ? 1.18f : t.Star == 2 ? 1.08f : 1f;
                actor.SyncState(
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
                    t.Star,
                    t.Branch,
                    forceWalk: !fighting && (marching || t.NeedsRegroup) && !paused,
                    movementSpeed: fighting ? Mathf.Sqrt(t.Vx * t.Vx + t.Vz * t.Vz) : -1f);
            }

            // 仅在战斗阶段保留敌方表现（含死亡演出）；波次结束切出 Fighting 后立即清除死怪，消除最后一只怪残留
            if (fighting)
            {
                var enemies = session.Enemies;
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i];
                    activeIds.Add(e.Id);
                    var actor = GetOrCreate(e.Id, (int)e.Kind, true, e.Boss);
                    bool frozen = e.Alive && e.FrozenUntil > session.BattleTime;
                    actor.SyncState(
                        new Vector3(e.X, 0.11f, e.Z),
                        e.FacingYaw,
                        e.Hp / Mathf.Max(1f, e.MaxHp),
                        e.Boss ? BattleDefinitions.Name(e.Kind) : "",
                        frozen,
                        1f,
                        e.Alive,
                        false,
                        e.Alive && !frozen,
                        paused || frozen,
                        movementSpeed: Mathf.Sqrt(e.Vx * e.Vx + e.Vz * e.Vz));
                }
            }

            removeBuffer.Clear();
            foreach (var kv in actors)
            {
                if (!activeIds.Contains(kv.Key))
                    removeBuffer.Add(kv.Key);
            }
            for (int i = 0; i < removeBuffer.Count; i++)
            {
                int id = removeBuffer[i];
                if (actors[id] != null) UnityEngine.Object.Destroy(actors[id].gameObject);
                actors.Remove(id);
            }
        }

        public Vector2 UnitScreen(int id)
        {
            if (actors.TryGetValue(id, out var actor) && actor != null)
                return camera.WorldToScreenPoint(actor.transform.position + Vector3.up * 0.4f);
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

        public IEnumerable<object> SnapshotActors() => actors.Values.Select(a => a.Snapshot());

        private CombatUnitActor GetOrCreate(int id, int kind, bool isEnemy, bool isBoss)
        {
            string visualKey = kind + "/" + isEnemy + "/" + isBoss;
            if (actors.TryGetValue(id, out var existing))
            {
                if (existing != null && existing.VisualKey == visualKey) return existing;
                if (existing != null)
                {
                    existing.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(existing.gameObject);
                }
                actors.Remove(id);
            }

            var go = new GameObject((isEnemy ? "Enemy_" : "Troop_") + id);
            go.transform.SetParent(parent, false);
            var actor = go.AddComponent<CombatUnitActor>();
            actor.Initialize(id, kind, isEnemy, isBoss, visualKey, camera, ringMesh, getMaterial, colorForTroop);
            actors[id] = actor;
            return actor;
        }
    }
}
