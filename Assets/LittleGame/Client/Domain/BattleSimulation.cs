using System;
using System.Linq;

namespace LittleGame.Combat
{
    public sealed partial class BattleSession
    {
        private CommandResult StartWave()
        {
            if (pending.Count > 0) return Reject("先处理暂存单位");
            if (!troops.Any(t => t.Deployed)) return Reject("至少部署一名单位");
            if (troops.Any(t => t.Star >= 2 && t.Branch == SkillBranch.None)) return Reject("先为升星单位选择分支");
            Phase = RunPhase.Fighting; BattleTime = accumulator = 0; Tactics = 1; Aim = AimKind.None; Paused = false;
            enemies.Clear(); zones.Clear(); spawns = BattleDefinitions.Wave(Wave); spawnCursor = 0;
            focusTarget = 0; focusUntil = 0; bossNextSlam = 5; slamAt = -1; slamLane = -1;
            foreach (var t in troops) ResetTroop(t);
            Emit("wave_started", FencesUp ? "普通波：近战本路，远程支援相邻路" : "首领波：隔栏撤下，近战可跨路"); return new CommandResult(true, "战斗开始");
        }
        private CommandResult CombatCommand(BattleCommand c)
        {
            if (c.Kind == CommandKind.Aim)
            {
                if (Paused || Tactics <= 0 || c.Aim == AimKind.None || !Enum.IsDefined(typeof(AimKind), c.Aim)) return Reject("当前不能瞄准");
                Aim = c.Aim; return new CommandResult(true, "瞄准中，战斗暂停；可取消");
            }
            if (c.Kind == CommandKind.Focus)
            {
                if (Aim != AimKind.Focus || Tactics <= 0) return Reject("先进入集火瞄准");
                var target = enemies.Find(e => e.Id == c.UnitId && e.Alive);
                if (target == null) return Reject("目标已失效，可重选或取消");
                focusTarget = target.Id; focusUntil = BattleTime + 4; Tactics--; Aim = AimKind.None;
                Emit("focus", "集火目标，仍受射程与隔栏支援范围限制", 0, target.Id); return new CommandResult(true, "已集火");
            }
            if (c.Kind == CommandKind.Protect)
            {
                if (Aim != AimKind.Protect || Tactics <= 0 || c.Lane < 0 || c.Lane > 2) return Reject("先进入保护瞄准，并选择0到2路");
                var targets = troops.Where(t => t.Deployed && t.Alive && t.Lane == c.Lane).ToArray();
                if (targets.Length == 0) return Reject("该路没有存活友军，可重选或取消");
                foreach (var t in targets) { t.Shield = Math.Max(t.Shield, t.MaxHp * .25f); t.ShieldUntil = BattleTime + 4; }
                Tactics--; Aim = AimKind.None; Emit("protect", "该路获得4秒护盾", 0, 0, c.Lane); return new CommandResult(true, "已保护");
            }
            return Reject("战斗中不能招募、移动或合成");
        }
        public void Advance(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0 || seconds > 60) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (Phase != RunPhase.Fighting || Paused || Aim != AimKind.None) return;
            accumulator += seconds * Speed;
            while (accumulator + .000001f >= BattleDefinitions.Step && Phase == RunPhase.Fighting)
            { accumulator = Math.Max(0, accumulator - BattleDefinitions.Step); Step(BattleDefinitions.Step); }
        }
        private void Step(float dt)
        {
            BattleTime += dt;
            while (spawnCursor < spawns.Count && spawns[spawnCursor].At <= BattleTime) Spawn(spawns[spawnCursor++]);
            foreach (var t in troops.Where(t => t.Deployed && t.Alive).ToArray())
            {
                if (t.ShieldUntil <= BattleTime) t.Shield = 0;
                t.Cooldown -= dt; t.SkillCooldown -= dt;
                var target = Target(t);
                if (target == null && t.Kind == TroopKind.Shield)
                {
                    var approach = enemies.Where(e => e.Alive && (!FencesUp || e.Lane == t.Lane)).OrderBy(e => Distance(t.X, t.Z, e.X, e.Z)).FirstOrDefault();
                    if (approach != null)
                    {
                        float x = FencesUp ? BattleDefinitions.LaneX(t.Lane) : approach.X + (t.Lane - 1) * .7f;
                        Move(t, x, Math.Min(2.6f, approach.Z - .65f), 1.8f * dt);
                        target = Target(t);
                    }
                }
                if (target == null) continue;
                if (t.Cooldown <= 0)
                {
                    Damage(t, target, BattleDefinitions.Attack(t), t.Kind == TroopKind.Storm || t.Kind == TroopKind.Frost, false);
                    t.Cooldown = t.Kind == TroopKind.Archer ? .9f : 1.2f;
                }
                if (target.Alive && t.SkillCooldown <= 0) Skill(t, target);
            }
            foreach (var zone in zones.ToArray())
            {
                zone.Remaining -= dt; zone.UntilTick -= dt;
                if (zone.UntilTick <= 0)
                {
                    zone.UntilTick += .5f;
                    var caster = troops.Find(t => t.Id == zone.Source);
                    float radius = rewards.Contains(RewardKind.WideStorm) ? 1.7f : 1.25f;
                    if (caster != null) foreach (var e in enemies.Where(e => e.Alive && Distance(e.X, e.Z, zone.X, zone.Z) <= radius).ToArray()) Damage(caster, e, zone.Damage, true, true);
                }
                if (zone.Remaining <= 0) zones.Remove(zone);
            }
            foreach (var e in enemies.Where(e => e.Alive).ToArray()) EnemyStep(e, dt);
            if (Phase != RunPhase.Fighting) return;
            if (BattleTime >= 45) foreach (var e in enemies.Where(e => e.Alive).ToArray()) { Escape(e); if (Phase == RunPhase.Lost) return; }
            if (spawnCursor == spawns.Count && !enemies.Any(e => e.Alive)) FinishWave();
        }
        private Enemy Target(Troop t)
        {
            var reachable = enemies.Where(e => BattleDefinitions.CanReach(t, e, !FencesUp));
            if (t.Kind != TroopKind.Shield && focusUntil > BattleTime)
            { var focus = reachable.FirstOrDefault(e => e.Id == focusTarget); if (focus != null) return focus; }
            return reachable.OrderBy(e => e.Lane == t.Lane ? 0 : 1).ThenBy(e => Distance(t.X, t.Z, e.X, e.Z)).ThenBy(e => e.Id).FirstOrDefault();
        }
        private void Spawn(WaveSpawn spawn)
        {
            float hp = spawn.Kind == EnemyKind.Giant ? (Wave == 6 ? 700 : 1200) : (spawn.Kind == EnemyKind.Armored ? 130 : spawn.Kind == EnemyKind.Runner ? 50 : 65) * (1 + (Wave - 1) * .085f);
            var e = new Enemy { Id = nextId++, Kind = spawn.Kind, Lane = spawn.Lane, X = BattleDefinitions.LaneX(spawn.Lane), Z = 4.4f, MaxHp = hp, Hp = hp, Cooldown = 1 };
            enemies.Add(e); Emit("enemy_spawned", BattleDefinitions.Name(e.Kind), 0, e.Id, e.Lane);
        }
        private void Damage(Troop source, Enemy target, float amount, bool magic, bool indirect)
        {
            if (!target.Alive) return;
            float defense = target.Kind == EnemyKind.Armored || target.Boss ? (magic ? 8 : 100) : 0;
            amount = Math.Max(1, (float)Math.Floor(amount * 100 / (100 + defense)));
            target.Hp = Math.Max(0, target.Hp - amount);
            Emit(indirect ? "skill_tick" : "hit", magic ? "法术命中" : "物理命中", source.Id, target.Id, target.Lane, amount);
            if (!target.Alive) Emit("enemy_killed", "消灭" + BattleDefinitions.Name(target.Kind), source.Id, target.Id, target.Lane);
        }
        private void Hurt(Enemy source, Troop target, float amount)
        {
            float absorbed = Math.Min(target.Shield, amount); target.Shield -= absorbed;
            target.Hp = Math.Max(0, target.Hp - Math.Max(0, amount - absorbed));
            Emit("troop_hit", "友军受击", source.Id, target.Id, target.Lane, amount - absorbed);
            if (!target.Alive) Emit("troop_down", "单位倒下，下波恢复", source.Id, target.Id, target.Lane);
        }
        private void Skill(Troop t, Enemy target)
        {
            float attack = BattleDefinitions.Attack(t);
            Emit("skill", BattleDefinitions.Name(t.Kind) + "释放技能", t.Id, target.Id, target.Lane);
            switch (t.Kind)
            {
                case TroopKind.Shield:
                    t.Shield = Math.Max(t.Shield, t.MaxHp * (t.Branch == SkillBranch.Bastion ? .32f : .2f)); t.ShieldUntil = BattleTime + 3;
                    if (t.Branch == SkillBranch.WarCry) foreach (var ally in troops.Where(a => a.Alive && a.Deployed && a.Lane == t.Lane)) { ally.Shield = Math.Max(ally.Shield, ally.MaxHp * .12f); ally.ShieldUntil = BattleTime + 3; }
                    Damage(t, target, attack * 1.3f, false, false); t.SkillCooldown = 7; break;
                case TroopKind.Archer:
                    Damage(t, target, attack * 1.5f, false, false);
                    if (target.Alive && !target.Boss) { target.Z = Math.Min(5, target.Z + (t.Branch == SkillBranch.Gale ? 1.25f : .65f)); Emit("knockback", "击退", t.Id, target.Id, target.Lane); }
                    if (t.Branch == SkillBranch.Pierce) foreach (var e in enemies.Where(e => e.Alive && e.Id != target.Id && e.Lane == target.Lane && BattleDefinitions.CanReach(t, e, !FencesUp)).Take(2).ToArray()) Damage(t, e, attack, false, true);
                    t.SkillCooldown = t.Branch == SkillBranch.Gale ? 4.5f : 6; break;
                case TroopKind.Storm:
                    if (t.Branch == SkillBranch.ChainLightning)
                    {
                        int count = rewards.Contains(RewardKind.WideStorm) ? 4 : 3;
                        foreach (var e in enemies.Where(e => e.Alive && Distance(target.X, target.Z, e.X, e.Z) <= 3).OrderBy(e => e.Id == target.Id ? 0 : 1).ThenBy(e => e.Id).Take(count).ToArray()) Damage(t, e, attack * 1.6f, true, true);
                    }
                    else zones.Add(new StormZone { Source = t.Id, Lane = target.Lane, X = target.X, Z = target.Z, Remaining = t.Branch == SkillBranch.StormPool ? 3 : 2, UntilTick = .5f, Damage = attack * .55f });
                    t.SkillCooldown = 7; break;
                case TroopKind.Frost:
                    float duration = (t.Branch == SkillBranch.DeepFreeze ? 1.7f : 1) + (rewards.Contains(RewardKind.LongFreeze) ? .5f : 0);
                    if (target.Boss) { target.SlowUntil = BattleTime + 1; Emit("boss_control_resisted", "首领免疫冻结，转为减速", t.Id, target.Id); }
                    else { target.FrozenUntil = Math.Max(target.FrozenUntil, BattleTime + duration); Emit("freeze", "冻结", t.Id, target.Id, target.Lane, duration); }
                    Damage(t, target, attack * 1.15f, true, false);
                    if (t.Branch == SkillBranch.IceArmor) foreach (var ally in troops.Where(a => a.Deployed && a.Alive && a.Lane == t.Lane)) { ally.Shield = Math.Max(ally.Shield, ally.MaxHp * .12f); ally.ShieldUntil = BattleTime + 3; }
                    t.SkillCooldown = 8; break;
            }
        }
        private void EnemyStep(Enemy e, float dt)
        {
            if (e.FrozenUntil > BattleTime) return;
            e.Cooldown -= dt;
            var defenders = troops.Where(t => t.Deployed && t.Alive && (e.Boss || t.Lane == e.Lane));
            var target = defenders.OrderBy(t => Distance(t.X, t.Z, e.X, e.Z)).ThenBy(t => t.Id).FirstOrDefault();
            if (e.Kind == EnemyKind.Healer && e.Cooldown <= 0)
            {
                var heal = enemies.Where(a => a.Alive && a.Id != e.Id && a.Hp < a.MaxHp).OrderBy(a => a.Hp / a.MaxHp).FirstOrDefault();
                if (heal != null) { heal.Hp = Math.Min(heal.MaxHp, heal.Hp + 23); e.Cooldown = 3; Emit("heal", "治疗师为敌人回血", e.Id, heal.Id, heal.Lane, 23); }
            }
            if (e.Boss)
            {
                if (slamAt < 0 && BattleTime >= bossNextSlam)
                {
                    slamLane = Enumerable.Range(0, 3).OrderByDescending(l => troops.Count(t => t.Deployed && t.Alive && t.Lane == l)).ThenBy(l => Math.Abs(l - 1)).First();
                    slamAt = BattleTime + 2; Emit("boss_telegraph", "巨像蓄力：保护预警路线", e.Id, 0, slamLane);
                }
                if (slamAt >= 0 && BattleTime >= slamAt)
                {
                    foreach (var t in troops.Where(t => t.Deployed && t.Alive && t.Lane == slamLane).ToArray()) Hurt(e, t, Wave == 6 ? 40 : 65);
                    Emit("boss_slam", "巨像重击", e.Id, 0, slamLane); slamAt = -1; slamLane = -1; bossNextSlam = BattleTime + 7;
                }
            }
            float dx = target == null ? 0 : Math.Max(0, Math.Abs(e.X - target.X) - (e.Boss ? 2.5f : 0));
            float dz = target == null ? 100 : Math.Abs(e.Z - target.Z);
            if (target != null && dx * dx + dz * dz <= .75f * .75f)
            { if (e.Cooldown <= 0) { Hurt(e, target, e.Boss ? 16 : e.Kind == EnemyKind.Armored ? 17 : 10); e.Cooldown = e.Boss ? 1.8f : 1.3f; } }
            else
            {
                float speed = e.Kind == EnemyKind.Runner ? 1.35f : e.Boss ? .35f : .65f;
                if (e.SlowUntil > BattleTime) speed *= .7f;
                // 首领朝最近近战接战点前进，普通敌人始终不穿栏。
                e.Z -= speed * dt;
                if (e.Z <= BattleDefinitions.GateZ) Escape(e);
            }
        }
        private void Escape(Enemy e)
        {
            if (!e.Alive) return; e.Escaped = true;
            int damage = e.Boss ? 10 : e.Kind == EnemyKind.Armored ? 2 : 1; GateHp = Math.Max(0, GateHp - damage);
            Emit("gate_hit", "漏怪，城门扣血", e.Id, 0, e.Lane, damage);
            if (GateHp <= 0) { Phase = RunPhase.Lost; Aim = AimKind.None; Paused = false; Emit("run_lost", "城门生命归零"); }
        }
        private void FinishWave()
        {
            Aim = AimKind.None; Paused = false; zones.Clear();
            if (Wave >= 12) { Phase = RunPhase.Won; Emit("run_won", "守住12波，远征胜利"); }
            else { Phase = Wave == 4 || Wave == 8 ? RunPhase.Reward : RunPhase.WaveResult; Emit("wave_cleared", "本波结束，下波单位满血恢复"); }
        }
        private static float Distance(float x, float z, float xx, float zz) { float dx = x - xx, dz = z - zz; return (float)Math.Sqrt(dx * dx + dz * dz); }
        private static void Move(Troop t, float x, float z, float amount)
        { float dist = Distance(t.X, t.Z, x, z); if (dist <= amount) { t.X = x; t.Z = z; } else if (dist > 0) { t.X += (x - t.X) / dist * amount; t.Z += (z - t.Z) / dist * amount; } }
    }
}
