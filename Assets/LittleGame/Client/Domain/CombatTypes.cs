using System;
using System.Collections.Generic;

namespace LittleGame.Combat
{
    public enum RunPhase { Preparation, Fighting, Reward, WaveResult, Won, Lost }
    public enum TroopKind { Shield, Archer, Storm, Frost }
    public enum EnemyKind { Grunt, Runner, Armored, Healer, Giant }
    public enum SkillBranch { None, Bastion, WarCry, Pierce, Gale, StormPool, ChainLightning, DeepFreeze, IceArmor }
    public enum RewardKind { Reinforcements, WideStorm, LongFreeze }
    public enum AimKind { None, Focus, Protect }
    public enum CommandKind { NewRun, Spin, Deploy, Recall, Recycle, Merge, ChooseBranch, StartWave, NextWave, ChooseReward, Aim, CancelAim, Focus, Protect, Pause, SetSpeed }

    public sealed class BattleCommand
    {
        public CommandKind Kind;
        public int UnitId, OtherId, ThirdId, Lane, Row, Seed = 173;
        /// <summary>自由拖拽落位：启用时直接按世界坐标 (TargetX, TargetZ) 校验是否处于白色布阵框内。</summary>
        public bool UseWorldPos;
        public float TargetX, TargetZ;
        public SkillBranch Branch;
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

    public sealed class Troop
    {
        public int Id { get; internal set; }
        public TroopKind Kind { get; internal set; }
        public int Star { get; internal set; } = 1;
        public SkillBranch Branch { get; internal set; }
        public int Lane { get; internal set; } = -1;
        public int Row { get; internal set; } = -1;
        /// <summary>白色布阵框内的归队目标坐标。</summary>
        public float HomeX { get; internal set; }
        public float HomeZ { get; internal set; }
        public float X { get; internal set; }
        public float Z { get; internal set; }
        /// <summary>执行移动并处理碰撞/边界后的实际速度（米/秒）。</summary>
        public float Vx { get; internal set; }
        public float Vz { get; internal set; }
        /// <summary>当前追击/攻击目标；避免新怪出现时每帧抢换最近目标。</summary>
        public int TargetId { get; internal set; }
        public CombatAiState AiState { get; internal set; }
        public BattleNavigationState Navigation { get; } = new BattleNavigationState();
        /// <summary>当前朝向角（度，0=+Z，90=+X 向右推进，-90=-X 向左）。</summary>
        public float FacingYaw { get; internal set; } = 90f;
        /// <summary>转身速率（度/秒）。</summary>
        public float TurnSpeed { get; internal set; } = 360f;
        /// <summary>移动速度（米/秒），不同职业速度不同。</summary>
        public float MoveSpeed { get; internal set; } = 2.4f;
        public float Hp { get; internal set; }
        public float MaxHp { get; internal set; }
        public float Cooldown { get; internal set; }
        public float SkillCooldown { get; internal set; }
        public float Shield { get; internal set; }
        public float ShieldUntil { get; internal set; }
        public bool Deployed => Lane >= 0;
        public bool Alive => Hp > 0;
        public bool NeedsRegroup => Deployed && ((X - HomeX) * (X - HomeX) + (Z - HomeZ) * (Z - HomeZ) > 0.0025f);
    }

    public sealed class Enemy
    {
        public int Id { get; internal set; }
        public EnemyKind Kind { get; internal set; }
        public int Lane { get; internal set; }
        public float X { get; internal set; }
        public float Z { get; internal set; }
        /// <summary>执行移动并处理碰撞/边界后的实际速度（米/秒）。</summary>
        public float Vx { get; internal set; }
        public float Vz { get; internal set; }
        public int TargetId { get; internal set; }
        public CombatAiState AiState { get; internal set; }
        public BattleNavigationState Navigation { get; } = new BattleNavigationState();
        public float FacingYaw { get; internal set; } = -90f;
        public float TurnSpeed { get; internal set; } = 360f;
        public float MoveSpeed { get; internal set; } = 1.15f;
        public float Hp { get; internal set; }
        public float MaxHp { get; internal set; }
        public float Cooldown { get; internal set; }
        public float FrozenUntil { get; internal set; }
        public float SlowUntil { get; internal set; }
        public bool Alive => Hp > 0;
        public bool Boss => Kind == EnemyKind.Giant;
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

    public sealed class WaveSpawn { public EnemyKind Kind; public int Lane; public float At; }
    public sealed class StormZone { public int Source, Lane; public float X, Z, Remaining, UntilTick; public float Damage; }
    public sealed class SaveTroop { public int Id, Star, Lane, Row; public float HomeX, HomeZ; public bool HasHomePos; public TroopKind Kind; public SkillBranch Branch; }

    public sealed class RunSave
    {
        public int Version = 4, Seed, Wave, Spins, Misses, SpinCount, NextId, Speed;
        public int MaxDeployed = 6, PopExp;
        public float MarchDistance;
        public uint RandomState;
        public RunPhase Phase;
        public List<SaveTroop> Troops = new List<SaveTroop>();
        public List<RewardKind> Rewards = new List<RewardKind>();
        public List<TroopKind> Pending = new List<TroopKind>();
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
