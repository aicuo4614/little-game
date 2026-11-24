using System;
using System.Linq;

namespace LittleGame.Combat
{
    public sealed partial class BattleSession
    {
        private CommandResult StartWave()
        {
            if (pending.Count > 0) return Reject("请先处理暂存单位");
            if (!troops.Any(t => t.Deployed)) return Reject("至少部署一名单位");
            if (troops.Any(t => t.Star >= 2 && t.Branch == SkillBranch.None)) return Reject("请先为升星单位选择技能分支");
            Phase = RunPhase.Fighting; BattleTime = accumulator = 0f; Tactics = 1; Aim = AimKind.None; Paused = false; enraged = false;
            enemies.Clear(); zones.Clear(); spawns = BattleDefinitions.Wave(Wave); spawnCursor = 0;
            focusTarget = 0; focusUntil = 0f; bossNextSlam = 4.8f; slamAt = -1f; slamLane = -1;
            foreach (var t in troops) ResetTroop(t, snapToHome: true);
            Emit("wave_started", BattleDefinitions.IsBoss(Wave) ? "首领来袭：全军自由合围" : "全军接敌：自由转向迎战");
            return new CommandResult(true, "战斗开始");
        }

        private CommandResult CombatCommand(BattleCommand c)
        {
            if (c.Kind == CommandKind.Aim)
            {
                if (Paused || Tactics <= 0 || c.Aim == AimKind.None || !Enum.IsDefined(typeof(AimKind), c.Aim)) return Reject("当前无法开启战术瞄准");
                Aim = c.Aim; return new CommandResult(true, "瞄准中，战斗暂停；可随时取消");
            }
            if (c.Kind == CommandKind.Focus)
            {
                if (Aim != AimKind.Focus || Tactics <= 0) return Reject("请先进入集火瞄准");
                var target = enemies.Find(e => e.Id == c.UnitId && e.Alive);
                if (target == null) return Reject("目标已失效，请重选或取消");
                focusTarget = target.Id; focusUntil = BattleTime + 4.5f; Tactics--; Aim = AimKind.None;
                Emit("focus", "全军集火指定目标（射程内优先转向输出）", 0, target.Id);
                return new CommandResult(true, "已锁定集火目标");
            }
            if (c.Kind == CommandKind.Protect)
            {
                if (Aim != AimKind.Protect || Tactics <= 0 || c.Lane < 0 || c.Lane > 2) return Reject("请先进入保护瞄准并选择上/中/下行区域");
                float bandZ = BattleDefinitions.CellZ(c.Lane);
                var targets = troops.Where(t => t.Deployed && t.Alive && (t.Lane == c.Lane || Math.Abs(t.Z - bandZ) <= 1.25f)).ToArray();
                if (targets.Length == 0) return Reject("该区域没有存活友军，请重选或取消");
                foreach (var t in targets) { t.Shield = Math.Max(t.Shield, t.MaxHp * .28f); t.ShieldUntil = BattleTime + 4f; }
                Tactics--; Aim = AimKind.None;
                Emit("protect", "指定区域友军获得4秒护盾", 0, 0, c.Lane);
                return new CommandResult(true, "护盾已生效");
            }
            return Reject("战斗中不能招募、移动或合成");
        }

        public void Advance(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0 || seconds > 60) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (Phase != RunPhase.Fighting || Paused || Aim != AimKind.None) return;
            accumulator += seconds * Speed;
            while (accumulator + .000001f >= BattleDefinitions.Step && Phase == RunPhase.Fighting)
            {
                accumulator = Math.Max(0f, accumulator - BattleDefinitions.Step);
                Step(BattleDefinitions.Step);
            }
        }

        private void Step(float dt)
        {
            BattleTime += dt;
            while (spawnCursor < spawns.Count && spawns[spawnCursor].At <= BattleTime) Spawn(spawns[spawnCursor++]);

            if (!enraged && BattleTime >= BattleDefinitions.WaveTimeout)
            {
                enraged = true;
                Emit("enemies_enraged", "战斗超时，敌军进入狂暴！");
            }

            var aliveTroops = troops.Where(t => t.Deployed && t.Alive).ToArray();
            var aliveEnemies = enemies.Where(e => e.Alive).ToArray();

            foreach (var t in aliveTroops)
            {
                if (t.ShieldUntil <= BattleTime) t.Shield = 0f;
                t.Cooldown -= dt; t.SkillCooldown -= dt;

                var target = Target(t);
                if (target == null && t.Kind == TroopKind.Shield)
                {
                    var approach = enemies.Where(e => e.Alive).OrderBy(e => Distance(t.X, t.Z, e.X, e.Z)).ThenBy(e => e.Id).FirstOrDefault();
                    if (approach != null)
                    {
                        float angleOffset = (t.Lane - 1) * 0.55f;
                        float standDist = approach.Boss ? 1.95f : 0.85f;
                        float goalX = Math.Min(4.8f, approach.X - (float)Math.Cos(angleOffset) * standDist);
                        float goalZ = Clamp(approach.Z + (float)Math.Sin(angleOffset) * standDist, -3.0f, 3.0f);
                        float moveYaw = BattleDefinitions.VectorToYaw(goalX - t.X, goalZ - t.Z, 90f);
                        t.FacingYaw = BattleDefinitions.MoveTowardsAngle(t.FacingYaw, moveYaw, t.TurnSpeed * dt);
                        MoveTroop(t, goalX, goalZ, t.MoveSpeed * dt);
                        target = Target(t);
                    }
                }

                if (target == null)
                {
                    t.FacingYaw = BattleDefinitions.MoveTowardsAngle(t.FacingYaw, 90f, t.TurnSpeed * dt);
                    continue;
                }

                float desiredYaw = BattleDefinitions.VectorToYaw(target.X - t.X, target.Z - t.Z, t.FacingYaw);
                t.FacingYaw = BattleDefinitions.MoveTowardsAngle(t.FacingYaw, desiredYaw, t.TurnSpeed * dt);
                bool facingReady = Math.Abs(BattleDefinitions.DeltaAngle(t.FacingYaw, desiredYaw)) <= BattleDefinitions.AttackAngleTolerance;

                if (!facingReady) continue;

                if (t.Cooldown <= 0f)
                {
                    Damage(t, target, BattleDefinitions.Attack(t), t.Kind == TroopKind.Storm || t.Kind == TroopKind.Frost, false);
                    t.Cooldown = t.Kind == TroopKind.Archer ? .88f : 1.18f;
                }
                if (target.Alive && t.SkillCooldown <= 0f) Skill(t, target);
            }

            foreach (var zone in zones.ToArray())
            {
                zone.Remaining -= dt; zone.UntilTick -= dt;
                if (zone.UntilTick <= 0f)
                {
                    zone.UntilTick += .5f;
                    var caster = troops.Find(t => t.Id == zone.Source);
                    float radius = rewards.Contains(RewardKind.WideStorm) ? 1.85f : 1.35f;
                    if (caster != null)
                        foreach (var e in enemies.Where(e => e.Alive && Distance(e.X, e.Z, zone.X, zone.Z) <= radius).ToArray())
                            Damage(caster, e, zone.Damage, true, true);
                }
                if (zone.Remaining <= 0f) zones.Remove(zone);
            }

            foreach (var e in aliveEnemies)
            {
                if (e.Alive) EnemyStep(e, dt);
                if (Phase != RunPhase.Fighting) return;
            }

            ApplySeparation(dt);

            if (Phase != RunPhase.Fighting) return;

            if (!troops.Any(t => t.Deployed && t.Alive))
            {
                Phase = RunPhase.Lost; Aim = AimKind.None; Paused = false;
                Emit("run_lost", "全军阵亡，远征失败");
                return;
            }
            if (spawnCursor == spawns.Count && !enemies.Any(e => e.Alive)) FinishWave();
        }

        private void ApplySeparation(float dt)
        {
            var aliveT = troops.Where(t => t.Deployed && t.Alive).ToArray();
            for (int i = 0; i < aliveT.Length; i++)
            {
                for (int j = i + 1; j < aliveT.Length; j++)
                {
                    var a = aliveT[i]; var b = aliveT[j];
                    if (a.Kind != TroopKind.Shield || b.Kind != TroopKind.Shield) continue;
                    float minD = BattleDefinitions.BodyRadius(a.Kind) + BattleDefinitions.BodyRadius(b.Kind);
                    float dx = a.X - b.X, dz = a.Z - b.Z;
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (dist < minD)
                    {
                        float nx = dist > 1e-4f ? dx / dist : 0f;
                        float nz = dist > 1e-4f ? dz / dist : (i % 2 == 0 ? 1f : -1f);
                        float push = (minD - dist) * Math.Min(1f, dt * 6f) * 0.5f;
                        a.X = Clamp(a.X + nx * push, -5.2f, 5.2f);
                        a.Z = Clamp(a.Z + nz * push, -3.0f, 3.0f);
                        b.X = Clamp(b.X - nx * push, -5.2f, 5.2f);
                        b.Z = Clamp(b.Z - nz * push, -3.0f, 3.0f);
                    }
                }
            }

            var aliveE = enemies.Where(e => e.Alive).ToArray();
            for (int i = 0; i < aliveE.Length; i++)
            {
                for (int j = i + 1; j < aliveE.Length; j++)
                {
                    var a = aliveE[i]; var b = aliveE[j];
                    float minD = (BattleDefinitions.BodyRadius(a.Kind) + BattleDefinitions.BodyRadius(b.Kind)) * 0.92f;
                    float dx = a.X - b.X, dz = a.Z - b.Z;
                    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (dist < minD)
                    {
                        float nx = dist > 1e-4f ? dx / dist : 0f;
                        float nz = dist > 1e-4f ? dz / dist : (i % 2 == 0 ? 1f : -1f);
                        float push = (minD - dist) * Math.Min(1f, dt * 6f) * 0.5f;
                        a.X = Clamp(a.X + nx * push, -5.4f, 6.4f);
                        a.Z = Clamp(a.Z + nz * push, -3.0f, 3.0f);
                        b.X = Clamp(b.X - nx * push, -5.4f, 6.4f);
                        b.Z = Clamp(b.Z - nz * push, -3.0f, 3.0f);
                    }
                }
            }
        }

        private Enemy Target(Troop t)
        {
            var reachable = enemies.Where(e => BattleDefinitions.CanReach(t, e));
            if (focusUntil > BattleTime)
            {
                var focus = reachable.FirstOrDefault(e => e.Id == focusTarget);
                if (focus != null) return focus;
            }
            return reachable.OrderBy(e => Distance(t.X, t.Z, e.X, e.Z)).ThenBy(e => e.Id).FirstOrDefault();
        }

        private void Spawn(WaveSpawn spawn)
        {
            float hp = spawn.Kind == EnemyKind.Giant
                ? (Wave == 6 ? 760f : 1320f)
                : (spawn.Kind == EnemyKind.Armored ? 132f : spawn.Kind == EnemyKind.Runner ? 52f : 66f) * (1f + (Wave - 1) * .085f);
            float spawnX = spawn.Kind == EnemyKind.Giant ? BattleDefinitions.BossSpawnX : BattleDefinitions.EnemySpawnX;
            var e = new Enemy
            {
                Id = nextId++,
                Kind = spawn.Kind,
                Lane = spawn.Lane,
                X = spawnX,
                Z = BattleDefinitions.CellZ(spawn.Lane),
                FacingYaw = -90f,
                TurnSpeed = BattleDefinitions.TurnSpeed(spawn.Kind),
                MoveSpeed = BattleDefinitions.MoveSpeed(spawn.Kind),
                MaxHp = hp,
                Hp = hp,
                Cooldown = .85f
            };
            enemies.Add(e);
            Emit("enemy_spawned", BattleDefinitions.Name(e.Kind), 0, e.Id, e.Lane);
        }

        private void Damage(Troop source, Enemy target, float amount, bool magic, bool indirect)
        {
            if (!target.Alive) return;
            float defense = target.Kind == EnemyKind.Armored || target.Boss ? (magic ? 8f : 100f) : 0f;
            amount = Math.Max(1f, (float)Math.Floor(amount * 100f / (100f + defense)));
            target.Hp = Math.Max(0f, target.Hp - amount);
            Emit(indirect ? "skill_tick" : "hit", magic ? "法术命中" : "物理命中", source.Id, target.Id, target.Lane, amount);
            if (!target.Alive) Emit("enemy_killed", "消灭" + BattleDefinitions.Name(target.Kind), source.Id, target.Id, target.Lane);
        }

        private void Hurt(Enemy source, Troop target, float amount)
        {
            if (enraged) amount *= 2.5f;
            float absorbed = Math.Min(target.Shield, amount); target.Shield -= absorbed;
            float realDmg = Math.Max(0f, amount - absorbed);
            target.Hp = Math.Max(0f, target.Hp - realDmg);
            Emit("troop_hit", "友军受击", source.Id, target.Id, target.Lane, realDmg);
            if (!target.Alive) Emit("troop_down", "单位倒下，下波满血恢复", source.Id, target.Id, target.Lane);
        }

        private void Skill(Troop t, Enemy target)
        {
            float attack = BattleDefinitions.Attack(t);
            Emit("skill", BattleDefinitions.Name(t.Kind) + "释放技能", t.Id, target.Id, target.Lane);
            switch (t.Kind)
            {
                case TroopKind.Shield:
                    t.Shield = Math.Max(t.Shield, t.MaxHp * (t.Branch == SkillBranch.Bastion ? .34f : .22f)); t.ShieldUntil = BattleTime + 3.2f;
                    if (t.Branch == SkillBranch.WarCry)
                        foreach (var ally in troops.Where(a => a.Alive && a.Deployed && Distance(a.X, a.Z, t.X, t.Z) <= 3.2f))
                        { ally.Shield = Math.Max(ally.Shield, ally.MaxHp * .14f); ally.ShieldUntil = BattleTime + 3f; }
                    Damage(t, target, attack * 1.35f, false, false); t.SkillCooldown = 6.8f;
                    break;
                case TroopKind.Archer:
                    Damage(t, target, attack * 1.55f, false, false);
                    if (target.Alive && !target.Boss)
                    {
                        target.X = Math.Min(6.3f, target.X + (t.Branch == SkillBranch.Gale ? 1.35f : .72f));
                        Emit("knockback", "击退", t.Id, target.Id, target.Lane);
                    }
                    if (t.Branch == SkillBranch.Pierce)
                        foreach (var e in enemies.Where(e => e.Alive && e.Id != target.Id && Math.Abs(e.Z - target.Z) <= 1.35f && BattleDefinitions.CanReach(t, e)).OrderBy(e => e.X).Take(2).ToArray())
                            Damage(t, e, attack, false, true);
                    t.SkillCooldown = t.Branch == SkillBranch.Gale ? 4.4f : 5.8f;
                    break;
                case TroopKind.Storm:
                    if (t.Branch == SkillBranch.ChainLightning)
                    {
                        int count = rewards.Contains(RewardKind.WideStorm) ? 4 : 3;
                        foreach (var e in enemies.Where(e => e.Alive && Distance(target.X, target.Z, e.X, e.Z) <= 3.4f).OrderBy(e => e.Id == target.Id ? 0 : 1).ThenBy(e => e.Id).Take(count).ToArray())
                            Damage(t, e, attack * 1.6f, true, true);
                    }
                    else
                    {
                        zones.Add(new StormZone { Source = t.Id, Lane = target.Lane, X = target.X, Z = target.Z, Remaining = t.Branch == SkillBranch.StormPool ? 3f : 2f, UntilTick = .5f, Damage = attack * .55f });
                    }
                    t.SkillCooldown = 6.8f;
                    break;
                case TroopKind.Frost:
                    float duration = (t.Branch == SkillBranch.DeepFreeze ? 1.7f : 1f) + (rewards.Contains(RewardKind.LongFreeze) ? .5f : 0f);
                    if (target.Boss)
                    {
                        target.SlowUntil = BattleTime + 1.2f;
                        Emit("boss_control_resisted", "首领免疫冻结，转为减速", t.Id, target.Id);
                    }
                    else
                    {
                        target.FrozenUntil = Math.Max(target.FrozenUntil, BattleTime + duration);
                        Emit("freeze", "冻结", t.Id, target.Id, target.Lane, duration);
                    }
                    Damage(t, target, attack * 1.15f, true, false);
                    if (t.Branch == SkillBranch.IceArmor)
                        foreach (var ally in troops.Where(a => a.Deployed && a.Alive && Distance(a.X, a.Z, t.X, t.Z) <= 3.4f))
                        { ally.Shield = Math.Max(ally.Shield, ally.MaxHp * .14f); ally.ShieldUntil = BattleTime + 3f; }
                    t.SkillCooldown = 7.8f;
                    break;
            }
        }

        private void EnemyStep(Enemy e, float dt)
        {
            if (e.FrozenUntil > BattleTime) return;
            e.Cooldown -= dt;

            var target = troops.Where(t => t.Deployed && t.Alive)
                .OrderBy(t => Distance(t.X, t.Z, e.X, e.Z))
                .ThenBy(t => t.Id)
                .FirstOrDefault();

            if (e.Kind == EnemyKind.Healer && e.Cooldown <= 0f)
            {
                var heal = enemies.Where(a => a.Alive && a.Id != e.Id && a.Hp < a.MaxHp).OrderBy(a => a.Hp / a.MaxHp).FirstOrDefault();
                if (heal != null)
                {
                    float healYaw = BattleDefinitions.VectorToYaw(heal.X - e.X, heal.Z - e.Z, e.FacingYaw);
                    e.FacingYaw = BattleDefinitions.MoveTowardsAngle(e.FacingYaw, healYaw, e.TurnSpeed * dt);
                    heal.Hp = Math.Min(heal.MaxHp, heal.Hp + 24f);
                    e.Cooldown = 3f;
                    Emit("heal", "治疗师为敌人回血", e.Id, heal.Id, heal.Lane, 24f);
                }
            }

            if (e.Boss)
            {
                if (slamAt < 0f && BattleTime >= bossNextSlam)
                {
                    slamLane = Enumerable.Range(0, 3)
                        .OrderByDescending(l => troops.Count(t => t.Deployed && t.Alive && Math.Abs(t.Z - BattleDefinitions.CellZ(l)) <= 1.25f))
                        .ThenBy(l => Math.Abs(l - 1))
                        .First();
                    slamAt = BattleTime + 2f;
                    Emit("boss_telegraph", "巨像蓄力横扫：注意保护预警区域", e.Id, 0, slamLane);
                }
                if (slamAt >= 0f)
                {
                    float slamZ = BattleDefinitions.CellZ(slamLane);
                    float telegraphYaw = BattleDefinitions.VectorToYaw(-3.0f - e.X, slamZ - e.Z, -90f);
                    e.FacingYaw = BattleDefinitions.MoveTowardsAngle(e.FacingYaw, telegraphYaw, e.TurnSpeed * dt);
                    if (BattleTime >= slamAt)
                    {
                        foreach (var t in troops.Where(t => t.Deployed && t.Alive && Math.Abs(t.Z - slamZ) <= 1.28f).ToArray())
                            Hurt(e, t, Wave == 6 ? 42f : 68f);
                        Emit("boss_slam", "巨像裂地重击", e.Id, 0, slamLane);
                        slamAt = -1f; slamLane = -1; bossNextSlam = BattleTime + 7f;
                    }
                }
            }

            if (target == null) return;

            float desiredYaw = BattleDefinitions.VectorToYaw(target.X - e.X, target.Z - e.Z, e.FacingYaw);
            if (!e.Boss || slamAt < 0f)
                e.FacingYaw = BattleDefinitions.MoveTowardsAngle(e.FacingYaw, desiredYaw, e.TurnSpeed * dt);

            float dist = Distance(e.X, e.Z, target.X, target.Z);
            float attackReach = e.Boss ? 2.15f : 0.98f;
            if (dist <= attackReach)
            {
                bool facingReady = Math.Abs(BattleDefinitions.DeltaAngle(e.FacingYaw, desiredYaw)) <= BattleDefinitions.EnemyAttackAngleTolerance;
                if (facingReady && e.Cooldown <= 0f)
                {
                    float dmg = e.Boss ? 22f : e.Kind == EnemyKind.Armored ? 19f : e.Kind == EnemyKind.Runner ? 14f : 13f;
                    Hurt(e, target, dmg);
                    e.Cooldown = e.Boss ? 1.75f : 1.25f;
                }
            }
            else
            {
                float speed = e.MoveSpeed;
                if (e.SlowUntil > BattleTime) speed *= .7f;
                if (enraged) speed *= 1.5f;
                MoveEnemy(e, target.X, target.Z, speed * dt);
            }
        }

        private void FinishWave()
        {
            Aim = AimKind.None; Paused = false; zones.Clear(); enraged = false;
            GrantWavePopExp(2);
            foreach (var t in troops) ResetTroop(t, snapToHome: false);
            if (Wave >= BattleDefinitions.TotalWaves)
            {
                Phase = RunPhase.Won;
                Emit("run_won", "十二波远征全胜！");
            }
            else
            {
                Phase = Wave == 4 || Wave == 8 ? RunPhase.Reward : RunPhase.WaveResult;
                Emit("wave_cleared", "本波告捷！全员满血归队，人口经验 +2");
            }
        }

        private static float Distance(float x, float z, float xx, float zz) { float dx = x - xx, dz = z - zz; return (float)Math.Sqrt(dx * dx + dz * dz); }
        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

        private static void MoveTroop(Troop t, float x, float z, float amount)
        {
            float dist = Distance(t.X, t.Z, x, z);
            if (dist <= amount) { t.X = x; t.Z = z; }
            else if (dist > 0f) { t.X += (x - t.X) / dist * amount; t.Z += (z - t.Z) / dist * amount; }
        }

        private static void MoveEnemy(Enemy e, float x, float z, float amount)
        {
            float dist = Distance(e.X, e.Z, x, z);
            if (dist <= amount) { e.X = x; e.Z = z; }
            else if (dist > 0f) { e.X += (x - e.X) / dist * amount; e.Z += (z - e.Z) / dist * amount; }
        }
    }
}
