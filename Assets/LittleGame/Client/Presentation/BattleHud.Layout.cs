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
        private void Rebuild()
        {
            foreach (Transform child in dynamic) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            targets.Clear();
            statusPanel.RegisterTargets();
            footer.anchorMax = new Vector2(1, app.Session.Phase == RunPhase.Fighting ? .115f : .325f);
            Array.Clear(reelCardImages, 0, 9);
            Array.Clear(reelIconImages, 0, 9);
            Array.Clear(reelTileRects, 0, 9);
            Array.Clear(reserveSlotRects, 0, 6);

            var s = app.Session;
            Button("restart", "重开", new Rect(.82f, .944f, .145f, .046f), () => { confirmRestart = true; Refresh(); });

            if (s.Phase == RunPhase.Preparation)
            {
                var reelPanel = Panel(dynamic, "Reels", new Rect(.03f, .11f, .435f, .18f), Color.white);
                CombatUiSkin.Decorate(reelPanel, "Panel", Color.white, true, 5);

                Panel(reelPanel, "CrossRowTrack", new Rect(.015f, .345f, .97f, .31f), new Color(1f, 0.82f, 0.28f, 0.16f));
                Panel(reelPanel, "CrossColTrack", new Rect(.34f, .015f, .32f, .97f), new Color(1f, 0.82f, 0.28f, 0.16f));

                for (int row = 0; row < 3; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        int idx = row * 3 + col;
                        var kind = s.Reels[idx];
                        bool onCross = row == 1 || col == 1;
                        bool wonCell = (s.LastWinRowMid && row == 1) || (s.LastWinColMid && col == 1);
                        var tile = Panel(reelPanel, "Symbol_" + idx, new Rect(col / 3f + .02f, (2 - row) / 3f + .025f, .295f, .29f), Color.white);
                        Color cardTint = wonCell
                            ? new Color(1f, 0.90f, 0.45f)
                            : onCross
                                ? new Color(0.95f, 0.90f, 0.72f)
                                : new Color(0.55f, 0.56f, 0.68f);
                        CombatUiSkin.Decorate(tile, "Card", cardTint);
                        var iconImg = Icon(tile, CombatUiSkin.TroopIcon((int)kind), new Rect(.15f, .12f, .7f, .76f),
                            kind == TroopKind.Frost ? new Color(.55f, .9f, 1f) : (Color?)null);

                        reelTileRects[idx] = tile;
                        reelCardImages[idx] = tile.GetComponent<Image>();
                        reelIconImages[idx] = iconImg;
                    }
                }

                // 仅显示已落地揭晓的候补单位（飞行途中隐藏，抵达后立即显现）
                var reserve = s.Troops.Where(t => !t.Deployed && !hiddenReserveIds.Contains(t.Id)).ToArray();
                for (int i = 0; i < 6; i++)
                {
                    var rect = new Rect(.51f + (i % 3) * .158f, .205f - (i / 3) * .09f, .148f, .082f);
                    if (i >= reserve.Length)
                    {
                        var empty = Panel(dynamic, "EmptyReserve", rect, Color.white);
                        CombatUiSkin.Decorate(empty, "Card", new Color(.55f, .55f, .67f));
                        Text(empty, "+", new Rect(0, .1f, 1, .8f), 24, TextAnchor.MiddleCenter, new Color(.4f, .39f, .49f));
                        reserveSlotRects[i] = empty;
                        continue;
                    }
                    var t = reserve[i];
                    var btnRect = Button("unit_" + t.Id, t.Definition.Name + "\n" + new string('★', t.Star), rect,
                        () => ShowTip("拖动或双击" + t.Definition.Name + "上阵", isError: false), t.Id, t.Star >= 2);
                    reserveSlotRects[i] = btnRect;
                }

                Button("spin", "招募  ·  " + s.Spins + " 次", new Rect(.03f, .027f, .435f, .065f), () => Submit(new BattleCommand { Kind = CommandKind.Spin }));
                Button("start_wave", "开始战斗", new Rect(.51f, .027f, .46f, .065f), () => { if (s.Spins > 0) { confirmStart = true; Refresh(); } else Submit(new BattleCommand { Kind = CommandKind.StartWave }); });
                Text(dynamic, "十字三同出兵 · 拖动或双击候补上阵 · 拖回候补区召回", new Rect(.04f, .004f, .92f, .02f), 10, TextAnchor.MiddleCenter, new Color(.6f, .57f, .67f));

                if (s.Pending.Count > 0) ShowTip("暂存" + s.Pending.Count + "兵：先部署或遣散候补再继续", isError: true);
            }
            else if (s.Phase == RunPhase.Fighting)
            {
                Button("pause", s.Paused ? "继续" : "暂停", new Rect(.05f, .035f, .27f, .06f), () => Submit(new BattleCommand { Kind = CommandKind.Pause, Flag = !s.Paused }));
                Button("speed", s.Speed + "倍速", new Rect(.68f, .035f, .27f, .06f), () => Submit(new BattleCommand { Kind = CommandKind.SetSpeed, Speed = s.Speed == 1 ? 2 : 1 }));
            }
            else if (s.Phase == RunPhase.Reward)
            {
                var options = Enum.GetValues(typeof(RewardKind)).Cast<RewardKind>().Where(r => !s.Rewards.Contains(r)).ToArray();
                for (int i = 0; i < options.Length; i++)
                {
                    var r = options[i]; float x = .03f + i * .32f;
                    Button("reward_" + r, BattleDefinitions.Name(r) + "\n" + BattleDefinitions.RewardDescription(r), new Rect(x, .06f, .30f, .23f), () => Submit(new BattleCommand { Kind = CommandKind.ChooseReward, Reward = r }));
                }
            }
            else
            {
                Text(dynamic, s.Phase == RunPhase.Won ? "大捷！第一关远征全胜" : s.Phase == RunPhase.Lost ? "全军覆没 · 调整阵容再战" : "本波告捷 · 全员满血恢复，人口经验 +2", new Rect(.05f, .17f, .9f, .09f), 20, TextAnchor.MiddleCenter);
                if (s.Phase == RunPhase.WaveResult) Button("next_wave", "继续推进", new Rect(.15f, .05f, .7f, .085f), () => { ClearSelection(); Submit(new BattleCommand { Kind = CommandKind.NextWave }); });
                else Button("new_run", "重新远征", new Rect(.15f, .05f, .7f, .085f), () => Submit(new BattleCommand { Kind = CommandKind.NewRun, Seed = BattleApplication.FreshSeed() }));
            }
            if (confirmStart || confirmRestart || confirmRecycle) ConfirmPanel();
            if (fxLayer != null) fxLayer.SetAsLastSibling();
            if (toastRoot != null) toastRoot.SetAsLastSibling();
        }

        private void ConfirmPanel()
        {
            Panel(dynamic, "ConfirmBackdrop", new Rect(0, 0, 1, 1), new Color(0, 0, 0, .7f));
            var confirm = Panel(dynamic, "Confirm", new Rect(.07f, .35f, .86f, .31f), Color.white);
            CombatUiSkin.Decorate(confirm, "Panel", Color.white, true, 2);
            Text(confirm, confirmRestart ? "重新远征" : confirmStart ? "准备迎战" : "遣散候补", new Rect(.08f, .78f, .84f, .14f), 23, TextAnchor.MiddleCenter, CombatUiSkin.Gold).fontStyle = FontStyle.Bold;
            var s = app.Session;
            var host = s.Troops.FirstOrDefault(t => t.Id == Selected);
            string text = confirmRestart ? "重开会清空本局军团与奖励，确定？" : confirmStart ? "还有" + s.Spins + "次未转，提前开战会放弃这些次数。确定？" : "遣散这名候补，本局不可找回。确定？";
            Text(dynamic, text, new Rect(.12f, .455f, .76f, .12f), 18, TextAnchor.MiddleCenter);
            Button("confirm_cancel", "取消", new Rect(.12f, .375f, .33f, .063f), CancelUi);
            Button("confirm_accept", "确认", new Rect(.55f, .375f, .33f, .063f), () =>
            {
                if (confirmRestart) { Selected = 0; Submit(new BattleCommand { Kind = CommandKind.NewRun, Seed = BattleApplication.FreshSeed() }); }
                else if (confirmStart) Submit(new BattleCommand { Kind = CommandKind.StartWave });
                else if (confirmRecycle && host != null) Submit(new BattleCommand { Kind = CommandKind.Recycle, UnitId = host.Id });
                else CancelUi();
            });
        }

        private RectTransform Button(string id, string label, Rect area, Action click, int unitId = 0, bool accent = false)
        {
            string sprite = id.StartsWith("unit_") ? (accent ? "CardGold" : "Card") : id == "start_wave" || id == "confirm_accept" || id == "next_wave" || id == "new_run" ? "ButtonBlue" : id == "spin" || id.StartsWith("reward_") ? "ButtonGold" : "ButtonGray";
            Color tint = id.StartsWith("unit_") ? (accent ? new Color(1, .95f, .72f) : new Color(.9f, .9f, 1)) : id == "spin" && app.Session.Spins <= 0 ? new Color(.55f, .55f, .62f) : Color.white;
            var rect = Panel(dynamic, "Btn_" + id, area, Color.white);
            CombatUiSkin.Decorate(rect, sprite, tint);
            var textColor = sprite == "ButtonGold" ? new Color(.22f, .12f, .04f) : id.StartsWith("unit_") ? CombatUiSkin.Paper : Color.white;
            bool iconBtn = id.StartsWith("unit_");
            if (id.StartsWith("unit_"))
            {
                var unit = app.Session.Troops.FirstOrDefault(u => u.Id == unitId);
                if (unit != null) Icon(rect, "Helmet", new Rect(.27f, .4f, .46f, .52f)).sprite = CombatUiSkin.Portrait(unit.Identity);
            }
            var text = Text(rect, label, id.StartsWith("unit_") ? new Rect(.04f, .02f, .92f, .44f) : iconBtn ? new Rect(.35f, .05f, .61f, .9f) : new Rect(.05f, .05f, .9f, .9f), id.StartsWith("unit_") ? 10 : id == "spin" || id == "start_wave" ? 19 : 14, iconBtn && !id.StartsWith("unit_") ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, textColor);
            if (id == "spin" || id == "start_wave" || id == "confirm_accept") text.fontStyle = FontStyle.Bold;
            targets.Add(new HudTarget { Id = id, Rect = rect, Click = click, UnitId = unitId, UiCamera = uiCamera });
            return rect;
        }

        private Image Icon(Transform parent, string key, Rect area, Color? tint = null)
        {
            var go = new GameObject("Icon_" + key, typeof(RectTransform), typeof(Image));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = area.min; rect.anchorMax = area.max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.GetComponent<Image>();
            image.sprite = CombatUiSkin.Sprite(key);
            image.preserveAspect = true;
            image.color = tint ?? Color.white;
            image.raycastTarget = false;
            return image;
        }

        private Image CreateRingImage(Transform parent, string name, Rect area, Color color, float fillAmount)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = area.min; rect.anchorMax = area.max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = radialRingSprite;
            img.preserveAspect = true;
            img.color = color;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)Image.Origin360.Top;
            img.fillClockwise = true;
            img.fillAmount = fillAmount;
            img.raycastTarget = false;
            return img;
        }

        private static Sprite CreateRadialRingSprite()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float center = (size - 1) * 0.5f;
            float outer = size * 0.46f;
            float inner = size * 0.30f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float aOuter = Mathf.Clamp01(outer - d + 1f);
                    float aInner = Mathf.Clamp01(d - inner + 1f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, aOuter * aInner));
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        private RectTransform Panel(Transform parent, string name, Rect area, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = area.min; rect.anchorMax = area.max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            go.GetComponent<Image>().color = color;
            go.GetComponent<Image>().raycastTarget = false;
            return rect;
        }

        private Text Text(Transform parent, string content, Rect area, int size, TextAnchor align = TextAnchor.MiddleCenter, Color? color = null)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = area.min; rect.anchorMax = area.max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>();
            text.text = content; text.fontSize = size; text.alignment = align;
            text.color = color ?? new Color(.95f, .96f, .91f);
            text.font = font; text.raycastTarget = false;
            return text;
        }

    }
}
