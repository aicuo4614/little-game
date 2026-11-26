using System;
using System.Collections.Generic;

namespace LittleGame.Client.Battle
{
    /// <summary>
    /// 技能状态（对齐 Demo BattleUnitSkillState）：
    /// 持有并发 TimelinePlayback 的停止回调；全部演出结束后交回待机，进入受控或死亡时自动中断所有技能演出。
    /// </summary>
    public sealed class BattleUnitSkillState : BattleUnitStateBase
    {
        private readonly Dictionary<int, Action> playbacks = new Dictionary<int, Action>();
        private int nextHandle;

        public int ActiveCount => playbacks.Count;

        public int AddPlayback(Action play, Action stop)
        {
            int handle = ++nextHandle;
            playbacks.Add(handle, stop);
            try
            {
                play?.Invoke();
            }
            catch
            {
                CompletePlayback(handle);
                throw;
            }
            return handle;
        }

        public void CompletePlayback(int handle)
        {
            if (!playbacks.TryGetValue(handle, out var stop))
            {
                return;
            }

            playbacks.Remove(handle);
            stop?.Invoke();
            if (playbacks.Count == 0 && control.CurrentStateType == BattleUnitStateType.Skill)
            {
                control.ChangeState(BattleUnitStateType.Idle);
            }
        }

        public override void OnExit()
        {
            var stops = new List<Action>(playbacks.Values);
            playbacks.Clear();
            for (int i = 0; i < stops.Count; i++)
            {
                stops[i]?.Invoke();
            }
        }
    }
}
