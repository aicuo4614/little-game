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
        public float X { get; internal set; }
        public float Z { get; internal set; }
        public float Hp { get; internal set; }
        public float MaxHp { get; internal set; }
        public float Cooldown { get; internal set; }
        public float SkillCooldown { get; internal set; }
        public float Shield { get; internal set; }
        public float ShieldUntil { get; internal set; }
        public bool Deployed => Lane >= 0;
        public bool Alive => Hp > 0;
    }
    public sealed class Enemy
    {
        public int Id { get; internal set; }
        public EnemyKind Kind { get; internal set; }
        public int Lane { get; internal set; }
        public float X { get; internal set; }
        public float Z { get; internal set; }
        public float Hp { get; internal set; }
        public float MaxHp { get; internal set; }
        public float Cooldown { get; internal set; }
        public float FrozenUntil { get; internal set; }
        public float SlowUntil { get; internal set; }
        public bool Escaped { get; internal set; }
        public bool Alive => Hp > 0 && !Escaped;
        public bool Boss => Kind == EnemyKind.Giant;
    }
    public sealed class CombatEvent
    {
        public long Sequence;
        public string Type, Message;
        public int Source, Target, Lane;
        public float Amount, Time;
    }
    public sealed class WaveSpawn { public EnemyKind Kind; public int Lane; public float At; }
    public sealed class StormZone { public int Source, Lane; public float X, Z, Remaining, UntilTick; public float Damage; }
    public sealed class SaveTroop { public int Id, Star, Lane, Row; public TroopKind Kind; public SkillBranch Branch; }
    public sealed class RunSave
    {
        public int Version = 1, Seed, Wave, GateHp, Spins, Misses, SpinCount, NextId, Speed;
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
