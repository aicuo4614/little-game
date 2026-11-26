using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>记录 MCP 提交，检测 GUI 插入的修改并保护它们免受撤销覆盖。</summary>
internal sealed class TimelineEditorMcpHistory
{
    private EditorTimeLine owner;
    private string expectedRevision;
    private List<string> undo = new List<string>();
    private List<string> redo = new List<string>();

    internal void Record(EditorTimeLine window, TimelineConfig before, string previousRevision, string revision)
    {
        if (owner != window || previousRevision != expectedRevision)
        {
            Clear();
        }
        owner = window;
        expectedRevision = revision;
        Push(undo, before);
        redo.Clear();
    }

    internal TimelineConfig Peek(EditorTimeLine window, string revision, bool forward)
    {
        var source = forward ? redo : undo;
        if (window != owner || revision != expectedRevision || source.Count == 0)
        {
            throw new InvalidOperationException("没有可用的 MCP 历史，或编辑缓冲区已被其他操作修改。");
        }
        return TimelineJsonUtil.FromJson(source[source.Count - 1]);
    }

    internal void Advance(TimelineConfig before, string revision, bool forward)
    {
        var source = forward ? redo : undo;
        source.RemoveAt(source.Count - 1);
        Push(forward ? undo : redo, before);
        expectedRevision = revision;
    }

    internal JObject Capture(EditorTimeLine window, string revision)
    {
        return owner == window && expectedRevision == revision
            ? new JObject { ["undo"] = new JArray(undo), ["redo"] = new JArray(redo) }
            : null;
    }

    internal void Restore(EditorTimeLine window, string revision, JObject state)
    {
        if (state is null)
        {
            return;
        }
        Clear();
        owner = window;
        expectedRevision = revision;
        undo.AddRange(state["undo"].Values<string>());
        redo.AddRange(state["redo"].Values<string>());
    }

    internal void Clear()
    {
        owner = null;
        expectedRevision = null;
        undo.Clear();
        redo.Clear();
    }

    private static void Push(List<string> stack, TimelineConfig state)
    {
        if (stack.Count >= 32)
        {
            stack.RemoveAt(0);
        }
        stack.Add(TimelineJsonUtil.ToJson(state, false));
    }
}
