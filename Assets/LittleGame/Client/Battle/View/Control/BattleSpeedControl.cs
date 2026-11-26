using System;

namespace LittleGame.Client.Battle
{
    /// <summary>战斗倍速子系统（对齐 Demo BattleSpeedControl）。</summary>
    public sealed class BattleSpeedControl : BattleControlBase
    {
        public int CurrentSpeed { get; private set; } = 1;

        public void Sync(int speed)
        {
            CurrentSpeed = Math.Max(1, speed);
        }

        public void Stop()
        {
            CurrentSpeed = 1;
        }
    }
}
