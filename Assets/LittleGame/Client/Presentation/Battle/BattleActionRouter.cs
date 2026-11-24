using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Client.SkillTimeline;
using LittleGame.Client.Units;
using LittleGame.Combat;
using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 战斗事件路由器（参考 Demo BattleActionRouter）：
    /// 消费纯规则层产出的 CombatEvent，分发给技能时间轴演出、飘字与兜底动作，保持严格单向数据流。
    /// </summary>
    public sealed class BattleActionRouter
    {
        private readonly CombatUnitRegistry unitRegistry;
        private readonly SkillTimelineDirector timelineDirector;
        private readonly BattleFloatingText floatingText;
        private readonly Func<int, Vector3> getLaneCenter;
        private long lastSequence;

        public BattleActionRouter(
            CombatUnitRegistry unitRegistry,
            SkillTimelineDirector timelineDirector,
            BattleFloatingText floatingText,
            Func<int, Vector3> getLaneCenter)
        {
            this.unitRegistry = unitRegistry;
            this.timelineDirector = timelineDirector;
            this.floatingText = floatingText;
            this.getLaneCenter = getLaneCenter;
        }

        public void Reset()
        {
            lastSequence = 0;
        }

        public void ProcessEvents(BattleSession session)
        {
            if (session.LastSequence < lastSequence)
            {
                lastSequence = 0;
                timelineDirector?.ClearAll();
                floatingText?.ClearAll();
            }

            var batch = session.EventsAfter(lastSequence).ToArray();
            if (batch.Length == 0) return;

            var skillCasters = new HashSet<int>();
            for (int i = 0; i < batch.Length; i++)
            {
                lastSequence = Math.Max(lastSequence, batch[i].Sequence);
                if (batch[i].Type == "skill") skillCasters.Add(batch[i].Source);
            }

            for (int i = 0; i < batch.Length; i++)
            {
                var ev = batch[i];
                EmitFloatingFeedback(ev);
                // 同一帧释放技能附带的伤害命中不再重复触发普攻动作时间轴
                if (ev.Type == "hit" && skillCasters.Contains(ev.Source)) continue;
                DispatchTimeline(session, ev);
            }
        }

        private void DispatchTimeline(BattleSession session, CombatEvent ev)
        {
            if (timelineDirector == null) return;
            Vector3 lanePos = getLaneCenter(ev.Lane);
            bool played = false;

            switch (ev.Type)
            {
                case "skill":
                case "hit":
                case "skill_tick":
                {
                    var troop = session.Troops.FirstOrDefault(t => t.Id == ev.Source);
                    if (troop != null)
                    {
                        string trigger = ev.Type == "skill" ? "Skill" : ev.Type == "skill_tick" ? "SkillTick" : "BasicAttack";
                        string branch = troop.Branch == SkillBranch.None ? "" : troop.Branch.ToString();
                        played = timelineDirector.Trigger(new SkillTriggerContext(
                            ev.Source, ev.Target, ev.Lane, troop.Kind.ToString(), trigger, branch, troop.Star, lanePos));
                    }
                    break;
                }
                case "troop_hit":
                {
                    var enemy = session.Enemies.FirstOrDefault(e => e.Id == ev.Source);
                    string kind = enemy != null ? enemy.Kind.ToString() : "Grunt";
                    played = timelineDirector.Trigger(new SkillTriggerContext(
                        ev.Source, ev.Target, ev.Lane, kind, "EnemyAttack", "", 1, lanePos));
                    break;
                }
                case "heal":
                    played = timelineDirector.Trigger(new SkillTriggerContext(ev.Source, ev.Target, ev.Lane, "Healer", "Heal", "", 1, lanePos));
                    break;
                case "boss_telegraph":
                    played = timelineDirector.Trigger(new SkillTriggerContext(ev.Source, 0, ev.Lane, "Giant", "BossTelegraph", "", 1, lanePos));
                    break;
                case "boss_slam":
                    played = timelineDirector.Trigger(new SkillTriggerContext(ev.Source, 0, ev.Lane, "Giant", "BossSlam", "", 1, lanePos));
                    break;
                case "protect":
                    played = timelineDirector.Trigger(new SkillTriggerContext(0, 0, ev.Lane, "*", "Protect", "", 1, lanePos));
                    break;
                case "focus":
                    played = timelineDirector.Trigger(new SkillTriggerContext(0, ev.Target, ev.Lane, "*", "Focus", "", 1, lanePos));
                    break;
                case "merged":
                case "branch_chosen":
                    played = timelineDirector.Trigger(new SkillTriggerContext(ev.Source, ev.Source, ev.Lane, "*", "Upgrade", "", 2, lanePos));
                    break;
            }

            if (played) return;
            if ((ev.Type == "skill" || ev.Type == "hit" || ev.Type == "troop_hit") && unitRegistry.TryGet(ev.Source, out var actor))
                actor.Motion?.Attack();
            if ((ev.Type == "hit" || ev.Type == "troop_hit") && unitRegistry.TryGet(ev.Target, out var injured))
                injured.Motion?.Hit();
            if (ev.Type == "heal" && unitRegistry.TryGet(ev.Source, out var healer))
                healer.Motion?.Attack();
            if (ev.Type == "boss_slam" && unitRegistry.TryGet(ev.Source, out var giant))
                giant.Motion?.Slam();
        }

        private void EmitFloatingFeedback(CombatEvent ev)
        {
            if (floatingText == null) return;
            switch (ev.Type)
            {
                case "hit":
                case "skill_tick":
                    if (ev.Amount > 0 && TryGetOverhead(ev.Target, out var enemyPos))
                    {
                        bool magic = ev.Message != null && ev.Message.Contains("法术");
                        floatingText.Spawn(enemyPos, "-" + Mathf.RoundToInt(ev.Amount),
                            magic ? new Color(0.68f, 0.85f, 1f) : new Color(1f, 0.92f, 0.65f),
                            ev.Type == "skill_tick" ? 0.9f : 1.05f, 0.58f);
                    }
                    break;
                case "troop_hit":
                    if (ev.Amount > 0 && TryGetOverhead(ev.Target, out var troopPos))
                    {
                        floatingText.Spawn(troopPos, "-" + Mathf.RoundToInt(ev.Amount),
                            new Color(1f, 0.45f, 0.42f), 0.95f, 0.55f);
                    }
                    break;
                case "heal":
                    if (ev.Amount > 0 && TryGetOverhead(ev.Target, out var healPos))
                    {
                        floatingText.Spawn(healPos, "+" + Mathf.RoundToInt(ev.Amount),
                            new Color(0.45f, 1f, 0.62f), 1.05f, 0.65f);
                    }
                    break;
                case "freeze":
                    if (TryGetOverhead(ev.Target, out var freezePos))
                        floatingText.Spawn(freezePos, "冻结", new Color(0.48f, 0.92f, 1f), 1.0f, 0.7f);
                    break;
                case "boss_control_resisted":
                    if (TryGetOverhead(ev.Target, out var bossPos))
                        floatingText.Spawn(bossPos, "免疫冻结·减速", new Color(1f, 0.78f, 0.42f), 1.05f, 0.75f);
                    break;
                case "protect":
                    floatingText.Spawn(getLaneCenter(ev.Lane) + Vector3.up * 1.1f, "护盾保护",
                        new Color(0.45f, 0.88f, 1f), 1.15f, 0.8f);
                    break;
            }
        }

        private bool TryGetOverhead(int unitId, out Vector3 pos)
        {
            if (unitRegistry.TryGet(unitId, out var actor) && actor != null)
            {
                pos = actor.OverheadPoint != null ? actor.OverheadPoint.position : actor.transform.position + Vector3.up * 1.3f;
                return true;
            }
            pos = Vector3.zero;
            return false;
        }
    }
}
