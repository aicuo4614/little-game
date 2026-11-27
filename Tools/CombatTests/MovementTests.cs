using LittleGame.Combat;
using System.Text.Json;

static class MovementTests
{
    public static void Run()
    {
        void State(CombatAiState before, bool alive, bool controlled, bool target, float distance, CombatAiState expected)
        {
            var actual = BattleAgentBrain.Transition(before, alive, controlled, target, distance, 1.3f, .06f);
            if (actual != expected) throw new Exception($"状态转移错误 {before} -> {actual}，预期 {expected}");
        }
        State(CombatAiState.Idle, true, false, false, 0, CombatAiState.Idle);
        State(CombatAiState.Idle, true, false, true, 3, CombatAiState.Pursuing);
        State(CombatAiState.Pursuing, true, false, true, 1.23f, CombatAiState.Engaging);
        State(CombatAiState.Pursuing, true, false, true, 1.27f, CombatAiState.Pursuing);
        State(CombatAiState.Engaging, true, false, true, 1.27f, CombatAiState.Engaging);
        State(CombatAiState.Engaging, true, false, true, 1.31f, CombatAiState.Pursuing);
        State(CombatAiState.Pursuing, true, true, true, 3, CombatAiState.Controlled);
        State(CombatAiState.Controlled, true, false, true, 3, CombatAiState.Pursuing);
        State(CombatAiState.Engaging, true, false, false, 0, CombatAiState.Idle);
        State(CombatAiState.Engaging, false, false, true, 1, CombatAiState.Dead);
        State(CombatAiState.Dead, true, false, false, 0, CombatAiState.Idle);

        var s = NavigationTests.AttachStub(new BattleSession(173));
        s.Execute(new BattleCommand { Kind = CommandKind.Spin });
        var troop = s.Troops[0];
        s.Execute(new BattleCommand { Kind = CommandKind.Deploy, UnitId = troop.Id, Lane = 1, Row = 0 });
        s.Execute(new BattleCommand { Kind = CommandKind.StartWave });
        while (s.Enemies.Count < 2 && s.Phase == RunPhase.Fighting) s.Advance(.05f);
        if (s.Enemies.Count < 2) throw new Exception("缺少测试所需敌人");
        var first = s.Enemies[0]; var second = s.Enemies[1];
        troop.X = 0; troop.Z = 0; troop.TargetId = first.Id;
        troop.Cooldown = 100; troop.Hp = 10000;
        first.X = 1.22f; first.Z = 0; first.Hp = 10000; first.Cooldown = 100;
        second.X = 1.18f; second.Z = 0; second.Hp = 10000; second.Cooldown = 100;
        s.Advance(.05f);
        if (troop.TargetId != first.Id) throw new Exception("新怪略近时不应抢换仍可攻击的目标");
        first.Hp = 0; second.X = troop.X + 1.2f; second.Z = troop.Z;
        s.Advance(.05f);
        if (troop.TargetId != second.Id) throw new Exception("目标死亡后必须重新寻敌");
        second.Status.Apply(UnitStatus.Frozen, 0, s.BattleTime + 1);
        var frozen = BattleAgentBrain.PlanEnemy(second, s.Troops, s.BattleTime, first.MoveSpeed, .05f);
        if (frozen.State != CombatAiState.Controlled || !frozen.HoldPosition || frozen.CanAttack)
            throw new Exception("冻结状态不得追击或攻击");
        // 验证常规单位 90° 转向在 0.1s（2 个逻辑步）内即可对准，避免绕行时未转身先位移
        float turnYaw = 90f;
        for (int i = 0; i < 2; i++)
            turnYaw = BattleDefinitions.MoveTowardsAngle(turnYaw, 0f, troop.Properties.TurnSpeed * 1.4f * BattleDefinitions.Step);
        if (Math.Abs(BattleDefinitions.DeltaAngle(turnYaw, 0f)) > 0.01f)
            throw new Exception($"盾卫移动 90° 转向过慢：剩余偏差 {BattleDefinitions.DeltaAngle(turnYaw, 0f):F1}°");
        Console.WriteLine("通过：FSM状态边界/新怪出现目标稳定/目标死亡重选/快速转身收敛（原生NavMesh需在Unity验收）");
        Console.WriteLine("通过：11项FSM状态边界/冻结输出零移动零攻击意图");
    }
}
