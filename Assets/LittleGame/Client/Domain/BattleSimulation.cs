using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleGame.Combat
{
    public sealed partial class BattleSession
    {
        private CommandResult StartWave()
        {
            if (pending.Count > 0) return Reject("请先处理暂存单位");
            if (!troops.Any(t => t.Deployed)) return Reject("至少部署一名单位");
            Phase = RunPhase.Fighting; BattleTime = accumulator = 0f; Tactics = 1; Aim = AimKind.None; Paused = false; enraged = false;
            enemies.Clear(); zones.Clear(); spawns = BattleDefinitions.Wave(Wave); spawnCursor = 0;
            focusTarget = 0; focusUntil = 0f; bossNextSlam = 4.8f; slamAt = -1f; slamLane = -1;
            navigation.Reset();
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
            StepSkills();
            while (spawnCursor < spawns.Count && spawns[spawnCursor].At <= BattleTime) Spawn(spawns[spawnCursor++]);

            if (!enraged && BattleTime >= BattleDefinitions.WaveTimeout)
            {
                enraged = true;
                Emit("enemies_enraged", "战斗超时，敌军进入狂暴！");
            }

            var aliveTroops = troops.Where(t => t.Deployed && t.Alive).ToArray();
            var aliveEnemies = enemies.Where(e => e.Alive).ToArray();

            navInputs.Clear();

            // 1. 我方行动决策（攻击/技能 + 生成导航意图）
            foreach (var t in aliveTroops)
            {
                if (t.ShieldUntil <= BattleTime) t.Shield = 0f;
                t.Cooldown -= dt; t.SkillCooldown -= dt;

                var intent = BattleAgentBrain.PlanTroop(t, enemies, focusUntil > BattleTime ? focusTarget : 0, dt);
                if (SkillBusy(t.Id)) { intent.State = CombatAiState.Engaging; intent.CanAttack = false; }
                t.TargetId = intent.TargetId;
                if (intent.HoldPosition) SetAiState(t, intent.State);
                var target = enemies.Find(e => e.Id == intent.TargetId && e.Alive);
                float desiredYaw = BattleDefinitions.VectorToYaw(intent.LookX, intent.LookZ, t.FacingYaw);
                if (intent.HoldPosition) t.FacingYaw = BattleDefinitions.MoveTowardsAngle(t.FacingYaw, desiredYaw, t.TurnSpeed * dt);

                QueueNavigation(t, intent);

                if (target == null || !intent.CanAttack || !BattleDefinitions.CanReach(t, target)) continue;
                bool facingReady = Math.Abs(BattleDefinitions.DeltaAngle(t.FacingYaw, desiredYaw)) <= BattleDefinitions.AttackAngleTolerance;

                if (!facingReady) continue;

                if (t.SkillCooldown <= 0f)
                {
                    var definition = FindSkill(t.Kind.ToString(), "Skill", t.Branch == SkillBranch.None ? "" : t.Branch.ToString(), t.Star);
                    BeginSkill(t.Id, target.Id, target.Lane, false, definition);
                    t.SkillCooldown = t.Kind == TroopKind.Shield ? 6.5f : t.Kind == TroopKind.Archer ? (t.Branch == SkillBranch.Gale ? 4.4f : 5.8f) : t.Kind == TroopKind.Storm ? 6.6f : 7.4f;
                }
                else if (t.Cooldown <= 0f)
                {
                    BeginSkill(t.Id, target.Id, target.Lane, false, FindSkill(t.Kind.ToString(), "BasicAttack", "", t.Star));
                    t.Cooldown = BattleDefinitions.AttackInterval(t.Kind);
                }
            }

            // 2. 持续雷池伤害
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

            // 3. 敌方行动决策（攻击/治疗/首领技能 + 生成导航意图）
            foreach (var e in aliveEnemies)
            {
                if (!e.Alive) continue;
                EnemyStep(e, dt);
                if (Phase != RunPhase.Fighting) return;
            }

            // 4. A*路径跟随、通路占位、扫掠碰撞与真实速度反馈
            navInputs.RemoveAll(a => a.IsTroop ? !troops.Any(t => t.Id == a.Id && t.Alive) : !enemies.Any(e => e.Id == a.Id && e.Alive));
            navigation.Step(navInputs, dt, navOutputs);
            ApplyNavigationOutputs(dt);

            if (Phase != RunPhase.Fighting) return;

            if (!troops.Any(t => t.Deployed && t.Alive))
            {
                Phase = RunPhase.Lost; Aim = AimKind.None; Paused = false;
                Emit("run_lost", "全军阵亡，远征失败");
                return;
            }
            if (spawnCursor == spawns.Count && !enemies.Any(e => e.Alive)) FinishWave();
        }

        private void ApplyNavigationOutputs(float dt)
        {
            for (int i = 0; i < navOutputs.Count; i++)
            {
                var o = navOutputs[i];
                if (o.IsTroop)
                {
                    var t = troops.Find(u => u.Id == o.Id);
                    if (t == null || !t.Alive) continue;
                    t.X = Clamp(o.NewX, -5.25f, 5.20f);
                    t.Z = Clamp(o.NewZ, -2.95f, 2.95f);
                    t.Vx = o.NewVx;
                    t.Vz = o.NewVz;
                    if (t.Navigation.Status != NavigationStatus.Holding && t.TargetId != 0)
                        SetAiState(t, o.Waiting ? CombatAiState.Waiting : CombatAiState.Pursuing);
                    if (t.Vx * t.Vx + t.Vz * t.Vz > .0025f)
                        t.FacingYaw = BattleDefinitions.MoveTowardsAngle(t.FacingYaw, BattleDefinitions.VectorToYaw(t.Vx, t.Vz, t.FacingYaw), t.TurnSpeed * 1.4f * dt);
                }
                else
                {
                    var e = enemies.Find(u => u.Id == o.Id);
                    if (e == null || !e.Alive) continue;
                    e.X = Clamp(o.NewX, -5.35f, 6.35f);
                    e.Z = Clamp(o.NewZ, -2.95f, 2.95f);
                    e.Vx = o.NewVx;
                    e.Vz = o.NewVz;
                    if (e.Navigation.Status != NavigationStatus.Holding && e.TargetId != 0)
                        SetAiState(e, o.Waiting ? CombatAiState.Waiting : CombatAiState.Pursuing);
                    if ((!e.Boss || slamAt < 0) && e.Vx * e.Vx + e.Vz * e.Vz > .0025f)
                        e.FacingYaw = BattleDefinitions.MoveTowardsAngle(e.FacingYaw, BattleDefinitions.VectorToYaw(e.Vx, e.Vz, e.FacingYaw), e.TurnSpeed * 1.4f * dt);
                }
            }
        }

        private void QueueNavigation(Troop unit, CombatAiIntent intent)
        {
            navInputs.Add(new BattleNavigation.AgentInput { Id = unit.Id, IsTroop = true,
                X = unit.X, Z = unit.Z, Radius = BattleDefinitions.BodyRadius(unit.Kind), MaxSpeed = unit.MoveSpeed,
                TargetId = intent.TargetId, TargetX = intent.TargetX, TargetZ = intent.TargetZ,
                TargetRadius = intent.TargetRadius, AttackReach = intent.AttackReach,
                HoldPosition = intent.HoldPosition, State = unit.Navigation });
        }

        private void QueueNavigation(Enemy unit, CombatAiIntent intent, float speed)
        {
            navInputs.Add(new BattleNavigation.AgentInput { Id = unit.Id, IsTroop = false,
                X = unit.X, Z = unit.Z, Radius = BattleDefinitions.BodyRadius(unit.Kind), MaxSpeed = speed,
                TargetId = intent.TargetId, TargetX = intent.TargetX, TargetZ = intent.TargetZ,
                TargetRadius = intent.TargetRadius, AttackReach = intent.AttackReach,
                HoldPosition = intent.HoldPosition, State = unit.Navigation });
        }

        private void SetAiState(Troop unit, CombatAiState state)
        {
            if (unit.AiState == state) return;
            var previous = unit.AiState; unit.AiState = state;
            Emit("ai_state_changed", previous + " -> " + state, unit.Id, unit.TargetId);
        }

        private void SetAiState(Enemy unit, CombatAiState state)
        {
            if (unit.AiState == state) return;
            var previous = unit.AiState; unit.AiState = state;
            Emit("ai_state_changed", previous + " -> " + state, unit.Id, unit.TargetId);
        }

        private void Spawn(WaveSpawn spawn)
        {
            float hp = BattleDefinitions.EnemyHealth(spawn.Kind, Wave);
            float spawnX = spawn.Kind == EnemyKind.Giant ? BattleDefinitions.BossSpawnX : BattleDefinitions.EnemySpawnX;
            var e = new Enemy
            {
                Id = nextId++,
                Kind = spawn.Kind,
                Lane = spawn.Lane,
                X = spawnX,
                Z = BattleDefinitions.CellZ(spawn.Lane),
                Vx = 0f,
                Vz = 0f,
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
            float defense = target.Kind == EnemyKind.Armored || target.Boss ? (magic ? 10f : 100f) : 0f;
            amount = Math.Max(1f, (float)Math.Floor(amount * 100f / (100f + defense)));
            target.Hp = Math.Max(0f, target.Hp - amount);
            Emit(indirect ? "skill_tick" : "hit", magic ? "法术命中" : "物理命中", source.Id, target.Id, target.Lane, amount);
            if (!target.Alive)
            {
                target.Vx = target.Vz = 0f;
                SetAiState(target, CombatAiState.Dead);
                Emit("enemy_killed", "消灭" + BattleDefinitions.Name(target.Kind), source.Id, target.Id, target.Lane);
            }
        }

        private void Hurt(Enemy source, Troop target, float amount)
        {
            if (enraged) amount *= 2.5f;
            float absorbed = Math.Min(target.Shield, amount); target.Shield -= absorbed;
            float realDmg = Math.Max(0f, amount - absorbed);
            target.Hp = Math.Max(0f, target.Hp - realDmg);
            Emit("troop_hit", "友军受击", source.Id, target.Id, target.Lane, realDmg);
            if (!target.Alive)
            {
                target.Vx = target.Vz = 0f;
                SetAiState(target, CombatAiState.Dead);
                Emit("troop_down", "单位倒下，下波满血恢复", source.Id, target.Id, target.Lane);
            }
        }

        private void Skill(Troop t, Enemy target)
        {
            float attack = BattleDefinitions.Attack(t);
            Emit("skill", BattleDefinitions.Name(t.Kind) + "释放技能", t.Id, target.Id, target.Lane);
            switch (t.Kind)
            {
                case TroopKind.Shield:
                    t.Shield = Math.Max(t.Shield, t.MaxHp * (t.Branch == SkillBranch.Bastion ? .34f : .24f)); t.ShieldUntil = BattleTime + 3.4f;
                    if (t.Branch == SkillBranch.WarCry)
                        foreach (var ally in troops.Where(a => a.Alive && a.Deployed && Distance(a.X, a.Z, t.X, t.Z) <= 3.2f))
                        { ally.Shield = Math.Max(ally.Shield, ally.MaxHp * .14f); ally.ShieldUntil = BattleTime + 3f; }
                    Damage(t, target, attack * 1.40f, false, false); t.SkillCooldown = 6.5f;
                    break;
                case TroopKind.Archer:
                    Damage(t, target, attack * 1.55f, false, false);
                    if (target.Alive && !target.Boss)
                    {
                        float radians = (resolvingSkill?.FacingYaw ?? t.FacingYaw) * (float)Math.PI / 180f;
                        float push = t.Branch == SkillBranch.Gale ? 1.25f : .68f;
                        target.X = Clamp(target.X + (float)Math.Sin(radians) * push, -5.35f, BattleDefinitions.EnemyMaxX);
                        target.Z = Clamp(target.Z + (float)Math.Cos(radians) * push, -2.95f, 2.95f);
                        Emit("knockback", "击退", t.Id, target.Id, target.Lane);
                    }
                    if (t.Branch == SkillBranch.Pierce)
                        foreach (var e in enemies.Where(e => e.Alive && e.Id != target.Id && InPierceLine(t, target, e) && BattleDefinitions.CanReach(t, e)).OrderBy(e => Distance(t.X, t.Z, e.X, e.Z)).Take(2).ToArray())
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
                        zones.Add(new StormZone { Source = t.Id, Lane = target.Lane, X = resolvingSkill?.TargetX ?? target.X, Z = resolvingSkill?.TargetZ ?? target.Z, Remaining = t.Branch == SkillBranch.StormPool ? 3f : 2.2f, UntilTick = .5f, Damage = attack * .60f });
                    }
                    t.SkillCooldown = 6.6f;
                    break;
                case TroopKind.Frost:
                    float duration = (t.Branch == SkillBranch.DeepFreeze ? 1.7f : 1.05f) + (rewards.Contains(RewardKind.LongFreeze) ? .5f : 0f);
                    if (target.Boss)
                    {
                        target.SlowUntil = BattleTime + 1.4f;
                        Emit("boss_control_resisted", "首领免疫冻结，转为减速", t.Id, target.Id);
                    }
                    else
                    {
                        target.FrozenUntil = Math.Max(target.FrozenUntil, BattleTime + duration);
                        Emit("freeze", "冻结", t.Id, target.Id, target.Lane, duration);
                    }
                    Damage(t, target, attack * 1.20f, true, false);
                    if (t.Branch == SkillBranch.IceArmor)
                        foreach (var ally in troops.Where(a => a.Deployed && a.Alive && Distance(a.X, a.Z, t.X, t.Z) <= 3.4f))
                        { ally.Shield = Math.Max(ally.Shield, ally.MaxHp * .14f); ally.ShieldUntil = BattleTime + 3f; }
                    t.SkillCooldown = 7.4f;
                    break;
            }
        }

        private void EnemyStep(Enemy e, float dt)
        {
            float speed = e.MoveSpeed;
            if (e.SlowUntil > BattleTime) speed *= .7f;
            if (enraged) speed *= 1.5f;
            var intent = BattleAgentBrain.PlanEnemy(e, troops, BattleTime, speed, dt);
            bool busy = SkillBusy(e.Id);
            if (busy) { intent.State = CombatAiState.Engaging; intent.CanAttack = false; }
            e.TargetId = intent.TargetId;
            if (intent.HoldPosition) SetAiState(e, intent.State);
            if (e.FrozenUntil > BattleTime)
            {
                QueueNavigation(e, intent, speed);
                return;
            }

            e.Cooldown -= dt;

            var target = troops.Find(t => t.Id == intent.TargetId && t.Deployed && t.Alive);

            if (!busy && e.Kind == EnemyKind.Healer && e.Cooldown <= 0f)
            {
                var heal = enemies.Where(a => a.Alive && a.Id != e.Id && a.Hp < a.MaxHp).OrderBy(a => a.Hp / a.MaxHp).FirstOrDefault();
                if (heal != null)
                {
                    float healYaw = BattleDefinitions.VectorToYaw(heal.X - e.X, heal.Z - e.Z, e.FacingYaw);
                    e.FacingYaw = BattleDefinitions.MoveTowardsAngle(e.FacingYaw, healYaw, e.TurnSpeed * dt);
                    BeginSkill(e.Id, heal.Id, heal.Lane, true, FindSkill(e.Kind.ToString(), "Heal"));
                    busy = true; intent.CanAttack = false; intent.State = CombatAiState.Engaging;
                    e.Cooldown = 3.2f;
                }
            }

            if (e.Boss)
            {
                if (!busy && slamAt < 0f && BattleTime >= bossNextSlam)
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
                        float slamDmg = BattleDefinitions.BossSlamDamage(Wave);
                        foreach (var t in troops.Where(t => t.Deployed && t.Alive && Math.Abs(t.Z - slamZ) <= 1.28f).ToArray())
                            Hurt(e, t, slamDmg);
                        Emit("boss_slam", "巨像裂地重击", e.Id, 0, slamLane);
                        slamAt = -1f; slamLane = -1; bossNextSlam = BattleTime + 7f;
                    }
                }
            }

            if (target != null)
            {
                float desiredYaw = BattleDefinitions.VectorToYaw(intent.LookX, intent.LookZ, e.FacingYaw);
                // 仅在站定交战时朝向目标；追击/绕行时由导航实际位移方向统一驱动朝向，避免同帧来回拉扯。
                if (intent.HoldPosition && (!e.Boss || slamAt < 0f))
                    e.FacingYaw = BattleDefinitions.MoveTowardsAngle(e.FacingYaw, desiredYaw, e.TurnSpeed * dt);
                bool facingReady = Math.Abs(BattleDefinitions.DeltaAngle(e.FacingYaw, desiredYaw)) <= BattleDefinitions.EnemyAttackAngleTolerance;
                if (!busy && (!e.Boss || slamAt < 0f) && intent.CanAttack && facingReady && e.Cooldown <= 0f)
                {
                    BeginSkill(e.Id, target.Id, target.Lane, true, FindSkill(e.Kind.ToString(), "EnemyAttack"));
                    e.Cooldown = e.Boss ? 1.75f : 1.28f;
                }
            }

            QueueNavigation(e, intent, speed);
        }

        private void FinishWave()
        {
            Aim = AimKind.None; Paused = false; zones.Clear(); skillInstances.Clear(); enraged = false;
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

        private static bool InPierceLine(Troop caster, Enemy target, Enemy other)
        {
            float dx = target.X - caster.X, dz = target.Z - caster.Z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < .001f) return false;
            float ox = other.X - target.X, oz = other.Z - target.Z;
            return (ox * dx + oz * dz) / length >= 0f && Math.Abs(ox * dz - oz * dx) / length <= 1.35f;
        }

        private static float Distance(float x, float z, float xx, float zz) { float dx = x - xx, dz = z - zz; return (float)Math.Sqrt(dx * dx + dz * dz); }
        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
