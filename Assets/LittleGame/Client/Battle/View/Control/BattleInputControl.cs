using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>战斗共用输入子系统（对齐 Demo BattleInputControl），统一承接鼠标、触屏与 MCP pointer。</summary>
    public sealed class BattleInputControl : BattleControlBase
    {
        public BattleInput Input { get; private set; }

        public override void Init(BattleView owner)
        {
            base.Init(owner);
            Input = owner.gameObject.GetComponent<BattleInput>();
            if (Input == null)
            {
                Input = owner.gameObject.AddComponent<BattleInput>();
            }
        }

        public void BindHud(BattleHud hud)
        {
            Input?.Bind(Owner.App, Owner, hud);
        }

        public void Cancel()
        {
            Input?.Cancel();
        }
    }
}
