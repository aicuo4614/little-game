using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Combat;
using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 战斗动作演出与事件分发子系统（对齐 Demo BattleActionControl + ActionHandlerBase）：
    /// 消费纯规则层产出的 CombatEvent，驱动并发 3D TimelinePlayback、伤害飘字、受击闪白与升星烟雾。
    /// </summary>
    public sealed class BattleActionControl : BattleControlBase
    {
        private readonly BattleSkillLibrary skillLibrary = new BattleSkillLibrary();
        private readonly List<ActionHandlerBase> handlers = new List<ActionHandlerBase>();
        private readonly List<BattleSkillTimeline> activeTimelines = new List<BattleSkillTimeline>();
        private readonly Dictionary<int, BattleSkillTimeline> activeBySkillInstance = new Dictionary<int, BattleSkillTimeline>();

        private long lastSequence;
        private int totalPlayed;
        private int totalCancelled;
        private string lastPlayedId = string.Empty;

        public BattleSkillLibrary Library => skillLibrary;
        public int ActiveCount => activeTimelines.Count;
        public int TotalPlayed => totalPlayed;

        public override void Init(BattleView owner)
        {
            base.Init(owner);
            skillLibrary.Reload();
            RegisterHandler(new CastStartedActionHandler());
            RegisterHandler(new CastCancelledActionHandler());
            RegisterHandler(new HitAndFeedbackActionHandler());
            RegisterHandler(new MergeAndSpecialActionHandler());
        }

        private void RegisterHandler(ActionHandlerBase handler)
        {
            handler.Init(this);
            handlers.Add(handler);
        }

        public void ProcessEvents(BattleSession session)
        {
            if (session == null) return;
            if (session.LastSequence < lastSequence)
            {
                lastSequence = 0;
                ClearAll();
            }

            var batch = session.EventsAfter(lastSequence).ToArray();
            if (batch.Length == 0) return;

            for (int i = 0; i < batch.Length; i++)
            {
                var ev = batch[i];
                lastSequence = Math.Max(lastSequence, ev.Sequence);

                for (int h = 0; h < handlers.Count; h++)
                {
                    if (handlers[h].CanHandle(ev))
                    {
                        handlers[h].Handle(session, ev);
                    }
                }
            }
        }

        public void Step(float scaledDt, bool paused)
        {
            if (paused || scaledDt <= 0f) return;

            for (int i = activeTimelines.Count - 1; i >= 0; i--)
            {
                var tl = activeTimelines[i];
                tl.Tick(scaledDt);
                if (tl.IsFinished)
                {
                    if (tl.SkillInstanceId > 0) activeBySkillInstance.Remove(tl.SkillInstanceId);
                    activeTimelines.RemoveAt(i);
                }
            }
        }

        public bool PlayTimeline(string timelineId, int skillInstanceId, int sourceUnitId, int targetUnitId, Vector3 fallbackPoint)
        {
            var cfg = skillLibrary.Get(timelineId);
            if (cfg == null)
            {
                return false;
            }

            var caster = Owner.UnitsControl.FindUnit(sourceUnitId);
            var target = Owner.UnitsControl.FindUnit(targetUnitId);
            var runner = new BattleSkillTimeline(Owner, cfg, skillInstanceId, caster, target, fallbackPoint);
            if (!runner.Start())
            {
                return false;
            }

            activeTimelines.Add(runner);
            if (skillInstanceId > 0)
            {
                activeBySkillInstance[skillInstanceId] = runner;
            }

            totalPlayed++;
            lastPlayedId = timelineId;
            return true;
        }

        public void CancelSkillInstance(int skillInstanceId)
        {
            if (skillInstanceId <= 0) return;
            if (activeBySkillInstance.TryGetValue(skillInstanceId, out var runner))
            {
                activeBySkillInstance.Remove(skillInstanceId);
                activeTimelines.Remove(runner);
                runner.Stop();
                totalCancelled++;
            }
        }

        public void ClearAll()
        {
            for (int i = activeTimelines.Count - 1; i >= 0; i--)
            {
                activeTimelines[i].Stop();
            }
            activeTimelines.Clear();
            activeBySkillInstance.Clear();
            Owner.CameraControl?.ResetShake();
            Owner.FieldOverlayControl?.ResetDarkenImmediate();
        }

        public object Snapshot() => new
        {
            engine = "shared:TimelinePlayback/TimelinePlayer",
            loadedTimelines = skillLibrary.Timelines.Count,
            loadedBindings = skillLibrary.Bindings?.Bindings?.Count ?? 0,
            activeCount = activeTimelines.Count,
            totalPlayed,
            totalCancelled,
            lastPlayedId,
            active = activeTimelines.Select(t => t.Diagnostics).ToArray(),
        };

        public override void End()
        {
            ClearAll();
        }

        internal bool TryGetOverhead(int unitId, out Vector3 pos)
        {
            if (Owner.UnitsControl.TryGet(unitId, out var unit) && unit != null && !unit.IsEnded)
            {
                pos = unit.OverheadPoint != null ? unit.OverheadPoint.position : unit.Transform.position + Vector3.up * 1.3f;
                return true;
            }
            pos = Vector3.zero;
            return false;
        }

        // ---- 具体事件处理器 ----

        private sealed class CastStartedActionHandler : ActionHandlerBase
        {
            public override bool CanHandle(CombatEvent ev) => ev.Type == "cast_started";

            public override void Handle(BattleSession session, CombatEvent ev)
            {
                var fallback = new Vector3(ev.TargetX, 0.14f, ev.TargetZ);
                if (!Control.PlayTimeline(ev.TimelineId, ev.SkillInstanceId, ev.Source, ev.Target, fallback))
                {
                    if (View.UnitsControl.TryGet(ev.Source, out var unit) && !unit.IsDead)
                    {
                        unit.AnimatorControl.PlayAttack();
                    }
                }
            }
        }

        private sealed class CastCancelledActionHandler : ActionHandlerBase
        {
            public override bool CanHandle(CombatEvent ev) => ev.Type == "cast_cancelled";

            public override void Handle(BattleSession session, CombatEvent ev)
            {
                Control.CancelSkillInstance(ev.SkillInstanceId);
            }
        }

        private sealed class HitAndFeedbackActionHandler : ActionHandlerBase
        {
            public override bool CanHandle(CombatEvent ev) =>
                ev.Type == "hit" || ev.Type == "troop_hit" || ev.Type == "skill_tick"
                || ev.Type == "heal" || ev.Type == "freeze" || ev.Type == "boss_control_resisted";

            public override void Handle(BattleSession session, CombatEvent ev)
            {
                var fx = View.EffectControl;
                switch (ev.Type)
                {
                    case "hit":
                    case "skill_tick":
                        if (ev.Amount > 0 && Control.TryGetOverhead(ev.Target, out var enemyPos))
                        {
                            bool magic = ev.Message != null && ev.Message.Contains("法术");
                            fx.SpawnFloatingText(enemyPos, "-" + Mathf.RoundToInt(ev.Amount),
                                magic ? new Color(0.68f, 0.85f, 1f) : new Color(1f, 0.92f, 0.65f),
                                ev.Type == "skill_tick" ? 0.9f : 1.05f, 0.58f);
                        }
                        if (View.UnitsControl.TryGet(ev.Target, out var hitTarget) && !hitTarget.IsDead)
                        {
                            hitTarget.GraphicControl.TriggerHitFlash(0.11f);
                        }
                        if (ev.Type == "skill_tick" && ev.SkillInstanceId <= 0)
                        {
                            Control.PlayTimeline("storm_tick", 0, ev.Source, ev.Target, View.GetLaneCenter(ev.Lane));
                        }
                        break;

                    case "troop_hit":
                        if (ev.Amount > 0 && Control.TryGetOverhead(ev.Target, out var troopPos))
                        {
                            fx.SpawnFloatingText(troopPos, "-" + Mathf.RoundToInt(ev.Amount),
                                new Color(1f, 0.45f, 0.42f), 0.95f, 0.55f);
                        }
                        if (View.UnitsControl.TryGet(ev.Target, out var injuredTroop) && !injuredTroop.IsDead)
                        {
                            injuredTroop.GraphicControl.TriggerHitFlash(0.11f);
                        }
                        break;

                    case "heal":
                        if (ev.Amount > 0 && Control.TryGetOverhead(ev.Target, out var healPos))
                        {
                            fx.SpawnFloatingText(healPos, "+" + Mathf.RoundToInt(ev.Amount),
                                new Color(0.45f, 1f, 0.62f), 1.05f, 0.65f);
                        }
                        break;

                    case "freeze":
                        if (Control.TryGetOverhead(ev.Target, out var freezePos))
                        {
                            fx.SpawnFloatingText(freezePos, "冻结", new Color(0.48f, 0.92f, 1f), 1.0f, 0.7f);
                        }
                        break;

                    case "boss_control_resisted":
                        if (Control.TryGetOverhead(ev.Target, out var bossPos))
                        {
                            fx.SpawnFloatingText(bossPos, "免疫冻结·减速", new Color(1f, 0.78f, 0.42f), 1.05f, 0.75f);
                        }
                        break;
                }
            }
        }

        private sealed class MergeAndSpecialActionHandler : ActionHandlerBase
        {
            public override bool CanHandle(CombatEvent ev) =>
                ev.Type == "merged" || ev.Type == "branch_chosen" || ev.Type == "protect"
                || ev.Type == "focus" || ev.Type == "boss_telegraph" || ev.Type == "boss_slam";

            public override void Handle(BattleSession session, CombatEvent ev)
            {
                var laneCenter = View.GetLaneCenter(ev.Lane);
                switch (ev.Type)
                {
                    case "merged":
                    {
                        int star = Mathf.Clamp(Mathf.RoundToInt(ev.Amount), 2, 3);
                        if (View.UnitsControl.TryGet(ev.Source, out var unit))
                        {
                            var body = unit.BodyPoint != null ? unit.BodyPoint.position : unit.Transform.position + Vector3.up * 0.55f;
                            View.EffectControl.SpawnMergeSmokeVfx(body, star);
                        }
                        if (Control.TryGetOverhead(ev.Source, out var mergePos))
                        {
                            Color col = star >= 3 ? new Color(1f, 0.82f, 0.16f) : new Color(0.32f, 0.84f, 1f);
                            View.EffectControl.SpawnFloatingText(mergePos + Vector3.up * 0.25f, new string('★', star) + " 升星!", col, 1.25f, 0.95f);
                        }
                        Control.PlayTimeline("unit_upgrade", 0, ev.Source, ev.Source, laneCenter);
                        break;
                    }
                    case "branch_chosen":
                        Control.PlayTimeline("unit_upgrade", 0, ev.Source, ev.Source, laneCenter);
                        break;
                    case "protect":
                        View.EffectControl.SpawnFloatingText(laneCenter + Vector3.up * 1.1f, "护盾保护", new Color(0.45f, 0.88f, 1f), 1.15f, 0.8f);
                        Control.PlayTimeline("tactic_protect", 0, 0, 0, laneCenter);
                        break;
                    case "focus":
                        Control.PlayTimeline("tactic_focus", 0, 0, ev.Target, laneCenter);
                        break;
                    case "boss_telegraph":
                        Control.PlayTimeline("giant_telegraph", 0, ev.Source, 0, laneCenter);
                        break;
                    case "boss_slam":
                        Control.PlayTimeline("giant_slam", 0, ev.Source, 0, laneCenter);
                        break;
                }
            }
        }
    }
}
