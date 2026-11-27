using Unity.Profiling;
using System;
using LittleGame.Combat;
using PureMVC.Patterns.Facade;
using PureMVC.Interfaces;

namespace LittleGame.Client
{
    public interface IRunStore { RunSave Load(); void Save(RunSave data); }
    public interface ICommerceService { bool Available { get; } void Purchase(string productId); }
    public interface IBattleView { void Refresh(); }

    /// <summary>单场应用宿主；所有正常操作在此路由到同一套规则。</summary>
    public sealed class BattleApplication : IDisposable
    {
        private static readonly ProfilerMarker PerformanceMarker = new ProfilerMarker("LittleGame.Rules");
        public const string Changed = "combat.changed";
        private readonly IRunStore store;
        public LittleGame.Client.Battle.BattleNavMeshNavigation Navigation { get; private set; }
        private readonly IFacade facade;
        public BattleSession Session { get; private set; }
        public CommandResult LastResult { get; private set; } = new CommandResult(true, "先看三路敌情，再转盘招募");
        public bool SaveAvailable { get; private set; } = true;
        public event Action<string> PersistenceError;
        public static int FreshSeed() => unchecked((int)DateTime.UtcNow.Ticks ^ Environment.TickCount);
        public BattleApplication(IRunStore store)
        {
            this.store = store;
            BattleTables.Configure(name =>
            {
                var source = UnityEngine.Resources.Load<UnityEngine.TextAsset>("BattleTables/" + name);
                return source != null ? source.text : throw new System.IO.FileNotFoundException("找不到战斗配表：" + name);
            });
            facade = MVC.Facade ?? throw new InvalidOperationException("MVC 必须先完成初始化");
            try { var save = store?.Load(); Session = save == null ? new BattleSession(FreshSeed()) : BattleSession.Restore(save); }
            catch (Exception e) { Session = new BattleSession(); SaveAvailable = false; LastResult = new CommandResult(true, "旧档无法恢复，已开新局；原档保留：" + e.Message); }
            BindNavigation();
        }
        private void BindNavigation() { Navigation = new LittleGame.Client.Battle.BattleNavMeshNavigation(); Session.AttachNavigation(Navigation); }
        public void AttachView(IBattleView view) => facade.RegisterMediator(new BattleMediator(view));
        public CommandResult Submit(BattleCommand command)
        {
            // 开战前先记录检查点；退出重打本波，不重新生成招募结果。
            if (command.Kind == CommandKind.StartWave && Session.Phase == RunPhase.Preparation) SaveCheckpoint();
            LastResult = Session.Execute(command);
            if (LastResult.Accepted && Session.Phase != RunPhase.Fighting) SaveCheckpoint();
            facade.SendNotification(Changed, LastResult); return LastResult;
        }
        public void Update(float dt)
        {
            using var performanceSample = PerformanceMarker.Auto();
            var phase = Session.Phase;
            Navigation.PrepareFrame(phase == RunPhase.Fighting, Session.Paused, Session.Speed);
            if (phase == RunPhase.Fighting) Session.Advance(dt);
            else Session.StepRegroup(dt);
            if (Session.Phase != phase) { SaveCheckpoint(); facade.SendNotification(Changed); }
        }
        public void SaveCheckpoint()
        {
            if (store == null || Session.Phase == RunPhase.Fighting || !SaveAvailable) return;
            try { store.Save(Session.Save()); }
            catch (Exception e) { SaveAvailable = false; PersistenceError?.Invoke("保存失败，原档保留：" + e.Message); }
        }
        public void Dispose() { Navigation?.Dispose(); Navigation = null; SaveCheckpoint(); facade.RemoveMediator(BattleMediator.MediatorKey); }
    }
}
