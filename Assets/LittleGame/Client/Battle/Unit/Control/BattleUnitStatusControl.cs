using UnityEngine;
using UnityEngine.Rendering;

namespace LittleGame.Client.Battle
{
    /// <summary>单位临时状态表现控件（对齐 Demo BattleUnitStatusControl，仅护盾/冻结激活时显示脚底提示圈）。</summary>
    public sealed class BattleUnitStatusControl : BattleUnitControlBase
    {
        private GameObject statusRing;

        public override void Init(BattleUnit owner)
        {
            base.Init(owner);
            if (owner.RingMesh == null || owner.GetMaterial == null)
            {
                return;
            }

            var go = new GameObject("StatusRing", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(owner.Transform, false);
            go.transform.localPosition = new Vector3(0f, 0.028f, 0f);
            go.transform.localScale = new Vector3(0.98f, 1f, 0.98f);
            go.GetComponent<MeshFilter>().sharedMesh = owner.RingMesh;
            var renderer = go.GetComponent<MeshRenderer>();
            var col = new Color(0.27f, 0.7f, 0.92f);
            renderer.sharedMaterial = owner.GetMaterial("Ring" + col, col, true);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
            statusRing = go;
        }

        public void SetStatusActive(bool active)
        {
            if (statusRing != null && statusRing.activeSelf != active)
            {
                statusRing.SetActive(active);
            }
        }
    }
}
