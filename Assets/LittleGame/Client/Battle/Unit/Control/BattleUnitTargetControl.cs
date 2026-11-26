using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>单位屏幕投影与拾取辅助控件（对齐 Demo BattleUnitTargetControl）。</summary>
    public sealed class BattleUnitTargetControl : BattleUnitControlBase
    {
        public Vector2 GetScreenPosition()
        {
            if (Owner.Camera == null || Owner.Transform == null)
            {
                return new Vector2(-1000f, -1000f);
            }

            return Owner.Camera.WorldToScreenPoint(Owner.Transform.position + Vector3.up * 0.4f);
        }
    }
}
