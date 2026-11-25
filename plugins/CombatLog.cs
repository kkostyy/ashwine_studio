using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    // Окно после смерти: кто убил, чем, куда попадал, сколько урона и с какой дистанции.
    [Info("CombatLog", "Claude", "1.0.0")]
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

        private const string ColGlass = "0.07 0.04 0.05 0.94", ColWine = "0.639 0.086 0.184 1", ColRow = "1 1 1 0.04", ColRowHead = "0.639 0.086 0.184 0.22",
            ColText = "0.95 0.92 0.92 1", ColDim = "0.66 0.6 0.62 1", ColPink = "0.94 0.65 0.69 1", ColGold = "0.91 0.77 0.42 1", ColGreen = "0.5 0.83 0.6 1";
        private const string Font = "robotocondensed-bold.ttf", FontReg = "robotocondensed-regular.ttf";

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

            const float W = 380f;
            int rows = Mathf.Min(8, taken.Count);
            float H = 128 + rows * 30 + 56;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = ColGlass }, RectTransform = { AnchorMin = "1 0.5", AnchorMax = "1 0.5", OffsetMin = $"{-W - 24} {-H / 2}", OffsetMax = $"-24 {H / 2}" } }, "Overlay", Ui);
            Box(c, ColWine, 0, 0, 4, H);
            Txt(c, T(id, "Title").ToUpper(), 10, 18, 12, W - 70, 14, TextAnchor.MiddleLeft, ColPink, Font);
            Btn(c, "×", "clog.close", "1 1 1 0.06", W - 40, 10, 28, 28, 15);

            // убийца
            if (killer != null && killer != victim)
            {
                var last = taken.LastOrDefault(h => h.From == kid);
                Txt(c, killer.displayName, 20, 18, 30, W - 140, 28, TextAnchor.MiddleLeft, ColText, Font);
                Txt(c, (last != null ? last.Weapon : "?") + (last != null ? " · " + Mathf.RoundToInt(last.Dist) + " м" : ""), 12, 18, 58, W - 140, 18, TextAnchor.MiddleLeft, ColDim, FontReg);
                float hp = killer.IsDead() ? 0f : killer.health;
                Txt(c, Mathf.CeilToInt(hp).ToString(), 26, W - 120, 30, 100, 30, TextAnchor.MiddleRight, hp > 50 ? ColGreen : hp > 20 ? ColGold : ColPink, Font);
                Txt(c, T(id, "HpLeft"), 10, W - 140, 60, 120, 14, TextAnchor.MiddleRight, ColDim, FontReg);
            }
            else Txt(c, T(id, "NoKiller"), 18, 18, 30, W - 40, 40, TextAnchor.MiddleLeft, ColText, Font);

            // попадания по тебе
            float y = 92;
            Txt(c, T(id, "Taken", taken.Count, Mathf.RoundToInt(taken.Sum(h => h.Dmg))), 11, 18, y, W - 36, 18, TextAnchor.MiddleLeft, ColText, Font);
            y += 24;
            foreach (var h in taken.Skip(Mathf.Max(0, taken.Count - rows)))
            {
                Box(c, h.Zone == "Head" ? ColRowHead : ColRow, 14, y, W - 28, 26);
                Txt(c, "−" + (now - h.T).ToString("0.0", CultureInfo.InvariantCulture) + "с", 10, 22, y, 44, 26, TextAnchor.MiddleLeft, ColDim, FontReg);
                Txt(c, h.FromName, 11, 66, y, 110, 26, TextAnchor.MiddleLeft, ColText, FontReg);
                Txt(c, h.Weapon, 10, 176, y, 80, 26, TextAnchor.MiddleLeft, ColDim, FontReg);
                Txt(c, T(id, "Z_" + h.Zone), 10, 256, y, 50, 26, TextAnchor.MiddleLeft, h.Zone == "Head" ? ColPink : ColDim, FontReg);
                Txt(c, Mathf.RoundToInt(h.Dmg).ToString(), 12, W - 96, y, 40, 26, TextAnchor.MiddleRight, ColText, Font);
                Txt(c, Mathf.RoundToInt(h.Dist) + "м", 10, W - 56, y, 36, 26, TextAnchor.MiddleRight, ColDim, FontReg);
                y += 30;
            }

            // твой урон по убийце
            y = H - 50;
            Box(c, "1 1 1 0.05", 14, y, W - 28, 36);
            if (kid != 0)
                Txt(c, T(id, "Dealt", Mathf.RoundToInt(dealt.Sum(h => h.Dmg)), dealt.Count, dealt.Count(h => h.Zone == "Head")), 11, 24, y, W - 48, 36, TextAnchor.MiddleLeft, dealt.Count > 0 ? ColText : ColDim, FontReg);
            else Txt(c, T(id, "Hint"), 10, 24, y, W - 48, 36, TextAnchor.MiddleLeft, ColDim, FontReg);

            CuiHelper.DestroyUi(victim, Ui);
            CuiHelper.AddUi(victim, c);
            Timer old;
            if (_close.TryGetValue(id, out old)) old?.Destroy();
            _close[id] = timer.Once(ShowFor, () => { if (victim != null) CuiHelper.DestroyUi(victim, Ui); });
        }

        private static string[] O(float x, float y, float w, float h) => new[] { $"{x} {-y - h}", $"{x + w} {-y}" };
        private static void Box(CuiElementContainer c, string col, float x, float y, float w, float h)
        { var o = O(x, y, w, h); c.Add(new CuiPanel { Image = { Color = col }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = o[0], OffsetMax = o[1] } }, Ui); }
        private static void Txt(CuiElementContainer c, string t, int s, float x, float y, float w, float h, TextAnchor a, string col, string font)
        { var o = O(x, y, w, h); c.Add(new CuiLabel { Text = { Text = t, FontSize = s, Align = a, Color = col, Font = font }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = o[0], OffsetMax = o[1] } }, Ui); }
        private static void Btn(CuiElementContainer c, string t, string cmd, string col, float x, float y, float w, float h, int s)
        { var o = O(x, y, w, h); c.Add(new CuiButton { Button = { Command = cmd, Color = col }, Text = { Text = t, FontSize = s, Align = TextAnchor.MiddleCenter, Color = ColText }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = o[0], OffsetMax = o[1] } }, Ui); }

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
    }
}
