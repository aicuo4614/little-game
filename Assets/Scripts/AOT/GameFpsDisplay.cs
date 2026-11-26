using UnityEngine;

/// <summary>
/// 轻量 FPS 浮层（OnGUI）。由 <see cref="GfxQualityMgr"/> 根据 <see cref="QualitySOConfig.ShowFps"/> 控制。
/// </summary>
public class GameFpsDisplay : MonoBehaviour
{
    private static GameFpsDisplay instance;

    private float accum;
    private int frames;
    private float displayFps;
    private GUIStyle style;

    /// <summary>
    /// 按开关确保 FPS 浮层存在或销毁。
    /// </summary>
    public static void Ensure(bool show)
    {
        if (show)
        {
            if (instance != null)
            {
                instance.gameObject.SetActive(true);
                return;
            }

            var go = new GameObject("[GameFpsDisplay]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GameFpsDisplay>();
            return;
        }

        if (instance != null)
        {
            Destroy(instance.gameObject);
            instance = null;
        }
    }

    private void Update()
    {
        accum += Time.unscaledDeltaTime;
        frames++;
        if (accum >= 1f)
        {
            displayFps = frames / accum;
            accum = 0f;
            frames = 0;
        }
    }

    private void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label);
            style.fontSize = 28;
            style.normal.textColor = Color.green;
            style.fontStyle = FontStyle.Bold;
        }

        GUI.Label(new Rect(12f, 12f, 240f, 40f), $"FPS: {displayFps:0.0}", style);
    }
}
