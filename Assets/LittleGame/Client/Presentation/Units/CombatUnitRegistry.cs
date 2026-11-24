using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Combat;
using UnityEngine;

namespace LittleGame.Client.Units
{
    /// <summary>
    /// 战场单位表现注册表（参考 Demo Unit 管理分层）：
    /// 负责全部 CombatUnitActor 的创建、复用、状态同步、清理与屏幕拾取。
    /// </summary>
    public sealed class CombatUnitRegistry
    {
        private readonly Dictionary<int, CombatUnitActor> actors = new Dictionary<int, CombatUnitActor>();
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
            var activeIds = new HashSet<int>();
            bool paused = session.Paused || session.Aim != AimKind.None;

            foreach (var t in session.Troops.Where(t => t.Deployed))
            {
                activeIds.Add(t.Id);
                var actor = GetOrCreate(t.Id, (int)t.Kind, false, false);
                float scale = t.Star == 3 ? 1.18f : t.Star == 2 ? 1.08f : 1f;
                actor.SyncState(
                    new Vector3(t.X, 0.11f, t.Z),
                    t.FacingYaw,
                    t.Hp / Mathf.Max(1f, t.MaxHp),
                    new string('★', t.Star),
                    t.Shield > 0f,
                    scale,
                    t.Alive || session.Phase != RunPhase.Fighting,
                    session.Phase != RunPhase.Fighting,
                    session.Phase == RunPhase.Fighting,
                    paused,
                    t.Star,
                    t.Branch,
                    forceWalk: (marching || t.NeedsRegroup) && !paused);
            }

            foreach (var e in session.Enemies)
            {
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
                    e.Alive && session.Phase == RunPhase.Fighting && !frozen,
                    paused || frozen);
            }

            foreach (var id in actors.Keys.Where(id => !activeIds.Contains(id)).ToArray())
            {
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
            var ids = enemy
                ? session.Enemies.Where(e => e.Alive).Select(e => e.Id)
                : session.Troops.Where(t => t.Deployed).Select(t => t.Id);
            return ids
                .Select(id => new { id, d = Vector2.Distance(screen, UnitScreen(id)) })
                .Where(x => x.d < Mathf.Max(23f, Screen.width * 0.065f))
                .OrderBy(x => x.d)
                .Select(x => x.id)
                .FirstOrDefault();
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
