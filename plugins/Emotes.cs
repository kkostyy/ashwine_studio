using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    // Жесты и танцы из меню. Базовые — всем, танцы и насмешки — по праву (продаёт магазин).
    [Info("Emotes", "Claude", "1.1.0")]
    [Description("Gesture wheel in the ASH & WINE style, groups unlocked by permissions")]
    public class Emotes : RustPlugin
    {
        private const string Ui = "Emotes.Main";

        private Configuration _config;

        private class Emote { public string Gesture; public string Ru; public string En; }
        private class Group
        {
            [JsonProperty("Право (пусто — всем)")] public string Perm = "";
            [JsonProperty("Название")] public string Ru;
            [JsonProperty("Название EN")] public string En;
            [JsonProperty("Жесты")] public List<Emote> List = new List<Emote>();
        }
        private class Configuration
        {
            [JsonProperty("Пауза между жестами (с)")] public float Cooldown = 2f;
            [JsonProperty("Группы")] public List<Group> Groups = new List<Group>
            {
                new Group { Ru = "Основные", En = "Basic", List = new List<Emote>
                {
                    E("wave", "Помахать", "Wave"), E("thumbsup", "Класс", "Thumbs up"), E("thumbsdown", "Не очень", "Thumbs down"),
                    E("ok", "Окей", "OK"), E("point", "Показать", "Point"), E("shrug", "Пожать плечами", "Shrug"),
                    E("clap", "Хлопать", "Clap"), E("victory", "Победа", "Victory"), E("hurry", "Быстрее", "Hurry"), E("friendly", "Я свой", "Friendly"),
                } },
                new Group { Perm = "emotes.dance", Ru = "Танцы", En = "Dances", List = new List<Emote>
                {
                    E("cabbagepatch", "Капуста", "Cabbage patch"), E("twist", "Твист", "Twist"), E("raiseroof", "Качай", "Raise the roof"),
                } },
                new Group { Perm = "emotes.taunt", Ru = "Насмешки", En = "Taunts", List = new List<Emote>
                {
                    E("throatcut", "По горлу", "Throat cut"), E("fingergun", "Пиу", "Finger gun"), E("loser", "Лузер", "Loser"),
                    E("nono", "Не-не", "No no"), E("watchingyou", "Слежу", "Watching you"), E("shush", "Тсс", "Shush"),
                } },
            };
            private static Emote E(string g, string ru, string en) => new Emote { Gesture = g, Ru = ru, En = en };
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { _config = Config.ReadObject<Configuration>(); if (_config == null || _config.Groups == null) throw new Exception(); }
            catch { PrintWarning("Конфиг повреждён — создаю новый"); LoadDefaultConfig(); }
            SaveConfig();
        }
        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private readonly Dictionary<ulong, float> _last = new Dictionary<ulong, float>();
        private static ulong Id(BasePlayer p) => (ulong)p.userID;

        private void Init()
        {
            foreach (var g in _config.Groups) if (!string.IsNullOrEmpty(g.Perm) && !permission.PermissionExists(g.Perm, this)) permission.RegisterPermission(g.Perm, this);
            RegisterLang();
        }
        private void Unload() { foreach (var p in BasePlayer.activePlayerList) CuiHelper.DestroyUi(p, Ui); }

        private bool Has(BasePlayer p, Group g) => string.IsNullOrEmpty(g.Perm) || permission.UserHasPermission(p.UserIDString, g.Perm);

        private void Play(BasePlayer p, string gesture)
        {
            if (p == null || p.IsDead() || p.isMounted) return;
            var g = _config.Groups.FirstOrDefault(x => x.List.Any(e => e.Gesture == gesture));
            if (g == null) return;
            var id = Id(p);
            if (!Has(p, g)) { Msg(p, "Locked", Lang(id) == "ru" ? g.Ru : g.En); return; }
            float now = Time.realtimeSinceStartup, last;
            if (_last.TryGetValue(id, out last) && now - last < _config.Cooldown) return;
            _last[id] = now;
            // жест играет клиент: та же команда, что на клавише жестов
            p.SendConsoleCommand("gesture", gesture);
        }

        [ChatCommand("emote")]
        private void CmdEmote(BasePlayer p, string c, string[] a)
        {
            if (a.Length > 0) { Play(p, a[0].ToLower()); return; }
            Open(p);
        }

        [ConsoleCommand("emotes.ui")]
        private void CmdUi(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;
            switch (arg.GetString(0))
            {
                case "close": CuiHelper.DestroyUi(p, Ui); break;
                case "play": CuiHelper.DestroyUi(p, Ui); Play(p, arg.GetString(1)); break;
                case "open": Open(p); break;
            }
        }

        private void Open(BasePlayer p)
        {
            VInit();
            var id = Id(p);
            bool ru = Lang(id) == "ru";
            const float W = 640f;
            float rowsH = _config.Groups.Sum(g => 30 + Mathf.CeilToInt(g.List.Count / 5f) * 46 + 12);
            float H = 73 + rowsH + 8;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0.03 0.02 0.02 0.5" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", Ui);
            c.Add(new CuiButton { Button = { Command = "emotes.ui close", Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, Text = { Text = "" } }, Ui);
            string w = Ui + ".w";
            VRoot(c, w, Ui, VGlass, "0.5 0.5", "0.5 0.5", $"{-W / 2} {-H / 2}", $"{W / 2} {H / 2}", 22f, true);
            VHead(c, w, "play", "☺", T(id, "Title"), T(id, "Sub"), W, "emotes.ui close");
            float y = 73, bw = (W - 34 - 4 * 8) / 5f;
            foreach (var g in _config.Groups)
            {
                bool open = Has(p, g);
                VTx(c, w, (ru ? g.Ru : g.En).ToUpper(), 11, 17, y, 300, 22, TextAnchor.MiddleLeft, VDim, false);
                if (!open) VTx(c, w, T(id, "InShop"), 11, W - 317, y, 300, 22, TextAnchor.MiddleRight, VGold, false);
                y += 30;
                for (int i = 0; i < g.List.Count; i++)
                {
                    var e = g.List[i];
                    float x = 17 + (i % 5) * (bw + 8), yy = y + (i / 5) * 46;
                    VPBtn(c, w, (ru ? e.Ru : e.En), open ? "emotes.ui play " + e.Gesture : "chat.say /shop", open ? "ghost" : "dark", x, yy, bw, 38, 12);
                }
                y += Mathf.CeilToInt(g.List.Count / 5f) * 46 + 12;
            }
            CuiHelper.DestroyUi(p, Ui);
            CuiHelper.AddUi(p, c);
        }

        private void RegisterLang()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Title"] = "Жесты", ["Sub"] = "Нажми — персонаж сделает жест · /emote <название> — сразу",
                ["InShop"] = "открывается в магазине", ["Locked"] = "Группа «{0}» открывается в магазине (/shop)",
            }, this, "ru");
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Title"] = "Emotes", ["Sub"] = "Click to play · /emote <name> for a quick one",
                ["InShop"] = "unlock in the shop", ["Locked"] = "\"{0}\" unlocks in the shop (/shop)",
            }, this);
        }

        private string Lang(ulong id) => lang.GetLanguage(id.ToString());
        private string T(ulong id, string key, params object[] a) { var s = lang.GetMessage(key, this, id.ToString()); return a.Length > 0 ? string.Format(s, a) : s; }
        private void Msg(BasePlayer p, string key, params object[] a) => SendReply(p, "<color=#f0a6b0>Жесты</color> " + T(Id(p), key, a));
    
        #region Стиль VideoRecorder (общий для окон)

        private static string _vRound;
        private const string VRoundB64 = "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAACgklEQVR42u2bMWsUQRiGn5k7sbNKEBGxsUnAJo0BQVJohHTW/gWr9Km0Su1fsAkEa2MnHGiTRjgLbeQ4gpDGK5LC3f0s9p1ks7kLdmZ3vg+W290Zjnmf+WZnBuYNZsaCCMAAKPR8G3gOPAPWgLvALdX7n2HADJgCh8BH4APwS+VDoFS9yyIXAIhApfsV4BXwArhDN+IIeA+8Bb7N0dTAZ9a+BvodmtlrMzux8yjNrDCzStd1idSeQm1McSINw5a2s6udAQOly33gHfBY7wuVhY5kgEnHUM8j4CXws6Hx0hBIBavAgcb4H/1JV4TPA1EAN/SN2ATGTQgJQBof94AvGutFg2DXI2k5Ah4Bk6Q5qneDKO31UHyaCQpp25PWAIQoEiWwA6w30r5vMZS2dWktgRjMLAAPgK+qFDs85v/lm1ApGx4CP6JebgM3dd9X8WlxZ9K6DVgwsyV9GZcalfocado7BlYjsAUsZ9D77SxYBrai5kZbtFbucRYYsBm1sQmZ9H4zCwKwFszst3Z1lhGEpHUW7Ir9cA4RyTwcgANwAA7AATgAB+AAHIADcAAOwAE4AAfgAByAA3AADsABOAAH4AAcgANwAA7AATgAB5AHgBkXj4+RyREZgFmkPkWdK4BppLaZ5HpM7jBSewNyPSZ34EdldbMv4WUGvV9K6z5w7MflJfg7sMtFn2AfI5m/dqX5zDCRQHyidlQUPXSNJE2fgSfKhMpNU6pQKTUmwFMtjpLHxjo+5pMHaiptE2mt2nuBUgVjasPkiHN3VdExEMkvmNxwI2kat42Tcc4UMaB2WG4Ab4DThnmyahiR7Rqu7Eq1MajNp9KwMc816ubpKwBkY5//C56fx7Wog3HqAAAAAElFTkSuQmCC";
        private const string VGlass = "0.071 0.055 0.055 0.94", VSurf = "1 1 1 0.045", VLine = "1 1 1 0.08", VAcc = "0.620 0.082 0.188 1",
            VSoft = "0.639 0.086 0.184 0.18", VSoftLine = "0.941 0.651 0.694 0.25", VText = "0.937 0.914 0.906 1", VDim = "0.639 0.596 0.588 1",
            VRose = "0.941 0.651 0.694 1", VDark = "0 0 0 0.3", VGold = "0.851 0.737 0.514 1", VGreen = "0.498 0.831 0.604 1";

        private class VRoundImg : ICuiComponent
        {
            [Newtonsoft.Json.JsonProperty("type")] public string Type => "UnityEngine.UI.Image";
            [Newtonsoft.Json.JsonProperty("png")] public string Png;
            [Newtonsoft.Json.JsonProperty("color")] public string Color;
            [Newtonsoft.Json.JsonProperty("imagetype")] public string ImageType = "Sliced";
            [Newtonsoft.Json.JsonProperty("slice")] public string Slice = "16 16 16 16";
            [Newtonsoft.Json.JsonProperty("ppuMultiplier")] public float Ppu = 1f;
        }

        private static void VInit()
        {
            if (_vRound != null) return;
            try { _vRound = FileStorage.server.StoreByCrc(CommunityEntity.ServerInstance.net.ID, FileStorage.Type.png, Convert.FromBase64String(VRoundB64)).ToString(); }
            catch { _vRound = null; }
        }

        private static string VMin(float x, float y, float h) => $"{x} {-y - h}";
        private static string VMax(float x, float y, float w) => $"{x + w} {-y}";

        // окно/панель со скруглением по якорям
        private static void VRoot(CuiElementContainer c, string name, string parent, string color, string amin, string amax, string omin, string omax, float r, bool cursor)
        {
            var e = new CuiElement { Name = name, Parent = parent };
            if (_vRound != null && r > 0f) e.Components.Add(new VRoundImg { Png = _vRound, Color = color, Ppu = 16f / r });
            else e.Components.Add(new CuiImageComponent { Color = color });
            e.Components.Add(new CuiRectTransformComponent { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax });
            if (cursor) e.Components.Add(new CuiNeedsCursorComponent());
            c.Add(e);
        }

        private static void VBox(CuiElementContainer c, string parent, string color, float x, float y, float w, float h, float r = 12f)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) / 2f);
            if (_vRound != null && r > 0.5f)
                c.Add(new CuiElement { Parent = parent, Components = { new VRoundImg { Png = _vRound, Color = color, Ppu = 16f / r }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = VMin(x, y, h), OffsetMax = VMax(x, y, w) } } });
            else c.Add(new CuiPanel { Image = { Color = color }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = VMin(x, y, h), OffsetMax = VMax(x, y, w) } }, parent);
        }

        private static void VBBox(CuiElementContainer c, string parent, string bg, string line, float x, float y, float w, float h, float r)
        {
            VBox(c, parent, line, x, y, w, h, r);
            VBox(c, parent, "0.071 0.055 0.055 0.96", x + 1, y + 1, w - 2, h - 2, Mathf.Max(0f, r - 1));
            VBox(c, parent, bg, x + 1, y + 1, w - 2, h - 2, Mathf.Max(0f, r - 1));
        }

        private static void VTx(CuiElementContainer c, string parent, string text, int size, float x, float y, float w, float h,
            TextAnchor align = TextAnchor.MiddleLeft, string color = VText, bool bold = true)
        {
            c.Add(new CuiLabel { Text = { Text = text ?? "", FontSize = size, Align = align, Color = color, Font = bold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf" },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = VMin(x, y, h), OffsetMax = VMax(x, y, w) } }, parent);
        }

        private static void VHit(CuiElementContainer c, string parent, string cmd, float x, float y, float w, float h)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            c.Add(new CuiButton { Button = { Command = cmd, Color = "0 0 0 0" }, Text = { Text = "" }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = VMin(x, y, h), OffsetMax = VMax(x, y, w) } }, parent);
        }

        private string VIconPng(string n) => plugins.Find("Hub")?.Call("GetIconPng", n) as string;

        private void VIc(CuiElementContainer c, string parent, string icon, float x, float y, float s, string color = VText)
        {
            var png = VIconPng(icon);
            if (png == null) return;
            c.Add(new CuiElement { Parent = parent, Components = { new CuiRawImageComponent { Png = png, Color = color }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = VMin(x, y, s), OffsetMax = VMax(x, y, s) } } });
        }

        // кнопка-иконка 34×34 (как в VideoRecorder); без иконки — символ
        private void VIBtn(CuiElementContainer c, string parent, string icon, string fallback, string cmd, float x, float y, float s = 34f)
        {
            VBox(c, parent, VSurf, x, y, s, s, 10f);
            if (VIconPng(icon) != null) VIc(c, parent, icon, x + (s - 16f) / 2f, y + (s - 16f) / 2f, 16f);
            else VTx(c, parent, fallback, 16, x, y, s, s, TextAnchor.MiddleCenter);
            VHit(c, parent, cmd, x, y, s, s);
        }

        private static string VNice(string t)
        {
            if (string.IsNullOrEmpty(t) || t.Length < 5 || t != t.ToUpperInvariant() || t == t.ToLowerInvariant()) return t;
            return t.Substring(0, 1) + t.Substring(1).ToLowerInvariant();
        }

        // кнопка с подписью: primary / ghost / soft / dark
        private static void VPBtn(CuiElementContainer c, string parent, string text, string cmd, string kind, float x, float y, float w, float h, int fs = 12)
        {
            float r = h <= 40 ? h / 2f : 14f;
            if (kind == "soft") VBBox(c, parent, VSoft, VSoftLine, x, y, w, h, r);
            else VBox(c, parent, kind == "primary" ? VAcc : kind == "dark" ? VDark : VSurf, x, y, w, h, r);
            VTx(c, parent, VNice(text), fs, x, y, w, h, TextAnchor.MiddleCenter, kind == "soft" ? VRose : kind == "dark" ? VDim : VText);
            VHit(c, parent, cmd, x, y, w, h);
        }

        // шапка окна: винная плитка с иконкой, заголовок, подзаголовок, круглая кнопка закрытия
        private void VHead(CuiElementContainer c, string parent, string icon, string letter, string title, string sub, float w, string closeCmd)
        {
            VBox(c, parent, VAcc, 17, 19, 40, 40, 12f);
            if (VIconPng(icon) != null) VIc(c, parent, icon, 27, 29, 20, "1 1 1 1");
            else VTx(c, parent, letter, 15, 17, 19, 40, 40, TextAnchor.MiddleCenter);
            VTx(c, parent, title, 18, 69, 20, w - 69 - 60, 22);
            VTx(c, parent, sub, 12, 69, 42, w - 69 - 60, 16, TextAnchor.MiddleLeft, VDim, false);
            if (!string.IsNullOrEmpty(closeCmd)) VIBtn(c, parent, "close", "×", closeCmd, w - 51, 22);
        }

        #endregion
    }
}
