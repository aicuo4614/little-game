using UnityEditor;

/// <summary>时间轴对象绑定声明。预览选择保存在配置中，实战对象由程序注入。</summary>
public sealed class TimelineClipSetHitTargets : TimelineClipBase
{
    public override void Init(TimelineControl control, TimelineClipConfigBase config)
    {
        config.Duration = 0f;
        base.Init(control, config);
    }

    public override void DrawInspector()
    {
        base.DrawInspector();
        EditorGUILayout.HelpBox(TimelineBindingEditorText.PreviewHint, MessageType.Info);
        var binding = (TimelineClipSetHitTargetsConfig)config;
        control.PreviewBindings.DrawSource(binding);
        control.PreviewBindings.DrawTargets(binding.PreviewTargets);
    }

    public override bool TryNudgeEditableDuration(float frameStep, int frameDelta)
    {
        return false;
    }

    public override void Draw()
    {
        config.Duration = 0f;
        base.Draw();
    }

    protected override void SetRect()
    {
        base.SetRect();
        rect.width = 100f;
    }
}
