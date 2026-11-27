using System;
using System.Collections.Generic;

namespace LittleGame.Combat
{
    public enum RunPhase { Preparation, Fighting, Reward, WaveResult, Won, Lost }
    public enum TroopKind { Shield, Archer, Storm, Frost }
    public enum SkillBranch { None, Bastion, WarCry, Pierce, Gale, StormPool, ChainLightning, DeepFreeze, IceArmor }
    public enum RewardKind { Reinforcements, WideStorm, LongFreeze }
    public enum AimKind { None, Focus, Protect }
    public enum CommandKind { NewRun, Spin, Deploy, Recall, Recycle, Merge, StartWave, NextWave, ChooseReward, Aim, CancelAim, Focus, Protect, Pause, SetSpeed }

    public sealed class BattleCommand
    {
        public CommandKind Kind;
        public int UnitId, OtherId, ThirdId, Lane, Row, Seed = 173;
        /// <summary>自由拖拽落位：启用时直接按世界坐标 (TargetX, TargetZ) 校验是否处于白色布阵框内。</summary>
        public bool UseWorldPos;
        public float TargetX, TargetZ;
        public RewardKind Reward;
        public AimKind Aim;
        public bool Flag;
        public int Speed = 1;
    }

    public sealed class CommandResult
    {
        public bool Accepted;
        public string Reason;
        public CommandResult(bool accepted, string reason) { Accepted = accepted; Reason = reason; }
    }

    public enum BattleSide { Ally, Enemy }

    public abstract class BattleCombatant
    {
        public const float MaxMana = 100f;
        public int Id { get; internal set; }
        public int Star { get; internal set; } = 1;
        public int Lane { get; internal set; } = -1;
        public float X { get; internal set; }
        public float Z { get; internal set; }
        public float Vx { get; internal set; }
        public float Vz { get; internal set; }
        public int TargetId { get; internal set; }
        public CombatAiState AiState { get; internal set; }
        public BattleNavigationState Navigation { get; } = new BattleNavigationState();
        public abstract BattleSide Side { get; }
        public abstract UnitDefinition Definition { get; }
        public virtual bool Deployed => true;
        public bool Alive => Hp > 0f;
        public bool Boss => Definition.Grade == UnitGrade.Boss;
        public float FacingYaw { get; internal set; }
        public float TurnSpeed { get; internal set; } = 360f;
        public float MoveSpeed { get; internal set; } = 2.4f;
        public float Hp { get; internal set; }
        public float MaxHp { get; internal set; }
        public float Cooldown { get; internal set; }
        public float Mana { get; internal set; }
        public float[] SkillReadyAt { get; } = new float[3];
        public UnitBuffControl Buffs { get; } = new UnitBuffControl();
        public UnitStatusControl Status { get; } = new UnitStatusControl();
        public UnitPropertyControl Properties { get; }

        public float Shield => Buffs.Total(UnitModifier.Shield);
        public float FrozenUntil => Status.Until(UnitStatus.Frozen);
        public float SlowUntil => Status.Until(UnitStatus.Slowed);
        public bool Jumped { get; internal set; }
        public float JumpStartedAt { get; internal set; }
        public float JumpEndsAt { get; internal set; }
        public float JumpStartX { get; internal set; }
        public float JumpStartZ { get; internal set; }
        public float JumpX { get; internal set; }
        public float JumpZ { get; internal set; }

        protected BattleCombatant() { Properties = new UnitPropertyControl(this); }

        internal void ResetCombat()
        {
            Mana = 0f;
            Array.Clear(SkillReadyAt, 0, SkillReadyAt.Length);
            Buffs.Clear();
            Status.Clear();
            Jumped = false;
            JumpStartedAt = JumpEndsAt = 0f;
        }
    }

    public sealed class Troop : BattleCombatant
    {
        private UnitDefinition definition;
        private TroopKind kind;
        private string heroId;
        public TroopKind Kind { get => kind; internal set { kind = value; definition = null; } }
        public string HeroId { get => heroId; internal set { heroId = value; definition = null; } }
        public string Identity => HeroId ?? BattleTables.Current.LegacyHero(Kind);
        public override UnitDefinition Definition => definition ?? (definition = BattleTables.Current.Unit(Identity));
        public override BattleSide Side => BattleSide.Ally;
        public int Row { get; internal set; } = -1;
        public float HomeX { get; internal set; }
        public float HomeZ { get; internal set; }
        public override bool Deployed => Lane >= 0;
        public bool NeedsRegroup => Deployed && ((X - HomeX) * (X - HomeX) + (Z - HomeZ) * (Z - HomeZ) > 0.0025f);

        public Troop() { FacingYaw = 90f; }
    }

    public sealed class Enemy : BattleCombatant
    {
        private UnitDefinition definition;
        private string monsterId;
        public string MonsterId { get => monsterId; internal set { monsterId = value; definition = null; } }
        public override UnitDefinition Definition => definition ?? (definition = BattleTables.Current.Unit(MonsterId));
        public override BattleSide Side => BattleSide.Enemy;

        public Enemy() { FacingYaw = -90f; }
    }

    public sealed class CombatEvent
    {
        public long Sequence;
        public string Type, Message;
        public int Source, Target, Lane;
        public float Amount, Time;
        public int SkillInstanceId;
        public string TimelineId, Trigger;
        public float CasterX, CasterZ, TargetX, TargetZ, FacingYaw;
    }

    public sealed class WaveSpawn { public string MonsterId; public int Lane, Batch, Star = 1; public float At; }
    public sealed class StormZone { public int Source, Lane; public float X, Z, Remaining, UntilTick, Damage, Radius; }
    public sealed class SaveTroop { public int Id, Star, Lane, Row; public float HomeX, HomeZ; public bool HasHomePos; public TroopKind Kind; public SkillBranch Branch; public string HeroId; }

    public sealed class RunSave
    {
        public List<HeroDamageRecord> DamageStatistics = new List<HeroDamageRecord>();
        public int Version = 5, Seed, Wave, Spins, Misses, SpinCount, NextId, Speed;
        public int MaxDeployed = 6, PopExp;
        public float MarchDistance;
        public uint RandomState;
        public RunPhase Phase;
        public List<SaveTroop> Troops = new List<SaveTroop>();
        public List<RewardKind> Rewards = new List<RewardKind>();
        public List<TroopKind> Pending = new List<TroopKind>();
        public List<string> PendingHeroes = new List<string>();
        public TroopKind[] Reels = new TroopKind[9];
    }

    /// <summary>项目内固定算法，避免运行时 Random 实现差异破坏回放。</summary>
    public sealed class BattleRandom
    {
        public uint State;
        public BattleRandom(int seed) { State = unchecked((uint)seed); if (State == 0) State = 0x9e3779b9; }
        public int Next(int max) { uint x = State; x ^= x << 13; x ^= x >> 17; x ^= x << 5; State = x; return (int)(x % (uint)max); }
    }
}
