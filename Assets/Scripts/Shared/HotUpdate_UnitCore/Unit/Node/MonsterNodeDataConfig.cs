using System;
using System.Collections.Generic;

/// <summary>
/// 单位节点绑定 JSON 根对象（仅 NodeType + 相对路径）。
/// 落盘：与预制体同目录的 <c>{预制体名}_Node_Data.json</c>；Yoo 地址 <c>{prefabAddress}_Node_Data</c>。
/// </summary>
[Serializable]
public class MonsterNodeDataConfig
{
    /// <summary>节点绑定列表。</summary>
    public List<MonsterNodePathEntry> Nodes = new List<MonsterNodePathEntry>();
}
