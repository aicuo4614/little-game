using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleGame.Combat
{
    /// <summary>
    /// 横向无限推进远征常量与统一数值配置表：
    /// 1. 左侧整体白色布阵矩形区域 [DeployMinX..DeployMaxX] × [DeployMinZ..DeployMaxZ] 自由拖拽摆放（超界自动夹紧到最近点 A）；
    /// 2. 统一管理我方职业与敌方波次血量、攻击、攻速、射程、移速、转身速率与体积半径，
    ///    远程射程收紧至战场中段（不再覆盖出生点），怪物血量与波次曲线平滑递进，保证前排接敌与远征过渡手感。
    /// </summary>
    public static class BattleDefinitions
    {
        public const int TotalWaves = 12, ReserveCapacity = 6;
        public const float Step = .05f, WaveTimeout = 48f;
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

        // ===== 我方统一数值配置 =====

        public static float StarScale(int star) => star == 1 ? 1.0f : star == 2 ? 1.95f : 3.65f;

        public static float Health(Troop troop)
        {
            float baseHp = troop.Kind == TroopKind.Shield ? 340f
                : troop.Kind == TroopKind.Archer ? 170f
                : troop.Kind == TroopKind.Storm ? 160f
                : 170f;
            return baseHp * StarScale(troop.Star);
        }

        public static float Attack(Troop troop)
        {
            float baseAtk = troop.Kind == TroopKind.Shield ? 17f
                : troop.Kind == TroopKind.Archer ? 23f
                : troop.Kind == TroopKind.Storm ? 21f
                : 19f;
            return baseAtk * StarScale(troop.Star);
        }

        public static float AttackInterval(TroopKind kind) =>
            kind == TroopKind.Archer ? 0.96f
            : kind == TroopKind.Storm ? 1.22f
            : kind == TroopKind.Frost ? 1.24f
            : 1.25f;

        /// <summary>
        /// 统一攻击射程：
        /// 盾卫近战 1.15m；雷/冰法中程 4.15m；风弓手远程 4.85m。
        /// 敌人从 X=5.6 推进到中场后才进入远程火力网，杜绝出生即秒杀。
        /// </summary>
        public static float Range(Troop troop) => Range(troop.Kind);

        public static float Range(TroopKind kind) =>
            kind == TroopKind.Shield ? 1.15f
            : kind == TroopKind.Archer ? 4.85f
            : 4.15f;

        /// <summary>我方角色统一固定移动速度（米/秒）。</summary>
        public const float FixedTroopMoveSpeed = 2.40f;

        /// <summary>我方职业移动速度（米/秒）：统一固定移速，不忽快忽慢。</summary>
        public static float MoveSpeed(TroopKind kind) => FixedTroopMoveSpeed;

        /// <summary>我方职业转身速率（度/秒）：足够利落，避免 A* 绕行折返时未转身先位移。</summary>
        public static float TurnSpeed(TroopKind kind) =>
            kind == TroopKind.Archer ? 1260f : kind == TroopKind.Shield ? 1080f : 1140f;

        public static float BodyRadius(TroopKind kind) => kind == TroopKind.Shield ? 0.56f : 0.46f;

        // ===== 敌方统一数值与波次成长曲线 =====

        public static float EnemyWaveHpScale(int wave) =>
            1.0f + (wave - 1) * 0.15f + (wave >= 7 ? (wave - 6) * 0.08f : 0f);

        public static float EnemyWaveAtkScale(int wave) =>
            1.0f + (wave - 1) * 0.075f;

        public static float EnemyHealth(EnemyKind kind, int wave)
        {
            if (kind == EnemyKind.Giant) return wave <= 6 ? 1350f : 2850f;
            float baseHp = kind == EnemyKind.Armored ? 235f
                : kind == EnemyKind.Runner ? 105f
                : kind == EnemyKind.Healer ? 130f
                : 135f;
            return (float)Math.Round(baseHp * EnemyWaveHpScale(wave));
        }

        public static float EnemyAttack(EnemyKind kind, int wave)
        {
            if (kind == EnemyKind.Giant) return wave <= 6 ? 26f : 40f;
            float baseAtk = kind == EnemyKind.Armored ? 19f
                : kind == EnemyKind.Runner ? 15f
                : kind == EnemyKind.Healer ? 11f
                : 13f;
            return (float)Math.Round(baseAtk * EnemyWaveAtkScale(wave));
        }

        public static float EnemyHealAmount(int wave) => (float)Math.Round(28f * (1f + (wave - 1) * 0.08f));

        public static float BossSlamDamage(int wave) => wave <= 6 ? 48f : 76f;

        /// <summary>敌方移动速度（米/秒）：疾行狼高速突进，重甲兵与巨像沉重推进。</summary>
        public static float MoveSpeed(EnemyKind kind) =>
            kind == EnemyKind.Runner ? 2.10f : kind == EnemyKind.Giant ? 0.72f : kind == EnemyKind.Armored ? 0.92f : kind == EnemyKind.Healer ? 1.08f : 1.25f;

        /// <summary>敌方转身速率（度/秒）：保留巨像体量感的同时避免绕行侧滑。</summary>
        public static float TurnSpeed(EnemyKind kind) =>
            kind == EnemyKind.Runner ? 1200f : kind == EnemyKind.Giant ? 600f : kind == EnemyKind.Armored ? 960f : 1080f;

        public static float BodyRadius(EnemyKind kind) => kind == EnemyKind.Giant ? 1.45f : kind == EnemyKind.Armored ? 0.62f : 0.48f;

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
                    result.Add(new WaveSpawn { Kind = kind, Lane = lane, At = i * 1.65f });
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
            float reach = Range(troop) + (enemy.Boss ? 1.25f : 0.15f);
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
