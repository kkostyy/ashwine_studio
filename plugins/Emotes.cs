using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    // Жесты и танцы из меню. Базовые — всем, танцы и насмешки — по праву (продаёт магазин).
    [Info("Emotes", "Claude", "1.0.0")]
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

        private const string ColGlass = "0.07 0.04 0.05 0.96", ColPanel = "1 1 1 0.05", ColWine = "0.639 0.086 0.184 1", ColWineSoft = "0.639 0.086 0.184 0.35",
            ColText = "0.95 0.92 0.92 1", ColDim = "0.66 0.6 0.62 1", ColPink = "0.94 0.65 0.69 1", ColGold = "0.91 0.77 0.42 1";

        private void Open(BasePlayer p)
        {
            var id = Id(p);
            bool ru = Lang(id) == "ru";
            const float W = 640f;
            float rowsH = _config.Groups.Sum(g => 30 + Mathf.CeilToInt(g.List.Count / 5f) * 44 + 10);
            float H = 76 + rowsH + 16;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0 0 0 0.5" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", Ui);
            c.Add(new CuiButton { Button = { Command = "emotes.ui close", Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, Text = { Text = "" } }, Ui);
            string w = Ui + ".w";
            c.Add(new CuiPanel { Image = { Color = ColGlass }, RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-W / 2} {-H / 2}", OffsetMax = $"{W / 2} {H / 2}" } }, Ui, w);
            Box(c, w, ColWine, 0, 0, W, 4);
            Txt(c, w, T(id, "Title"), 18, 20, 16, W - 80, 26, TextAnchor.MiddleLeft, ColText, true);
            Txt(c, w, T(id, "Sub"), 11, 20, 42, W - 80, 16, TextAnchor.MiddleLeft, ColDim, false);
            Btn(c, w, "×", "emotes.ui close", "1 1 1 0.06", W - 52, 16, 34, 34, 16, ColText);
            float y = 76, bw = (W - 40 - 4 * 8) / 5f;
            foreach (var g in _config.Groups)
            {
                bool open = Has(p, g);
                Txt(c, w, (ru ? g.Ru : g.En).ToUpper() + (open ? "" : "  · " + T(id, "InShop")), 10, 20, y, W - 40, 22, TextAnchor.MiddleLeft, open ? ColPink : ColGold, false);
                y += 30;
                for (int i = 0; i < g.List.Count; i++)
                {
                    var e = g.List[i];
                    float x = 20 + (i % 5) * (bw + 8), yy = y + (i / 5) * 44;
                    Btn(c, w, (ru ? e.Ru : e.En), open ? "emotes.ui play " + e.Gesture : "chat.say /shop", open ? ColPanel : "1 1 1 0.02", x, yy, bw, 36, 12, open ? ColText : ColDim);
                }
                y += Mathf.CeilToInt(g.List.Count / 5f) * 44 + 10;
            }
            CuiHelper.DestroyUi(p, Ui);
            CuiHelper.AddUi(p, c);
        }

        private static string[] O(float x, float y, float w, float h) => new[] { $"{x} {-y - h}", $"{x + w} {-y}" };
        private static void Box(CuiElementContainer c, string p, string col, float x, float y, float w, float h)
        { var o = O(x, y, w, h); c.Add(new CuiPanel { Image = { Color = col }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = o[0], OffsetMax = o[1] } }, p); }
        private static void Txt(CuiElementContainer c, string p, string t, int s, float x, float y, float w, float h, TextAnchor a, string col, bool bold)
        { var o = O(x, y, w, h); c.Add(new CuiLabel { Text = { Text = t, FontSize = s, Align = a, Color = col, Font = bold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf" }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = o[0], OffsetMax = o[1] } }, p); }
        private static void Btn(CuiElementContainer c, string p, string t, string cmd, string col, float x, float y, float w, float h, int s, string tc)
        { var o = O(x, y, w, h); c.Add(new CuiButton { Button = { Command = cmd, Color = col }, Text = { Text = t, FontSize = s, Align = TextAnchor.MiddleCenter, Color = tc, Font = "robotocondensed-regular.ttf" }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = o[0], OffsetMax = o[1] } }, p); }

        private void RegisterLang()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Title"] = "ЖЕСТЫ", ["Sub"] = "Нажми — персонаж сделает жест · /emote <название> — сразу",
                ["InShop"] = "открывается в магазине", ["Locked"] = "Группа «{0}» открывается в магазине (/shop)",
            }, this, "ru");
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Title"] = "EMOTES", ["Sub"] = "Click to play · /emote <name> for a quick one",
                ["InShop"] = "unlock in the shop", ["Locked"] = "\"{0}\" unlocks in the shop (/shop)",
            }, this);
        }

        private string Lang(ulong id) => lang.GetLanguage(id.ToString());
        private string T(ulong id, string key, params object[] a) { var s = lang.GetMessage(key, this, id.ToString()); return a.Length > 0 ? string.Format(s, a) : s; }
        private void Msg(BasePlayer p, string key, params object[] a) => SendReply(p, "<color=#f0a6b0>Жесты</color> " + T(Id(p), key, a));
    }
}
