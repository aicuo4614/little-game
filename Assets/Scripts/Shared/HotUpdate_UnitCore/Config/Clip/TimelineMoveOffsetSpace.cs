/// <summary>单位位移的终点偏移空间；不改变特效/子弹的挂点偏移语义。</summary>
public enum TimelineMoveOffsetSpace
{
    /// <summary>X 沿起点到参考点的地面方向，Y 世界向上，Z 为叉乘(X,Y)；负 X 停在目标前。</summary>
    ApproachDirection = 0,
    /// <summary>偏移沿选定参考挂点的旋转和缩放；群体中心使用首个参考对象的挂点朝向。</summary>
    NodeLocal = 1,
    /// <summary>以参考点为原点，XYZ 偏移保持世界方向，单位为米。</summary>
    World = 2,
}
