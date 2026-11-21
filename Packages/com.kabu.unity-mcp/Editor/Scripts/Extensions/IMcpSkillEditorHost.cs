using System;

namespace KabuUnityMcp.Editor.Extensions
{
    /// <summary>
    /// 技能编辑器 Host 契约。实现放在工程 TimelineEditor 程序集；
    /// kabu-unity-mcp 只持接口，不引用 EditorTimeLine。
    /// </summary>
    public interface IMcpSkillEditorHost
    {
        /// <summary>打开技能编辑器场景与窗口。</summary>
        McpHostResult Open();

        /// <summary>窗口 / 当前技能 / 播放与录制状态。</summary>
        McpHostResult Status();

        /// <summary>按资源路径或 TimelineId 加载技能 JSON。</summary>
        McpHostResult Load(string pathOrTimelineId);

        /// <summary>设置预览施法阵营与画质（不写技能 JSON）。</summary>
        McpHostResult SetPreview(string casterTeam, string quality);

        McpHostResult Play(bool fromStart);

        McpHostResult Pause();

        McpHostResult Stop();

        /// <summary>
        /// 停播放后把播放头滑到目标时间（与点标尺相同，禁止瞬移）。
        /// clipKey 非空时以该 Clip 内容起点为准；滑完后再等采样帧回包。
        /// </summary>
        void Seek(float time, string clipKey, Action<McpHostResult> onDone);

        McpHostResult ListClips(int offset, int limit);

        McpHostResult GetClip(string clipKey);

        /// <summary>当前帧实体世界 TRS 快照（与预览录制 catalog/samples 同形）。</summary>
        McpHostResult Snapshot();

        /// <summary>
        /// 异步：可选切阵营后从头播并采样；默认播到 min(CutOff, 内容终点) 自动结束，或 Stop 后回调。
        /// </summary>
        void RecordPlay(
            string casterTeam,
            string quality,
            float sampleHz,
            bool untilCutoff,
            Action<McpHostResult> onDone);
    }
}
