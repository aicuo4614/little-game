using System;
using System.Collections.Generic;
using Rotation = System.Numerics.Quaternion;
using Direction = System.Numerics.Vector3;

namespace LittleGame
{
    // 玩家和 MCP 共用正常操作，视角也参与可见性校验。
    public sealed class PuzzleSession
    {
        public int Revision { get; private set; }
        public int Moves { get; private set; }
        public int FailureSequence { get; private set; }
        public string FailedPart { get; private set; } = "";
        public string LastAction { get; private set; } = "";
        public string LastTarget { get; private set; } = "";
        public const float CameraPitch = 59;
        public const float OrbitSensitivity = 1.8f;
        public Rotation Orientation { get; private set; } = HomeOrientation;
        private static readonly float CameraRadians = CameraPitch * (float)Math.PI / 180;
        private static readonly Direction CameraUp = new Direction(0, (float)Math.Cos(CameraRadians), (float)Math.Sin(CameraRadians));
        private static readonly Direction CameraForward = new Direction(0, -(float)Math.Sin(CameraRadians), (float)Math.Cos(CameraRadians));
        private static readonly Rotation HomeOrientation = Rotation.Normalize(
            Rotation.CreateFromAxisAngle(Direction.UnitX, .14f)
            * Rotation.CreateFromAxisAngle(CameraUp, -.2f)
            * Rotation.CreateFromAxisAngle(Direction.UnitZ, (float)Math.PI));
        private Rotation FrontOrientation => LevelIndex==1?HomeOrientation:Rotation.Normalize(Rotation.CreateFromAxisAngle(CameraUp,-.6f)*HomeOrientation);
        public float BackFacing => Direction.Dot(Direction.Transform(Direction.UnitY, Orientation), -CameraForward);
        public int LevelIndex { get; private set; } = 1;
        public DeviceLevel Level => DeviceLevels.All[LevelIndex-1];
        private readonly HashSet<string> collected=new HashSet<string>();
        public bool Won => collected.Count==Level.Parts.Length && !IsAnimating;
        public int RecoveredCount => collected.Count-(IsAnimating?1:0);
        public bool Recovered(string part) => Collected(part) && !(IsAnimating && AnimationPart==part);
        public bool Cover => !Collected("cover");
        public bool Battery => Level.Find("battery")!=null && !Collected("battery");
        public bool Cable => !Collected("cable");
        public bool ScrewA => !Collected("screw_a");
        public bool ScrewB => !Collected("screw_b");
        public bool BoardScrew => !Collected("board_screw");
        public string AnimationPart { get; private set; } = "";
        private float animationRemaining;
        private float animationDuration;
        public bool IsAnimating => animationRemaining > 0;
        public float AnimationProgress => animationDuration > 0 ? 1 - animationRemaining / animationDuration : 1;

        public void Advance(float seconds)
        {
            if (!IsAnimating || !Finite(seconds) || seconds < 0) return;
            animationRemaining = Math.Max(0, animationRemaining - seconds);
            if (!IsAnimating) { Revision++;if(Won)Message="整机拆解完成 · 全部零件已回收"; }
        }
        public bool Accepted { get; private set; }
        public string Selected { get; private set; } = "";
        public string Message { get; private set; } = "这是一台旧掌机，翻到背面开始拆卸";
        public string Blocker { get; private set; } = "";
        public bool BackVisible => BackFacing > .1f;
        public string Stage => IsAnimating ? "animating" : Won ? "complete" : Collected("board") ? "recycle_chassis" : Cover ? "open_case" : LevelIndex==2 ? "recycle_modules" : Battery ? "disconnect_battery" : "extract_board";

        public bool Accessible(string part)
        {
            var definition=Level.Find(part);
            return BackVisible && !Won && !IsAnimating && definition!=null && !Collected(part)
                && (!definition.Interior || !Cover);
        }
        public void LoadLevel(int level)
        {
            Revision++;LastAction="load_level";LastTarget="";Accepted=false;
            if(level<1 || level>DeviceLevels.All.Length) { Reject("关卡不存在","");return; }
            if(IsAnimating) { Reject("请等待回收动画完成",AnimationPart);return; }
            LevelIndex=level;ResetRound();
        }
        private void ResetRound()
        {
            Orientation=FrontOrientation;Moves=0;collected.Clear();Selected=FailedPart=Blocker=AnimationPart="";
            FailureSequence=0;animationRemaining=animationDuration=0;
            Accept("翻到背面，从外壳螺丝开始",false);
        }

        // 增量绕屏幕坐标轴累积为单位四元数，允许连续越过两极并绕任意方向翻转。
        public void Rotate(float delta, float tilt, float roll = 0)
        {
            Revision++;
            Accepted = false;
            Blocker = "";
            if(IsAnimating) { Message="正在回收，请稍候旋转";return; }
            if (!Finite(delta) || !Finite(tilt) || !Finite(roll))
            {
                Message = "视角参数必须为有限数值";
                return;
            }
            var horizontal = Rotation.CreateFromAxisAngle(CameraUp, Radians(delta));
            var vertical = Rotation.CreateFromAxisAngle(Direction.UnitX, Radians(tilt));
            var twist = Rotation.CreateFromAxisAngle(CameraForward, Radians(roll));
            Orientation = Rotation.Normalize(twist * vertical * horizontal * Orientation);
            Selected = "";
            Accepted = true;
            Message = BackVisible ? "双击部件拆卸，拖动机器可旋转" : "拖动掌机，寻找背面的螺丝";
        }

        public void Orbit(float fromX, float fromY, float toX, float toY)
        {
            Revision++;
            Accepted = false;
            Blocker = "";
            if(IsAnimating) { Message="正在回收，请稍候旋转";return; }
            if (!Finite(fromX) || !Finite(fromY) || !Finite(toX) || !Finite(toY))
            {
                Message = "视角参数必须为有限数值";
                return;
            }
            var from = Trackball(fromX, fromY);
            var to = Trackball(toX, toY);
            float dot = Math.Max(-1, Math.Min(1, Direction.Dot(from, to)));
            Rotation delta;
            if (dot < -.99999f)
            {
                var axis = Direction.Cross(from, Direction.UnitX);
                if (axis.LengthSquared() < .001f) axis = Direction.Cross(from, Direction.UnitY);
                delta = Rotation.CreateFromAxisAngle(Direction.Normalize(axis), (float)Math.PI);
            }
            else
            {
                var axis = Direction.Cross(from, to);
                float length = axis.Length();
                delta = length < .000001f ? Rotation.Identity
                    : Rotation.CreateFromAxisAngle(axis / length, (float)Math.Atan2(length, dot) * OrbitSensitivity);
            }
            Orientation = Rotation.Normalize(delta * Orientation);
            Selected = "";
            Accept(BackVisible ? "双击零件拆卸，单击选中查看" : "转到掌机背面，寻找外壳螺丝", false);
        }

        private static Direction Trackball(float x, float y)
        {
            float length = x * x + y * y;
            float z = length <= 1 ? (float)Math.Sqrt(1 - length) : 0;
            var projected = Direction.Normalize(new Direction(x, y, z));
            return Direction.UnitX * projected.X + CameraUp * projected.Y - CameraForward * projected.Z;
        }

        public void Flip()
        {
            if(IsAnimating) { Revision++;Accepted=false;Message="正在回收，请稍候翻面";return; }
            Orientation = BackVisible
                ? FrontOrientation
                : Rotation.Identity;
            Selected = "";
            Blocker = "";
            Revision++;
            Accept(BackVisible ? "已翻到背面" : "已翻到正面", false);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Radians(float degrees) => degrees % 360 * (float)Math.PI / 180;

        public void Execute(string action, string part = "")
        {
            Revision++;
            Accepted = false;
            Blocker = "";
            LastAction=action;LastTarget=part ?? "";
            if (action == "restart")
            {
                ResetRound();
                return;
            }
            if(action=="next_level")
            {
                if(!Won || IsAnimating || LevelIndex>=DeviceLevels.All.Length) { Reject("本关回收完成后进入下一关","");return; }
                LoadLevel(LevelIndex+1);return;
            }
            if (action == "clear_selection") { Selected = ""; Accept(Message, false); return; }
            if (IsAnimating) { Reject("正在拆卸，请等待动作完成", AnimationPart); return; }
            if (!Accessible(part)) { Reject("这个零件当前不可操作，先转到背面并拆开遮挡", Cover ? "cover" : part); return; }
            if (action == "select") { Selected = part; Accept("已选中" + Name(part), false); return; }
            if (action != "disassemble") { Reject("未知操作", ""); return; }
            string blocker=RemovalBlocker(part);
            if(blocker!="") { Reject("先拆下"+Name(blocker)+"，再回收"+Name(part),blocker);return; }
            collected.Add(part);Remove(part,Name(part)+"已拆下，正在回收");
        }

        public string RemovalBlocker(string part)
        {
            var definition=Level.Find(part);if(definition==null) return "";
            if(definition.Interior && Cover) return "cover";
            foreach(string dependency in definition.Requires) if(!Collected(dependency)) return dependency;
            return "";
        }

        private void Remove(string part, string message)
        {
            AnimationPart = part;
            animationDuration = part.Contains("screw") ? 1.25f : 1.45f;
            animationRemaining = animationDuration;
            Accept(message);
        }

        private void Accept(string message, bool move = true)
        {
            Accepted = true; Message = message;
            if (move) { Moves++; Selected = ""; }
        }
        private void Reject(string message, string part)
        {
            Message = message; Blocker = part;
            if(LastAction=="disassemble") { FailureSequence++;FailedPart=LastTarget; }
        }
        public bool Collected(string part) { return part!=null && collected.Contains(part); }
        public string Name(string part) { return Level.Find(part)?.Name ?? ""; }

        public object Snapshot()
        {
            var available = new List<object>();
            foreach (var definition in Level.Parts)
            {
                string part=definition.Id;
                if (Accessible(part))
                {
                    available.Add(new { action = "select", part });
                    available.Add(new { action = "disassemble", part, ready=RemovalBlocker(part)=="", blocker=RemovalBlocker(part) });
                }
            }
            var parts=new List<object>();
            foreach(var p in Level.Parts) parts.Add(new {id=p.Id,name=p.Name,detached=Collected(p.Id),collected=Recovered(p.Id),accessible=Accessible(p.Id),blocker=RemovalBlocker(p.Id)});
            return new { level=LevelIndex,levelCount=DeviceLevels.All.Length,device=Level.Title,totalParts=Level.Parts.Length,recoveredCount=RecoveredCount,parts,revision = Revision, moves = Moves, phase = IsAnimating ? "animating" : Won ? "won" : "playing",
                stage = Stage, animating = IsAnimating, animationPart = AnimationPart, animationProgress = AnimationProgress,
                orientation = new { x = Orientation.X, y = Orientation.Y, z = Orientation.Z, w = Orientation.W },
                backFacing = BackFacing, backVisible = BackVisible,
                selected = Selected, screwAAttached = ScrewA, screwBAttached = ScrewB,
                coverAttached = Cover, cableAttached = Cable, batteryAttached = Battery,
                boardScrewAttached = BoardScrew, accepted = Accepted, message = Message,
                blocker = Blocker, failureSequence=FailureSequence,failedPart=FailedPart,lastAction=LastAction,lastTarget=LastTarget, availableActions = available };
        }
    }
}
