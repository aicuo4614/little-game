using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LittleGame.Client
{
    /// <summary>奇幻UI公共视觉，不承担交互或玩法规则。</summary>
    public static class CombatUiSkin
    {
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        public static readonly Color Gold = new Color(.97f, .79f, .43f);
        public static readonly Color Ink = new Color(.12f, .105f, .17f);
        public static readonly Color Paper = new Color(.98f, .94f, .85f);
        public static Sprite Sprite(string name)
        {
            if (!sprites.TryGetValue(name, out var sprite) || sprite == null) sprites[name] = sprite = Resources.Load<Sprite>("CombatUI/" + name);
            return sprite;
        }
        private static readonly Dictionary<string, Sprite> portraits = new Dictionary<string, Sprite>();
        public static Sprite Portrait(string heroId)
        {
            if (!portraits.TryGetValue(heroId, out var sprite) || sprite == null)
                portraits[heroId] = sprite = MVC.ResourceService.LoadAsset<Sprite>("Portrait/" + heroId, ResourcePackageName.UnitPackage);
            return sprite;
        }
        public static void ReleasePortraits()
        {
            foreach (string id in portraits.Keys) MVC.ResourceService.Release("Portrait/" + id, ResourcePackageName.UnitPackage);
            portraits.Clear();
        }
        public static Image Decorate(RectTransform rect, string sprite, Color? tint = null, bool sliced = true, float density = 2)
        {
            var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            image.sprite = Sprite(sprite); image.color = tint ?? Color.white; image.raycastTarget = false;
            image.type = sliced ? Image.Type.Sliced : Image.Type.Simple; image.pixelsPerUnitMultiplier = density;
            image.preserveAspect = !sliced; return image;
        }
        public static string TroopIcon(int kind) => new[] { "Shield", "Archer", "Storm", "Assassin" }[kind];
        public static object Snapshot() => new { theme = "fantasy-rpg-indigo-gold", loadedSprites = sprites.Count, missingSprites = MissingSprites() };
        private static int MissingSprites() { int count = 0; foreach (var sprite in sprites.Values) if (sprite == null) count++; return count; }
    }
}
