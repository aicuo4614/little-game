using System.Threading.Tasks;

namespace LittleGame.Client.Battle
{
    /// <summary>战斗子系统控制器基类（对齐 Demo ControlBase&lt;BattleView&gt; / BattleControlBase）。</summary>
    public abstract class BattleControlBase : ControlBase<BattleView>
    {
        public virtual Task PrepareAsync() => Task.CompletedTask;

        public virtual Task ReleaseResourcesAsync()
        {
            ReleaseResources();
            return Task.CompletedTask;
        }
    }
}
