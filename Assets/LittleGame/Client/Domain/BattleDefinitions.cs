using System;

namespace LittleGame.Combat
{
    /// <summary>
    /// 布阵、距离与方向的共用规则。单位属性和波次内容由配置表提供。
    /// </summary>
    public static class BattleDefinitions
    {
        public const int ReserveCapacity = 6;
        public static int TotalWaves => BattleTables.Current.TotalWaves;
        public const float Step = .05f, WaveTimeout = 48f;
        public const float LaneSpacingZ = 2.15f;
        public const float ColSpacingX = 1.65f;
        public const float FrontlineX = -1.15f;

        // 整体白色布阵框边界（世界坐标 X/Z）
        public const float DeployMinX = -5.20f;
        public const float DeployMaxX = -0.35f;
        public const float DeployMinZ = -2.65f;
        public const float DeployMaxZ = 2.65f;

        public const float AttackAngleTolerance = 28f;
        public const float FieldMinX = -5.25f, FieldMaxX = 6.35f, FieldMinZ = -2.95f, FieldMaxZ = 2.95f;

        public static string Name(TroopKind kind) => new[] { "战士", "射手", "法师", "刺客" }[(int)kind];
        public static string Name(RewardKind reward) => new[] { "援军令", "扩大雷区", "寒潮" }[(int)reward];
        public static bool IsBoss(int wave) => BattleTables.Current.IsBoss(wave);

        public static bool IsInsideDeployBox(float x, float z) =>
            x >= DeployMinX && x <= DeployMaxX && z >= DeployMinZ && z <= DeployMaxZ;

        /// <summary>将任意世界坐标夹紧到白色布阵框内最近合法点 A。</summary>
        public static void ClampToDeployBox(float x, float z, out float clampedX, out float clampedZ)
        {
            clampedX = x < DeployMinX ? DeployMinX : x > DeployMaxX ? DeployMaxX : x;
            clampedZ = z < DeployMinZ ? DeployMinZ : z > DeployMaxZ ? DeployMaxZ : z;
        }

        /// <summary>默认参考站位坐标（用于初始编队、自动上阵及兼容 lane/row 指令）。</summary>
        public static float CellX(int row) => FrontlineX - row * ColSpacingX;
        public static float CellZ(int lane) => (1 - lane) * LaneSpacingZ;

        public static int NearestLane(float z) => Math.Max(0, Math.Min(2, (int)Math.Round(1f - z / LaneSpacingZ)));
        public static int NearestRow(float x) => Math.Max(0, Math.Min(2, (int)Math.Round((FrontlineX - x) / ColSpacingX)));

        public const float FixedTroopMoveSpeed = 2.40f;

        public static float StarScale(int star) => star == 1 ? 1f :
            BattleTables.Current.Tune(star == 2 ? BattleTuning.TwoStarScale : BattleTuning.ThreeStarScale);
        public static float Reach(BattleCombatant source, BattleCombatant target)
        {
            float minimum = source.Properties.Radius + target.Properties.Radius + .08f;
            return Math.Max(minimum, source.Properties.AttackRange + (target.Boss ? .85f : .10f));
        }

        public static string RewardDescription(RewardKind reward) =>
            reward == RewardKind.Reinforcements ? "后续每波4转，上限4，不重复出现"
            : reward == RewardKind.WideStorm ? "我方法术范围伤害的半径增加35%"
            : "普通敌人冻结延长0.5秒；首领减速不延长";

        public static bool CanReach(BattleCombatant source, BattleCombatant target) =>
            source.Alive && target.Alive && DistanceSquared(source.X, source.Z, target.X, target.Z) <= Math.Pow(Reach(source, target), 2);

        public static float DistanceSquared(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return dx * dx + dz * dz;
        }

        public static float VectorToYaw(float dx, float dz, float fallbackYaw)
        {
            if (dx * dx + dz * dz < 1e-6f) return fallbackYaw;
            return (float)(Math.Atan2(dx, dz) * (180.0 / Math.PI));
        }

        public static float DeltaAngle(float current, float target)
        {
            float delta = (target - current) % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta < -180f) delta += 360f;
            return delta;
        }

        public static float MoveTowardsAngle(float current, float target, float maxDelta)
        {
            float delta = DeltaAngle(current, target);
            if (Math.Abs(delta) <= maxDelta) return NormalizeAngle(target);
            return NormalizeAngle(current + Math.Sign(delta) * maxDelta);
        }

        public static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }
    }
}
