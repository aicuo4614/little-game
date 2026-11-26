/// <summary>
/// UGUI 分层 Canvas 的统一排序层级。
/// 数值越大越靠上；同层内再靠子节点顺序（后挂 / SetChildIndex）决定。
/// </summary>
public enum UiSortingLayer
{
    /// <summary>未声明 / 兜底。</summary>
    Default = 0,

    /// <summary>常驻主界面（<c>MainUIWindow</c>），非全屏遮挡模块窗。</summary>
    Main = 100,

    /// <summary>场景 HUD（房间摇杆等）。</summary>
    SceneHud = 500,

    /// <summary>
    /// 房间场景定格底图（Module 窗打开时盖住 3D；低于 Module、高于 Main / SceneHud）。
    /// </summary>
    SceneFreeze = 900,

    /// <summary>
    /// 全屏遮挡的主 UI（背包、战斗主窗、宠物背包等 Module 窗）。
    /// 打开时会挂起 RoomScene，全部关闭后再恢复。
    /// </summary>
    Module = 1000,

    /// <summary>业务弹层（挂 UI 根 的二级面板）。</summary>
    Popup = 5000,

    /// <summary>全屏加载。</summary>
    Loading = 8000,

    /// <summary>通用确认 / 提示。</summary>
    Alert = 8500,

    /// <summary>调试浮层（GM、网络收发、战斗 Debug 若挂 UI 根）。</summary>
    Debug = 9000,

    /// <summary>系统级总开关等，压过调试。</summary>
    System = 10000,
}
