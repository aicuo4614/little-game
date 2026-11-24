using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleGame.Combat
{
    public static class BattleDefinitions
    {
        public const int TotalWaves = 12, MaxGateHp = 10, ReserveCapacity = 6;
        public const float Step = .05f, WaveTimeout = 45, LaneSpacing = 2.5f, GateZ = -4.2f;
        public static string Name(TroopKind kind) => new[] { "盾卫", "风弓手", "雷法师", "冰法师" }[(int)kind];
        public static string Name(EnemyKind kind) => new[] { "步兵", "疾行狼", "重甲兵", "治疗师", "石甲巨像" }[(int)kind];
        public static string Name(SkillBranch branch) => new[] { "待选择", "堡垒", "战吼", "穿杨", "逐风", "雷池", "链雷", "深寒", "冰甲" }[(int)branch];
        public static string Name(RewardKind reward) => new[] { "援军令", "扩大雷区", "寒潮" }[(int)reward];
        public static bool IsBoss(int wave) => wave == 6 || wave == 12;
        public static bool ValidBranch(TroopKind kind, SkillBranch branch) =>
            (int)branch >= 1 + (int)kind * 2 && (int)branch <= 2 + (int)kind * 2;
        public static float LaneX(int lane) => (lane - 1) * LaneSpacing;
        public static float RowZ(int row) => -1.25f - row * 1.05f;
        public static float Health(Troop troop) => (troop.Kind == TroopKind.Shield ? 245 : 125) * StarScale(troop.Star);
        public static float Attack(Troop troop) => (troop.Kind == TroopKind.Shield ? 19 : troop.Kind == TroopKind.Archer ? 27 : 25) * StarScale(troop.Star);
        public static float StarScale(int star) => star == 1 ? 1 : star == 2 ? 2.15f : 4.7f;
        public static float Range(Troop troop) => troop.Kind == TroopKind.Shield ? .85f : 5.8f;
        public static List<WaveSpawn> Wave(int wave)
        {
            var result = new List<WaveSpawn>();
            if (IsBoss(wave)) { result.Add(new WaveSpawn { Kind = EnemyKind.Giant, Lane = 1, At = 0 }); return result; }
            for (int lane = 0; lane < 3; lane++)
            {
                int count = wave <= 2 ? 1 : 2 + (wave >= 7 ? 1 : 0) + (lane == wave % 3 ? 1 : 0);
                for (int i = 0; i < count; i++)
                {
                    var kind = wave >= 3 && lane == wave % 3 ? EnemyKind.Runner : EnemyKind.Grunt;
                    if (wave >= 5 && lane == 0 && i == 0) kind = EnemyKind.Armored;
                    if (wave >= 5 && lane == 1 && i == count - 1) kind = EnemyKind.Healer;
                    result.Add(new WaveSpawn { Kind = kind, Lane = lane, At = i * 1.7f });
                }
            }
            return result.OrderBy(e => e.At).ThenBy(e => e.Lane).ToList();
        }
        public static string RewardDescription(RewardKind reward) => reward == RewardKind.Reinforcements ? "后续每波4转，上限4，不重复出现" : reward == RewardKind.WideStorm ? "雷区半径增加35%，链雷增加一个目标" : "普通敌人冻结延长0.5秒；首领减速不延长";
        public static bool CanReach(Troop troop, Enemy enemy, bool bossWave)
        {
            if (!enemy.Alive || !troop.Alive) return false;
            // 栏撤除后允许近战跨路；远程仍按实际空间距离。
            if (!bossWave && Math.Abs(troop.Lane - enemy.Lane) > (troop.Kind == TroopKind.Shield ? 0 : 1)) return false;
            float dx = troop.X - enemy.X, dz = troop.Z - enemy.Z;
            float bossExtent = enemy.Boss ? 2.5f : 0;
            dx = Math.Max(0, Math.Abs(dx) - bossExtent);
            return dx * dx + dz * dz <= Range(troop) * Range(troop);
        }
    }
}
