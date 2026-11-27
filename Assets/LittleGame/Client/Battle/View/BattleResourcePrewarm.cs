using System;
using System.Collections.Generic;
using LittleGame.Combat;

namespace LittleGame.Client.Battle
{
    /// <summary>按本关十波与可招募英雄收集模型、普攻、主动技能的特效；开战前每种入池一只。</summary>
    public sealed class BattleResourcePrewarm
    {
        private readonly HashSet<string> roles = new HashSet<string>();
        private readonly HashSet<string> timelines = new HashSet<string>();
        private readonly HashSet<string> effects = new HashSet<string>();
        public bool Ready { get; private set; }
        public double ElapsedMs { get; private set; }

        public void Prepare(BattleView view)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var tables = BattleTables.Current;
            var unitIds = new HashSet<string>();
            for (int wave = 1; wave <= tables.TotalWaves; wave++)
                foreach (var spawn in tables.Schedule(wave, view.App.Session.Seed)) unitIds.Add(spawn.MonsterId);
            foreach (UnitJob job in Enum.GetValues(typeof(UnitJob)))
                foreach (string hero in tables.RecruitPool(job)) unitIds.Add(hero);
            foreach (var id in unitIds)
            {
                var unit = tables.Unit(id);
                roles.Add(unit.Role);
                for (int star = 1; star <= 3; star++)
                {
                    AddTimeline(view, unit.BasicTiming, unit.Grade == UnitGrade.Hero ? "BasicAttack" : "EnemyAttack", star);
                    foreach (var skill in unit.Skills)
                        if (skill != null && !skill.Passive)
                            AddTimeline(view, skill.Timing, skill.Effect == SkillEffect.Heal ? "Heal" : skill.Effect == SkillEffect.Slam ? "BossSlam" : "Skill", star);
                    if (unit.Grade == UnitGrade.Boss) AddTimeline(view, unit.BasicTiming, "BossTelegraph", star);
                }
            }
            AddTimeline(view, "*", "Upgrade", 1);
            foreach (string role in roles)
                view.PrefabPool.Prewarm("Unit/Low/" + role + "/" + role, ResourcePackageName.UnitPackage);
            foreach (string timeline in timelines)
            {
                var config = view.ActionControl.Library.Get(timeline);
                if (config == null) continue;
                foreach (string effect in TimelinePlayback.EffectPaths(config)) effects.Add(effect);
            }
            foreach (string effect in effects) view.EffectControl.Prewarm(effect);
            Ready = true;
            ElapsedMs = clock.Elapsed.TotalMilliseconds;
        }

        private void AddTimeline(BattleView view, string kind, string trigger, int star)
        {
            string id = view.ActionControl.Library.ResolveTimelineId(kind, trigger, star: star);
            if (!string.IsNullOrEmpty(id)) timelines.Add(id);
        }

        public object Snapshot() => new { ready = Ready, elapsedMs = ElapsedMs, models = roles, timelines, effects, instancesPerPrefab = 1 };
    }
}
