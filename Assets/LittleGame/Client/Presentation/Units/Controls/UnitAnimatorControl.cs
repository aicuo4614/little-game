namespace LittleGame.Client.Units.Controls
{
    /// <summary>单位动作与受击反馈控制器（参考 Demo UnitAnimatorControl）。</summary>
    public sealed class UnitAnimatorControl
    {
        public CharacterMotion Motion { get; }

        public UnitAnimatorControl(CharacterMotion motion)
        {
            Motion = motion;
        }

        public bool DeathFinished => Motion != null && Motion.DeathFinished;
        public string CurrentAction => Motion != null ? Motion.CurrentAction : null;
        public float ActionTime => Motion != null ? Motion.ActionTime : 0f;

        public void Sync(bool alive, bool paused, bool forceWalk = false, float movementSpeed = -1f)
        {
            if (Motion == null) return;
            Motion.SetAlive(alive);
            Motion.Apply(paused, forceWalk, movementSpeed);
        }

        public void PlayAction(string alias, float speed = 1f) => Motion?.PlayAction(alias, speed);
        public void TriggerHitPulse(float duration = 0.12f) => Motion?.TriggerHitPulse(duration);
    }
}
