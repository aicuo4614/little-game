using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>单位材质闪白、受击反馈与点选标记控件（对齐 Demo BattleUnitGraphicControl）。</summary>
    public sealed class BattleUnitGraphicControl : BattleUnitControlBase
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock mpb;
        private float hitFlashTimer;
        private BattleUnitMarkType currentMark;

        public BattleUnitMarkType CurrentMark => currentMark;

        public override void Init(BattleUnit owner)
        {
            base.Init(owner);
            mpb = new MaterialPropertyBlock();
        }

        public void SetMark(BattleUnitMarkType mark)
        {
            currentMark = mark;
        }

        public void TriggerHitFlash(float duration = 0.12f)
        {
            hitFlashTimer = Mathf.Max(hitFlashTimer, duration);
            ApplyTint(new Color(1.45f, 1.45f, 1.45f, 1f));
        }

        public override void Tick(float deltaTime)
        {
            if (hitFlashTimer <= 0f)
            {
                return;
            }

            hitFlashTimer -= deltaTime;
            if (hitFlashTimer <= 0f)
            {
                ClearTint();
            }
        }

        public override void ReleaseResources()
        {
            ClearTint();
        }

        private void ApplyTint(Color tint)
        {
            var renderers = Owner.ModelControl.Renderers;
            if (renderers == null || mpb == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(BaseColorId, tint);
                mpb.SetColor(ColorId, tint);
                r.SetPropertyBlock(mpb);
            }
        }

        private void ClearTint()
        {
            var renderers = Owner.ModelControl.Renderers;
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].SetPropertyBlock(null);
                }
            }
        }
    }
}
