using System;
using System.IO;
using Newtonsoft.Json.Linq;

/// <summary>跨技能复制数据通过编辑器自身的剪贴板保存。</summary>
internal sealed class TimelineEditorMcpClipboard
{
    internal bool HasClip => TimelineMenuControl.HasCopiedClip;

    internal void Copy(JObject args, TimelineConfig current)
    {
        var path = (string)args["path"];
        var source = string.IsNullOrEmpty(path) ? current :
            TimelineJsonUtil.FromJson(File.ReadAllText(EditorTimeLine.ResolveTimelinePath(path)));
        var key = (string)args["key"];
        var clip = source?.Clips.Find(item => item.ClipKey == key)
            ?? throw new ArgumentException("找不到源 Clip：" + key);
        TimelineMenuControl.CopyClipConfig(clip);
    }

    internal TimelineClipConfigBase Paste() => TimelineMenuControl.ReadCopiedClip()
        ?? throw new InvalidOperationException("剪贴板为空。");
}
