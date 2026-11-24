using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleGame.Combat
{
    /// <summary>
    /// 横向无限推进远征常量与属性定义：
    /// 去掉固定蓝格，改为左侧整体白色布阵矩形区域 [DeployMinX..DeployMaxX] × [DeployMinZ..DeployMaxZ] 自由拖拽摆放；
    /// 每个职业与怪物具备独立移动速度（MoveSpeed）与转身速率（TurnSpeed）。
    /// </summary>
    public static class BattleDefinitions
    {
        public const int TotalWaves = 12, ReserveCapacity = 6;
        public const float Step = .05f, WaveTimeout = 45f;
        public const float LaneSpacingZ = 2.15f;
        public const float ColSpacingX = 1.65f;
        public const float FrontlineX = -1.15f;

        // 整体白色布阵框边界（世界坐标 X/Z）
        public const float DeployMinX = -5.20f;
        public const float DeployMaxX = -0.35f;
        public const float DeployMinZ = -2.65f;
        public const float DeployMaxZ = 2.65f;

        public const float EnemySpawnX = 5.60f;
        public const float BossSpawnX = 6.00f;
        public const float AttackAngleTolerance = 28f;
        public const float EnemyAttackAngleTolerance = 36f;

        public static string Name(TroopKind kind) => new[] { "盾卫", "风弓手", "雷法师", "冰法师" }[(int)kind];
        public static string Name(EnemyKind kind) => new[] { "步兵", "疾行狼", "重甲兵", "治疗师", "石甲巨像" }[(int)kind];
        public static string Name(SkillBranch branch) => new[] { "待选择", "堡垒", "战吼", "穿杨", "逐风", "雷池", "链雷", "深寒", "冰甲" }[(int)branch];
        public static string Name(RewardKind reward) => new[] { "援军令", "扩大雷区", "寒潮" }[(int)reward];
        public static bool IsBoss(int wave) => wave == 6 || wave == 12;

        public static bool ValidBranch(TroopKind kind, SkillBranch branch) =>
            (int)branch >= 1 + (int)kind * 2 && (int)branch <= 2 + (int)kind * 2;

        public static bool IsInsideDeployBox(float x, float z) =>
            x >= DeployMinX && x <= DeployMaxX && z >= DeployMinZ && z <= DeployMaxZ;

        /// <summary>默认参考站位坐标（用于初始编队及兼容 lane/row 指令）。</summary>
        public static float CellX(int row) => FrontlineX - row * ColSpacingX;
        public static float CellZ(int lane) => (1 - lane) * LaneSpacingZ;

        public static int NearestLane(float z) => Math.Max(0, Math.Min(2, (int)Math.Round(1f - z / LaneSpacingZ)));
        public static int NearestRow(float x) => Math.Max(0, Math.Min(2, (int)Math.Round((FrontlineX - x) / ColSpacingX)));

        public static float Health(Troop troop) => (troop.Kind == TroopKind.Shield ? 260f : 132f) * StarScale(troop.Star);
        public static float Attack(Troop troop) => (troop.Kind == TroopKind.Shield ? 20f : troop.Kind == TroopKind.Archer ? 28f : 26f) * StarScale(troop.Star);
        public static float StarScale(int star) => star == 1 ? 1f : star == 2 ? 2.15f : 4.7f;
        public static float Range(Troop troop) => troop.Kind == TroopKind.Shield ? 1.05f : 7.6f;

        /// <summary>我方职业移动速度（米/秒）：风弓手最灵动，盾卫重甲沉稳。</summary>
        public static float MoveSpeed(TroopKind kind) =>
            kind == TroopKind.Archer ? 3.05f : kind == TroopKind.Storm ? 2.60f : kind == TroopKind.Frost ? 2.50f : 2.20f;

        /// <summary>敌方移动速度（米/秒）：疾行狼高速突进，重甲兵与巨像沉重推进。</summary>
        public static float MoveSpeed(EnemyKind kind) =>
            kind == EnemyKind.Runner ? 2.05f : kind == EnemyKind.Giant ? 0.64f : kind == EnemyKind.Armored ? 0.88f : kind == EnemyKind.Healer ? 1.05f : 1.15f;

        /// <summary>我方职业转身速率（度/秒）。</summary>
        public static float TurnSpeed(TroopKind kind) =>
            kind == TroopKind.Archer ? 620f : kind == TroopKind.Shield ? 320f : 560f;

        /// <summary>敌方转身速率（度/秒）。</summary>
        public static float TurnSpeed(EnemyKind kind) =>
            kind == EnemyKind.Runner ? 520f : kind == EnemyKind.Giant ? 125f : kind == EnemyKind.Armored ? 260f : 380f;

        public static float BodyRadius(TroopKind kind) => kind == TroopKind.Shield ? 0.56f : 0.46f;
        public static float BodyRadius(EnemyKind kind) => kind == EnemyKind.Giant ? 1.55f : kind == EnemyKind.Armored ? 0.62f : 0.48f;

        public static List<WaveSpawn> Wave(int wave)
        {
            var result = new List<WaveSpawn>();
            if (IsBoss(wave))
            {
                result.Add(new WaveSpawn { Kind = EnemyKind.Giant, Lane = 1, At = 0f });
                return result;
            }
            for (int lane = 0; lane < 3; lane++)
            {
                int count = wave <= 2 ? 1 : 2 + (wave >= 7 ? 1 : 0) + (lane == wave % 3 ? 1 : 0);
                for (int i = 0; i < count; i++)
                {
                    var kind = wave >= 3 && lane == wave % 3 ? EnemyKind.Runner : EnemyKind.Grunt;
                    if (wave >= 5 && lane == 0 && i == 0) kind = EnemyKind.Armored;
                    if (wave >= 5 && lane == 1 && i == count - 1) kind = EnemyKind.Healer;
                    result.Add(new WaveSpawn { Kind = kind, Lane = lane, At = i * 1.55f });
                }
            }
            return result.OrderBy(e => e.At).ThenBy(e => e.Lane).ToList();
        }

        public static string RewardDescription(RewardKind reward) =>
            reward == RewardKind.Reinforcements ? "后续每波4转，上限4，不重复出现"
            : reward == RewardKind.WideStorm ? "雷区半径增加35%，链雷增加一个目标"
            : "普通敌人冻结延长0.5秒；首领减速不延长";

        public static bool CanReach(Troop troop, Enemy enemy)
        {
            if (!enemy.Alive || !troop.Alive) return false;
            float dx = troop.X - enemy.X;
            float dz = troop.Z - enemy.Z;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            float reach = Range(troop) + (enemy.Boss ? 1.35f : 0.15f);
            return dist <= reach;
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
