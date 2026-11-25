// =====================================================================================
//  Arenas 1.0 — PvP-арены для хаб-сервера Rust (Oxide / Carbon)
// =====================================================================================
//  FFA (арена = пушка), команда на команду (TDM), ган-гейм со случайной лестницей,
//  дуэли /duel (оружие, сет, арена, раунды, ставка монетами), свои арены из шаблонов
//  (набор, команды 1v1–5v5, раунды, баррикады, публичная/по паролю).
//  Экран смерти 2,5 с с авто-возрождением, киллфид, HUD, серии, точность, хедшоты,
//  топ за день/неделю/всё время, монеты за убийства и победы.
//
//  Работает вместе с Hub (режимы ffa / duels / primitive), но может и без него.
//
//  Права:  arenas.admin  — разметка арен
//          arenas.create — создавать свои арены (по умолчанию всем, см. конфиг)
//  Чат:    /arena — список арен · /arena leave · /arena top
//          /duel <ник> — вызвать на дуэль · /duel accept · /duel decline
//  Админ:  /arenaadmin — команды разметки
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
    [Info("Arenas", "Claude", "1.3.0")]
    [Description("FFA, TDM, ган-гейм, дуэли и свои арены с экраном смерти, киллфидом и статистикой")]
    public class Arenas : RustPlugin
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

        private const string PermAdmin = "arenas.admin";
        private const string PermCreate = "arenas.create";

        private const string UiMain = "Ar.Main";
        private const string UiHud = "Ar.Hud";
        private const string UiFeed = "Ar.Feed";
        private const string UiCenter = "Ar.Center";
        private const string UiDeath = "Ar.Death";
        private const string UiPopup = "Ar.Popup";
        private const string UiToast = "Ar.Toast";

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

        #endregion

        #region Конфиг

        private class KitEntry
        {
            [JsonProperty("Предмет")] public string Item;
            [JsonProperty("Кол-во")] public int Amount = 1;
            [JsonProperty("Скин")] public ulong Skin;
            [JsonProperty("Куда (main/belt/wear)")] public string Container = "main";
            [JsonProperty("Модули")] public List<string> Mods = new List<string>();
        }

        private class Loadout
        {
            [JsonProperty("ID")] public string Id;
            [JsonProperty("Название")] public string Name;
            [JsonProperty("Описание")] public string Desc;
            [JsonProperty("Предметы")] public List<KitEntry> Items = new List<KitEntry>();
        }

        private class GunStep
        {
            [JsonProperty("Оружие")] public string Weapon;
            [JsonProperty("Название")] public string Name;
            [JsonProperty("Патроны")] public string Ammo;
            [JsonProperty("Кол-во патронов")] public int AmmoAmount = 128;
            [JsonProperty("Модули")] public List<string> Mods = new List<string>();
        }

        private class Configuration
        {
            [JsonProperty("Версия конфига")] public int ConfigVersion = 3;
            [JsonProperty("Лабиринт: префаб стены")] public string MazePrefab = "assets/prefabs/misc/xmas/icewalls/icewall.prefab";
            [JsonProperty("Лабиринт: ширина одной стены (м)")] public float MazeWallWidth = 6f;
            [JsonProperty("Лабиринт: размер клетки (м)")] public float MazeCell = 6f;
            [JsonProperty("Лабиринт: доля лишних проходов (петли для FFA)")] public float MazeLoops = 0.12f;
            [JsonProperty("Лабиринт: FFA — новый лабиринт каждые (мин)")] public float MazeRegenMin = 5f;
            [JsonProperty("Роум: префаб большой стены")] public string RoamPrefab = "assets/prefabs/building/wall.external.high.stone/wall.external.high.stone.prefab";
            [JsonProperty("Роум: стен на арене")] public int RoamWalls = 26;
            [JsonProperty("Возрождение после смерти (сек)")] public float RespawnDelay = 1.5f;
            [JsonProperty("За убийство: восстановить здоровье (FFA, TDM, ган-гейм)")] public bool KillHeal = true;
            [JsonProperty("За убийство: сколько ХП (0 = полностью)")] public float KillHealAmount = 0f;
            [JsonProperty("За убийство: пополнить патроны и магазин")] public bool KillRefill = true;
            [JsonProperty("Большие арены: баррикад в наборе FFA/TDM")] public int LargeBarricades = 2;
            [JsonProperty("Без «ранен» на аренах (сразу смерть)")] public bool NoWounded = true;
            [JsonProperty("TDM: убийств до победы")] public int TdmKills = 30;
            [JsonProperty("Ган-гейм: длина лестницы (0 = все пушки)")] public int GunGameLength = 9;
            [JsonProperty("Матч: отсчёт перед раундом (сек)")] public int RoundCountdown = 3;
            [JsonProperty("Дуэль: приглашение действует (сек)")] public float InviteTimeout = 30f;
            [JsonProperty("Своя арена: максимум у игрока")] public int MaxOwnArenas = 1;
            [JsonProperty("Своя арена: всем можно создавать (иначе право arenas.create)")] public bool CreateForAll = true;
            [JsonProperty("Своя арена: закрыть пустую через (сек)")] public float EmptyClose = 180f;
            [JsonProperty("Монеты: убийство")] public int CoinsKill = 1;
            [JsonProperty("Монеты: серия 5")] public int CoinsStreak5 = 5;
            [JsonProperty("Монеты: серия 10")] public int CoinsStreak10 = 15;
            [JsonProperty("Монеты: победа TDM")] public int CoinsTdmWin = 10;
            [JsonProperty("Монеты: победа в ган-гейме")] public int CoinsGunGameWin = 25;
            [JsonProperty("Монеты: победа в дуэли")] public int CoinsDuelWin = 10;
            [JsonProperty("Ставки в дуэлях")] public List<int> Bets = new List<int> { 0, 50, 100, 500 };
            [JsonProperty("Раунды в дуэлях")] public List<int> RoundOptions = new List<int> { 1, 3, 5, 7 };
            [JsonProperty("Режим Hub для дуэлей и своих арен")] public string DuelHubMode = "duels";
            [JsonProperty("Скруглённые углы интерфейса")] public bool RoundedCorners = true;
            [JsonProperty("Наборы")] public List<Loadout> Loadouts = DefaultLoadouts();
            [JsonProperty("Ган-гейм: пушки")] public List<GunStep> GunGame = DefaultGunGame();
        }

        private static KitEntry W(string i) => new KitEntry { Item = i, Container = "wear" };
        private static KitEntry B(string i, int a = 1, params string[] mods) => new KitEntry { Item = i, Amount = a, Container = "belt", Mods = mods.ToList() };
        private static KitEntry Mn(string i, int a) => new KitEntry { Item = i, Amount = a };

        private static List<Loadout> DefaultLoadouts()
        {
            var roadsign = new[] { W("metal.facemask"), W("roadsign.jacket"), W("roadsign.kilt"), W("hoodie"), W("pants"), W("shoes.boots"), W("tactical.gloves") };
            var hqm = new[] { W("metal.facemask"), W("metal.plate.torso"), W("roadsign.kilt"), W("hoodie"), W("pants"), W("shoes.boots"), W("tactical.gloves") };
            var wood = new[] { W("wood.armor.helmet"), W("wood.armor.jacket"), W("wood.armor.pants"), W("hoodie"), W("shoes.boots") };
            var kevlar = new[] { W("metal.facemask"), W("heavy.plate.jacket"), W("heavy.plate.pants"), W("shoes.boots"), W("tactical.gloves") };
            var heal = new[] { B("syringe.medical", 2), B("bandage", 3) };
            Func<string, string, string, IEnumerable<KitEntry>, IEnumerable<KitEntry>, Loadout> L = (id, n, d, wear, belt) =>
                new Loadout { Id = id, Name = n, Desc = d, Items = wear.Concat(belt).Concat(heal).ToList() };
            return new List<Loadout>
            {
                L("ak", "AK + голо", "роадсайн · 2 шприца · 3 бинта", roadsign, new[] { B("rifle.ak", 1, "weapon.mod.holosight"), Mn("ammo.rifle", 256) }),
                L("lr", "LR-300 + 8x", "кевлар · 2 шприца · 3 бинта", kevlar, new[] { B("rifle.lr300", 1, "weapon.mod.small.scope"), Mn("ammo.rifle", 256) }),
                L("mp5", "MP5 + глушитель", "деревянный сет", wood, new[] { B("smg.mp5", 1, "weapon.mod.silencer"), Mn("ammo.pistol", 256) }),
                L("bolt", "Болт + 16x", "без брони", new KitEntry[] { W("hoodie"), W("pants"), W("shoes.boots") }, new[] { B("rifle.bolt", 1, "weapon.mod.8x.scope"), B("pistol.python"), Mn("ammo.rifle", 128), Mn("ammo.pistol", 64) }),
                L("thompson", "Томпсон", "МВК-сет", hqm, new[] { B("smg.thompson", 1, "weapon.mod.holosight"), Mn("ammo.pistol", 256) }),
                L("python", "Питон", "роадсайн", roadsign, new[] { B("pistol.python", 1, "weapon.mod.holosight"), Mn("ammo.pistol", 128) }),
                L("primitive", "Примитив", "копьё, лук, арбалет, Eoka, револьвер", new KitEntry[] { W("burlap.shirt"), W("burlap.trousers"), W("burlap.shoes") },
                    new[] { B("spear.wooden"), B("bow.hunting"), B("crossbow"), B("pistol.eoka"), B("pistol.revolver"), Mn("arrow.wooden", 64), Mn("ammo.handmade.shell", 32), Mn("ammo.pistol", 64) }),
                L("tier2", "Тир 2: Томпсон / MP5 / SAR", "роадсайн · 2 шприца · 3 бинта", roadsign,
                    new[] { B("smg.thompson", 1, "weapon.mod.holosight"), B("smg.mp5"), B("rifle.semiauto"), Mn("ammo.pistol", 256), Mn("ammo.rifle", 128) }),
                L("bows", "Луки: лук + арбалет", "шкурный сет · стрелы", new KitEntry[] { W("deer.skull.mask"), W("hide.poncho"), W("hide.pants"), W("hide.boots") },
                    new[] { B("bow.hunting"), B("crossbow"), Mn("arrow.wooden", 64), Mn("arrow.hv", 32) }),
                L("speargun", "Гарпуны", "водолазный сет · бой в воде", new KitEntry[] { W("diving.mask"), W("diving.tank"), W("diving.fins"), W("diving.wetsuit") },
                    new[] { B("speargun"), Mn("speargun.spear", 32) }),
                L("tier3", "Тир 3: AK / LR / M249", "МВК-сет · 2 шприца · 3 бинта", hqm,
                    new[] { B("rifle.ak", 1, "weapon.mod.holosight"), B("rifle.lr300"), B("lmg.m249"), Mn("ammo.rifle", 400) }),
            };
        }

        private static List<GunStep> DefaultGunGame()
        {
            Func<string, string, string, int, string[], GunStep> G = (w, n, a, c, m) => new GunStep { Weapon = w, Name = n, Ammo = a, AmmoAmount = c, Mods = m.ToList() };
            return new List<GunStep>
            {
                G("rifle.ak", "AK", "ammo.rifle", 180, new[] { "weapon.mod.holosight" }),
                G("rifle.lr300", "LR-300", "ammo.rifle", 180, new string[0]),
                G("lmg.m249", "M249", "ammo.rifle", 200, new string[0]),
                G("smg.mp5", "MP5", "ammo.pistol", 180, new string[0]),
                G("smg.thompson", "Томпсон", "ammo.pistol", 180, new string[0]),
                G("smg.2", "СМГ", "ammo.pistol", 180, new string[0]),
                G("rifle.semiauto", "SAR", "ammo.rifle", 120, new string[0]),
                G("rifle.bolt", "Болт", "ammo.rifle", 60, new[] { "weapon.mod.small.scope" }),
                G("pistol.python", "Питон", "ammo.pistol", 60, new string[0]),
                G("pistol.revolver", "Револьвер", "ammo.pistol", 60, new string[0]),
                G("shotgun.pump", "Помпа", "ammo.shotgun", 40, new string[0]),
                G("crossbow", "Арбалет", "arrow.wooden", 30, new string[0]),
                G("bow.hunting", "Лук", "arrow.wooden", 40, new string[0]),
                G("rock", "Камень", null, 0, new string[0]),
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
                if (_config == null) throw new Exception();
                if (_config.Loadouts == null || _config.Loadouts.Count == 0) _config.Loadouts = DefaultLoadouts();
                if (_config.GunGame == null || _config.GunGame.Count == 0) _config.GunGame = DefaultGunGame();
                if (_config.ConfigVersion < 2)
                {
                    // 1.2: примитив с Eoka и револьвером, наборы по тирам, возрождение быстрее
                    var defs = DefaultLoadouts();
                    var prim = _config.Loadouts.FindIndex(l => l.Id == "primitive");
                    if (prim >= 0) _config.Loadouts[prim] = defs.First(l => l.Id == "primitive");
                    foreach (var l in defs) if (!_config.Loadouts.Any(x => x.Id == l.Id)) _config.Loadouts.Add(l);
                    if (Mathf.Approximately(_config.RespawnDelay, 2.5f)) _config.RespawnDelay = 1.5f;
                    _config.ConfigVersion = 2;
                    PrintWarning("Конфиг обновлён до 1.2: примитив с Eoka и револьвером, наборы «Тир 2» и «Тир 3», возрождение 1.5 с");
                }
                if (_config.ConfigVersion < 3)
                {
                    // 1.3: наборы «Луки» и «Гарпуны», лабиринт и роум
                    foreach (var l in DefaultLoadouts()) if (!_config.Loadouts.Any(x => x.Id == l.Id)) _config.Loadouts.Add(l);
                    _config.ConfigVersion = 3;
                    PrintWarning("Конфиг обновлён до 1.3: наборы «Луки» и «Гарпуны», лабиринт и роум");
                }
            }
            catch
            {
                PrintWarning("Конфиг повреждён — создаю новый");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private Loadout LoadoutById(string id) => _config.Loadouts.FirstOrDefault(l => l.Id == id) ?? _config.Loadouts.FirstOrDefault();

        #endregion

        #region Данные

        private class Pt
        {
            public float X, Y, Z, Yaw;
            [JsonIgnore] public Vector3 V => new Vector3(X, Y, Z);
            public static Pt From(Vector3 v, float yaw) => new Pt { X = v.x, Y = v.y, Z = v.z, Yaw = yaw };
        }

        // kind: ffa | tdm | gungame | match (дуэли и свои арены)
        private class ArenaDef
        {
            public string Id;
            public string Name;
            public string Kind = "ffa";
            public string HubMode = "ffa";
            public string Loadout = "ak";
            public int Max = 16;
            public float Radius = 60f;
            public bool Template;
            public bool Barricades;
            public string Size = "small";            // small — тесная арена, large — открытая местность с баррикадами
            public string Layout = "none";           // none | maze — лабиринт из ледяных стен | roam — большие стены
            public int MazeSize = 9;                 // клеток по стороне
            public bool Enabled = true;
            public Pt Center;
            public List<Pt> Spawns = new List<Pt>();
            public List<Pt> SpawnsA = new List<Pt>();
            public List<Pt> SpawnsB = new List<Pt>();
        }

        private class Stats
        {
            public string Name;
            public int Kills, Deaths, Headshots, Shots, Hits, BestStreak;
            public int DuelWins, DuelLosses, TdmWins, GunGameWins, MatchWins, MatchLosses;
            public Dictionary<string, int> DayKills = new Dictionary<string, int>();
        }

        private class StoredData
        {
            public Dictionary<string, ArenaDef> Arenas = new Dictionary<string, ArenaDef>();
        }

        private StoredData _data;
        private Dictionary<ulong, Stats> _stats;
        private bool _dirty;

        private void LoadData()
        {
            try { _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("Arenas/Arenas"); } catch { _data = null; }
            if (_data == null) _data = new StoredData();
            if (_data.Arenas == null) _data.Arenas = new Dictionary<string, ArenaDef>();
            try { _stats = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, Stats>>("Arenas/Stats"); } catch { _stats = null; }
            if (_stats == null) _stats = new Dictionary<ulong, Stats>();
        }

        private void SaveArenas() => Interface.Oxide.DataFileSystem.WriteObject("Arenas/Arenas", _data);

        private void SaveStats()
        {
            Interface.Oxide.DataFileSystem.WriteObject("Arenas/Stats", _stats);
            _dirty = false;
        }

        private Stats S(ulong id, string name = null)
        {
            Stats s;
            if (!_stats.TryGetValue(id, out s)) _stats[id] = s = new Stats();
            if (s.DayKills == null) s.DayKills = new Dictionary<string, int>();
            if (name != null) s.Name = name;
            _dirty = true;
            return s;
        }

        private static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd");

        #endregion

        #region Сессии

        private class FeedLine { public string Text; public float Until; }

        private class Session
        {
            public string Id;
            public ArenaDef Arena;
            public string Kind;                       // ffa | tdm | gungame | match
            public Loadout Loadout;
            public HashSet<ulong> Players = new HashSet<ulong>();
            public Dictionary<ulong, int> Kills = new Dictionary<ulong, int>();
            public Dictionary<ulong, int> Deaths = new Dictionary<ulong, int>();
            public Dictionary<ulong, int> Streak = new Dictionary<ulong, int>();
            public Dictionary<ulong, int> Team = new Dictionary<ulong, int>();     // 0 = A (красные), 1 = B (синие)
            public int[] Score = new int[2];
            public List<FeedLine> Feed = new List<FeedLine>();
            public bool Resetting;

            // ган-гейм
            public List<GunStep> Ladder = new List<GunStep>();
            public Dictionary<ulong, int> Level = new Dictionary<ulong, int>();

            // матч (дуэль / своя арена)
            public bool IsDuel;
            public ulong Owner;
            public string OwnerName;
            public string Password;
            public bool Public = true;
            public int TeamSize = 1;
            public int RoundsToWin = 3;
            public int Bet;
            public bool AllowBarricades;
            public string State = "waiting";         // waiting | countdown | live | between | ended
            public int Round;
            public HashSet<ulong> Alive = new HashSet<ulong>();
            public float EmptySince;

            public List<BaseEntity> Built = new List<BaseEntity>();
            // лабиринт / роум: стены и точки появления, пересоздаются каждый раунд
            public List<BaseEntity> LayoutEnts = new List<BaseEntity>();
            public List<Pt> LAll = new List<Pt>(), LA = new List<Pt>(), LB = new List<Pt>();
            public float NextRegen;
            public string Tag;                       // матч турнира (Social)
        }

        private readonly Dictionary<string, Session> _sessions = new Dictionary<string, Session>();
        private readonly Dictionary<ulong, Session> _in = new Dictionary<ulong, Session>();
        private readonly Dictionary<ulong, string> _pendingJoin = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, Timer> _respawn = new Dictionary<ulong, Timer>();
        private readonly Dictionary<ulong, float> _deathAt = new Dictionary<ulong, float>();

        private class DuelInvite
        {
            public ulong From, To;
            public string FromName;
            public string Loadout = "ak";
            public string Arena;
            public int Rounds = 3;
            public int Bet;
            public float Until;
        }

        private readonly Dictionary<ulong, DuelInvite> _invites = new Dictionary<ulong, DuelInvite>();   // по получателю
        private readonly Dictionary<ulong, DuelInvite> _drafts = new Dictionary<ulong, DuelInvite>();    // по отправителю

        private class CustomDraft
        {
            public string Template;
            public string Loadout = "ak";
            public int TeamSize = 1;
            public int Rounds = 3;
            public bool Barricades;
            public bool Public = true;
            public string Password = "";
        }

        private readonly Dictionary<ulong, CustomDraft> _customDrafts = new Dictionary<ulong, CustomDraft>();
        private readonly Dictionary<ulong, string> _tab = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> _pwInput = new Dictionary<ulong, string>();

        private static ulong Id(BasePlayer p) => (ulong)p.userID;

        private bool IsAdmin(BasePlayer p) => p != null && (p.IsAdmin || permission.UserHasPermission(p.UserIDString, PermAdmin));

        private Session SessionOf(BasePlayer p) { Session s; return p != null && _in.TryGetValue(Id(p), out s) ? s : null; }

        private string HubMode(BasePlayer p) => Hub != null ? Hub.Call("GetMode", p) as string : null;

        #endregion

        #region Хуки сервера

        private Timer _tick, _save;

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermCreate, this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            RegisterLang();
            InitRoundedSprite();
            _tick = timer.Every(1f, Tick);
            _save = timer.Every(120f, () => { if (_dirty) SaveStats(); });
        }

        private void Unload()
        {
            foreach (var s in _sessions.Values.ToList()) CleanupBuilt(s);
            foreach (var p in BasePlayer.activePlayerList)
                foreach (var ui in new[] { UiMain, UiHud, UiFeed, UiCenter, UiDeath, UiPopup, UiToast }) { CuiHelper.DestroyUi(p, ui); CuiHelper.DestroyUi(p, ui + ".dim"); }
            SaveStats();
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            var id = Id(player);
            _pendingJoin.Remove(id);
            _invites.Remove(id);
            Leave(player, false);
        }

        // Hub: игрок сменил режим — выходим из арены, или заходим в отложенную
        private void OnHubModeChanged(BasePlayer player, string from, string to)
        {
            if (player == null) return;
            var s = SessionOf(player);
            if (s != null && (s.Arena == null || s.Arena.HubMode != to) && !(s.Kind == "match" && to == _config.DuelHubMode)) Leave(player, false);
            string pending;
            if (_pendingJoin.TryGetValue(Id(player), out pending))
            {
                _pendingJoin.Remove(Id(player));
                timer.Once(0.5f, () => { if (player != null && player.IsConnected) Join(player, pending, null); });
            }
        }

        #endregion

        #region Вход и выход

        private Session GetOrCreatePublic(ArenaDef a)
        {
            Session s;
            if (_sessions.TryGetValue(a.Id, out s)) return s;
            s = new Session { Id = a.Id, Arena = a, Kind = a.Kind, Loadout = LoadoutById(a.Loadout) };
            if (a.Kind == "gungame") NewLadder(s);
            _sessions[a.Id] = s;
            BuildLayout(s);
            return s;
        }

        private void Join(BasePlayer player, string sessionOrArena, string password)
        {
            var id = Id(player);
            if (player.IsDead()) { Toast(player, T(id, "DeadNow"), true); return; }
            Session s;
            if (!_sessions.TryGetValue(sessionOrArena, out s))
            {
                ArenaDef a;
                if (!_data.Arenas.TryGetValue(sessionOrArena, out a) || !a.Enabled || a.Kind == "match") { Toast(player, T(id, "NoArena"), true); return; }
                if (a.Layout != "maze" && Spawns(a, -1).Count == 0 && (a.Kind != "tdm" || a.SpawnsA.Count == 0)) { Toast(player, T(id, "NoSpawns", a.Name), true); return; }
                s = GetOrCreatePublic(a);
            }

            var cur = SessionOf(player);
            if (cur == s) { Toast(player, T(id, "AlreadyIn"), false); return; }

            if (s.Kind == "match")
            {
                if (s.State != "waiting") { Toast(player, T(id, "MatchStarted"), true); return; }
                if (!string.IsNullOrEmpty(s.Password) && s.Owner != id && password != s.Password && !IsAdmin(player)) { ShowPassword(player, s.Id); return; }
                if (s.Players.Count >= s.TeamSize * 2) { Toast(player, T(id, "Full"), true); return; }
            }
            else if (s.Players.Count >= s.Arena.Max) { Toast(player, T(id, "Full"), true); return; }

            // режим Hub: арена живёт в своём режиме
            string need = s.Kind == "match" ? _config.DuelHubMode : s.Arena.HubMode;
            if (Hub != null && !string.IsNullOrEmpty(need) && HubMode(player) != need)
            {
                _pendingJoin[id] = s.Id;
                Hub.Call("SendToMode", player, need, false);
                return;
            }

            if (cur != null) Leave(player, false);
            CloseAll(player);
            s.Players.Add(id);
            _in[id] = s;
            s.Kills[id] = 0; s.Deaths[id] = 0; s.Streak[id] = 0;
            S(id, player.displayName);

            if (s.Kind == "tdm" || s.Kind == "match")
            {
                int a0 = s.Team.Count(kv => kv.Value == 0 && s.Players.Contains(kv.Key));
                int b0 = s.Team.Count(kv => kv.Value == 1 && s.Players.Contains(kv.Key));
                s.Team[id] = a0 <= b0 ? 0 : 1;
                if (s.Kind == "match" && s.TeamSize > 0 && (s.Team[id] == 0 ? a0 : b0) >= s.TeamSize) s.Team[id] = 1 - s.Team[id];
            }
            if (s.Kind == "gungame") s.Level[id] = 0;

            if (s.Kind == "match")
            {
                MoveToTeamSpawn(player, s, true);
                Heal(player);
                player.inventory.Strip();
                BroadcastRoom(s);
                return;
            }

            Spawn(player, s);
            Broadcast(s, T(0, "Joined", player.displayName), false);
            RefreshHudAll(s);
        }

        private void Leave(BasePlayer player, bool toMode)
        {
            if (player == null) return;
            var id = Id(player);
            CancelRespawn(id);
            var s = SessionOf(player);
            if (s == null) return;
            _in.Remove(id);
            s.Players.Remove(id);
            s.Alive.Remove(id);
            foreach (var ui in new[] { UiHud, UiFeed, UiCenter, UiDeath }) CuiHelper.DestroyUi(player, ui);

            if (s.Kind == "match")
            {
                if (s.State == "countdown" || s.State == "live" || s.State == "between")
                {
                    int team;
                    if (s.Team.TryGetValue(id, out team) && !s.Players.Any(p => s.Team.ContainsKey(p) && s.Team[p] == team))
                        EndMatch(s, 1 - team, true);
                    else CheckRoundEnd(s);
                }
                else if (s.Players.Count == 0 || id == s.Owner && s.IsDuel) CloseMatch(s);
                else
                {
                    if (id == s.Owner) { s.Owner = s.Players.First(); s.OwnerName = S(s.Owner).Name; }
                    BroadcastRoom(s);
                }
            }
            else
            {
                if (s.Players.Count == 0) { CleanupBuilt(s); _sessions.Remove(s.Id); }
                else RefreshHudAll(s);
            }

            if (toMode && player.IsConnected && !player.IsDead())
            {
                player.inventory.Strip();
                if (Hub != null)
                {
                    var mode = HubMode(player);
                    if (!string.IsNullOrEmpty(mode)) Hub.Call("SendToMode", player, mode, true);
                }
            }
        }

        private List<Pt> Spawns(ArenaDef a, int team)
        {
            if (team == 0 && a.SpawnsA.Count > 0) return a.SpawnsA;
            if (team == 1 && a.SpawnsB.Count > 0) return a.SpawnsB;
            if (a.Spawns.Count > 0) return a.Spawns;
            return team == 1 ? a.SpawnsB : a.SpawnsA;
        }

        // лучшая точка: дальше всех от врагов
        private Pt PickSpawn(Session s, ulong id)
        {
            int team = s.Team.ContainsKey(id) ? s.Team[id] : -1;
            var list = SpawnsFor(s, s.Kind == "tdm" || s.Kind == "match" ? team : -1);
            if (list.Count == 0) return null;
            var enemies = s.Players.Where(p => p != id && (team < 0 || !s.Team.ContainsKey(p) || s.Team[p] != team))
                .Select(BasePlayer.FindByID).Where(p => p != null && !p.IsDead()).Select(p => p.transform.position).ToList();
            if (enemies.Count == 0) return list[UnityEngine.Random.Range(0, list.Count)];
            Pt best = null;
            float bestD = -1f;
            foreach (var pt in list.OrderBy(x => UnityEngine.Random.Range(0, 1000)).Take(6))
            {
                float d = enemies.Min(e => Vector3.Distance(e, pt.V));
                if (d > bestD) { bestD = d; best = pt; }
            }
            return best;
        }

        private void Spawn(BasePlayer player, Session s)
        {
            var pt = PickSpawn(s, Id(player));
            if (pt != null) player.Teleport(pt.V + Vector3.up * 0.15f);
            Heal(player);
            GiveLoadout(player, s);
        }

        private void MoveToTeamSpawn(BasePlayer player, Session s, bool any)
        {
            var id = Id(player);
            int team = s.Team.ContainsKey(id) ? s.Team[id] : 0;
            var list = SpawnsFor(s, team);
            if (list.Count == 0) return;
            int idx = s.Players.Where(p => s.Team.ContainsKey(p) && s.Team[p] == team).ToList().IndexOf(id);
            var pt = list[Mathf.Clamp(idx, 0, list.Count - 1) % list.Count];
            player.Teleport(pt.V + Vector3.up * 0.15f);
        }

        #endregion

        #region Наборы

        private void GiveLoadout(BasePlayer player, Session s)
        {
            player.inventory.Strip();
            if (s.Kind == "gungame")
            {
                if (s.Loadout != null) foreach (var k in s.Loadout.Items.Where(x => x.Container == "wear")) GiveEntry(player, k);
                int lvl;
                s.Level.TryGetValue(Id(player), out lvl);
                GiveGunStep(player, s.Ladder[Mathf.Clamp(lvl, 0, s.Ladder.Count - 1)]);
                GiveEntry(player, new KitEntry { Item = "bandage", Amount = 2, Container = "belt" });
                return;
            }
            if (s.Loadout != null) foreach (var k in s.Loadout.Items) GiveEntry(player, k);
            if (s.Kind == "match" && s.AllowBarricades)
                GiveEntry(player, new KitEntry { Item = "barricade.wood.cover", Amount = 2, Container = "belt" });
            else if (s.Kind != "match" && s.Arena != null && s.Arena.Size == "large" && _config.LargeBarricades > 0)
                GiveEntry(player, new KitEntry { Item = "barricade.wood.cover", Amount = _config.LargeBarricades, Container = "belt" });
        }

        private static void GiveEntry(BasePlayer player, KitEntry k)
        {
            var item = ItemManager.CreateByName(k.Item, Mathf.Max(1, k.Amount), k.Skin);
            if (item == null) return;
            if (k.Mods != null && item.contents != null)
                foreach (var m in k.Mods)
                {
                    var mod = ItemManager.CreateByName(m, 1, 0UL);
                    if (mod != null && !mod.MoveToContainer(item.contents)) mod.Remove();
                }
            var proj = item.GetHeldEntity() as BaseProjectile;
            if (proj?.primaryMagazine != null) proj.primaryMagazine.contents = proj.primaryMagazine.capacity;
            var c = k.Container == "wear" ? player.inventory.containerWear : k.Container == "belt" ? player.inventory.containerBelt : player.inventory.containerMain;
            if (!item.MoveToContainer(c) && !item.MoveToContainer(player.inventory.containerMain)) item.Remove();
        }

        private static void GiveGunStep(BasePlayer player, GunStep g)
        {
            GiveEntry(player, new KitEntry { Item = g.Weapon, Container = "belt", Mods = g.Mods });
            if (!string.IsNullOrEmpty(g.Ammo) && g.AmmoAmount > 0) GiveEntry(player, new KitEntry { Item = g.Ammo, Amount = g.AmmoAmount });
        }

        private void NewLadder(Session s)
        {
            var pool = _config.GunGame.ToList();
            var last = pool.LastOrDefault();
            var rest = pool.Take(pool.Count - 1).OrderBy(x => UnityEngine.Random.Range(0, 10000)).ToList();
            int len = _config.GunGameLength > 0 ? Mathf.Min(_config.GunGameLength - 1, rest.Count) : rest.Count;
            s.Ladder = rest.Take(len).ToList();
            if (last != null) s.Ladder.Add(last);   // последняя ступень всегда «камень»
            foreach (var k in s.Players.ToList()) s.Level[k] = 0;
        }

        private static void Heal(BasePlayer player)
        {
            try
            {
                player.health = player.MaxHealth();
                var m = player.metabolism;
                if (m != null)
                {
                    m.calories.value = m.calories.max;
                    m.hydration.value = m.hydration.max;
                    m.bleeding.value = 0f;
                    m.radiation_poison.value = 0f;
                    m.SendChanges();
                }
                player.SendNetworkUpdate();
            }
            catch { }
        }

        #endregion

        #region Бой: урон, смерть, возрождение

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var victim = entity as BasePlayer;
            if (victim == null || info == null) return null;
            var vs = SessionOf(victim);
            var attacker = info.InitiatorPlayer;
            if (attacker == null || attacker == victim) return vs != null && vs.Kind == "match" && vs.State != "live" ? (object)true : null;
            var a = SessionOf(attacker);
            if (vs == null && a == null) return null;
            if (vs != a) return true;                                          // снаружи арены — нельзя
            var vid = Id(victim);
            if (vs.Kind == "match" && (vs.State != "live" || !vs.Alive.Contains(vid) || !vs.Alive.Contains(Id(attacker)))) return true;
            if ((vs.Kind == "tdm" || vs.Kind == "match") && vs.Team.ContainsKey(vid) && vs.Team.ContainsKey(Id(attacker)) && vs.Team[vid] == vs.Team[Id(attacker)]) return true;
            var st = S(Id(attacker));
            st.Hits++;
            return null;
        }

        private void OnWeaponFired(BaseProjectile projectile, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            if (player != null && _in.ContainsKey(Id(player))) S(Id(player)).Shots++;
        }

        private object CanBeWounded(BasePlayer player, HitInfo info)
        {
            return _config.NoWounded && player != null && _in.ContainsKey(Id(player)) ? (object)false : null;
        }

        private void OnPlayerDeath(BasePlayer victim, HitInfo info)
        {
            if (victim == null) return;
            var s = SessionOf(victim);
            if (s == null) return;
            var vid = Id(victim);
            var killer = info?.InitiatorPlayer;
            if (killer == victim) killer = null;
            if (killer != null && SessionOf(killer) != s) killer = null;

            victim.inventory.Strip();
            Inc(s.Deaths, vid);
            s.Streak[vid] = 0;
            var vst = S(vid, victim.displayName);
            vst.Deaths++;

            bool hs = info != null && info.isHeadshot;
            string weapon = WeaponName(info);
            float dist = killer != null ? Vector3.Distance(killer.transform.position, victim.transform.position) : 0f;

            if (killer != null)
            {
                var kid = Id(killer);
                Inc(s.Kills, kid);
                Inc(s.Streak, kid);
                var kst = S(kid, killer.displayName);
                kst.Kills++;
                if (hs) kst.Headshots++;
                int dk; kst.DayKills.TryGetValue(Today, out dk); kst.DayKills[Today] = dk + 1;
                if (kst.DayKills.Count > 8) foreach (var old in kst.DayKills.Keys.OrderBy(k => k).Take(kst.DayKills.Count - 8).ToList()) kst.DayKills.Remove(old);
                int streak = s.Streak[kid];
                if (streak > kst.BestStreak) kst.BestStreak = streak;
                if (CanEarnSilent(kid, Id(victim))) { Ach(kid, "arena_kills"); if (hs) Ach(kid, "arena_hs"); AchMax(kid, "arena_streak", streak); }

                bool earn = CanEarn(kid, Id(victim));
                if (earn) AddCoins(kid, _config.CoinsKill, "");
                if (streak == 5) { if (earn) AddCoins(kid, _config.CoinsStreak5, T(kid, "Streak", 5)); Center(killer, T(kid, "StreakBig", 5), ColGold, 2f); }
                if (streak == 10) { if (earn) AddCoins(kid, _config.CoinsStreak10, T(kid, "Streak", 10)); Broadcast(s, T(0, "StreakAll", killer.displayName, 10), false); }

                // ХП убийцы и подсветка в киллфиде
                AddFeed(s, $"<color=#f0a6b1>{Esc(killer.displayName)}</color>  <color=#a39896>{weapon}{(hs ? " · HS" : "")}</color>  {Esc(victim.displayName)}");

                if (s.Kind == "tdm" && s.Team.ContainsKey(kid)) s.Score[s.Team[kid]]++;
                if (s.Kind == "gungame") GunGameKill(s, killer);
                else if (s.Kind != "match") KillReward(killer, s);
            }
            else AddFeed(s, $"<color=#a39896>×</color> {Esc(victim.displayName)}");

            ShowDeath(victim, killer, weapon, dist, hs);
            RefreshHudAll(s);

            if (s.Kind == "match")
            {
                s.Alive.Remove(vid);
                CheckRoundEnd(s);
            }
            else if (s.Kind == "tdm" && s.Score.Max() >= _config.TdmKills && !s.Resetting) TdmWin(s);

            ScheduleRespawn(victim, s.Kind == "match" ? 1.5f : _config.RespawnDelay);
        }

        private static void Inc(Dictionary<ulong, int> d, ulong k) { int v; d.TryGetValue(k, out v); d[k] = v + 1; }

        // За убийство: здоровье и патроны (как на FFA-серверах: сразу снова в бой)
        private void KillReward(BasePlayer killer, Session s)
        {
            if (killer == null || killer.IsDead()) return;
            try
            {
                if (_config.KillHeal)
                {
                    float max = killer.MaxHealth();
                    killer.health = _config.KillHealAmount > 0f ? Mathf.Min(max, killer.health + _config.KillHealAmount) : max;
                    if (killer.metabolism != null) { killer.metabolism.bleeding.value = 0f; killer.metabolism.SendChanges(); }
                    killer.SendNetworkUpdate();
                }
                if (!_config.KillRefill) return;
                foreach (var it in killer.inventory.containerBelt.itemList)
                {
                    var proj = it.GetHeldEntity() as BaseProjectile;
                    if (proj?.primaryMagazine == null) continue;
                    proj.primaryMagazine.contents = proj.primaryMagazine.capacity;
                    proj.SendNetworkUpdateImmediate();
                }
                if (s.Loadout == null) return;
                foreach (var k in s.Loadout.Items.Where(x => x.Container == "main"))
                {
                    var def = ItemManager.FindItemDefinition(k.Item);
                    if (def == null || def.category != ItemCategory.Ammunition) continue;
                    int have = killer.inventory.GetAmount(def.itemid);
                    if (have < k.Amount) GiveEntry(killer, new KitEntry { Item = k.Item, Amount = k.Amount - have });
                }
            }
            catch (Exception ex) { PrintWarning("KillReward: " + ex.Message); }
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");

        private static string WeaponName(HitInfo info)
        {
            try
            {
                var item = info?.Weapon?.GetItem();
                if (item?.info != null) return item.info.displayName.english;
                var pref = info?.WeaponPrefab?.ShortPrefabName;
                if (!string.IsNullOrEmpty(pref)) return pref.Replace(".entity", "").Replace("_", " ");
            }
            catch { }
            return "?";
        }

        private void ScheduleRespawn(BasePlayer player, float delay)
        {
            var id = Id(player);
            CancelRespawn(id);
            _deathAt[id] = Time.realtimeSinceStartup;
            _respawn[id] = timer.Once(delay, () =>
            {
                _respawn.Remove(id);
                if (player == null || !player.IsConnected || !player.IsDead()) return;
                if (!_in.ContainsKey(id)) return;
                player.Respawn();
            });
        }

        private void CancelRespawn(ulong id)
        {
            Timer t;
            if (_respawn.TryGetValue(id, out t)) { t?.Destroy(); _respawn.Remove(id); }
        }

        // Hub спрашивает: где возродить?
        private object OnHubRespawn(BasePlayer player)
        {
            var s = SessionOf(player);
            if (s == null) return null;
            Pt pt = s.Kind == "match" ? Spawns(s.Arena, s.Team.ContainsKey(Id(player)) ? s.Team[Id(player)] : 0).FirstOrDefault() : PickSpawn(s, Id(player));
            if (pt == null) return null;
            return new BasePlayer.SpawnPoint { pos = pt.V + Vector3.up * 0.15f, rot = Quaternion.Euler(0f, pt.Yaw, 0f) };
        }

        // Hub: набор не выдавать, мы сами
        private object OnHubRespawned(BasePlayer player, string mode)
        {
            var s = SessionOf(player);
            if (s == null) return null;
            CuiHelper.DestroyUi(player, UiDeath);
            Heal(player);
            if (s.Kind == "match")
            {
                if (s.State == "live" || s.State == "between") { player.inventory.Strip(); Center(player, T(Id(player), "WaitRound"), ColDim, 3f); }
                else if (s.State == "countdown") player.inventory.Strip();     // набор выдаст StartRound
                else GiveLoadout(player, s);
            }
            else GiveLoadout(player, s);
            RefreshHud(player, s);
            return true;
        }

        // без Hub — сами возрождаем на арене
        private object OnPlayerRespawn(BasePlayer player, BasePlayer.SpawnPoint spawnPoint)
        {
            if (Hub != null) return null;
            return OnHubRespawn(player);
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (Hub != null) return;
            OnHubRespawned(player, null);
        }

        private bool CanEarn(ulong killer, ulong victim)
        {
            if (Hub == null || !Hub.IsLoaded) return true;
            var r = Hub.Call("CanEarnFrom", killer, victim);
            return !(r is bool) || (bool)r;
        }

        // для достижений: тот же IP не считается, без записи в лог жертв
        private bool CanEarnSilent(ulong killer, ulong victim) => killer != victim && !SameIp(killer, victim);

        private bool SameIp(ulong a, ulong b) => Hub != null && Hub.IsLoaded && Hub.Call("SameIp", a, b) is bool && (bool)Hub.Call("SameIp", a, b);

        private void AddCoins(ulong id, int amount, string reason)
        {
            if (Hub == null || amount == 0) return;
            Hub.Call("AddCoins", id, amount, reason);
        }

        #endregion

        #region TDM и ган-гейм

        private void TdmWin(Session s)
        {
            s.Resetting = true;
            int win = s.Score[0] >= s.Score[1] ? 0 : 1;
            foreach (var id in s.Players.ToList())
            {
                var p = BasePlayer.FindByID(id);
                if (p == null) continue;
                bool w = s.Team.ContainsKey(id) && s.Team[id] == win;
                Center(p, w ? T(id, "TeamWin") : T(id, "TeamLose"), w ? ColGreen : ColRed, 5f);
                if (w) { S(id).TdmWins++; AddCoins(id, _config.CoinsTdmWin, T(id, "WinReason")); }
            }
            timer.Once(5f, () =>
            {
                s.Score[0] = s.Score[1] = 0;
                s.Resetting = false;
                foreach (var id in s.Players.ToList()) { s.Kills[id] = 0; s.Deaths[id] = 0; s.Streak[id] = 0; }
                foreach (var id in s.Players.ToList()) { var p = BasePlayer.FindByID(id); if (p != null && !p.IsDead()) Spawn(p, s); }
                RefreshHudAll(s);
            });
        }

        private void GunGameKill(Session s, BasePlayer killer)
        {
            var kid = Id(killer);
            int lvl;
            s.Level.TryGetValue(kid, out lvl);
            lvl++;
            if (lvl >= s.Ladder.Count)
            {
                if (s.Resetting) return;
                s.Resetting = true;
                S(kid).GunGameWins++;
                Ach(kid, "gungame_wins");
                AddCoins(kid, _config.CoinsGunGameWin, T(kid, "WinReason"));
                foreach (var id in s.Players.ToList())
                {
                    var p = BasePlayer.FindByID(id);
                    if (p != null) Center(p, T(id, "GgWinner", killer.displayName), id == kid ? ColGreen : ColGold, 5f);
                }
                timer.Once(5f, () =>
                {
                    NewLadder(s);
                    s.Resetting = false;
                    foreach (var id in s.Players.ToList()) { var p = BasePlayer.FindByID(id); if (p != null && !p.IsDead()) Spawn(p, s); }
                    RefreshHudAll(s);
                });
                return;
            }
            s.Level[kid] = lvl;
            // меняем пушку сразу, броню оставляем
            foreach (var item in killer.inventory.containerBelt.itemList.ToList()) item.Remove();
            foreach (var item in killer.inventory.containerMain.itemList.ToList()) item.Remove();
            ItemManager.DoRemoves();
            GiveGunStep(killer, s.Ladder[lvl]);
            GiveEntry(killer, new KitEntry { Item = "bandage", Amount = 2, Container = "belt" });
            Center(killer, T(kid, "GgNext", s.Ladder[lvl].Name, lvl + 1, s.Ladder.Count), ColRose, 1.5f);
        }

        #endregion

        #region Матчи: дуэли и свои арены

        private ArenaDef FreeMatchArena(string wanted)
        {
            var busy = new HashSet<string>(_sessions.Values.Where(x => x.Kind == "match").Select(x => x.Arena.Id));
            var all = _data.Arenas.Values.Where(a => a.Enabled && a.Kind == "match" && a.SpawnsA.Count > 0 && a.SpawnsB.Count > 0).ToList();
            if (!string.IsNullOrEmpty(wanted))
            {
                var w = all.FirstOrDefault(a => a.Id == wanted);
                if (w != null && !busy.Contains(w.Id)) return w;
            }
            return all.Where(a => !busy.Contains(a.Id)).OrderBy(a => UnityEngine.Random.Range(0, 1000)).FirstOrDefault();
        }

        private Session CreateMatch(ArenaDef a, ulong owner, string ownerName, string loadout, int teamSize, int rounds, bool barricades, bool isDuel, bool pub, string password, int bet)
        {
            var s = new Session
            {
                Id = "m_" + a.Id, Arena = a, Kind = "match", Loadout = LoadoutById(loadout), IsDuel = isDuel,
                Owner = owner, OwnerName = ownerName, TeamSize = Mathf.Clamp(teamSize, 1, 5), RoundsToWin = Mathf.Max(1, rounds),
                AllowBarricades = barricades && a.Barricades, Public = pub, Password = password, Bet = bet, EmptySince = Time.realtimeSinceStartup
            };
            _sessions[s.Id] = s;
            return s;
        }

        private void StartMatch(Session s)
        {
            if (s.State != "waiting") return;
            s.Score[0] = s.Score[1] = 0;
            s.Round = 0;
            StartRound(s);
        }

        private void StartRound(Session s)
        {
            if (!_sessions.ContainsKey(s.Id)) return;
            s.Round++;
            s.State = "countdown";
            s.Alive.Clear();
            CleanupBuilt(s);
            BuildLayout(s);
            foreach (var id in s.Players.ToList())
            {
                var p = BasePlayer.FindByID(id);
                if (p == null || !p.IsConnected) continue;
                CancelRespawn(id);
                if (p.IsDead()) { p.Respawn(); continue; }
                MoveToTeamSpawn(p, s, false);
                Heal(p);
                GiveLoadout(p, s);
                s.Alive.Add(id);
            }
            // мёртвых добавим, когда возродятся (OnHubRespawned → GiveLoadout в countdown нельзя: поэтому даём здесь)
            timer.Once(0.6f, () =>
            {
                foreach (var id in s.Players.ToList())
                {
                    if (s.Alive.Contains(id)) continue;
                    var p = BasePlayer.FindByID(id);
                    if (p == null || p.IsDead()) continue;
                    MoveToTeamSpawn(p, s, false); Heal(p); GiveLoadout(p, s); s.Alive.Add(id);
                }
            });
            int n = Mathf.Max(1, _config.RoundCountdown);
            for (int i = 0; i <= n; i++)
            {
                int left = n - i;
                timer.Once(i, () =>
                {
                    if (s.State != "countdown") return;
                    foreach (var id in s.Players.ToList())
                    {
                        var p = BasePlayer.FindByID(id);
                        if (p == null) continue;
                        if (left > 0) Center(p, $"<size=14>{T(id, "RoundN", s.Round)}</size>\n{left}", ColText, 1.1f);
                        else Center(p, T(id, "Fight"), ColGold, 1f);
                    }
                    if (left == 0) { s.State = "live"; RefreshHudAll(s); }
                });
            }
            RefreshHudAll(s);
        }

        private void CheckRoundEnd(Session s)
        {
            if (s.Kind != "match" || s.State != "live") return;
            bool aAlive = s.Alive.Any(id => s.Team.ContainsKey(id) && s.Team[id] == 0);
            bool bAlive = s.Alive.Any(id => s.Team.ContainsKey(id) && s.Team[id] == 1);
            if (aAlive && bAlive) return;
            s.State = "between";
            int winner = aAlive ? 0 : bAlive ? 1 : -1;
            if (winner >= 0) s.Score[winner]++;
            RefreshHudAll(s);
            foreach (var id in s.Players.ToList())
            {
                var p = BasePlayer.FindByID(id);
                if (p == null) continue;
                string msg = winner < 0 ? T(id, "RoundDraw") : (s.Team.ContainsKey(id) && s.Team[id] == winner ? T(id, "RoundWin") : T(id, "RoundLose"));
                Center(p, $"{msg}\n<size=16>{s.Score[0]} : {s.Score[1]}</size>", winner < 0 ? ColDim : (s.Team.ContainsKey(id) && s.Team[id] == winner ? ColGreen : ColRed), 2.5f);
            }
            if (winner >= 0 && s.Score[winner] >= s.RoundsToWin) { timer.Once(2.5f, () => EndMatch(s, winner, false)); return; }
            timer.Once(3f, () => { if (_sessions.ContainsKey(s.Id) && s.State == "between") StartRound(s); });
        }

        private void EndMatch(Session s, int winner, bool forfeit)
        {
            if (s.State == "ended") return;
            s.State = "ended";
            var players = s.Players.ToList();
            if (!string.IsNullOrEmpty(s.Tag))
            {
                var win = s.Team.Where(kv => kv.Value == winner).Select(kv => kv.Key).ToList();
                var lose = s.Team.Where(kv => kv.Value != winner).Select(kv => kv.Key).ToList();
                Interface.CallHook("OnArenasMatchEnd", s.Tag, win, lose, s.Score[0], s.Score[1]);
            }
            int winners = players.Count(id => s.Team.ContainsKey(id) && s.Team[id] == winner);
            foreach (var id in players)
            {
                var p = BasePlayer.FindByID(id);
                bool w = s.Team.ContainsKey(id) && s.Team[id] == winner;
                var st = S(id);
                if (s.IsDuel) { if (w) { st.DuelWins++; Ach(id, "duel_wins"); } else st.DuelLosses++; }
                else { if (w) st.MatchWins++; else st.MatchLosses++; }
                if (w)
                {
                    var me = id;
                    if (!players.Any(o => o != me && SameIp(me, o))) AddCoins(id, _config.CoinsDuelWin, T(id, "WinReason"));
                    if (s.Bet > 0 && winners > 0) AddCoins(id, s.Bet * players.Count / winners, T(id, "BetWon"));
                }
                if (p != null) ShowResult(p, s, w, forfeit);
            }
            timer.Once(5f, () =>
            {
                foreach (var id in s.Players.ToList())
                {
                    var p = BasePlayer.FindByID(id);
                    if (p != null) { CuiHelper.DestroyUi(p, UiMain); CuiHelper.DestroyUi(p, UiMain + ".dim"); Leave(p, true); }
                }
                CloseMatch(s);
            });
        }

        private void CloseMatch(Session s)
        {
            CleanupBuilt(s);
            foreach (var id in s.Players.ToList())
            {
                _in.Remove(id);
                var p = BasePlayer.FindByID(id);
                if (p != null) { foreach (var ui in new[] { UiHud, UiFeed, UiCenter, UiDeath }) CuiHelper.DestroyUi(p, ui); if (s.State == "waiting") { CloseAll(p); Toast(p, T(id, "RoomClosed"), true); } }
            }
            s.Players.Clear();
            _sessions.Remove(s.Id);
        }

        private void OnEntityBuilt(Planner planner, GameObject go)
        {
            var player = planner?.GetOwnerPlayer();
            var e = go?.ToBaseEntity();
            if (player == null || e == null) return;
            var s = SessionOf(player);
            if (s != null) s.Built.Add(e);
        }

        private void CleanupBuilt(Session s)
        {
            foreach (var e in s.Built) if (e != null && !e.IsDestroyed) e.Kill();
            s.Built.Clear();
            foreach (var e in s.LayoutEnts) if (e != null && !e.IsDestroyed) e.Kill();
            s.LayoutEnts.Clear();
            s.LAll.Clear(); s.LA.Clear(); s.LB.Clear();
        }

        #endregion

        #region Лабиринт и роум

        private List<Pt> SpawnsFor(Session s, int team)
        {
            if (s.LAll.Count > 0)
            {
                if (team == 0 && s.LA.Count > 0) return s.LA;
                if (team == 1 && s.LB.Count > 0) return s.LB;
                return s.LAll;
            }
            return Spawns(s.Arena, team);
        }

        private BaseEntity PlaceWall(Session s, string prefab, Vector3 pos, float yaw)
        {
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out hit, 60f, LayerMask.GetMask("Terrain", "World"), QueryTriggerInteraction.Ignore)) pos = hit.point;
            var e = GameManager.server.CreateEntity(prefab, pos, Quaternion.Euler(0f, yaw, 0f));
            if (e == null) return null;
            e.enableSaving = false;
            e.Spawn();
            s.LayoutEnts.Add(e);
            return e;
        }

        // стенка вдоль ребра клетки, длина = клетка, из нескольких секций если надо
        private void EdgeWall(Session s, Vector3 a, Vector3 b)
        {
            float len = Vector3.Distance(a, b), ww = Mathf.Max(1f, _config.MazeWallWidth);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / ww - 0.01f));
            var dir = (b - a) / len;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg - 90f;
            for (int i = 0; i < n; i++) PlaceWall(s, _config.MazePrefab, a + dir * (len * (i + 0.5f) / n), yaw);
        }

        private void BuildLayout(Session s)
        {
            var a = s.Arena;
            foreach (var e in s.LayoutEnts) if (e != null && !e.IsDestroyed) e.Kill();
            s.LayoutEnts.Clear(); s.LAll.Clear(); s.LA.Clear(); s.LB.Clear();
            if (a == null || a.Center == null || a.Layout == "none") return;
            try
            {
                if (a.Layout == "maze") BuildMaze(s);
                else if (a.Layout == "roam") BuildRoam(s);
            }
            catch (Exception ex) { PrintWarning($"Арена {a.Id}: не удалось построить {a.Layout}: {ex.Message}"); }
            if (a.Layout == "maze" && s.Kind != "match") s.NextRegen = Time.realtimeSinceStartup + Mathf.Max(1f, _config.MazeRegenMin) * 60f;
        }

        // лабиринт «обходом в глубину» + немного лишних проходов, чтобы в FFA были петли
        private void BuildMaze(Session s)
        {
            var a = s.Arena;
            int n = Mathf.Clamp(a.MazeSize, 4, 16);
            float c = Mathf.Max(3f, _config.MazeCell);
            var o = a.Center.V - new Vector3(n * c / 2f, 0f, n * c / 2f);
            // стены: east[x,z] — между (x,z) и (x+1,z); north[x,z] — между (x,z) и (x,z+1)
            var east = new bool[n, n]; var north = new bool[n, n];
            for (int x = 0; x < n; x++) for (int z = 0; z < n; z++) { east[x, z] = true; north[x, z] = true; }
            var seen = new bool[n, n];
            var stack = new Stack<int>();
            int sx = UnityEngine.Random.Range(0, n), sz = UnityEngine.Random.Range(0, n);
            seen[sx, sz] = true; stack.Push(sx * n + sz);
            var dirs = new List<int>();
            while (stack.Count > 0)
            {
                int cur = stack.Peek(), x = cur / n, z = cur % n;
                dirs.Clear();
                if (x + 1 < n && !seen[x + 1, z]) dirs.Add(0);
                if (x - 1 >= 0 && !seen[x - 1, z]) dirs.Add(1);
                if (z + 1 < n && !seen[x, z + 1]) dirs.Add(2);
                if (z - 1 >= 0 && !seen[x, z - 1]) dirs.Add(3);
                if (dirs.Count == 0) { stack.Pop(); continue; }
                int d = dirs[UnityEngine.Random.Range(0, dirs.Count)];
                int nx = x, nz = z;
                if (d == 0) { east[x, z] = false; nx = x + 1; }
                else if (d == 1) { east[x - 1, z] = false; nx = x - 1; }
                else if (d == 2) { north[x, z] = false; nz = z + 1; }
                else { north[x, z - 1] = false; nz = z - 1; }
                seen[nx, nz] = true; stack.Push(nx * n + nz);
            }
            // петли: в FFA и командном бою больше проходов, в дуэли — классический лабиринт
            float loops = s.Kind == "match" && s.TeamSize <= 1 ? 0f : _config.MazeLoops;
            for (int x = 0; x < n; x++) for (int z = 0; z < n; z++)
            {
                if (x + 1 < n && east[x, z] && UnityEngine.Random.value < loops) east[x, z] = false;
                if (z + 1 < n && north[x, z] && UnityEngine.Random.value < loops) north[x, z] = false;
            }
            Func<int, int, Vector3> P = (x, z) => o + new Vector3(x * c, 0f, z * c);
            // рамка
            for (int i = 0; i < n; i++)
            {
                EdgeWall(s, P(i, 0), P(i + 1, 0));
                EdgeWall(s, P(i, n), P(i + 1, n));
                EdgeWall(s, P(0, i), P(0, i + 1));
                EdgeWall(s, P(n, i), P(n, i + 1));
            }
            for (int x = 0; x < n; x++) for (int z = 0; z < n; z++)
            {
                if (x + 1 < n && east[x, z]) EdgeWall(s, P(x + 1, z), P(x + 1, z + 1));
                if (z + 1 < n && north[x, z]) EdgeWall(s, P(x, z + 1), P(x + 1, z + 1));
            }
            // точки появления — центры клеток; дуэль: противоположные углы; команды: левый и правый край
            for (int x = 0; x < n; x++) for (int z = 0; z < n; z++)
            {
                var v = Ground(P(x, z) + new Vector3(c / 2f, 0f, c / 2f));
                var pt = Pt.From(v, 0f);
                s.LAll.Add(pt);
                if (x == 0) s.LA.Add(pt);
                if (x == n - 1) s.LB.Add(pt);
            }
            if (s.Kind == "match" && s.TeamSize <= 1)
            {
                s.LA.Clear(); s.LB.Clear();
                s.LA.Add(Pt.From(Ground(P(0, 0) + new Vector3(c / 2f, 0f, c / 2f)), 45f));
                s.LB.Add(Pt.From(Ground(P(n - 1, n - 1) + new Vector3(c / 2f, 0f, c / 2f)), 225f));
            }
        }

        // роум: большие стены в случайных местах, команды появляются на противоположных краях
        private void BuildRoam(Session s)
        {
            var a = s.Arena;
            var ctr = a.Center.V;
            float r = Mathf.Max(30f, a.Radius) * 0.8f;
            var placed = new List<Vector3>();
            int tries = 0;
            while (placed.Count < _config.RoamWalls && tries++ < _config.RoamWalls * 10)
            {
                float ang = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad, dist = Mathf.Sqrt(UnityEngine.Random.value) * r;
                var v = ctr + new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                if (Mathf.Abs(v.x - ctr.x) > r * 0.85f) continue;           // края свободны под спавны
                if (placed.Any(q => Vector3.Distance(q, v) < 9f)) continue;
                placed.Add(v);
                PlaceWall(s, _config.RoamPrefab, v, UnityEngine.Random.Range(0, 4) * 45f);
            }
            if (a.Spawns.Count > 0 || a.SpawnsA.Count > 0) return;          // админ расставил свои точки
            for (int i = 0; i < 6; i++)
            {
                float z = (i - 2.5f) * 6f;
                var pa = Pt.From(Ground(ctr + new Vector3(-r * 0.95f, 0f, z)), 90f);
                var pb = Pt.From(Ground(ctr + new Vector3(r * 0.95f, 0f, z)), 270f);
                s.LA.Add(pa); s.LB.Add(pb); s.LAll.Add(pa); s.LAll.Add(pb);
            }
        }

        private Vector3 Ground(Vector3 p)
        {
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out hit, 60f, LayerMask.GetMask("Terrain", "World"), QueryTriggerInteraction.Ignore)) return hit.point;
            return p;
        }

        // FFA-лабиринт перестраивается на лету: всех живых переносим на новые точки
        private void RegenLive(Session s)
        {
            BuildLayout(s);
            foreach (var id in s.Players.ToList())
            {
                var p = BasePlayer.FindByID(id);
                if (p == null || p.IsDead()) continue;
                var pt = PickSpawn(s, id);
                if (pt != null) p.Teleport(pt.V + Vector3.up * 0.15f);
                Center(p, T(id, "MazeNew"), ColRose, 2.5f);
            }
        }

        #endregion

        #region Тик

        private void Tick()
        {
            float now = Time.realtimeSinceStartup;
            foreach (var s in _sessions.Values.ToList())
            {
                // киллфид: убираем старые строки
                if (s.Feed.RemoveAll(f => f.Until < now) > 0) foreach (var id in s.Players) { var p = BasePlayer.FindByID(id); if (p != null) DrawFeed(p, s); }

                // держим в зоне
                if (s.Arena.Center != null && s.Arena.Radius > 0f)
                    foreach (var id in s.Players.ToList())
                    {
                        var p = BasePlayer.FindByID(id);
                        if (p == null || p.IsDead()) continue;
                        if (Vector3.Distance(p.transform.position, s.Arena.Center.V) > s.Arena.Radius)
                        {
                            var pt = s.Kind == "match" ? SpawnsFor(s, s.Team.ContainsKey(id) ? s.Team[id] : 0).FirstOrDefault() : PickSpawn(s, id);
                            if (pt != null) { p.Teleport(pt.V + Vector3.up * 0.15f); Toast(p, T(id, "Border"), true); }
                        }
                    }

                // лабиринт в FFA/TDM: новый каждые N минут
                if (s.Arena.Layout == "maze" && s.Kind != "match" && s.NextRegen > 0f && now >= s.NextRegen && s.Players.Count > 0) RegenLive(s);

                // пустая своя комната
                if (s.Kind == "match" && s.State == "waiting" && s.Players.Count <= 1 && now - s.EmptySince > _config.EmptyClose) CloseMatch(s);
                if (s.Kind == "match" && s.Players.Count > 1) s.EmptySince = now;
            }

            // протухшие приглашения
            foreach (var kv in _invites.Where(x => x.Value.Until < now).ToList())
            {
                _invites.Remove(kv.Key);
                var p = BasePlayer.FindByID(kv.Key);
                if (p != null) DestroyWindow(p, UiPopup);
            }

            // экран смерти: обратный отсчёт
            foreach (var kv in _deathAt.ToList())
            {
                var p = BasePlayer.FindByID(kv.Key);
                if (p == null || !p.IsDead()) { _deathAt.Remove(kv.Key); continue; }
            }
        }

        #endregion

        #region Команды

        [ChatCommand("arena")]
        private void CmdArena(BasePlayer player, string c, string[] a)
        {
            var id = Id(player);
            if (a.Length > 0)
            {
                switch (a[0].ToLower())
                {
                    case "leave": if (SessionOf(player) == null) Toast(player, T(id, "NotInArena"), true); else { Leave(player, true); Toast(player, T(id, "Left"), false); } return;
                    case "top": ShowTop(player, "day"); return;
                    case "join": if (a.Length > 1) Join(player, a[1], a.Length > 2 ? a[2] : null); return;
                }
            }
            var cur = SessionOf(player);
            if (cur != null && cur.Kind == "match" && cur.State == "waiting") { OpenRoom(player, cur); return; }
            OpenBrowser(player);
        }

        [ChatCommand("duel")]
        private void CmdDuel(BasePlayer player, string c, string[] a)
        {
            var id = Id(player);
            if (a.Length == 0) { OpenDuelSetup(player, 0); return; }
            switch (a[0].ToLower())
            {
                case "accept": AcceptDuel(player); return;
                case "decline": DeclineDuel(player); return;
            }
            var target = FindOnline(string.Join(" ", a));
            if (target == null) { Toast(player, T(id, "NotFound", string.Join(" ", a)), true); return; }
            OpenDuelSetup(player, Id(target));
        }

        private BasePlayer FindOnline(string q)
        {
            ulong uid;
            if (ulong.TryParse(q, out uid)) { var byId = BasePlayer.FindByID(uid); if (byId != null) return byId; }
            return BasePlayer.activePlayerList.FirstOrDefault(p => p.displayName.Equals(q, StringComparison.OrdinalIgnoreCase))
                ?? BasePlayer.activePlayerList.FirstOrDefault(p => p.displayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [ConsoleCommand("arena.ui")]
        private void CcUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs()) return;
            var id = Id(player);
            string a1 = arg.GetString(1), a2 = arg.GetString(2);
            switch (arg.GetString(0))
            {
                case "close": CloseAll(player); break;
                case "tab": _tab[id] = a1; OpenBrowser(player); break;
                case "join": Join(player, a1, null); break;
                case "joinpw": { string pw; _pwInput.TryGetValue(id, out pw); Join(player, a1, pw); break; }
                case "pw": _pwInput[id] = string.Join(" ", arg.Args.Skip(1).ToArray()); break;
                case "leave": CloseAll(player); Leave(player, true); break;
                case "top": ShowTop(player, a1); break;

                case "duel.open": OpenDuelSetup(player, 0); break;
                case "duel.target": { DuelInvite d = Draft(id); d.To = ulong.Parse(a1); OpenDuelSetup(player, d.To); break; }
                case "duel.set":
                {
                    var d = Draft(id);
                    if (a1 == "loadout") d.Loadout = a2;
                    else if (a1 == "arena") d.Arena = a2 == "-" ? null : a2;
                    else if (a1 == "rounds") d.Rounds = int.Parse(a2);
                    else if (a1 == "bet") d.Bet = int.Parse(a2);
                    OpenDuelSetup(player, d.To);
                    break;
                }
                case "duel.send": SendDuel(player); break;
                case "duel.accept": AcceptDuel(player); break;
                case "duel.decline": DeclineDuel(player); break;

                case "custom": OpenCustom(player); break;
                case "custom.set":
                {
                    var d = CDraft(id);
                    if (a1 == "template") d.Template = a2;
                    else if (a1 == "loadout") d.Loadout = a2;
                    else if (a1 == "size") d.TeamSize = int.Parse(a2);
                    else if (a1 == "rounds") d.Rounds = int.Parse(a2);
                    else if (a1 == "barricades") d.Barricades = !d.Barricades;
                    else if (a1 == "public") d.Public = !d.Public;
                    OpenCustom(player);
                    break;
                }
                case "custom.pw": CDraft(id).Password = string.Join(" ", arg.Args.Skip(1).ToArray()).Trim(); break;
                case "custom.create": CreateCustom(player); break;
                case "room.team":
                {
                    var s = SessionOf(player);
                    if (s == null || s.Kind != "match" || s.State != "waiting") break;
                    int team = int.Parse(a1);
                    if (s.Players.Count(p => s.Team.ContainsKey(p) && s.Team[p] == team) >= s.TeamSize) { Toast(player, T(id, "TeamFull"), true); break; }
                    s.Team[id] = team;
                    MoveToTeamSpawn(player, s, true);
                    BroadcastRoom(s);
                    break;
                }
                case "room.start":
                {
                    var s = SessionOf(player);
                    if (s == null || s.Kind != "match" || (s.Owner != id && !IsAdmin(player))) break;
                    if (!s.Players.Any(p => s.Team[p] == 0) || !s.Players.Any(p => s.Team[p] == 1)) { Toast(player, T(id, "NeedBoth"), true); break; }
                    foreach (var pid in s.Players) { var p = BasePlayer.FindByID(pid); if (p != null) DestroyWindow(p, UiMain); }
                    StartMatch(s);
                    break;
                }
                case "room.close":
                {
                    var s = SessionOf(player);
                    if (s != null && s.Kind == "match" && (s.Owner == id || IsAdmin(player))) CloseMatch(s);
                    break;
                }
                case "room.invite":
                {
                    var s = SessionOf(player);
                    var t = BasePlayer.FindByID(ulong.Parse(a1));
                    if (s == null || t == null) break;
                    ShowRoomInvite(t, s, player.displayName);
                    Toast(player, T(id, "InviteSent", t.displayName), false);
                    break;
                }
            }
        }

        [ChatCommand("arenaadmin")]
        private void CmdAdmin(BasePlayer player, string c, string[] a)
        {
            var id = Id(player);
            if (!IsAdmin(player)) { Reply(player, T(id, "NoPerm")); return; }
            if (a.Length == 0) { Reply(player, T(id, "AdminHelp")); return; }
            var pos = player.transform.position;
            float yaw = player.eyes != null ? player.eyes.rotation.eulerAngles.y : 0f;
            string aid = a.Length > 1 ? a[1].ToLower() : null;
            ArenaDef ar = aid != null && _data.Arenas.ContainsKey(aid) ? _data.Arenas[aid] : null;
            string cmd = a[0].ToLower();

            if (cmd == "list")
            {
                var sb = new System.Text.StringBuilder();
                foreach (var x in _data.Arenas.Values)
                    sb.AppendLine($"{x.Id} [{x.Kind}] «{x.Name}» hub:{x.HubMode} набор:{x.Loadout} спавнов {x.Spawns.Count}/{x.SpawnsA.Count}/{x.SpawnsB.Count} R{x.Radius}{(x.Template ? " шаблон" : "")}{(x.Enabled ? "" : " выкл")}");
                if (sb.Length == 0) sb.Append(T(id, "NoArenas"));
                Reply(player, sb.ToString());
                return;
            }
            if (cmd == "loadouts") { Reply(player, string.Join("\n", _config.Loadouts.Select(l => $"{l.Id} — {l.Name} ({l.Desc})").ToArray())); return; }
            if (cmd == "create")
            {
                if (aid == null || a.Length < 3) { Reply(player, "/arenaadmin create <id> <ffa|tdm|gungame|match> [режим Hub]"); return; }
                var kind = a[2].ToLower();
                if (!new[] { "ffa", "tdm", "gungame", "match" }.Contains(kind)) { Reply(player, "ffa | tdm | gungame | match"); return; }
                _data.Arenas[aid] = new ArenaDef
                {
                    Id = aid, Name = aid, Kind = kind, Center = Pt.From(pos, yaw), Template = kind == "match",
                    HubMode = a.Length > 3 ? a[3] : (kind == "match" ? _config.DuelHubMode : "ffa"), Max = kind == "match" ? 10 : 16
                };
                SaveArenas();
                Reply(player, T(id, "AdmCreated", aid, kind));
                return;
            }
            if (ar == null) { Reply(player, T(id, "AdminHelp")); return; }
            switch (cmd)
            {
                case "name": ar.Name = string.Join(" ", a.Skip(2).ToArray()); break;
                case "spawn": ar.Spawns.Add(Pt.From(pos, yaw)); Reply(player, $"спавнов: {ar.Spawns.Count}"); break;
                case "spawna": ar.SpawnsA.Add(Pt.From(pos, yaw)); Reply(player, $"красных: {ar.SpawnsA.Count}"); break;
                case "spawnb": ar.SpawnsB.Add(Pt.From(pos, yaw)); Reply(player, $"синих: {ar.SpawnsB.Count}"); break;
                case "clearspawns": ar.Spawns.Clear(); ar.SpawnsA.Clear(); ar.SpawnsB.Clear(); break;
                case "center": ar.Center = Pt.From(pos, yaw); break;
                case "radius": ar.Radius = a.Length > 2 ? float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) : 60f; break;
                case "loadout": if (a.Length > 2 && _config.Loadouts.Any(l => l.Id == a[2])) ar.Loadout = a[2]; else { Reply(player, T(id, "AdmLoadouts")); return; } break;
                case "max": ar.Max = a.Length > 2 ? int.Parse(a[2]) : 16; break;
                case "hub": ar.HubMode = a.Length > 2 ? a[2] : "ffa"; break;
                case "template": ar.Template = a.Length < 3 || a[2] == "on"; break;
                case "barricades": ar.Barricades = a.Length < 3 || a[2] == "on"; break;
                case "layout":
                    ar.Layout = a.Length > 2 && (a[2] == "maze" || a[2] == "roam") ? a[2] : "none";
                    { Session sl; if (_sessions.TryGetValue(ar.Id, out sl)) BuildLayout(sl); }
                    Reply(player, T(id, "AdmLayout", ar.Layout)); break;
                case "mazesize": ar.MazeSize = Mathf.Clamp(a.Length > 2 ? int.Parse(a[2]) : 9, 4, 16); break;
                case "size": ar.Size = a.Length > 2 && a[2] == "large" ? "large" : "small"; if (ar.Size == "large") ar.Barricades = true; break;
                case "enable": ar.Enabled = true; break;
                case "disable": ar.Enabled = false; Session s0; if (_sessions.TryGetValue(ar.Id, out s0)) foreach (var pid in s0.Players.ToList()) { var p = BasePlayer.FindByID(pid); if (p != null) Leave(p, true); } break;
                case "delete": _data.Arenas.Remove(ar.Id); SaveArenas(); Reply(player, T(id, "AdmDeleted")); return;
                case "show": ShowArena(player, ar); return;
                default: Reply(player, T(id, "AdminHelp")); return;
            }
            SaveArenas();
            Reply(player, T(id, "AdmOk", ar.Id));
        }

        private void ShowArena(BasePlayer player, ArenaDef a)
        {
            bool was = player.IsAdmin;
            if (!was) { player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true); player.SendNetworkUpdateImmediate(); }
            try
            {
                foreach (var p in a.Spawns) player.SendConsoleCommand("ddraw.text", 15f, Color.white, p.V + Vector3.up, "спавн");
                foreach (var p in a.SpawnsA) player.SendConsoleCommand("ddraw.text", 15f, Color.red, p.V + Vector3.up, "A");
                foreach (var p in a.SpawnsB) player.SendConsoleCommand("ddraw.text", 15f, Color.cyan, p.V + Vector3.up, "B");
                if (a.Center != null) player.SendConsoleCommand("ddraw.sphere", 15f, Color.yellow, a.Center.V, a.Radius);
            }
            finally { if (!was) { player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false); player.SendNetworkUpdateImmediate(); } }
        }

        private void Reply(BasePlayer player, string msg) => player.ChatMessage($"<color=#f0a6b1>[Arenas]</color> {msg}");

        #endregion

        #region Дуэли

        private DuelInvite Draft(ulong id)
        {
            DuelInvite d;
            if (!_drafts.TryGetValue(id, out d)) _drafts[id] = d = new DuelInvite { From = id, Rounds = 3 };
            return d;
        }

        private void SendDuel(BasePlayer player)
        {
            var id = Id(player);
            var d = Draft(id);
            var target = BasePlayer.FindByID(d.To);
            if (target == null || !target.IsConnected || target == player) { Toast(player, T(id, "PickTarget"), true); return; }
            if (SessionOf(target) != null && SessionOf(target).Kind == "match") { Toast(player, T(id, "TargetBusy", target.displayName), true); return; }
            if (d.Bet > 0 && Hub != null && Convert.ToInt32(Hub.Call("GetCoins", id) ?? 0) < d.Bet) { Toast(player, T(id, "NoCoins"), true); return; }
            if (FreeMatchArena(d.Arena) == null) { Toast(player, T(id, "NoFreeArena"), true); return; }
            var inv = new DuelInvite { From = id, FromName = player.displayName, To = d.To, Loadout = d.Loadout, Arena = d.Arena, Rounds = d.Rounds, Bet = d.Bet, Until = Time.realtimeSinceStartup + _config.InviteTimeout };
            _invites[d.To] = inv;
            CloseAll(player);
            Toast(player, T(id, "DuelSent", target.displayName), false);
            ShowDuelInvite(target, inv);
        }

        private void AcceptDuel(BasePlayer player)
        {
            var id = Id(player);
            DuelInvite inv;
            if (!_invites.TryGetValue(id, out inv)) { Toast(player, T(id, "NoInvite"), true); return; }
            _invites.Remove(id);
            DestroyWindow(player, UiPopup);
            var from = BasePlayer.FindByID(inv.From);
            if (from == null || !from.IsConnected) { Toast(player, T(id, "InviteGone"), true); return; }
            var arena = FreeMatchArena(inv.Arena);
            if (arena == null) { Toast(player, T(id, "NoFreeArena"), true); Toast(from, T(inv.From, "NoFreeArena"), true); return; }
            if (inv.Bet > 0 && Hub != null)
            {
                if (SameIp(id, inv.From)) { Toast(player, T(id, "BetSameIp"), true); Toast(from, T(inv.From, "BetSameIp"), true); return; }
                if (Convert.ToInt32(Hub.Call("GetCoins", id) ?? 0) < inv.Bet) { Toast(player, T(id, "NoCoins"), true); Toast(from, T(inv.From, "TargetNoCoins"), true); return; }
                if (Convert.ToInt32(Hub.Call("GetCoins", inv.From) ?? 0) < inv.Bet) { Toast(from, T(inv.From, "NoCoins"), true); Toast(player, T(id, "TargetNoCoins"), true); return; }
                Hub.Call("TakeCoins", id, inv.Bet);
                Hub.Call("TakeCoins", inv.From, inv.Bet);
            }
            foreach (var p in new[] { from, player }) { var cur = SessionOf(p); if (cur != null) Leave(p, false); }

            var s = CreateMatch(arena, inv.From, inv.FromName, inv.Loadout, 1, inv.Rounds, false, true, false, null, inv.Bet);
            foreach (var p in new[] { from, player })
            {
                var pid = Id(p);
                if (Hub != null && HubMode(p) != _config.DuelHubMode) Hub.Call("SendToMode", p, _config.DuelHubMode, true);
                s.Players.Add(pid);
                _in[pid] = s;
                s.Team[pid] = p == from ? 0 : 1;
                s.Kills[pid] = 0; s.Deaths[pid] = 0; s.Streak[pid] = 0;
                S(pid, p.displayName);
                CloseAll(p);
            }
            StartRound(s);
        }

        private void DeclineDuel(BasePlayer player)
        {
            var id = Id(player);
            DuelInvite inv;
            if (!_invites.TryGetValue(id, out inv)) return;
            _invites.Remove(id);
            DestroyWindow(player, UiPopup);
            var from = BasePlayer.FindByID(inv.From);
            if (from != null) Toast(from, T(inv.From, "DuelDeclined", player.displayName), true);
        }

        #endregion

        #region Свои арены

        private CustomDraft CDraft(ulong id)
        {
            CustomDraft d;
            if (!_customDrafts.TryGetValue(id, out d)) _customDrafts[id] = d = new CustomDraft();
            return d;
        }

        private bool CanCreate(BasePlayer p) => _config.CreateForAll || IsAdmin(p) || permission.UserHasPermission(p.UserIDString, PermCreate);

        private void CreateCustom(BasePlayer player)
        {
            var id = Id(player);
            if (!CanCreate(player)) { Toast(player, T(id, "NoPerm"), true); return; }
            if (_sessions.Values.Count(x => x.Kind == "match" && !x.IsDuel && x.Owner == id) >= _config.MaxOwnArenas && !IsAdmin(player)) { Toast(player, T(id, "OwnLimit", _config.MaxOwnArenas), true); return; }
            var d = CDraft(id);
            var arena = FreeMatchArena(d.Template);
            if (arena == null) { Toast(player, T(id, "NoFreeArena"), true); return; }
            var s = CreateMatch(arena, id, player.displayName, d.Loadout, d.TeamSize, d.Rounds, d.Barricades, false, d.Public, string.IsNullOrEmpty(d.Password) ? null : d.Password, 0);
            Join(player, s.Id, d.Password);
            string pending;
            if (_pendingJoin.TryGetValue(id, out pending) && pending == s.Id) return;   // ждём перехода в режим Hub
            if (SessionOf(player) != s) _sessions.Remove(s.Id);
        }

        private void BroadcastRoom(Session s)
        {
            foreach (var id in s.Players.ToList())
            {
                var p = BasePlayer.FindByID(id);
                if (p != null && s.State == "waiting") OpenRoom(p, s);
            }
        }

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
                    Name = CuiHelper.GetGuid(),
                    Parent = parent,
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

        private static void Input(CuiElementContainer c, string parent, string name, string value, string hint, string cmd, float x, float y, float w, float h)
        {
            c.Add(new CuiElement
            {
                Name = name, Parent = parent,
                Components = { new CuiImageComponent { Color = "0 0 0 0.35" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - h}", OffsetMax = $"{x + w} {-y}" } }
            });
            if (string.IsNullOrEmpty(value)) Text(c, name, hint, 12, 12, 0, w - 24, h, TextAnchor.MiddleLeft, "0.45 0.41 0.40 1", FontReg);
            c.Add(new CuiElement
            {
                Parent = name,
                Components =
                {
                    new CuiInputFieldComponent { Text = value ?? "", Command = cmd, FontSize = 13, Align = TextAnchor.MiddleLeft, CharsLimit = 24, Color = ColText },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "12 0", OffsetMax = "-12 0" }
                }
            });
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

        private void DestroyWindow(BasePlayer p, string name)
        {
            CuiHelper.DestroyUi(p, name + ".dim");
            CuiHelper.DestroyUi(p, name);
        }

        private void CloseAll(BasePlayer p)
        {
            DestroyWindow(p, UiMain);
            DestroyWindow(p, UiPopup);
        }

        // ряд одинаковых кнопок на всю ширину
        private void Seg(CuiElementContainer c, string parent, float x, float y, float w, float h, IList<KeyValuePair<string, string>> items, string active, int size = 12)
        {
            int n = items.Count;
            float bw = (w - (n - 1) * 6f) / n;
            for (int i = 0; i < n; i++)
            {
                bool a = items[i].Key == active;
                Btn(c, parent, items[i].Value.Split('|')[0], items[i].Value.Contains("|") ? items[i].Value.Split('|')[1] : "", a ? ColWine : ColBtn, size, x + i * (bw + 6f), y, bw, h, a ? ColText : ColDim, 10f);
            }
        }

        #endregion

        #region Интерфейс: список арен

        private static readonly string[] Tabs = { "ffa", "tdm", "gungame", "duel", "custom" };

        private void OpenBrowser(BasePlayer player)
        {
            var id = Id(player);
            string tab;
            if (!_tab.TryGetValue(id, out tab)) tab = "ffa";
            const float W = 960f, H = 620f;
            var c = new CuiElementContainer();
            Window(c, UiMain, "A", T(id, "BrowserTitle"), T(id, "BrowserSub"), W, H, "arena.ui close");
            var R = UiMain;
            float top = M + 52 + 2 + M;

            Seg(c, R, M, top, W - 2 * M, 34, Tabs.Select(t => new KeyValuePair<string, string>(t, T(id, "Tab_" + t) + "|arena.ui tab " + t)).ToList(), tab);
            top += 34 + G;
            float listH = H - M - 46 - G - top;
            Box(c, R, ColPanel, M, top, W - 2 * M, listH, 16f);

            var rows = new List<Action<float>>();
            float rw = W - 2 * M - 2 * G;
            if (tab == "duel" || tab == "custom")
            {
                var rooms = _sessions.Values.Where(s => s.Kind == "match" && (tab == "duel" ? s.IsDuel : !s.IsDuel) && (s.Public || s.Players.Contains(id) || IsAdmin(player))).ToList();
                foreach (var s in rooms.Take(7))
                {
                    var ss = s;
                    rows.Add(y =>
                    {
                        Box(c, R, ColRow, M + G, y, rw, 52, 14f);
                        Text(c, R, ss.IsDuel ? $"{Esc(NamesOfTeam(ss, 0))} vs {Esc(NamesOfTeam(ss, 1))}" : $"{Esc(ss.OwnerName)} · {ss.Arena.Name}{(ss.Password != null ? " · " + T(id, "Locked") : "")}", 14, M + G + 14, y + 6, rw - 260, 22);
                        Text(c, R, $"{ss.Loadout?.Name} · {ss.TeamSize}v{ss.TeamSize} · {T(id, "ToWins", ss.RoundsToWin)}{(ss.Bet > 0 ? " · " + T(id, "BetN", ss.Bet) : "")} · {T(id, "State_" + ss.State)}", 11, M + G + 14, y + 28, rw - 260, 18, TextAnchor.MiddleLeft, ColDim, FontReg);
                        Text(c, R, $"{ss.Players.Count} / {ss.TeamSize * 2}", 12, M + G + rw - 236, y, 90, 52, TextAnchor.MiddleRight);
                        if (!ss.IsDuel && ss.State == "waiting")
                            Btn(c, R, ss.Password != null ? T(id, "Password") : T(id, "JoinBtn"), "arena.ui join " + ss.Id, ss.Password != null ? ColBtn : ColWine, 12, M + G + rw - 130, y + 10, 118, 32);
                        else Btn(c, R, T(id, "State_" + ss.State), "", ColBtn, 11, M + G + rw - 130, y + 10, 118, 32, ColDim);
                    });
                }
                if (rooms.Count == 0) Text(c, R, tab == "duel" ? T(id, "NoDuels") : T(id, "NoRooms"), 13, M, top, W - 2 * M, listH, TextAnchor.MiddleCenter, ColDim, FontReg);
            }
            else
            {
                var arenas = _data.Arenas.Values.Where(a => a.Enabled && a.Kind == tab).ToList();
                foreach (var a in arenas.Take(6))
                {
                    var aa = a;
                    Session s;
                    _sessions.TryGetValue(a.Id, out s);
                    int n = s?.Players.Count ?? 0;
                    var lo = LoadoutById(a.Loadout);
                    rows.Add(y =>
                    {
                        Box(c, R, ColRow, M + G, y, rw, 52, 14f);
                        Box(c, R, "0.48 0.07 0.14 0.45", M + G + 8, y + 8, 36, 36, 10f);
                        Text(c, R, aa.Name.Substring(0, 1).ToUpper(), 15, M + G + 8, y + 8, 36, 36, TextAnchor.MiddleCenter, ColRose);
                        Text(c, R, aa.Name, 14, M + G + 54, y + 6, rw - 320, 22);
                        Text(c, R, (aa.Layout == "maze" ? T(id, "LayMaze") + " · " : aa.Layout == "roam" ? T(id, "LayRoam") + " · " : "") + T(id, aa.Size == "large" ? "SizeLarge" : "SizeSmall") + " · " + (aa.Kind == "gungame" ? T(id, "GgDesc", _config.GunGameLength) : $"{lo?.Name} · {lo?.Desc}"), 11, M + G + 54, y + 28, rw - 320, 18, TextAnchor.MiddleLeft, ColDim, FontReg);
                        Text(c, R, $"{n} / {aa.Max}", 12, M + G + rw - 236, y + 8, 90, 18, TextAnchor.MiddleRight);
                        Box(c, R, "1 1 1 0.08", M + G + rw - 236, y + 32, 90, 4, 2f);
                        if (n > 0) Box(c, R, ColWine, M + G + rw - 236, y + 32, Mathf.Max(4f, 90f * n / Mathf.Max(1, aa.Max)), 4, 2f);
                        bool here = s != null && s.Players.Contains(id);
                        Btn(c, R, here ? T(id, "YouHere") : T(id, "JoinBtn"), here ? "arena.ui leave" : "arena.ui join " + aa.Id, here ? ColBtn : ColWine, 12, M + G + rw - 130, y + 10, 118, 32);
                    });
                }
                if (arenas.Count == 0) Text(c, R, T(id, "NoArenasTab"), 13, M, top, W - 2 * M, listH, TextAnchor.MiddleCenter, ColDim, FontReg);
            }
            for (int i = 0; i < rows.Count; i++) rows[i](top + G + i * (52 + 8));

            float by = H - M - 46, bw = (W - 2 * M - 2 * G) / 3f;
            Btn(c, R, T(id, "CreateOwn"), "arena.ui custom", ColWine, 13, M, by, bw, 46, ColText, 14f);
            Btn(c, R, T(id, "DuelBtn"), "arena.ui duel.open", ColBtn, 13, M + bw + G, by, bw, 46, ColText, 14f);
            Btn(c, R, T(id, "TopBtn"), "arena.ui top day", ColBtn, 13, M + 2 * (bw + G), by, bw, 46, ColText, 14f);

            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        private string NamesOfTeam(Session s, int team) => string.Join(", ", s.Players.Where(p => s.Team.ContainsKey(p) && s.Team[p] == team).Select(p => S(p).Name ?? p.ToString()).ToArray());

        #endregion

        #region Интерфейс: дуэль

        private void OpenDuelSetup(BasePlayer player, ulong target)
        {
            var id = Id(player);
            var d = Draft(id);
            if (target != 0) d.To = target;
            const float W = 900f, H = 600f;
            var c = new CuiElementContainer();
            Window(c, UiMain, "D", T(id, "DuelTitle"), T(id, "DuelSub"), W, H, "arena.ui close");
            var R = UiMain;
            float top = M + 52 + 2 + M, cw = (W - 2 * M - G) / 2f;

            // кто против кого
            var t = BasePlayer.FindByID(d.To);
            Box(c, R, ColPanel, M, top, cw, 60, 14f);
            Text(c, R, player.displayName, 16, M + 16, top, cw - 32, 40, TextAnchor.MiddleLeft);
            Text(c, R, DuelLine(id), 11, M + 16, top + 34, cw - 32, 18, TextAnchor.MiddleLeft, ColDim, FontReg);
            Box(c, R, ColPanel, M + cw + G, top, cw, 60, 14f);
            Text(c, R, t != null ? t.displayName : T(id, "PickTarget"), 16, M + cw + G + 16, top, cw - 32, 40, TextAnchor.MiddleRight, t != null ? ColText : ColDim);
            if (t != null) Text(c, R, DuelLine(d.To), 11, M + cw + G + 16, top + 34, cw - 32, 18, TextAnchor.MiddleRight, ColDim, FontReg);
            Text(c, R, "VS", 18, W / 2f - 30, top, 60, 60, TextAnchor.MiddleCenter, ColDim);
            top += 60 + G;

            float ph = H - M - 46 - G - top;
            // слева — соперник и набор
            Box(c, R, ColPanel, M, top, cw, ph, 16f);
            Text(c, R, T(id, "Opponent"), 10, M + 14, top + 10, cw - 28, 16, TextAnchor.MiddleLeft, ColDim);
            var online = BasePlayer.activePlayerList.Where(p => p != player && p.IsConnected).OrderBy(p => p.displayName).Take(6).ToList();
            float ow = (cw - 28 - 2 * 6f) / 3f;
            for (int i = 0; i < online.Count; i++)
                Btn(c, R, online[i].displayName, "arena.ui duel.target " + Id(online[i]), Id(online[i]) == d.To ? ColWine : ColBtn, 11, M + 14 + (i % 3) * (ow + 6), top + 32 + (i / 3) * 36, ow, 30, ColText, 10f);
            if (online.Count == 0) Text(c, R, T(id, "NoOneOnline"), 11, M + 14, top + 32, cw - 28, 30, TextAnchor.MiddleLeft, ColDim, FontReg);
            float ly = top + 32 + 2 * 36 + 8;
            Text(c, R, T(id, "WeaponSet"), 10, M + 14, ly, cw - 28, 16, TextAnchor.MiddleLeft, ColDim);
            var los = _config.Loadouts.Take(12).ToList();
            for (int i = 0; i < los.Count; i++)
                Btn(c, R, los[i].Name, "arena.ui duel.set loadout " + los[i].Id, los[i].Id == d.Loadout ? ColWine : ColBtn, 11, M + 14 + (i % 3) * (ow + 6), ly + 22 + (i / 3) * 36, ow, 30, ColText, 10f);

            // справа — арена, раунды, ставка
            float rx = M + cw + G;
            Box(c, R, ColPanel, rx, top, cw, ph, 16f);
            Text(c, R, T(id, "ArenaLbl"), 10, rx + 14, top + 10, cw - 28, 16, TextAnchor.MiddleLeft, ColDim);
            var arenas = _data.Arenas.Values.Where(a => a.Enabled && a.Kind == "match").Take(5).Select(a => new KeyValuePair<string, string>(a.Id, a.Name + "|arena.ui duel.set arena " + a.Id)).ToList();
            arenas.Add(new KeyValuePair<string, string>("-", T(id, "Random") + "|arena.ui duel.set arena -"));
            float aw = (cw - 28 - 6f) / 2f;
            for (int i = 0; i < arenas.Count; i++)
            {
                bool a = (d.Arena ?? "-") == arenas[i].Key;
                Btn(c, R, arenas[i].Value.Split('|')[0], arenas[i].Value.Split('|')[1], a ? ColWine : ColBtn, 11, rx + 14 + (i % 2) * (aw + 6), top + 32 + (i / 2) * 36, aw, 30, ColText, 10f);
            }
            float ry = top + 32 + 3 * 36 + 8;
            Text(c, R, T(id, "RoundsLbl"), 10, rx + 14, ry, cw - 28, 16, TextAnchor.MiddleLeft, ColDim);
            Seg(c, R, rx + 14, ry + 22, cw - 28, 30, _config.RoundOptions.Select(r => new KeyValuePair<string, string>(r.ToString(), r + "|arena.ui duel.set rounds " + r)).ToList(), d.Rounds.ToString());
            ry += 22 + 30 + 14;
            Text(c, R, T(id, "BetLbl"), 10, rx + 14, ry, cw - 28, 16, TextAnchor.MiddleLeft, ColDim);
            Seg(c, R, rx + 14, ry + 22, cw - 28, 30, _config.Bets.Select(b => new KeyValuePair<string, string>(b.ToString(), (b == 0 ? T(id, "NoBet") : b.ToString()) + "|arena.ui duel.set bet " + b)).ToList(), d.Bet.ToString());

            float by = H - M - 46, bw = (W - 2 * M - G) / 2f;
            Btn(c, R, T(id, "SendDuel"), "arena.ui duel.send", ColWine, 14, M, by, bw, 46, ColText, 14f);
            Btn(c, R, T(id, "Cancel"), "arena.ui close", ColBtn, 13, M + bw + G, by, bw, 46, ColText, 14f);
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        private string DuelLine(ulong id)
        {
            var s = S(id);
            int total = s.DuelWins + s.DuelLosses;
            return T(id, "DuelStats", total, total > 0 ? Mathf.RoundToInt(100f * s.DuelWins / total) : 0);
        }

        private void ShowDuelInvite(BasePlayer target, DuelInvite inv)
        {
            var id = Id(target);
            const float W = 460f, H = 250f;
            var c = new CuiElementContainer();
            Window(c, UiPopup, "D", T(id, "DuelInviteTitle", inv.FromName), T(id, "DuelInviteSub", (int)_config.InviteTimeout), W, H, "", false);
            var R = UiPopup;
            var lo = LoadoutById(inv.Loadout);
            Text(c, R, $"{lo?.Name} · {T(id, "ToWins", inv.Rounds)}{(inv.Bet > 0 ? " · " + T(id, "BetN", inv.Bet) : "")}", 14, 0, 92, W, 40, TextAnchor.MiddleCenter, ColText);
            float bw = (W - 2 * M - G) / 2f;
            Btn(c, R, T(id, "Accept"), "arena.ui duel.accept", ColWine, 14, M, H - M - 46, bw, 46, ColText, 14f);
            Btn(c, R, T(id, "Decline"), "arena.ui duel.decline", ColBtn, 13, M + bw + G, H - M - 46, bw, 46, ColText, 14f);
            DestroyWindow(target, UiPopup);
            AddUiScaled(target, c);
        }

        #endregion

        #region Интерфейс: своя арена

        private void OpenCustom(BasePlayer player)
        {
            var id = Id(player);
            var d = CDraft(id);
            var templates = _data.Arenas.Values.Where(a => a.Enabled && a.Kind == "match" && a.Template).ToList();
            if (d.Template == null && templates.Count > 0) d.Template = templates[0].Id;
            const float W = 1040f, H = 640f;
            var c = new CuiElementContainer();
            Window(c, UiMain, "+", T(id, "CustomTitle"), T(id, "CustomSub"), W, H, "arena.ui close");
            var R = UiMain;
            float top = M + 52 + 2 + M, cw = (W - 2 * M - 2 * G) / 3f, ph = H - M - 46 - G - top;

            // 1. шаблон
            Box(c, R, ColPanel, M, top, cw, ph, 16f);
            Text(c, R, T(id, "Step1"), 10, M + 14, top + 10, cw - 28, 16, TextAnchor.MiddleLeft, ColDim);
            for (int i = 0; i < templates.Count && i < 8; i++)
            {
                var a = templates[i];
                bool busy = _sessions.Values.Any(s => s.Kind == "match" && s.Arena.Id == a.Id);
                Btn(c, R, a.Name + (busy ? "  · " + T(id, "Busy") : ""), "arena.ui custom.set template " + a.Id, a.Id == d.Template ? ColWine : ColBtn, 12, M + 12, top + 34 + i * 44, cw - 24, 38, busy ? ColDim : ColText, 12f);
            }
            if (templates.Count == 0) Text(c, R, T(id, "NoTemplates"), 12, M, top, cw, ph, TextAnchor.MiddleCenter, ColDim, FontReg);

            // 2. набор
            float x2 = M + cw + G;
            Box(c, R, ColPanel, x2, top, cw, ph, 16f);
            Text(c, R, T(id, "Step2"), 10, x2 + 14, top + 10, cw - 28, 16, TextAnchor.MiddleLeft, ColDim);
            int nlo = Mathf.Min(12, _config.Loadouts.Count);
            bool two = nlo > 9;
            float lw = two ? (cw - 30) / 2f : cw - 24;
            for (int i = 0; i < nlo; i++)
            {
                var lo = _config.Loadouts[i];
                bool a = lo.Id == d.Loadout;
                float lx = x2 + 12 + (two ? (i % 2) * (lw + 6) : 0f), lyy = top + 34 + (two ? i / 2 : i) * 44;
                Box(c, R, a ? ColWineSoft : ColRow, lx, lyy, lw, 38, 12f);
                Text(c, R, lo.Name, two ? 10 : 12, lx + 12, lyy + 2, lw - 20, 18);
                Text(c, R, lo.Desc, 9, lx + 12, lyy + 19, lw - 20, 16, TextAnchor.MiddleLeft, ColDim, FontReg);
                Btn(c, R, "", "arena.ui custom.set loadout " + lo.Id, "0 0 0 0", 10, lx, lyy, lw, 38, ColText, 0f);
            }

            // 3. правила и доступ
            float x3 = M + 2 * (cw + G), iw = cw - 24;
            Box(c, R, ColPanel, x3, top, cw, ph, 16f);
            float y = top + 10;
            Text(c, R, T(id, "Step3"), 10, x3 + 14, y, iw, 16, TextAnchor.MiddleLeft, ColDim); y += 22;
            Seg(c, R, x3 + 12, y, iw, 30, new[] { 1, 2, 3, 5 }.Select(n => new KeyValuePair<string, string>(n.ToString(), $"{n}v{n}|arena.ui custom.set size {n}")).ToList(), d.TeamSize.ToString()); y += 40;
            Text(c, R, T(id, "RoundsLbl"), 10, x3 + 14, y, iw, 16, TextAnchor.MiddleLeft, ColDim); y += 22;
            Seg(c, R, x3 + 12, y, iw, 30, _config.RoundOptions.Select(n => new KeyValuePair<string, string>(n.ToString(), n + "|arena.ui custom.set rounds " + n)).ToList(), d.Rounds.ToString()); y += 44;
            var tmpl = templates.FirstOrDefault(a => a.Id == d.Template);
            bool canBar = tmpl != null && tmpl.Barricades;
            Box(c, R, ColRow, x3 + 12, y, iw, 38, 12f);
            Text(c, R, canBar ? T(id, "Barricades") : T(id, "BarricadesNo"), 12, x3 + 24, y, iw - 90, 38, TextAnchor.MiddleLeft, canBar ? ColText : ColDim, FontReg);
            if (canBar) Btn(c, R, d.Barricades ? T(id, "On") : T(id, "Off"), "arena.ui custom.set barricades", d.Barricades ? ColWine : ColBtn, 11, x3 + 12 + iw - 70, y + 6, 60, 26, ColText, 10f);
            y += 48;
            Text(c, R, T(id, "Step4"), 10, x3 + 14, y, iw, 16, TextAnchor.MiddleLeft, ColDim); y += 22;
            Seg(c, R, x3 + 12, y, iw, 30, new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("1", T(id, "Public") + "|" + (d.Public ? "" : "arena.ui custom.set public")),
                new KeyValuePair<string, string>("0", T(id, "Private") + "|" + (d.Public ? "arena.ui custom.set public" : "")),
            }, d.Public ? "1" : "0"); y += 40;
            Input(c, R, UiMain + ".pw", d.Password, T(id, "PasswordHint"), "arena.ui custom.pw", x3 + 12, y, iw, 36);

            float by = H - M - 46, bw = (W - 2 * M - G) / 2f;
            int mine = _sessions.Values.Count(s => s.Kind == "match" && !s.IsDuel && s.Owner == id);
            Btn(c, R, T(id, "CreateBtn"), "arena.ui custom.create", ColWine, 14, M, by, bw, 46, ColText, 14f);
            Box(c, R, ColRow, M + bw + G, by, bw, 46, 14f);
            Text(c, R, T(id, "OwnCount", mine, _config.MaxOwnArenas), 12, M + bw + G, by, bw, 46, TextAnchor.MiddleCenter, ColDim);
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        // комната перед стартом: команды, пригласить, старт
        private void OpenRoom(BasePlayer player, Session s)
        {
            var id = Id(player);
            const float W = 900f, H = 560f;
            var c = new CuiElementContainer();
            Window(c, UiMain, "+", T(id, "RoomTitle", s.Arena.Name), $"{s.Loadout?.Name} · {s.TeamSize}v{s.TeamSize} · {T(id, "ToWins", s.RoundsToWin)}{(s.Password != null ? " · " + T(id, "Password") : "")}", W, H, "arena.ui close", false);
            var R = UiMain;
            float top = M + 52 + 2 + M, cw = (W - 2 * M - G) / 2f, ph = H - M - 46 - G - top;
            for (int team = 0; team < 2; team++)
            {
                float x = M + team * (cw + G);
                Box(c, R, ColPanel, x, top, cw, ph, 16f);
                Box(c, R, team == 0 ? "0.88 0.34 0.42 0.25" : "0.44 0.71 1 0.22", x, top, cw, 36, 16f);
                var members = s.Players.Where(p => s.Team.ContainsKey(p) && s.Team[p] == team).ToList();
                Text(c, R, $"{(team == 0 ? T(id, "TeamA") : T(id, "TeamB"))} · {members.Count} / {s.TeamSize}", 13, x, top, cw, 36, TextAnchor.MiddleCenter, team == 0 ? ColRed : ColBlue);
                for (int i = 0; i < members.Count; i++)
                {
                    Box(c, R, ColRow, x + 12, top + 46 + i * 40, cw - 24, 34, 10f);
                    Text(c, R, (S(members[i]).Name ?? "?") + (members[i] == s.Owner ? "  · " + T(id, "Host") : ""), 13, x + 24, top + 46 + i * 40, cw - 48, 34);
                }
                bool mineTeam = s.Team.ContainsKey(id) && s.Team[id] == team;
                if (!mineTeam && members.Count < s.TeamSize)
                    Btn(c, R, T(id, "SwitchTeam"), "arena.ui room.team " + team, ColBtn, 12, x + 12, top + ph - 48, cw - 24, 36, ColText, 12f);
            }
            float by = H - M - 46, bw = (W - 2 * M - 2 * G) / 3f;
            bool owner = s.Owner == id || IsAdmin(player);
            Btn(c, R, owner ? T(id, "StartBtn") : T(id, "WaitOwner"), owner ? "arena.ui room.start" : "", owner ? ColWine : ColBtn, 14, M, by, bw, 46, owner ? ColText : ColDim, 14f);
            var inviteTo = BasePlayer.activePlayerList.FirstOrDefault(p => p != player && !s.Players.Contains(Id(p)) && SessionOf(p) == null);
            Btn(c, R, inviteTo != null ? T(id, "InviteBtn", inviteTo.displayName) : T(id, "InviteNone"), inviteTo != null ? "arena.ui room.invite " + Id(inviteTo) : "", ColBtn, 12, M + bw + G, by, bw, 46, ColText, 14f);
            Btn(c, R, owner ? T(id, "CloseRoom") : T(id, "LeaveRoom"), owner ? "arena.ui room.close" : "arena.ui leave", ColBtn, 13, M + 2 * (bw + G), by, bw, 46, ColText, 14f);
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        private void ShowRoomInvite(BasePlayer target, Session s, string fromName)
        {
            var id = Id(target);
            const float W = 460f, H = 230f;
            var c = new CuiElementContainer();
            Window(c, UiPopup, "+", T(id, "RoomInvite", fromName), $"{s.Arena.Name} · {s.TeamSize}v{s.TeamSize}", W, H, "", false);
            var R = UiPopup;
            float bw = (W - 2 * M - G) / 2f;
            _pwInput[id] = s.Password;
            Btn(c, R, T(id, "JoinBtn"), "arena.ui joinpw " + s.Id, ColWine, 14, M, H - M - 46, bw, 46, ColText, 14f);
            Btn(c, R, T(id, "Decline"), "arena.ui close", ColBtn, 13, M + bw + G, H - M - 46, bw, 46, ColText, 14f);
            DestroyWindow(target, UiPopup);
            AddUiScaled(target, c);
        }

        private void ShowPassword(BasePlayer player, string sessionId)
        {
            var id = Id(player);
            const float W = 420f, H = 220f;
            var c = new CuiElementContainer();
            Window(c, UiPopup, "?", T(id, "Password"), T(id, "PasswordSub"), W, H, "arena.ui close", false);
            var R = UiPopup;
            Input(c, R, UiPopup + ".in", "", T(id, "PasswordHint"), "arena.ui pw", M, 94, W - 2 * M, 38);
            Btn(c, R, T(id, "JoinBtn"), "arena.ui joinpw " + sessionId, ColWine, 14, M, H - M - 44, W - 2 * M, 44, ColText, 14f);
            DestroyWindow(player, UiPopup);
            AddUiScaled(player, c);
        }

        #endregion

        #region Интерфейс: HUD, киллфид, экран смерти, итоги

        private void RefreshHudAll(Session s)
        {
            foreach (var id in s.Players.ToList())
            {
                var p = BasePlayer.FindByID(id);
                if (p != null && p.IsConnected) RefreshHud(p, s);
            }
        }

        private void RefreshHud(BasePlayer player, Session s)
        {
            var id = Id(player);
            var c = new CuiElementContainer();
            int k, d, st;
            s.Kills.TryGetValue(id, out k); s.Deaths.TryGetValue(id, out d); s.Streak.TryGetValue(id, out st);
            string kd = d == 0 ? k.ToString() : (k / (float)d).ToString("0.0");

            if (s.Kind == "match" || s.Kind == "tdm")
            {
                // счёт по центру сверху
                Root(c, UiHud, "Hud", "0 0 0 0", "0.5 1", "0.5 1", "-190 -74", "190 -14", 0f, false);
                string goal = s.Kind == "tdm" ? T(id, "ToKills", _config.TdmKills) : T(id, "RoundN", Mathf.Max(1, s.Round)) + " · " + T(id, "ToWins", s.RoundsToWin);
                Box(c, UiHud, "0.66 0.13 0.22 0.88", 0, 4, 150, 50, 14f);
                Text(c, UiHud, s.Score[0].ToString(), 26, 0, 4, 140, 50, TextAnchor.MiddleRight);
                Text(c, UiHud, s.Kind == "match" && s.IsDuel ? Esc(NamesOfTeam(s, 0)) : T(id, "TeamA"), 11, 12, 4, 90, 50, TextAnchor.MiddleLeft);
                Box(c, UiHud, "0.05 0.04 0.04 0.92", 150, 0, 80, 58, 8f);
                Text(c, UiHud, goal, 10, 150, 0, 80, 58, TextAnchor.MiddleCenter, ColDim);
                Box(c, UiHud, "0.20 0.38 0.62 0.88", 230, 4, 150, 50, 14f);
                Text(c, UiHud, s.Score[1].ToString(), 26, 240, 4, 140, 50, TextAnchor.MiddleLeft);
                Text(c, UiHud, s.Kind == "match" && s.IsDuel ? Esc(NamesOfTeam(s, 1)) : T(id, "TeamB"), 11, 278, 4, 90, 50, TextAnchor.MiddleRight);
            }
            else
            {
                // своя статистика слева сверху + топ
                Root(c, UiHud, "Hud", ColHud, "0 1", "0 1", "16 -150", "236 -16", 16f, false);
                Text(c, UiHud, s.Kind == "gungame" ? T(id, "Tab_gungame") + " · " + s.Arena.Name : s.Arena.Name, 10, 12, 8, 196, 16, TextAnchor.MiddleLeft, ColDim);
                var vals = new[] { k.ToString(), d.ToString(), kd, st.ToString() };
                var lbls = new[] { T(id, "HKills"), T(id, "HDeaths"), "K/D", T(id, "HStreak") };
                for (int i = 0; i < 4; i++)
                {
                    Text(c, UiHud, vals[i], 18, 10 + i * 50, 26, 50, 24, TextAnchor.MiddleCenter, i == 3 && st >= 5 ? ColGold : ColText);
                    Text(c, UiHud, lbls[i], 9, 10 + i * 50, 50, 50, 14, TextAnchor.MiddleCenter, ColDim, FontReg);
                }
                var top = s.Players.OrderByDescending(p => s.Kind == "gungame" ? (s.Level.ContainsKey(p) ? s.Level[p] : 0) * 1000 + (s.Kills.ContainsKey(p) ? s.Kills[p] : 0) : (s.Kills.ContainsKey(p) ? s.Kills[p] : 0)).Take(4).ToList();
                for (int i = 0; i < top.Count; i++)
                {
                    bool me = top[i] == id;
                    string val = s.Kind == "gungame" ? $"{(s.Level.ContainsKey(top[i]) ? s.Level[top[i]] + 1 : 1)}/{s.Ladder.Count}" : (s.Kills.ContainsKey(top[i]) ? s.Kills[top[i]] : 0).ToString();
                    Text(c, UiHud, $"{i + 1}", 11, 12, 72 + i * 15, 16, 15, TextAnchor.MiddleLeft, ColDim);
                    Text(c, UiHud, Esc(S(top[i]).Name ?? "?"), 11, 28, 72 + i * 15, 140, 15, TextAnchor.MiddleLeft, me ? ColRose : ColText);
                    Text(c, UiHud, val, 11, 160, 72 + i * 15, 48, 15, TextAnchor.MiddleRight, ColText);
                }

                if (s.Kind == "gungame" && s.Ladder.Count > 0)
                {
                    int lvl; s.Level.TryGetValue(id, out lvl);
                    int n = s.Ladder.Count;
                    float sw = 62f, total = n * sw + (n - 1) * 4f;
                    Root(c, UiHud + ".gg", "Hud", ColHud, "0.5 1", "0.5 1", $"{-total / 2f - 12} -80", $"{total / 2f + 12} -14", 16f, false);
                    for (int i = 0; i < n; i++)
                    {
                        string col = i < lvl ? "0.50 0.83 0.60 0.20" : i == lvl ? ColWine : "1 1 1 0.05";
                        Box(c, UiHud + ".gg", col, 12 + i * (sw + 4), 10, sw, 34, 9f);
                        Text(c, UiHud + ".gg", s.Ladder[i].Name, 10, 12 + i * (sw + 4), 10, sw, 34, TextAnchor.MiddleCenter, i < lvl ? ColGreen : i == lvl ? ColText : ColDim);
                    }
                    var leader = s.Level.OrderByDescending(x => x.Value).FirstOrDefault();
                    Text(c, UiHud + ".gg", T(id, "GgYou", lvl + 1, n) + "   ·   " + T(id, "GgLeader", Esc(S(leader.Key).Name ?? "?"), leader.Value + 1, n), 10, 12, 48, total, 14, TextAnchor.MiddleCenter, ColDim, FontReg);
                }
            }
            CuiHelper.DestroyUi(player, UiHud);
            CuiHelper.DestroyUi(player, UiHud + ".gg");
            AddUiScaled(player, c);
            DrawFeed(player, s);
        }

        private void AddFeed(Session s, string text)
        {
            s.Feed.Add(new FeedLine { Text = text, Until = Time.realtimeSinceStartup + 6f });
            if (s.Feed.Count > 5) s.Feed.RemoveAt(0);
            foreach (var id in s.Players) { var p = BasePlayer.FindByID(id); if (p != null) DrawFeed(p, s); }
        }

        private void DrawFeed(BasePlayer player, Session s)
        {
            CuiHelper.DestroyUi(player, UiFeed);
            if (s.Feed.Count == 0) return;
            var c = new CuiElementContainer();
            Root(c, UiFeed, "Hud", "0 0 0 0", "1 1", "1 1", $"-420 {-16 - s.Feed.Count * 32}", "-16 -16", 0f, false);
            for (int i = 0; i < s.Feed.Count; i++)
            {
                Box(c, UiFeed, "0.04 0.03 0.03 0.65", 0, i * 32, 404, 28, 9f);
                Text(c, UiFeed, s.Feed[i].Text, 12, 10, i * 32, 384, 28, TextAnchor.MiddleRight);
            }
            AddUiScaled(player, c);
        }

        private void ShowDeath(BasePlayer victim, BasePlayer killer, string weapon, float dist, bool hs)
        {
            var id = Id(victim);
            var c = new CuiElementContainer();
            const float W = 440f, H = 220f;
            Root(c, UiDeath, "Overlay", ColBg, "0.5 0.5", "0.5 0.5", $"{-W / 2f} {-H / 2f + 40}", $"{W / 2f} {H / 2f + 40}", 18f, false);
            Text(c, UiDeath, T(id, "YouDied"), 12, 0, 14, W, 18, TextAnchor.MiddleCenter, ColRed);
            if (killer != null)
            {
                Box(c, UiDeath, ColPanel, M, 40, W - 2 * M, 60, 14f);
                Text(c, UiDeath, Esc(killer.displayName), 20, M + 16, 40, W - 2 * M - 130, 60);
                Text(c, UiDeath, Mathf.RoundToInt(killer.health).ToString(), 26, W - M - 110, 42, 96, 40, TextAnchor.MiddleRight, ColGreen);
                Text(c, UiDeath, T(id, "HpLeft"), 9, W - M - 110, 78, 96, 14, TextAnchor.MiddleRight, ColDim, FontReg);
                float tw = (W - 2 * M - 2 * 8f) / 3f;
                var vals = new[] { weapon, Mathf.RoundToInt(dist) + " м", hs ? T(id, "Head") : T(id, "Body") };
                var lbls = new[] { T(id, "Weapon"), T(id, "Distance"), T(id, "Where") };
                for (int i = 0; i < 3; i++)
                {
                    Box(c, UiDeath, ColPanel, M + i * (tw + 8), 110, tw, 54, 12f);
                    Text(c, UiDeath, vals[i], 14, M + i * (tw + 8), 114, tw, 26, TextAnchor.MiddleCenter, i == 2 && hs ? ColGold : ColText);
                    Text(c, UiDeath, lbls[i], 9, M + i * (tw + 8), 140, tw, 16, TextAnchor.MiddleCenter, ColDim, FontReg);
                }
            }
            else Text(c, UiDeath, T(id, "SelfDeath"), 16, 0, 40, W, 124, TextAnchor.MiddleCenter, ColDim);
            Text(c, UiDeath, T(id, "Respawning"), 11, 0, H - 40, W, 24, TextAnchor.MiddleCenter, ColDim, FontReg);
            CuiHelper.DestroyUi(victim, UiDeath);
            AddUiScaled(victim, c);
        }

        private void ShowResult(BasePlayer player, Session s, bool win, bool forfeit)
        {
            var id = Id(player);
            const float W = 560f, H = 380f;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = win ? "0.10 0.22 0.13 0.55" : "0.22 0.05 0.08 0.55" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", UiMain + ".dim");
            Root(c, UiMain, UiMain + ".dim", ColBg, "0.5 0.5", "0.5 0.5", $"{-W / 2f} {-H / 2f}", $"{W / 2f} {H / 2f}", 18f, true);
            var R = UiMain;
            Text(c, R, win ? T(id, "Victory") : T(id, "Defeat"), 40, 0, 20, W, 54, TextAnchor.MiddleCenter, win ? ColGreen : ColRed);
            Text(c, R, $"{(s.IsDuel ? T(id, "Tab_duel") : T(id, "Tab_custom"))} · {s.Score[0]} : {s.Score[1]}{(forfeit ? " · " + T(id, "Forfeit") : "")}", 13, 0, 74, W, 20, TextAnchor.MiddleCenter, ColDim, FontReg);
            float y = 108;
            Text(c, R, T(id, "Player"), 10, M + 12, y, 200, 18, TextAnchor.MiddleLeft, ColDim);
            Text(c, R, T(id, "HKills"), 10, W - M - 200, y, 60, 18, TextAnchor.MiddleCenter, ColDim);
            Text(c, R, T(id, "HDeaths"), 10, W - M - 140, y, 60, 18, TextAnchor.MiddleCenter, ColDim);
            Text(c, R, "K/D", 10, W - M - 80, y, 60, 18, TextAnchor.MiddleCenter, ColDim);
            y += 22;
            foreach (var pid in s.Players.OrderBy(p => s.Team.ContainsKey(p) ? s.Team[p] : 0).Take(6))
            {
                int k, d;
                s.Kills.TryGetValue(pid, out k); s.Deaths.TryGetValue(pid, out d);
                Box(c, R, pid == id ? ColWineSoft : ColRow, M, y, W - 2 * M, 30, 10f);
                Text(c, R, Esc(S(pid).Name ?? "?"), 12, M + 12, y, 220, 30, TextAnchor.MiddleLeft, s.Team.ContainsKey(pid) && s.Team[pid] == 0 ? ColRose : ColBlue);
                Text(c, R, k.ToString(), 12, W - M - 200, y, 60, 30, TextAnchor.MiddleCenter);
                Text(c, R, d.ToString(), 12, W - M - 140, y, 60, 30, TextAnchor.MiddleCenter);
                Text(c, R, d == 0 ? k.ToString() : (k / (float)d).ToString("0.0"), 12, W - M - 80, y, 60, 30, TextAnchor.MiddleCenter);
                y += 34;
            }
            Text(c, R, T(id, "BackSoon"), 11, 0, H - 36, W, 20, TextAnchor.MiddleCenter, ColDim, FontReg);
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        private void Center(BasePlayer player, string text, string color, float seconds)
        {
            if (player == null || !player.IsConnected) return;
            var c = new CuiElementContainer();
            c.Add(new CuiLabel { Text = { Text = text, FontSize = 30, Align = TextAnchor.MiddleCenter, Color = color, Font = Font }, RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-400 60", OffsetMax = "400 170" } }, "Hud", UiCenter);
            CuiHelper.DestroyUi(player, UiCenter);
            AddUiScaled(player, c);
            var p = player;
            timer.Once(seconds, () => { if (p != null && p.IsConnected) CuiHelper.DestroyUi(p, UiCenter); });
        }

        private void Toast(BasePlayer player, string text, bool warn)
        {
            if (player == null || !player.IsConnected) return;
            var c = new CuiElementContainer();
            Root(c, UiToast, "Hud", warn ? "0.40 0.06 0.11 0.92" : "0.07 0.065 0.065 0.90", "0.5 1", "0.5 1", "-240 -128", "240 -86", 20f, false);
            c.Add(new CuiLabel { Text = { Text = text, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = ColText, Font = Font }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, UiToast);
            CuiHelper.DestroyUi(player, UiToast);
            AddUiScaled(player, c);
            var p = player;
            timer.Once(3f, () => { if (p != null && p.IsConnected) CuiHelper.DestroyUi(p, UiToast); });
        }

        private void Broadcast(Session s, string text, bool warn)
        {
            foreach (var id in s.Players) { var p = BasePlayer.FindByID(id); if (p != null) Toast(p, text, warn); }
        }

        #endregion

        #region Топ и профиль

        private void ShowTop(BasePlayer player, string period)
        {
            var id = Id(player);
            if (string.IsNullOrEmpty(period)) period = "day";
            const float W = 760f, H = 600f;
            var c = new CuiElementContainer();
            Window(c, UiMain, "T", T(id, "TopTitle"), T(id, "TopSub"), W, H, "arena.ui close");
            var R = UiMain;
            float top = M + 52 + 2 + M;
            Seg(c, R, M, top, W - 2 * M, 34, new[] { "day", "week", "all" }.Select(p => new KeyValuePair<string, string>(p, T(id, "P_" + p) + "|arena.ui top " + p)).ToList(), period);
            top += 34 + G;
            var weekKeys = Enumerable.Range(0, 7).Select(i => DateTime.UtcNow.AddDays(-i).ToString("yyyy-MM-dd")).ToList();
            Func<Stats, int> score = s => period == "day" ? (s.DayKills.ContainsKey(Today) ? s.DayKills[Today] : 0)
                                     : period == "week" ? weekKeys.Sum(k => s.DayKills.ContainsKey(k) ? s.DayKills[k] : 0) : s.Kills;
            var list = _stats.Where(kv => score(kv.Value) > 0).OrderByDescending(kv => score(kv.Value)).Take(10).ToList();
            float ph = H - M - top;
            Box(c, R, ColPanel, M, top, W - 2 * M, ph, 16f);
            float y = top + 10, rw = W - 2 * M - 24;
            Text(c, R, "#", 10, M + 24, y, 30, 18, TextAnchor.MiddleLeft, ColDim);
            Text(c, R, T(id, "Player"), 10, M + 56, y, 240, 18, TextAnchor.MiddleLeft, ColDim);
            Text(c, R, T(id, "HKills"), 10, M + 12 + rw - 330, y, 80, 18, TextAnchor.MiddleCenter, ColDim);
            Text(c, R, "K/D", 10, M + 12 + rw - 250, y, 80, 18, TextAnchor.MiddleCenter, ColDim);
            Text(c, R, "HS %", 10, M + 12 + rw - 170, y, 80, 18, TextAnchor.MiddleCenter, ColDim);
            Text(c, R, T(id, "Accuracy"), 10, M + 12 + rw - 90, y, 80, 18, TextAnchor.MiddleCenter, ColDim);
            y += 24;
            for (int i = 0; i < list.Count; i++)
            {
                var st = list[i].Value;
                bool me = list[i].Key == id;
                Box(c, R, me ? ColWineSoft : ColRow, M + 12, y, rw, 34, 10f);
                Text(c, R, (i + 1).ToString(), 13, M + 24, y, 30, 34, TextAnchor.MiddleLeft, i == 0 ? ColGold : i < 3 ? ColText : ColDim);
                Text(c, R, Esc(st.Name ?? "?"), 13, M + 56, y, 260, 34);
                Text(c, R, score(st).ToString(), 13, M + 12 + rw - 330, y, 80, 34, TextAnchor.MiddleCenter);
                Text(c, R, st.Deaths == 0 ? st.Kills.ToString() : (st.Kills / (float)st.Deaths).ToString("0.00"), 13, M + 12 + rw - 250, y, 80, 34, TextAnchor.MiddleCenter);
                Text(c, R, st.Kills == 0 ? "—" : Mathf.RoundToInt(100f * st.Headshots / st.Kills) + "%", 13, M + 12 + rw - 170, y, 80, 34, TextAnchor.MiddleCenter);
                Text(c, R, st.Shots == 0 ? "—" : Mathf.RoundToInt(100f * Mathf.Min(st.Hits, st.Shots) / st.Shots) + "%", 13, M + 12 + rw - 90, y, 80, 34, TextAnchor.MiddleCenter);
                y += 38;
            }
            if (list.Count == 0) Text(c, R, T(id, "TopEmpty"), 13, M, top, W - 2 * M, ph, TextAnchor.MiddleCenter, ColDim, FontReg);
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        // строка в профиль Hub
        private string OnHubProfile(ulong userId, string lng)
        {
            Stats s;
            if (!_stats.TryGetValue(userId, out s)) return null;
            bool en = lng == "en";
            string kd = s.Deaths == 0 ? s.Kills.ToString() : (s.Kills / (float)s.Deaths).ToString("0.00");
            string hs = s.Kills == 0 ? "—" : Mathf.RoundToInt(100f * s.Headshots / s.Kills) + "%";
            string acc = s.Shots == 0 ? "—" : Mathf.RoundToInt(100f * Mathf.Min(s.Hits, s.Shots) / s.Shots) + "%";
            return en
                ? $"<color=#f0a6b1>ARENAS</color>\nKills {s.Kills} · Deaths {s.Deaths} · K/D {kd}\nHeadshots {hs} · Accuracy {acc} · Best streak {s.BestStreak}\nDuels {s.DuelWins}–{s.DuelLosses} · Gun game wins {s.GunGameWins} · TDM wins {s.TdmWins}"
                : $"<color=#f0a6b1>АРЕНЫ</color>\nУбийств {s.Kills} · Смертей {s.Deaths} · K/D {kd}\nХедшоты {hs} · Точность {acc} · Лучшая серия {s.BestStreak}\nДуэли {s.DuelWins}–{s.DuelLosses} · Побед в ган-гейме {s.GunGameWins} · В TDM {s.TdmWins}";
        }

        // API: матч турнира. Возвращает id сессии или null, если нет свободной арены / кто-то занят
        [HookMethod("StartTournamentMatch")]
        public string StartTournamentMatch(List<ulong> teamA, List<ulong> teamB, string loadout, int rounds, string tag)
        {
            var arena = FreeMatchArena(null);
            if (arena == null || teamA == null || teamB == null || teamA.Count == 0 || teamB.Count == 0) return null;
            var all = teamA.Concat(teamB).Select(BasePlayer.FindByID).ToList();
            if (all.Any(p => p == null || !p.IsConnected || p.IsDead())) return null;
            if (all.Any(p => { var cur = SessionOf(p); return cur != null && cur.Kind == "match"; })) return null;
            foreach (var p in all) if (SessionOf(p) != null) Leave(p, false);
            var s = CreateMatch(arena, teamA[0], S(teamA[0]).Name ?? "?", loadout, Mathf.Max(teamA.Count, teamB.Count), rounds, false, teamA.Count == 1 && teamB.Count == 1, false, null, 0);
            s.Tag = tag;
            foreach (var p in all)
            {
                var pid = Id(p);
                if (Hub != null && HubMode(p) != _config.DuelHubMode) Hub.Call("SendToMode", p, _config.DuelHubMode, true);
                s.Players.Add(pid);
                _in[pid] = s;
                s.Team[pid] = teamA.Contains(pid) ? 0 : 1;
                s.Kills[pid] = 0; s.Deaths[pid] = 0; s.Streak[pid] = 0;
                S(pid, p.displayName);
                CloseAll(p);
            }
            StartRound(s);
            return s.Id;
        }

        [HookMethod("GetLiveMatches")]
        public List<Dictionary<string, object>> GetLiveMatches()
        {
            return _sessions.Values.Where(s => s.Kind == "match" && s.State != "waiting" && s.State != "ended").Select(s => new Dictionary<string, object>
            {
                ["id"] = s.Id, ["arena"] = s.Arena.Name, ["a"] = NamesOfTeam(s, 0), ["b"] = NamesOfTeam(s, 1),
                ["score"] = s.Score[0] + ":" + s.Score[1], ["players"] = s.Players.ToList(), ["tag"] = s.Tag ?? ""
            }).ToList();
        }

        [HookMethod("IsInArena")] public bool IsInArena(BasePlayer player) => SessionOf(player) != null;

        [HookMethod("GetArenaStats")] public Dictionary<string, int> GetArenaStats(ulong id)
        {
            Stats s;
            if (!_stats.TryGetValue(id, out s)) return null;
            return new Dictionary<string, int> { ["kills"] = s.Kills, ["deaths"] = s.Deaths, ["headshots"] = s.Headshots, ["shots"] = s.Shots, ["hits"] = s.Hits, ["beststreak"] = s.BestStreak, ["duelwins"] = s.DuelWins };
        }

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
            new[] { "BrowserTitle", "АРЕНЫ", "ARENAS" },
            new[] { "BrowserSub", "арена = пушка · набор задаёт создатель · /arena", "arena = gun · kit set by the creator · /arena" },
            new[] { "Tab_ffa", "FFA", "FFA" }, new[] { "Tab_tdm", "Команды", "Teams" }, new[] { "Tab_gungame", "Ган-гейм", "Gun game" },
            new[] { "Tab_duel", "Дуэли", "Duels" }, new[] { "Tab_custom", "Свои арены", "Custom" },
            new[] { "JoinBtn", "ВОЙТИ", "JOIN" }, new[] { "YouHere", "ВЫЙТИ", "LEAVE" },
            new[] { "CreateOwn", "СОЗДАТЬ СВОЮ АРЕНУ", "CREATE ARENA" }, new[] { "DuelBtn", "ВЫЗВАТЬ НА ДУЭЛЬ", "CHALLENGE TO DUEL" }, new[] { "TopBtn", "ТОП ИГРОКОВ", "TOP PLAYERS" },
            new[] { "NoArenasTab", "Здесь пока нет арен — админ добавит их через /arenaadmin", "No arenas here yet" },
            new[] { "NoDuels", "Сейчас никто не дерётся на дуэли", "No duels right now" },
            new[] { "NoRooms", "Своих арен пока нет — создай первую", "No custom arenas yet — create one" },
            new[] { "GgDesc", "случайная лестница · {0} пушек · последняя — камень", "random ladder · {0} guns · last is a rock" },
            new[] { "ToWins", "до {0} побед", "first to {0}" }, new[] { "ToKills", "до {0} убийств", "to {0} kills" },
            new[] { "BetN", "ставка {0}", "bet {0}" },
            new[] { "State_waiting", "ждут игроков", "waiting" }, new[] { "State_countdown", "старт", "starting" },
            new[] { "State_live", "идёт бой", "live" }, new[] { "State_between", "между раундами", "between rounds" }, new[] { "State_ended", "закончен", "ended" },
            new[] { "Password", "ПАРОЛЬ", "PASSWORD" }, new[] { "Locked", "по паролю", "password" }, new[] { "Host", "создатель", "host" }, new[] { "PasswordSub", "арена закрыта паролем", "this arena is password protected" },
            new[] { "PasswordHint", "пароль (по желанию)", "password (optional)" },

            new[] { "Joined", "{0} зашёл на арену", "{0} joined" },
            new[] { "Left", "Ты вышел с арены", "You left the arena" },
            new[] { "NotInArena", "Ты не на арене", "You are not in an arena" },
            new[] { "AlreadyIn", "Ты уже здесь", "You are already here" },
            new[] { "NoArena", "Арена не найдена", "Arena not found" },
            new[] { "NoSpawns", "{0}: нет точек спавна", "{0}: no spawn points" },
            new[] { "Full", "Мест нет", "Full" },
            new[] { "DeadNow", "Сначала возродись", "Respawn first" },
            new[] { "MatchStarted", "Матч уже начался", "The match has already started" },
            new[] { "Border", "Граница арены", "Arena border" },
            new[] { "WaitRound", "Ждёшь следующий раунд", "Waiting for the next round" },

            new[] { "HKills", "убийств", "kills" }, new[] { "HDeaths", "смертей", "deaths" }, new[] { "HStreak", "серия", "streak" },
            new[] { "TeamA", "Красные", "Red" }, new[] { "TeamB", "Синие", "Blue" },
            new[] { "RoundN", "Раунд {0}", "Round {0}" }, new[] { "Fight", "БОЙ!", "FIGHT!" },
            new[] { "RoundWin", "РАУНД ВАШ", "ROUND WON" }, new[] { "RoundLose", "РАУНД ПРОИГРАН", "ROUND LOST" }, new[] { "RoundDraw", "НИЧЬЯ", "DRAW" },
            new[] { "TeamWin", "ПОБЕДА КОМАНДЫ", "TEAM VICTORY" }, new[] { "TeamLose", "ПОРАЖЕНИЕ", "DEFEAT" },
            new[] { "Victory", "ПОБЕДА", "VICTORY" }, new[] { "Defeat", "ПОРАЖЕНИЕ", "DEFEAT" }, new[] { "Forfeit", "соперник вышел", "opponent left" },
            new[] { "BackSoon", "возврат через 5 с", "returning in 5s" }, new[] { "Player", "ИГРОК", "PLAYER" },
            new[] { "BetSameIp", "Ставки между игроками с одного IP отключены", "Bets between players on the same IP are disabled" },
            new[] { "WinReason", "за победу", "for the win" }, new[] { "BetWon", "выигрыш ставки", "bet won" },
            new[] { "Streak", "за серию {0}", "for a {0} streak" }, new[] { "StreakBig", "СЕРИЯ {0}", "STREAK {0}" },
            new[] { "StreakAll", "{0} — серия {1}!", "{0} is on a {1} streak!" },
            new[] { "GgNext", "{0}  ·  {1} / {2}", "{0}  ·  {1} / {2}" },
            new[] { "GgWinner", "{0} ПРОШЁЛ ЛЕСТНИЦУ!", "{0} WON THE GUN GAME!" },
            new[] { "GgYou", "ты {0} / {1}", "you {0} / {1}" }, new[] { "GgLeader", "лидер: {0} {1} / {2}", "leader: {0} {1} / {2}" },

            new[] { "YouDied", "ТЫ УБИТ", "YOU DIED" }, new[] { "HpLeft", "ХП осталось", "HP left" },
            new[] { "Weapon", "оружие", "weapon" }, new[] { "Distance", "дистанция", "distance" }, new[] { "Where", "куда", "hit" },
            new[] { "Head", "голова", "head" }, new[] { "Body", "тело", "body" },
            new[] { "SelfDeath", "Ты погиб", "You died" }, new[] { "Respawning", "возрождение через пару секунд…", "respawning in a moment…" },

            new[] { "DuelTitle", "ДУЭЛЬ", "DUEL" }, new[] { "DuelSub", "вызов игроку · без рейтинга", "challenge a player · unranked" },
            new[] { "DuelStats", "дуэлей {0} · {1}% побед", "{0} duels · {1}% wins" },
            new[] { "Opponent", "СОПЕРНИК", "OPPONENT" }, new[] { "WeaponSet", "ОРУЖИЕ И СЕТ", "WEAPON & KIT" },
            new[] { "ArenaLbl", "АРЕНА", "ARENA" }, new[] { "RoundsLbl", "РАУНДОВ ДО ПОБЕДЫ", "ROUNDS TO WIN" }, new[] { "BetLbl", "СТАВКА МОНЕТАМИ", "COIN BET" },
            new[] { "NoBet", "без ставки", "no bet" }, new[] { "Random", "Случайная", "Random" },
            new[] { "SendDuel", "ОТПРАВИТЬ ВЫЗОВ", "SEND CHALLENGE" }, new[] { "Cancel", "Отмена", "Cancel" },
            new[] { "PickTarget", "выбери соперника", "pick an opponent" }, new[] { "NoOneOnline", "никого нет онлайн", "nobody online" },
            new[] { "DuelSent", "Вызов отправлен: {0}", "Challenge sent to {0}" },
            new[] { "DuelInviteTitle", "{0} ВЫЗЫВАЕТ НА ДУЭЛЬ", "{0} CHALLENGES YOU" }, new[] { "DuelInviteSub", "ответь за {0} с · /duel accept", "answer within {0}s · /duel accept" },
            new[] { "Accept", "ПРИНЯТЬ", "ACCEPT" }, new[] { "Decline", "Отклонить", "Decline" },
            new[] { "DuelDeclined", "{0} отказался от дуэли", "{0} declined" },
            new[] { "NoInvite", "Приглашений нет", "No invites" }, new[] { "InviteGone", "Игрок ушёл", "The player left" },
            new[] { "TargetBusy", "{0} сейчас в матче", "{0} is in a match" },
            new[] { "NoCoins", "Не хватает монет на ставку", "Not enough coins" }, new[] { "TargetNoCoins", "У соперника не хватает монет", "Opponent lacks coins" },
            new[] { "NoFreeArena", "Все арены для дуэлей заняты", "All duel arenas are busy" },

            new[] { "CustomTitle", "СВОЯ АРЕНА", "CUSTOM ARENA" }, new[] { "CustomSub", "шаблон · набор · правила · доступ", "template · kit · rules · access" },
            new[] { "Step1", "1 · ШАБЛОН", "1 · TEMPLATE" }, new[] { "Step2", "2 · НАБОР", "2 · KIT" }, new[] { "Step3", "3 · КОМАНДЫ", "3 · TEAMS" }, new[] { "Step4", "4 · ДОСТУП", "4 · ACCESS" },
            new[] { "Busy", "занят", "busy" }, new[] { "NoTemplates", "Шаблонов нет — админ: /arenaadmin create <id> match", "No templates yet" },
            new[] { "Barricades", "Баррикады в бою (исчезают после раунда)", "Barricades (removed after each round)" },
            new[] { "BarricadesNo", "Баррикады на этом шаблоне выключены", "Barricades disabled on this template" },
            new[] { "On", "ВКЛ", "ON" }, new[] { "Off", "ВЫКЛ", "OFF" },
            new[] { "Public", "Публичная", "Public" }, new[] { "Private", "По приглашению", "Invite only" },
            new[] { "CreateBtn", "СОЗДАТЬ И ПРИГЛАСИТЬ", "CREATE & INVITE" },
            new[] { "OwnCount", "Твоих арен: {0} / {1}", "Your arenas: {0} / {1}" },
            new[] { "OwnLimit", "Можно держать не больше {0} арен", "You can host up to {0} arenas" },
            new[] { "RoomTitle", "КОМНАТА · {0}", "ROOM · {0}" },
            new[] { "SwitchTeam", "ПЕРЕЙТИ СЮДА", "JOIN THIS TEAM" },
            new[] { "StartBtn", "НАЧАТЬ МАТЧ", "START MATCH" }, new[] { "WaitOwner", "ждём создателя", "waiting for host" },
            new[] { "InviteBtn", "Пригласить: {0}", "Invite: {0}" }, new[] { "InviteNone", "Некого пригласить", "Nobody to invite" },
            new[] { "InviteSent", "Приглашение отправлено: {0}", "Invite sent to {0}" },
            new[] { "RoomInvite", "{0} ЗОВЁТ НА АРЕНУ", "{0} INVITES YOU" },
            new[] { "CloseRoom", "Закрыть комнату", "Close room" }, new[] { "LeaveRoom", "Выйти", "Leave" },
            new[] { "RoomClosed", "Комната закрыта", "Room closed" },
            new[] { "TeamFull", "В этой команде нет мест", "That team is full" }, new[] { "NeedBoth", "Нужен хотя бы один игрок в каждой команде", "Need at least one player per team" },

            new[] { "TopTitle", "ТОП ИГРОКОВ", "TOP PLAYERS" }, new[] { "TopSub", "убийства на аренах · K/D · хедшоты · точность", "arena kills · K/D · headshots · accuracy" },
            new[] { "P_day", "За день", "Today" }, new[] { "P_week", "За неделю", "This week" }, new[] { "P_all", "За всё время", "All time" },
            new[] { "Accuracy", "точность", "accuracy" }, new[] { "TopEmpty", "Пока пусто — иди на арену!", "Nothing yet — go play!" },

            new[] { "NotFound", "Игрок «{0}» не найден", "Player '{0}' not found" },
            new[] { "NoPerm", "Нет прав", "No permission" },
            new[] { "NoArenas", "Арен нет", "No arenas" },
            new[] { "AdmCreated", "Арена {0} ({1}) создана здесь. Добавь спавны: spawn (ffa/gungame) или spawna/spawnb (tdm/match)", "Arena {0} ({1}) created" },
            new[] { "AdmOk", "{0}: сохранено", "{0}: saved" }, new[] { "AdmDeleted", "Удалено", "Deleted" },
            new[] { "MazeNew", "Лабиринт перестроен!", "The maze has changed!" },
            new[] { "LayMaze", "лабиринт", "maze" }, new[] { "LayRoam", "роум · большие стены", "roam · high walls" },
            new[] { "AdmLayout", "Раскладка арены: {0} (none | maze | roam). Размер лабиринта: /arenaadmin mazesize <id> 4–16", "Arena layout: {0} (none | maze | roam). Maze size: /arenaadmin mazesize <id> 4–16" },
            new[] { "AdmLoadouts", "Наборы: /arenaadmin loadouts", "Kits: /arenaadmin loadouts" },
            new[] { "AdminHelp",
                "<color=#f0a6b1>/arenaadmin</color>\n" +
                "create <id> <ffa|tdm|gungame|match> [режим Hub] — новая арена (центр = где стоишь)\n" +
                "name <id> <название> · loadout <id> <набор> · max <id> <n> · radius <id> <м> · center <id>\n" +
                "spawn <id> — спавн FFA/ган-гейм · spawna <id> / spawnb <id> — спавны команд (TDM, дуэли)\n" +
                "layout <id> none|maze|roam — лабиринт из ледяных стен / роум с большими стенами · mazesize <id> 4–16\n" +
                "clearspawns <id> · template <id> on|off · barricades <id> on|off · hub <id> <режим>\n" +
                "size <id> small|large — тесная арена или открытая местность (на large в FFA/TDM выдаются баррикады)\n" +
                "enable|disable|delete|show <id> · list · loadouts",
                "<color=#f0a6b1>/arenaadmin</color> create <id> <ffa|tdm|gungame|match> · name · loadout · max · radius · center\n" +
                "spawn · spawna · spawnb · clearspawns · template · barricades · size small|large · hub · enable · disable · delete · show · list · loadouts" },
            new[] { "SizeSmall", "тесная", "small" }, new[] { "SizeLarge", "большая · баррикады", "large · barricades" },
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
