/// <summary>参考对象为多个单位时的空间取点方式。</summary>
public enum TimelineMoveReferenceType
{
    /// <summary>位移开始时距离各移动单位最近的对象，数值沿用旧的第一个选项。</summary>
    Nearest = 0,
    /// <summary>全部参考对象的中心点。</summary>
    Center = 1,
    /// <summary>片段开始时距离作用单位最远的对象。</summary>
    Farthest = 2,
}
