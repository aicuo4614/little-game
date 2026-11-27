using System;
using System.Collections.Generic;
using System.Linq;

namespace LittleGame.Combat
{
    public sealed partial class BattleSession
    {
        private bool StepJump(BattleCombatant self, IReadOnlyList<BattleCombatant> opponents, List<BattleCombatant> active)
        {
            if (self.JumpEndsAt > 0f)
            {
                float fraction = Clamp((BattleTime - self.JumpStartedAt) / (self.JumpEndsAt - self.JumpStartedAt), 0f, 1f);
                self.X = self.JumpStartX + (self.JumpX - self.JumpStartX) * fraction;
                self.Z = self.JumpStartZ + (self.JumpZ - self.JumpStartZ) * fraction;
                if (fraction >= 1f)
                {
                    self.JumpEndsAt = 0f;
                    if (LandingClear(self, active, self.X, self.Z))
                    {
                        self.Jumped = true;
                        self.Navigation.Reset();
                        Buffs.OnLanding(self, BattleTime);
                        Emit("assassin_landed", "刺客落地", self.Id, self.TargetId);
                    }
                    else
                    {
                        self.X = self.JumpStartX;
                        self.Z = self.JumpStartZ;
                        Emit("assassin_jump_blocked", "落点被占，返回起跳点", self.Id);
                    }
                }
                return true;
            }

            var table = BattleTables.Current;
            if (self.Definition.Job != UnitJob.Assassin || self.Jumped || BattleTime < table.Tune(BattleTuning.AssassinEarliestJump) ||
                !self.Status.CanJump(BattleTime) || SkillBusy(self.Id))
                return false;

            var targets = opponents.Where(u => u.Alive && u.Deployed).Select(u => (Unit: u, Distance: Distance(self.X, self.Z, u.X, u.Z))).ToArray();
            float range = table.Tune(BattleTuning.AssassinJumpRange);
            if (!targets.Any(t => t.Distance <= table.Tune(BattleTuning.AssassinDetection))) return false;
            var target = targets.Where(t => t.Distance <= range)
                .OrderByDescending(t => t.Distance).ThenBy(t => t.Unit.Id).Select(t => t.Unit).FirstOrDefault();
            if (target == null || !FindLanding(self, target, active, range, out float x, out float z)) return false;

            self.JumpStartedAt = BattleTime;
            self.JumpEndsAt = BattleTime + table.Tune(BattleTuning.AssassinJumpDuration);
            self.JumpStartX = self.X;
            self.JumpStartZ = self.Z;
            self.JumpX = x;
            self.JumpZ = z;
            self.Vx = self.Vz = 0f;
            self.TargetId = target.Id;
            self.FacingYaw = BattleDefinitions.VectorToYaw(x - self.X, z - self.Z, self.FacingYaw);
            Emit("assassin_jump", "刺客起跳", self.Id, target.Id);
            return true;
        }

        private static bool FindLanding(BattleCombatant self, BattleCombatant target, List<BattleCombatant> active, float range, out float x, out float z)
        {
            float angle = (float)Math.Atan2(target.Z - self.Z, target.X - self.X);
            float radius = target.Properties.Radius + self.Properties.Radius + .12f;
            for (int i = 0; i < 12; i++)
            {
                int offset = (i + 1) / 2 * (i % 2 == 0 ? -1 : 1);
                float direction = angle + offset * (float)Math.PI / 6f;
                x = target.X + (float)Math.Cos(direction) * radius;
                z = target.Z + (float)Math.Sin(direction) * radius;
                if (x >= BattleDefinitions.FieldMinX && x <= BattleDefinitions.FieldMaxX &&
                    z >= BattleDefinitions.FieldMinZ && z <= BattleDefinitions.FieldMaxZ &&
                    Distance(self.X, self.Z, x, z) <= range &&
                    LandingClear(self, active, x, z))
                    return true;
            }
            x = z = 0f;
            return false;
        }

        private static bool LandingClear(BattleCombatant self, List<BattleCombatant> active, float x, float z)
        {
            foreach (var other in active)
            {
                if (other == self || !other.Alive) continue;
                float required = other.Properties.Radius + self.Properties.Radius + .04f;
                if (Distance(other.X, other.Z, x, z) < required) return false;
                if (other.JumpEndsAt > 0f && Distance(other.JumpX, other.JumpZ, x, z) < required) return false;
            }
            return true;
        }
    }
}
