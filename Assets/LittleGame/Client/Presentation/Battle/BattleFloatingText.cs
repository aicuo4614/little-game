using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LittleGame.Client.Battle
{
    /// <summary>手机清晰可读的大号 3D 世界空间伤害、治疗与状态飘字（带轻量对象池，零高频 Instantiate/Destroy GC，无碰撞体）。</summary>
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

        private readonly List<Entry> active = new List<Entry>(MaxEntries);
        private readonly Stack<Entry> pool = new Stack<Entry>(MaxEntries);
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

            Entry e;
            if (active.Count >= MaxEntries)
            {
                e = active[0];
                active.RemoveAt(0);
            }
            else if (pool.Count > 0)
            {
                e = pool.Pop();
                e.Root.SetActive(true);
            }
            else
            {
                e = CreateEntry();
            }

            Vector3 jitter = new Vector3(Random.Range(-0.26f, 0.26f), Random.Range(0.05f, 0.22f), Random.Range(-0.12f, 0.12f));
            e.StartPos = worldPos + jitter;
            e.Root.transform.position = e.StartPos;
            e.Root.transform.rotation = targetCamera.transform.rotation;

            float clampedScale = Mathf.Clamp(scale, 0.85f, 1.55f);
            int fontSize = Mathf.RoundToInt(46 * clampedScale);
            e.Text.text = content;
            e.Text.fontSize = fontSize;
            e.Text.color = color;
            if (e.Shadow != null)
            {
                e.Shadow.text = content;
                e.Shadow.fontSize = fontSize;
                e.Shadow.color = new Color(0.04f, 0.03f, 0.08f, 0.92f);
            }

            e.BaseColor = color;
            e.Age = 0f;
            e.Lifetime = Mathf.Max(0.32f, lifetime);
            e.RiseHeight = 0.92f * clampedScale;
            e.BaseScale = 1f;
            e.Root.transform.localScale = Vector3.one * 0.65f;
            active.Add(e);
        }

        private Entry CreateEntry()
        {
            var go = new GameObject("FloatText");
            go.transform.SetParent(transform, false);

            var shadowGo = new GameObject("Shadow");
            shadowGo.transform.SetParent(go.transform, false);
            shadowGo.transform.localPosition = new Vector3(0.032f, -0.032f, 0.03f);
            var shadowTm = CreateTextMesh(shadowGo, 46, 0.062f, new Color(0.04f, 0.03f, 0.08f, 0.92f));

            var mainGo = new GameObject("Main");
            mainGo.transform.SetParent(go.transform, false);
            var mainTm = CreateTextMesh(mainGo, 46, 0.062f, Color.white);

            return new Entry { Root = go, Text = mainTm, Shadow = shadowTm };
        }

        private TextMesh CreateTextMesh(GameObject go, int fontSize, float charSize, Color color)
        {
            var tm = go.AddComponent<TextMesh>();
            tm.text = "";
            tm.fontSize = fontSize;
            tm.characterSize = charSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.color = color;
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            if (font != null)
            {
                tm.font = font;
                mr.sharedMaterial = font.material;
            }
            return tm;
        }

        public void Step(float dt, bool paused)
        {
            if (paused || dt <= 0f) return;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var e = active[i];
                e.Age += dt;
                if (e.Age >= e.Lifetime || e.Root == null)
                {
                    RecycleAt(i);
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

        private void RecycleAt(int index)
        {
            var e = active[index];
            active.RemoveAt(index);
            if (e.Root != null)
            {
                e.Root.SetActive(false);
                pool.Push(e);
            }
        }

        public void ClearAll()
        {
            for (int i = active.Count - 1; i >= 0; i--)
                RecycleAt(i);
        }
    }
}
