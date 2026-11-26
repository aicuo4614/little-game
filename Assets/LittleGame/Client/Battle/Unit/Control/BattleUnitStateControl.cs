using System;
using System.Collections.Generic;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 单位表现状态机控制器（对齐 Demo BattleUnitStateControl）：
    /// 明确 Idle / Move / Skill / Controlled / Die 五态转移边界，统一中断与恢复，与规则层分离。
    /// </summary>
    public sealed class BattleUnitStateControl : BattleUnitControlBase
    {
        private readonly Dictionary<BattleUnitStateType, BattleUnitStateBase> states =
            new Dictionary<BattleUnitStateType, BattleUnitStateBase>();
        private readonly BattleUnitIdleState idleState = new BattleUnitIdleState();
        private readonly BattleUnitMoveState moveState = new BattleUnitMoveState();
        private readonly BattleUnitSkillState skillState = new BattleUnitSkillState();
        private readonly BattleUnitControlledState controlledState = new BattleUnitControlledState();
        private readonly BattleUnitDieState dieState = new BattleUnitDieState();

        private BattleUnitStateBase current;

        public BattleUnitStateType CurrentStateType { get; private set; }
        public bool DeathCompleted => CurrentStateType == BattleUnitStateType.Die && dieState.IsComplete;
        public bool IsInSkill => CurrentStateType == BattleUnitStateType.Skill;

        public override void Init(BattleUnit owner)
        {
            base.Init(owner);
            states[BattleUnitStateType.Idle] = idleState;
            states[BattleUnitStateType.Move] = moveState;
            states[BattleUnitStateType.Skill] = skillState;
            states[BattleUnitStateType.Controlled] = controlledState;
            states[BattleUnitStateType.Die] = dieState;

            foreach (var state in states.Values)
            {
                state.Init(owner, this);
            }
        }

        public override void Start()
        {
            ChangeState(BattleUnitStateType.Idle);
        }

        public void ChangeState(BattleUnitStateType next)
        {
            if (Owner.IsEnded || CurrentStateType == next)
            {
                return;
            }

            // 死亡态为终态，只能通过显式 Revive 恢复（波次重置）
            if (CurrentStateType == BattleUnitStateType.Die)
            {
                return;
            }

            var nextState = states[next];
            current?.OnExit();
            CurrentStateType = next;
            current = nextState;
            current.OnEnter();
        }

        /// <summary>进入死亡状态（自动退出技能/受控并停止所有 TimelinePlayback）。</summary>
        public void ShowDeath()
        {
            ChangeState(BattleUnitStateType.Die);
        }

        /// <summary>波次恢复/重开时将死亡单位重置回待机态。</summary>
        public void Revive()
        {
            if (Owner.IsEnded || CurrentStateType != BattleUnitStateType.Die)
            {
                return;
            }

            current?.OnExit();
            CurrentStateType = BattleUnitStateType.Idle;
            current = idleState;
            current.OnEnter();
        }

        /// <summary>同步规则层的受控/移动/待机意图（不打断进行中的技能，除非受控或死亡）。</summary>
        public void SyncLocomotion(bool alive, bool controlled, bool moving)
        {
            if (Owner.IsEnded)
            {
                return;
            }

            if (!alive)
            {
                ShowDeath();
                return;
            }

            if (CurrentStateType == BattleUnitStateType.Die)
            {
                Revive();
            }

            if (controlled)
            {
                ChangeState(BattleUnitStateType.Controlled);
                return;
            }

            if (CurrentStateType == BattleUnitStateType.Controlled)
            {
                ChangeState(moving ? BattleUnitStateType.Move : BattleUnitStateType.Idle);
                return;
            }

            // 技能演出期间不覆盖状态，由 SkillState 结束后回退 Idle
            if (CurrentStateType == BattleUnitStateType.Skill)
            {
                return;
            }

            ChangeState(moving ? BattleUnitStateType.Move : BattleUnitStateType.Idle);
        }

        /// <summary>进入技能状态并登记 Timeline 停止回调（对齐 Demo BeginSkill）。</summary>
        public int BeginSkill(Action play, Action stopPlayback)
        {
            if (Owner.IsEnded || CurrentStateType == BattleUnitStateType.Die || CurrentStateType == BattleUnitStateType.Controlled)
            {
                return 0;
            }

            ChangeState(BattleUnitStateType.Skill);
            return skillState.AddPlayback(play, stopPlayback);
        }

        /// <summary>完成指定技能句柄（防止迟到回调影响新状态）。</summary>
        public void CompleteSkill(int handle)
        {
            if (handle <= 0) return;
            skillState.CompletePlayback(handle);
        }

        /// <summary>Timeline 复位后恢复当前状态外观。</summary>
        public void RefreshPresentation()
        {
            if (!Owner.IsEnded)
            {
                current?.RefreshPresentation();
            }
        }

        public override void Tick(float deltaTime)
        {
            current?.OnUpdate(deltaTime);
        }

        public override void End()
        {
            current?.OnExit();
            current = null;
            CurrentStateType = BattleUnitStateType.None;
        }
    }
}
