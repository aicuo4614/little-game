using System.Collections.Generic;
using UnityEngine;

namespace LittleGame.Client.Battle
{
    /// <summary>手机清晰可读的大号 3D 世界空间伤害、治疗与状态飘字（带深色阴影描边，无碰撞体）。</summary>
    public sealed class BattleFloatingText : MonoBehaviour
    {
        private const int MaxEntries = 36;

        private sealed class Entry
        {
            public GameObject Root;
            public TextMesh Text;
            public TextMesh Shadow;
            public Vector3 StartPos;
            public Color BaseColor;
            public float Age;
            public float Lifetime;
            public float RiseHeight;
            public float BaseScale;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private Camera targetCamera;
        private Font font;

        public void Initialize(Camera camera)
        {
            targetCamera = camera;
            font = Resources.Load<Font>("GameFont");
        }

        public void Spawn(Vector3 worldPos, string content, Color color, float scale = 1f, float lifetime = 0.68f)
        {
            if (targetCamera == null || string.IsNullOrWhiteSpace(content)) return;
            if (entries.Count >= MaxEntries)
            {
                if (entries[0].Root != null) Destroy(entries[0].Root);
                entries.RemoveAt(0);
            }

            var go = new GameObject("FloatText");
            go.transform.SetParent(transform, false);
            Vector3 jitter = new Vector3(Random.Range(-0.26f, 0.26f), Random.Range(0.05f, 0.22f), Random.Range(-0.12f, 0.12f));
            go.transform.position = worldPos + jitter;
            go.transform.rotation = targetCamera.transform.rotation;

            float clampedScale = Mathf.Clamp(scale, 0.85f, 1.55f);
            int fontSize = Mathf.RoundToInt(46 * clampedScale);
            const float charSize = 0.062f;

            // 深色投影底字，保证在亮色粒子与地砖上依然清晰醒目
            var shadowGo = new GameObject("Shadow");
            shadowGo.transform.SetParent(go.transform, false);
            shadowGo.transform.localPosition = new Vector3(0.032f, -0.032f, 0.03f);
            var shadowTm = CreateTextMesh(shadowGo, content, fontSize, charSize, new Color(0.04f, 0.03f, 0.08f, 0.92f));

            var mainGo = new GameObject("Main");
            mainGo.transform.SetParent(go.transform, false);
            var mainTm = CreateTextMesh(mainGo, content, fontSize, charSize, color);

            entries.Add(new Entry
            {
                Root = go,
                Text = mainTm,
                Shadow = shadowTm,
                StartPos = go.transform.position,
                BaseColor = color,
                Age = 0f,
                Lifetime = Mathf.Max(0.32f, lifetime),
                RiseHeight = 0.92f * clampedScale,
                BaseScale = 1f
            });
        }

        private TextMesh CreateTextMesh(GameObject go, string content, int fontSize, float charSize, Color color)
        {
            var tm = go.AddComponent<TextMesh>();
            tm.text = content;
            tm.fontSize = fontSize;
            tm.characterSize = charSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.color = color;
            if (font != null)
            {
                tm.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            return tm;
        }

        public void Step(float dt, bool paused)
        {
            if (paused || dt <= 0f) return;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                e.Age += dt;
                if (e.Age >= e.Lifetime || e.Root == null)
                {
                    if (e.Root != null) Destroy(e.Root);
                    entries.RemoveAt(i);
                    continue;
                }

                float t = Mathf.Clamp01(e.Age / e.Lifetime);
                float ease = 1f - (1f - t) * (1f - t);
                e.Root.transform.position = e.StartPos + Vector3.up * (e.RiseHeight * ease);
                float pop = t < 0.16f ? Mathf.Lerp(0.65f, 1.24f, t / 0.16f) : Mathf.Lerp(1.24f, 0.96f, (t - 0.16f) / 0.84f);
                e.Root.transform.localScale = Vector3.one * (e.BaseScale * pop);

                float alpha = t > 0.62f ? Mathf.Lerp(1f, 0f, (t - 0.62f) / 0.38f) : 1f;
                var c = e.BaseColor;
                c.a = alpha;
                e.Text.color = c;
                if (e.Shadow != null)
                {
                    var sc = e.Shadow.color;
                    sc.a = 0.92f * alpha;
                    e.Shadow.color = sc;
                }
            }
        }

        public void ClearAll()
        {
            for (int i = entries.Count - 1; i >= 0; i--)
                if (entries[i].Root != null) Destroy(entries[i].Root);
            entries.Clear();
        }
    }
}
