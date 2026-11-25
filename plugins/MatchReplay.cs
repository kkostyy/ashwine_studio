using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    // Запись матчей ранкеда и дуэлей: позиции игроков 5 раз в секунду, выстрелы, смерти.
    // Просмотр — только для смотрящего (ddraw), без ботов на сервере: арена может быть занята.
    [Info("MatchReplay", "Claude", "1.0.0")]
    [Description("Records ranked/arena matches and plays them back privately for the viewer")]
    public class MatchReplay : RustPlugin
    {
        private const string UiList = "Replay.List", UiBar = "Replay.Bar";
        private const float Rate = 0.2f;          // 5 кадров в секунду
        private const int Keep = 30;              // хранить последних записей

        #region Данные

        private class PTrack
        {
            public ulong Id; public string Name; public int Team;
            public List<float> F = new List<float>();     // t, x, y, z, yaw, hp — по 6 чисел на кадр
        }
        private class Ev { public float T; public string K; public ulong A, B; public float X, Y, Z, DX, DY, DZ; }
        private class Rec
        {
            public string Id, Title, Result, Date; public float Dur;
            public List<PTrack> P = new List<PTrack>();
            public List<Ev> E = new List<Ev>();
        }
        private class Index { public List<Meta> List = new List<Meta>(); }
        private class Meta { public string Id, Title, Result, Date; public float Dur; public List<string> Names = new List<string>(); public List<ulong> Ids = new List<ulong>(); }

        private Index _index;
        private readonly Dictionary<string, Rec> _rec = new Dictionary<string, Rec>();          // идёт запись, по ключу матча
        private readonly Dictionary<string, float> _recStart = new Dictionary<string, float>();
        private Timer _tick;

        private class View { public Rec R; public float T; public float Speed = 1f; public bool Paused; public Timer Tm; public ulong Follow; }
        private readonly Dictionary<ulong, View> _views = new Dictionary<ulong, View>();

        private void Init() { RegisterLang(); try { _index = Interface.Oxide.DataFileSystem.ReadObject<Index>("MatchReplay/index"); } catch { } if (_index == null) _index = new Index(); }
        private void OnServerInitialized() { _tick = timer.Every(Rate, Tick); }
        private void Unload()
        {
            foreach (var k in _rec.Keys.ToList()) Save(k, "—");
            foreach (var p in BasePlayer.activePlayerList) { CuiHelper.DestroyUi(p, UiList); CuiHelper.DestroyUi(p, UiBar); }
        }

        private static ulong Id(BasePlayer p) => (ulong)p.userID;
        private static float R2(float v) => Mathf.Round(v * 100f) / 100f;

        #endregion

        #region API для Ranked и Arenas

        [HookMethod("ReplayBegin")]
        public void ReplayBegin(string key, string title, List<ulong> team0, List<ulong> team1)
        {
            if (string.IsNullOrEmpty(key) || _rec.ContainsKey(key)) return;
            var r = new Rec { Id = DateTime.UtcNow.ToString("yyMMddHHmmss") + "_" + Math.Abs(key.GetHashCode() % 1000), Title = title, Date = DateTime.Now.ToString("dd.MM HH:mm") };
            foreach (var id in team0 ?? new List<ulong>()) r.P.Add(new PTrack { Id = id, Name = BasePlayer.FindByID(id)?.displayName ?? id.ToString(), Team = 0 });
            foreach (var id in team1 ?? new List<ulong>()) r.P.Add(new PTrack { Id = id, Name = BasePlayer.FindByID(id)?.displayName ?? id.ToString(), Team = 1 });
            _rec[key] = r;
            _recStart[key] = Time.realtimeSinceStartup;
        }

        [HookMethod("ReplayEnd")]
        public void ReplayEnd(string key, string result) { if (key != null && _rec.ContainsKey(key)) Save(key, result); }

        private void Save(string key, string result)
        {
            var r = _rec[key];
            _rec.Remove(key);
            r.Dur = Time.realtimeSinceStartup - _recStart[key];
            _recStart.Remove(key);
            r.Result = result;
            if (r.Dur < 5f) return;
            Interface.Oxide.DataFileSystem.WriteObject("MatchReplay/" + r.Id, r);
            _index.List.Insert(0, new Meta { Id = r.Id, Title = r.Title, Result = result, Date = r.Date, Dur = r.Dur, Names = r.P.Select(p => p.Name).ToList(), Ids = r.P.Select(p => p.Id).ToList() });
            while (_index.List.Count > Keep) _index.List.RemoveAt(_index.List.Count - 1);
            Interface.Oxide.DataFileSystem.WriteObject("MatchReplay/index", _index);
        }

        #endregion

        #region Запись

        private void Tick()
        {
            float now = Time.realtimeSinceStartup;
            foreach (var kv in _rec)
            {
                float t = R2(now - _recStart[kv.Key]);
                foreach (var tr in kv.Value.P)
                {
                    var p = BasePlayer.FindByID(tr.Id);
                    if (p == null || !p.IsConnected) continue;
                    var pos = p.transform.position;
                    tr.F.Add(t); tr.F.Add(R2(pos.x)); tr.F.Add(R2(pos.y)); tr.F.Add(R2(pos.z));
                    tr.F.Add(Mathf.Round(p.eyes.rotation.eulerAngles.y)); tr.F.Add(p.IsDead() ? 0f : Mathf.Round(p.health));
                }
            }
        }

        private Rec RecOf(ulong id, out float t)
        {
            t = 0f;
            foreach (var kv in _rec) if (kv.Value.P.Any(p => p.Id == id)) { t = R2(Time.realtimeSinceStartup - _recStart[kv.Key]); return kv.Value; }
            return null;
        }

        private void OnWeaponFired(BaseProjectile projectile, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            if (player == null || _rec.Count == 0) return;
            float t; var r = RecOf(Id(player), out t);
            if (r == null) return;
            var o = player.eyes.position; var d = player.eyes.HeadForward();
            r.E.Add(new Ev { T = t, K = "shot", A = Id(player), X = R2(o.x), Y = R2(o.y), Z = R2(o.z), DX = R2(d.x), DY = R2(d.y), DZ = R2(d.z) });
        }

        private void OnPlayerDeath(BasePlayer victim, HitInfo info)
        {
            if (victim == null || _rec.Count == 0) return;
            float t; var r = RecOf(Id(victim), out t);
            if (r == null) return;
            var pos = victim.transform.position;
            r.E.Add(new Ev { T = t, K = "death", A = Id(victim), B = info?.InitiatorPlayer != null ? Id(info.InitiatorPlayer) : 0UL, X = R2(pos.x), Y = R2(pos.y), Z = R2(pos.z) });
        }

        #endregion

        #region Просмотр

        private static bool Frame(PTrack tr, float t, out Vector3 pos, out float yaw, out float hp)
        {
            pos = Vector3.zero; yaw = 0f; hp = 0f;
            int n = tr.F.Count / 6;
            if (n == 0 || t < tr.F[0]) return false;
            int lo = 0, hi = n - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (tr.F[mid * 6] <= t) lo = mid; else hi = mid - 1; }
            int a = lo * 6, b = Mathf.Min(lo + 1, n - 1) * 6;
            float ta = tr.F[a], tb = tr.F[b];
            float k = tb > ta ? Mathf.Clamp01((t - ta) / (tb - ta)) : 0f;
            pos = new Vector3(Mathf.Lerp(tr.F[a + 1], tr.F[b + 1], k), Mathf.Lerp(tr.F[a + 2], tr.F[b + 2], k), Mathf.Lerp(tr.F[a + 3], tr.F[b + 3], k));
            yaw = tr.F[a + 4]; hp = tr.F[a + 5];
            return t - ta < 2f;
        }

        private void Watch(BasePlayer player, string recId)
        {
            Rec r = null;
            try { r = Interface.Oxide.DataFileSystem.ReadObject<Rec>("MatchReplay/" + recId); } catch { }
            if (r == null || r.P.Count == 0) { Msg(player, "Missing"); return; }
            Stop(player);
            var v = new View { R = r };
            _views[Id(player)] = v;
            CuiHelper.DestroyUi(player, UiList);
            // смотрящий — у арены, чуть выше середины всех игроков в первую секунду
            var pts = r.P.Select(tr => { Vector3 p; float y, h; return Frame(tr, tr.F.Count > 0 ? tr.F[0] : 0f, out p, out y, out h) ? p : Vector3.zero; }).Where(p => p != Vector3.zero).ToList();
            if (pts.Count > 0 && !player.isMounted)
            {
                var c = new Vector3(pts.Average(p => p.x), pts.Max(p => p.y), pts.Average(p => p.z));
                player.Teleport(c + new Vector3(0f, 6f, -14f));
            }
            v.Tm = timer.Every(0.1f, () => Step(player, v));
            DrawBar(player, v);
        }

        private void Stop(BasePlayer player)
        {
            View v;
            if (!_views.TryGetValue(Id(player), out v)) return;
            v.Tm?.Destroy();
            _views.Remove(Id(player));
            CuiHelper.DestroyUi(player, UiBar);
        }

        private int _barTick;

        private void Step(BasePlayer player, View v)
        {
            if (player == null || !player.IsConnected) { if (player != null) _views.Remove(Id(player)); v.Tm?.Destroy(); return; }
            if (!v.Paused) v.T += 0.1f * v.Speed;
            if (v.T >= v.R.Dur) { v.T = v.R.Dur; v.Paused = true; }
            var r = v.R;
            WithAdmin(player, () =>
            {
                foreach (var tr in r.P)
                {
                    Vector3 pos; float yaw, hp;
                    if (!Frame(tr, v.T, out pos, out yaw, out hp)) continue;
                    var col = tr.Team == 0 ? new Color(0.88f, 0.34f, 0.42f) : new Color(0.45f, 0.62f, 0.95f);
                    if (hp <= 0f) col = new Color(0.5f, 0.5f, 0.5f);
                    player.SendConsoleCommand("ddraw.box", 0.12f, col, pos + Vector3.up * 0.9f, 0.6f);
                    var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    player.SendConsoleCommand("ddraw.line", 0.12f, col, pos + Vector3.up * 1.5f, pos + Vector3.up * 1.5f + fwd * 1.2f);
                    player.SendConsoleCommand("ddraw.text", 0.12f, col, pos + Vector3.up * 2.3f, $"<size=13>{tr.Name}  {Mathf.RoundToInt(hp)}</size>");
                }
                foreach (var e in r.E.Where(x => x.T <= v.T && x.T > v.T - 0.1f * Mathf.Max(1f, v.Speed)))
                {
                    var o = new Vector3(e.X, e.Y, e.Z);
                    if (e.K == "shot") player.SendConsoleCommand("ddraw.line", 0.3f, new Color(0.95f, 0.85f, 0.5f), o, o + new Vector3(e.DX, e.DY, e.DZ) * 40f);
                    else if (e.K == "death") player.SendConsoleCommand("ddraw.sphere", 2.5f, Color.red, o + Vector3.up, 0.6f);
                }
            });
            if (++_barTick % 3 == 0) DrawBar(player, v);
        }

        private static void WithAdmin(BasePlayer player, Action action)
        {
            bool was = player.IsAdmin;
            if (!was) { player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true); player.SendNetworkUpdateImmediate(); }
            try { action(); }
            finally { if (!was) { player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false); player.SendNetworkUpdateImmediate(); } }
        }

        #endregion

        #region Интерфейс

        private const string ColGlass = "0.07 0.04 0.05 0.94", ColPanel = "1 1 1 0.05", ColWine = "0.639 0.086 0.184 1", ColText = "0.95 0.92 0.92 1",
            ColDim = "0.66 0.6 0.62 1", ColPink = "0.94 0.65 0.69 1", ColRed = "0.88 0.34 0.42 1", ColBlue = "0.45 0.62 0.95 1";

        private static string Mmss(float s) => $"{(int)s / 60}:{(int)s % 60:00}";

        private void OpenList(BasePlayer player, bool mineOnly)
        {
            var id = Id(player);
            var list = _index.List.Where(m => !mineOnly || m.Ids.Contains(id)).Take(9).ToList();
            const float W = 720f, H = 560f;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0 0 0 0.55" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", UiList);
            string w = UiList + ".w";
            c.Add(new CuiPanel { Image = { Color = ColGlass }, RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-W / 2} {-H / 2}", OffsetMax = $"{W / 2} {H / 2}" } }, UiList, w);
            Box(c, w, ColWine, 0, 0, W, 4);
            Txt(c, w, T(id, "Title"), 18, 20, 16, 300, 26, TextAnchor.MiddleLeft, ColText, true);
            Txt(c, w, T(id, "Sub"), 11, 20, 42, W - 260, 16, TextAnchor.MiddleLeft, ColDim, false);
            Btn(c, w, T(id, mineOnly ? "All" : "Mine"), "replay.ui list " + (mineOnly ? "all" : "mine"), "1 1 1 0.07", W - 210, 18, 150, 32, 11, ColText);
            Btn(c, w, "×", "replay.ui close", "1 1 1 0.06", W - 52, 18, 32, 32, 15, ColText);
            float y = 76;
            if (list.Count == 0) Txt(c, w, T(id, "Empty"), 12, 20, y, W - 40, 50, TextAnchor.UpperLeft, ColDim, false);
            foreach (var m in list)
            {
                Box(c, w, ColPanel, 20, y, W - 40, 46);
                Txt(c, w, m.Title, 13, 34, y + 4, 300, 20, TextAnchor.MiddleLeft, ColText, true);
                Txt(c, w, string.Join(", ", m.Names.Take(6)) + (m.Names.Count > 6 ? "…" : ""), 10, 34, y + 24, 380, 16, TextAnchor.MiddleLeft, ColDim, false);
                Txt(c, w, m.Result ?? "", 14, 340, y, 90, 46, TextAnchor.MiddleCenter, ColPink, true);
                Txt(c, w, m.Date + " · " + Mmss(m.Dur), 10, 430, y, 120, 46, TextAnchor.MiddleLeft, ColDim, false);
                Btn(c, w, T(id, "Watch"), "replay.ui watch " + m.Id, ColWine, W - 150, y + 8, 116, 30, 12, ColText);
                y += 52;
            }
            CuiHelper.DestroyUi(player, UiList);
            CuiHelper.AddUi(player, c);
        }

        private void DrawBar(BasePlayer player, View v)
        {
            var id = Id(player);
            const float W = 620f, H = 96f;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = ColGlass }, RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = $"{-W / 2} 110", OffsetMax = $"{W / 2} {110 + H}" }, CursorEnabled = true }, "Hud", UiBar);
            Box(c, UiBar, ColWine, 0, 0, 4, H);
            Txt(c, UiBar, T(id, "Rec").ToUpper() + " · " + v.R.Title, 10, 16, 8, W - 200, 14, TextAnchor.MiddleLeft, ColPink, false);
            Txt(c, UiBar, Mmss(v.T) + " / " + Mmss(v.R.Dur) + "   ×" + v.Speed.ToString("0.#", CultureInfo.InvariantCulture), 12, W - 200, 6, 184, 18, TextAnchor.MiddleRight, ColText, true);
            // полоса времени + смерти
            Box(c, UiBar, "1 1 1 0.08", 16, 30, W - 32, 6);
            Box(c, UiBar, ColWine, 16, 30, (W - 32) * Mathf.Clamp01(v.T / Mathf.Max(1f, v.R.Dur)), 6);
            foreach (var e in v.R.E.Where(e => e.K == "death"))
            {
                var tr = v.R.P.FirstOrDefault(p => p.Id == e.A);
                Box(c, UiBar, tr != null && tr.Team == 0 ? ColRed : ColBlue, 16 + (W - 32) * e.T / Mathf.Max(1f, v.R.Dur) - 1.5f, 26, 3, 14);
            }
            string[][] btns =
            {
                new[] { "−10", "replay.ui seek -10" }, new[] { v.Paused ? T(id, "Play") : T(id, "Pause"), "replay.ui pause" }, new[] { "+10", "replay.ui seek 10" },
                new[] { "×0.5", "replay.ui speed 0.5" }, new[] { "×1", "replay.ui speed 1" }, new[] { "×2", "replay.ui speed 2" }, new[] { T(id, "Exit"), "replay.ui stop" },
            };
            float bw = (W - 32 - 6 * 6) / 7f;
            for (int i = 0; i < btns.Length; i++)
                Btn(c, UiBar, btns[i][0], btns[i][1], i == 1 ? ColWine : i == 6 ? "1 1 1 0.08" : ColPanel, 16 + i * (bw + 6), 50, bw, 32, 12, ColText);
            CuiHelper.DestroyUi(player, UiBar);
            CuiHelper.AddUi(player, c);
        }

        private static string[] O(float x, float y, float w, float h) => new[] { $"{x} {-y - h}", $"{x + w} {-y}" };
        private static void Box(CuiElementContainer c, string p, string col, float x, float y, float w, float h)
        { var o = O(x, y, w, h); c.Add(new CuiPanel { Image = { Color = col }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = o[0], OffsetMax = o[1] } }, p); }
        private static void Txt(CuiElementContainer c, string p, string t, int s, float x, float y, float w, float h, TextAnchor a, string col, bool bold)
        { var o = O(x, y, w, h); c.Add(new CuiLabel { Text = { Text = t, FontSize = s, Align = a, Color = col, Font = bold ? "robotocondensed-bold.ttf" : "robotocondensed-regular.ttf" }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = o[0], OffsetMax = o[1] } }, p); }
        private static void Btn(CuiElementContainer c, string p, string t, string cmd, string col, float x, float y, float w, float h, int s, string tc)
        { var o = O(x, y, w, h); c.Add(new CuiButton { Button = { Command = cmd, Color = col }, Text = { Text = t, FontSize = s, Align = TextAnchor.MiddleCenter, Color = tc, Font = "robotocondensed-regular.ttf" }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = o[0], OffsetMax = o[1] } }, p); }

        #endregion

        #region Команды и язык

        [ChatCommand("replay")]
        private void CmdReplay(BasePlayer player, string c, string[] a) => OpenList(player, a.Length > 0 && a[0] == "mine");

        [ConsoleCommand("replay.ui")]
        private void CmdUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            View v; _views.TryGetValue(Id(player), out v);
            switch (arg.GetString(0))
            {
                case "close": CuiHelper.DestroyUi(player, UiList); break;
                case "list": OpenList(player, arg.GetString(1) == "mine"); break;
                case "watch": Watch(player, arg.GetString(1)); break;
                case "stop": Stop(player); break;
                case "pause": if (v != null) { v.Paused = !v.Paused; if (!v.Paused && v.T >= v.R.Dur) v.T = 0f; DrawBar(player, v); } break;
                case "seek": if (v != null) { v.T = Mathf.Clamp(v.T + arg.GetFloat(1), 0f, v.R.Dur); DrawBar(player, v); } break;
                case "speed": if (v != null) { v.Speed = Mathf.Clamp(arg.GetFloat(1, 1f), 0.25f, 4f); DrawBar(player, v); } break;
            }
        }

        private void RegisterLang()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Title"] = "ЗАПИСИ МАТЧЕЙ", ["Sub"] = "Ранкед и дуэли записываются сами. Просмотр видишь только ты.", ["Mine"] = "ТОЛЬКО МОИ", ["All"] = "ВСЕ ЗАПИСИ",
                ["Empty"] = "Пока нет записей. Сыграй ранкед или дуэль — запись появится здесь.", ["Watch"] = "СМОТРЕТЬ",
                ["Rec"] = "Повтор", ["Play"] = "▶ ИГРАТЬ", ["Pause"] = "ПАУЗА", ["Exit"] = "ВЫЙТИ", ["Missing"] = "Запись не найдена",
            }, this, "ru");
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Title"] = "MATCH REPLAYS", ["Sub"] = "Ranked and duels are recorded automatically. Only you see the playback.", ["Mine"] = "MINE ONLY", ["All"] = "ALL",
                ["Empty"] = "No replays yet. Play ranked or a duel.", ["Watch"] = "WATCH",
                ["Rec"] = "Replay", ["Play"] = "▶ PLAY", ["Pause"] = "PAUSE", ["Exit"] = "EXIT", ["Missing"] = "Replay not found",
            }, this);
        }

        private string T(ulong id, string key, params object[] a) { var s = lang.GetMessage(key, this, id.ToString()); return a.Length > 0 ? string.Format(s, a) : s; }
        private void Msg(BasePlayer p, string key) => SendReply(p, "<color=#f0a6b0>Replay</color> " + T(Id(p), key));

        #endregion
    }
}
