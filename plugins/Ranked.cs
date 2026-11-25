// =====================================================================================
//  Ranked 1.0 — рейтинговые матчи для хаб-сервера Rust (Oxide / Carbon)
// =====================================================================================
//  Четыре ранкеда: «Калаши» (АК и одинаковый сет), «Разные ганы» (каждый раунд у всех
//  одна и та же случайная пушка и сет), «Тир 2» (Томпсон/MP5/SAR, роадсайн) и «Примитив». Форматы 1v1, 2v2, 3v3, 5v5, матч до 5 побед.
//  Очередь соло или командой Rust (team), подбор по MMR с расширением окна,
//  «Матч найден» с подтверждением (можно ждать, играя в другом режиме),
//  60 секунд на перезаход при вылете, ранги Бронза → Легенда, таблица лидеров,
//  месячные сезоны с наградой топ-3 и мягким сбросом.
//
//  Работает с Hub (режимы ranked_ak / ranked_mix), без него тоже можно.
//  Права:  ranked.admin — арены и сезоны
//  Чат:    /ranked — очередь · /ranked leave · /top — таблица лидеров
//  Админ:  /rankedadmin — команды
// =====================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Ranked", "Claude", "1.2.0")]
    [Description("Рейтинговые матчи 1v1–5v5: два ранкеда, MMR, ранги, сезоны, таблица лидеров")]
    public class Ranked : RustPlugin
    {

        // ---------- размер HUD: S / M / L выбирает игрок в профиле хаба (/hud) ----------
        private static readonly HashSet<string> HudRootLayers = new HashSet<string> { "Hud", "Hud.Menu", "Under", "Overlay", "Overall" };

        private void Ach(ulong id, string stat, int n = 1) { if (Hub != null && Hub.IsLoaded) Hub.Call("AchAdd", id, stat, n); }
        private void AchMax(ulong id, string stat, int v) { if (Hub != null && Hub.IsLoaded) Hub.Call("AchMax", id, stat, v); }

        private float HudScale(BasePlayer p)
        {
            if (p == null || Hub == null || !Hub.IsLoaded) return 1f;
            var r = Hub.Call("GetHudScale", (ulong)p.userID);
            return r is float ? (float)r : 1f;
        }

        private static string ScaleOffset(string v, float k)
        {
            if (string.IsNullOrEmpty(v)) return v;
            var parts = v.Split(' ');
            float x, y;
            if (parts.Length != 2
                || !float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)
                || !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y)) return v;
            return (x * k).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " " + (y * k).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }

        // уменьшает только HUD (окна без курсора); меню с курсором не трогает
        private void ScaleHud(BasePlayer p, CuiElementContainer c)
        {
            float k = HudScale(p);
            if (Mathf.Abs(k - 1f) < 0.01f) return;
            var scaled = new HashSet<string>();
            foreach (var e in c)
            {
                bool root = HudRootLayers.Contains(e.Parent ?? "");
                if (root)
                {
                    if (e.Components.Any(x => x is CuiNeedsCursorComponent || x is CuiNeedsKeyboardComponent)) continue;
                }
                else if (!scaled.Contains(e.Parent ?? "")) continue;
                if (e.Name != null) scaled.Add(e.Name);
                foreach (var comp in e.Components)
                {
                    var rt = comp as CuiRectTransformComponent;
                    if (rt != null) { rt.OffsetMin = ScaleOffset(rt.OffsetMin, k); rt.OffsetMax = ScaleOffset(rt.OffsetMax, k); continue; }
                    var tx = comp as CuiTextComponent;
                    if (tx != null) tx.FontSize = Mathf.Max(7, Mathf.RoundToInt(tx.FontSize * k));
                }
            }
        }

        private void AddUiScaled(BasePlayer p, CuiElementContainer c)
        {
            ScaleHud(p, c);
            CuiHelper.AddUi(p, c);
        }
        [PluginReference] private Plugin Hub;

        #region Константы

        private const string PermAdmin = "ranked.admin";
        private const string UiMain = "Rk.Main";
        private const string UiHud = "Rk.Hud";
        private const string UiTeams = "Rk.Teams";
        private const string UiCenter = "Rk.Center";
        private const string UiPopup = "Rk.Popup";
        private const string UiToast = "Rk.Toast";
        private const string UiPause = "Rk.Pause";

        private const string ColBg = "0.07 0.065 0.065 0.97";
        private const string ColHud = "0.07 0.065 0.065 0.86";
        private const string ColPanel = "0.105 0.097 0.097 1";
        private const string ColRow = "0.155 0.145 0.145 1";
        private const string ColBtn = "0.215 0.20 0.20 1";
        private const string ColWine = "0.48 0.07 0.14 1";
        private const string ColWineSoft = "0.48 0.07 0.14 0.35";
        private const string ColText = "0.93 0.90 0.89 1";
        private const string ColDim = "0.58 0.53 0.52 1";
        private const string ColRose = "0.94 0.65 0.69 1";
        private const string ColGold = "0.91 0.77 0.42 1";
        private const string ColGreen = "0.50 0.83 0.60 1";
        private const string ColRed = "0.88 0.34 0.42 1";
        private const string ColBlue = "0.44 0.71 1 1";
        private const string Font = "robotocondensed-bold.ttf";
        private const string FontReg = "robotocondensed-regular.ttf";

        private static readonly int[] Formats = { 1, 2, 3, 5 };
        private static readonly string[] Ladders = { "ak", "mix", "t2", "prim" };

        #endregion

        #region Конфиг

        private class RankDef
        {
            [JsonProperty("Название")] public string Name;
            [JsonProperty("Название (EN)")] public string NameEn;
            [JsonProperty("С MMR")] public int From;
            [JsonProperty("Цвет")] public string Color;
        }

        private class KitDef
        {
            [JsonProperty("Название")] public string Name;
            [JsonProperty("Оружие")] public string Weapon;
            [JsonProperty("Модули")] public List<string> Mods = new List<string>();
            [JsonProperty("Патроны")] public string Ammo;
            [JsonProperty("Кол-во патронов")] public int AmmoAmount = 128;
            [JsonProperty("Шанс (вес)")] public int Weight = 10;
        }

        private class WearDef
        {
            [JsonProperty("Название")] public string Name;
            [JsonProperty("Одежда")] public List<string> Wear = new List<string>();
            [JsonProperty("Шанс (вес)")] public int Weight = 10;
        }

        private class Configuration
        {
            [JsonProperty("Побед в матче")] public int RoundsToWin = 5;
            [JsonProperty("Отсчёт перед раундом (сек)")] public int Countdown = 3;
            [JsonProperty("Пауза между раундами (сек)")] public float Between = 3f;
            [JsonProperty("Перезаход при вылете (сек)")] public int ReconnectSeconds = 60;
            [JsonProperty("Подтвердить матч за (сек)")] public int AcceptSeconds = 10;
            [JsonProperty("Начальный MMR")] public int StartMmr = 1000;
            [JsonProperty("K-фактор")] public int K = 32;
            [JsonProperty("K-фактор первых 10 матчей")] public int KNew = 48;
            [JsonProperty("Окно подбора: старт (±MMR)")] public int WindowStart = 50;
            [JsonProperty("Окно подбора: +MMR каждые 15 с")] public int WindowStep = 50;
            [JsonProperty("Окно подбора: максимум")] public int WindowMax = 400;
            [JsonProperty("Шприцов в раунде")] public int Syringes = 2;
            [JsonProperty("Бинтов в раунде")] public int Bandages = 3;
            [JsonProperty("Монеты: победа")] public int CoinsWin = 40;
            [JsonProperty("Монеты: поражение")] public int CoinsLoss = 5;
            [JsonProperty("Сезон: длина (дней)")] public int SeasonDays = 30;
            [JsonProperty("Сезон: мягкий сброс (доля, 0.5 = половина отрыва от старта)")] public float SoftReset = 0.5f;
            [JsonProperty("Сезон: монеты топ-1 / топ-2 / топ-3")] public List<int> SeasonCoins = new List<int> { 3000, 2000, 1000 };
            [JsonProperty("Сезон: команды наградам ({id} {place} {season} {ladder})")] public List<string> SeasonCommands = new List<string>();
            [JsonProperty("Режим Hub: калаши")] public string HubAk = "ranked_ak";
            [JsonProperty("Режим Hub: разные ганы")] public string HubMix = "ranked_mix";
            [JsonProperty("Режим Hub: тир 2")] public string HubT2 = "ranked_t2";
            [JsonProperty("Режим Hub: примитив")] public string HubPrim = "ranked_prim";

            [JsonProperty("Анти-смурф: минимум часов на сервере для ранкеда (0 = нет)")] public float MinHours = 2f;
            [JsonProperty("Анти-смурф: не сводить соперниками игроков с одного IP")] public bool BlockSameIp = true;
            [JsonProperty("Анти-смурф: ключ Steam Web API (пусто = не проверять возраст аккаунта)")] public string SteamApiKey = "";
            [JsonProperty("Анти-смурф: минимальный возраст Steam-аккаунта (дней)")] public int MinAccountDays = 30;
            [JsonProperty("Анти-смурф: пускать закрытые профили Steam")] public bool AllowPrivateProfiles = true;
            [JsonProperty("Ступени внутри ранга (III → II → I, 0 = без ступеней)")] public int Divisions = 3;
            [JsonProperty("Наблюдать за тиммейтом после смерти")] public bool SpectateOnDeath = true;
            [JsonProperty("Скруглённые углы интерфейса")] public bool RoundedCorners = true;

            [JsonProperty("Ранги")] public List<RankDef> Ranks = new List<RankDef>
            {
                new RankDef { Name = "Бронза", NameEn = "Bronze", From = 0, Color = "0.69 0.48 0.31 1" },
                new RankDef { Name = "Серебро", NameEn = "Silver", From = 1100, Color = "0.79 0.80 0.83 1" },
                new RankDef { Name = "Золото", NameEn = "Gold", From = 1300, Color = "0.91 0.77 0.42 1" },
                new RankDef { Name = "Платина", NameEn = "Platinum", From = 1500, Color = "0.50 0.85 0.82 1" },
                new RankDef { Name = "Алмаз", NameEn = "Diamond", From = 1700, Color = "0.56 0.72 1 1" },
                new RankDef { Name = "Мастер", NameEn = "Master", From = 1900, Color = "0.77 0.55 1 1" },
                new RankDef { Name = "Легенда", NameEn = "Legend", From = 2200, Color = "1 0.42 0.50 1" },
            };

            [JsonProperty("Калаши: оружие")] public KitDef AkKit = new KitDef { Name = "AK + голо", Weapon = "rifle.ak", Mods = new List<string> { "weapon.mod.holosight" }, Ammo = "ammo.rifle", AmmoAmount = 128 };
            [JsonProperty("Калаши: одежда")] public List<string> AkWear = new List<string> { "metal.facemask", "metal.plate.torso", "roadsign.kilt", "hoodie", "pants", "shoes.boots", "tactical.gloves" };

            [JsonProperty("Разные ганы: оружие")] public List<KitDef> MixGuns = new List<KitDef>
            {
                new KitDef { Name = "AK + голо", Weapon = "rifle.ak", Mods = new List<string> { "weapon.mod.holosight" }, Ammo = "ammo.rifle", Weight = 12 },
                new KitDef { Name = "LR-300 + голо", Weapon = "rifle.lr300", Mods = new List<string> { "weapon.mod.holosight" }, Ammo = "ammo.rifle", Weight = 12 },
                new KitDef { Name = "MP5", Weapon = "smg.mp5", Ammo = "ammo.pistol", Weight = 10 },
                new KitDef { Name = "Томпсон", Weapon = "smg.thompson", Ammo = "ammo.pistol", Weight = 10 },
                new KitDef { Name = "СМГ", Weapon = "smg.2", Ammo = "ammo.pistol", Weight = 8 },
                new KitDef { Name = "SAR", Weapon = "rifle.semiauto", Ammo = "ammo.rifle", Weight = 8 },
                new KitDef { Name = "Болт + 8x", Weapon = "rifle.bolt", Mods = new List<string> { "weapon.mod.small.scope" }, Ammo = "ammo.rifle", AmmoAmount = 40, Weight = 6 },
                new KitDef { Name = "M249", Weapon = "lmg.m249", Ammo = "ammo.rifle", AmmoAmount = 200, Weight = 4 },
                new KitDef { Name = "Питон", Weapon = "pistol.python", Ammo = "ammo.pistol", AmmoAmount = 60, Weight = 6 },
                new KitDef { Name = "Помпа", Weapon = "shotgun.pump", Ammo = "ammo.shotgun", AmmoAmount = 32, Weight = 5 },
            };
            [JsonProperty("Тир 2: оружие (случайное каждый раунд)")] public List<KitDef> T2Guns = new List<KitDef>
            {
                new KitDef { Name = "Томпсон", Weapon = "smg.thompson", Mods = new List<string> { "weapon.mod.holosight" }, Ammo = "ammo.pistol", Weight = 10 },
                new KitDef { Name = "MP5", Weapon = "smg.mp5", Ammo = "ammo.pistol", Weight = 10 },
                new KitDef { Name = "SAR", Weapon = "rifle.semiauto", Ammo = "ammo.rifle", Weight = 10 },
                new KitDef { Name = "Кастом СМГ", Weapon = "smg.2", Ammo = "ammo.pistol", Weight = 6 },
                new KitDef { Name = "Питон", Weapon = "pistol.python", Ammo = "ammo.pistol", AmmoAmount = 60, Weight = 4 },
            };
            [JsonProperty("Тир 2: одежда")] public List<string> T2Wear = new List<string> { "coffeecan.helmet", "roadsign.jacket", "roadsign.kilt", "hoodie", "pants", "shoes.boots" };
            [JsonProperty("Примитив: набор (выдаётся весь)")] public List<KitDef> PrimKit = new List<KitDef>
            {
                new KitDef { Name = "Лук", Weapon = "bow.hunting", Ammo = "arrow.wooden", AmmoAmount = 40 },
                new KitDef { Name = "Арбалет", Weapon = "crossbow", Ammo = "arrow.wooden", AmmoAmount = 20 },
                new KitDef { Name = "Eoka", Weapon = "pistol.eoka", Ammo = "ammo.handmade.shell", AmmoAmount = 24 },
                new KitDef { Name = "Револьвер", Weapon = "pistol.revolver", Ammo = "ammo.pistol", AmmoAmount = 48 },
                new KitDef { Name = "Копьё", Weapon = "spear.wooden" },
            };
            [JsonProperty("Примитив: одежда")] public List<string> PrimWear = new List<string> { "burlap.headwrap", "burlap.shirt", "burlap.trousers", "burlap.shoes" };

            [JsonProperty("Разные ганы: сеты")] public List<WearDef> MixWear = new List<WearDef>
            {
                new WearDef { Name = "без брони", Wear = new List<string> { "hoodie", "pants", "shoes.boots" }, Weight = 6 },
                new WearDef { Name = "деревянный", Wear = new List<string> { "wood.armor.helmet", "wood.armor.jacket", "wood.armor.pants", "hoodie", "shoes.boots" }, Weight = 10 },
                new WearDef { Name = "роадсайн", Wear = new List<string> { "coffeecan.helmet", "roadsign.jacket", "roadsign.kilt", "hoodie", "pants", "shoes.boots" }, Weight = 12 },
                new WearDef { Name = "МВК", Wear = new List<string> { "metal.facemask", "metal.plate.torso", "roadsign.kilt", "hoodie", "pants", "shoes.boots" }, Weight = 10 },
                new WearDef { Name = "кевлар", Wear = new List<string> { "metal.facemask", "heavy.plate.jacket", "heavy.plate.pants", "shoes.boots" }, Weight = 5 },
            };
        }

        private Configuration _config;

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                Config.Settings.ObjectCreationHandling = ObjectCreationHandling.Replace; _config = Config.ReadObject<Configuration>();
                if (_config == null || _config.Ranks == null || _config.Ranks.Count == 0) throw new Exception();
            }
            catch
            {
                PrintWarning("Конфиг повреждён — создаю новый");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        #endregion

        #region Данные

        private class Pt
        {
            public float X, Y, Z, Yaw;
            [JsonIgnore] public Vector3 V => new Vector3(X, Y, Z);
            public static Pt From(Vector3 v, float yaw) => new Pt { X = v.x, Y = v.y, Z = v.z, Yaw = yaw };
        }

        private class ArenaDef
        {
            public string Id, Name;
            public List<int> Formats = new List<int> { 1, 2, 3, 5 };
            public List<Pt> A = new List<Pt>(), B = new List<Pt>();
            public Pt Center;
            public float Radius = 60f;
            public bool Enabled = true;
        }

        private class Rating
        {
            public int Mmr;
            public int Wins, Losses, Matches;
            public int Peak;
        }

        private class SeasonResult { public int Season; public string Ladder; public int Format; public int Mmr; public string Rank; public int Place; }

        private class MatchLog { public string Ladder; public int Format; public bool Win; public string Score; public int Delta; public string Date; }

        private class PData
        {
            public string Name;
            public Dictionary<string, Rating> R = new Dictionary<string, Rating>();     // ключ: "ak_1", "mix_2"…
            public List<MatchLog> History = new List<MatchLog>();
            public List<SeasonResult> Seasons = new List<SeasonResult>();
            public List<string> Titles = new List<string>();
            public int Kills, Deaths, HsKills;
        }

        private class StoredData
        {
            public Dictionary<string, ArenaDef> Arenas = new Dictionary<string, ArenaDef>();
            public int Season = 1;
            public string SeasonEnds;
            public Dictionary<ulong, PData> Players = new Dictionary<ulong, PData>();
        }

        private StoredData _data;
        private bool _dirty;

        private void LoadData()
        {
            try { Interface.Oxide.DataFileSystem.GetFile("Ranked").Settings.ObjectCreationHandling = ObjectCreationHandling.Replace; _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("Ranked"); } catch { _data = null; }
            if (_data == null) _data = new StoredData();
            if (_data.Arenas == null) _data.Arenas = new Dictionary<string, ArenaDef>();
            if (_data.Players == null) _data.Players = new Dictionary<ulong, PData>();
            if (string.IsNullOrEmpty(_data.SeasonEnds)) _data.SeasonEnds = DateTime.UtcNow.AddDays(_config.SeasonDays).ToString("yyyy-MM-dd HH:mm");
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("Ranked", _data);
            _dirty = false;
        }

        private PData D(ulong id, string name = null)
        {
            PData d;
            if (!_data.Players.TryGetValue(id, out d)) _data.Players[id] = d = new PData();
            if (d.R == null) d.R = new Dictionary<string, Rating>();
            if (d.History == null) d.History = new List<MatchLog>();
            if (d.Seasons == null) d.Seasons = new List<SeasonResult>();
            if (d.Titles == null) d.Titles = new List<string>();
            if (name != null) d.Name = name;
            return d;
        }

        private static string Key(string ladder, int format) => ladder + "_" + format;

        private Rating R(ulong id, string ladder, int format)
        {
            var d = D(id);
            Rating r;
            if (!d.R.TryGetValue(Key(ladder, format), out r)) d.R[Key(ladder, format)] = r = new Rating { Mmr = _config.StartMmr, Peak = _config.StartMmr };
            return r;
        }

        private RankDef RankOf(int mmr) => _config.Ranks.Where(r => mmr >= r.From).OrderByDescending(r => r.From).FirstOrDefault() ?? _config.Ranks[0];

        private RankDef NextRank(int mmr) => _config.Ranks.Where(r => r.From > mmr).OrderBy(r => r.From).FirstOrDefault();

        private string RankName(RankDef r, ulong id) => IsEn(id) && !string.IsNullOrEmpty(r.NameEn) ? r.NameEn : r.Name;

        // ступень внутри ранга: III (низ) → II → I (верх); у последнего ранга ступеней нет
        private string Div(int mmr)
        {
            if (_config.Divisions <= 1) return "";
            var rank = RankOf(mmr);
            var next = NextRank(mmr);
            if (next == null) return "";
            int span = Mathf.Max(1, next.From - rank.From);
            int step = Mathf.Clamp((mmr - rank.From) * _config.Divisions / span, 0, _config.Divisions - 1);
            int div = _config.Divisions - step;
            string[] roman = { "", "I", "II", "III", "IV", "V" };
            return " " + (div < roman.Length ? roman[div] : div.ToString());
        }

        private string RankLabel(int mmr, ulong id) => RankName(RankOf(mmr), id) + Div(mmr);

        private bool IsEn(ulong id) => lang.GetLanguage(id.ToString()) != "ru";

        #endregion

        #region Очередь и матчи

        private class Entry
        {
            public List<ulong> Members = new List<ulong>();
            public string Ladder;
            public int Format;
            public float Since;
            public int Avg;
        }

        private class Match
        {
            public string Id;
            public string Ladder;
            public int Format;
            public ArenaDef Arena;
            public List<ulong>[] Teams = { new List<ulong>(), new List<ulong>() };
            public int[] Score = new int[2];
            public int Round;
            public string State = "accept";      // accept | countdown | live | between | paused | ended
            public HashSet<ulong> Accepted = new HashSet<ulong>();
            public float AcceptUntil;
            public List<Entry> Entries = new List<Entry>();
            public HashSet<ulong> Alive = new HashSet<ulong>();
            public KitDef Gun;
            public WearDef Wear;
            public List<KitDef> Extras = new List<KitDef>();
            public Dictionary<ulong, int> Kills = new Dictionary<ulong, int>(), Deaths = new Dictionary<ulong, int>(), Hs = new Dictionary<ulong, int>();
            public Dictionary<ulong, float> Damage = new Dictionary<ulong, float>();
            public Dictionary<ulong, int> StartMmr = new Dictionary<ulong, int>();
            public ulong PausedBy;
            public float PauseUntil;
            public string PrevState;

            public IEnumerable<ulong> All => Teams[0].Concat(Teams[1]);
            public int TeamOf(ulong id) => Teams[0].Contains(id) ? 0 : Teams[1].Contains(id) ? 1 : -1;
        }

        private readonly List<Entry> _queue = new List<Entry>();
        private readonly List<Match> _matches = new List<Match>();
        private readonly Dictionary<ulong, Match> _in = new Dictionary<ulong, Match>();
        private readonly Dictionary<ulong, string> _ladderPick = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> _topView = new Dictionary<ulong, string>();
        private readonly HashSet<ulong> _spectating = new HashSet<ulong>();
        private readonly Dictionary<ulong, int> _acctDays = new Dictionary<ulong, int>();      // возраст Steam-аккаунта; -1 = закрытый профиль
        private readonly HashSet<ulong> _acctPending = new HashSet<ulong>();
        private readonly Dictionary<ulong, int> _formatPick = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, string> _topTab = new Dictionary<ulong, string>();
        private int _matchSeq;

        private static ulong Id(BasePlayer p) => (ulong)p.userID;

        private bool IsAdmin(BasePlayer p) => p != null && (p.IsAdmin || permission.UserHasPermission(p.UserIDString, PermAdmin));

        private string HubModeFor(string ladder) => ladder == "mix" ? _config.HubMix : ladder == "t2" ? _config.HubT2 : ladder == "prim" ? _config.HubPrim : _config.HubAk;

        private static string LadderShort(string l) => l == "ak" ? "AK" : l == "mix" ? "MIX" : l == "t2" ? "T2" : "PRIM";

        private Entry QueueOf(ulong id) => _queue.FirstOrDefault(e => e.Members.Contains(id));

        private Match MatchOf(BasePlayer p) { Match m; return p != null && _in.TryGetValue(Id(p), out m) ? m : null; }

        // команда Rust = пати
        private List<ulong> PartyOf(BasePlayer player)
        {
            var list = new List<ulong> { Id(player) };
            try
            {
                if (player.currentTeam == 0UL || RelationshipManager.ServerInstance == null) return list;
                var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                if (team == null) return list;
                foreach (var m in team.members)
                {
                    if (m == Id(player)) continue;
                    var p = BasePlayer.FindByID(m);
                    if (p != null && p.IsConnected) list.Add(m);
                }
            }
            catch { }
            return list;
        }

        private bool IsPartyLeader(BasePlayer player)
        {
            try
            {
                if (player.currentTeam == 0UL || RelationshipManager.ServerInstance == null) return true;
                var team = RelationshipManager.ServerInstance.FindTeam(player.currentTeam);
                return team == null || team.teamLeader == Id(player);
            }
            catch { return true; }
        }

        private void JoinQueue(BasePlayer player)
        {
            var id = Id(player);
            string ladder; if (!_ladderPick.TryGetValue(id, out ladder)) ladder = "ak";
            int format; if (!_formatPick.TryGetValue(id, out format)) format = 1;
            if (MatchOf(player) != null) { Toast(player, T(id, "InMatch"), true); return; }
            if (QueueOf(id) != null) { Toast(player, T(id, "AlreadyQueued"), false); return; }
            if (!_data.Arenas.Values.Any(a => a.Enabled && a.Formats.Contains(format) && a.A.Count >= format && a.B.Count >= format))
            { Toast(player, T(id, "NoArenas", format), true); return; }

            var party = PartyOf(player);
            if (party.Count > 1 && !IsPartyLeader(player)) { Toast(player, T(id, "OnlyLeader"), true); return; }
            if (party.Count > format) { Toast(player, T(id, "PartyTooBig", party.Count, format), true); return; }
            foreach (var m in party)
            {
                if (_in.ContainsKey(m) || QueueOf(m) != null) { Toast(player, T(id, "PartyBusy"), true); return; }
            }
            foreach (var m in party)
            {
                string why = SmurfCheck(m);
                if (why == null) continue;
                if (why == "wait") { Toast(player, T(id, "SmurfChecking"), false); return; }
                Toast(player, why, true);
                return;
            }
            var e = new Entry { Members = party, Ladder = ladder, Format = format, Since = Time.realtimeSinceStartup };
            e.Avg = Mathf.RoundToInt((float)party.Average(m => R(m, ladder, format).Mmr));
            _queue.Add(e);
            foreach (var m in party)
            {
                var p = BasePlayer.FindByID(m);
                if (p != null) { D(m, p.displayName); Toast(p, T(m, "Queued", T(m, "L_" + ladder), format), false); }
            }
            OpenQueue(player);
            TryMatch();
        }

        // анти-смурф: часы на сервере и возраст Steam-аккаунта. null = можно, "wait" = ждём ответ Steam, иначе причина
        private string SmurfCheck(ulong uid)
        {
            var name = D(uid).Name ?? uid.ToString();
            if (_config.MinHours > 0f && Hub != null)
            {
                var o = Hub.Call("GetPlayMinutes", uid);
                if (o is int && (int)o < _config.MinHours * 60f)
                    return T(uid, "SmurfHours", name, _config.MinHours, ((int)o / 60f).ToString("0.#"));
            }
            if (string.IsNullOrEmpty(_config.SteamApiKey) || _config.MinAccountDays <= 0) return null;
            int days;
            if (_acctDays.TryGetValue(uid, out days))
            {
                if (days < 0) return _config.AllowPrivateProfiles ? null : T(uid, "SmurfPrivate", name);
                return days < _config.MinAccountDays ? T(uid, "SmurfAge", name, _config.MinAccountDays) : null;
            }
            if (_acctPending.Add(uid))
            {
                string url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={_config.SteamApiKey}&steamids={uid}";
                webrequest.Enqueue(url, null, (code, body) =>
                {
                    _acctPending.Remove(uid);
                    int d = -1;
                    try
                    {
                        if (code == 200 && body != null)
                        {
                            int i = body.IndexOf("\"timecreated\":");
                            if (i >= 0)
                            {
                                var num = new string(body.Substring(i + 14).TakeWhile(char.IsDigit).ToArray());
                                long ts;
                                if (long.TryParse(num, out ts)) d = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1).AddSeconds(ts)).TotalDays;
                            }
                        }
                    }
                    catch { }
                    _acctDays[uid] = d;
                }, this, Oxide.Core.Libraries.RequestMethod.GET);
            }
            return "wait";
        }

        private bool SameIp(ulong a, ulong b) => _config.BlockSameIp && Hub != null && Hub.Call("SameIp", a, b) is bool && (bool)Hub.Call("SameIp", a, b);

        private void LeaveQueue(BasePlayer player, bool notify)
        {
            var e = QueueOf(Id(player));
            if (e == null) return;
            _queue.Remove(e);
            foreach (var m in e.Members)
            {
                var p = BasePlayer.FindByID(m);
                if (p != null && notify) Toast(p, T(m, "QueueLeft", player.displayName), true);
            }
        }

        private int Window(Entry e) => Mathf.Min(_config.WindowMax, _config.WindowStart + _config.WindowStep * Mathf.FloorToInt((Time.realtimeSinceStartup - e.Since) / 15f));

        private ArenaDef FreeArena(int format)
        {
            var busy = new HashSet<string>(_matches.Select(m => m.Arena.Id));
            return _data.Arenas.Values.Where(a => a.Enabled && a.Formats.Contains(format) && a.A.Count >= format && a.B.Count >= format && !busy.Contains(a.Id))
                .OrderBy(a => UnityEngine.Random.Range(0, 1000)).FirstOrDefault();
        }

        private void TryMatch()
        {
            foreach (var ladder in Ladders)
                foreach (var format in Formats)
                {
                    var q = _queue.Where(e => e.Ladder == ladder && e.Format == format).OrderBy(e => e.Since).ToList();
                    while (q.Count > 0)
                    {
                        var anchor = q[0];
                        int win = Window(anchor);
                        var cands = q.Where(e => Mathf.Abs(e.Avg - anchor.Avg) <= Mathf.Max(win, Window(e))).OrderBy(e => Mathf.Abs(e.Avg - anchor.Avg)).ToList();
                        var teams = new[] { new List<Entry>(), new List<Entry>() };
                        int[] fill = new int[2], sum = new int[2];
                        foreach (var e in cands)
                        {
                            int n = e.Members.Count;
                            bool fitA = fill[0] + n <= format && !teams[1].Any(o => o.Members.Any(x => e.Members.Any(y => SameIp(x, y)))),
                                 fitB = fill[1] + n <= format && !teams[0].Any(o => o.Members.Any(x => e.Members.Any(y => SameIp(x, y))));
                            if (!fitA && !fitB) continue;
                            int t = fitA && fitB ? (sum[0] <= sum[1] ? 0 : 1) : (fitA ? 0 : 1);
                            if (e == anchor) t = 0;
                            teams[t].Add(e); fill[t] += n; sum[t] += e.Avg * n;
                            if (fill[0] == format && fill[1] == format) break;
                        }
                        if (fill[0] != format || fill[1] != format) { q.RemoveAt(0); continue; }
                        var arena = FreeArena(format);
                        if (arena == null) return;
                        var all = teams[0].Concat(teams[1]).ToList();
                        foreach (var e in all) { _queue.Remove(e); q.Remove(e); }
                        CreateMatch(ladder, format, arena, teams[0], teams[1]);
                    }
                }
        }

        private void CreateMatch(string ladder, int format, ArenaDef arena, List<Entry> a, List<Entry> b)
        {
            var m = new Match { Id = "r" + (++_matchSeq), Ladder = ladder, Format = format, Arena = arena, AcceptUntil = Time.realtimeSinceStartup + _config.AcceptSeconds };
            foreach (var e in a) m.Teams[0].AddRange(e.Members);
            foreach (var e in b) m.Teams[1].AddRange(e.Members);
            m.Entries.AddRange(a); m.Entries.AddRange(b);
            _matches.Add(m);
            foreach (var id in m.All) { _in[id] = m; m.StartMmr[id] = R(id, ladder, format).Mmr; }
            foreach (var id in m.All) { var p = BasePlayer.FindByID(id); if (p != null) ShowFound(p, m); }
        }

        private void Accept(BasePlayer player)
        {
            var m = MatchOf(player);
            if (m == null || m.State != "accept") return;
            m.Accepted.Add(Id(player));
            foreach (var id in m.All) { var p = BasePlayer.FindByID(id); if (p != null) ShowFound(p, m); }
            if (m.All.All(x => m.Accepted.Contains(x))) BeginMatch(m);
        }

        private void Decline(BasePlayer player)
        {
            var m = MatchOf(player);
            if (m == null || m.State != "accept") return;
            CancelAccept(m, new HashSet<ulong> { Id(player) });
        }

        // кто не подтвердил — вылетает; остальные — обратно в начало очереди
        private void CancelAccept(Match m, HashSet<ulong> bad)
        {
            m.State = "ended";
            _matches.Remove(m);
            foreach (var id in m.All) _in.Remove(id);
            foreach (var e in m.Entries)
            {
                var p0 = e.Members.Select(BasePlayer.FindByID).ToList();
                if (e.Members.Any(bad.Contains))
                {
                    foreach (var mid in e.Members) { var p = BasePlayer.FindByID(mid); if (p != null) { DestroyWindow(p, UiPopup); Toast(p, T(mid, "NotAccepted"), true); } }
                    continue;
                }
                e.Since -= 30f;   // приоритет
                _queue.Add(e);
                foreach (var mid in e.Members) { var p = BasePlayer.FindByID(mid); if (p != null) { DestroyWindow(p, UiPopup); Toast(p, T(mid, "Requeued"), false); } }
            }
            TryMatch();
        }

        private void BeginMatch(Match m)
        {
            foreach (var id in m.All)
            {
                var p = BasePlayer.FindByID(id);
                if (p == null) continue;
                DestroyWindow(p, UiPopup);
                CloseAll(p);
                D(id, p.displayName);
                var mode = HubModeFor(m.Ladder);
                if (Hub != null && !string.IsNullOrEmpty(mode) && Hub.Call("GetMode", p) as string != mode) Hub.Call("SendToMode", p, mode, true);
                m.Kills[id] = 0; m.Deaths[id] = 0; m.Hs[id] = 0; m.Damage[id] = 0f;
            }
            StartRound(m);
        }

        private void StartRound(Match m)
        {
            if (!_matches.Contains(m)) return;
            m.Round++;
            m.State = "countdown";
            m.Alive.Clear();
            m.Extras.Clear();
            if (m.Ladder == "mix") { m.Gun = Pick(_config.MixGuns, k => k.Weight); m.Wear = Pick(_config.MixWear, w => w.Weight); }
            else if (m.Ladder == "t2") { m.Gun = Pick(_config.T2Guns, k => k.Weight); m.Wear = new WearDef { Name = T(0, "RoadsignSet"), Wear = _config.T2Wear }; }
            else if (m.Ladder == "prim") { m.Gun = _config.PrimKit[0]; m.Extras.AddRange(_config.PrimKit.Skip(1)); m.Wear = new WearDef { Name = "", Wear = _config.PrimWear }; }
            else { m.Gun = _config.AkKit; m.Wear = new WearDef { Name = "", Wear = _config.AkWear }; }

            foreach (var id in m.All.ToList())
            {
                var p = BasePlayer.FindByID(id);
                if (p == null || !p.IsConnected) continue;
                if (p.IsDead()) { p.Respawn(); continue; }
                Place(p, m);
            }
            timer.Once(0.7f, () =>
            {
                foreach (var id in m.All.ToList())
                {
                    if (m.Alive.Contains(id)) continue;
                    var p = BasePlayer.FindByID(id);
                    if (p != null && p.IsConnected && !p.IsDead()) Place(p, m);
                }
            });

            int n = Mathf.Max(1, _config.Countdown);
            for (int i = 0; i <= n; i++)
            {
                int left = n - i;
                timer.Once(i, () =>
                {
                    if (!_matches.Contains(m) || m.State != "countdown") return;
                    foreach (var id in m.All)
                    {
                        var p = BasePlayer.FindByID(id);
                        if (p == null) continue;
                        if (left > 0) Center(p, (left == n && (m.Ladder == "mix" || m.Ladder == "t2") ? $"<size=14>{T(id, "RoundN", m.Round)} · {T(id, "Dropped")}</size>\n<size=22>{m.Gun.Name}</size>\n<size=13>{T(id, "Set")}: {m.Wear.Name}</size>\n" : $"<size=14>{T(id, "RoundN", m.Round)}</size>\n") + left, ColText, 1.1f);
                        else Center(p, T(id, "Fight"), ColGold, 0.9f);
                    }
                    if (left == 0) { m.State = "live"; RefreshAll(m); }
                });
            }
            RefreshAll(m);
        }

        private void Place(BasePlayer p, Match m)
        {
            var id = Id(p);
            StopSpec(p);
            int team = m.TeamOf(id);
            var list = team == 0 ? m.Arena.A : m.Arena.B;
            int idx = Mathf.Max(0, m.Teams[team].IndexOf(id));
            var pt = list[idx % list.Count];
            p.Teleport(pt.V + Vector3.up * 0.15f);
            Heal(p);
            GiveKit(p, m);
            m.Alive.Add(id);
        }

        private T Pick<T>(List<T> list, Func<T, int> weight)
        {
            int total = list.Sum(x => Mathf.Max(0, weight(x)));
            if (total <= 0) return list[0];
            int r = UnityEngine.Random.Range(0, total);
            foreach (var x in list) { r -= Mathf.Max(0, weight(x)); if (r < 0) return x; }
            return list[list.Count - 1];
        }

        private void GiveKit(BasePlayer p, Match m)
        {
            p.inventory.Strip();
            foreach (var w in m.Wear.Wear) Give(p, w, 1, null, p.inventory.containerWear);
            Give(p, m.Gun.Weapon, 1, m.Gun.Mods, p.inventory.containerBelt);
            if (_config.Syringes > 0) Give(p, "syringe.medical", _config.Syringes, null, p.inventory.containerBelt);
            if (_config.Bandages > 0) Give(p, "bandage", _config.Bandages, null, p.inventory.containerBelt);
            if (!string.IsNullOrEmpty(m.Gun.Ammo)) Give(p, m.Gun.Ammo, m.Gun.AmmoAmount, null, p.inventory.containerMain);
            foreach (var x in m.Extras)
            {
                Give(p, x.Weapon, 1, x.Mods, p.inventory.containerBelt);
                if (!string.IsNullOrEmpty(x.Ammo)) Give(p, x.Ammo, x.AmmoAmount, null, p.inventory.containerMain);
            }
        }

        private static void Give(BasePlayer p, string shortname, int amount, List<string> mods, ItemContainer c)
        {
            var item = ItemManager.CreateByName(shortname, Mathf.Max(1, amount), 0UL);
            if (item == null) return;
            if (mods != null && item.contents != null)
                foreach (var md in mods)
                {
                    var mod = ItemManager.CreateByName(md, 1, 0UL);
                    if (mod != null && !mod.MoveToContainer(item.contents)) mod.Remove();
                }
            var proj = item.GetHeldEntity() as BaseProjectile;
            if (proj?.primaryMagazine != null) proj.primaryMagazine.contents = proj.primaryMagazine.capacity;
            if (!item.MoveToContainer(c) && !item.MoveToContainer(p.inventory.containerMain)) item.Remove();
        }

        private static void Heal(BasePlayer p)
        {
            try
            {
                p.health = p.MaxHealth();
                var mt = p.metabolism;
                if (mt != null) { mt.bleeding.value = 0f; mt.calories.value = mt.calories.max; mt.hydration.value = mt.hydration.max; mt.SendChanges(); }
                p.SendNetworkUpdate();
            }
            catch { }
        }

        private void CheckRoundEnd(Match m)
        {
            if (m.State != "live") return;
            bool a = m.Teams[0].Any(m.Alive.Contains), b = m.Teams[1].Any(m.Alive.Contains);
            if (a && b) return;
            m.State = "between";
            int winner = a ? 0 : b ? 1 : -1;
            if (winner >= 0) m.Score[winner]++;
            RefreshAll(m);
            foreach (var id in m.All)
            {
                var p = BasePlayer.FindByID(id);
                if (p == null) continue;
                bool won = m.TeamOf(id) == winner;
                Center(p, (winner < 0 ? T(id, "RoundDraw") : won ? T(id, "RoundWin") : T(id, "RoundLose")) + $"\n<size=18>{m.Score[0]} : {m.Score[1]}</size>", winner < 0 ? ColDim : won ? ColGreen : ColRed, 2.5f);
            }
            if (winner >= 0 && m.Score[winner] >= _config.RoundsToWin) { timer.Once(2.5f, () => EndMatch(m, winner, false)); return; }
            timer.Once(_config.Between, () => { if (_matches.Contains(m) && m.State == "between") StartRound(m); });
        }

        private void EndMatch(Match m, int winner, bool forfeit)
        {
            if (m.State == "ended") return;
            m.State = "ended";
            _matches.Remove(m);
            int[] avg =
            {
                Mathf.RoundToInt((float)m.Teams[0].Average(x => m.StartMmr.ContainsKey(x) ? m.StartMmr[x] : _config.StartMmr)),
                Mathf.RoundToInt((float)m.Teams[1].Average(x => m.StartMmr.ContainsKey(x) ? m.StartMmr[x] : _config.StartMmr)),
            };
            var deltas = new Dictionary<ulong, int>();
            foreach (var id in m.All)
            {
                int t = m.TeamOf(id);
                var r = R(id, m.Ladder, m.Format);
                bool win = t == winner;
                float expected = 1f / (1f + Mathf.Pow(10f, (avg[1 - t] - r.Mmr) / 400f));
                int k = r.Matches < 10 ? _config.KNew : _config.K;
                int delta = Mathf.RoundToInt(k * ((win ? 1f : 0f) - expected));
                if (win && delta < 5) delta = 5;
                if (!win && delta > -5) delta = -5;
                r.Mmr = Mathf.Max(0, r.Mmr + delta);
                r.Peak = Mathf.Max(r.Peak, r.Mmr);
                AchMax(id, "ranked_rank", _config.Ranks.OrderBy(x => x.From).ToList().IndexOf(RankOf(r.Mmr)));
                r.Matches++;
                if (win) r.Wins++; else r.Losses++;
                if (win) Ach(id, "ranked_wins");
                deltas[id] = delta;
                var d = D(id);
                int ki, de, hs;
                m.Kills.TryGetValue(id, out ki); m.Deaths.TryGetValue(id, out de); m.Hs.TryGetValue(id, out hs);
                d.Kills += ki; d.Deaths += de; d.HsKills += hs;
                d.History.Insert(0, new MatchLog { Ladder = m.Ladder, Format = m.Format, Win = win, Score = $"{m.Score[t]}:{m.Score[1 - t]}", Delta = delta, Date = DateTime.UtcNow.ToString("MM-dd HH:mm") });
                if (d.History.Count > 20) d.History.RemoveRange(20, d.History.Count - 20);
                var me = id;
                bool farm = Hub != null && m.Teams[1 - t].Any(o => Hub.Call("SameIp", me, o) is bool && (bool)Hub.Call("SameIp", me, o));
                if (Hub != null && !farm) Hub.Call("AddCoins", id, win ? _config.CoinsWin : _config.CoinsLoss, win ? T(id, "ForWin") : T(id, "ForMatch"));
            }
            _dirty = true;
            SaveData();

            foreach (var id in m.All)
            {
                _in.Remove(id);
                var p = BasePlayer.FindByID(id);
                StopSpec(p);
                if (p == null || !p.IsConnected) continue;
                CuiHelper.DestroyUi(p, UiHud); CuiHelper.DestroyUi(p, UiTeams); CuiHelper.DestroyUi(p, UiPause);
                ShowResult(p, m, winner, deltas, forfeit);
                var pl = p;
                timer.Once(6f, () =>
                {
                    if (pl == null || !pl.IsConnected || _in.ContainsKey(Id(pl))) return;
                    DestroyWindow(pl, UiMain);
                    if (!pl.IsDead()) pl.inventory.Strip();
                    if (Hub != null && !pl.IsDead()) Hub.Call("SendToMode", pl, HubModeFor(m.Ladder), true);
                });
            }
        }

        private void LeaveMatch(BasePlayer player)
        {
            var m = MatchOf(player);
            if (m == null) return;
            var id = Id(player);
            if (m.State == "accept") { CancelAccept(m, new HashSet<ulong> { id }); return; }
            if (m.State == "ended") { _in.Remove(id); return; }
            int t = m.TeamOf(id);
            EndMatch(m, 1 - t, true);
        }

        #endregion

        #region Хуки

        private Timer _tick;

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            RegisterLang();
            InitRoundedSprite();
            _tick = timer.Every(1f, Tick);
        }

        private void Unload()
        {
            foreach (var m in _matches.ToList()) if (m.State != "accept") EndMatch(m, -1, true);
            foreach (var p in BasePlayer.activePlayerList)
                foreach (var ui in new[] { UiMain, UiHud, UiTeams, UiCenter, UiPopup, UiToast, UiPause }) { CuiHelper.DestroyUi(p, ui); CuiHelper.DestroyUi(p, ui + ".dim"); }
            SaveData();
        }

        private void OnServerSave() { if (_dirty) SaveData(); }

        private void Tick()
        {
            float now = Time.realtimeSinceStartup;
            TryMatch();

            foreach (var m in _matches.ToList())
            {
                if (m.State == "accept" && now > m.AcceptUntil)
                {
                    CancelAccept(m, new HashSet<ulong>(m.All.Where(x => !m.Accepted.Contains(x))));
                    continue;
                }
                if (m.State == "paused")
                {
                    if (now > m.PauseUntil) { EndMatch(m, 1 - m.TeamOf(m.PausedBy), true); continue; }
                    foreach (var id in m.All) { var p = BasePlayer.FindByID(id); if (p != null && p.IsConnected) ShowPause(p, m); }
                    continue;
                }
                if (m.State == "live" || m.State == "countdown")
                {
                    foreach (var id in m.All)
                    {
                        var p = BasePlayer.FindByID(id);
                        if (p == null || !p.IsConnected) continue;
                        DrawTeams(p, m);
                        if (m.Arena.Center != null && m.Arena.Radius > 0 && !p.IsDead() && Vector3.Distance(p.transform.position, m.Arena.Center.V) > m.Arena.Radius)
                        {
                            var list = m.TeamOf(id) == 0 ? m.Arena.A : m.Arena.B;
                            p.Teleport(list[0].V + Vector3.up * 0.15f);
                        }
                    }
                }
            }

            // очередь: обновить окна поиска у тех, кто смотрит меню
            foreach (var e in _queue)
                foreach (var id in e.Members)
                {
                    var p = BasePlayer.FindByID(id);
                    if (p != null && _viewingQueue.Contains(id)) OpenQueue(p);
                }

            // конец сезона
            DateTime ends;
            if (DateTime.TryParse(_data.SeasonEnds, out ends) && DateTime.UtcNow > ends) EndSeason();
        }

        private readonly HashSet<ulong> _viewingQueue = new HashSet<ulong>();

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var victim = entity as BasePlayer;
            if (victim == null || info == null) return null;
            var vm = MatchOf(victim);
            var attacker = info.InitiatorPlayer;
            if (vm == null)
            {
                if (attacker != null && MatchOf(attacker) != null && attacker != victim) return true;
                return null;
            }
            if (vm.State != "live") return true;
            if (attacker == null || attacker == victim) return null;
            var am = MatchOf(attacker);
            if (am != vm) return true;
            var vid = Id(victim); var aid = Id(attacker);
            if (!vm.Alive.Contains(vid) || !vm.Alive.Contains(aid)) return true;
            if (vm.TeamOf(vid) == vm.TeamOf(aid)) return true;
            float dmg = 0f;
            try { dmg = info.damageTypes.Total(); } catch { }
            float cur; vm.Damage.TryGetValue(aid, out cur);
            vm.Damage[aid] = cur + Mathf.Min(dmg, victim.health);
            return null;
        }

        private object CanBeWounded(BasePlayer player, HitInfo info) => player != null && _in.ContainsKey(Id(player)) ? (object)false : null;

        private void OnPlayerDeath(BasePlayer victim, HitInfo info)
        {
            var m = MatchOf(victim);
            if (m == null || m.State == "accept") return;
            var vid = Id(victim);
            victim.inventory.Strip();
            m.Alive.Remove(vid);
            int d; m.Deaths.TryGetValue(vid, out d); m.Deaths[vid] = d + 1;
            var killer = info?.InitiatorPlayer;
            if (killer != null && killer != victim && MatchOf(killer) == m)
            {
                var kid = Id(killer);
                int k; m.Kills.TryGetValue(kid, out k); m.Kills[kid] = k + 1;
                if (info.isHeadshot) { int h; m.Hs.TryGetValue(kid, out h); m.Hs[kid] = h + 1; }
            }
            CheckRoundEnd(m);
            if (m.State == "live")
                foreach (var sid in m.Teams[Mathf.Max(0, m.TeamOf(vid))].Where(x => _spectating.Contains(x)).ToList())
                {
                    var sp = BasePlayer.FindByID(sid);
                    if (sp != null && !SpectateMate(sp, m)) StopSpec(sp);
                }
            var pl = victim;
            timer.Once(1.5f, () => { if (pl != null && pl.IsConnected && pl.IsDead() && _in.ContainsKey(Id(pl))) pl.Respawn(); });
        }

        private object OnHubRespawn(BasePlayer player)
        {
            var m = MatchOf(player);
            if (m == null || m.State == "accept") return null;
            var list = m.TeamOf(Id(player)) == 0 ? m.Arena.A : m.Arena.B;
            var pt = list[Mathf.Max(0, m.Teams[m.TeamOf(Id(player))].IndexOf(Id(player))) % list.Count];
            return new BasePlayer.SpawnPoint { pos = pt.V + Vector3.up * 0.15f, rot = Quaternion.Euler(0f, pt.Yaw, 0f) };
        }

        private object OnHubRespawned(BasePlayer player, string mode)
        {
            var m = MatchOf(player);
            if (m == null || m.State == "accept") return null;
            Heal(player);
            player.inventory.Strip();
            if (m.State == "live" && _config.SpectateOnDeath && SpectateMate(player, m)) return true;
            if (m.State == "live" || m.State == "between") Center(player, T(Id(player), "WaitRound"), ColDim, 2.5f);
            return true;   // набор выдаст следующий раунд
        }

        // ---------- наблюдение за живым тиммейтом до конца раунда
        private static object PCall(object target, string method, params object[] args)
        {
            if (target == null) return null;
            foreach (var mi in target.GetType().GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                if (mi.Name != method || mi.GetParameters().Length != args.Length) continue;
                try { return mi.Invoke(target, args) ?? true; } catch { return null; }
            }
            return null;
        }

        private bool SpectateMate(BasePlayer player, Match m)
        {
            var id = Id(player);
            int t = m.TeamOf(id);
            if (t < 0) return false;
            var mate = m.Teams[t].Where(x => x != id && m.Alive.Contains(x)).Select(x => BasePlayer.FindByID(x)).FirstOrDefault(x => x != null && !x.IsDead());
            if (mate == null) return false;
            if (_spectating.Add(id)) PCall(player, "StartSpectating");
            PCall(player, "UpdateSpectateTarget", mate.UserIDString);
            Center(player, T(id, "Spectating", mate.displayName), ColDim, 2.5f);
            return true;
        }

        private void StopSpec(BasePlayer player)
        {
            if (player == null || !_spectating.Remove(Id(player))) return;
            PCall(player, "StopSpectating");
        }

        private object OnPlayerRespawn(BasePlayer player, BasePlayer.SpawnPoint sp) => Hub != null ? null : OnHubRespawn(player);

        private void OnPlayerRespawned(BasePlayer player) { if (Hub == null) OnHubRespawned(player, null); }

        // вылет: держим игрока в режиме и ставим матч на паузу
        private object OnHubPlayerDisconnect(BasePlayer player, string mode)
        {
            var m = MatchOf(player);
            if (m == null) return null;
            if (m.State == "accept") { CancelAccept(m, new HashSet<ulong> { Id(player) }); return null; }
            if (m.State == "ended") return null;
            Pause(m, Id(player));
            return true;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            _viewingQueue.Remove(Id(player));
            LeaveQueue(player, true);
            if (Hub == null)
            {
                var m = MatchOf(player);
                if (m != null && m.State != "accept" && m.State != "ended") Pause(m, Id(player));
                else if (m != null && m.State == "accept") CancelAccept(m, new HashSet<ulong> { Id(player) });
            }
        }

        private void Pause(Match m, ulong who)
        {
            if (m.State == "paused") return;
            m.PrevState = m.State;
            m.State = "paused";
            m.PausedBy = who;
            m.PauseUntil = Time.realtimeSinceStartup + _config.ReconnectSeconds;
        }

        // перезашёл: возвращаем в матч, раунд переигрывается
        private object OnHubPlayerReady(BasePlayer player)
        {
            var m = MatchOf(player);
            if (m == null || m.State != "paused" || m.PausedBy != Id(player)) return null;
            ResumeAfterReconnect(m);
            return true;
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (Hub != null || player == null) return;
            var m = MatchOf(player);
            if (m != null && m.State == "paused" && m.PausedBy == Id(player)) timer.Once(3f, () => ResumeAfterReconnect(m));
        }

        private void ResumeAfterReconnect(Match m)
        {
            if (!_matches.Contains(m)) return;
            foreach (var id in m.All) { var p = BasePlayer.FindByID(id); if (p != null) { CuiHelper.DestroyUi(p, UiPause); CuiHelper.DestroyUi(p, UiPause + ".dim"); Toast(p, T(id, "Resumed"), false); } }
            m.State = "between";
            m.Round--;   // переигрываем раунд
            timer.Once(2f, () => StartRound(m));
        }

        private void OnHubModeChanged(BasePlayer player, string from, string to)
        {
            var m = MatchOf(player);
            if (m == null || m.State == "accept" || m.State == "ended") return;
            if (to != HubModeFor(m.Ladder)) LeaveMatch(player);
        }

        #endregion

        #region Сезоны

        private void EndSeason()
        {
            int season = _data.Season;
            foreach (var ladder in Ladders)
                foreach (var format in Formats)
                {
                    var key = Key(ladder, format);
                    var top = _data.Players.Where(kv => kv.Value.R.ContainsKey(key) && kv.Value.R[key].Matches >= 5)
                        .OrderByDescending(kv => kv.Value.R[key].Mmr).ToList();
                    for (int i = 0; i < top.Count; i++)
                    {
                        var d = top[i].Value;
                        var r = d.R[key];
                        d.Seasons.Add(new SeasonResult { Season = season, Ladder = ladder, Format = format, Mmr = r.Mmr, Rank = RankOf(r.Mmr).Name, Place = i + 1 });
                        if (i < 3)
                        {
                            string title = $"#{i + 1} S{season} {LadderShort(ladder)} {format}v{format}";
                            d.Titles.Add(title);
                            if (Hub != null && i < _config.SeasonCoins.Count && !(Hub.Call("AddPrize", top[i].Key, _config.SeasonCoins[i], T(top[i].Key, "SeasonReward")) is bool)) Hub.Call("AddCoins", top[i].Key, _config.SeasonCoins[i], T(top[i].Key, "SeasonReward"));
                            foreach (var cmd in _config.SeasonCommands)
                                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), cmd.Replace("{id}", top[i].Key.ToString()).Replace("{place}", (i + 1).ToString()).Replace("{season}", season.ToString()).Replace("{ladder}", key));
                        }
                    }
                }
            // мягкий сброс
            foreach (var d in _data.Players.Values)
                foreach (var r in d.R.Values)
                {
                    r.Mmr = Mathf.RoundToInt(_config.StartMmr + (r.Mmr - _config.StartMmr) * _config.SoftReset);
                    r.Wins = r.Losses = r.Matches = 0;
                }
            _data.Season = season + 1;
            _data.SeasonEnds = DateTime.UtcNow.AddDays(_config.SeasonDays).ToString("yyyy-MM-dd HH:mm");
            SaveData();
            foreach (var p in BasePlayer.activePlayerList) Toast(p, T(Id(p), "SeasonEnded", season, season + 1), false);
            Puts($"Сезон {season} завершён, начался сезон {season + 1}");
        }

        #endregion

        #region Команды

        [ChatCommand("ranked")]
        private void CmdRanked(BasePlayer player, string c, string[] a)
        {
            if (a.Length > 0 && (a[0] == "leave" || a[0] == "cancel"))
            {
                if (MatchOf(player) != null) LeaveMatch(player); else LeaveQueue(player, true);
                return;
            }
            if (a.Length > 0 && a[0] == "top") { OpenTop(player); return; }
            OpenQueue(player);
        }

        [ChatCommand("top")]
        private void CmdTop(BasePlayer player, string c, string[] a) => OpenTop(player);

        [ConsoleCommand("ranked.ui")]
        private void CcUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs()) return;
            var id = Id(player);
            string a1 = arg.GetString(1);
            switch (arg.GetString(0))
            {
                case "close": _viewingQueue.Remove(id); CloseAll(player); break;
                case "ladder": _ladderPick[id] = a1; OpenQueue(player); break;
                case "format": _formatPick[id] = int.Parse(a1); OpenQueue(player); break;
                case "join": JoinQueue(player); break;
                case "cancel": LeaveQueue(player, true); OpenQueue(player); break;
                case "accept": Accept(player); break;
                case "decline": Decline(player); break;
                case "top": _viewingQueue.Remove(id); if (!string.IsNullOrEmpty(a1)) _topTab[id] = a1; OpenTop(player); break;
                case "topview": _topView[id] = a1 == "hall" ? "hall" : "ranks"; OpenTop(player); break;
                case "queue": OpenQueue(player); break;
            }
        }

        [ChatCommand("rankedadmin")]
        private void CmdAdmin(BasePlayer player, string c, string[] a)
        {
            var id = Id(player);
            if (!IsAdmin(player)) { Reply(player, T(id, "NoPerm")); return; }
            if (a.Length == 0) { Reply(player, T(id, "AdminHelp")); return; }
            var pos = player.transform.position;
            float yaw = player.eyes != null ? player.eyes.rotation.eulerAngles.y : 0f;
            string key = a.Length > 1 ? a[1].ToLower() : null;
            ArenaDef ar = key != null && _data.Arenas.ContainsKey(key) ? _data.Arenas[key] : null;
            switch (a[0].ToLower())
            {
                case "create":
                    if (key == null) { Reply(player, "/rankedadmin create <id>"); return; }
                    _data.Arenas[key] = new ArenaDef { Id = key, Name = key, Center = Pt.From(pos, yaw) };
                    Reply(player, T(id, "AdmCreated", key)); break;
                case "a": if (ar == null) return; ar.A.Add(Pt.From(pos, yaw)); Reply(player, $"A: {ar.A.Count}"); break;
                case "b": if (ar == null) return; ar.B.Add(Pt.From(pos, yaw)); Reply(player, $"B: {ar.B.Count}"); break;
                case "clear": if (ar == null) return; ar.A.Clear(); ar.B.Clear(); break;
                case "name": if (ar == null) return; ar.Name = string.Join(" ", a.Skip(2).ToArray()); break;
                case "radius": if (ar == null || a.Length < 3) return; ar.Radius = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture); ar.Center = Pt.From(pos, yaw); break;
                case "formats": if (ar == null || a.Length < 3) return; ar.Formats = a.Skip(2).Select(int.Parse).Where(Formats.Contains).ToList(); break;
                case "enable": if (ar != null) ar.Enabled = true; break;
                case "disable": if (ar != null) ar.Enabled = false; break;
                case "delete": if (key != null) _data.Arenas.Remove(key); break;
                case "list":
                    Reply(player, _data.Arenas.Count == 0 ? T(id, "Empty") : string.Join("\n", _data.Arenas.Values.Select(x => $"{x.Id} «{x.Name}» A{x.A.Count} B{x.B.Count} форматы {string.Join(",", x.Formats.Select(f => f + "v" + f).ToArray())}{(x.Enabled ? "" : " выкл")}").ToArray()));
                    return;
                case "season":
                    if (a.Length > 1 && a[1] == "end") { EndSeason(); return; }
                    Reply(player, T(id, "SeasonInfo", _data.Season, _data.SeasonEnds)); return;
                case "setmmr":
                {
                    if (a.Length < 5) { Reply(player, "/rankedadmin setmmr <ник> <ak|mix> <1|2|3|5> <mmr>"); return; }
                    var t = BasePlayer.activePlayerList.FirstOrDefault(p => p.displayName.IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0);
                    if (t == null) return;
                    R(Id(t), a[2], int.Parse(a[3])).Mmr = int.Parse(a[4]);
                    break;
                }
                default: Reply(player, T(id, "AdminHelp")); return;
            }
            SaveData();
            Reply(player, T(id, "AdmOk"));
        }

        private void Reply(BasePlayer player, string msg) => player.ChatMessage($"<color=#f0a6b1>[Ranked]</color> {msg}");

        #endregion

        #region Интерфейс: основа

        private static string _roundPng;
        private const string RoundPngB64 = "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAACgklEQVR42u2bMWsUQRiGn5k7sbNKEBGxsUnAJo0BQVJohHTW/gWr9Km0Su1fsAkEa2MnHGiTRjgLbeQ4gpDGK5LC3f0s9p1ks7kLdmZ3vg+W290Zjnmf+WZnBuYNZsaCCMAAKPR8G3gOPAPWgLvALdX7n2HADJgCh8BH4APwS+VDoFS9yyIXAIhApfsV4BXwArhDN+IIeA+8Bb7N0dTAZ9a+BvodmtlrMzux8yjNrDCzStd1idSeQm1McSINw5a2s6udAQOly33gHfBY7wuVhY5kgEnHUM8j4CXws6Hx0hBIBavAgcb4H/1JV4TPA1EAN/SN2ATGTQgJQBof94AvGutFg2DXI2k5Ah4Bk6Q5qneDKO31UHyaCQpp25PWAIQoEiWwA6w30r5vMZS2dWktgRjMLAAPgK+qFDs85v/lm1ApGx4CP6JebgM3dd9X8WlxZ9K6DVgwsyV9GZcalfocado7BlYjsAUsZ9D77SxYBrai5kZbtFbucRYYsBm1sQmZ9H4zCwKwFszst3Z1lhGEpHUW7Ir9cA4RyTwcgANwAA7AATgAB+AAHIADcAAOwAE4AAfgAByAA3AADsABOAAH4AAcgANwAA7AATgAB5AHgBkXj4+RyREZgFmkPkWdK4BppLaZ5HpM7jBSewNyPSZ34EdldbMv4WUGvV9K6z5w7MflJfg7sMtFn2AfI5m/dqX5zDCRQHyidlQUPXSNJE2fgSfKhMpNU6pQKTUmwFMtjpLHxjo+5pMHaiptE2mt2nuBUgVjasPkiHN3VdExEMkvmNxwI2kat42Tcc4UMaB2WG4Ab4DThnmyahiR7Rqu7Eq1MajNp9KwMc816ubpKwBkY5//C56fx7Wog3HqAAAAAElFTkSuQmCC";

        private class RoundedImage : ICuiComponent
        {
            [JsonProperty("type")] public string Type => "UnityEngine.UI.Image";
            [JsonProperty("png")] public string Png;
            [JsonProperty("color")] public string Color;
            [JsonProperty("imagetype")] public string ImageType = "Sliced";
            [JsonProperty("slice")] public string Slice = "16 16 16 16";
            [JsonProperty("ppuMultiplier")] public float Ppu = 1.6f;
        }

        private void InitRoundedSprite()
        {
            _roundPng = null;
            if (!_config.RoundedCorners) return;
            try { _roundPng = FileStorage.server.StoreByCrc(CommunityEntity.ServerInstance.net.ID, FileStorage.Type.png, Convert.FromBase64String(RoundPngB64)).ToString(); }
            catch (Exception ex) { PrintWarning("Скругления выключены: " + ex.Message); }
        }

        private static void Root(CuiElementContainer c, string name, string parent, string color, string amin, string amax, string omin, string omax, float r, bool cursor)
        {
            var e = new CuiElement { Name = name, Parent = parent };
            if (_roundPng != null && r > 0f) e.Components.Add(new RoundedImage { Png = _roundPng, Color = color, Ppu = 16f / r });
            else e.Components.Add(new CuiImageComponent { Color = color });
            e.Components.Add(new CuiRectTransformComponent { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax });
            if (cursor) e.Components.Add(new CuiNeedsCursorComponent());
            c.Add(e);
        }

        private static void Box(CuiElementContainer c, string parent, string color, float x, float y, float w, float h, float r = 12f)
        {
            string omin = $"{x} {-y - h}", omax = $"{x + w} {-y}";
            if (r > 0f && _roundPng != null)
            {
                c.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(), Parent = parent,
                    Components =
                    {
                        new RoundedImage { Png = _roundPng, Color = color, Ppu = 16f / Mathf.Max(1f, Mathf.Min(r, Mathf.Min(w, h) / 2f)) },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = omin, OffsetMax = omax }
                    }
                });
                return;
            }
            c.Add(new CuiPanel { Image = { Color = color }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = omin, OffsetMax = omax } }, parent);
        }

        private static void Text(CuiElementContainer c, string parent, string text, int size, float x, float y, float w, float h,
            TextAnchor align = TextAnchor.MiddleLeft, string color = ColText, string font = Font)
        {
            c.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = size, Align = align, Color = color, Font = font },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - h}", OffsetMax = $"{x + w} {-y}" }
            }, parent);
        }

        private static void Btn(CuiElementContainer c, string parent, string text, string cmd, string color, int size, float x, float y, float w, float h,
            string textColor = ColText, float r = 12f)
        {
            bool round = _roundPng != null && r > 0f;
            if (round) Box(c, parent, color, x, y, w, h, r);
            c.Add(new CuiButton
            {
                Button = { Command = cmd, Color = round ? "0 0 0 0" : color },
                Text = { Text = text, FontSize = size, Align = TextAnchor.MiddleCenter, Color = textColor, Font = Font },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - h}", OffsetMax = $"{x + w} {-y}" }
            }, parent);
        }

        private static void Bar(CuiElementContainer c, string parent, float x, float y, float w, float h, float pct, string color)
        {
            Box(c, parent, "1 1 1 0.08", x, y, w, h, h / 2f);
            if (pct > 0.01f) Box(c, parent, color, x, y, Mathf.Max(h, w * Mathf.Clamp01(pct)), h, h / 2f);
        }

        private void Badge(CuiElementContainer c, string parent, RankDef r, float x, float y, float s)
        {
            var col = r.Color.Split(' ');
            string soft = col.Length >= 3 ? $"{col[0]} {col[1]} {col[2]} 0.18" : "1 1 1 0.1";
            Box(c, parent, soft, x, y, s, s, s / 3f);
            Text(c, parent, r.Name.Substring(0, 1), Mathf.RoundToInt(s * 0.5f), x, y, s, s, TextAnchor.MiddleCenter, r.Color);
        }

        private const float M = 18f, G = 10f;

        private void Window(CuiElementContainer c, string name, string icon, string title, string sub, float w, float h, string closeCmd, bool dim = true)
        {
            if (dim) c.Add(new CuiPanel { Image = { Color = "0.03 0.02 0.02 0.5" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", name + ".dim");
            Root(c, name, dim ? name + ".dim" : "Overlay", ColBg, "0.5 0.5", "0.5 0.5", $"{-w / 2f} {-h / 2f}", $"{w / 2f} {h / 2f}", 18f, true);
            Box(c, name, ColWine, M, M, 40, 40, 12f);
            Text(c, name, icon, 13, M, M, 40, 40, TextAnchor.MiddleCenter);
            Text(c, name, title, 18, M + 50, M - 2, w - 2 * (M + 50), 26, TextAnchor.MiddleCenter);
            Text(c, name, sub, 11, M + 50, M + 22, w - 2 * (M + 50), 18, TextAnchor.MiddleCenter, ColDim, FontReg);
            if (!string.IsNullOrEmpty(closeCmd)) Btn(c, name, "X", closeCmd, ColBtn, 14, w - M - 40, M, 40, 40);
            Box(c, name, ColWine, 0, M + 52, w, 2, 0f);
        }

        private void DestroyWindow(BasePlayer p, string name) { CuiHelper.DestroyUi(p, name + ".dim"); CuiHelper.DestroyUi(p, name); }

        private void CloseAll(BasePlayer p) => DestroyWindow(p, UiMain);

        private void Seg(CuiElementContainer c, string parent, float x, float y, float w, float h, IList<string[]> items, string active, int size = 13)
        {
            int n = items.Count;
            float bw = (w - (n - 1) * 6f) / n;
            for (int i = 0; i < n; i++)
            {
                bool a = items[i][0] == active;
                Btn(c, parent, items[i][1], items[i][2], a ? ColWine : ColBtn, size, x + i * (bw + 6f), y, bw, h, a ? ColText : ColDim, 10f);
            }
        }

        private string SeasonLeft(ulong id)
        {
            DateTime ends;
            if (!DateTime.TryParse(_data.SeasonEnds, out ends)) return "";
            var left = ends - DateTime.UtcNow;
            return left.TotalDays >= 1 ? T(id, "DaysLeft", (int)left.TotalDays) : T(id, "HoursLeft", Mathf.Max(0, (int)left.TotalHours));
        }

        #endregion

        #region Интерфейс: очередь

        private void OpenQueue(BasePlayer player)
        {
            var id = Id(player);
            _viewingQueue.Add(id);
            string ladder; if (!_ladderPick.TryGetValue(id, out ladder)) ladder = "ak";
            int format; if (!_formatPick.TryGetValue(id, out format)) format = 1;
            D(id, player.displayName);
            const float W = 960f, H = 700f;
            var c = new CuiElementContainer();
            Window(c, UiMain, "R", T(id, "Title"), T(id, "Sub", _data.Season, SeasonLeft(id), _config.RoundsToWin), W, H, "ranked.ui close");
            var R0 = UiMain;
            float top = M + 52 + 2 + M, cw = (W - 2 * M - G) / 2f;

            // четыре ранкеда: калаши, разные ганы, тир 2, примитив
            for (int i = 0; i < Ladders.Length; i++)
            {
                var l = Ladders[i];
                float x = M + (i % 2) * (cw + G), y0 = top + (i / 2) * (72 + 8);
                var r = R(id, l, format);
                var rank = RankOf(r.Mmr);
                bool a = l == ladder;
                Box(c, R0, a ? ColWineSoft : ColPanel, x, y0, cw, 72, 16f);
                Badge(c, R0, rank, x + 14, y0 + 14, 44);
                Text(c, R0, T(id, "L_" + l), 15, x + 70, y0 + 8, cw - 86, 20);
                Text(c, R0, T(id, "LD_" + l), 10, x + 70, y0 + 28, cw - 86, 16, TextAnchor.MiddleLeft, ColDim, FontReg);
                int q = _queue.Where(e => e.Ladder == l && e.Format == format).Sum(e => e.Members.Count);
                Text(c, R0, $"{RankLabel(r.Mmr, id)} · {r.Mmr} MMR   ·   {T(id, "InQueue", q)}", 10, x + 70, y0 + 46, cw - 86, 18, TextAnchor.MiddleLeft, rank.Color);
                Btn(c, R0, "", "ranked.ui ladder " + l, "0 0 0 0", 10, x, y0, cw, 72, ColText, 0f);
            }
            top += 2 * 72 + 8 + G;
            Text(c, R0, T(id, "Format"), 10, M, top, 200, 16, TextAnchor.MiddleLeft, ColDim); top += 22;
            Seg(c, R0, M, top, W - 2 * M, 40, Formats.Select(f => new[] { f.ToString(), $"{f}v{f}", "ranked.ui format " + f }).ToList(), format.ToString(), 14);
            top += 40 + G;

            float ph = H - M - top;
            // пати
            Box(c, R0, ColPanel, M, top, cw, ph, 16f);
            var party = PartyOf(player);
            Text(c, R0, T(id, "Party", party.Count, format), 10, M + 14, top + 10, cw - 28, 16, TextAnchor.MiddleLeft, ColDim);
            for (int i = 0; i < party.Count && i < 5; i++)
            {
                var pid = party[i];
                var pr = R(pid, ladder, format);
                var prank = RankOf(pr.Mmr);
                Box(c, R0, ColRow, M + 12, top + 34 + i * 40, cw - 24, 34, 10f);
                Text(c, R0, (D(pid).Name ?? pid.ToString()) + (pid == id ? T(id, "You") : ""), 12, M + 24, top + 34 + i * 40, cw - 160, 34);
                Badge(c, R0, prank, M + cw - 112, top + 39 + i * 40, 24);
                Text(c, R0, pr.Mmr.ToString(), 12, M + cw - 84, top + 34 + i * 40, 60, 34, TextAnchor.MiddleRight);
            }
            Text(c, R0, party.Count > format ? T(id, "PartyTooBigShort", format) : T(id, "PartyHint"), 10, M + 14, top + ph - 50, cw - 28, 40, TextAnchor.LowerLeft, party.Count > format ? ColRed : ColDim, FontReg);

            // поиск
            float rx = M + cw + G;
            Box(c, R0, ColPanel, rx, top, cw, ph, 16f);
            var e0 = QueueOf(id);
            var m0 = MatchOf(player);
            if (m0 != null && m0.State != "ended")
            {
                Text(c, R0, T(id, "InMatchNow"), 16, rx, top, cw, ph - 60, TextAnchor.MiddleCenter, ColGreen);
                Btn(c, R0, T(id, "LeaveMatch"), "ranked.ui cancel", ColBtn, 13, rx + 14, top + ph - 58, cw - 28, 44, ColText, 14f);
            }
            else if (e0 != null)
            {
                int secs = Mathf.FloorToInt(Time.realtimeSinceStartup - e0.Since);
                Box(c, R0, "1 1 1 0.06", rx + cw / 2f - 50, top + 30, 100, 100, 50f);
                Text(c, R0, $"{secs / 60}:{secs % 60:00}", 24, rx + cw / 2f - 50, top + 30, 100, 100, TextAnchor.MiddleCenter);
                Text(c, R0, T(id, "Searching"), 15, rx, top + 140, cw, 24, TextAnchor.MiddleCenter);
                Text(c, R0, T(id, "SearchInfo", T(id, "L_" + e0.Ladder), e0.Format, Window(e0)), 11, rx + 14, top + 166, cw - 28, 36, TextAnchor.UpperCenter, ColDim, FontReg);
                Btn(c, R0, T(id, "CancelSearch"), "ranked.ui cancel", ColBtn, 13, rx + 14, top + ph - 58, cw - 28, 44, ColText, 14f);
            }
            else
            {
                Text(c, R0, T(id, "ReadyText"), 12, rx + 14, top + 14, cw - 28, ph - 90, TextAnchor.MiddleCenter, ColDim, FontReg);
                Btn(c, R0, T(id, "FindMatch"), "ranked.ui join", ColWine, 15, rx + 14, top + ph - 58, cw - 28, 44, ColText, 14f);
            }

            Btn(c, R0, T(id, "TopBtn"), "ranked.ui top", ColBtn, 11, W - M - 40 - G - 120, M + 4, 120, 32, ColText, 10f);
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        private void ShowFound(BasePlayer player, Match m)
        {
            var id = Id(player);
            const float W = 540f, H = 330f;
            var c = new CuiElementContainer();
            Root(c, UiPopup, "Overlay", ColBg, "0.5 0.5", "0.5 0.5", $"{-W / 2f} {-H / 2f}", $"{W / 2f} {H / 2f}", 18f, true);
            var R0 = UiPopup;
            Text(c, R0, T(id, "Found"), 12, 0, 16, W, 18, TextAnchor.MiddleCenter, ColGreen);
            Text(c, R0, $"{T(id, "L_" + m.Ladder)} · {m.Format}v{m.Format}", 22, 0, 36, W, 30, TextAnchor.MiddleCenter);
            int a0 = Mathf.RoundToInt((float)m.Teams[0].Average(x => m.StartMmr[x])), b0 = Mathf.RoundToInt((float)m.Teams[1].Average(x => m.StartMmr[x]));
            Text(c, R0, T(id, "FoundSub", m.Arena.Name, a0, b0), 11, 0, 66, W, 18, TextAnchor.MiddleCenter, ColDim, FontReg);
            float cw = (W - 2 * M - 60f) / 2f;
            for (int t = 0; t < 2; t++)
            {
                float x = t == 0 ? M : W - M - cw;
                for (int i = 0; i < m.Teams[t].Count && i < 5; i++)
                {
                    var pid = m.Teams[t][i];
                    bool ok = m.Accepted.Contains(pid);
                    Box(c, R0, ok ? "0.50 0.83 0.60 0.18" : ColRow, x, 96 + i * 30, cw, 26, 9f);
                    Text(c, R0, D(pid).Name ?? "?", 11, x + 10, 96 + i * 30, cw - 50, 26, TextAnchor.MiddleLeft, t == 0 ? ColRose : ColBlue);
                    Badge(c, R0, RankOf(m.StartMmr[pid]), x + cw - 28, 98 + i * 30, 22);
                }
            }
            Text(c, R0, "VS", 18, W / 2f - 30, 96, 60, 30 * Mathf.Min(5, m.Format), TextAnchor.MiddleCenter, ColDim);
            float left = Mathf.Max(0f, m.AcceptUntil - Time.realtimeSinceStartup);
            Bar(c, R0, M, H - M - 46 - 16, W - 2 * M, 6, left / _config.AcceptSeconds, ColGreen);
            float bw = (W - 2 * M - G) / 2f;
            bool me = m.Accepted.Contains(id);
            Btn(c, R0, me ? T(id, "Waiting", m.Accepted.Count, m.Format * 2) : T(id, "AcceptBtn", Mathf.CeilToInt(left)), me ? "" : "ranked.ui accept", me ? ColBtn : ColWine, 14, M, H - M - 46, bw, 46, ColText, 14f);
            Btn(c, R0, T(id, "DeclineBtn"), "ranked.ui decline", ColBtn, 13, M + bw + G, H - M - 46, bw, 46, ColText, 14f);
            DestroyWindow(player, UiPopup);
            AddUiScaled(player, c);
            if (!me)
            {
                var pl = player;
                timer.Once(1f, () => { if (pl != null && pl.IsConnected && MatchOf(pl) == m && m.State == "accept") ShowFound(pl, m); });
            }
        }

        #endregion

        #region Интерфейс: HUD, пауза, итоги

        private void RefreshAll(Match m)
        {
            foreach (var id in m.All) { var p = BasePlayer.FindByID(id); if (p != null && p.IsConnected) { DrawHud(p, m); DrawTeams(p, m); } }
        }

        private void DrawHud(BasePlayer player, Match m)
        {
            var id = Id(player);
            var c = new CuiElementContainer();
            Root(c, UiHud, "Hud", "0 0 0 0", "0.5 1", "0.5 1", "-210 -76", "210 -14", 0f, false);
            Box(c, UiHud, "0.66 0.13 0.22 0.88", 0, 4, 170, 50, 14f);
            Text(c, UiHud, m.Score[0].ToString(), 28, 0, 4, 160, 50, TextAnchor.MiddleRight);
            Text(c, UiHud, TeamLabel(m, 0), 11, 12, 4, 110, 50, TextAnchor.MiddleLeft);
            Box(c, UiHud, "0.05 0.04 0.04 0.92", 170, 0, 80, 58, 8f);
            Text(c, UiHud, $"{T(id, "RoundN", Mathf.Max(1, m.Round))}\n<size=10>{T(id, "ToWins", _config.RoundsToWin)}</size>", 11, 170, 0, 80, 58, TextAnchor.MiddleCenter, ColDim);
            Box(c, UiHud, "0.20 0.38 0.62 0.88", 250, 4, 170, 50, 14f);
            Text(c, UiHud, m.Score[1].ToString(), 28, 260, 4, 160, 50, TextAnchor.MiddleLeft);
            Text(c, UiHud, TeamLabel(m, 1), 11, 298, 4, 110, 50, TextAnchor.MiddleRight);
            if (m.Ladder == "mix" && m.Gun != null)
            {
                Root(c, UiHud + ".gun", "Hud", ColHud, "0.5 1", "0.5 1", "-150 -112", "150 -82", 12f, false);
                Text(c, UiHud + ".gun", $"{m.Gun.Name} · {m.Wear.Name}", 12, 0, 0, 300, 30, TextAnchor.MiddleCenter, ColRose);
            }
            CuiHelper.DestroyUi(player, UiHud);
            CuiHelper.DestroyUi(player, UiHud + ".gun");
            AddUiScaled(player, c);
        }

        private string TeamLabel(Match m, int t) => m.Format == 1 ? (D(m.Teams[t][0]).Name ?? "?") : string.Join(" · ", m.Teams[t].Take(2).Select(x => D(x).Name ?? "?").ToArray()) + (m.Teams[t].Count > 2 ? "…" : "");

        private void DrawTeams(BasePlayer player, Match m)
        {
            var c = new CuiElementContainer();
            for (int t = 0; t < 2; t++)
            {
                string name = UiTeams + t;
                int n = m.Teams[t].Count;
                float h = n * 40f + 8f;
                if (t == 0) Root(c, name, "Hud", "0 0 0 0", "0 1", "0 1", $"16 {-90 - h}", "196 -90", 0f, false);
                else Root(c, name, "Hud", "0 0 0 0", "1 1", "1 1", $"-196 {-90 - h}", "-16 -90", 0f, false);
                for (int i = 0; i < n; i++)
                {
                    var pid = m.Teams[t][i];
                    var p = BasePlayer.FindByID(pid);
                    float hp = p != null && !p.IsDead() && m.Alive.Contains(pid) ? p.health : 0f;
                    Box(c, name, "0.04 0.03 0.03 0.62", 0, i * 40, 180, 36, 9f);
                    Text(c, name, D(pid).Name ?? "?", 11, 8, i * 40 + 2, 120, 18, TextAnchor.MiddleLeft, hp > 0 ? ColText : ColDim);
                    Text(c, name, Mathf.RoundToInt(hp).ToString(), 11, 120, i * 40 + 2, 52, 18, TextAnchor.MiddleRight, hp > 0 ? ColText : ColDim);
                    Bar(c, name, 8, i * 40 + 24, 164, 4, hp / 100f, t == 0 ? ColRed : ColBlue);
                }
            }
            CuiHelper.DestroyUi(player, UiTeams + "0");
            CuiHelper.DestroyUi(player, UiTeams + "1");
            AddUiScaled(player, c);
        }

        private void ShowPause(BasePlayer player, Match m)
        {
            var id = Id(player);
            int left = Mathf.CeilToInt(Mathf.Max(0f, m.PauseUntil - Time.realtimeSinceStartup));
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0.03 0.02 0.02 0.5" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, "Hud", UiPause + ".dim");
            const float W = 460f, H = 190f;
            Root(c, UiPause, UiPause + ".dim", ColBg, "0.5 0.5", "0.5 0.5", $"{-W / 2f} {-H / 2f}", $"{W / 2f} {H / 2f}", 18f, false);
            Text(c, UiPause, T(id, "Paused"), 12, 0, 18, W, 18, TextAnchor.MiddleCenter, ColGold);
            Text(c, UiPause, T(id, "PausedWho", D(m.PausedBy).Name ?? "?"), 18, 0, 40, W, 26, TextAnchor.MiddleCenter);
            Text(c, UiPause, T(id, "PausedHint"), 11, 0, 66, W, 18, TextAnchor.MiddleCenter, ColDim, FontReg);
            Text(c, UiPause, $"{left / 60}:{left % 60:00}", 40, 0, 88, W, 56, TextAnchor.MiddleCenter);
            Bar(c, UiPause, M, H - M - 8, W - 2 * M, 6, left / (float)_config.ReconnectSeconds, ColGold);
            CuiHelper.DestroyUi(player, UiPause);
            CuiHelper.DestroyUi(player, UiPause + ".dim");
            AddUiScaled(player, c);
        }

        private void ShowResult(BasePlayer player, Match m, int winner, Dictionary<ulong, int> deltas, bool forfeit)
        {
            var id = Id(player);
            bool win = m.TeamOf(id) == winner;
            int t = m.TeamOf(id);
            const float W = 620f, H = 520f;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = win ? "0.10 0.22 0.13 0.6" : "0.22 0.05 0.08 0.6" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", UiMain + ".dim");
            Root(c, UiMain, UiMain + ".dim", ColBg, "0.5 0.5", "0.5 0.5", $"{-W / 2f} {-H / 2f}", $"{W / 2f} {H / 2f}", 18f, true);
            var R0 = UiMain;
            Text(c, R0, win ? T(id, "Victory") : T(id, "Defeat"), 42, 0, 16, W, 56, TextAnchor.MiddleCenter, win ? ColGreen : ColRed);
            Text(c, R0, $"{T(id, "L_" + m.Ladder)} · {m.Format}v{m.Format} · {m.Score[t]} : {m.Score[1 - t]}{(forfeit ? " · " + T(id, "Forfeit") : "")}", 13, 0, 72, W, 20, TextAnchor.MiddleCenter, ColDim, FontReg);

            var r = R(id, m.Ladder, m.Format);
            var rank = RankOf(r.Mmr);
            var next = NextRank(r.Mmr);
            int delta; deltas.TryGetValue(id, out delta);
            Box(c, R0, ColPanel, M, 104, W - 2 * M, 84, 16f);
            Badge(c, R0, rank, M + 14, 118, 56);
            Text(c, R0, RankLabel(r.Mmr, id), 17, M + 84, 112, 260, 26, TextAnchor.MiddleLeft, rank.Color);
            Text(c, R0, (delta >= 0 ? "+" : "") + delta + " MMR", 22, W - M - 200, 112, 186, 26, TextAnchor.MiddleRight, delta >= 0 ? ColGreen : ColRed);
            float pct = next == null ? 1f : (r.Mmr - rank.From) / (float)Mathf.Max(1, next.From - rank.From);
            Bar(c, R0, M + 84, 144, W - 2 * M - 98, 8, pct, rank.Color);
            Text(c, R0, next == null ? $"{r.Mmr} MMR · {T(id, "MaxRank")}" : T(id, "ToNext", r.Mmr, RankName(next, id), next.From - r.Mmr), 11, M + 84, 156, W - 2 * M - 98, 20, TextAnchor.MiddleLeft, ColDim, FontReg);

            float y = 202;
            float[] cx = { W - M - 280, W - M - 220, W - M - 160, W - M - 80 };
            Text(c, R0, T(id, "Player"), 10, M + 12, y, 200, 18, TextAnchor.MiddleLeft, ColDim);
            var heads = new[] { T(id, "K"), T(id, "D"), T(id, "HS"), T(id, "Dmg") };
            for (int i = 0; i < 4; i++) Text(c, R0, heads[i], 10, cx[i], y, 60, 18, TextAnchor.MiddleCenter, ColDim);
            y += 22;
            foreach (var pid in m.Teams[0].Concat(m.Teams[1]).Take(10))
            {
                int k, d, hs; float dmg;
                m.Kills.TryGetValue(pid, out k); m.Deaths.TryGetValue(pid, out d); m.Hs.TryGetValue(pid, out hs); m.Damage.TryGetValue(pid, out dmg);
                Box(c, R0, pid == id ? ColWineSoft : ColRow, M, y, W - 2 * M, 26, 9f);
                Text(c, R0, D(pid).Name ?? "?", 12, M + 12, y, 220, 26, TextAnchor.MiddleLeft, m.TeamOf(pid) == 0 ? ColRose : ColBlue);
                var vals = new[] { k.ToString(), d.ToString(), k > 0 ? Mathf.RoundToInt(100f * hs / k) + "%" : "0%", Mathf.RoundToInt(dmg).ToString() };
                for (int i = 0; i < 4; i++) Text(c, R0, vals[i], 12, cx[i], y, 60, 26, TextAnchor.MiddleCenter);
                y += 29;
            }
            Text(c, R0, T(id, "BackSoon", win ? _config.CoinsWin : _config.CoinsLoss), 11, 0, H - 34, W, 20, TextAnchor.MiddleCenter, ColDim, FontReg);
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        private void Center(BasePlayer player, string text, string color, float seconds)
        {
            if (player == null || !player.IsConnected) return;
            var c = new CuiElementContainer();
            c.Add(new CuiLabel { Text = { Text = text, FontSize = 30, Align = TextAnchor.MiddleCenter, Color = color, Font = Font }, RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-420 40", OffsetMax = "420 200" } }, "Hud", UiCenter);
            CuiHelper.DestroyUi(player, UiCenter);
            AddUiScaled(player, c);
            var p = player;
            timer.Once(seconds, () => { if (p != null && p.IsConnected) CuiHelper.DestroyUi(p, UiCenter); });
        }

        private void Toast(BasePlayer player, string text, bool warn)
        {
            if (player == null || !player.IsConnected) return;
            var c = new CuiElementContainer();
            Root(c, UiToast, "Hud", warn ? "0.40 0.06 0.11 0.92" : "0.07 0.065 0.065 0.90", "0.5 1", "0.5 1", "-260 -170", "260 -128", 20f, false);
            c.Add(new CuiLabel { Text = { Text = text, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = ColText, Font = Font }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, UiToast);
            CuiHelper.DestroyUi(player, UiToast);
            AddUiScaled(player, c);
            var p = player;
            timer.Once(3.5f, () => { if (p != null && p.IsConnected) CuiHelper.DestroyUi(p, UiToast); });
        }

        #endregion

        #region Интерфейс: таблица лидеров

        private void OpenTop(BasePlayer player)
        {
            var id = Id(player);
            string tab; if (!_topTab.TryGetValue(id, out tab)) tab = "ak_1";
            var parts = tab.Split('_');
            string ladder = parts[0]; int format = parts.Length > 1 ? int.Parse(parts[1]) : 1;
            const float W = 980f, H = 660f;
            var c = new CuiElementContainer();
            Window(c, UiMain, "T", T(id, "TopTitle"), T(id, "TopSub", _data.Season, SeasonLeft(id)), W, H, "ranked.ui close");
            var R0 = UiMain;
            float top = M + 52 + 2 + M;
            float half = (W - 2 * M - G) / 2f;
            Seg(c, R0, M, top, half, 34, Ladders.Select(l => new[] { l, T(id, "L_" + l), "ranked.ui top " + Key(l, format) }).ToList(), ladder);
            Seg(c, R0, M + half + G, top, half, 34, Formats.Select(f => new[] { f.ToString(), $"{f}v{f}", "ranked.ui top " + Key(ladder, f) }).ToList(), format.ToString());
            top += 34 + G;
            float ph = H - M - 46 - G - top, lw = (W - 2 * M - G) * 0.62f, rw = W - 2 * M - G - lw;
            Box(c, R0, ColPanel, M, top, lw, ph, 16f);
            var key = Key(ladder, format);
            var list = _data.Players.Where(kv => kv.Value.R.ContainsKey(key) && kv.Value.R[key].Matches > 0).OrderByDescending(kv => kv.Value.R[key].Mmr).ToList();
            float y = top + 10;
            Text(c, R0, "#", 10, M + 20, y, 30, 18, TextAnchor.MiddleLeft, ColDim);
            Text(c, R0, T(id, "Player"), 10, M + 52, y, 200, 18, TextAnchor.MiddleLeft, ColDim);
            Text(c, R0, T(id, "Rank"), 10, M + lw - 250, y, 110, 18, TextAnchor.MiddleLeft, ColDim);
            Text(c, R0, "MMR", 10, M + lw - 134, y, 56, 18, TextAnchor.MiddleCenter, ColDim);
            Text(c, R0, T(id, "WinPct"), 10, M + lw - 74, y, 56, 18, TextAnchor.MiddleCenter, ColDim);
            y += 24;
            int myPlace = list.FindIndex(kv => kv.Key == id);
            var show = list.Take(10).ToList();
            for (int i = 0; i < show.Count; i++)
            {
                var r = show[i].Value.R[key];
                var rank = RankOf(r.Mmr);
                bool me = show[i].Key == id;
                Box(c, R0, me ? ColWineSoft : ColRow, M + 10, y, lw - 20, 34, 10f);
                Text(c, R0, (i + 1).ToString(), 13, M + 20, y, 30, 34, TextAnchor.MiddleLeft, i == 0 ? ColGold : i < 3 ? ColText : ColDim);
                Text(c, R0, show[i].Value.Name ?? "?", 13, M + 52, y, lw - 320, 34);
                Badge(c, R0, rank, M + lw - 250, y + 6, 22);
                Text(c, R0, RankLabel(r.Mmr, id), 12, M + lw - 222, y, 90, 34, TextAnchor.MiddleLeft, rank.Color);
                Text(c, R0, r.Mmr.ToString(), 13, M + lw - 134, y, 56, 34, TextAnchor.MiddleCenter);
                Text(c, R0, r.Matches > 0 ? Mathf.RoundToInt(100f * r.Wins / r.Matches) + "%" : "—", 13, M + lw - 74, y, 56, 34, TextAnchor.MiddleCenter);
                y += 38;
            }
            if (show.Count == 0) Text(c, R0, T(id, "TopEmpty"), 13, M, top, lw, ph, TextAnchor.MiddleCenter, ColDim, FontReg);
            if (myPlace >= 10)
            {
                var r = list[myPlace].Value.R[key];
                Box(c, R0, ColWineSoft, M + 10, top + ph - 44, lw - 20, 34, 10f);
                Text(c, R0, (myPlace + 1).ToString(), 13, M + 20, top + ph - 44, 40, 34, TextAnchor.MiddleLeft);
                Text(c, R0, player.displayName, 13, M + 62, top + ph - 44, lw - 330, 34);
                Text(c, R0, r.Mmr.ToString(), 13, M + lw - 134, top + ph - 44, 56, 34, TextAnchor.MiddleCenter);
            }

            float rx = M + lw + G;
            Box(c, R0, ColPanel, rx, top, rw, ph, 16f);
            string view; if (!_topView.TryGetValue(id, out view)) view = "ranks";
            float tw2 = (rw - 28 - 6) / 2f;
            Btn(c, R0, T(id, "Ranks"), "ranked.ui topview ranks", view == "ranks" ? ColWine : ColBtn, 11, rx + 14, top + 10, tw2, 26, view == "ranks" ? ColText : ColDim, 9f);
            Btn(c, R0, T(id, "HallOfFame"), "ranked.ui topview hall", view == "hall" ? ColWine : ColBtn, 11, rx + 14 + tw2 + 6, top + 10, tw2, 26, view == "hall" ? ColText : ColDim, 9f);
            if (view == "hall")
            {
                // зал славы: топ-3 прошлых сезонов в выбранной лестнице и формате
                var hall = _data.Players.SelectMany(kv => kv.Value.Seasons.Where(x => x.Ladder == ladder && x.Format == format && x.Place <= 3)
                        .Select(x => new { x.Season, x.Place, x.Mmr, Name = kv.Value.Name ?? kv.Key.ToString() }))
                    .GroupBy(x => x.Season).OrderByDescending(g2 => g2.Key).Take(4).ToList();
                float hy = top + 46;
                if (hall.Count == 0) Text(c, R0, T(id, "HallEmpty"), 12, rx + 14, top + 46, rw - 28, ph - 120, TextAnchor.MiddleCenter, ColDim, FontReg);
                foreach (var g2 in hall)
                {
                    Text(c, R0, T(id, "SeasonN", g2.Key), 11, rx + 14, hy, rw - 28, 16, TextAnchor.MiddleLeft, ColGold);
                    hy += 18;
                    foreach (var x in g2.OrderBy(x => x.Place).Take(3))
                    {
                        Box(c, R0, ColRow, rx + 12, hy, rw - 24, 22, 7f);
                        Text(c, R0, $"#{x.Place}", 11, rx + 20, hy, 30, 22, TextAnchor.MiddleLeft, x.Place == 1 ? ColGold : ColText);
                        Text(c, R0, x.Name, 11, rx + 48, hy, rw - 130, 22, TextAnchor.MiddleLeft, ColText, FontReg);
                        Text(c, R0, x.Mmr.ToString(), 11, rx + rw - 80, hy, 62, 22, TextAnchor.MiddleRight, ColDim);
                        hy += 24;
                    }
                    hy += 6;
                }
            }
            for (int i = 0; i < _config.Ranks.Count && view == "ranks"; i++)
            {
                var rk = _config.Ranks[i];
                float ry = top + 46 + i * 38;
                Badge(c, R0, rk, rx + 14, ry, 30);
                Text(c, R0, RankName(rk, id), 13, rx + 54, ry, rw - 140, 30, TextAnchor.MiddleLeft, rk.Color);
                Text(c, R0, rk.From + "+", 12, rx + rw - 90, ry, 76, 30, TextAnchor.MiddleRight, ColDim);
            }
            if (view == "ranks") Text(c, R0, T(id, "SeasonRules", _config.SeasonDays) + (_config.Divisions > 1 ? " " + T(id, "DivRules") : ""), 10, rx + 14, top + ph - 60, rw - 28, 50, TextAnchor.LowerCenter, ColDim, FontReg);

            float by = H - M - 46, bw = (W - 2 * M - G) / 2f;
            Btn(c, R0, T(id, "FindMatch"), "ranked.ui queue", ColWine, 14, M, by, bw, 46, ColText, 14f);
            var me0 = R(id, ladder, format);
            Box(c, R0, ColRow, M + bw + G, by, bw, 46, 14f);
            Text(c, R0, T(id, "YourPlace", myPlace >= 0 ? (myPlace + 1).ToString() : "—", me0.Mmr, me0.Peak), 12, M + bw + G, by, bw, 46, TextAnchor.MiddleCenter, ColDim);
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        #endregion

        #region Профиль и API

        private string OnHubProfile(ulong userId, string lng)
        {
            PData d;
            if (!_data.Players.TryGetValue(userId, out d) || d.R.Count == 0) return null;
            bool en = lng == "en";
            var lines = new List<string> { en ? "<color=#f0a6b1>RANKED</color>" : "<color=#f0a6b1>РАНКЕД</color>" };
            foreach (var l in Ladders)
            {
                var best = d.R.Where(kv => kv.Key.StartsWith(l + "_") && kv.Value.Matches > 0).OrderByDescending(kv => kv.Value.Mmr).FirstOrDefault();
                if (best.Value == null) continue;
                var rank = RankOf(best.Value.Mmr);
                string f = best.Key.Split('_')[1];
                string ln = lang.GetMessage("L_" + l, this, en ? null : userId.ToString());
                lines.Add($"{ln} {f}v{f}: <color=#{ColorHex(rank.Color)}>{(en ? rank.NameEn : rank.Name)}{Div(best.Value.Mmr)}</color> · {best.Value.Mmr} MMR · {best.Value.Wins}–{best.Value.Losses}");
            }
            if (d.Titles.Count > 0) lines.Add((en ? "Titles: " : "Титулы: ") + string.Join(", ", d.Titles.Take(3).ToArray()));
            return string.Join("\n", lines.ToArray());
        }

        private static string ColorHex(string rgba)
        {
            var p = rgba.Split(' ');
            if (p.Length < 3) return "ffffff";
            Func<string, string> h = s => Mathf.Clamp(Mathf.RoundToInt(float.Parse(s, System.Globalization.CultureInfo.InvariantCulture) * 255f), 0, 255).ToString("x2");
            return h(p[0]) + h(p[1]) + h(p[2]);
        }

        [HookMethod("GetRank")]
        public string GetRank(ulong id, string ladder, int format)
        {
            PData d;
            Rating r;
            if (!_data.Players.TryGetValue(id, out d) || !d.R.TryGetValue(Key(ladder, format), out r)) return null;
            return RankOf(r.Mmr).Name + Div(r.Mmr);
        }

        [HookMethod("GetBestRank")]
        public string GetBestRank(ulong id)
        {
            PData d;
            if (!_data.Players.TryGetValue(id, out d) || d.R.Count == 0) return null;
            var best = d.R.Values.Where(r => r.Matches > 0).OrderByDescending(r => r.Mmr).FirstOrDefault();
            return best == null ? null : RankOf(best.Mmr).Name + Div(best.Mmr);
        }

        [HookMethod("GetBestMmr")]
        public int GetBestMmr(ulong id)
        {
            PData d;
            if (!_data.Players.TryGetValue(id, out d)) return 0;
            var best = d.R.Values.Where(x => x.Matches > 0).OrderByDescending(x => x.Mmr).FirstOrDefault();
            return best?.Mmr ?? 0;
        }

        [HookMethod("GetTitles")]
        public List<string> GetTitles(ulong id)
        {
            PData d;
            return _data.Players.TryGetValue(id, out d) ? d.Titles.ToList() : new List<string>();
        }

        [HookMethod("IsInRanked")] public bool IsInRanked(BasePlayer player) => MatchOf(player) != null;

        #endregion

        #region Язык

        private string T(ulong id, string key, params object[] args)
        {
            var msg = lang.GetMessage(key, this, id == 0 ? null : id.ToString());
            if (args == null || args.Length == 0) return msg;
            try { return string.Format(msg, args); } catch { return msg; }
        }

        private static readonly string[][] Messages =
        {
            new[] { "Title", "РАНКЕД", "RANKED" },
            new[] { "Sub", "сезон {0} · {1} · до {2} побед", "season {0} · {1} · first to {2}" },
            new[] { "DaysLeft", "осталось {0} дн.", "{0} days left" }, new[] { "HoursLeft", "осталось {0} ч", "{0} h left" },
            new[] { "L_ak", "Калаши", "AK only" }, new[] { "L_mix", "Разные ганы", "Mixed guns" },
            new[] { "LD_ak", "АК + одинаковый сет у всех", "AK and the same kit for everyone" },
            new[] { "LD_mix", "каждый раунд случайная пушка и сет", "random gun and kit every round" },
            new[] { "L_t2", "Тир 2", "Tier 2" }, new[] { "LD_t2", "Томпсон, MP5, SAR · дорожная броня", "Thompson, MP5, SAR · roadsign armor" },
            new[] { "L_prim", "Примитив", "Primitive" }, new[] { "LD_prim", "луки, арбалет, Eoka, револьвер, копьё", "bows, crossbow, Eoka, revolver, spear" },
            new[] { "RoadsignSet", "роадсайн", "roadsign" },
            new[] { "HallOfFame", "ЗАЛ СЛАВЫ", "HALL OF FAME" }, new[] { "HallEmpty", "Сезоны ещё не завершались — первые чемпионы появятся здесь", "No finished seasons yet" },
            new[] { "SeasonN", "СЕЗОН {0}", "SEASON {0}" },
            new[] { "DivRules", "Внутри ранга три ступени: III → II → I.", "Each rank has three steps: III → II → I." },
            new[] { "SmurfHours", "{0}: для ранкеда нужно {1} ч на сервере (сейчас {2} ч)", "{0}: ranked needs {1}h on the server (now {2}h)" },
            new[] { "SmurfAge", "{0}: Steam-аккаунт младше {1} дней — ранкед недоступен", "{0}: Steam account younger than {1} days — no ranked" },
            new[] { "SmurfPrivate", "{0}: профиль Steam закрыт — откройте его для ранкеда", "{0}: Steam profile is private — make it public for ranked" },
            new[] { "SmurfChecking", "Проверяем аккаунт Steam… нажми «Найти матч» ещё раз через пару секунд", "Checking Steam account… press Find match again in a moment" },
            new[] { "Spectating", "Наблюдаешь за {0} · вернёшься в следующем раунде", "Spectating {0} · back next round" },
            new[] { "InQueue", "{0} в очереди", "{0} in queue" }, new[] { "Format", "ФОРМАТ", "FORMAT" },
            new[] { "Party", "ТВОЯ КОМАНДА · {0} / {1}", "YOUR PARTY · {0} / {1}" }, new[] { "You", " (ты)", " (you)" },
            new[] { "PartyHint", "Пати = твоя команда в Rust (меню Tab, вкладка «Команда»). В очередь ставит лидер, недостающих добирает поиск.", "Party = your Rust team. The leader queues; missing players are filled by matchmaking." },
            new[] { "PartyTooBigShort", "В команде больше {0} — выбери формат побольше", "Your team is bigger than {0}" },
            new[] { "ReadyText", "Нажми «Найти матч». Пока идёт поиск, можно играть в любом режиме — когда соперники найдутся, появится окно «Матч найден».", "Press Find match. You can play any mode while searching." },
            new[] { "FindMatch", "НАЙТИ МАТЧ", "FIND MATCH" }, new[] { "CancelSearch", "ОТМЕНИТЬ ПОИСК", "CANCEL SEARCH" },
            new[] { "Searching", "Ищем соперников…", "Searching…" },
            new[] { "SearchInfo", "{0} · {1}v{1} · диапазон ±{2} MMR", "{0} · {1}v{1} · range ±{2} MMR" },
            new[] { "InMatchNow", "Ты в матче", "You are in a match" }, new[] { "LeaveMatch", "СДАТЬСЯ И ВЫЙТИ", "FORFEIT" },
            new[] { "TopBtn", "ТОП", "TOP" },
            new[] { "Queued", "В очереди: {0} · {1}v{1}", "Queued: {0} · {1}v{1}" },
            new[] { "AlreadyQueued", "Ты уже в очереди", "Already queued" }, new[] { "InMatch", "Ты уже в матче", "Already in a match" },
            new[] { "NoArenas", "Нет арен для {0}v{0} — админ: /rankedadmin", "No arenas for {0}v{0}" },
            new[] { "OnlyLeader", "В очередь ставит лидер команды", "Only the team leader can queue" },
            new[] { "PartyTooBig", "В команде {0}, а формат {1}v{1}", "Team of {0} is too big for {1}v{1}" },
            new[] { "PartyBusy", "Кто-то из команды уже в очереди или матче", "Someone in your team is busy" },
            new[] { "QueueLeft", "{0} вышел из очереди", "{0} left the queue" },
            new[] { "NotAccepted", "Матч не подтверждён — ты вышел из очереди", "Match not accepted — removed from queue" },
            new[] { "Requeued", "Кто-то не подтвердил — ищем дальше (ты в начале очереди)", "Someone declined — back in queue with priority" },
            new[] { "Found", "МАТЧ НАЙДЕН", "MATCH FOUND" },
            new[] { "FoundSub", "арена «{0}» · средний рейтинг {1} vs {2}", "arena '{0}' · avg rating {1} vs {2}" },
            new[] { "AcceptBtn", "ПРИНЯТЬ · {0} с", "ACCEPT · {0}s" }, new[] { "DeclineBtn", "Отклонить", "Decline" },
            new[] { "Waiting", "ждём остальных {0}/{1}", "waiting {0}/{1}" },
            new[] { "RoundN", "Раунд {0}", "Round {0}" }, new[] { "ToWins", "до {0}", "to {0}" },
            new[] { "Dropped", "выпало", "rolled" }, new[] { "Set", "сет", "kit" }, new[] { "Fight", "БОЙ!", "FIGHT!" },
            new[] { "RoundWin", "РАУНД ВАШ", "ROUND WON" }, new[] { "RoundLose", "РАУНД ПРОИГРАН", "ROUND LOST" }, new[] { "RoundDraw", "НИЧЬЯ", "DRAW" },
            new[] { "WaitRound", "Ждёшь следующий раунд", "Waiting for the next round" },
            new[] { "Paused", "МАТЧ НА ПАУЗЕ", "MATCH PAUSED" }, new[] { "PausedWho", "{0} вылетел с сервера", "{0} disconnected" },
            new[] { "PausedHint", "если не вернётся — его команде засчитают поражение", "if they don't return, their team loses" },
            new[] { "Resumed", "Игрок вернулся — раунд переигрывается", "Player is back — replaying the round" },
            new[] { "Victory", "ПОБЕДА", "VICTORY" }, new[] { "Defeat", "ПОРАЖЕНИЕ", "DEFEAT" }, new[] { "Forfeit", "техническое", "forfeit" },
            new[] { "ToNext", "{0} MMR · до ранга {1} ещё {2}", "{0} MMR · {2} to {1}" }, new[] { "MaxRank", "высший ранг", "top rank" },
            new[] { "Player", "ИГРОК", "PLAYER" }, new[] { "K", "У", "K" }, new[] { "D", "С", "D" }, new[] { "HS", "ХШ", "HS" }, new[] { "Dmg", "урон", "dmg" },
            new[] { "BackSoon", "+{0} монет · возврат через 6 с", "+{0} coins · returning in 6s" },
            new[] { "ForWin", "за победу в ранкеде", "ranked win" }, new[] { "ForMatch", "за ранкед-матч", "ranked match" },
            new[] { "TopTitle", "ТАБЛИЦА ЛИДЕРОВ", "LEADERBOARD" },
            new[] { "TopSub", "сезон {0} · {1} · топ-3 получают титул и монеты", "season {0} · {1} · top 3 get a title and coins" },
            new[] { "Rank", "РАНГ", "RANK" }, new[] { "WinPct", "побед", "win %" }, new[] { "Ranks", "РАНГИ", "RANKS" },
            new[] { "TopEmpty", "Пока никто не сыграл — будь первым", "Nobody has played yet" },
            new[] { "SeasonRules", "Сезон длится {0} дней. В конце рейтинг частично сбрасывается, топ-3 в каждом формате получают награду.", "Seasons last {0} days. Ratings are softly reset; top 3 per format are rewarded." },
            new[] { "YourPlace", "Ты: место {0} · {1} MMR · пик {2}", "You: #{0} · {1} MMR · peak {2}" },
            new[] { "SeasonEnded", "Сезон {0} завершён! Начался сезон {1}", "Season {0} is over! Season {1} has started" },
            new[] { "SeasonReward", "награда сезона", "season reward" },
            new[] { "NoPerm", "Нет прав", "No permission" }, new[] { "Empty", "Пусто", "Empty" }, new[] { "AdmOk", "Сохранено", "Saved" },
            new[] { "AdmCreated", "Арена {0} создана (центр здесь). Поставь точки команд: /rankedadmin a {0} и b {0} — по 5 штук", "Arena {0} created" },
            new[] { "SeasonInfo", "Сезон {0}, конец {1} UTC", "Season {0}, ends {1} UTC" },
            new[] { "AdminHelp",
                "<color=#f0a6b1>/rankedadmin</color>\n" +
                "create <id> — арена (центр = где стоишь)\n" +
                "a <id> · b <id> — точки команд (по 5 для 5v5) · clear <id>\n" +
                "name <id> <название> · radius <id> <м> · formats <id> 1 2 3 5\n" +
                "enable|disable|delete <id> · list\n" +
                "season — инфо · season end — завершить сезон сейчас\n" +
                "setmmr <ник> <ak|mix> <1|2|3|5> <mmr>",
                "<color=#f0a6b1>/rankedadmin</color> create · a · b · clear · name · radius · formats · enable · disable · delete · list · season [end] · setmmr" },
        };

        private void RegisterLang()
        {
            var ru = new Dictionary<string, string>();
            var en = new Dictionary<string, string>();
            foreach (var m in Messages) { ru[m[0]] = m[1]; en[m[0]] = m[2]; }
            lang.RegisterMessages(en, this, "en");
            lang.RegisterMessages(ru, this, "ru");
        }

        #endregion
    }
}
