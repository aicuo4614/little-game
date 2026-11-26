using UnityEngine;

namespace LittleGame.Client
{
    /// <summary>角色资源与规则解耦；普通动作、AnimatorController、武器和升星装配由资源配置。</summary>
    public sealed class UnitAppearance : ScriptableObject
    {
        public string Source;
        public GameObject Model;
        public RuntimeAnimatorController AnimatorController;
        public AnimationClip Idle, Move, Attack, Hit, Death, Slam;
        public float Height = 1.05f;
        public float AttackSpeed = 1.5f;
    }
}
