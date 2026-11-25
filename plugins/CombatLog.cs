using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    // Окно после смерти: кто убил, чем, куда попадал, сколько урона и с какой дистанции.
    [Info("CombatLog", "Claude", "1.1.0")]
    [Description("Death recap window: hits taken in the last seconds, killer HP, distance")]
    public class CombatLog : RustPlugin
    {
        private const string Ui = "CombatLog.Main";
        private const float Window = 12f;      // сколько секунд истории показывать
        private const float ShowFor = 9f;

        private class Hit
        {
            public float T; public ulong From, To; public string FromName, ToName, Weapon, Zone;
            public float Dmg, Dist; public bool Head;
        }

        private readonly Dictionary<ulong, List<Hit>> _hits = new Dictionary<ulong, List<Hit>>();     // по жертве
        private readonly HashSet<ulong> _off = new HashSet<ulong>();
        private readonly Dictionary<ulong, Timer> _close = new Dictionary<ulong, Timer>();

        private static ulong Id(BasePlayer p) => (ulong)p.userID;

        private void Init() => RegisterLang();
        private void Unload() { foreach (var p in BasePlayer.activePlayerList) CuiHelper.DestroyUi(p, Ui); }

        #region Сбор урона

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var victim = entity as BasePlayer;
            var attacker = info?.InitiatorPlayer;
            if (victim == null || attacker == null || attacker == victim || info == null) return;
            float before = victim.health;
            var h = new Hit
            {
                T = Time.realtimeSinceStartup, From = Id(attacker), To = Id(victim),
                FromName = attacker.displayName, ToName = victim.displayName,
                Weapon = WeaponName(info), Zone = ZoneKey(info.boneArea), Head = info.isHeadshot,
                Dist = Vector3.Distance(attacker.transform.position, victim.transform.position),
            };
            // реальный урон — после брони: смотрим ХП на следующем кадре
            NextTick(() =>
            {
                h.Dmg = victim == null || victim.IsDead() ? before : Mathf.Max(0f, before - victim.health);
                if (h.Dmg <= 0.05f) return;
                List<Hit> list;
                if (!_hits.TryGetValue(h.To, out list)) _hits[h.To] = list = new List<Hit>();
                list.Add(h);
                list.RemoveAll(x => h.T - x.T > Window);
                // свой исходящий урон тоже храним — для «ты нанёс ему»
                if (!_hits.TryGetValue(h.From, out list)) _hits[h.From] = list = new List<Hit>();
                list.RemoveAll(x => h.T - x.T > Window);
            });
        }

        private static string WeaponName(HitInfo info)
        {
            try
            {
                var item = info.Weapon != null ? info.Weapon.GetItem() : null;
                if (item?.info != null) return item.info.displayName.english;
                if (info.WeaponPrefab != null) return info.WeaponPrefab.ShortPrefabName;
            }
            catch { }
            return "?";
        }

        private static string ZoneKey(HitArea a) =>
            (a & HitArea.Head) != 0 ? "Head" : (a & (HitArea.Chest | HitArea.Stomach)) != 0 ? "Body" : (a & (HitArea.Arm | HitArea.Hand)) != 0 ? "Arms" : (a & (HitArea.Leg | HitArea.Foot)) != 0 ? "Legs" : "Body";

        private void OnPlayerDeath(BasePlayer victim, HitInfo info)
        {
            if (victim == null || !victim.IsConnected || _off.Contains(Id(victim))) return;
            var vid = Id(victim);
            var killer = info?.InitiatorPlayer;
            NextTick(() => { if (victim != null && victim.IsConnected) Show(victim, killer); });
        }

        private void OnPlayerRespawned(BasePlayer p) { if (p != null) { CuiHelper.DestroyUi(p, Ui); _hits.Remove(Id(p)); } }
        private void OnPlayerDisconnected(BasePlayer p) { if (p != null) { _hits.Remove(Id(p)); _off.Remove(Id(p)); } }

        #endregion

        #region Окно

        private void Show(BasePlayer victim, BasePlayer killer)
        {
            var id = Id(victim);
            float now = Time.realtimeSinceStartup;
            List<Hit> all;
            _hits.TryGetValue(id, out all);
            var taken = (all ?? new List<Hit>()).Where(h => h.To == id && now - h.T <= Window).OrderBy(h => h.T).ToList();
            if (taken.Count == 0 && killer == null) return;
            var kid = killer != null ? Id(killer) : 0UL;
            List<Hit> kh;
            var dealt = kid != 0 && _hits.TryGetValue(kid, out kh) ? kh.Where(h => h.From == id && h.To == kid && now - h.T <= Window).ToList() : new List<Hit>();

            VInit();
            const float W = 400f;
            int rows = Mathf.Min(8, taken.Count);
            float H = 150 + 30 + rows * 34 + 62;
            var c = new CuiElementContainer();
            VRoot(c, Ui, "Overlay", VGlass, "1 0.5", "1 0.5", $"{-W - 24} {-H / 2}", $"-24 {H / 2}", 22f, false);
            string sub = T(id, "NoKiller");
            var last = killer != null ? taken.LastOrDefault(h => h.From == kid) : null;
            if (killer != null && killer != victim) sub = (last != null ? last.Weapon : "?") + (last != null ? " · " + Mathf.RoundToInt(last.Dist) + " м" : "");
            VHead(c, Ui, "aim", "!", T(id, "Title"), sub, W, "clog.close");

            // убийца: карточка с ХП
            float y = 73;
            if (killer != null && killer != victim)
            {
                float hp = killer.IsDead() ? 0f : killer.health;
                string hc = hp > 50 ? VGreen : hp > 20 ? VGold : VRose;
                VBBox(c, Ui, VSoft, VSoftLine, 17, y, W - 34, 60, 14f);
                VTx(c, Ui, killer.displayName, 16, 31, y + 8, W - 160, 24);
                VTx(c, Ui, T(id, "HpLeft"), 11, 31, y + 32, W - 160, 16, TextAnchor.MiddleLeft, VDim, false);
                VTx(c, Ui, Mathf.CeilToInt(hp).ToString(), 26, W - 140, y, 104, 46, TextAnchor.MiddleRight, hc);
                VBox(c, Ui, "1 1 1 0.08", W - 140, y + 46, 104, 4, 2f);
                if (hp > 0) VBox(c, Ui, hc, W - 140, y + 46, Mathf.Max(4f, 104f * Mathf.Clamp01(hp / 100f)), 4, 2f);
            }
            else VBBox(c, Ui, VSurf, VLine, 17, y, W - 34, 60, 14f);
            y += 72;

            // попадания по тебе
            VTx(c, Ui, T(id, "Taken", taken.Count, Mathf.RoundToInt(taken.Sum(h => h.Dmg))).ToUpper(), 11, 17, y, W - 34, 22, TextAnchor.MiddleLeft, VDim, false);
            y += 30;
            foreach (var h in taken.Skip(Mathf.Max(0, taken.Count - rows)))
            {
                bool head = h.Zone == "Head";
                VBBox(c, Ui, head ? "0.639 0.086 0.184 0.16" : VSurf, head ? "0.941 0.651 0.694 0.22" : VLine, 17, y, W - 34, 30, 10f);
                VTx(c, Ui, "−" + (now - h.T).ToString("0.0", CultureInfo.InvariantCulture) + "с", 10, 27, y, 44, 30, TextAnchor.MiddleLeft, VDim, false);
                VTx(c, Ui, h.FromName, 12, 71, y, 112, 30, TextAnchor.MiddleLeft, VText);
                VTx(c, Ui, h.Weapon, 10, 183, y, 82, 30, TextAnchor.MiddleLeft, VDim, false);
                VTx(c, Ui, T(id, "Z_" + h.Zone), 10, 265, y, 50, 30, TextAnchor.MiddleLeft, head ? VRose : VDim, false);
                VTx(c, Ui, Mathf.RoundToInt(h.Dmg).ToString(), 13, W - 104, y, 40, 30, TextAnchor.MiddleRight, VText);
                VTx(c, Ui, Mathf.RoundToInt(h.Dist) + "м", 10, W - 62, y, 36, 30, TextAnchor.MiddleRight, VDim, false);
                y += 34;
            }

            // твой урон по убийце
            y = H - 58;
            VBox(c, Ui, VDark, 17, y, W - 34, 40, 14f);
            if (kid != 0)
                VTx(c, Ui, T(id, "Dealt", Mathf.RoundToInt(dealt.Sum(h => h.Dmg)), dealt.Count, dealt.Count(h => h.Zone == "Head")), 12, 31, y, W - 62, 40, TextAnchor.MiddleLeft, dealt.Count > 0 ? VText : VDim, false);
            else VTx(c, Ui, T(id, "Hint"), 11, 31, y, W - 62, 40, TextAnchor.MiddleLeft, VDim, false);

            CuiHelper.DestroyUi(victim, Ui);
            CuiHelper.AddUi(victim, c);
            Timer old;
            if (_close.TryGetValue(id, out old)) old?.Destroy();
            _close[id] = timer.Once(ShowFor, () => { if (victim != null) CuiHelper.DestroyUi(victim, Ui); });
        }

        #endregion

        #region Команды и язык

        [ConsoleCommand("clog.close")]
        private void CmdClose(ConsoleSystem.Arg arg) { var p = arg.Player(); if (p != null) CuiHelper.DestroyUi(p, Ui); }

        [ChatCommand("clog")]
        private void CmdToggle(BasePlayer p, string c, string[] a)
        {
            var id = Id(p);
            if (!_off.Remove(id)) _off.Add(id);
            SendReply(p, "<color=#f0a6b0>Combat Log</color> " + T(id, _off.Contains(id) ? "Off" : "On"));
        }

        private void RegisterLang()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Title"] = "Кто тебя убил", ["HpLeft"] = "ХП у убийцы", ["NoKiller"] = "Ты погиб",
                ["Taken"] = "По тебе: {0} попаданий · {1} урона", ["Dealt"] = "Ты нанёс ему: {0} урона · {1} попаданий · {2} в голову",
                ["Hint"] = "Окно закроется само · /clog — выключить", ["Z_Head"] = "голова", ["Z_Body"] = "тело", ["Z_Arms"] = "руки", ["Z_Legs"] = "ноги",
                ["On"] = "окно после смерти включено", ["Off"] = "окно после смерти выключено",
            }, this, "ru");
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Title"] = "Who killed you", ["HpLeft"] = "killer HP", ["NoKiller"] = "You died",
                ["Taken"] = "Taken: {0} hits · {1} damage", ["Dealt"] = "You dealt: {0} damage · {1} hits · {2} headshots",
                ["Hint"] = "Closes by itself · /clog to turn off", ["Z_Head"] = "head", ["Z_Body"] = "body", ["Z_Arms"] = "arms", ["Z_Legs"] = "legs",
                ["On"] = "death recap on", ["Off"] = "death recap off",
            }, this);
        }

        private string T(ulong id, string key, params object[] a) { var s = lang.GetMessage(key, this, id.ToString()); return a.Length > 0 ? string.Format(s, a) : s; }

        #endregion
    
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
