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
            if (DeployedCount > MaxDeployed) return Reject("当前波次上阵人数超出上限");
            Phase = RunPhase.Fighting; BattleTime = accumulator = 0f; Paused = false; enraged = false;
            foreach (var record in damageStatistics) record.WaveDamage = 0f;
            enemies.Clear(); zones.Clear(); spawns = BattleTables.Current.Schedule(Wave, Seed); spawnCursor = 0;
            Buffs.Apply(enemies, BattleSide.Enemy);
            slamAt = -1f; slamLane = -1;
            if (navigation == null) throw new InvalidOperationException("战斗导航未绑定：实战需要Unity NavMeshAgent后端。");
            navigation.Reset();
            foreach (var t in troops) ResetTroop(t, snapToHome: true);
            Buffs.Apply(troops);
            foreach (var troop in troops) troop.Hp = troop.MaxHp = troop.Properties.Health;
            Emit("wave_started", BattleDefinitions.IsBoss(Wave) ? "首领来袭：全军自由合围" : "全军接敌：自由转向迎战");
            SpawnDue();
            return new CommandResult(true, "战斗开始");
        }

        private CommandResult CombatCommand(BattleCommand c)
        {
            return Reject("战斗中不能招募、移动或合成");
        }

        public void Advance(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0 || seconds > 60) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (Phase != RunPhase.Fighting || Paused) return;
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
            SpawnDue();
            var active = new List<BattleCombatant>(troops.Count + enemies.Count);
            active.AddRange(troops.Where(t => t.Deployed && t.Alive));
            active.AddRange(enemies.Where(e => e.Alive));
            foreach (var unit in active)
            {
                unit.Status.Tick(BattleTime);
                unit.Buffs.Tick(BattleTime);
            }

            if (!enraged && BattleTime >= BattleDefinitions.WaveTimeout)
            {
                enraged = true;
                Emit("enemies_enraged", "战斗超时，敌军进入狂暴！");
            }

            StepSkills();
            StepZones(active, dt);
            navInputs.Clear();
            foreach (var unit in active)
            {
                if (!unit.Alive) continue;
                IReadOnlyList<BattleCombatant> opponents = unit.Side == BattleSide.Ally ? (IReadOnlyList<BattleCombatant>)enemies : troops;
                StepUnit(unit, opponents, active, dt);
            }

            long navigationStart = System.Diagnostics.Stopwatch.GetTimestamp();
            navigation.Step(navInputs, dt, navOutputs);
            NavigationCpuTicks += System.Diagnostics.Stopwatch.GetTimestamp() - navigationStart;
            foreach (var output in navOutputs)
            {
                var unit = FindUnit(output.Id);
                if (unit == null || !unit.Alive || unit.JumpEndsAt > BattleTime) continue;
                unit.X = output.NewX;
                unit.Z = output.NewZ;
                unit.Vx = output.NewVx;
                unit.Vz = output.NewVz;
                if (unit.Navigation.Status != NavigationStatus.Holding && unit.TargetId != 0)
                    SetAiState(unit, output.Waiting ? CombatAiState.Waiting : CombatAiState.Pursuing);
                if (unit.Vx * unit.Vx + unit.Vz * unit.Vz > .0025f)
                    unit.FacingYaw = BattleDefinitions.MoveTowardsAngle(unit.FacingYaw,
                        BattleDefinitions.VectorToYaw(unit.Vx, unit.Vz, unit.FacingYaw), unit.TurnSpeed * 1.4f * dt);
            }

            if (!troops.Any(t => t.Deployed && t.Alive))
            {
                Phase = RunPhase.Lost; Paused = false;
                Emit("run_lost", "全军阵亡，远征失败");
                return;
            }
            if (spawnCursor == spawns.Count && !enemies.Any(e => e.Alive)) FinishWave();
        }

        private void SpawnDue()
        {
            while (spawnCursor < spawns.Count && spawns[spawnCursor].At <= BattleTime + .00001f)
            {
                var spawn = spawns[spawnCursor++];
                if (spawnCursor == 1 || spawns[spawnCursor - 2].Batch != spawn.Batch)
                    Emit("spawn_batch", "第 " + spawn.Batch + " 批怪物入场", amount: spawn.Batch);
                Spawn(spawn);
            }
        }

        private void StepUnit(BattleCombatant unit, IReadOnlyList<BattleCombatant> opponents, List<BattleCombatant> active, float dt)
        {
            unit.Cooldown -= dt;
            int focus = 0;
            if (StepJump(unit, opponents, active))
            {
                QueueNavigation(unit, default, hold: true, speed: 0f);
                return;
            }

            var intent = BattleAgentBrain.Plan(unit, opponents, focus, BattleTime);
            bool busy = SkillBusy(unit.Id);
            unit.TargetId = intent.TargetId;
            if (busy && intent.State != CombatAiState.Controlled) intent.State = CombatAiState.Engaging;
            if (intent.HoldPosition) SetAiState(unit, intent.State);

            float speed = unit.MoveSpeed;
            if (unit.Status.Has(UnitStatus.Slowed, BattleTime)) speed *= .7f;
            if (enraged && unit.Side == BattleSide.Enemy) speed *= 1.5f;
            QueueNavigation(unit, intent, busy || intent.HoldPosition, speed);
            if (busy) return;

            var target = FindUnit(intent.TargetId);
            if (target == null) return;
            float desiredYaw = BattleDefinitions.VectorToYaw(intent.LookX, intent.LookZ, unit.FacingYaw);
            if (intent.HoldPosition)
                unit.FacingYaw = BattleDefinitions.MoveTowardsAngle(unit.FacingYaw, desiredYaw, unit.TurnSpeed * dt);
            bool facingReady = Math.Abs(BattleDefinitions.DeltaAngle(unit.FacingYaw, desiredYaw)) <= BattleDefinitions.AttackAngleTolerance;

            if (unit.Mana >= BattleCombatant.MaxMana && unit.Status.CanCast(BattleTime))
            {
                for (int slot = unit.Star - 1; slot >= 0; slot--)
                {
                    var skill = unit.Definition.Skills[slot];
                    if (skill == null || skill.Passive || unit.SkillReadyAt[slot] > BattleTime) continue;
                    var skillTarget = skill.Effect == SkillEffect.Heal
                        ? active.Where(u => u.Alive && u.Side == unit.Side && u.Hp < u.MaxHp && BattleDefinitions.CanReach(unit, u))
                            .OrderBy(u => u.Hp / u.MaxHp).ThenBy(u => u.Id).FirstOrDefault()
                        : intent.CanAttack && facingReady ? target : null;
                    if (skillTarget == null) continue;
                    BeginSkill(unit, skillTarget, skill);
                    unit.Mana = 0f;
                    unit.SkillReadyAt[slot] = BattleTime + skill.Cooldown;
                    return;
                }
            }

            if (!intent.CanAttack || !facingReady || unit.Cooldown > 0f) return;
            BeginSkill(unit, target, null);
            unit.Cooldown = unit.Properties.AttackInterval;
        }

        private void QueueNavigation(BattleCombatant unit, CombatAiIntent intent, bool hold, float speed)
        {
            navInputs.Add(new BattleNavigation.AgentInput
            {
                Id = unit.Id, IsTroop = unit.Side == BattleSide.Ally, X = unit.X, Z = unit.Z,
                Radius = unit.Properties.Radius, MaxSpeed = speed, TargetId = intent.TargetId,
                TargetX = intent.TargetX, TargetZ = intent.TargetZ, TargetRadius = intent.TargetRadius,
                AttackReach = intent.AttackReach, HoldPosition = hold, Airborne = unit.JumpEndsAt > BattleTime, State = unit.Navigation
            });
        }

        private void SetAiState(BattleCombatant unit, CombatAiState state)
        {
            if (unit.AiState == state) return;
            var previous = unit.AiState;
            unit.AiState = state;
            Emit("ai_state_changed", previous + " -> " + state, unit.Id, unit.TargetId);
        }

        private BattleCombatant FindUnit(int id) =>
            (BattleCombatant)troops.Find(t => t.Id == id) ?? enemies.Find(e => e.Id == id);

        private void Spawn(WaveSpawn spawn)
        {
            var unit = new Enemy { Id = nextId++, MonsterId = spawn.MonsterId, Star = spawn.Star, Lane = spawn.Lane, Cooldown = .55f };
            ResetUnit(unit);
            unit.X = unit.Boss ? 6.25f : 6.15f;
            unit.Z = BattleDefinitions.CellZ(spawn.Lane);
            // 同批同路用侧向错位，避免多个角色在完全相同的点重叠。
            int nearby = enemies.Count(e => e.Alive && e.Lane == spawn.Lane && Math.Abs(e.X - unit.X) < .5f);
            if (nearby > 0) unit.Z = Clamp(unit.Z + ((nearby + 1) / 2) * (nearby % 2 == 0 ? -.62f : .62f), -2.85f, 2.85f);
            enemies.Add(unit);
            if (BattleTables.Current.HasEnemySynergy(Wave))
            {
                Buffs.Apply(enemies, BattleSide.Enemy);
                foreach (var enemy in enemies)
                {
                    float fraction = enemy.Hp / enemy.MaxHp;
                    enemy.MaxHp = enemy.Properties.Health;
                    enemy.Hp = enemy.MaxHp * fraction;
                }
            }
            Emit("enemy_spawned", unit.Definition.Name, 0, unit.Id, unit.Lane);
        }

        private static void ResetUnit(BattleCombatant unit)
        {
            unit.ResetCombat();
            for (int slot = 0; slot < unit.Star; slot++)
            {
                var skill = unit.Definition.Skills[slot];
                if (skill == null || !skill.Passive) continue;
                var modifier = skill.Effect == SkillEffect.ArmorBonus ? UnitModifier.Armor :
                    skill.Effect == SkillEffect.SpeedBonus ? UnitModifier.Speed : UnitModifier.ManaGain;
                unit.Buffs.Apply(modifier, skill.Id + "/" + slot, skill.Power, float.MaxValue);
            }
            unit.Hp = unit.MaxHp = unit.Properties.Health;
            unit.MoveSpeed = unit.Properties.MoveSpeed;
            unit.TurnSpeed = unit.Properties.TurnSpeed;
        }

        private void DealDamage(BattleCombatant source, BattleCombatant target, float amount, bool magic, bool basic = false, bool periodic = false)
        {
            if (!target.Alive || source.Side == target.Side) return;
            if (enraged && source.Side == BattleSide.Enemy) amount *= 2.5f;
            float defense = magic ? target.Properties.MagicResist : target.Properties.Armor;
            amount = Math.Max(1f, (float)Math.Floor(amount * 100f / (100f + defense)));
            float hpLoss = Math.Min(target.Hp, target.Buffs.Absorb(amount));
            target.Hp -= hpLoss;
            RecordDamage(source, hpLoss);
            var table = BattleTables.Current;
            if (basic && source.Alive) AddMana(source, table.Tune(BattleTuning.BasicMana) + source.Buffs.Total(UnitModifier.ManaGain));
            if (hpLoss > 0f && target.Alive)
                AddMana(target, Math.Min(table.Tune(BattleTuning.DamageManaLimit),
                    table.Tune(BattleTuning.DamageManaBase) + hpLoss * table.Tune(BattleTuning.DamageManaHealthRatio) / target.MaxHp));
            string type = periodic ? "skill_tick" : target.Side == BattleSide.Ally ? "troop_hit" : "hit";
            Emit(type, magic ? "法术命中" : "物理命中", source.Id, target.Id, target.Lane, hpLoss);
            if (target.Alive) return;
            target.Vx = target.Vz = 0f;
            target.JumpEndsAt = 0f;
            SetAiState(target, CombatAiState.Dead);
            Emit(target.Side == BattleSide.Ally ? "troop_down" : "enemy_killed", target.Definition.Name, source.Id, target.Id, target.Lane);
        }

        private static void AddMana(BattleCombatant unit, float amount) =>
            unit.Mana = Math.Min(BattleCombatant.MaxMana, unit.Mana + amount);

        private void StepZones(IReadOnlyList<BattleCombatant> active, float dt)
        {
            foreach (var zone in zones.ToArray())
            {
                zone.Remaining -= dt;
                zone.UntilTick -= dt;
                if (zone.UntilTick <= 0f)
                {
                    zone.UntilTick += .5f;
                    var caster = FindUnit(zone.Source);
                    if (caster != null)
                        foreach (var unit in active)
                            if (Distance(unit.X, unit.Z, zone.X, zone.Z) <= zone.Radius)
                                DealDamage(caster, unit, zone.Damage, magic: true, periodic: true);
                }
                if (zone.Remaining <= 0f) zones.Remove(zone);
            }
        }

        private void FinishWave()
        {
            Paused = false; zones.Clear(); skillInstances.Clear(); enraged = false;
            GrantWavePopExp(2);
            foreach (var t in troops) ResetTroop(t, snapToHome: false);
            Buffs.Apply(troops);
            foreach (var troop in troops) troop.Hp = troop.MaxHp = troop.Properties.Health;
            if (Wave >= BattleDefinitions.TotalWaves)
            {
                Phase = RunPhase.Won;
                Emit("run_won", "第一关远征全胜！");
            }
            else
            {
                Phase = BattleTables.Current.HasReward(Wave) ? RunPhase.Reward : RunPhase.WaveResult;
                Emit("wave_cleared", "本波告捷！全员满血归队，人口经验 +2");
            }
        }

        private static float Distance(float x, float z, float xx, float zz) => (float)Math.Sqrt(BattleDefinitions.DistanceSquared(x, z, xx, zz));
        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
    }
}
