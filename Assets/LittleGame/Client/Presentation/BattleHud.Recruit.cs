using System;
using System.Collections.Generic;
using System.Linq;
using LittleGame.Client.Battle;
using LittleGame.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace LittleGame.Client
{
    public sealed partial class BattleHud
    {
        private void UpdateSlotReelAnimation(BattleSession s)
        {
            if (s.Phase != RunPhase.Preparation) return;
            float dt = Time.unscaledDeltaTime;

            if (reelSpinTimer > 0f)
            {
                reelSpinTimer = Mathf.Max(0f, reelSpinTimer - dt);
                reelIconFlipTimer -= dt;
                bool flipTick = reelIconFlipTimer <= 0f;
                if (flipTick) reelIconFlipTimer = 0.055f;

                float elapsed = 0.50f - reelSpinTimer;
                float[] stopTimes = { 0.18f, 0.32f, 0.46f };

                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        int idx = row * 3 + col;
                        var icon = reelIconImages[idx];
                        var tile = reelTileRects[idx];
                        if (icon == null || tile == null) continue;

                        if (elapsed < stopTimes[col])
                        {
                            if (flipTick)
                            {
                                int fakeKind = ((int)(elapsed * 26f) + row + col * 2) % 4;
                                icon.sprite = CombatUiSkin.Sprite(CombatUiSkin.TroopIcon(fakeKind));
                                icon.color = fakeKind == (int)TroopKind.Frost ? new Color(.55f, .9f, 1f) : Color.white;
                            }
                            float bounce = 0.92f + 0.08f * Mathf.Sin(elapsed * 42f + col);
                            tile.localScale = new Vector3(1f, bounce, 1f);
                        }
                        else
                        {
                            var kind = s.Reels[idx];
                            icon.sprite = CombatUiSkin.Sprite(CombatUiSkin.TroopIcon((int)kind));
                            icon.color = kind == TroopKind.Frost ? new Color(.55f, .9f, 1f) : Color.white;
                            float settle = Mathf.Clamp01((elapsed - stopTimes[col]) / 0.10f);
                            float pop = 1f + Mathf.Sin(settle * Mathf.PI) * 0.12f;
                            tile.localScale = Vector3.one * pop;
                        }
                    }
                }

                if (reelSpinTimer <= 0f)
                {
                    for (int i = 0; i < 9; i++) if (reelTileRects[i] != null) reelTileRects[i].localScale = Vector3.one;
                    if (reelWinRowMid || reelWinColMid) reelWinFlashTimer = 0.42f;
                }
                return;
            }

            if (reelWinFlashTimer > 0f)
            {
                reelWinFlashTimer = Mathf.Max(0f, reelWinFlashTimer - dt);
                float progress = 1f - reelWinFlashTimer / 0.42f;
                float flash = Mathf.Sin(progress * Mathf.PI * 3f) * 0.5f + 0.5f;
                float shinePos = Mathf.Lerp(-0.4f, 1.4f, progress);
                if (slotShineMat != null)
                {
                    slotShineMat.SetFloat(ShinePosId, shinePos);
                    slotShineMat.SetFloat(FlashIntensityId, flash * (1f - progress * 0.35f) * 1.4f);
                }

                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        int idx = row * 3 + col;
                        bool wonCell = (reelWinRowMid && row == 1) || (reelWinColMid && col == 1);
                        if (!wonCell) continue;
                        if (reelCardImages[idx] != null && slotShineMat != null)
                            reelCardImages[idx].material = reelWinFlashTimer > 0.01f ? slotShineMat : null;
                        if (reelTileRects[idx] != null)
                        {
                            float sPop = 1f + Mathf.Sin(progress * Mathf.PI * 2f) * 0.11f * (1f - progress);
                            reelTileRects[idx].localScale = Vector3.one * sPop;
                        }
                    }
                }
                if (reelWinFlashTimer <= 0.01f)
                {
                    for (int i = 0; i < 9; i++)
                    {
                        if (reelCardImages[i] != null) reelCardImages[i].material = null;
                        if (reelTileRects[i] != null) reelTileRects[i].localScale = Vector3.one;
                    }
                    if (pendingFlyCount > 0)
                    {
                        LaunchRecruitConvergeAndFly(s);
                        pendingFlyCount = 0;
                    }
                }
            }
        }

        private void LaunchRecruitConvergeAndFly(BattleSession s)
        {
            if (fxLayer == null)
            {
                ClearRecruitFlyAnims(revealAll: true);
                return;
            }

            var linesToLaunch = new List<int[]>();
            if (reelWinRowMid) linesToLaunch.Add(new[] { 3, 4, 5 });
            if (reelWinColMid) linesToLaunch.Add(new[] { 1, 4, 7 });

            // 当前已可见候补数决定新单位落地槽位序号
            int visibleBefore = s.Troops.Count(t => !t.Deployed && !hiddenReserveIds.Contains(t.Id));
            Vector2 centerAnchor = ReelTileAnchorCenter(4);

            for (int li = 0; li < linesToLaunch.Count; li++)
            {
                int revealId = li < pendingRevealIds.Count ? pendingRevealIds[li] : 0;
                int slotIdx = -1;
                Vector2 targetAnchor;

                if (revealId > 0)
                {
                    // 正常进入候补军团：飞向对应候补空槽，落地后再显现卡片
                    slotIdx = Mathf.Clamp(visibleBefore + li, 0, 5);
                    targetAnchor = ReserveSlotAnchorCenter(slotIdx);
                }
                else if (pendingMergedHostIds.Count > 0)
                {
                    // 直接触发了自动 3 合 1 升星：飞向发生升星的宿主真实位置（场上英雄或候补宿主）
                    int hostId = pendingMergedHostIds[Mathf.Min(li, pendingMergedHostIds.Count - 1)];
                    var host = s.Troops.FirstOrDefault(t => t.Id == hostId);
                    if (host != null && host.Deployed)
                    {
                        Vector2 scr = view.UnitScreen(host.Id);
                        targetAnchor = new Vector2(
                            Mathf.Clamp01(scr.x / Mathf.Max(1f, Screen.width)),
                            Mathf.Clamp01(scr.y / Mathf.Max(1f, Screen.height)));
                    }
                    else
                    {
                        int hostReserveIdx = s.Troops.Where(t => !t.Deployed).ToList().FindIndex(t => t.Id == hostId);
                        slotIdx = Mathf.Clamp(hostReserveIdx >= 0 ? hostReserveIdx : 0, 0, 5);
                        targetAnchor = ReserveSlotAnchorCenter(slotIdx);
                    }
                }
                else
                {
                    slotIdx = Mathf.Clamp(visibleBefore + li, 0, 5);
                    targetAnchor = ReserveSlotAnchorCenter(slotIdx);
                }

                var anim = new RecruitFlyAnim
                {
                    Kind = lastWonKind,
                    SourceIndices = linesToLaunch[li],
                    ConvergeIcons = new RectTransform[3],
                    CenterAnchor = centerAnchor,
                    TargetAnchor = targetAnchor,
                    RevealUnitId = revealId,
                    TargetSlotIdx = slotIdx,
                    Delay = li * 0.10f
                };

                Color iconTint = lastWonKind == TroopKind.Frost ? new Color(0.65f, 0.94f, 1f) : Color.white;
                string iconKey = CombatUiSkin.TroopIcon((int)lastWonKind);

                for (int k = 0; k < 3; k++)
                {
                    Vector2 src = ReelTileAnchorCenter(anim.SourceIndices[k]);
                    var iconRoot = Panel(fxLayer, "RecruitConverge_" + li + "_" + k, new Rect(src.x - 0.045f, src.y - 0.032f, 0.09f, 0.064f), Color.clear);
                    Icon(iconRoot, iconKey, new Rect(0.08f, 0.08f, 0.84f, 0.84f), iconTint);
                    anim.ConvergeIcons[k] = iconRoot;
                }

                var orb = Panel(fxLayer, "RecruitMergedOrb_" + li, new Rect(centerAnchor.x - 0.055f, centerAnchor.y - 0.04f, 0.11f, 0.08f), Color.clear);
                var halo = CreateRingImage(orb, "OrbHalo", new Rect(0f, 0f, 1f, 1f), new Color(1f, 0.86f, 0.30f, 0.95f), 1f).rectTransform;
                Icon(orb, iconKey, new Rect(0.16f, 0.16f, 0.68f, 0.68f), iconTint);
                orb.gameObject.SetActive(false);

                anim.MergedOrb = orb;
                anim.MergedHalo = halo;
                recruitFlyAnims.Add(anim);
            }
            fxLayer.SetAsLastSibling();
            if (toastRoot != null) toastRoot.SetAsLastSibling();
        }

        private void UpdateRecruitFlyAnims(float dt)
        {
            if (recruitFlyAnims.Count > 0)
            {
                for (int i = recruitFlyAnims.Count - 1; i >= 0; i--)
                {
                    var a = recruitFlyAnims[i];
                    if (a.Delay > 0f)
                    {
                        a.Delay -= dt;
                        continue;
                    }

                    a.Elapsed += dt;
                    if (a.Elapsed < a.ConvergeDuration)
                    {
                        float t = Mathf.Clamp01(a.Elapsed / a.ConvergeDuration);
                        float ease = t * t * (3f - 2f * t);
                        for (int k = 0; k < 3; k++)
                        {
                            var rt = a.ConvergeIcons[k];
                            if (rt == null) continue;
                            Vector2 src = ReelTileAnchorCenter(a.SourceIndices[k]);
                            Vector2 p = Vector2.Lerp(src, a.CenterAnchor, ease);
                            rt.anchorMin = p - new Vector2(0.045f, 0.032f);
                            rt.anchorMax = p + new Vector2(0.045f, 0.032f);
                            rt.localScale = Vector3.one * Mathf.Lerp(1.05f, 0.65f, ease);
                        }
                        continue;
                    }

                    if (!a.BurstDone)
                    {
                        a.BurstDone = true;
                        for (int k = 0; k < 3; k++)
                            if (a.ConvergeIcons[k] != null) Destroy(a.ConvergeIcons[k].gameObject);
                        if (a.MergedOrb != null) a.MergedOrb.gameObject.SetActive(true);
                    }

                    float flyT = Mathf.Clamp01((a.Elapsed - a.ConvergeDuration) / a.FlyDuration);
                    float flyEase = 1f - Mathf.Pow(1f - flyT, 2.2f);
                    if (a.MergedOrb != null)
                    {
                        Vector2 lin = Vector2.Lerp(a.CenterAnchor, a.TargetAnchor, flyEase);
                        float arcY = Mathf.Sin(flyT * Mathf.PI) * 0.135f;
                        Vector2 p = lin + new Vector2(0f, arcY);
                        a.MergedOrb.anchorMin = p - new Vector2(0.055f, 0.04f);
                        a.MergedOrb.anchorMax = p + new Vector2(0.055f, 0.04f);
                        float popScale = flyT < 0.25f
                            ? Mathf.Lerp(1.38f, 1.08f, flyT / 0.25f)
                            : Mathf.Lerp(1.08f, 0.78f, (flyT - 0.25f) / 0.75f);
                        a.MergedOrb.localScale = Vector3.one * popScale;
                        if (a.MergedHalo != null) a.MergedHalo.localRotation = Quaternion.Euler(0f, 0f, -flyT * 420f);
                    }

                    if (flyT >= 1f)
                    {
                        if (a.MergedOrb != null) Destroy(a.MergedOrb.gameObject);
                        int arrivedSlot = a.TargetSlotIdx;
                        // 动画抵达结束：此时才真正显现候补图标并触发槽位弹跳！
                        if (a.RevealUnitId > 0 && hiddenReserveIds.Remove(a.RevealUnitId))
                        {
                            Refresh();
                            Rebuild();
                            Canvas.ForceUpdateCanvases();
                        }
                        if (arrivedSlot >= 0 && arrivedSlot < reserveSlotRects.Length && reserveSlotRects[arrivedSlot] != null)
                        {
                            reserveSlotRects[arrivedSlot].localScale = Vector3.one * 1.16f;
                        }
                        recruitFlyAnims.RemoveAt(i);
                    }
                }
            }

            // 候补槽位弹跳平滑回弹至 1.0
            for (int sIdx = 0; sIdx < reserveSlotRects.Length; sIdx++)
            {
                var rt = reserveSlotRects[sIdx];
                if (rt != null && rt.localScale.x > 1.001f)
                    rt.localScale = Vector3.Lerp(rt.localScale, Vector3.one, Mathf.Clamp01(dt * 14f));
            }
        }

        private void ClearRecruitFlyAnims(bool revealAll)
        {
            for (int i = 0; i < recruitFlyAnims.Count; i++)
            {
                var a = recruitFlyAnims[i];
                if (a.ConvergeIcons != null)
                    for (int k = 0; k < a.ConvergeIcons.Length; k++)
                        if (a.ConvergeIcons[k] != null) Destroy(a.ConvergeIcons[k].gameObject);
                if (a.MergedOrb != null) Destroy(a.MergedOrb.gameObject);
            }
            recruitFlyAnims.Clear();
            pendingFlyCount = 0;
            if (revealAll && hiddenReserveIds.Count > 0)
            {
                hiddenReserveIds.Clear();
                Refresh();
            }
        }

        private static Vector2 ReelTileAnchorCenter(int idx)
        {
            int row = idx / 3;
            int col = idx % 3;
            float rx = 0.03f + (col / 3f + 0.02f + 0.1475f) * 0.435f;
            float ry = 0.11f + ((2 - row) / 3f + 0.025f + 0.145f) * 0.18f;
            return new Vector2(rx, ry);
        }

        private static Vector2 ReserveSlotAnchorCenter(int slotIdx)
        {
            int i = Mathf.Clamp(slotIdx, 0, 5);
            float x = 0.51f + (i % 3) * 0.158f + 0.074f;
            float y = 0.205f - (i / 3) * 0.09f + 0.041f;
            return new Vector2(x, y);
        }

    }
}
