using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    // Запись матчей ранкеда и дуэлей: позиции игроков 5 раз в секунду, выстрелы, смерти.
    // Просмотр — только для смотрящего (ddraw), без ботов на сервере: арена может быть занята.
    [Info("MatchReplay", "Claude", "1.1.1")]
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

        private const string ColRed = "0.88 0.34 0.42 1", ColBlue = "0.45 0.62 0.95 1";

        private static string Mmss(float s) => $"{(int)s / 60}:{(int)s % 60:00}";

        private void OpenList(BasePlayer player, bool mineOnly)
        {
            VInit();
            var id = Id(player);
            var list = _index.List.Where(m => !mineOnly || m.Ids.Contains(id)).Take(8).ToList();
            const float W = 720f, H = 560f;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0.03 0.02 0.02 0.5" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", UiList);
            string w = UiList + ".w";
            VRoot(c, w, UiList, VGlass, "0.5 0.5", "0.5 0.5", $"{-W / 2} {-H / 2}", $"{W / 2} {H / 2}", 22f, true);
            VHead(c, w, "play", "▶", T(id, "Title"), T(id, "Sub"), W, "replay.ui close");
            // фильтр: дорожка «Все / Мои»
            VBox(c, w, VDark, 17, 73, W - 34, 42, 12f);
            float tw = (W - 34 - 8 - 4) / 2f;
            for (int i = 0; i < 2; i++)
            {
                bool a = (i == 1) == mineOnly;
                if (a) VBox(c, w, VAcc, 21 + i * (tw + 4), 77, tw, 34, 9f);
                VTx(c, w, VNice(T(id, i == 0 ? "All" : "Mine")), 12, 21 + i * (tw + 4), 77, tw, 34, TextAnchor.MiddleCenter, a ? "1 1 1 1" : VDim);
                VHit(c, w, "replay.ui list " + (i == 0 ? "all" : "mine"), 21 + i * (tw + 4), 77, tw, 34);
            }
            float y = 127;
            VTx(c, w, T(id, "Count", list.Count).ToUpper(), 11, 17, y, 300, 20, TextAnchor.MiddleLeft, VDim, false);
            y += 26;
            if (list.Count == 0) VTx(c, w, T(id, "Empty"), 12, 17, y, W - 34, 60, TextAnchor.UpperLeft, VDim, false);
            foreach (var m in list)
            {
                VBBox(c, w, VSurf, VLine, 17, y, W - 34, 44, 12f);
                VIc(c, w, m.Title.StartsWith("Ранкед") || m.Title.StartsWith("Ranked") ? "cup" : "arena", 31, y + 14, 16, VRose);
                VTx(c, w, m.Title, 13, 57, y + 4, 280, 20);
                VTx(c, w, string.Join(", ", m.Names.Take(6)) + (m.Names.Count > 6 ? "…" : ""), 10, 57, y + 23, 300, 16, TextAnchor.MiddleLeft, VDim, false);
                VTx(c, w, m.Result ?? "", 15, 360, y, 70, 44, TextAnchor.MiddleCenter, VRose);
                VTx(c, w, m.Date + " · " + Mmss(m.Dur), 11, 440, y, 130, 44, TextAnchor.MiddleLeft, VDim, false);
                VPBtn(c, w, T(id, "Watch"), "replay.ui watch " + m.Id, "primary", W - 147, y + 7, 116, 30, 12);
                y += 50;
            }
            CuiHelper.DestroyUi(player, UiList);
            CuiHelper.AddUi(player, c);
        }

        private void DrawBar(BasePlayer player, View v)
        {
            VInit();
            var id = Id(player);
            const float W = 640f, H = 112f;
            var c = new CuiElementContainer();
            VRoot(c, UiBar, "Hud", VGlass, "0.5 0", "0.5 0", $"{-W / 2} 100", $"{W / 2} {100 + H}", 22f, true);
            // строка: точка-статус, название, время
            VBox(c, UiBar, v.Paused ? "0.75 0.72 0.71 1" : "0.831 0.122 0.235 1", 20, 21, 10, 10, 5f);
            VTx(c, UiBar, (v.Paused ? T(id, "Pause") : T(id, "Rec")).ToUpper(), 12, 38, 14, 90, 24, TextAnchor.MiddleLeft, VRose);
            VTx(c, UiBar, v.R.Title, 12, 128, 14, W - 340, 24, TextAnchor.MiddleLeft, VDim, false);
            VTx(c, UiBar, Mmss(v.T) + " / " + Mmss(v.R.Dur), 14, W - 200, 14, 180, 24, TextAnchor.MiddleRight, VText);
            // полоса времени + смерти
            VBox(c, UiBar, "1 1 1 0.14", 20, 44, W - 40, 6, 3f);
            VBox(c, UiBar, "0.831 0.122 0.235 1", 20, 44, Mathf.Max(6f, (W - 40) * Mathf.Clamp01(v.T / Mathf.Max(1f, v.R.Dur))), 6, 3f);
            foreach (var e in v.R.E.Where(e => e.K == "death"))
            {
                var tr = v.R.P.FirstOrDefault(p => p.Id == e.A);
                VBox(c, UiBar, tr != null && tr.Team == 0 ? ColRed : ColBlue, 20 + (W - 40) * e.T / Mathf.Max(1f, v.R.Dur) - 2f, 40, 4, 14, 2f);
            }
            // кнопки: ⟲10 · play · 10⟳ | скорость | выход
            float y = 64;
            VPBtn(c, UiBar, "−10", "replay.ui seek -10", "ghost", 20, y, 52, 36, 12);
            VBox(c, UiBar, VAcc, 80, y - 2, 40, 40, 20f);
            VIc(c, UiBar, v.Paused ? "play" : "stop", 90, y + 8, 20, "1 1 1 1");
            if (VIconPng("play") == null) VTx(c, UiBar, v.Paused ? "▶" : "II", 14, 80, y - 2, 40, 40, TextAnchor.MiddleCenter);
            VHit(c, UiBar, "replay.ui pause", 80, y - 2, 40, 40);
            VPBtn(c, UiBar, "+10", "replay.ui seek 10", "ghost", 128, y, 52, 36, 12);
            VBox(c, UiBar, "1 1 1 0.1", 194, y + 6, 1, 24, 0f);
            string[] sp = { "0.5", "1", "2" };
            VBox(c, UiBar, VDark, 208, y, 174, 36, 18f);
            for (int i = 0; i < 3; i++)
            {
                bool a = Mathf.Abs(v.Speed - float.Parse(sp[i], CultureInfo.InvariantCulture)) < 0.01f;
                if (a) VBox(c, UiBar, VAcc, 212 + i * 56, y + 4, 54, 28, 14f);
                VTx(c, UiBar, "×" + sp[i], 12, 212 + i * 56, y + 4, 54, 28, TextAnchor.MiddleCenter, a ? "1 1 1 1" : VDim);
                VHit(c, UiBar, "replay.ui speed " + sp[i], 212 + i * 56, y + 4, 54, 28);
            }
            VPBtn(c, UiBar, T(id, "Exit"), "replay.ui stop", "soft", W - 140, y, 120, 36, 12);
            CuiHelper.DestroyUi(player, UiBar);
            CuiHelper.AddUi(player, c);
        }

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
                ["Count"] = "Записей: {0}", ["Title"] = "Записи матчей", ["Sub"] = "Ранкед и дуэли записываются сами. Просмотр видишь только ты.", ["Mine"] = "ТОЛЬКО МОИ", ["All"] = "ВСЕ ЗАПИСИ",
                ["Empty"] = "Пока нет записей. Сыграй ранкед или дуэль — запись появится здесь.", ["Watch"] = "СМОТРЕТЬ",
                ["Rec"] = "Повтор", ["Play"] = "▶ ИГРАТЬ", ["Pause"] = "ПАУЗА", ["Exit"] = "ВЫЙТИ", ["Missing"] = "Запись не найдена",
            }, this, "ru");
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Count"] = "Replays: {0}", ["Title"] = "Match replays", ["Sub"] = "Ranked and duels are recorded automatically. Only you see the playback.", ["Mine"] = "MINE ONLY", ["All"] = "ALL",
                ["Empty"] = "No replays yet. Play ranked or a duel.", ["Watch"] = "WATCH",
                ["Rec"] = "Replay", ["Play"] = "▶ PLAY", ["Pause"] = "PAUSE", ["Exit"] = "EXIT", ["Missing"] = "Replay not found",
            }, this);
        }

        private string T(ulong id, string key, params object[] a) { var s = lang.GetMessage(key, this, id.ToString()); return a.Length > 0 ? string.Format(s, a) : s; }
        private void Msg(BasePlayer p, string key) => SendReply(p, "<color=#f0a6b0>Replay</color> " + T(Id(p), key));

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
