/// <summary>从 ClientExpress 迁移的窗口层级通知契约。</summary>
public static class MainNoti
{
    /// <summary>模块窗口打开完成，body 为 UiSortingLayer，type 为窗口类型名。</summary>
    public const string ON_OPEN_WINDOW = "MainNoti.OnOpenWindow";
    /// <summary>模块窗口关闭或隐藏，body 为 UiSortingLayer。</summary>
    public const string ON_CLOSE_WINDOW = "MainNoti.OnCloseWindow";
}
