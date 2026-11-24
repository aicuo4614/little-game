using System;
using System.Collections.Generic;

namespace LittleGame.Client.SkillTimeline
{
    public enum SkillPlaybackState
    {
        Stopped,
        Playing,
        Paused,
        Completed
    }

    /// <summary>按绝对时间驱动技能片段。预览与实战共用时间边界与采样调度。</summary>
    public sealed class SkillTimelinePlayer
    {
        private readonly SkillTimelineSchedule schedule;
        private readonly bool[] activeClips;
        private readonly bool[] firedInstant;
        private readonly Action<SkillScheduledClip, float> onSample;
        private readonly Action<SkillScheduledClip> onExit;
        private readonly Action<SkillScheduledClip> onHit;
        private readonly Action onReset;
        private float lastSampledTime = -0.0001f;

        public float Time { get; private set; }
        public float Duration => schedule.TotalDuration;
        public SkillPlaybackState State { get; private set; } = SkillPlaybackState.Stopped;
        public IReadOnlyList<SkillScheduledClip> ScheduledClips => schedule.OrderedClips;

        public SkillTimelinePlayer(
            SkillTimelineConfig config,
            Action<SkillScheduledClip, float> onSample,
            Action<SkillScheduledClip> onExit,
            Action<SkillScheduledClip> onHit = null,
            Action onReset = null)
        {
            schedule = new SkillTimelineSchedule(config);
            this.onSample = onSample ?? throw new ArgumentNullException(nameof(onSample));
            this.onExit = onExit ?? throw new ArgumentNullException(nameof(onExit));
            this.onHit = onHit;
            this.onReset = onReset;
            activeClips = new bool[schedule.OrderedClips.Count];
            firedInstant = new bool[schedule.OrderedClips.Count];
        }

        public void Play()
        {
            if (State == SkillPlaybackState.Playing) return;
            if (State == SkillPlaybackState.Paused)
            {
                State = SkillPlaybackState.Playing;
                return;
            }

            ClearActive();
            onReset?.Invoke();
            Time = 0f;
            lastSampledTime = -0.0001f;
            State = SkillPlaybackState.Playing;
            Sample(0f);
            DispatchHits(0f, 0f, true);
            if (Duration <= 0f)
            {
                ClearActive();
                State = SkillPlaybackState.Completed;
            }
        }

        public void Pause()
        {
            if (State == SkillPlaybackState.Playing) State = SkillPlaybackState.Paused;
        }

        public void Tick(float deltaTime)
        {
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (State != SkillPlaybackState.Playing || deltaTime == 0f) return;

            float previous = Time;
            float next = Math.Min(Time + deltaTime, Duration);
            DispatchHits(previous, next, false);
            if (State != SkillPlaybackState.Playing) return;

            Time = next;
            Sample(Time);
            if (Time >= Duration)
            {
                ClearActive();
                State = SkillPlaybackState.Completed;
            }
        }

        /// <summary>拖动/预览到指定时间点；不触发实战命中回调。</summary>
        public void Evaluate(float time)
        {
            if (float.IsNaN(time) || float.IsInfinity(time) || time < 0f)
                throw new ArgumentOutOfRangeException(nameof(time));
            float clamped = Math.Min(time, Duration);
            if (clamped < lastSampledTime)
            {
                ClearActive();
                onReset?.Invoke();
                lastSampledTime = -0.0001f;
            }
            Time = clamped;
            State = SkillPlaybackState.Paused;
            Sample(Time);
        }

        public void Stop()
        {
            ClearActive();
            onReset?.Invoke();
            Time = 0f;
            lastSampledTime = -0.0001f;
            State = SkillPlaybackState.Stopped;
        }

        private void Sample(float t)
        {
            var list = schedule.OrderedClips;
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                bool isInstant = item.ResolvedDuration <= 0.0001f;
                bool inside = isInstant
                    ? (t >= item.Start && lastSampledTime < item.Start)
                    : (t >= item.Start && t < item.End);

                if (inside)
                {
                    if (isInstant)
                    {
                        if (!firedInstant[i])
                        {
                            firedInstant[i] = true;
                            onSample(item, 0f);
                            onExit(item);
                        }
                    }
                    else
                    {
                        activeClips[i] = true;
                        onSample(item, Math.Max(0f, t - item.Start));
                    }
                }
                else if (activeClips[i])
                {
                    activeClips[i] = false;
                    onExit(item);
                }
            }
            lastSampledTime = t;
        }

        private void DispatchHits(float from, float to, bool inclusiveStart)
        {
            if (onHit == null) return;
            var list = schedule.OrderedClips;
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item.Config.Type != SkillClipType.PlayHit) continue;
                bool crossed = inclusiveStart
                    ? (item.Start >= from && item.Start <= to)
                    : (item.Start > from && item.Start <= to);
                if (crossed) onHit(item);
            }
        }

        private void ClearActive()
        {
            var list = schedule.OrderedClips;
            for (int i = 0; i < list.Count; i++)
            {
                if (activeClips[i])
                {
                    activeClips[i] = false;
                    onExit(list[i]);
                }
                firedInstant[i] = false;
            }
        }
    }
}
