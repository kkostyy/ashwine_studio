// =====================================================================================
//  Hub 1.0 — ядро хаб-сервера Rust (Oxide / Carbon)
// =====================================================================================
//  Табличка «Добро пожаловать» при каждом заходе, защищённое лобби (без урона и PvP),
//  выбор режима через меню, порталы и NPC, отдельный инвентарь в каждом режиме,
//  возврат /hub (/leave) через 5 с и только вне боя, монеты, профиль, онлайн по режимам.
//
//  Режимы описываются в конфиге. Каждый режим: точки спавна, свой инвентарь
//  (persist — сохраняется, kit — выдаётся набор, empty — пусто), группы прав, которые
//  игрок получает только пока он в режиме (так BuildServer/VideoRecorder работают
//  только в своих зонах), команды при входе.
//
//  Права:  hub.admin — разметка лобби, режимов, порталов, NPC; монеты игрокам
//  Чат:    /menu · /hub · /leave · /info · /profile [ник] · /coins
//  Админ:  /hubadmin — список команд разметки
//  Бинд:   bind p hub.menu
//
//  API для других плагинов (Ranked, Arenas, AimTrain…):
//      string GetMode(BasePlayer) · bool IsInLobby(BasePlayer) · bool IsInCombat(BasePlayer)
//      void TagCombat(BasePlayer) · bool SendToMode(BasePlayer, string mode, bool instant)
//      void SendToLobby(BasePlayer, bool instant) · int GetModeOnline(string mode)
//      List<BasePlayer> GetPlayersInMode(string mode)
//      int GetCoins(ulong) · void AddCoins(ulong, int, string reason) · bool TakeCoins(ulong, int)
//      bool AddPrize(ulong, int, string) — приз без дневного лимита (сезон, турнир)
//      bool CanEarnFrom(ulong killer, ulong victim) — антифарм: тот же IP, AFK-жертва, одна и та же жертва
//      bool SameIp(ulong a, ulong b) · bool IsAfk(ulong id, float seconds)
//      void AchAdd(ulong, string stat, int) · void AchMax(ulong, string stat, int) · List<string> GetAchTitles(ulong)
//      int GetPlayMinutes(ulong) — минуты на сервере (анти-смурф ранкеда)
//      float GetHudScale(ulong) — 0.65 / 0.8 / 1.0, игрок выбирает в профиле или /hud s|m|l
//  Хуки, которые вызывает Hub:
//      object CanHubTransfer(BasePlayer, string from, string to)  → string = причина отказа
//      void   OnHubModeChanged(BasePlayer, string from, string to)
//      object OnHubPlayerReady(BasePlayer)        → не null = не отправлять в лобби при заходе
//      object OnHubPlayerDisconnect(BasePlayer, string mode) → не null = не выводить из режима
//      string OnHubProfile(ulong userId, string lang) → строка в профиль
//      object OnHubRespawn(BasePlayer)            → BasePlayer.SpawnPoint = своя точка возрождения
//      object OnHubRespawned(BasePlayer, string mode) → не null = Hub не выдаёт набор и не переносит
// =====================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Hub", "Claude", "1.4.4")]
    [Description("Хаб-сервер: табличка, лобби, режимы, порталы, NPC, инвентарь на режим, монеты, профиль")]
    public class Hub : RustPlugin
    {
        [PluginReference] private Plugin GlowingEyes;
        #region Константы

        private const string PermAdmin = "hub.admin";
        private const string Lobby = "lobby";
        private const string PlayerPrefab = "assets/prefabs/player/player.prefab";

        private const string UiMain = "Hub.Main";
        private const string UiWelcome = "Hub.Welcome";
        private const string UiPopup = "Hub.Popup";
        private const string UiToast = "Hub.Toast";
        private const string UiBadge = "Hub.Badge";

        private const string ColBg = "0.07 0.065 0.065 0.97";
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

        private class ModeCfg
        {
            [JsonProperty("ID")] public string Id;
            [JsonProperty("Название")] public string Name;
            [JsonProperty("Название (EN)")] public string NameEn;
            [JsonProperty("Описание")] public string Desc;
            [JsonProperty("Описание (EN)")] public string DescEn;
            [JsonProperty("Включён")] public bool Enabled = true;
            [JsonProperty("Скоро (показывать, но не пускать)")] public bool ComingSoon;
            [JsonProperty("Право для входа (пусто = всем)")] public string Permission = "";
            [JsonProperty("Инвентарь: persist / kit / empty")] public string Inventory = "kit";
            [JsonProperty("Набор (для kit; для persist — при первом входе)")] public List<KitEntry> Kit = new List<KitEntry>();
            [JsonProperty("Лут с трупов (false = вещи пропадают при смерти)")] public bool DropLoot;
            [JsonProperty("PvP между игроками")] public bool AllowPvp = true;
            [JsonProperty("Группы прав на время режима")] public List<string> Groups = new List<string>();
            [JsonProperty("Права для групп (выдаются при создании группы)")] public List<string> GroupPerms = new List<string>();
            [JsonProperty("Консольные команды при входе ({id} {name})")] public List<string> EnterCommands = new List<string>();
            [JsonProperty("Чат-команда игрока после входа (без /)")] public string OpenChatCommand = "";
            [JsonProperty("Держать в зоне: радиус от центра (0 = нет)")] public float ZoneRadius;
        }

        private class Configuration
        {
            [JsonProperty("Название сервера")] public string ServerName = "ASH & WINE";
            [JsonProperty("Подзаголовок")] public string Tagline = "стройка · съёмки · ранкед · арены · аим-трен";
            [JsonProperty("Подзаголовок (EN)")] public string TaglineEn = "build · film · ranked · arenas · aim train";
            [JsonProperty("Логотип: ссылка на PNG (пусто = значок)")] public string LogoUrl = "";
            [JsonProperty("Discord")] public string Discord = "discord.gg/ashwine";
            [JsonProperty("Сайт")] public string Website = "ashwine.gg";
            [JsonProperty("Правила")] public List<string> Rules = new List<string>
            {
                "Без читов, макросов и багоюза", "Без тиминга в FFA и ранкеде", "Без оскорблений и рекламы",
                "Ливы из ранкеда — поражение", "Решение админа — окончательное"
            };
            [JsonProperty("Правила (EN)")] public List<string> RulesEn = new List<string>
            {
                "No cheats, macros or bug abuse", "No teaming in FFA and ranked", "No insults or advertising",
                "Leaving ranked counts as a loss", "Admin decisions are final"
            };
            [JsonProperty("Показывать табличку при каждом заходе")] public bool WelcomeEveryConnect = true;
            [JsonProperty("Режим при входе (игрок остаётся на месте; пусто = лобби)")] public string JoinMode = "film";
            [JsonProperty("Максимум игроков (для табло онлайна)")] public int MaxPlayers = 50;

            [JsonProperty("Возврат в лобби: задержка (сек)")] public int LeaveDelay = 5;
            [JsonProperty("«В бою» после урона (сек)")] public float CombatSeconds = 10f;
            [JsonProperty("Лобби: радиус зоны (м, 0 = не держать)")] public float LobbyRadius = 80f;
            [JsonProperty("Порталы: задержка между срабатываниями (сек)")] public float PortalCooldown = 4f;
            [JsonProperty("Подписи над порталами и NPC (ddraw)")] public bool Labels = true;
            [JsonProperty("Подписи: дальность (м)")] public float LabelDistance = 45f;
            [JsonProperty("NPC: дальность нажатия E (м)")] public float NpcUseDistance = 3.5f;

            [JsonProperty("Монеты в час онлайна")] public float CoinsPerHour = 10f;
            [JsonProperty("Писать в чат всем, когда кто-то получил достижение")] public bool AchBroadcast = true;
            [JsonProperty("Антифарм: монеты за онлайн только если двигался за последние N минут")] public float AfkMinutes = 3f;
            [JsonProperty("Антифарм: лимит заработка в день (0 = без лимита; призы сезона и турниров не считаются)")] public int DailyCap = 2000;
            [JsonProperty("Антифарм: сколько раз за 10 минут дают монеты за одного и того же игрока")] public int SameVictimLimit = 3;
            [JsonProperty("Антифарм: не давать монеты за игроков с того же IP")] public bool BlockSameIp = true;
            [JsonProperty("Антифарм: не давать монеты за AFK-жертву (секунд без движения)")] public float AfkVictimSeconds = 60f;
            [JsonProperty("Скруглённые углы интерфейса")] public bool RoundedCorners = true;

            [JsonProperty("Режимы")] public List<ModeCfg> Modes = DefaultModes();
        }

        private static List<KitEntry> Wear(params string[] items) => items.Select(i => new KitEntry { Item = i, Container = "wear" }).ToList();

        private static List<ModeCfg> DefaultModes()
        {
            var roadsign = Wear("metal.facemask", "roadsign.jacket", "roadsign.kilt", "hoodie", "pants", "shoes.boots", "tactical.gloves");
            var ffaKit = new List<KitEntry>(roadsign)
            {
                new KitEntry { Item = "rifle.ak", Container = "belt", Mods = new List<string> { "weapon.mod.holosight" } },
                new KitEntry { Item = "syringe.medical", Amount = 2, Container = "belt" },
                new KitEntry { Item = "bandage", Amount = 3, Container = "belt" },
                new KitEntry { Item = "ammo.rifle", Amount = 256 },
            };
            var aimKit = new List<KitEntry>
            {
                new KitEntry { Item = "rifle.ak", Container = "belt", Mods = new List<string> { "weapon.mod.holosight" } },
                new KitEntry { Item = "rifle.lr300", Container = "belt" }, new KitEntry { Item = "smg.mp5", Container = "belt" },
                new KitEntry { Item = "rifle.bolt", Container = "belt", Mods = new List<string> { "weapon.mod.small.scope" } },
                new KitEntry { Item = "ammo.rifle", Amount = 1000 }, new KitEntry { Item = "ammo.pistol", Amount = 1000 },
            };
            var primKit = new List<KitEntry>
            {
                new KitEntry { Item = "spear.wooden", Container = "belt" }, new KitEntry { Item = "bow.hunting", Container = "belt" },
                new KitEntry { Item = "crossbow", Container = "belt" }, new KitEntry { Item = "arrow.wooden", Amount = 64 },
                new KitEntry { Item = "bandage", Amount = 4, Container = "belt" },
                new KitEntry { Item = "burlap.shirt", Container = "wear" }, new KitEntry { Item = "burlap.trousers", Container = "wear" },
            };
            return new List<ModeCfg>
            {
                new ModeCfg { Id = "build", Name = "Стройка", NameEn = "Build", Desc = "креатив, полёт, рейд-тест", DescEn = "creative, fly, raid test",
                    Inventory = "persist", AllowPvp = false, Groups = new List<string> { "hub_build" },
                    GroupPerms = new List<string> { "buildserver.use", "buildserver.fly", "buildserver.god", "buildserver.items", "blueprints.use" },
                    OpenChatCommand = "build" },
                new ModeCfg { Id = "film", Name = "Съёмки", NameEn = "Filming", Desc = "постановки с ботами", DescEn = "staged scenes with bots",
                    Inventory = "persist", AllowPvp = true, Groups = new List<string> { "hub_film" },
                    GroupPerms = new List<string> { "videorecorder.use", "videorecorder.paths", "videorecorder.world", "videorecorder.kits", "buildserver.fly", "buildserver.items" },
                    OpenChatCommand = "vr" },
                new ModeCfg { Id = "ranked_ak", Name = "Ранкед: калаши", NameEn = "Ranked: AK", Desc = "АК · 1v1–5v5 · рейтинг", DescEn = "AK · 1v1–5v5 · rating",
                    Inventory = "empty", OpenChatCommand = "ranked" },
                new ModeCfg { Id = "ranked_mix", Name = "Ранкед: разные ганы", NameEn = "Ranked: mixed", Desc = "случайная пушка и сет", DescEn = "random gun and kit",
                    Inventory = "empty", OpenChatCommand = "ranked" },
                new ModeCfg { Id = "ranked_t2", Name = "Ранкед: тир 2", NameEn = "Ranked: Tier 2", Desc = "Томпсон, MP5, SAR · роадсайн", DescEn = "Thompson, MP5, SAR · roadsign",
                    Inventory = "empty", OpenChatCommand = "ranked" },
                new ModeCfg { Id = "ranked_prim", Name = "Ранкед: примитив", NameEn = "Ranked: Primitive", Desc = "луки, Eoka, револьвер", DescEn = "bows, Eoka, revolver",
                    Inventory = "empty", OpenChatCommand = "ranked" },
                new ModeCfg { Id = "ffa", Name = "FFA / TDM", NameEn = "FFA / TDM", Desc = "арена = пушка", DescEn = "arena = gun", Inventory = "empty", Kit = ffaKit, OpenChatCommand = "arena" },
                new ModeCfg { Id = "duels", Name = "Дуэли и свои арены", NameEn = "Duels & custom", Desc = "с друзьями, пароль", DescEn = "with friends, password",
                    Inventory = "empty", OpenChatCommand = "arena" },
                new ModeCfg { Id = "facecheck", Name = "Фейсчеки", NameEn = "Facechecks", Desc = "дверь и углы · игрок / бот", DescEn = "door & corners · player / bot",
                    Inventory = "empty", OpenChatCommand = "fc" },
                new ModeCfg { Id = "aim", Name = "Аим-трен", NameEn = "Aim train", Desc = "мишени, боты, зеркало", DescEn = "targets, bots, mirror",
                    Inventory = "kit", Kit = aimKit, AllowPvp = false, OpenChatCommand = "aim" },
                new ModeCfg { Id = "primitive", Name = "Примитив", NameEn = "Primitive", Desc = "луки, арбалет, Eoka, копья", DescEn = "bows, crossbow, Eoka, spears",
                    Inventory = "empty", Kit = primKit, OpenChatCommand = "arena" },
                new ModeCfg { Id = "maze", Name = "Лабиринт", NameEn = "Maze", Desc = "ледяные стены · 3 режима", DescEn = "ice walls · 3 modes", Inventory = "empty", OpenChatCommand = "arena" },
                new ModeCfg { Id = "walls", Name = "Роум со стенами", NameEn = "Wall roams", Desc = "пати на пати · стены", DescEn = "party vs party · walls", Inventory = "empty", OpenChatCommand = "arena" },
                new ModeCfg { Id = "bows", Name = "FFA луки", NameEn = "FFA bows", Desc = "лук и арбалет", DescEn = "bow & crossbow", Inventory = "empty", OpenChatCommand = "arena" },
                new ModeCfg { Id = "speargun", Name = "Гарпуны", NameEn = "Spearguns", Desc = "дуэли под водой", DescEn = "underwater duels", Inventory = "empty", OpenChatCommand = "arena" },
                new ModeCfg { Id = "helimm", Name = "Вертолётный ММ", NameEn = "Heli MM", Desc = "1v1 и 2v2 · атак-хели и мини", DescEn = "1v1 & 2v2 · attack heli & mini", Inventory = "empty", OpenChatCommand = "mg helimm" },
                new ModeCfg { Id = "heli", Name = "Хели-бои", NameEn = "Heli battles", Desc = "атак-хели · пилот + стрелок", DescEn = "attack heli · pilot + gunner", Inventory = "empty", OpenChatCommand = "mg heli" },
                new ModeCfg { Id = "roam", Name = "Роум", NameEn = "Roam", Desc = "открытая зона, транспорт", DescEn = "open zone, vehicles", Inventory = "empty", DropLoot = true, OpenChatCommand = "mg roam" },
                new ModeCfg { Id = "bedwars", Name = "Bedwars", NameEn = "Bedwars", Desc = "острова, кровати, генераторы", DescEn = "islands, beds, generators", Inventory = "empty", OpenChatCommand = "mg bedwars" },
                new ModeCfg { Id = "raidwars", Name = "Рейд против рейда", NameEn = "Raid wars", Desc = "строим и рейдим друг друга", DescEn = "build, then raid each other", Inventory = "empty", OpenChatCommand = "mg raidwars" },
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
                if (_config.Modes == null) _config.Modes = DefaultModes();
                // новые режимы из обновлений плагина добавляются в старый конфиг, свои настройки не трогаем
                foreach (var m in DefaultModes())
                    if (!_config.Modes.Any(x => x.Id == m.Id)) { _config.Modes.Add(m); PrintWarning($"В конфиг добавлен новый режим: {m.Id}"); }
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

        private class Vec
        {
            public float X, Y, Z, Yaw;
            [JsonIgnore] public Vector3 V => new Vector3(X, Y, Z);
            public static Vec From(Vector3 v, float yaw = 0f) => new Vec { X = v.x, Y = v.y, Z = v.z, Yaw = yaw };
        }

        private class Portal
        {
            public string Mode;
            public float X, Y, Z, Radius = 2f;
            [JsonIgnore] public Vector3 V => new Vector3(X, Y, Z);
        }

        private class NpcDef
        {
            public string Mode;
            public string Name;
            public float X, Y, Z, Yaw;
        }

        private class MapData
        {
            public List<Vec> LobbySpawns = new List<Vec>();
            public Dictionary<string, List<Vec>> ModeSpawns = new Dictionary<string, List<Vec>>();
            public Dictionary<string, Vec> ModeCenters = new Dictionary<string, Vec>();
            public List<Portal> Portals = new List<Portal>();
            public List<NpcDef> Npcs = new List<NpcDef>();
        }

        private class SItem
        {
            public string S;
            public int A = 1;
            public ulong K;
            public float C = -1f;
            public int Ct;
            public int P = -1;
            public int Am = -1;
            public string AmT;
            public List<string> M;
            public string T;
        }

        private class PData
        {
            public string Name;
            public string Mode = Lobby;
            public int Coins;
            public float CoinFrac;
            public string CapDay;
            public int CapEarned;
            public string FirstSeen;
            public string LastSeen;
            public Dictionary<string, int> Minutes = new Dictionary<string, int>();
            public Dictionary<string, List<SItem>> Inv = new Dictionary<string, List<SItem>>();
            public int HudSize = 1;
            public Dictionary<string, int> Stats = new Dictionary<string, int>();
            public HashSet<string> Achs = new HashSet<string>();
        }

        private MapData _map;
        private Dictionary<ulong, PData> _players;
        private bool _dirty;

        private void LoadData()
        {
            try { _map = Interface.Oxide.DataFileSystem.ReadObject<MapData>("Hub/Map"); } catch { _map = null; }
            if (_map == null) _map = new MapData();
            if (_map.LobbySpawns == null) _map.LobbySpawns = new List<Vec>();
            if (_map.ModeSpawns == null) _map.ModeSpawns = new Dictionary<string, List<Vec>>();
            if (_map.ModeCenters == null) _map.ModeCenters = new Dictionary<string, Vec>();
            if (_map.Portals == null) _map.Portals = new List<Portal>();
            if (_map.Npcs == null) _map.Npcs = new List<NpcDef>();

            try { _players = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PData>>("Hub/Players"); } catch { _players = null; }
            if (_players == null) _players = new Dictionary<ulong, PData>();
        }

        private void SaveMap() => Interface.Oxide.DataFileSystem.WriteObject("Hub/Map", _map);

        private void SavePlayers()
        {
            Interface.Oxide.DataFileSystem.WriteObject("Hub/Players", _players);
            _dirty = false;
        }

        private PData D(ulong id)
        {
            PData d;
            if (!_players.TryGetValue(id, out d)) { _players[id] = d = new PData { FirstSeen = Now() }; _dirty = true; }
            if (d.Minutes == null) d.Minutes = new Dictionary<string, int>();
            if (d.Inv == null) d.Inv = new Dictionary<string, List<SItem>>();
            if (string.IsNullOrEmpty(d.Mode)) d.Mode = Lobby;
            return d;
        }

        private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");

        #endregion

        #region Состояние

        private readonly Dictionary<ulong, float> _combat = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, Timer> _transfer = new Dictionary<ulong, Timer>();
        private readonly Dictionary<ulong, float> _portalCd = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, string> _npcMode = new Dictionary<ulong, string>();
        private readonly List<BasePlayer> _npcs = new List<BasePlayer>();
        private readonly HashSet<ulong> _ready = new HashSet<ulong>();
        private Timer _tickFast, _tickSlow, _tickMinute, _tickLabels;

        private static ulong Id(BasePlayer p) => (ulong)p.userID;

        // боты (VideoRecorder, AimTrain, NPC) — не игроки Hub
        private static bool IsBot(BasePlayer p) => p == null || (ulong)p.userID < 76561197960265728UL;

        private bool IsAdmin(BasePlayer p) => p != null && (p.IsAdmin || permission.UserHasPermission(p.UserIDString, PermAdmin));

        private ModeCfg Mode(string id) => _config.Modes.FirstOrDefault(m => m.Id == id);

        private string ModeOf(BasePlayer p) => p == null ? Lobby : D(Id(p)).Mode;

        private bool IsEn(ulong id) => lang.GetLanguage(id.ToString()) != "ru";

        private string ModeName(ModeCfg m, ulong id) => m == null ? T(id, "Lobby") : (IsEn(id) && !string.IsNullOrEmpty(m.NameEn) ? m.NameEn : m.Name);

        private string ModeDesc(ModeCfg m, ulong id) => m == null ? "" : (IsEn(id) && !string.IsNullOrEmpty(m.DescEn) ? m.DescEn : m.Desc);

        private string NameOf(string mode, ulong id) => mode == Lobby ? T(id, "Lobby") : ModeName(Mode(mode), id);

        private IEnumerable<BasePlayer> Real => BasePlayer.activePlayerList.Where(p => p != null && !IsBot(p));

        #endregion

        #region Хуки сервера

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            RegisterLang();
            InitRoundedSprite();
            InitMenuIcons();
            EnsureGroups();
            SpawnNpcs();

            foreach (var p in Real.ToList())
            {
                var d = D(Id(p));
                d.Name = p.displayName;
                _ready.Add(Id(p));
                if (d.Mode == Lobby) ShowBadge(p);
            }

            _tickFast = timer.Every(0.5f, TickFast);
            _tickSlow = timer.Every(1f, TickSlow);
            _tickMinute = timer.Every(60f, TickMinute);
            if (_config.Labels) _tickLabels = timer.Every(2f, TickLabels);
        }

        private void Unload()
        {
            foreach (var t in _transfer.Values) t?.Destroy();
            DespawnNpcs();
            foreach (var p in BasePlayer.activePlayerList)
                foreach (var ui in new[] { UiMain, UiWelcome, UiPopup, UiToast, UiBadge }) CuiHelper.DestroyUi(p, ui);
            SavePlayers();
        }

        private void OnServerSave() { if (_dirty) SavePlayers(); }

        private void EnsureGroups()
        {
            foreach (var m in _config.Modes)
                foreach (var g in m.Groups)
                {
                    if (string.IsNullOrEmpty(g) || permission.GroupExists(g)) continue;
                    permission.CreateGroup(g, "Hub: " + m.Name, 0);
                    foreach (var perm in m.GroupPerms) permission.GrantGroupPermission(g, perm, null);
                    Puts($"Создана группа {g} для режима {m.Id} ({m.GroupPerms.Count} прав)");
                }
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || _npcMode.ContainsKey(Id(player))) return;
            var d = D(Id(player));
            d.Name = player.displayName;
            d.LastSeen = Now();
            _dirty = true;
            _ready.Remove(Id(player));
            WaitReady(player, 0);
        }

        private void WaitReady(BasePlayer player, int tries)
        {
            timer.Once(1f, () =>
            {
                if (player == null || !player.IsConnected) return;
                if ((player.IsReceivingSnapshot || player.IsSleeping()) && tries < 30) { WaitReady(player, tries + 1); return; }
                _ready.Add(Id(player));
                if (Interface.CallHook("OnHubPlayerReady", player) != null) return;
                if (!player.IsDead()) { if (Mode(_config.JoinMode) != null) EnterInPlace(player, _config.JoinMode); else SendToLobbyNow(player, true); }
                if (_config.WelcomeEveryConnect) ShowWelcome(player);
            });
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null || _npcMode.ContainsKey(Id(player))) return;
            var id = Id(player);
            CancelTransfer(id);
            _ready.Remove(id);
            var d = D(id);
            d.LastSeen = Now();
            if (d.Mode == Lobby) return;
            if (Interface.CallHook("OnHubPlayerDisconnect", player, d.Mode) != null) return;
            // спящий игрок не должен лежать в режиме: сохраняем вещи и уносим в лобби
            SendToLobbyNow(player, false);
        }

        #endregion

        #region Режимы и переходы

        private bool CanEnter(BasePlayer player, ModeCfg m, out string reason)
        {
            var id = Id(player);
            reason = null;
            if (m == null || !m.Enabled) { reason = T(id, "NoMode"); return false; }
            if (m.ComingSoon) { reason = T(id, "Soon", ModeName(m, id)); return false; }
            if (!string.IsNullOrEmpty(m.Permission) && !permission.UserHasPermission(player.UserIDString, m.Permission) && !IsAdmin(player))
            { reason = T(id, "NoAccess", ModeName(m, id)); return false; }
            if (SpawnsOf(m.Id).Count == 0) { reason = T(id, "NoSpawn", ModeName(m, id)); return false; }
            return true;
        }

        private List<Vec> SpawnsOf(string mode)
        {
            if (mode == Lobby) return _map.LobbySpawns;
            List<Vec> l;
            return _map.ModeSpawns.TryGetValue(mode, out l) ? l : new List<Vec>();
        }

        private Vec RandomSpawn(string mode)
        {
            var list = SpawnsOf(mode);
            if (list.Count == 0) return null;
            return list[UnityEngine.Random.Range(0, list.Count)];
        }

        private void StartTransfer(BasePlayer player, string target)
        {
            var id = Id(player);
            var from = ModeOf(player);
            if (from == target)
            {
                if (target == Lobby) Toast(player, T(id, "AlreadyLobby"), false);
                else RunOpenCommand(player, Mode(target));
                return;
            }

            if (target != Lobby)
            {
                string reason;
                if (!CanEnter(player, Mode(target), out reason)) { Toast(player, reason, true); return; }
            }
            if (target == Lobby && SpawnsOf(Lobby).Count == 0) { Toast(player, T(id, "NoLobby"), true); return; }

            var deny = Interface.CallHook("CanHubTransfer", player, from, target) as string;
            if (!string.IsNullOrEmpty(deny)) { Toast(player, deny, true); return; }

            if (from == Lobby || IsAdmin(player) && _config.LeaveDelay <= 0) { DoTransfer(player, target, true); return; }

            float left = CombatLeft(id);
            if (left > 0f) { Toast(player, T(id, "InCombat", Mathf.CeilToInt(left)), true); return; }

            CancelTransfer(id);
            int sec = Mathf.Max(0, _config.LeaveDelay);
            if (sec == 0) { DoTransfer(player, target, true); return; }
            int n = sec;
            Toast(player, T(id, "Leaving", NameOf(target, id), n), false);
            _transfer[id] = timer.Repeat(1f, sec, () =>
            {
                if (player == null || !player.IsConnected) { CancelTransfer(id); return; }
                n--;
                if (n > 0) { Toast(player, T(id, "Leaving", NameOf(target, id), n), false); return; }
                _transfer.Remove(id);
                DoTransfer(player, target, true);
            });
        }

        private void CancelTransfer(ulong id)
        {
            Timer t;
            if (_transfer.TryGetValue(id, out t)) { t?.Destroy(); _transfer.Remove(id); }
        }

        // Вход сразу в режим (сейчас — съёмки) без лобби и телепорта; хаб открывается по P (bind p hub.menu)
        private void EnterInPlace(BasePlayer player, string mode)
        {
            var d = D(Id(player));
            if (d.Mode == mode) { foreach (var g in Mode(mode).Groups) permission.AddUserGroup(player.UserIDString, g); CuiHelper.DestroyUi(player, UiBadge); return; }
            DoTransfer(player, mode, true, true);
        }

        private void DoTransfer(BasePlayer player, string target, bool connected, bool stay = false)
        {
            var id = Id(player);
            var d = D(id);
            var from = d.Mode;
            var oldCfg = Mode(from);
            var newCfg = target == Lobby ? null : Mode(target);

            var spawn = RandomSpawn(target);
            if (spawn == null && connected && !stay) { Toast(player, target == Lobby ? T(id, "NoLobby") : T(id, "NoSpawn", NameOf(target, id)), true); return; }

            // 1. вещи старого режима
            if (oldCfg != null && oldCfg.Inventory == "persist") d.Inv[from] = Snapshot(player);
            // светящиеся глаза (плагин GlowingEyes) — не вещь режима, переносим с игроком
            bool eyes = player.inventory.containerWear?.itemList?.Any(x => x?.info?.shortname == "gloweyes") == true;
            player.inventory.Strip();

            // 2. группы прав
            if (oldCfg != null) foreach (var g in oldCfg.Groups) permission.RemoveUserGroup(player.UserIDString, g);
            if (newCfg != null) foreach (var g in newCfg.Groups) permission.AddUserGroup(player.UserIDString, g);

            d.Mode = target;
            _dirty = true;

            // 3. перенос
            if (spawn != null && !stay) MoveTo(player, spawn);
            if (!connected) return;

            Heal(player);

            // 4. вещи нового режима
            if (newCfg != null)
            {
                List<SItem> saved;
                if (newCfg.Inventory == "persist" && d.Inv.TryGetValue(target, out saved) && saved.Count > 0) Restore(player, saved);
                else if (newCfg.Inventory != "empty") GiveKit(player, newCfg);
                if (eyes) timer.Once(0.3f, () => { if (player != null && player.IsConnected && GlowingEyes != null) GlowingEyes.Call("Enable", player); });
                foreach (var cmd in newCfg.EnterCommands)
                    ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), cmd.Replace("{id}", player.UserIDString).Replace("{name}", player.displayName));
            }

            CloseAll(player);
            if (target == Lobby) ShowBadge(player); else CuiHelper.DestroyUi(player, UiBadge);
            if (!stay) Toast(player, T(id, "Entered", NameOf(target, id)), false);
            Interface.CallHook("OnHubModeChanged", player, from, target);
            if (newCfg != null && !stay) timer.Once(1.2f, () => RunOpenCommand(player, newCfg));
        }

        private void SendToLobbyNow(BasePlayer player, bool connected)
        {
            if (player == null) return;
            CancelTransfer(Id(player));
            if (SpawnsOf(Lobby).Count == 0)
            {
                // лобби ещё не размечено — просто чистим режим
                var d = D(Id(player));
                var cfg = Mode(d.Mode);
                if (cfg != null)
                {
                    if (cfg.Inventory == "persist") d.Inv[d.Mode] = Snapshot(player);
                    player.inventory.Strip();
                    foreach (var g in cfg.Groups) permission.RemoveUserGroup(player.UserIDString, g);
                }
                d.Mode = Lobby;
                if (connected) ShowBadge(player);
                return;
            }
            DoTransfer(player, Lobby, connected);
        }

        private void RunOpenCommand(BasePlayer player, ModeCfg m)
        {
            if (player == null || !player.IsConnected || m == null || string.IsNullOrEmpty(m.OpenChatCommand)) return;
            if (ModeOf(player) != m.Id) return;
            player.SendConsoleCommand("chat.say", "/" + m.OpenChatCommand);
        }

        private void MoveTo(BasePlayer player, Vec v)
        {
            try { if (player.isMounted) player.EnsureDismounted(); } catch { }
            player.Teleport(v.V + Vector3.up * 0.15f);
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
                    m.radiation_poison.value = 0f;
                    m.bleeding.value = 0f;
                    m.SendChanges();
                }
                player.SendNetworkUpdate();
            }
            catch { }
        }

        #endregion

        #region Инвентарь

        private static List<SItem> Snapshot(BasePlayer player)
        {
            var list = new List<SItem>();
            var inv = player.inventory;
            if (inv == null) return list;
            AddContainer(list, inv.containerMain, 0);
            AddContainer(list, inv.containerBelt, 1);
            AddContainer(list, inv.containerWear, 2);
            return list;
        }

        private static void AddContainer(List<SItem> list, ItemContainer c, int ct)
        {
            if (c?.itemList == null) return;
            foreach (var item in c.itemList)
            {
                if (item?.info == null || item.info.shortname == "gloweyes") continue;
                var s = new SItem { S = item.info.shortname, A = item.amount, K = item.skin, Ct = ct, P = item.position, T = item.text };
                if (item.hasCondition) s.C = item.condition;
                var proj = item.GetHeldEntity() as BaseProjectile;
                if (proj?.primaryMagazine != null)
                {
                    s.Am = proj.primaryMagazine.contents;
                    s.AmT = proj.primaryMagazine.ammoType?.shortname;
                }
                if (item.contents?.itemList != null && item.contents.itemList.Count > 0)
                    s.M = item.contents.itemList.Where(x => x?.info != null).Select(x => x.info.shortname).ToList();
                list.Add(s);
            }
        }

        private static Item Build(string shortname, int amount, ulong skin, List<string> mods)
        {
            var item = ItemManager.CreateByName(shortname, Mathf.Max(1, amount), skin);
            if (item == null) return null;
            if (mods != null && item.contents != null)
                foreach (var m in mods)
                {
                    var mod = ItemManager.CreateByName(m, 1, 0UL);
                    if (mod != null && !mod.MoveToContainer(item.contents)) mod.Remove();
                }
            return item;
        }

        private static void Restore(BasePlayer player, List<SItem> items)
        {
            foreach (var s in items)
            {
                var item = Build(s.S, s.A, s.K, s.M);
                if (item == null) continue;
                if (s.C >= 0f && item.hasCondition) item.condition = s.C;
                if (!string.IsNullOrEmpty(s.T)) item.text = s.T;
                var proj = item.GetHeldEntity() as BaseProjectile;
                if (proj?.primaryMagazine != null && s.Am >= 0)
                {
                    proj.primaryMagazine.contents = s.Am;
                    var ammo = string.IsNullOrEmpty(s.AmT) ? null : ItemManager.FindItemDefinition(s.AmT);
                    if (ammo != null) proj.primaryMagazine.ammoType = ammo;
                }
                var c = s.Ct == 2 ? player.inventory.containerWear : s.Ct == 1 ? player.inventory.containerBelt : player.inventory.containerMain;
                if (!item.MoveToContainer(c, s.P) && !item.MoveToContainer(c) && !item.MoveToContainer(player.inventory.containerMain)) item.Remove();
            }
        }

        private static void GiveKit(BasePlayer player, ModeCfg m)
        {
            foreach (var k in m.Kit)
            {
                var item = Build(k.Item, k.Amount, k.Skin, k.Mods);
                if (item == null) continue;
                var proj = item.GetHeldEntity() as BaseProjectile;
                if (proj?.primaryMagazine != null) proj.primaryMagazine.contents = proj.primaryMagazine.capacity;
                var c = k.Container == "wear" ? player.inventory.containerWear : k.Container == "belt" ? player.inventory.containerBelt : player.inventory.containerMain;
                if (!item.MoveToContainer(c) && !item.MoveToContainer(player.inventory.containerMain)) item.Remove();
            }
        }

        #endregion

        #region Смерть, урон, защита лобби

        private object OnPlayerRespawn(BasePlayer player, BasePlayer.SpawnPoint spawnPoint)
        {
            if (player == null || IsBot(player)) return null;
            var custom = Interface.CallHook("OnHubRespawn", player);
            if (custom is BasePlayer.SpawnPoint) return custom;
            var v = RandomSpawn(ModeOf(player));
            if (v == null) return null;
            return new BasePlayer.SpawnPoint { pos = v.V + Vector3.up * 0.15f, rot = Quaternion.Euler(0f, v.Yaw, 0f) };
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null || IsBot(player)) return;
            var mode = ModeOf(player);
            if (Interface.CallHook("OnHubRespawned", player, mode) != null) return;
            var v = RandomSpawn(mode);
            if (v != null && Vector3.Distance(player.transform.position, v.V) > 8f) MoveTo(player, v);
            var cfg = Mode(mode);
            if (cfg == null) { ShowBadge(player); return; }
            if (cfg.Inventory == "kit" || cfg.Inventory == "persist" && player.inventory.containerMain.itemList.Count == 0 && player.inventory.containerBelt.itemList.Count == 0)
                timer.Once(0.2f, () => { if (player != null && player.IsConnected) GiveKit(player, cfg); });
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player == null || IsBot(player)) return;
            var cfg = Mode(ModeOf(player));
            if (cfg != null && !cfg.DropLoot && cfg.Inventory != "persist") player.inventory.Strip();
            CancelTransfer(Id(player));
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var victim = entity as BasePlayer;
            if (victim == null) return null;
            var vid = Id(victim);
            if (_npcMode.ContainsKey(vid)) return true;
            if (IsBot(victim)) return null;

            var vmode = ModeOf(victim);
            if (vmode == Lobby) return true;

            var attacker = info?.InitiatorPlayer;
            if (attacker != null && attacker != victim && !_npcMode.ContainsKey(Id(attacker)) && (ulong)attacker.userID > 76561197960265728UL)
            {
                var amode = ModeOf(attacker);
                if (amode != vmode) return true;                     // урон между режимами запрещён
                var cfg = Mode(vmode);
                if (cfg != null && !cfg.AllowPvp) return true;
                TagCombat(attacker);
            }

            TagCombat(victim);
            if (_transfer.ContainsKey(vid))
            {
                CancelTransfer(vid);
                Toast(victim, T(vid, "LeaveCancelled"), true);
            }
            return null;
        }

        private object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            var player = planner?.GetOwnerPlayer();
            if (player == null || IsAdmin(player)) return null;
            return ModeOf(player) == Lobby ? (object)false : null;
        }

        private object CanLootPlayer(BasePlayer target, BasePlayer looter)
        {
            return target != null && _npcMode.ContainsKey(Id(target)) ? (object)false : null;
        }

        private object OnItemAction(Item item, string action, BasePlayer player)
        {
            if (player == null || action != "drop" || IsAdmin(player)) return null;
            return ModeOf(player) == Lobby ? (object)true : null;
        }

        private float CombatLeft(ulong id)
        {
            float t;
            if (!_combat.TryGetValue(id, out t)) return 0f;
            return Mathf.Max(0f, t + _config.CombatSeconds - Time.realtimeSinceStartup);
        }

        #endregion

        #region NPC и порталы

        private void SpawnNpcs()
        {
            DespawnNpcs();
            for (int i = 0; i < _map.Npcs.Count; i++)
            {
                var n = _map.Npcs[i];
                var npc = GameManager.server.CreateEntity(PlayerPrefab, new Vector3(n.X, n.Y, n.Z), Quaternion.Euler(0f, n.Yaw, 0f)) as BasePlayer;
                if (npc == null) continue;
                ulong fakeId = 7700000UL + (ulong)i;
                npc.userID = fakeId;
                npc.UserIDString = fakeId.ToString();
                npc.displayName = string.IsNullOrEmpty(n.Name) ? (Mode(n.Mode)?.Name ?? n.Mode.Replace("cmd:", "")) : n.Name;
                npc.enableSaving = false;
                npc.Spawn();
                if (npc.IsSleeping()) npc.EndSleeping();
                npc.InitializeHealth(100f, 100f);
                npc.inventory.Strip();
                foreach (var w in new[] { "hoodie", "pants", "shoes.boots" })
                {
                    var it = ItemManager.CreateByName(w, 1, 0UL);
                    if (it != null && !it.MoveToContainer(npc.inventory.containerWear)) it.Remove();
                }
                npc.SendNetworkUpdateImmediate();
                _npcs.Add(npc);
                _npcMode[fakeId] = n.Mode;
            }
        }

        private void DespawnNpcs()
        {
            foreach (var n in _npcs) if (n != null && !n.IsDestroyed) n.Kill();
            _npcs.Clear();
            _npcMode.Clear();
        }

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null || !input.WasJustPressed(BUTTON.USE) || _npcs.Count == 0) return;
            if (ModeOf(player) != Lobby) return;
            var eye = player.eyes.position;
            var fwd = player.eyes.HeadForward();
            BasePlayer best = null;
            float bestDot = 0.85f;
            foreach (var npc in _npcs)
            {
                if (npc == null || npc.IsDestroyed) continue;
                var to = npc.transform.position + Vector3.up * 1.2f - eye;
                if (to.magnitude > _config.NpcUseDistance) continue;
                var dot = Vector3.Dot(fwd, to.normalized);
                if (dot > bestDot) { bestDot = dot; best = npc; }
            }
            if (best == null) return;
            string mode;
            if (!_npcMode.TryGetValue(Id(best), out mode)) return;
            if (mode.StartsWith("cmd:")) { player.SendConsoleCommand("chat.say", "/" + mode.Substring(4)); return; }   // NPC-магазин и т.п.
            ShowModePopup(player, mode);
        }

        private void TickFast()
        {
            if (_map.Portals.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            foreach (var p in Real)
            {
                if (!p.IsConnected || p.IsDead() || !_ready.Contains(Id(p)) || ModeOf(p) != Lobby) continue;
                float cd;
                if (_portalCd.TryGetValue(Id(p), out cd) && now < cd) continue;
                var pos = p.transform.position;
                foreach (var portal in _map.Portals)
                {
                    if (Vector3.Distance(pos, portal.V) > portal.Radius) continue;
                    _portalCd[Id(p)] = now + _config.PortalCooldown;
                    StartTransfer(p, portal.Mode);
                    break;
                }
            }
        }

        private void TickSlow()
        {
            Vec lobbyCenter = _map.LobbySpawns.Count > 0 ? _map.LobbySpawns[0] : null;
            foreach (var p in Real)
            {
                if (!p.IsConnected || p.IsDead() || !_ready.Contains(Id(p))) continue;
                TrackActivity(p);
                var mode = ModeOf(p);
                if (mode == Lobby)
                {
                    if (p.metabolism != null && (p.metabolism.calories.value < 200f || p.metabolism.hydration.value < 100f || p.health < p.MaxHealth())) Heal(p);
                    if (_config.LobbyRadius > 0f && lobbyCenter != null && !IsAdmin(p) && Vector3.Distance(p.transform.position, lobbyCenter.V) > _config.LobbyRadius)
                        MoveTo(p, RandomSpawn(Lobby));
                    continue;
                }
                var cfg = Mode(mode);
                Vec c;
                if (cfg != null && cfg.ZoneRadius > 0f && _map.ModeCenters.TryGetValue(mode, out c) && !IsAdmin(p)
                    && Vector3.Distance(p.transform.position, c.V) > cfg.ZoneRadius)
                {
                    var s = RandomSpawn(mode);
                    if (s != null) { MoveTo(p, s); Toast(p, T(Id(p), "ZoneBack"), true); }
                }
            }
        }

        private void TickMinute()
        {
            foreach (var p in Real)
            {
                if (!p.IsConnected) continue;
                var d = D(Id(p));
                int m;
                d.Minutes.TryGetValue(d.Mode, out m);
                d.Minutes[d.Mode] = m + 1;
                AchAdd(Id(p), "play_minutes", 1);
                AchMax(Id(p), "modes_played", d.Minutes.Count(kv => kv.Value > 0 && kv.Key != Lobby));
                if (_config.AfkMinutes > 0f && IsAfk(Id(p), _config.AfkMinutes * 60f)) { _dirty = true; continue; }
                d.CoinFrac += _config.CoinsPerHour / 60f;
                if (d.CoinFrac >= 1f)
                {
                    int add = Mathf.Min(Mathf.FloorToInt(d.CoinFrac), CapLeft(d));
                    d.CoinFrac -= Mathf.FloorToInt(d.CoinFrac);
                    d.Coins += add;
                    d.CapEarned += add;
                    if (d.Mode == Lobby) ShowBadge(p);
                }
                _dirty = true;
            }
            if (_dirty) SavePlayers();
        }

        private void TickLabels()
        {
            if (_map.Portals.Count == 0 && _npcs.Count == 0) return;
            foreach (var p in Real)
            {
                if (!p.IsConnected || ModeOf(p) != Lobby || !_ready.Contains(Id(p))) continue;
                var id = Id(p);
                var pos = p.transform.position;
                var lines = new List<KeyValuePair<Vector3, string>>();
                foreach (var portal in _map.Portals)
                    if (Vector3.Distance(pos, portal.V) < _config.LabelDistance)
                        lines.Add(new KeyValuePair<Vector3, string>(portal.V + Vector3.up * (portal.Radius + 2.2f), Label(portal.Mode, id)));
                foreach (var npc in _npcs)
                {
                    if (npc == null || npc.IsDestroyed) continue;
                    string mode;
                    if (!_npcMode.TryGetValue(Id(npc), out mode)) continue;
                    if (Vector3.Distance(pos, npc.transform.position) < _config.LabelDistance)
                        lines.Add(new KeyValuePair<Vector3, string>(npc.transform.position + Vector3.up * 2.3f, (mode.StartsWith("cmd:") ? $"<size=20><color=#f0a6b1>{npc.displayName}</color></size>" : Label(mode, id)) + "\n<size=12><color=#a39896>[E]</color></size>"));
                }
                if (lines.Count == 0) continue;
                WithAdmin(p, () => { foreach (var l in lines) p.SendConsoleCommand("ddraw.text", 2.1f, Color.white, l.Key, l.Value); });
            }
        }

        private string Label(string mode, ulong id)
        {
            var m = Mode(mode);
            int online = GetModeOnline(mode);
            string sub = m != null && m.ComingSoon ? T(id, "SoonShort") : T(id, "InGame", online);
            return $"<size=20><color=#f0a6b1>{NameOf(mode, id)}</color></size>\n<size=13>{sub}</size>";
        }

        private static void WithAdmin(BasePlayer player, Action action)
        {
            bool was = player.IsAdmin;
            if (!was) { player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true); player.SendNetworkUpdateImmediate(); }
            try { action(); }
            finally { if (!was) { player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false); player.SendNetworkUpdateImmediate(); } }
        }

        #endregion

        #region API

        [HookMethod("GetMode")] public string GetMode(BasePlayer player) => ModeOf(player);

        [HookMethod("IsInLobby")] public bool IsInLobby(BasePlayer player) => ModeOf(player) == Lobby;

        [HookMethod("IsInCombat")] public bool IsInCombat(BasePlayer player) => player != null && CombatLeft(Id(player)) > 0f;

        [HookMethod("TagCombat")] public void TagCombat(BasePlayer player) { if (player != null) _combat[Id(player)] = Time.realtimeSinceStartup; }

        [HookMethod("SendToMode")]
        public bool SendToMode(BasePlayer player, string mode, bool instant)
        {
            if (player == null) return false;
            if (instant)
            {
                string reason;
                if (mode != Lobby && !CanEnter(player, Mode(mode), out reason)) return false;
                DoTransfer(player, mode, true);
                return ModeOf(player) == mode;
            }
            StartTransfer(player, mode);
            return true;
        }

        [HookMethod("SendToLobby")]
        public void SendToLobby(BasePlayer player, bool instant)
        {
            if (player == null) return;
            if (instant) SendToLobbyNow(player, player.IsConnected); else StartTransfer(player, Lobby);
        }

        [HookMethod("GetPlayMinutes")] public int GetPlayMinutes(ulong id) => D(id).Minutes.Values.Sum();

        [HookMethod("GetModeOnline")] public int GetModeOnline(string mode) => Real.Count(p => p.IsConnected && ModeOf(p) == mode);

        [HookMethod("GetPlayersInMode")] public List<BasePlayer> GetPlayersInMode(string mode) => Real.Where(p => p.IsConnected && ModeOf(p) == mode).ToList();

        [HookMethod("GetCoins")] public int GetCoins(ulong id) => D(id).Coins;

        [HookMethod("AddCoins")]
        public void AddCoins(ulong id, int amount, string reason) => GiveCoins(id, amount, reason, reason != "admin");

        [HookMethod("AddPrize")]
        public bool AddPrize(ulong id, int amount, string reason) { GiveCoins(id, amount, reason, false); return true; }

        // сколько ещё можно заработать сегодня с учётом лимита
        private int CapLeft(PData d)
        {
            if (_config.DailyCap <= 0) return int.MaxValue;
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (d.CapDay != today) { d.CapDay = today; d.CapEarned = 0; }
            return Mathf.Max(0, _config.DailyCap - d.CapEarned);
        }

        private void GiveCoins(ulong id, int amount, string reason, bool capped)
        {
            var d = D(id);
            if (capped && amount > 0)
            {
                int left = CapLeft(d);
                if (left <= 0)
                {
                    var pl = BasePlayer.FindByID(id);
                    if (pl != null && pl.IsConnected && !_capWarned.Contains(id)) { _capWarned.Add(id); Toast(pl, T(id, "CapReached", _config.DailyCap), true); }
                    return;
                }
                amount = Mathf.Min(amount, left);
                d.CapEarned += amount;
            }
            d.Coins = Mathf.Max(0, d.Coins + amount);
            _dirty = true;
            var p = BasePlayer.FindByID(id);
            if (p != null && p.IsConnected && amount != 0)
            {
                Toast(p, T(id, amount > 0 ? "CoinsPlus" : "CoinsMinus", Mathf.Abs(amount), reason ?? ""), false);
                if (ModeOf(p) == Lobby) ShowBadge(p);
            }
        }

        private readonly HashSet<ulong> _capWarned = new HashSet<ulong>();
        private readonly Dictionary<ulong, float> _active = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, KeyValuePair<Vector3, float>> _lastPose = new Dictionary<ulong, KeyValuePair<Vector3, float>>();
        private readonly Dictionary<string, List<float>> _victimLog = new Dictionary<string, List<float>>();

        private void TrackActivity(BasePlayer p)
        {
            var id = Id(p);
            var pos = p.transform.position;
            float yaw = p.eyes != null ? p.eyes.rotation.eulerAngles.y : 0f;
            KeyValuePair<Vector3, float> last;
            if (!_lastPose.TryGetValue(id, out last) || (last.Key - pos).sqrMagnitude > 0.25f || Mathf.Abs(Mathf.DeltaAngle(last.Value, yaw)) > 8f)
                _active[id] = Time.realtimeSinceStartup;
            _lastPose[id] = new KeyValuePair<Vector3, float>(pos, yaw);
        }

        [HookMethod("IsAfk")]
        public bool IsAfk(ulong id, float seconds)
        {
            float t;
            return !_active.TryGetValue(id, out t) || Time.realtimeSinceStartup - t > seconds;
        }

        private static string Ip(BasePlayer p)
        {
            var a = p?.net?.connection?.ipaddress;
            if (string.IsNullOrEmpty(a)) return null;
            int i = a.LastIndexOf(':');
            return i > 0 ? a.Substring(0, i) : a;
        }

        [HookMethod("SameIp")]
        public bool SameIp(ulong a, ulong b)
        {
            var ia = Ip(BasePlayer.FindByID(a));
            return ia != null && ia == Ip(BasePlayer.FindByID(b));
        }

        [HookMethod("CanEarnFrom")]
        public bool CanEarnFrom(ulong killer, ulong victim)
        {
            if (killer == victim) return false;
            if (_config.BlockSameIp && SameIp(killer, victim)) return false;
            if (_config.AfkVictimSeconds > 0f && IsAfk(victim, _config.AfkVictimSeconds)) return false;
            if (_config.SameVictimLimit > 0)
            {
                var key = killer + ">" + victim;
                List<float> log;
                if (!_victimLog.TryGetValue(key, out log)) _victimLog[key] = log = new List<float>();
                float now = Time.realtimeSinceStartup;
                log.RemoveAll(t => now - t > 600f);
                if (log.Count >= _config.SameVictimLimit) return false;
                log.Add(now);
            }
            return true;
        }

        [HookMethod("TakeCoins")]
        public bool TakeCoins(ulong id, int amount)
        {
            var d = D(id);
            if (amount < 0 || d.Coins < amount) return false;
            d.Coins -= amount;
            _dirty = true;
            return true;
        }

        #endregion

        #region Команды

        [ChatCommand("menu")] private void CmdMenu(BasePlayer player, string c, string[] a) => OpenMenu(player);

        [ChatCommand("hub")] private void CmdHub(BasePlayer player, string c, string[] a) => StartTransfer(player, Lobby);

        [ChatCommand("leave")] private void CmdLeave(BasePlayer player, string c, string[] a) => StartTransfer(player, Lobby);

        [ChatCommand("info")] private void CmdInfo(BasePlayer player, string c, string[] a) => ShowWelcome(player);

        [ChatCommand("coins")] private void CmdCoins(BasePlayer player, string c, string[] a) => Reply(player, T(Id(player), "CoinsBalance", D(Id(player)).Coins));

        [ChatCommand("profile")]
        private void CmdProfile(BasePlayer player, string c, string[] a)
        {
            ulong target = Id(player);
            if (a.Length > 0)
            {
                var q = string.Join(" ", a);
                var found = _players.FirstOrDefault(kv => kv.Value.Name != null && kv.Value.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                if (found.Value == null) { Reply(player, T(Id(player), "NotFound", q)); return; }
                target = found.Key;
            }
            ShowProfile(player, target);
        }

        [ConsoleCommand("hub.menu")]
        private void CcMenu(ConsoleSystem.Arg arg) { var p = arg.Player(); if (p == null) return; if (_menuShown.Contains(Id(p))) CloseAll(p); else OpenMenu(p); }   // P — открыть/закрыть хаб
        private readonly HashSet<ulong> _menuShown = new HashSet<ulong>();

        [ConsoleCommand("hub.ui")]
        private void CcUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs()) return;
            var id = Id(player);
            switch (arg.GetString(0))
            {
                case "close": CloseAll(player); break;
                case "welcome.close": CuiHelper.DestroyUi(player, UiWelcome); break;
                case "menu": CuiHelper.DestroyUi(player, UiWelcome); CuiHelper.DestroyUi(player, UiPopup); OpenMenu(player); break;
                case "play": CloseAll(player); StartTransfer(player, arg.GetString(1)); break;
                case "lobby": CloseAll(player); StartTransfer(player, Lobby); break;
                case "profile": OpenMenu(player, "profile"); break;
                case "page": OpenMenu(player, arg.GetString(1)); break;
                case "tab": _menuTab[id + ":" + arg.GetString(1)] = arg.GetString(2); OpenMenu(player); break;
                case "hud2": D(id).HudSize = Mathf.Clamp(arg.GetInt(1, 1), 0, 2); _dirty = true; OpenMenu(player, "settings"); break;
                case "welcome": CloseAll(player); ShowWelcome(player); break;
                case "chat":
                {
                    // команды других плагинов (/party, /build, /vr…) — как будто игрок написал их в чат
                    var text = string.Join(" ", arg.Args.Skip(1).ToArray()).Trim();
                    if (text.Length == 0 || text.Length > 64) break;
                    CloseAll(player);
                    player.SendConsoleCommand("chat.say", "/" + text);
                    break;
                }
                case "shop":
                    CloseAll(player);
                    if (P("Shop") == null) { Reply(player, T(id, "NoPlugin", "Shop")); break; }
                    player.SendConsoleCommand("chat.say", "/shop");
                    { var cat = arg.GetString(1); timer.Once(0.3f, () => { if (player != null && player.IsConnected) player.SendConsoleCommand("shop.ui", "tab", cat); }); }
                    break;
                case "kick":
                    CloseAll(player);
                    player.SendConsoleCommand("social.ui", "party.kick", arg.GetString(1));
                    break;
                case "popup.close": CuiHelper.DestroyUi(player, UiPopup); break;
                case "hud": D(id).HudSize = Mathf.Clamp(arg.GetInt(1, 1), 0, 2); _dirty = true; CuiHelper.DestroyUi(player, UiMain); ShowProfile(player, id); break;
            }
        }

        [ChatCommand("hubadmin")]
        private void CmdAdmin(BasePlayer player, string c, string[] a)
        {
            var id = Id(player);
            if (!IsAdmin(player)) { Reply(player, T(id, "NoPerm")); return; }
            if (a.Length == 0) { Reply(player, T(id, "AdminHelp")); return; }
            var pos = player.transform.position;
            float yaw = player.eyes != null ? player.eyes.rotation.eulerAngles.y : 0f;
            string mode = a.Length > 1 ? a[1].ToLower() : null;

            switch (a[0].ToLower())
            {
                case "lobby":
                    _map.LobbySpawns.Add(Vec.From(pos, yaw)); SaveMap();
                    Reply(player, T(id, "AdmLobby", _map.LobbySpawns.Count)); break;
                case "clearlobby":
                    _map.LobbySpawns.Clear(); SaveMap(); Reply(player, T(id, "AdmCleared")); break;
                case "spawn":
                    if (!CheckMode(player, mode)) return;
                    if (!_map.ModeSpawns.ContainsKey(mode)) _map.ModeSpawns[mode] = new List<Vec>();
                    _map.ModeSpawns[mode].Add(Vec.From(pos, yaw)); SaveMap();
                    Reply(player, T(id, "AdmSpawn", mode, _map.ModeSpawns[mode].Count)); break;
                case "clearspawns":
                    if (!CheckMode(player, mode)) return;
                    _map.ModeSpawns.Remove(mode); SaveMap(); Reply(player, T(id, "AdmCleared")); break;
                case "zone":
                    if (!CheckMode(player, mode)) return;
                    float r = a.Length > 2 ? ParseF(a[2], 0f) : 0f;
                    _map.ModeCenters[mode] = Vec.From(pos);
                    Mode(mode).ZoneRadius = r; SaveMap(); SaveConfig();
                    Reply(player, T(id, "AdmZone", mode, r)); break;
                case "portal":
                    if (!CheckMode(player, mode)) return;
                    float pr = a.Length > 2 ? ParseF(a[2], 2f) : 2f;
                    _map.Portals.Add(new Portal { Mode = mode, X = pos.x, Y = pos.y, Z = pos.z, Radius = pr }); SaveMap();
                    Reply(player, T(id, "AdmPortal", mode, pr)); break;
                case "npc":
                    if (mode != null && mode.StartsWith("cmd:")) { }
                    else if (!CheckMode(player, mode)) return;
                    var name = a.Length > 2 ? string.Join(" ", a.Skip(2).ToArray()) : null;
                    _map.Npcs.Add(new NpcDef { Mode = mode, Name = name, X = pos.x, Y = pos.y, Z = pos.z, Yaw = yaw + 180f }); SaveMap();
                    SpawnNpcs();
                    Reply(player, T(id, "AdmNpc", mode)); break;
                case "delnear":
                {
                    int removed = _map.Portals.RemoveAll(p => Vector3.Distance(p.V, pos) < 5f);
                    removed += _map.Npcs.RemoveAll(n => Vector3.Distance(new Vector3(n.X, n.Y, n.Z), pos) < 5f);
                    removed += _map.LobbySpawns.RemoveAll(v => Vector3.Distance(v.V, pos) < 3f);
                    foreach (var l in _map.ModeSpawns.Values) removed += l.RemoveAll(v => Vector3.Distance(v.V, pos) < 3f);
                    SaveMap(); SpawnNpcs();
                    Reply(player, T(id, "AdmDeleted", removed)); break;
                }
                case "show":
                    ShowMarks(player); break;
                case "list":
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"Лобби: {_map.LobbySpawns.Count} точек · порталов {_map.Portals.Count} · NPC {_map.Npcs.Count}");
                    foreach (var m in _config.Modes)
                        sb.AppendLine($"{m.Id}: спавнов {SpawnsOf(m.Id).Count}, зона {(m.ZoneRadius > 0 ? m.ZoneRadius + " м" : "нет")}{(m.ComingSoon ? " · скоро" : "")}{(m.Enabled ? "" : " · выкл")}");
                    Reply(player, sb.ToString()); break;
                }
                case "coins":
                {
                    if (a.Length < 3) { Reply(player, "/hubadmin coins <ник> <кол-во>"); return; }
                    var t = BasePlayer.activePlayerList.FirstOrDefault(p => p.displayName.IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0);
                    if (t == null) { Reply(player, T(id, "NotFound", a[1])); return; }
                    int n;
                    if (!int.TryParse(a[2], out n)) return;
                    AddCoins(Id(t), n, "admin");
                    Reply(player, $"{t.displayName}: {D(Id(t)).Coins}"); break;
                }
                case "tp":
                {
                    var v = mode == Lobby ? RandomSpawn(Lobby) : (CheckMode(player, mode) ? RandomSpawn(mode) : null);
                    if (v != null) MoveTo(player, v);
                    break;
                }
                default: Reply(player, T(id, "AdminHelp")); break;
            }
        }

        private bool CheckMode(BasePlayer player, string mode)
        {
            if (mode != null && Mode(mode) != null) return true;
            Reply(player, T(Id(player), "AdmModes", string.Join(", ", _config.Modes.Select(m => m.Id).ToArray())));
            return false;
        }

        private static float ParseF(string s, float def)
        {
            float f;
            return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f) ? f : def;
        }

        private void ShowMarks(BasePlayer player)
        {
            WithAdmin(player, () =>
            {
                foreach (var v in _map.LobbySpawns) player.SendConsoleCommand("ddraw.text", 15f, Color.green, v.V + Vector3.up, "лобби");
                foreach (var kv in _map.ModeSpawns) foreach (var v in kv.Value) player.SendConsoleCommand("ddraw.text", 15f, Color.cyan, v.V + Vector3.up, "спавн " + kv.Key);
                foreach (var p in _map.Portals) player.SendConsoleCommand("ddraw.sphere", 15f, Color.red, p.V, p.Radius);
                foreach (var kv in _map.ModeCenters)
                {
                    var m = Mode(kv.Key);
                    if (m != null && m.ZoneRadius > 0) player.SendConsoleCommand("ddraw.sphere", 15f, Color.yellow, kv.Value.V, m.ZoneRadius);
                }
            });
        }

        private void Reply(BasePlayer player, string msg) => player.ChatMessage($"<color=#f0a6b1>[Hub]</color> {msg}");

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

        private static readonly Dictionary<string, string> MenuIconB64 = new Dictionary<string, string>
        {
            { "home", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADEklEQVR4AeyWPWtUQRSG3RSxU2wVwUYUIUQwYERBUBPyA1KZ2o8foRGN/yGYyiZWqVMYFexSRAgkiIKd2CqWWrg+797JZPZ+zf2Ym93AhPPunHvunHPe987dk504ccz/ooBRH2A8gaIT6Pf7C2AX7ICFon1t48FPALITYAVim2AaXAObxF6AHn5QCyoAgmdg9wE8AS5Z+U+JvTV7cMNYMAEQ05Peh9ZtUGT3uLFv9uK2tyACIPQYKjvgLHBtlwuBxZr26HuhHBts6rQSAPFJsE7zVZC2NwSuG8jHHbJV5YLJoWjNi8YCaHyeXnrq91ld+8PFo16vtwT+GiwpBnSPxZpydRqqZYN1nEYCIH+XJntgCrj2g4tZSK+xDpmJzRLUHhZrqrFnatpgVaeWAJocjMgtGpwGrr3jYgqi6XeecGLmngh/TCL2U7W2qF971FYWQPGiEdmHhub+PAR/4Zea2XOHTS+BclkG1mjUVhIA+au00CuTHpG/ic9Bahm4ZAgXG3v/Af1fmGOXarBY06jVK6WeNljkeAVA/iHJ2+AccE2C9Mq8d4N1fEQoV6+Uarmp6rVN7wduMM8vFEDywYh8ReJJ4JrG4gwEvrvBJr6pMUOuarJYU881eKyDwlGbK4AEjTXviLStWjqI0LhtNGozAiB/Az76SaCjxbWmp32TZpkRaXe0dEztW5RRLxZr4qKfIBrDNignI4CgjvIUq2sam9M0+OQGu/DpoZPXF1g93Rbi9NoNyM8TcEE3HKxQtNKIdHJaufT7CeYpovHMYu2S9YyTJ8DcShYKLSfe0X9W6e0VcPS063WMAphai+ALqGvKWaz3vLO7Q5zAc8pmvlzEfKYc5fr2ld4PIeBKaYfym21yB5VDCBgUGtVHcAGMvlILLTS4gNAEffWiAN8T6vp+PIGun7CvfjwB3xPq+n48ga6fsK++9wR8PzHTDbren+7nFZBOGLfrPAGfx42kwyfDLU/AMxK+gXGzrxASN5ZDywjgp+QGuAiaWzeZlym7cUg98TICkvDx+YwCRn1W8QRGfQL/AQAA///Avx5xAAAABklEQVQDAChqeHAhRyCLAAAAAElFTkSuQmCC" },
            { "build", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAENklEQVR4AeyYPXIUMRCFtSbmJHAMMi5A4BBT2CYzARQxhBQB9trYRA4ggYibQMgZSMigqOV9U9KUflo7f7s75SpvqVdSd2vmPXWrd2b33A3/3BLoCuBqtdqX/JB8l+x3+Q+1bzUCAvxUgK4l9yT3Jddep+Fm2tYIeKBLA+bS2wzTcNVWCAjgkaBY4KVuGiTwaSZTvjZOQOAPBehU0tVOvW+X31r7Rgl4QGfGHdltiOWmM63hnOT63vONEfBALPCHi8ViKTkXKosE6TSaxEYIePBWzgMe4MLu3DZITCYg8KSHBf7IA3bxx+tYE6sZEwlLj60qkwgI/GNd2Tqw7LxFSu6OSGCz0omDPSidRhMQeHbr0pWf31KtJGvbpiIxioAHb+08oO/q69z7aFhvIkEk2IjciUhY+tzPDSYgYIS+Bj6+ASDwjXXFuINE5/pBBAT+QAisUvlEeutmveq8JzFqfW8CAv9IID9I8ka1uRQIyuVxbtS8V3UZu74XAYG/IyBvJXk70I3J40avMdGxdpJ06qwuY9b3IqAL/xPCv5K4UW2K9fIlEtYBHBKJ2nqyIMYw6BC/SFY6R7W5UHSKHRcJomKBIBKFf3bd8DthrX+d+xY7mDuEuUB90ti6KAe1ACX/GgnTX9fOG1Enyrk+mfcm4JwLO1OAdc6ZoDpIWJuhSzmnqGK70IQoq2vbq3bkB4MIsEagyPEaiTZHBaJ5F9YawHxTnzfSCVui1zp01u8M1e5z4qzJYAJaQyQgQe1nGssbJgIBwfhdmHfiZ9gygQS+jVrrqFQW+Oqz1SgC3E2R4DkoJ7EQCH4LKKe4BZH7AmDsbtCFvkk/rQM85yboQw94NizMk340Aa4iVJDgiZQp8lNf7yV5e4lC/gA0SciOTV3SSJsqeDwnEeACAvVRstCYnX+gPm/Hsre5qzFA8c398jnr8M31yXwyAa7mw0+KMI2F8OfpxBlC1+Z+vMCPzXXelnSTCXjw1k4Bohp+RQKbRWLtugS9JpMICDwALPCEH4C6Rb15EjzhBieerTrXBWf60QQEnqpBKnCdWNhBSx/7tGORuJKEdtUaeg5GEfDgrZ0H/KAd7Imz6lYQELiHkuq/ybJRBi3wnSWvimKCISEgcDz3U9v55Sz+TZadtKlVG4vUBGj9liYElIg8Aeb/KPAcf+jBWyB3njYxtYSANzz3fdxxKC3ws6RNDKwgoCjw3E95jP2sMTtvkbJ8t6YrCHAnkaCSrCMBeHxwn1VMAh7RL/XWGxG6/JzIdZ5mEtCBpRq9E6T8jUiq5l2Yf96oSMxnFZOAUohq9KcDGdWpfQPr8N2a2STg73biezqeV6xH4OJfApx3KVUCisIXSWg8r1BK84O9p3TjXSDFvMNZlYCFQWyoPEQjmE+km/VADyIAagEmGuqa9hXdnDKYwJxgrXvfErB2ZZe6/wAAAP//dsR2TwAAAAZJREFUAwA5UJBwdrkJ0wAAAABJRU5ErkJggg==" },
            { "film", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADVklEQVR4AeyZO2tUQRiGE5W0EiMKWtgphlSKilGDhXgN2OQ3GP0BIgnEC+IFLWyNgv4BK4mKpBCMa7AQEUXFQq3EImoKQQnEzfMuZ5bZ2dlz381Zssv3Mt/c3u97z8xuzkxWdbX5Z+UJKJfLI+BTgJGoBWRcovFRfG5/mhW4BMm2APJxQ01jkowPJXM70wjot0hs32quce0xtl8zKG0ljYC0sZoyryOgKY81AWnkCvArshdMgudgzuWmLdQ84+eYIK7blHvc/qT1hgIg7wE3IHwBToEDoA9kNXGIaxSiEjGugh78VOYVAOEa2JT4WUrvGNrzsNWQjIGZICZuMmuU3EVodoFW2W4CTYDEVieAJyGycYfpIfX9oLc740ccQFyPKG2bIPZOu0E+bYeAtto3ymNqs1EngM4zoBsYmyLnk6AE5k1j2lIcQFzDcDwGxhRT3zVT7yLh61SmwSDYAu6DGvMJ0J99e9AVu5Kzf9nhq8Qm8U2gRN85YNtGuyLfJ2CrOiy8t/y83c8O4XYSP0KbYuqp44abT4B+5qqzWO4/1UrODty/HMoN1J+AXhDLfAJiTWziIH0XYtMXUYBJ/jfOURBqRRXwkqwH2GJPKUOtiAKukfEQyX+njLQEAiK5chlA4uNgMS5Z4QTETdyM6wgwT2K5ys4KLNeTN3ELtwK8C+mEpoOOyTG0LJwAstUJ7RlCNuNHWhEFKGmdmd8h4rAqYSiqAOWsN9K2fJUoK/u48K3AD3syy7jOrufpe7gVW+devYnGCuUT8MaZWTnmOW15VV3u17wHadsMEEBvpBTh5hPw1pni3lA43Zmq7lVK5eEhQm+iQzDrUG9vqQ+01ZhPwF1G/APGhlnqKTAI1prGtCUcfUBculbRdjFUf3HugYohYhHoJ1VnZAn7SMcFUGN1Apj0hRH6pwRF1U7g6ZZgnuCZDB7dr4rrOL5t54n91W6QT9s02AH6wQO12agTEHTepHwFWmWzBLoFEptXAEp1oNAe1OXu/8Ss8Scojvb5wSBm/JnBSK8A9UG4AHSxpGvAO7TNgJ8gq4lDXJMQ7SPGGFjAT2UNBRg2yGfBKNA5dT1lVhOHuE5DlHmbRgowQopaNkdAC9V2BLTwYXtDdVbA+1ha2Nj2K7AEAAD//4Zb9aMAAAAGSURBVAMAGbhNcEXsnKcAAAAASUVORK5CYII=" },
            { "arena", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAFEklEQVR4AeyZSasVRxTH+5mQkSySVULIsAiEjIt8gCSbRPC5UFRQ0IWi4FMQp4VuXIiiCydwBtGFoKI4gSLowmHhF1BUEFwogq4ERcWFPn//+7qK6rqnu6u7rzignPOq6tSZ/jXfdlz2lv97D+B1T+C7OwOjo6NT4WtwHd1A4Z+mM4HNePgmXEfKYUqZ/6oZWIfRL3Ad/YzCGbL4gzKJ0P0dxZPwT3AdKYfVZUpVAFKcO7+fUjlLYt9RVhI6X6FwGv4YTqUXZYpVAMpsyuRf03GKBD+nrKJjdH4Pp9J1FFfBJiUDGDL+4XEZHNKfNI4DwvSLfDP98X7ZYrgORb/SOI6dSWYgU9MQ4ngT4n1wSP/R2AMXiORnIFgMh3SJRjwIiNKpE4A8zFzKc3BIs0l4iRNQ16aNgd6mfxKDULq+6a+lzgDyBCYTSWuVwtMmEp8If4kk3rTPkA1j+4CyE3UGoOgk8phyPHwPDukIjQtwvGnnYHMVeWcaCABlQUJ3KP+Hn8KOPqGijU3hSZv2gG91rFQBuBv6ZimIFoSyuA6IK8i0WSlMOo+O3xumRkNhFYCVhq/toKgDoRt2qWGrTTvVkHcSlQJgpPbj2Uo2BYTO+1vYh6R9sCYUDKJeCkDOAbGTshUI7GbBD+GQRpjB+C4I+xvXKwHIWw5iRPWINRPzI5lvYneZxjdwfLxuBMQw8oFQLQBFIZldlBaInSRTBeIJdrqZw+NVMY9gl/x6xUcpyVlpZ9jRAYROs/h4da/Xb8MYberJALIsyzqA0PE6LcuyUdiRXq/nmInPnKBN2QiAAnQAoefEcvkIWD9WTgCicR7ORyvDDiCSX68uwbqyFQA5bQsC29rXKzrJ1BqAIrQBgY2ez5Owj4/XDSylxsdrYwAEmQlfha/AE0hIR6x12emIteQ6DHS86vV6HyCOlMthfP7lBCmljFL0ejo4112gJ8ZvCPQjZRulEtKNrT41Qy697ACu16vuiPD1qhPpNHGSj9dkADjVhbUjzI76EPIhSoHQTFggNBOylVrM1xDEP2oaHa9JAEhSS0GjTLwCrWQk/dlOXSAWFjTGGgIhH2Ot/C/6z6nGRyuiTMervl6oXsm1AEheo7fd8DJCAodiOTLNkjUT5nJC/yA+LH19udtLXyVVAsiTt0ZeyWu0TeckpT4rKc2EBqRgV6Ff+DhQMMobpQBIXglYyS/MA+Yu7CLXKVtO8l0wrNDXx4E+fWdsAiB5jZKWgtNzpUbekrv+QklS0rWC78hjdNKXcR8AHOs3rTXyC0hIS0N2yZzb9G1gHGg5WeB00T2iPybpT4+FfQBQ0FdpigIpeQtUQamsAQjZWiA0E17O4Km+Gz9fwBb15eYBYDwMX8TqBzikTsk7RxUgdDrpp6ZmwzrtnAuVP5LjeXiCGmIPgIbW69+UIc3LA4ey1vXcl7WxFVsc+5buvEj4L+2tcI9CAD1B9OejqN25CQglqsTqfGnmpWvl4PP2FbytgGPqTW8s7NpOAKGjWpu2bFn5b1YeAE51I1ojo43Wt/tfIYjeyLPOdRpqBuJQAudfAB6AtAAhAwvEWvUPmvN44RoP99x6I56SV46+qwBA0typpk5Nx+MYkd6r0wkqywadxNsDO+r9xwixPsDFh3BI5iXaB0AWeNOFFY7MMmT+1SmdV8nE0it1URBDM6OcAtFY1QSgLpyEI5P0tJXdoJj4R2FHvZmxfJcCsJTfRNl7AK97Vl4CAAD//0gOLYMAAAAGSURBVAMAWloEf9cvhmcAAAAASUVORK5CYII=" },
            { "aim", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAGWklEQVR4AeyZa4hVVRTH79UQ+9TDPmQFlWSp6VBUWlqOykBMiZYzBYVBxJSWFRRJjWVmoYYlFYE2PQl6EAxlZS9iIp0msweVmZk9LCLrg5UQZBg2/v5nzjl33XXOPff9zWH9z1p77fXYa99z9tlnz7Bck/4GBwc7wY4QnU1Kk2taAQx4OTgthGTExlMzC5hghmtlo65fbGYB9Y+uggiHCqhgkppq0rBfgNWmBXSDPrDNj1o6oL474JN8f63tugtgMDeCnxjAl2AlmAVOB56kU98qOrbiswssRK6Lai6A5LPAN2R/FJwIqqWTcFhHjG1gOnJNVHUBJBsOHiFbHxgH6iX9MhuJuQZUPZ6qHEhwJKN9F9wM0uh9lIvBNPAdiEiydLej2AjS6FaU75DjKHjFVHEBYeCPiTwTeFqLYnQ+n5+Zz+cfBB/SXgJ2AN1mS6QDq8EMdMeDx4GnNhQfhbkQy1PFBRDqFTAWWNpK4xQGtQj8jhwT7V4wHkwAvXEHAu3dYAGithp+xToVvXLBylNFBTAjzxOqFVh6mcYUBvIDvCbCdyeOk8GrwFIrOZ+wilJy2QIINBvnK4GlDSTvAP9aZS0yMfaBS/BdDyx1kftCq0iTMwsgQB6nNcCSbpvLrKJB8hXE8bfTA+gyKbMAPK8GuidhAR3gOo8ZKznzFH0e6AGbwJ4Qkh9DnoJ/KoUxL6dzEEQ0CZ/5USONlyvgJufUQ6LUe55EI8Bq7D8A14ELwKgQkvXQDmCzEoxAnyBia8V60nX4MRR1lyyAJMdieSaI6G+EO0GCsD0MpQaud0DJmNgMB92gP/RBTJCW33+MdjK2mgijKohZyeYVzAJJy+LeQEpe7kF1DqiUtPIsTTPmV9iDXiscLCY/lrgjqwA/oIHYywjMjgajWTPa3Gs0zgd6qwqS36BtaSm+Z1mFkX0uP5bYNKuAE2KrQMhtH2KJ6/VotFrBAtISO5eZHAB7Q0jWcvxmYDF0kY+elaFW8dXn8mOJrYcxC9HpAWKBsNBrHRbTc/SmnS7obRobIawApeg+1+F9c2GOZ5xdO3pPOvHo1C+gE4NEIBdAzTFcZAsrIrvMqsOv5dJF0Js3ksXH6+KgHMrl1ImmxrxcBSR6qlT85+xTl0hnEzX/j4RauQpYhvP3oBz9iIFsYUX0eVErl9PMOFXc9H2fxT0FQTl+LjRLSt/Ss2wYD5mWx7HwIqJTW2JYTPMxKNpVhj36lAzFgPkVKVCGF790+uJzYY6rQvuI9aP3NA5Fr36ByMjzou0xnWeDNNKu0W4tZvO0bQBTwRFgFJCsZbTdBNiH/DRII79s/pZmJF1WAZ/IwCD1ZcIs6NbSg2dMcxfT0FquF59eTJIvQmfpbnx3WYWRLzWyRD8W6QJkFfBiYFG4TGcm01YNWWjXuEVChdiM3UMgQeTQc6IXn+17yTasXLIAZkcPkl0SZXuvdY5kbLVL1cmCNnNZK4vs7sdvRuiDmCD1W+UX2P5iFVbWoGzbyw87RQczlPorkGQ/0Ee7Zk/fu5vw/Qv8AST3wKdh0w32IyeI2C0o9XEDi8mPIe6QUK4APWT25aPX/wskGinnNDC4zWABaAVHg2OA5IXwkrdZGNPfttvxeTYtT6TLLABnfVzcEhmH/Ax4yXuSvlpJMf0xvPZZmfEyC5AnRWgD5j8y5jBj68HhsqkHxBgJXifGHGBpLbl161ldQi5bgDwIdC1ch1awmOYibSG5P2pBXRnhq33Up1hrpwqLqY+ci+JWhlBRAaF/B9x/TuqUeScD0TfwaPpjQhftcoNdY9yBQN9xQC9AbQd0tIg2Jj1zabve2MAKFRfAjPyJoz5e+uGetK/fzaB0fH4bfCoGOqnWmi7oO1hv48X0vUffr6ALeNItcy659AL0fantiguQN4FVhI4W/X5d3YKOz/VS05vX3lqSpdN7Qv6y9VBMHU1q6fV9JdtVFaAoFHEAXIOsrYFOERDroq/xbldMkPUSxCxJVRcQhSDZW0DL3g3o9A8OWFWkfVAXMSaCt6vyNMY1FxDFIPk6cDJtHcHcBdf/DTSriEUknfq03W7BZwx4qsiihkbdBUQ5GYz2LCvgbfl8fmKkjzh6zXQbfBX4KtLXyxtWQL0DqdX/UAG1zlyj/Jr5C9jDKSs3auxBnGYWoNOF6H9kkoOEjb4cBAAA//+RYIi8AAAABklEQVQDADG2H8hnwzIUAAAAAElFTkSuQmCC" },
            { "mini", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAEmUlEQVR4AeyYWagURxSG58a8JDEJBLO/JWYzCwQMEpJAICEYQkJIlIiEQB5ugsSIC4KI4Iav7g+igqKICCrig4iKILii4oa7oqKoIIK7vuj4/TNdfU9XV3fPOMOo0Jf/n3NO1alz6lT1rameZypP+V9ZwOPewHIHyh1ocQXKR6jFBWx5eLkDLS9hiwHKHWhxAVse3tQOVKvVn+E8eBq2G4qp2D82U1XDBTDb+QReC/+F78B2QzEVex255jQavLAAgvWCywnYDTuF4coJu4oSFhZAgNFwCOw0lHNkUdLcAliBlwkwDlpswvgcDobHIw7uavGPOIq5GWkxnjm8YBt8PbcAnMfCV6DDZRRNdj9yMvwgonTURwf1K+bvRLgEHfqgaA6IMDILoPLXGaLHBxFjGImuRVa/SEpYXXYhiT8IHoMn4R8aEMUeId1wDP2vGjuhZhaA11/wOehwhgRrnNEGqV3TDvYl1lTosArlAnTojfInDCKvgJ+8EbM8u51mfNqwSFUCz4QW/lzivmABbNlLeHwNDSqLrNEGfSIxTkWcgLRYYg30b5nT88gUggXgNRD2gg77UH4gyBFYA3YCtcb6h3wGJToDBiu9Er4XcYV1oe0K9iHooLloTs6OZVYB/tf5ekbomf0IWQT5yLfIr6hfOa2PP6daX1YBX9R6ez429qgNaQ8a8jJObJ5OJe2eTiadShtMt9QB+vCZKoBAz+KkVUTE2InmnlnUXBylV76IpqCTSHl1Mk1j5HZo8TFzi//ZXUeqADo+gbb9LM/kXeieWdSuVKCunr9+qKuJ0SzsrlWJcYcA56GD5qS5Obsm1VhTzMdnRpd6QB8doHbNP5UOenk/9exKqADf6Zw/KLKPRFJCz+7/bLG+VRG5OE7vcA2yZMXdDutkcqeSn7uhHXjNBka/AUNwK3aSzvtwNtS3KiIX79M7hyI2Ql0WMTPh59b1JuEc2gGdudbppjWcrhVD/w7q2fV3jeZCfI/HLop4G5mFW15Har6pBm+AzGABJH6Xzh1QpwbikaCxu4n1Ycbo61576vAIFeA7pbY5SriV4G9Bi8UYvdmdINQHl0KLNzG2EVPvA6gJpHInejFCBeglha4YieeaRKPo0Vn/BtJiBrP+G962jVZXH9Qt13/n1TvHVmL/Zv3R9f+CiHEi1iIlVIB/ZR5C4G44FO5m3HToYyIT898dfJ/Yxld3/klxQ13RZW0VOebCvlDv4Hrrq/fWP/25pY9RguvsvVj3r33qlU6/SCzD6g8tdPXtZswU29iIzhjdl4bhqxiIGP+h6WRTTvs+oi9Ue3TjVkkXUGutVIYi/ROApgT0qAxkIgsTrU0YjJ2H+69Q37qITCiXXvJTDqFHqELgLXjq/RQRxBlaB+DnX7hobg7E0G9NXzLKXhswE/gFv12JlsgIFqA+BmhyupXqUnWVNu2ITp5/0HXfOYxsC8ilx1ZHqmIrh1ZcOaX3p9//tSLOm1mAPBi4B34F+8AX4TdwAbyn/naSmLowKrZy6ChWTul78/LkFpA38EnpKwsI7kQHG8sd6OBiB1OVOxBclg42PvU78BAAAP//3ZaHSgAAAAZJREFUAwA+OLZwbnQo1gAAAABJRU5ErkJggg==" },
            { "shop", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAC+ElEQVR4AeyZvYoUQRDHZ0URg+PgAhNNDMREMDDyvMjQUzQQ38EXEETFQAUfwCcw8eMExVv1AfQ0E8FAAwMjRQxOzkAEdf1VM702vb29/TE9M3fcUv/pnq6u6vp3V80M7I5qk/+2CXR9gFvvBEaj0TnwAdjyloGlrnfcXt91AteZdAjYcoSBu6BX4iLw1xPhfk7hsEffuspF4BpRfATT5Pg0RRfjEwQGg8FDcBAoIairwJRe1cEEATPSur9Wt7rp9wnoKI32FX2zLg5QBwuM9UJmngB59ItI3wBTTpg3XfZnEqiDe1G3ulnhFLqSDRY+qwMJJWDXgbbvop1j0dtASSgBqQNl0JPLHx1HEAHq4AsGn0BfZJzSQQTqqO00ugKx4sLau8BPYMqKvskh0Nb74BTB7gFaftB5CpTkEGjrjXxeRfr/8oRj/61vYwi8w0jY0yiZ43FW9MMO/ztZafzIpC8yTh+5CSYA6xEGdh2UPgVv+hBPFUygqmR6ZRMoXQd2+qyykeP0kYh6S2BK+jyQoE3EEpAXWlsfdjPTR4hEEeD42vywm5k+0QTEABSvg9D0IZboIhab4gRYJCh9mJdE4KUYGjjKju027pvoBqWPLBRVA2JAHdgfduLjmOiaAJvhenlNPH30WrK47se0JdMoOH0k4KYI3GDnGhGCegRMmXh5mcpUAnYdmD6b7t/zOUwiQB3Ih91Xn+OGdOusterzlUSgdniR9hsoJZ9xfAF4JZkAO3MH7AWlZB+O73ujR5lMANteSBYBHjtnwBr4XkP6y7HMsE32k0yAReV5/ZhgF8F8DekP0Z3kPkiYm+UnmQDRXQLT5PI0hWM8y08OAde/ODo+n07P0a1vrk+n7HMIDJQH98Wnsy18c3065SeHwHvlwX3x6WwL31yfTvnJIXBLeXBdquqme9g5muUnmQAvmSHhnAavwQZYB/KVuozuOf0gYW6Wn2QCEp0sDhbBPFgAS+CZ6GKAzRAk+ckiEBNkqbnbBErtbKjffwAAAP//EfQnvwAAAAZJREFUAwDMpJ5w608ZxAAAAABJRU5ErkJggg==" },
            { "profile", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAEKklEQVR4AeyZWahNURjH7zEkcyJTJGPIA0rmJyJzMmYohCRjKQ8UwosylTKUDBEPkjkylIxPSIYMKfJwle4DxcXD9fvn7N063717n3323ueeq+7t/29931rrm/Y6Z+21zm1Q9p//1RdQ6gWsXwF3BaqqqhrCaXAHvArLs7xCux1Ogak+tNSckVgPinkEL8BNcCLskOUk2s3wErzH3G60qSCVAkhoOdk8h0NgPoxgwgtsltAmRuICSGQlWRyGzWFUtGDiUWxX0CZCogJIoCfR90CLU3TMgp2ylKw+1Bzsxkf3nJ4CldgFEFi2J4nXBHooRxiTyWQWwnOwPEvJCxkbB79AD80QTuArQxsLSiKWIUb6LIuIPmaS8B1fMwJjN+maD12MRhkKYyFJATboERJ8kC8L5txmzjHoYpirFCInKWC4CXTe6GGqnWtXMsw2ZyxJAXYFHud4DleemGH7MMxwsJqkgEbGbVOjh6lVZtD6MsPBapICnhm3g4wepg42g0+NHllNs4BlkaOWla02c+tEAdPZz8eYxKqpzJlG53jooiQFXCaDt9DFKRIc63a4MmN6kR1x+5Bfw6swFmJ/hNjPfxBxDvwNPXREuEmiKkTH6vbIXaFWR0eJG4y3hx5+IczC10/aWIhdgKIRWF/kjZIN9bbVsVrHhk+Mad9XH2IONuDjZU5PgUoBBdTsmQT2MbIUfodR8Y2JS7E9QJsIiQvIRtcTXoP8GebDRyasgudgYsQugM91Bk6A+jJ/JROdb7rQ5oNuYzrFVmB7EY6DtXsaJeAEstQOdI12MozzIGQzFVt9sd/gUzsUamGQk8gWBGkHz2KgxHvRpoXeOLqB75OwDXJkRC4AxwPwqnuvtk7EanhIj77Qi2h1ONNtTecjUbL6FjO2HwYd/HTp0X25H3MiIVIBJK/j7n086opI40P7t3aSvuwoI+F6eAI+hh9gZZaS1XccfR1UMf3xchBadKbjQTYmYjjyFoAjPT3dpFo7rnSaPI3eh2RWwzfIBQGb11A/CHTF8Ax0oY/RdWLrC+/2V5NDC8CBjrn6LUd3V89Yb8/ZBF8Ao2ybnl2NrXzAeQzq4v+H1kNLhAvk0Jg2EKEFYLUXaqlpfIwnYCp7uO8RIetTuxuaj4FIu2AgAgugcv2qpheOa7yZQHfdjjRlfOu+vM34XEsuOmOZ7n9qYAEMz4Uu3hFgp9tRDJkYW/H7AXrQS26Gp9g2rIBRZvIhoxdTtbvTyKBgYQVo93HttBO5ejHlW8a5zcUfLqSAd75V8QUdwd0osQpo5Xrgs1np6sWUiVVh/Lc1uq+GrYA/qS4L9QWUenUirwAvk1pF1AcTuYCoDmt7XlgBr2Ink75hYC5hBWwhj/ew1NBRXbnUmEdgAezF+rdQb9pSQ5elwNNvYAE1llsHO+sLKPWi/AUAAP//IT7IHAAAAAZJREFUAwCMOplwyJdCxAAAAABJRU5ErkJggg==" },
            { "group", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAFFklEQVR4AeyZWYgVRxSGHQ2ShSCJeQhkIYEkYwgJWQ0uWSG7kIfEBfTZBfVBRUVhxl1EER/dUMEFFJcHcUFfxGUQURFxYRRRcMEH910UHb+/bndN3brdfbvu3EEvzHD+OqdO1alT53Z11amezp1q/K8jgOf9ADueQMcTaOMvUPESamlp6QUWgz3gagTJi5C/zzsv+v4PmoHGCLYPDgBHXcFcJrgPDAU/gO4RJA9DbqLPbNAVuRxNo0M90BjB9kEBMKGXcKSJj4dn2XahfRLYG9kgBpG1L2eVNYkk26kovwN5qScdG0AWTaHxDEiinvwA8pnUZnS5A2AgTWaysWotNiP2BW9EkLwV2aUGbL9xFa5cV1e3AXyMTmMk2U/Jss8dAA5GgDoQ0xYc/wuawM0IkvvRYRuISTZ6V+J6Is+wV/9U+5AA9KJpsBizYiGBz/B0vq3XXFLNbR8SwCeem+Ne3a2edivIn4IQym0fEsBjbwZ5tsjY5GksVMhT7UMCOOI5z1oWftthz7ZcNbd9SABHPa/+juQ2+1unH7zbN0nObR8SwFI8PQQx9WN72wJ6g26gO5CsbfSvuBP8AVgOMgnbiuxzB8A2d5YZ6NiHWfoHqQncBFeB5L/hLjVie85VuDITN7kQujT7hiz73AHgQDSP4gDIS/vpuABk0XQa/TWPytB+Jj/fSClFUAAM9oRxfgRK5lJ3BtrVbw7858gGMZVaElqsfUJbkSoggIIdE3oEJlLTsb8EvgfcANeA5MXwPvSZBB4hlyPlQqfoVJF9cACs2XowHIdjgBK7D+Evg1eBZOVM4+gzDCjHQZ1OBKlcqAf8LfATGA5yL9NcATCRzmAg0H7ezHQWgv7gK/AeeCWCZOnUtgjdaWwOAr2oyolQVZfKBoDzPrjUI14L/xqE0rcYrAe6dfWCV5VSA2DiXYB2iN14/Ai0lZRL6YLTyLipfkOdZA20jsF0Iup2hFhEO6g1gj+AErXX4YJk6fRi7kTnk8bSWaKx/baK6okB8AtpufyXMOIadPW8ZH+CGWAnaAZ3I0iWbjp1BdKD/kmT1TuxmrY2U0kATF45zkBv5IvUf2VSQ4Cf6tKUTPQ9BQbRqmAuw10ajK8JrkIyOm0W1+CHwO/SZaEoAAy0BPyLyjEG+JKJ7IJXRNhqOX2O8Qng0jR8+u/XbDq8CXQN3UH7OvA29UQqCoAey4BLyn90muqQcfXBMkFojF8wPA9i0vmxIq5EXOdJJBo2gPIMQYyFl5ANgA5KEfxtbhSOr5dYVahgrCuYjgYu9cV3b0fxG7L/leI1dPPpp/QEsZVsAKj89bYbh9vRV5UYU18ylOS541rftB8HOsG1A7p9JE8kCC1FyQZuAFpzRhkVKyPeHsxfNiUHJEHMxLF2sTtwl3TC27obwPtWWxCU2xek6pf+2L5v45EglAGMNJXWQpcmLXejcQNQHmOUUXEh4u3BLnmDvuvVbZUgVlHZCFyylyY3AJ2kthOG922lygJj3/KG1IddT1VUVfLoKvTxzNTdAIziBS10Bt1z5vYZL/M7qtdEADwx3f50GGrOMT6QUBMBaKJAHw5glvQRoFMtBaA0x84eQYdiTQWg6yrzLhDLymQI7hM4WWgypZ90GWXuIl/HUH/uRzXlaMaLG4AuITo4lH3qsmI6tGMR6k//I1BCeJs52f/a2AB4JPHXgS+QN9GpXQkfQf7or4uSvlx0Q9bhZuZnAzC1Giw6AnjeD+0ZAAAA//9LIwPcAAAABklEQVQDAOWXp3Ae3C8DAAAAAElFTkSuQmCC" },
            { "settings", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAGC0lEQVR4AeyYSYgeRRTHe1yCC5plEhUVERHFk4K4JxrEg8to1MQFFRfQxCR4UjFRIkpQXA6CoCYaXIIEk+AWYyKIC9EheBAVPAmaQzyIRp2IoETM5Pfv6ervfdVV1d0zPTnl4/27XlW9ev3+Xa/rq66Dso5+o6Oj88HPoE5+weDGjm6bdUKAgGYR0FpwAqiTYzFYBTqRTggQyQPgCNBUpkP66qbGKbsJEyCQQW5wH2grN7UdELKvJUCAs8EOEBSc7gKHAyf/oswa8H60XQusTP4MEPF07vg+OBk0leeJXaR8+y00/AOcHI3/ea4y3rJuBtbgeAZoKnr6z4aMIfUf7e8CK8sgcTeYahvb6FECOF2Ao+tBU9mJ4c0EGnr6dOWyIb/2LuejvgJGuN9WcCdoRSZIACczceovdRsILiUn0al0Y2hYiv6/wr3Z5bS/BhyZ24mjlkyQAE5eBlpdKHL5jeti0IUswolSjSIqIvMGvSKzBSLRNKsQwPgGBl4HrCzi6f1hG8ar4+ctxh4H7gEfgTq5AgOXZreg90mFAL1PAyvruan/8tn+1jr+doM1QMFNw8FdoAmZygIRInAkzqyst5WudUiIzOuUTcgcT4acZWMIEfjEGqDPB/tFIBEis5WbawmmyGVufi0uIQL+UncNrA8t7GsLbC8Aq8E2sKuA9FXo59U6KAwcGaofA3v/WgIfMsD+Yx5F/UqQFIKbAp7B6EuwEMwBWskE6Vp9hrF5Ekyhr6l87hnOYfyAa6vMAMw1Xe84g6LUn1qhVgscHkKrAn+QsuKTNicHoywHXxRjUNNCPN9gsRs40c7gTFeJ3Uz/qs5G5aW6JPAYfeeApnIuhitAU/FnoUyjCgGejP6wlnmef/fqZRV7BfNw2TCmbKKYDbQZFKQrNWkqZQVjzy5raaUZARwq+BcDvp4ItLkmjSlzksbNTPs8MAxGCkgfok87UopcNEbvSl6puShFrYn2UHm9nAGCv5WWUPBLCCL1X3A646ykyK60huj+WJr6hbiW0OL/gZWLTEkAo9CNlxL8S/RlWRa9nub1fO/VbfUHW0E/A0SF4DW7LwQMHnFtloD9qlK/gg/NiPostGrZepslcq8daHWCv4166P6Kax19uVgC2/OW3qVpIFrmeqOyLJUWft/XdqCn6z/Fa8qUzn2kLIHom+578erfeXV/RbLd/tLpk7e2esltfWUonVMELrajE7q2unZ/P8T0bwYXgqlgEEjXMqoNm3OlF/FVVwmUn3ptp3j1vGoJ6En+nbeOXXR207fzG2vuv/JUfqLlcWDlKirDYAToE1O6vx15lLE76I/JRq8juCcrCeBslAE+6/Ifj76UaJn7KmXg9el9e85r86uaMc2Saw/uyUoChdVnRekKfWRHP+ecEeT/R1fK6cWLrizYyO4pyrnFGNSw0K/Vzf+Q0tdi3wCfgP8ia9OkHNe3afLUgBvuAQ/hXdsGfVNvQ/8TaBsifTX6RdgsB3vQm4i/tb/MH9RHAMffYvArCIk+tO2pgWancmqAj+1A39CXUM4AM4H0eynbpJli0H+BSofKiUYfgcLq/qJMFZZM8tQg5STVx8qldPG38ZrhvmEVAjylN7Fo86GtpVFp1urcnwB15rqTUqL0LN81GnRc729hgocLFQIEn0Ei9G1ad2pwGGOV5xRpKQJ8D6sTgUQzqoeQv2s0KI0HKZ3oXGqpq9gySMAaBMjcQX+MzDSCa3LqrO2ADRCXpYhMWSmUxcShxaCo9opaAj3TzM3MWpwpbVya+acGyXN/CIZyO0v8NnK/t2P9rQhYJzjN04w2pY09NYjOAMGHcltLd2xGlTraUnObsIybgHMHER3o2i2Izv1jJPzU0TZjAT7cjNr01He5vuyCqePuP2EChaMPitIVm3jaFaHTXxYXEnweIKVmNE9PdIlOu7XlYFhcuiKQ+uSM3T24LMaMY+2dEOBxKY0q/5Kxm9Ku1Akui/S1kk4IFHfUy6aXrqhGix/pGYJ0njroE5LOCBDQOnAMGBhIX06lu+2eKEqyMwLRO0xyxwECk/yAa90fmIHaRzTJBvsAAAD//xcVgt4AAAAGSURBVAMAQ8o9fwmpqzgAAAAASUVORK5CYII=" },
            { "close", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAACR0lEQVR4AeyYMU4DMRBFswjRcRI4CVwAJNoUJBINNFyAjg6FSKRLAQ1cBm5CCWL5f2VLkePsjj3jRJGMZnazXvvPfzbZWHsw2vO/CrDrBawrUFdAOQP1X0g5gerhdQXUU6gUEK9A27bnyC/kJ/JKWXdtODWRXv9srcOGBhEAhA8x/gV5gjxFLtF2jbNJOK0lxLz+E9pE3kSdIMxoeVjJGYqoIZzGbEWXHxseJCkCaJrmF2J3yDBUEBvMs8Y9av7xw1CKACgCwVecJ8gwsiB6zE9Q6y0ssulaDEABCD/jrIYYMM8aKCOLJABKaiEszdNPMgAH5UJYm6eXLAAOTIUoYZ4+sgE4WApRyjw9qAAoMARR0jzrJwCwezz7IDAi/JFC04iPyqSnDQfF0gSAwj0QvL2aZuYpagZAMQGEqXnWNAWgIJJbgG+cw2Ab74XtqmtTAPeFncPRMTIMts1dn/Be9rUZgDMW+8KG5rL2TqGIvzYB6DHPfRPT1/NnMwg1QJ95fqmZcF0MQgUwZB7GuygJkQ0gNd8R4FAKIgsg1Tz8d1ECIhkg13xHgIM1RBKA1jz8d2EJIQaA+UtUjz3np84QbsvDjZlGRvARexFpjzaJAGCe74UeIwrc28SgIl3XmwDBsbFH7ANqiryJOrnS4XshmldviQFBjRCiyHuhG4D8uBy7wrjUh9MaQ8nr36JNtPETrwAE35FHLhcoZhrQXSC9/odUXAwgFdx2vwqw7RkP65VZgbBKwesKUHByRdJ1BUTTVLDT3q/APwAAAP//VjyuAQAAAAZJREFUAwCoIwhwnGpZCQAAAABJRU5ErkJggg==" },
            { "arrow", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAABt0lEQVR4AeyYsU7DMBCGE3iaCt6ElQEJiYlKiIGFgRHEA7B2KCMDQ3kaEOJlaPqdlHRI7Sjtnc+p5Op+ub3W9v/dNVaUk+rIXwUgdwNLB0oHlBUofyFlAdXTSwfUJVQu4NaBpmku0A/6RtdK39vpLgAYPmXHJTpD5+iD3C2jOlwAWpdNO3bD0gLCBaCu639cP6J+CMRdP7nPZxcAMQTEJ+Mc9WOh6YQbgLgG4p0xBCGdOOiacAXAfGUN4Q5gDZEFwBIiG4AVRFYAC4g9AGS7NNJc2FEAzuZL9ItcgtLIrQbDTsgRe7OTbRNRAL5/RTM0hXiOmRgCWMcmTSk/BPCC0T80hXiKmYgCcGGt0Ay5BAZDtxikqzkGVvImpChA6MepcpwSch8UuojFvNw/RbfODqAxL1RZAbTmswJYmM8GYGU+C4CleXcAa/OuAJi/YsPQUXnPOT94VDIvGi6nEObludBbwIWc84tAfnTKBaB1038uJOYPrny7ZuUCwF9Engs9dJsymphnHR8A2QiIL9SFuvKypsilA7JRKhWAVJUdu26aDozd3eB3BcCgiKolSgdU5TOYfPQd2AAAAP//+tthAgAAAAZJREFUAwATZPVh6G601QAAAABJRU5ErkJggg==" },
            { "plus", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAABOUlEQVR4AexXbQ6CMAwV72IMnIf7GO9DPA7yw7OIrwGSZW7Lslqm8Zkum2Pt3gc/yvHw4z8SqG0gHaADSgXMXqF5nnuMaR29Emc03YwAbrxinNchayw/H5YEOgeuu3a29UtLAnp0GRVIIEMk0yN0wFTejOJ0IEMk0yN0wFTejOJ0IEMk0yP/5ICpkMXFow6gj5d+fsRcFD6ioiJLkmCIfk9ECQCA9PAt5tohGARLEEeKQDCh0uYzdm+KwAVJD4zacQcAwYLpPaIEmqYZME4YReFfVVRkSeow3fx62/8oge3At88kUNshOkAHlArwFVIKqE6nA2oJlQXogFJAdTodSEg4Os/ctbOtX1o6ID38BIjJfh7PVWFDAJDQw8v3RItZ+vkBWyZhRsAEbaAoCQRE2XWLDuwqd+CyFwAAAP//klBzIAAAAAZJREFUAwAV8pthz+VpYgAAAABJRU5ErkJggg==" },
            { "star", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAEuElEQVR4AeyZTascVRCG57qQBAVFiaJwk6BR8QP9CybgSiK40J2uEoWEGDT4tXGr148EElH0ulFBt5qtaFzkByiYTUwkRlD8BLNJdpPnabp7Ttd09+2e6bmXkIR659SpOqeq3jpnmrmd60ZX+L9rBJoOcDwePw3+BafAU03r5rUv5AQo+BYK+xQ4PsD4WW5DHVYWQoASXwWbQCHqrxSTIcfBCeSdfqGmyIO5r8Y1u2lwApQSu48pk4WcwqAE8g7XdT9jwMfgpzAoAQr0nttp1Ewu8ikYMtHnmmwyxMdgBOj+rRR0EKRylMn7IJVBT2EwAlRoZ+0waiaX+HwbvAXUGTJxjWuzybwfgxCg+z7v490/urS09J+gyGMglcFOYRACVBafPHZ8BXshCzuFuQm0db+ofpGn0IsAxS6DXeA5sAKOU+RJ4L1mKOWdUpsofh8ms9HIPSeJ8TUwljGNvZwuWkufIkAwi9zJaEADH0f/Cfg4PE/Ab8FHwC/ibsb7QSordPyf1KCe29Jrpdm9T6AYy5jGPm8uYM6U3KPYpsiVBHDuBr8TzCK/YzSggS3yQeZ2jKFVJPluywp9rmlZkrnMZc6U3Ak8kjtDnY+jZ1ISYObz+g7GWeV/Nu7JO406LbnveTwXwKxyNxvfBJmkBMaZpf3Dp8splnwF7KbF7ETfSnE3gy/QW4U1n4ObWLQV7ALGMJYxjW0OzK2yufCmBF4vjGH8hnlR5GaSPwSeBC+Dj8H34DfW9BL3gBPAGMYyprEtriBn7rq4bxTGkgCBvsS4F0R5DMMO/L2LZN9Mkufyqpg7xtiL31oze0nAGY5PGOtIrI7H4z341kX4kpprtSaZxVtj6aoQ0LrRJPoUb71TBDRuFIm+xVtrLQEd601iluKts5GAzvUiMWvx1thKwAU5iX3qAaskfibYek+J8Syb6r6w+/LcuJtlTQJuJdCHjPtBFH8mR1vfed0Pv/15zjVjdSJgFAJ+wBhPwt8smOeS68NuO2+uYK6fdiaQb/8xH4vhl0KZYzwb9v4Q5q3TvgTuCdHOhPks0xgj5miNedUR2BHa8XOYzzKNMWKO1pjznkBM3pqswbmuV+i+UEQtAZ7tm8AhcBb4F5R60xPrdIgZcwR3ddr5BCjkNrbeAAq5wKO18rcvayz8JRacA/6RchejP4vVz+F/EVSIEONv1qR/od3Imi3YOklnAkSLT4ey+yRMC3+PtbeDKNoOY6wjEq9R5+9BHwJ2kvylnM4LP4DlV9BUOK6KpEQOGANvvEaxWSyplz4EYlceIaSF+wLX68W0In8x8zoJdaYVkYh7vW4PVzyjUWxWcE+mfQjcO9mWaf7fV13hf+I9BLZxv48IdaBNH2pFJGKs1Bhzpb6K3odAPIFKICZ+oe32doo+DMq3C+rA+7+dda5xLWqjLOQEmoJ6PXwzvUyRdrwsPJaH/xI4gt03bO5xL9Mpaco1tbDPCcTC7KLd9Koco7CJfypN1eBa4Cv3bXiMYSzUUuquWulMlT4Eit/tfxDApGt2nHWtAon0RIwpEX+dvta6MXF2JkAy7zXD0p18tF6VJH4nlXgZEcYtwHdQvvXutLczgU7RNmDRNQIb0PRKyssAAAD//3NY3XAAAAAGSURBVAMABtuwcKYVMecAAAAASUVORK5CYII=" },
            { "cup", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADY0lEQVR4AeyZXYhNURTHLy9SvCgfz2IGL/Kdr3wkg5LUiORpGl9JEWreBi/Kg3ihRp4Qat6QrxcfwyseMKSUSCkviDxdv/91Z9p3n33Oufvcfe5czZnWf9baa6+91vqfvc+5586MLf3nPwWBkd7A0bcD5XK5EwyC0KKcnb47mmUHTlCkHYQW5VRur7xZCMzxquAX7J07CwG/lnKOjhDgYOuMv0Y7xe5nTIM/dj5n0X9O9RS5RyIESKhzOBvdaqKe1FtNXy4C3uewJmO+g0hvLgL5thA4eyoB+4gHrh9J51svlUCkQos5XAS+mD3yAJhqjptpO2rX9KZeXAReasLAfMNutrnQKvjCGpdcBOyg3faiJo4PWrWeW+O6CGxlK9faC/MeU3MLNTqAKXURuMmKd8CUqyRcV3X8rOqKwp/5HnGs/aGk+Nej+4ApbxjcBjUSOUI8xn4RsR2YMo3BAxJfRk8ApswzB572Ait+YrXGPfxTgCnb6O236ZAdISAngboPDsm2sMsaazhXvzLCRd5V4wA9vXLVcBJQIAvOobuBdqRUKmG5RTHumQQvV3o8010gSXRcu+nlfFxQLAEtYOEltL5oPEXHyQyaORU3meA/ydx0ECcPmWir9oDplkQCWkKCT2AFtt4G96JvAPsDpQcSi/DXJcQuJvAoMOUjg2tgD5hFzTXArsNUraQSGAon2SDoAzvwLQd/gClPaOwYiM2pOdDDosfAFN2cy8i9E1wEb83JJDu2WNIiCnxg3j774/CdBgM0uQ+sApPAZCB7P3PPgI6bYjGHpYucn4dHHkYmAspPwSvoI8CWpTguAJ3hb+ivQLZuxCXYthwm13XbWe84MwEVoPAZ9CagpwXKS74T3UGOs+jM0hABVaWBO2jd4PrwwaxLbhGlG/U+uiFpmICqQ0JPqg3Yq4Eej3fReh3Rzgi6KeXTd9qVxG8GqU8YcqRKEAJDVWjqEegFG/G1Ab12CO3ygeNgAH8wCUogWFceiQoCHhcrl9BiB3K5rB5Jix3wuFi5hBY7kMtl9UjqvQO8Gif+/4D5itg9VJzJv5x//7fz2GNvAiTQ+4xe3jCDinIqt1fSLATSCzQxIguBXvp7D0KL3liV2yuvNwHeJvvBTBBa9P2g36t7gr0JsKalpCAw0tvxFwAA//8kwAJxAAAABklEQVQDAHbskHB5ikbMAAAAAElFTkSuQmCC" },
            { "play", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADWElEQVR4AeyYsU8UQRTG7wyNNEYpLIQKA0IIpSCNiY20ElsjiUppIzGKlYlRE/AfUArsDbZYWGiBYEmICJRcYmKCiRaYGJLz95Fc2HlZlrud2bkjOfK+m3kzy+337b55b+ZOlU74X1tAs19g+w2034DnE8gdQtVq9Sb4BirgiSeP3P+eWwB3fAoGwAXwDBHfwRX6Uc1HwKBh2o+/jIgF0EU/ivkIOIrgbSa2EXGHtnArQoBIn+VjHhGrQGGGW4yFFPAjheJlxrTQZxHSST+4hRRwEXavwD6wNs2AFvkN2qAWTEC5XN4DIjoMw6/AWg8Di7yJJdBNP4gFE1Bjg4gN+qNgCvwC1q4zsImIR6CDvpcFFyA2iKiCN/T7wFtgTevhBYPriPCqHYUIgNiBIWIXTOKMgU1gzbt2FCqgxhYRX+gPgRnwF1hT7djibdwFZTuZ5UcRIAKI2AcKG4XVB40ZnMNX2K0gou7aEU0A5A4MERUwjjMBKsCaascaIuaA1oqdd/zoAmp3R8R7+loDabVD2ekB88fWjgYE8HWBDRHetaOpAmrPAyEbYARfteM3rTXVjh1C6pqdaAkBNVKI0CLuxU+rHQyX3ukjiZYSkCD2j/4usLZjB1pKACFyD4LbQG3aoeg+c461hACID4BVmL0GOkvQOKa60UOIfXJGcZoqANKdYA4ea0D5n8YxhcwExMdBWs0oNU0AxHU20P5I+V55P8lcZwrVh0sQV71Izjn96AIg3g2WYLEI0s4FOksMQ3wa7HFNpkUTAOkO8Bg2W0B5ncYxnR20eEchrjOFM3mUE0UAxLXnX4fEc3AaWFPe74P4PKjaySy/UAEQ7wILEFgG2vfQOKY1MAbpSZCW952L05xCBEC6DBQOChft9e29FdsKpyGI66xg5+v2gwuAuPbyKzBQTtcen65jyun9EH8JlG2cyUadYAIg7p3TGyWv64MJ4MsULmk5nanSLB/H5nSuadhCCtCv1JaAcvogofIQKO7tvLcfUkCSjPb0U5AeAXXn9OQX1NsvQoByei/Etbevl0fu63wE2B9ztQauQjx3Ts+jwkfALW74E/wBMxBXavxMP6rlFgDhj+A8OAP0e88h8Yi93AIicsy8VVtA5uOJMNl+AxEecuYt/gMAAP//W00ZRAAAAAZJREFUAwBZlAdw5TnckQAAAABJRU5ErkJggg==" },
            { "check", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAACA0lEQVR4AeyWTU7DMBCFGzgOnASu0G0l0m7ZcA9KkeiuC7gPXAeF96oJitxJHMe/lYw8JBmP7ffNOI1vVlf+VwFyF7BWoFbAMwN1C3km0Ht4sRXoum4N+4F9wx7GSIsEgOAnCD7B7mD3sFf4GlwvWnEAEErxB0OpKp4xRQFA/BaiTPFwrV6apul4Y1oxABDPzO9NgXhuIf4LV7UVASDitcxT/LuqXJzZAXzEkyErgK/4rAAhxGcDCCU+C0BI8ckBQotPChBDvCMAw5dZLPFUY/0ZxeKPsP5UuOYgF8PYsePBFl/YyY/UnHUmAbD4LSb5gPWnwhN8/OTDZW8SO3Y80L689kmNiEkAiTUPUQcRJt36RWI0kdbjgT6j7p0EQIl/MewZZjZCcGuY/vMzxLNPE7/DnN7b5ryI/JsEYAwW/MS1hZltD6EX20l8Y9vmzZzE99kKwAUAwaxpEKzEP4SI1zIfdNtQU2+zABgsENwafBwaIdoc4iliNgCDAcHsahDcGuxj2NCC/FQOJzTvnQA4WCB2vLcYX1gNyjLMrdsZgNMDghnX3gl207jnGcP7qLYIgIoAMfZiUzz7GBbdFgNQmUBseC+2EZ88xr94AVAeBB9hfTvSl9K8AVKK1daqAFpWUvpqBVJmW1urVkDLSkpfnAokJKgACZOtLlUroKYlofPqK/AHAAD//5HbztEAAAAGSURBVAMALPSxYSkgbtMAAAAASUVORK5CYII=" },
            { "save", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAABuElEQVR4AeyYX04CQQzG0Yv4ZBTOw5uvmqiXMD54BTHRRx85D2LiXdavISRQ5l+nhRmgpMMws9Nv+2uXye5ejo784wCtC3i6FRiGYYq2QDuU0bnupBVNVeAVYmO0Qxmd6xvZupecMAUwkQgZrv2UQKQADGMSSxHEY4lXMcAF+3Bxdjg75P6B8QyVeArMb00VA2x57WfwEJB9B0TyP9ENAEr2BYBnNG50OUV3p24AKGpAzNCHLps3zAetKwCKEBAf6HklrjAXtO4AKEpAUCXoZ7Z1CZCNemOBA6yTge1OZGs/bX/WFVhos7fhX62lqcALAvhD09ovBEgLndyqAbDVzdGu0bR2C4G5PPSVhwBg5dDbtwO0rkh1BbDp0zPzEr3WSGNam4hqAJyQnplv0GuNNEirSkcDYPnMXK2lAajKmLWTGQD2cpFZgZgBWAUk1XEAacas13sFrDMq1fMKSDNmvd4rYJ1RqZ5XQJox6/XFFeA3/TwQfjw3zvnz47FxMUBMoPV8CqD6Xc3InioaSwqA3tUs7WMRK/7Ag2JBt2tRANzc03ufMfrWNkEA0fdGUYBd1j5nHKB1Xf4BAAD//xD6XjEAAAAGSURBVAMAH7pwcAnrX6sAAAAASUVORK5CYII=" },
            { "record", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAEqElEQVR4AeyZaagVZRjHzxSRQh+KPpQWbdpi5aWgLCo05UIUYeWtoOhDxG21gkJx/SS44AKKoLjjBxVBXFBckCsuKG4f3EVRVMQNRAVB/aLX3//OmZf3PuecOXNm5lwRjvz/8yzzvs/yzjhz5r2PFB7yf40GHvQFbFyBxhXIuAK53ULt7e1NcBRsg0fgRRhBunw6NxJn34x1u+mZG6CYv+FZIh6EE+Ag+BbsASNIl0/nJuI8xJwz8A/0TEjdAMkHweNknwlfhLXiJSbMJoauTH/0VKi5ARI+CmeQrQ2+AbNCV2YbMafBmuupaQIJnqTazfBfWA5bcQ6HH8PesHuR0uUbgb0NlsP/ODeR4ylkYiRuoBh4L5EHQotZOHoEQTAwCIKpcBc8De8UKV2+ydifMvY5OBdaNOPYXcyFWh2JGyDUKvgq9HEIozdFDYWX0ROBsRfh7wx+HR6BPl7DUC5EdSRqgBVZQqgB0MdKjA8o5DQyFZh7kon94BroYwA55/mOSnrVBgj0JZN/hD7WkbwF3vGdaXRi3IZfM3c19NFK7s98Rzk9tgECBEyaBn3otvnOd+Sk/0AceztNwReL2AaY+TPUPYnowF2OQ1ixzCtPnE4oxvweZzuM0JdF/CkyyslqDfxjJs0hUep73sQqMYmtF+N8c8LW0Ol0xQbo/FlGvgsj3EQZA+uN0SS4BSP0o5anI8PKig0wcAj0sYIVuuE76qGT4ypx9YRDONha3Im4Bt53o0JlZyi65Ghz2VpcEXENPO9GdSiFY6HokqPNZWtxRdTSQOI3rYueXrG5UjXwgsl/3tj1NHNpwH8eq9jHdegi3jN5HjO2M+NuIbsKz7hZ9Vf0CPez2FrcubgGrrhRodKVDegTNMwaHi+FovQY14Dt+r3S6XXz2Mdmqgb2mfIqvkzMuDzMb0wQW4s7HXcFlrlRodKfV3qfUK3fkRz6yPnEZFhubGdWbIBX+jlG+T9vNXYcvnpjkklwgFoqPsJVlBnfyZzeySoUWlihPsaXm0nsJoLp4wbhYGtwJ6RUa2Ahg/TZh+iAPnCWkqhbh5XjoRjT3rbHWP3FcWliG2CyXmb/mQDvYFe8JzmXFor5ppn8p7FLzNgGNJom1iPtR8ZgVmw11L4Pp9ODGN3gWiIMhj5mkXu77yinV21Akwj0K1KbVgiHr9D2kNxuteBOBubqc3U/o7VxgHBoI+dQZ8UoiRoozm9B2s9J7TKfpJA50L49GV4ejO0JtW1yghHaWkQ46P/ct86qoiRugBW5Rizt4exAWvyGQ1vo2j4fRnEfwV74CsgnYC8o33DkFvwXYCu00C3zIbkSf/klbkCZCKwmtLW4SHYZavtcWyH6ojpFsXoI6Fv6FGPlm4zUfEQJFFNbk9dLzsQ4ampAcWjiLvwF/QuoXQREJhxl9ueKCe3PaE7Fo+YGonAk2wD12PsLn/7AgagJZxjdSoy34Ub0VEjdQJSN5LPhy9jaghmL1N8NtKrRL0htgkmXT+e0bdLEnFfgAsZnQuYGouwUo98s45HNQRBoVXuiC905SJevGX0iPBzNyypzayBrIWnnNxpIu3J5zWtcgbxWMm2c+wAAAP//IfCDgwAAAAZJREFUAwBgpmBwZUmnJAAAAABJRU5ErkJggg==" },
            { "stop", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAABzUlEQVR4AeyYTUoDQRCF1UsY1K1gXAku3Sl6gJilARfiMZTcwZ07jTuDuPTvCOJOxX00eIn4NUyGTjvSNfRPDFSoR1cN3a/m1ZtsemFuxn8qYNoGqgPqQOAE9BMKHGDw8VoOjEajDuiDAYgdhtNwH9RRJRLAmzbAE8QXoAWWQOwwnIb7kl73YFHSQCQAomuwDXLFLo2ugDe8ApjEESxbIHfs0PvQ19QrAIJ9YIdxozkf+UeDJjDcLGWYT6osqhKJgA3n4Anv/u48Cy4LzlOHaNOpf5USAQ37FI3e7DpmDverw2f+2M6jyVIiYPLEP6tUwLQNUQfUgcAJ1PiEAjslOq4CEg1WTKsOiEeVaKM6kGiwYlp1QDyqRBvVgUSDFdOqA+JRJdoocWBo9+aqY92uY+YV3F8+fomAF4ekS6NV51lwCecaJF1gx7NdVOUSAefOwTb1Bw2jBpzmtsNwk5ZxVmZ/JF4BXHXccNbci7JkjVt63/k6egUUBObG+LHIcywPNDkG3hAJYBJDYC5cOzAaRz5ZY8cAwj7o0GsPfJN7QyRgzAJpD7TAMogdKxC2QW/cT7LWEiAhzL0njYCMKlRAxmFXtlIHKseS8eHMO/ADAAD//9H9llwAAAAGSURBVAMAs2D4YReie0EAAAAASUVORK5CYII=" },
            { "camera", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADtklEQVR4AeyYSWgUQRSGJyqIy0UPcQGJcQFDvCiIC/EgEQUlhEiiouQicceb4EFBUK+KB5EYEkQJSDC45yBBEUSiFxUxEcUNBY2ICgqKXsbvyXTS/bp7uqaqZRKY4f95r6pevXp/V01PT4/JjPJPSUCxN7C0A6UdcLwCzkcom83WwDewULxmwjLH+jNOAihgCgVchbNhoahkQk8uB64dnASwZDucCm0hc1ttJ8s8awFcuSYSbICu2EiuBtskVgJYUI6OvnIXyww/FHsJ+tGey+nvM/KtBJBZH52v9O2EpmghUOZg/sH6KIUEcCUa4QCMBUvqo9PCxf9GvxFysVqwHKXYNRmQmhr1AiEBBByBVdAUXRR02TTYi2NON74QYwSp6bCOjBIggTourv2Zgb3QFnuYaLxzxC6EAUQJCATkabxirI4r+QVrBebKBVjP5LfQCokCWCQO8xh44F+VczoW1sNjUH6kBrHCG9ijsA4G1iRHH6yEkfDnj/IDyaICTPsobA6xffAKPAjXwWk5ylU+hH8N3iW2ApsKUhFAQTuo5glcApOwgoCnzNmGdYazAAqRL+IZKpkETTGZwA7m7sI6wUkABcxl9RNQo5MOedSYgRWKL300AzhODnmoC3QW0ihAQDAtC8vc8/SOhx4GcWr5NjbDbjiYo/jNjK2Bn6CHiTjnyFWGtYIUYTWRSXKWhbhDaKTg20Mt5TDWS9dW6MdKGkuhFVwE6EXbKPBeUhXE3CLmLPTD+o+Ni4Dl/grw9RMmXbHQsXonYyfqARcBegfu6+R52g/VmL4Yaji+6SJgnEo7QbXzNbNqUOdSw/FNFwGPVdpFqp2vuVgNPlJt42aaArYbr5rJ7FOxI0JAA/fzWlVYqElMPZ1roR9FEXCdCl5APzopcLW/w+8zJj9kbf4+/GewB1rB+ghxP//JipvgH+hhOk4vhYoQeawux58FZXfkUeIm4+XQw2+cJnL9wlrBWoCsxsLyRT4gvqL82spjtTw2vGNM7vvShxvAfnL0B3oKbDgJkLUo4CRW3jL8wJriO4HyIuAU1gnOAmR1RHRgq2HiowQxd2BVbg6uG1IRICVQ0HtYgy9/vOWPfhf+RyhH6AJ2N6wmZhX8gJ8KUhPgVUNx/fA03Axnwgq4BbbCAS8uLZu6gLQKM82TKIBbYFGRJCRRQFKCYo9HCUj9nKYoMlRblAB5//gyxUXTSvWcRFIbZhghAdwp5A/4fKw9/s/MBaQNvQwOCRjWNjq8koBi71NpB4q9A38BAAD//67oG14AAAAGSURBVAMAiYbjcObdJP4AAAAASUVORK5CYII=" },
            { "sun", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAE+klEQVR4AeyZSYhVRxSGX3cCIcPaDBAkE0k0mwRCBpJFMCQQCRLSEpPoJmiIkWQTIYQI4oCuFBfazogDqKAiDgsRRRDRlYqz4kpB262CoiDt97dVUPe8qju+qwg253/n1KlTZ3h177t1T/d3WvobHh4eABccBloK02mtABKeA951kIzYe2qzgDFBuqEcqJuLbRbQPLsSHp4WUOJLatUkuQP8eowHZ8Bp8GurWUScKybw8b+LmIyoogWw8BlmV4Ox4AOwCd0f8EdCLtYmgvn4S9H1Me6iaAHOathxz5bjpEoR5/xCeCgzTJOLsdxYRJOXTbSAvr6++0zOBJZUxHSrTIxno78AzgPJsHwiefm2yWvRf+Rkv1Dp0w8yFmzGYgawNEigSVZpx6zfBt4HY8A2O2/H+PwZ3SCwNIP1W6zSj6M74CdZKIf6VrzK8wUETG6rNyrL8aV7blHEfrrLITL1UJVbgExwsAJui+hZ8vj2ZC8RJa/Yfj7KCwvQKlfENMkO/6CzAd1UdYYv3XN/ByunoStMXvalCpAhDtcATzuks9ClACaA+WAvGHLYA58HvgfRmDjeDjytsb5T46izlHGensTeZP4o2An+B3r4vAwXxsNngV3gMLaj4T2hnhRAQr+TzSnwMSiizzHQE/Y3eGNqXADJ/0kWK8GLoCy9hOFa1lZ5MLKkmxoVQAJv4XIxsKRjwESUrzpIlo5hhhbh442MpuKgdgEE1toNxHsOeBpCGMedOAXoQTYEFyRPYe4bcAN4egFhPb5q/ywrCXzUIl3LQrh4gIQPhopQZm4/Y3uy/RLdJ6AWVSigy78NuooEj3RZGQU2B1CtAyF9Gg6qyP1sn+8eIEZJnYVYV+EzEyj6bDA2fmht7U52yKRUXtoBdQzUPfDOLdecbKze7sAxa5AzPm7m7JehacVUbMkxaG6OCohNltE9a4yeN+O8oT2GWF95azNzKkBn9csZbXZwkaFsYBk6mRl1Oh+acd7wIzN5wow1VMzCvPq5qfQT9w48Re8xETvP2wLCw54SyMNfZrKrAMUEhXlpB4yv0kNbwA/ceOOKVmMzAZtvQUhdBYSTeXKTAnbj+BIISS//X4eKUCZ5PchWhTpkvXLuhdei2gWwvbeJ+BO4Bzy9grCfRFWIjtWjkF8H2h0dJfYxPwp4uoswEV934LWodgGKRmBdRv9KNtDTVsdqHRuuMKfffekQMzQTH2czmoqDRgUoFgksgU8Ft0BZuonhVNYuhTeixgUoOomshasJVXiUwO4QULdCaxCbUU8KUAoUcRV8gaxOntoxW5GvA11CatGoMTAWm6/ANfQ9odIFcCNOBnqTUq9Ur4vRBEjuLBgEk8BrYDT4BawAyQ4dvsNe7OSo84iyVAE415vTRtbrMtE3nOxVYlOZ8K++UNiL3YhOMQt9FRbgHNl2X+0XkJyM7PlIbczCInILIHm979rklUOyV6nJquDSUl8o1YtVDkmXyQJIXr3KZZGVub1Kb896f57X+8SPXp/iFKEbXTe/NVmGr2QvNloAC3RN1upVBtEXIuvMLsxFLiSKqNyLjRbgItlrslSv0q0Ve1sfDqX/S0kRainqJ9ctHWHJey5aAE50TdbqVY6Ea/hBfBURHs+TvdhoAYqPk1q9Sq3t9OCD+IW9WIVJFqDJJwFPC3jcu9TmDoTnnlDuac1tFqCuQqX/Utap7AEAAAD//8gHflcAAAAGSURBVAMAYp7pue3BgpEAAAAASUVORK5CYII=" },
            { "lock", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAC6UlEQVR4AeyZTWsUQRCGd/UgqHejBkRFMJ4EEb/wYjAHRYQYT7ogIiL+BUUJetWTFxURNd4Sv9CDnxcRvaggouI9unrzF6xPwezQUzs73bM1TBLSod50VXfXW11dEybTvaSxwH9iAm4BO53OUnAYXAbPQDvBU9pL4BCodNMqI2NhG0jmPXgEzoEDYFWCg7TnwRPwlrnraCuRShJgQadZzRewHfhkNxO+4nOS1izmBFjIWVZxHawAobKSibfwPUNrElMCLGAj0a8CLVN0HAWrE4gufZgZuQLH+kxPSWPgBAgsvneJtwx0pY0y2mw2W2AatBOI3mJsDPwBXVmOcgeuJu1AIosYyBEneZYFqKlMsOA3qaUUxl7SdQy4shdjBxhILAnooDdY4DvfKpjzmjm3gSs7XaOMbklglwr0QNlFpp6rK1nkmxmzJKAr8CHDXGx8UsN6M9Rwf9OSwLBLy6Pxz7WLdOb+UuMZLjVWaFoSKCSuazAmUNdO94sTK9BvZ+rqL1UBXvktMANm9QLpKyU5/rMQCPdxPVZkByUA8RCQN6j87zMO4RpQtQincN8j1gsg3xLeGEEJwDIN9oG6ZD+B7gOveBNgJ07BsqfR4He9MkrsE76Q3gQgOAJckWqM8DatVAgwAoSbJhV5pFIjTwlJYKtyvMDKf6g+s5lwXlRE25TdY4YkMOR6Eei7a1epw/1N8ckfturKmiEJZD3mmRUTmOuCxArEChh3ID5Cxg00u8cKmLfQSBArYNxAs/uiqIAcmac7xUfGltSoWMnh/u0LEVKBz4pkkkCbVJ/ZhHMzJJPAlY+ukaeHJHBTOU5g/yRgpQKnfGcIN2oq11Ktj+JNgI+Mh/jKiQRNrfKY2M99Eb0JJARyVvMq0f2NfYbc5MjNp5cpKAF2Qu665KhD7rmkIvp43BsoYIIcls0wT+7Xxoj5F90rQQl0WSCdAuNgLahahiGUO7a828zuEnraUgn0eM+DjpjAXBfhPwAAAP//AhlDWwAAAAZJREFUAwDz0zpw2NL2CwAAAABJRU5ErkJggg==" },
            { "coin", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAEsElEQVR4AeyYXYgVZRjHd6uLtGiliyBSCtcuQwXDTEEivQhKXS+sIIKgIrLIiMrUPslML0LDMKnbyIjUVLzwA0FdFT/w80pcQRT8uPELVPRi/f30nGGcPTPnfc/snkU48vx8P+aZ//M+Z2beZ2YfaLvP/7USGOwL2LoCvb29k2AeLIdVsAUOwmm4XsG+c5sZ66PvXPovlr2C0VeAoM/CbFgLl1nADlgIs+F1eBnGwHB4uIJ956Yw1kffRfS70bgEq+EDGMlclAUngPir0I36cVgO0+ExKGsdCHTBCughxk54hX6Q1U0AsadhH2rrofQlR6OeTcRhIzH3gFeOYb4VJoDAS5x6GMZBnnkbreXgfHgPvDITaDthSAX7znlMnwXM+4Ncoc2z8Rw4zBom0eZabgKc+AxnuTAvMd17bCujL2Fce3v7MOiCn+BPWAd74CTcqGDfOY/ps5D5aaC2C52Llpo099jjjNazlhG0NS03Abz/guw9/i9zowk8BZbAAcalDI29sBh8wMciZgyaxIbR+xtqWlEC/jLpk94hyCw4kp7szz7ah2AWmp9A2rJrSY4VJfBg4nW3c+1u05T/L2SiPJQZJ8OiBBKnSmcl9+I38FRl3O8N2sPhO4RXQpDFJOC9+D2qZwiyCT6Hot0J1/qGxvPwBWzG+zR8C9lnj6naFpNAWmEqgyWwj8AXYQ34OvEu7TR4Ae5UVdpHYSQ45zF95jO2kl9CYy8sBh9imjhrNIF0FK/MDCZ8nfiD9n/YDVbVXtqr0APOeUyfHxlbE9xG6TZuMQnMIcyA7UBoV83Cmd2Fqsf6tMEJsL0tg9EojIIPwSJnFaZbytRYg4Kao4gxBn5lHGTBCVTVEO+BFWD19fax1Psa8Rs+/4AV9RDtGbgB18G+c1vo66PvV/QnomMln0mrprca0+EWnUBWmsDd4GvER7RvgFV6LO0IGAJDwb5zU+nro+/P9Hdl9WLHEQnESjfHPzgBtr23mrOktjZivR0aKzgBBH9H2P279NaHVk1DvwN8nvy4qemTnYxJ4BFOdv8+S5Cl0AVPMFfK1AC13HnOImaMobRBFpNAVdCPFPfp1UycJ7ivFv/Rfg3vw3Sw6qYrcSdzE2AG6OM7ld/B7k7n0VHrY1q1acKtkQSy6r7czWTyB/AlzPpg1U1X4hMcc8dxv9fHdyq/gz2XQ41bTAKrGg8TfWZwrOAE2LPfZBl+ZvrO46/JsF9NTbWtGcYKEg9OQDWSOAULwAraztxkcNfYQOtb5THak3AO0h/s9p3zmD76+lE/D7/J6Glqqu1zwXSYRSWQlSTqdrAKv0Y7Hp6DTngSOqBq9p3zmD76+lG/CIftWd2YcakEYgINlG9RAl7yJC7b32BWYutDspZ0pyiB/WlH+oNZif3LIEvoa0UJLM24D2Ylzq4lWVpuAjxcvtdb3hPnSsdq2cxK/Atr2VaJ3afJTUBPTnShn9K/BXlmNR2ISnyTgHNYw2e0uVaYgGch4OXzzyfB1dHzSmIs/+66rJ5O3QQUIIkjYHVsWiUm3lFj1yMogaoIoq1KzI/QqsTVO8I26hbyhCCa6NRKoIk/ds1QrStQ82dp4uR9fwVuAwAA//+NYm4xAAAABklEQVQDAJRZUH+dLDlbAAAAAElFTkSuQmCC" },
            { "layers", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAD3ElEQVR4AeyXPW4UQRCFd8mMRIJkE9omQeIAJKRIxCRwDmMiEBkgIWFfhJCjIBAJMg75ix2a9/VuDd29tbOzMzUIS7Oq2pn+q36vu7qq59rsiv8mAt4GXl5e7kiPpV+lX6S873h9h9aF7oCAAvypQJ1J30lvS+9IeT9T+5E0lEgIAUBJDfiJAN+S1kLdqSpDiQwiINCs+JFAfZOuA66mQkKJ9CKwBH4sWABnVff0XstPVdAH5V3FQnIivc/IVgSWwM1V8GsP+HfBBPT+fD4/RVXel1JHm14LgQi2erlWJwIV8HWuAjhAHgJaemEweZeyU4eqow999VoIROizFZFWAkOBF/BUEIkLKSDDiLgEooELeyEiEUakIDA28IKFChFEGgIC/0Q2iSrrfJxIgv+u+LjGDRKHCHPVNvMzct8aGwKqeC31osoP1QPcokpzOFUfKhkRi1rMXc8BkQ9WmRO4bpXVk12ZV3X/osiczO3N1RDLCTxUTy+83VN97zitsZ1FbkxmJ88AnDmZux4P+JdW2RDQ9n1UZVh4k63OkgHnEsgZ9FyZxcWVD4T1vRlvCFChhrDwhr1N6gDHv+thBtwNHgUBGzk2kQjghtUlYI3RRCKBG8ZWAtZpKJExgBu2TgQWnWczhwgRYVb98GPuO1zKngv8C7UTVTictKlYCDY4nK6PFz2dQkNAEz2W/pZ+krbezzMiB7LJ5OsyJ8nxlfp4UYUxjCWqcO1emyCFh/AKJrCB8ZFsJmkIqPRGelN6V0oMZgVbv2EzIm2ZU+YKsRXfmNmXwPO8ADYwvjWLOYEbVrl8st24wrkMwX7tx3hGhB15pvGsrh6FUEdb5xXX6HMprlfvYIMlJ/BAnYm5ehSyq9I2O8KEtiO/NBbguAorfgJZ1bmihcJV8hVn7rovGLk1pPqGgAyHZWLZSglRz13pnrSLjwO8SybmsH9O6PXXENC7F2VgS1Ou5lobz0g+yHvPVtyAY7vuCgZ2EOArC1EQsJFasbSCKo9yN4oALmxJXAKpRX/RRCKBC16SVgKph/6GEhkDuGAl6UQg9dSfQ4SYrpZC8GPCL2dkysQszXIHyUVTJmZBVjRzrSkTy11IaNxeyf5TJiYL1i6TR5nW22o9sC5rte3uM2XienFSOTucIVeKiBVPwLK/TolsKJExgBuHTgSss0Pk/8nEBrLLMyNC3Oeqy0fL36GLNw57yDfxwpz/v9UO1CYyIvYF5kWtehh9IM0X2sr9vu68qTyIgBnPiLQddgPufpiYrW2fIQRsUodI/k0cCtzmDCVgRo2Inp2+iW1cn+coBPoA6TvmDwAAAP//wM6YDwAAAAZJREFUAwBaYGmO5DoxQQAAAABJRU5ErkJggg==" },
            { "eye", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAGbklEQVR4AeyYaagVZRjH51So0SK5UJEhfcgo20Xjes1ciDK1oKwPYaVZmqURSomVUkiRKWVppVmabdBGpjfNyC6Z96qttNy0PrQYFZqRZWXZcvv9x5k5z7wzZzznzBURzuX5n2d5n+V9Znnfd+4B3n7+V2tgX9/A2h2o3YGcV6D2COW8gLnD2+wOtLa2dgBXgXlgOfgY/BJAsmxz0eXTIffMgwS5G2BC/cHj5NsKngA3gGHgFHB4AMmyTUCXzxZiFoJ69FxUdQMU7wPeo/pb4GpwGCiX1Ng1OK9VDtAHuSqquAGKdQPPUW0D6AXyknJsIOczoFulySpqgAIXUmATuAyk0dcY7wO6I3o8jkAWJMv2APpmkEaXY9xEjSHwsqnsBkh8O1lfAYcAS3+gTAc9C4XCcYVCYTJYDJrB9gCSZbsJvTu+J4M7wZ/AknKvoNYt1pgl77EBkrUHz5NkBnDpdQw9mNQM8BlyWYRvC7gD5xNAI3BppmqC9u6Aq2c2QIJOBKwDlwJL21CuYBLnge+QqyJiN4NBBOvx+hluSTWbmYMeQWuPySUbIFDdr8T7DGDpS5TTKPw0PEHE1YEFYA3YFkDyfOSzEgEYyLUYdjr4Blg6E+U14jQXxCSlNkCA7Etxd5e3z7HVUfB7eIyIaQfuxbgWjAVng84BJI9DbsLnbtAOOUbk1MvdF6MuECwizWEpMYXIYgRN1KiR+BjS+cCSJt+PQtqwrN0j+UEYNPGb4aVyMuQdyM9U8HYQg1gkcuvCqAnVKg54nuay0BpCOVGMxNcyOBpY2oIykAJ69hETpBeyd8Ja2qCrOi1tmBqqdS5j4rCIxjA3d15erAEcjsb9fmBpB8pgEv8ATxAxmsytzsAy9H5AL6Ag+VV0S9OI1SZmbb5MrW8RBoPfgaU5xGiOkS3WAFa9mFqLEX3axe8QErZ4HlI6jcdsn88G/C8CTSDcByTrLLQC35AUo3cl1GOc2BYMw8E/ICQdQTTHUC/eATrTRLSkRYMIU0jUBM8ireV2/C6rOLK7l7ixMXdqa49w7+4g5qrH3Pe1d0Avl28MfhpJMCeQs1gPZ/BTR7fqF1ZBPhFkEnOYhYN7EW/D5pNtwDeYHx0RjFpS/NsZSSyRzrhV/7NKhqz3MHXYNuDe3qHcqhtTo+LGD+Oql/VYuGPvO7EJlTlMwqhlFBZRNNeoAW6V1tk3I5fdwiwS6CS5W0v//cgxu8+sHXaXTrd566v9ZSCGmcDSKuaqDyjfFjXga543Ev4rCEmPw0qa6BkaUrgat6fKYfg3gL6gI+gMJGsZtUflneRaBFKJGNVczqA2SZhPmpvm6Cv6iTVAZ1rrJ2rAQF9aq0kYW3/DcWK09etoHJrEh/KjF287XJuf5AuQLU0n9itrCGVqHYu8GtglHdUbT4zySfYRa0AWHJ6EzweWjkRpJPFR8DTSSqEvtLSxNJtOuO6G6ftRQ7U0eXHfFvw8zNyeDeSIJRoIRq6H200H1X8536XASVIsSPwven+gw1zWyiK/e/AbEMQgFoncWlb1Yh9ftPrSMvz1zwJfsT+pDeDcitMlYD2wpG/W9RTSNm/tHjG7wBSMOjY8Cl8DdMb/CS55Abwen6lAOzxqkcg5AO0dcAywJNsIa7ByagNyoIheTD237iqjd+INCs4GB8vXgrh1YBw4B3QCXYDk6+CJx0w5wGxyaNc9FG5Jq5Q+mty9JvIp2YA8KKgrWIesDxtYjCajbaS4rjhi5USsrvpGIpULFqNVaLpjWggQ0ymzAYXQhJY7rSqPSHegD3Sd7fXFNZIJlfxyCuPkA64E+n7QVVeOcDjkWpp1iFTt0JbK99iAomiiFejF1jMukwt9cT2FcSsTexCMAvVA+0AXuD4zR8PnygcsAaU2yEnUGgv0HuKWTWU1EKYgqVYZneHd9yJ00XFX+4i+cXWFdft/ZLAZaNPSvxblg5ogrT69qJG6vCa8A0NFDSiGAh/A9aGvI60mh5qL9Ik6hgy9g9yI5VPFDSg1hfRI6btZz6/+S/cy9r9AuaQV7iWc9a+T7uRbBMp6ZIiJUVUNhBkouhO8AC7G1hXo/zsPwRvAJ0BnF0GybPOwjQJdiRkBXgRqBlN1lKsBW5KJ7AD69+EE+HBwKugYQLJsE9GXgN9sbB65zRrIM4k8sXungTwzqjC21kCFF6zN3Wt3oM0vaYUJ9/s78D8AAAD//1kKSb0AAAAGSURBVAMAHNQvf/SLSmUAAAAASUVORK5CYII=" },
            { "trash", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAACnklEQVR4AexYS07DQAxtWcIGkOAKQCV6mt6Hz76CgyAug1TKHjYgfgvYhveipkzepI2TOG0qtbKTGY/n2W+cpJPs9Db8tyWw7gJuK5AkyQg6hVYVzhk1raBHBa6QxAm0qnAO51adl/P3IDDIIVbrNJmbRvIgkAKt6+BOoF/y8ybqTsA7wTK8iAAeJXyqTHA2iQYom1TVP8BjTtFTKyKAANfQM2jXhDldalJFBBJ16lA/yq2IwAUSnkK7Jg9IiLnh9C8RATxE7qBn0FTg+gQN5TwdaOGAIENoKNMgzBDt+3CQ7YgAjaKf0j+Qvmf3UMA0tgz3enUIaJAItIFBF6cVAhqkQb7RVMVuhcB+FBYGPK/5/8EdJjV6XpeNA4Ki2B80LlPPS4g7S+4wqWxrXNo4RmVbx9nXy/OLxmVah4CWOcMPd5Zh2zpOP8X2vISIn6oGSY1OB8XeEuDC6iroKtHHSxXb5SZWEA3ilTxxFFsXjz459byJc8A1OyshcFwzOcu0I3FqXgFsoH4AGm5j+/hT2oXNVYC5B8A+NJMEsX+zzqKz5RLi3DceAtVSB0O1m4r5akGyEtBSajBLrDIfxdSYhfO7REC3Ea0S0GCFq1PRuPEV2BJYVnG9HnW1OHfCw0zD9szUC21hOxtXTI2Z+eXO1ptYtxP64kFQfjHg14xHdNjGKSe0LRtXzPfc7AUdKwF9sYhuYvzpZF8zBmxrPNqg/NpROA5/xfyGrVSsBLScWu7SQAYHxdSYhRBdJqCXbSMCCqarVQhe0aiYrVbgFJsvVwFZvuzjNBdXAlqBeZQWG34E8PTgtvbFnGxzx+dZzFIk601MoBscTKsCvybCrfvYCmAmgBUZQw+hbcsxAty6E7ACrtrPXIFVJ2aNt/EE/gAAAP//8jS60AAAAAZJREFUAwC17opwrtspkQAAAABJRU5ErkJggg==" },
            { "clock", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAFsUlEQVR4AeyZa4hVVRTH7/QOy7QsnYossIyg1xiVWSJjUGhlkUTYQzIr6DVpEUESPaiIpNFKsjL7kvQhKrASkh5kD/2QUxDSg0xnPpQVmGlkD2r6/e89+7Bm3X2u59zHDMEM63/W2mvv9Trn7H3P3rNX6X/+N1zAUD/A4ScQnkB/f/9UsAxsAF+BbSCQZOnWo1gKzgt2jfKGngCJtIPHwTYS+QDcDM4CE8FYEEiydGejuB2skw2QbTvtuqmuAgg8Biwh6hZwF1CCsEIkG9l+h6/FYEwh62Rw4QIIdAu2faAL7A8apQNwcCfow/d18EJUqAACLMf70+BAEKPVKK8Gk8B4oHHCschngGvAGyBGGreSGE/FOrN0uQrA6SjwMU5uAp62o1gADm5ra5sFVoEe0Af+SNAL3wheApcw9hAgm1/gnm4llubIKN8Ra+cqAMPXwTnA05MoJpDUEvAbckokMRto5RFmpx0IjN0JNIcm0HwGeNIq9apXxtp7LIAkVmI4DVjaSaOTJLpA7C7SXXqAi1YeQTLNgYTtdqCV6wJ65BOWUiexn09bGULNAnBwG3Z+Ym1G10Hg9+G16CTTaWWjroj4WoukeaNVDTGl+UkOqcILmQVgqInX7Qx0t6cTUEW4rsaa+PwWD51AMWApdZPL0WnLCZkFMO4hsDcI9DfCDAL1wltC+N6K44uAYsHKpBweKUuRS7QAKj6BsXOApccIsMEqWiET4xP8PgEsXUVOmktWV5ajBdBzB7B9P9POvAv0NZsexqGWZ1iZlIvmY7lhL+qw7SBfFoSEL+LO7E7kljNi7SLIfcCSz6ncV1UAj+pMesaBQL8jrACDTVpC/zRBjyQ3rVRGVSpVFUDvpcDS29yRf61iMGRi/kUcLa+wlGalUiLECvBVvpmMHQr2lgva4drRJ3C4G/R5pT0kVx/b55argJ+GJPVKUB87VwHaaFTMK1fttipSA1cmoLaVC+D6bM7rScu3HZurAE0ea1TvpsV/1+jG6AdqC0XkLaTfJhKTY5P4RzfwCNfO27yWgf4VQFUKhWzNUYhdzmVb9TbkKUABZVwILIMfYaAPQm0X/atAV0k3Rk9EhUyRIoKmFHBaxHEuFUXsBkpS28tahWRtM093gXI9gR5ndKFrF25ShC/Ev6axV01xZuhi4HOLLqP+h+t83tXYq2b85hNNIcdhsRCEO7oMeQARc18U04Eln1t1AQT5DIvvQaARCHNB04gYeiLd8HYgip1EXE/A/UCgHxj4RWgEnnVn/Tv5IHdkn2DUak4sLd33uzivuXa5mVXAUnrtGqwtnY4EUQ8K6dDMrn7/ENVvb1HFv0ZLPKov6X0OWNJTONkqWiFz97Xy+Lu/nJyi+/CsJ6Dc7uFijzo0F9YS4Cj0LSF8H4NjfULbzw1tbhahj1JmAVS8A4t5wJJ+WN4jkH6grL5hOfH5Do78Ie/cJBe6qimzAA3FUKdjiyUbaMPfQ8DJRteQiK+pONAafzzc0qPkoFNBqxsg1ywgGXk3fB2wNJrGhwTWPzQORa6LsNcx/bMY65BMPhFTehfpXlCT9lgAd0CrkbZy650nndfoWHAziXSBg1x/ZpOxI4E+LTQxb2Sgz0NHK5cnsenOJm8YHYmjHUCHuy9GBugUWQe1u0hqNbgSdABNyBJ8BBgPJoE5QEfwv+JHr+ZIuKcVxJoCNMb3VbVzFRCscKpJrU+ArE3+xYx9GWwEvSSrp6dTa524fYpuFdAYWBVprV9IjBuqemooChUgPwTQD8opyPp/AawppP+vnZr4LuSwcAHyTqBN4FzkmaD6uBFlTpLtTHxNA5ty2gwYVlcBwQNB1wAtp4ehuwJojiipb5DtJ7I+n79Gp74X4Bo7WrZgDe26qaECQlSS0D8qXoHPA5PBRDAWBBqHcCJQ33y4xuqHMriomzelgLqjN8FwuIAm3MSGXPwHAAD//wvyIaEAAAAGSURBVAMAPdrTcM4tHBQAAAAASUVORK5CYII=" },
            { "flag", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAC10lEQVR4AeyYuUsdURSHfalCQLFItAgxpMweQposhCxEEkKSP8DGSgiErBAFCxtxAUVFFERBbLSxUgtF0UIFG8EF3CoXLBTEQq2f30EujMOMc+/47rwRJ5wvd5lz7z2/c5+PN+dKzgX/ZywgnU4XQRm0wxQcwSI0w6uo82EkgAB/EOAqdMB3eAnX4AH8gkl8dqEa8hlbNy0BBFMAE0TTClfhLLvBw0rYZI11IYECCOIbwUjW39CaWC7OImSbPergOmMtwzcPGkGSsEH7xW9hoAAWtoH747DO3B94DXlQDJ2wB26Tj1g5k1sE0gAF9H2N56U8XIO/cAuKoAs8TUfATdfKHsaPUqlUM0zCAYxCGUiWS3g+D26Tj94/JncIchz+w2PIh89QCws874ZCcJqvaB0Bzo1yCLIUDk5NOgY864UnTMmtjNN62Vsm62EO9mEIKuAhGJmxAN3dESG38h5/+WqdobVi1gSoaBEyDc8Zf4RZ0LFhHSfxsS5ADhEQMQLP6N+H3zAIm3AI8hVdQyvfeIX4faKvZZEJUNEQ3BK0wFe4DbnwDiphAHaVr04buQCdoEx8EgEm2bLhm9yAjaya7JncgEm2bPgmN2AjqyZ7GtyAybbR+V4+Abx0qKpEC/1pUFWJJvry0zm69HNSmBuQ92OpSvxk/QuQV0apSsgvzHhWJQjSafJq6By7+/GqSrijMxifqyqhe06Yj5DsHUVVQs4JJIyAbFQltvyUGAvgjSkbVQmpK3lqMBbguYvHJELPW5VIs20v3GGvPlpPsyZAncbhqirxgTnd8soovk9ZWwLy98bQ26wLUMcSyBhIeUWnKlGMrxS91HLfNjIBKgICS6oSKhnSRn4DcmgmSQRkMpth9kpuIEzWMrkmuYFMZjPMXnZuIEwkIddcCgFLjuQ4+47p7HV1bqCK8FZgGaRPEx8LFMCPr364C/egPz6hn0QSKODELb7/HwMAAP//SEi44gAAAAZJREFUAwDLFDJwsXQsiAAAAABJRU5ErkJggg==" },
            { "snow", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAFMUlEQVR4AeyZy4sVRxTGexKSdUyIWSZxBNFkaXQR/4EkJIQoxCHbmKALHzgKrsSNICqoiO+l6IAzrhT/AUUcdaX4wlG3vl0rMv6+mSqnqrqqu/rWvQtBOZ9Vdc6pc87Xt+pW3Z5Pqg/830cCqQ9wenp6FbhjsCrlV6of5Cewg+IWGahPt/8ySAJLnHLdvqMu7w6SQHl1GRGKCbDGfwM3wQ3wT1tO+QDr/2ubf5u9iACFfEqC4+AH8CM4ie5f2qgY20mM1v8guiHGPUsRgaGhobdkngauHKeoNa5CfXQiJrIaWhQVryBFBBQAjIJQjoUKxmHxqKptPITwAUifjWICFHCabLUnjq5N1jB3rM2pzV5MQAko5ATtWpAra82cXP+kX40Aa/UP8BjoFN2anBkYKOgIqhwSKl6+uLcLdWwFt4Bq+iWcUSOAwy4wH+gU3cXEh2A141YxJNY1OK4zPg0usyZyjoBHjFTPYlrVtJfWkxiBLz2PqvquqqrTBJsEP9NvFAo8jEOMhIqXDXNalANcxeMU+Ba4Ms8dqB8j8CeGVyCUn1BcJPgZMEw/KYaEZ4/pXAfFBGfQXQRLQSiq6fdQWSNAoss4LQAHwBsQim6Wt0m2D9SeSOjcNlYMsB+/20CxaTxRDbIvoLZrnoVBjQC6CseXYAN9rb2ztKF8hkL2ByQfBZ8z7iSaA3SGPGDieqCYNJ4o92Jq2QheehYziBIwNhGZYuJKxstBjT26L8BuoG+sv2mzhML1pXAXZ81VDLqeKNdy5QZTniUYNBKwvgSZBNoDI+j0zUDjyfeMxijsKlhGPyqyAW1QHX76cgj9FHtEucBkaIyNswjYiQQdAypWH702lTXZVpvvCkWOW4Vt0U3QvwLkQ+OJYo0qNuh0OncgMJeQJPo+XohGG52mJlp2ofKvUGHGijFsYhpVftMTAYUn4XOgjSwi2mxSd4HmLFQM8KLLRNe3ZwI2CMnbNrp1tW32BrUTmtpiAk5wfQ0+ccaprnzkm7J30hcTYHMOA21QnaA5PxHloxN9nHmNJ3oOk54JkPwroBPyPolSGxRTUrTR7ysGCO9fyUmhoScCJNQ1W4XrBA1jaqxPRK2LmE52xZgyMTXuhE4ESLIaPCSDrrhNJ2jtTsNml67pRNfVXVeT7BOdOqosAhS9DBSfoJDoy4muwi0aCVC03aBNJ+gWgi2iuOwT1PjqB5OWok5hQnii01on+gQ1xK4c752jBJg0D2iD6oob26DuFXcPBb1+HzGzozlAl7mmq7ty36OW5NW9RgBnrdOiK24mhxk3SHS5uuuTmZln/6sRwHAOxDbodfQrSLgSNF5xeQj/4+sJuv88RTBQTKCv1hWYlIvGE9Wk2jxljED4w8FecZeS4JI3OzKgUL2Bi711OGpskVlzKuUAetKxq/vzOc/ZXozAJkzPwFPQ6YpLgXqtEnsDR6gZ0WtH+cwMmv6DhHt1Vz26guga702rEWDiefA1mA90bfYmpAam+EMpu6M/ZHwdVbqrGoDq+Yb2QuhZIxA65IwpSMsmp3gbTiQ0x457bosJULzWatOySRWn5aTfxil7lr6IAMXr7wOxZRZ72RvT7SRG0Sv2IgLmEYWvx/XWWS97jXm2Yf1KF5IoKl6RiwhQlP7AodukYgnR4mUQ8A9JbEYXPgC5ZqOIgLJQwASwogKrSoYEcDwBrOh3ccIzT11MIC/N4LwGSeCWU7bbd9Tl3UES2E55d4ButOrT7b8MjACLfBzoxewS2tqbun5RGRiBfhXYFucdAAAA//+n98pIAAAABklEQVQDAF/N8HD4wuEIAAAAAElFTkSuQmCC" },
            { "map", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAACkUlEQVR4AeyZu27UQBSGd5HoiDZIpETicUgoqREg0VNQ8BII0VNweQEqCO9CSxeBEigpzPdZcWSPZ22PJ9YmyqzOL8+cOeefc9nqzK3VNf+VBHbdwJvbgaqqNlVVvQDHoBHX6ja5nYEw5P+N7knIm9QBCC5IIToF78FD0IhrdafYJieDzxD/XS55CzoymsAIaYcs2ExKJpH/XnDHKprAOekzvsc4xCqNOknCZF7CLbL5ewlA/JjQfoIPwIv5XKrI+Q5G4ZrlfOklANUbcAcMyXcOn4N9EIo6z7QJz6bu9ZVDrkGfWAIPIh7/0H0DNel6vT4EH8EZuo6oA54dcmAA+vhXkQNVVDybxB96xxJo2/xlYwAHBPUIGFgvaGyigv0Z0OcIgwMgl5wsa3Gtbha/DGMJ7J0HMDloSWOAp06Gsz3QSDb/WALNRVf2WxLYdWtKB0oHMitQ/kKZBcx2T+hA9l2LEJQEFilrAmnpQEKxFjEtHVikrAmkpQMJxVrEtHRgkbImkJYOJBRrEdPSgUXKmkA6uwPMUOtReHgX+kt9Hwj5w31SAgRn0E6tHQOeQOZbAJ+OqDvB9ivQdvJjB/ZT+DuXjSbQInW+6ajdqbVzz9sdpu7GM8eJ2jaPHdFkEvl/dK9Zxd8HAqMm6JxRuL51MgG326n8zlFf69DGaAfaxlvWdsYBrXAsvsVstrrhv8989UvIMjcBAzXgfUiPgBNo4V+rGalrE943da9vyB8dMMcS+LXlFkmfcmbQw+8D63U7GX30xXVQtNF2kD9kiCXwCqM/QJG0qYRBf6ba0UpoHEJboM+2zmTxe18vAS78BDZAMWirOTloSWOArH4f4Csnn1pcZ/H3EohdfpV1JYFdd+c/AAAA//+CQBs2AAAABklEQVQDAPleRnDdCKhGAAAAAElFTkSuQmCC" },
            { "back", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAABtklEQVR4AeyYu07DMBSGHd4GgXgQVgYWJBY6wMoAE4KJF0AwtBsTCzxMEa8T/l9qKzc6Tuw4xzmVXPmoruPL9/k0F+XIHfinCsydwJqBmoHMHah/ocwNzB5eM5C9hZkTTJaBtm2vEL+INeI8kyt6+CQCAL7Bip+IE8Qp4g1tDb7VS7YAQAm/7JAWgeeaWQKAv8UkXXg0ucemaVpWtGO0AOC58+8C4ALwX0K7StMogQ28tPOEX6mQBiZNFrAET6ckAWvwSQIW4aMFrMJHCViGHxTYh2f3XRS/2uxW7lSCJzHgr9FXulSi2S1xvFT5w0IXXFSKoAA6vyAslGNAPCPE0icgDrDW2CfwYAR2DY4nhFiCApvnmYU4yjmexOhSpJxhlZ8AhwsKcAAG8rlGkuBJzIc5dps1egVIZl1iUMC6RJSAZYloAasSSQIWJZIFrEmMEvAk7ljvBC+xl502tZ+jBUiES+wHvqX7xCsewIq8WskSALyDhHSzKwLP9bMFOIkgcY822++FCO4HgFeIbfn2j2nWJ8mAJuDQ3FVgaIe0j+tkQJvam78KeJsxS7VmYJZt9xY9+Az8AwAA///YRFVVAAAABklEQVQDANZf8mHOgjZXAAAAAElFTkSuQmCC" },
        };

        private void InitRoundedSprite()
        {
            _roundPng = null;
            if (!_config.RoundedCorners) return;
            try { _roundPng = FileStorage.server.StoreByCrc(CommunityEntity.ServerInstance.net.ID, FileStorage.Type.png, Convert.FromBase64String(RoundPngB64)).ToString(); }
            catch (Exception ex) { PrintWarning("Скругления выключены: " + ex.Message); }
        }

        private static void Root(CuiElementContainer c, string name, string parent, string color, string amin, string amax, string omin, string omax, float r, bool cursor)
        {
            var comps = new List<ICuiComponent>();
            if (_roundPng != null && r > 0f) comps.Add(new RoundedImage { Png = _roundPng, Color = color, Ppu = 16f / r });
            else comps.Add(new CuiImageComponent { Color = color });
            comps.Add(new CuiRectTransformComponent { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax });
            if (cursor) comps.Add(new CuiNeedsCursorComponent());
            var e = new CuiElement { Name = name, Parent = parent };
            foreach (var x in comps) e.Components.Add(x);
            c.Add(e);
        }

        private static void Box(CuiElementContainer c, string parent, string color, float x, float y, float w, float h, float r = 12f, string name = null)
        {
            string omin = $"{x} {-y - h}", omax = $"{x + w} {-y}";
            if (r > 0f && _roundPng != null)
            {
                c.Add(new CuiElement
                {
                    Name = name ?? CuiHelper.GetGuid(),
                    Parent = parent,
                    Components =
                    {
                        new RoundedImage { Png = _roundPng, Color = color, Ppu = 16f / Mathf.Max(1f, Mathf.Min(r, Mathf.Min(w, h) / 2f)) },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = omin, OffsetMax = omax }
                    }
                });
                return;
            }
            c.Add(new CuiPanel { Image = { Color = color }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = omin, OffsetMax = omax } }, parent, name);
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

        private const float M = 18f, G = 10f;

        // окно по центру экрана с шапкой: значок · заголовок по центру · крестик
        private void Window(CuiElementContainer c, string name, ulong id, string icon, string title, string sub, float w, float h, string closeCmd, bool dim = true)
        {
            if (dim) c.Add(new CuiPanel { Image = { Color = "0.03 0.02 0.02 0.55" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", name + ".dim");
            Root(c, name, dim ? name + ".dim" : "Overlay", ColBg, "0.5 0.5", "0.5 0.5", $"{-w / 2f} {-h / 2f}", $"{w / 2f} {h / 2f}", 18f, true);
            Box(c, name, ColWine, M, M, 40, 40, 12f);
            Text(c, name, icon, 13, M, M, 40, 40, TextAnchor.MiddleCenter);
            Text(c, name, title, 18, M + 50, M - 2, w - 2 * (M + 50), 26, TextAnchor.MiddleCenter);
            Text(c, name, sub, 11, M + 50, M + 22, w - 2 * (M + 50), 18, TextAnchor.MiddleCenter, ColDim, FontReg);
            Btn(c, name, "X", closeCmd, ColBtn, 14, w - M - 40, M, 40, 40);
            Box(c, name, ColWine, 0, M + 52, w, 2, 0f);
        }

        private void DestroyWindow(BasePlayer p, string name)
        {
            CuiHelper.DestroyUi(p, name + ".dim");
            CuiHelper.DestroyUi(p, name);
        }

        private void CloseAll(BasePlayer p)
        {
            _menuShown.Remove(Id(p));
            DestroyWindow(p, UiMain);
            DestroyWindow(p, UiWelcome);
            DestroyWindow(p, UiPopup);
        }

        #endregion

        #region Интерфейс: табличка

        private void ShowWelcome(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;
            var id = Id(player);
            bool en = IsEn(id);
            const float W = 900f, H = 620f;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0.06 0.015 0.02 0.80" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", UiWelcome + ".dim");
            Root(c, UiWelcome, UiWelcome + ".dim", ColBg, "0.5 0.5", "0.5 0.5", $"{-W / 2f} {-H / 2f}", $"{W / 2f} {H / 2f}", 20f, true);
            var R = UiWelcome;

            // логотип
            if (!string.IsNullOrEmpty(_config.LogoUrl))
                c.Add(new CuiElement { Parent = R, Components = { new CuiRawImageComponent { Url = _config.LogoUrl }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{W / 2f - 36} {-24 - 72}", OffsetMax = $"{W / 2f + 36} -24" } } });
            else
            {
                Box(c, R, ColWine, W / 2f - 36, 24, 72, 72, 22f);
                Text(c, R, _config.ServerName.Length > 0 ? _config.ServerName.Substring(0, 1) : "H", 34, W / 2f - 36, 24, 72, 72, TextAnchor.MiddleCenter);
            }
            Text(c, R, T(id, "WelcomeTo"), 12, 0, 104, W, 18, TextAnchor.MiddleCenter, ColRose);
            Text(c, R, _config.ServerName, 32, 0, 122, W, 40, TextAnchor.MiddleCenter);
            Text(c, R, en ? _config.TaglineEn : _config.Tagline, 13, 0, 162, W, 20, TextAnchor.MiddleCenter, ColDim, FontReg);

            float top = 196f, bottom = H - 22f - 48f - G, colW = (W - 2 * 22f - G) / 2f;
            float lx = 22f, rx = 22f + colW + G;

            // правила
            Box(c, R, ColPanel, lx, top, colW, bottom - top, 16f);
            Text(c, R, T(id, "Rules"), 10, lx + 14, top + 10, colW - 28, 16, TextAnchor.MiddleLeft, ColDim);
            var rules = en ? _config.RulesEn : _config.Rules;
            for (int i = 0; i < rules.Count && i < 7; i++)
            {
                float ry = top + 34 + i * 42;
                Box(c, R, ColRow, lx + 12, ry, colW - 24, 34, 10f);
                Box(c, R, ColWine, lx + 20, ry + 6, 22, 22, 7f);
                Text(c, R, (i + 1).ToString(), 11, lx + 20, ry + 6, 22, 22, TextAnchor.MiddleCenter);
                Text(c, R, rules[i], 13, lx + 52, ry, colW - 72, 34, TextAnchor.MiddleLeft, ColText, FontReg);
            }

            // онлайн по режимам
            Box(c, R, ColPanel, rx, top, colW, bottom - top, 16f);
            int total = Real.Count(p => p.IsConnected);
            Text(c, R, T(id, "OnlineModes"), 10, rx + 14, top + 10, colW - 120, 16, TextAnchor.MiddleLeft, ColDim);
            Text(c, R, $"● {total} / {_config.MaxPlayers}", 11, rx + colW - 114, top + 10, 100, 16, TextAnchor.MiddleRight, ColGreen);
            var modes = _config.Modes.Where(m => m.Enabled).Take(9).ToList();
            float rowH = Mathf.Min(34f, (bottom - top - 44f) / Mathf.Max(1, modes.Count));
            for (int i = 0; i < modes.Count; i++)
            {
                var m = modes[i];
                float ry = top + 34 + i * rowH;
                int n = GetModeOnline(m.Id);
                Text(c, R, ModeName(m, id), 12, rx + 14, ry, colW - 80, 16, TextAnchor.MiddleLeft, m.ComingSoon ? ColDim : ColText, FontReg);
                Bar(c, R, rx + 14, ry + 18, colW - 80, 4, m.ComingSoon ? 0f : Mathf.Clamp01(n / (float)Mathf.Max(1, _config.MaxPlayers) * 4f), ColWine);
                Text(c, R, m.ComingSoon ? T(id, "SoonShort") : n.ToString(), 12, rx + colW - 60, ry, 46, 22, TextAnchor.MiddleRight, m.ComingSoon ? ColDim : ColText);
            }

            // кнопки: 4 равные
            float bw = (W - 2 * 22f - 3 * G) / 4f, by = H - 22f - 48f;
            Box(c, R, ColRow, 22, by, bw, 48, 14f);
            Text(c, R, _config.Discord, 12, 22, by, bw, 48, TextAnchor.MiddleCenter, ColDim);
            Btn(c, R, T(id, "ChooseMode"), "hub.ui menu", ColWine, 14, 22 + bw + G, by, bw, 48, ColText, 14f);
            Btn(c, R, T(id, "StayLobby"), "hub.ui welcome.close", ColBtn, 13, 22 + 2 * (bw + G), by, bw, 48, ColText, 14f);
            Box(c, R, ColRow, 22 + 3 * (bw + G), by, bw, 48, 14f);
            Text(c, R, _config.Website, 12, 22 + 3 * (bw + G), by, bw, 48, TextAnchor.MiddleCenter, ColDim);

            DestroyWindow(player, UiWelcome);
            AddUiScaled(player, c);
        }

        #endregion

        #region Интерфейс: меню хаба (P)

        // Меню хаба по макету ASH & WINE: слева разделы, справа содержимое.
        // Окно 1120×680 по центру; координаты — в пикселях от левого верхнего угла окна.
        private const float MW = 1120f, MH = 680f, SideW = 176f;
        private const float CX = 198f, CY = 92f, CW = 900f;          // область содержимого
        private const string UiMenuDim = UiMain + ".dim";

        private const string MInk = "0.937 0.914 0.906 1";
        private const string MMuted = "0.639 0.596 0.588 1";
        private const string MLine = "1 1 1 0.08";
        private const string MTile = "1 1 1 0.045";
        private const string MGreen = "0.498 0.831 0.604 1";
        private const string MGold = "0.851 0.737 0.514 1";
        private const string MPink = "0.941 0.651 0.694 1";
        private const string MWine = "0.639 0.086 0.184 1";
        private const string MWineSoft = "0.639 0.086 0.184 0.22";
        private const string MRoseLine = "0.941 0.651 0.694 0.25";
        private const string MGlass = "0.071 0.055 0.055 0.94";

        private static readonly string[][] MenuPages =
        {
            new[] { "home", "home" }, new[] { "build", "build" }, new[] { "film", "film" }, new[] { "arena", "arena" },
            new[] { "aim", "aim" }, new[] { "mini", "mini" }, new[] { "mm", "cup" }, new[] { "shop", "shop" }, new[] { "profile", "profile" },
            new[] { "group", "group" }, new[] { "settings", "settings" },
        };

        // какие режимы из конфига в каком разделе (остальные — в «Мини-игры»)
        private static readonly Dictionary<string, string> ModePage = new Dictionary<string, string>
        {
            { "build", "build" }, { "film", "film" },
            { "ffa", "arena" }, { "duels", "arena" }, { "ranked_ak", "arena" }, { "ranked_mix", "arena" }, { "ranked_t2", "arena" }, { "ranked_prim", "arena" }, { "primitive", "arena" },
            { "maze", "arena" }, { "walls", "arena" }, { "bows", "arena" }, { "speargun", "arena" },
            { "aim", "aim" }, { "facecheck", "aim" },
        };

        private static readonly Dictionary<string, string> ModeIcon = new Dictionary<string, string>
        {
            { "build", "build" }, { "film", "film" }, { "ffa", "arena" }, { "duels", "arena" }, { "ranked_ak", "cup" }, { "ranked_mix", "star" }, { "ranked_t2", "cup" }, { "ranked_prim", "flag" },
            { "primitive", "flag" }, { "aim", "aim" }, { "facecheck", "eye" }, { "roam", "map" }, { "heli", "mini" }, { "bedwars", "flag" }, { "raidwars", "build" },
            { "maze", "layers" }, { "walls", "build" }, { "bows", "flag" }, { "speargun", "eye" }, { "helimm", "mini" },
        };

        private readonly Dictionary<ulong, string> _menuPage = new Dictionary<ulong, string>();
        private readonly Dictionary<string, string> _menuTab = new Dictionary<string, string>();   // "id:page" → вкладка
        private static readonly Dictionary<string, string> _menuIcons = new Dictionary<string, string>();

        private void InitMenuIcons()
        {
            _menuIcons.Clear();
            foreach (var kv in MenuIconB64)
            {
                try { _menuIcons[kv.Key] = FileStorage.server.StoreByCrc(CommunityEntity.ServerInstance.net.ID, FileStorage.Type.png, Convert.FromBase64String(kv.Value)).ToString(); }
                catch { }
            }
        }

        private string MenuTab(ulong id, string page, string def)
        {
            string v;
            return _menuTab.TryGetValue(id + ":" + page, out v) ? v : def;
        }

        private Plugin P(string name)
        {
            var p = plugins.Find(name);
            return p != null && p.IsLoaded ? p : null;
        }

        // ---------- примитивы
        private static void BBox(CuiElementContainer c, string parent, string bg, string line, float x, float y, float w, float h, float r)
        {
            Box(c, parent, line, x, y, w, h, r);
            Box(c, parent, bg, x + 1, y + 1, w - 2, h - 2, Mathf.Max(0f, r - 1));
        }

        private static void Ic(CuiElementContainer c, string parent, string icon, float x, float y, float s, string color)
        {
            string png;
            if (!_menuIcons.TryGetValue(icon, out png)) return;
            c.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiRawImageComponent { Png = png, Color = color },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - s}", OffsetMax = $"{x + s} {-y}" }
                }
            });
        }

        private static void Hit(CuiElementContainer c, string parent, string cmd, float x, float y, float w, float h)
        {
            c.Add(new CuiButton
            {
                Button = { Command = cmd, Color = "0 0 0 0" },
                Text = { Text = "" },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - h}", OffsetMax = $"{x + w} {-y}" }
            }, parent);
        }

        private static float TW(string s, int fs) => string.IsNullOrEmpty(s) ? 0f : s.Length * fs * 0.56f + 4f;

        // кнопка-пилюля: primary — вино, ghost — плитка с рамкой, rec — красная; с иконкой слева или стрелкой справа
        private static void Pill(CuiElementContainer c, string parent, string text, string icon, string cmd, string kind, float x, float y, float w, float h, int fs = 12, bool arrow = false)
        {
            bool primary = kind == "primary", rec = kind == "rec", off = kind == "off";
            if (primary) Box(c, parent, MWine, x, y, w, h, h / 2f);
            else if (rec) Box(c, parent, "0.831 0.122 0.235 1", x, y, w, h, h / 2f);
            else BBox(c, parent, off ? "1 1 1 0.03" : "0.13 0.11 0.11 1", MLine, x, y, w, h, h / 2f);
            string col = primary || rec ? "1 1 1 1" : off ? MMuted : MInk;
            float iw = icon != null ? fs + 6 : 0, aw = arrow ? fs + 6 : 0;
            float tw = Mathf.Min(w - 12, TW(text, fs) + iw + aw);
            float sx = x + (w - tw) / 2f;
            if (icon != null) Ic(c, parent, icon, sx, y + (h - fs - 2) / 2f, fs + 2, col);
            Text(c, parent, text, fs, sx + iw, y, tw - iw - aw + 12, h, TextAnchor.MiddleLeft, col);
            if (arrow) Ic(c, parent, "arrow", sx + tw - aw + 6, y + (h - fs) / 2f, fs, col);
            if (!string.IsNullOrEmpty(cmd)) Hit(c, parent, cmd, x, y, w, h);
        }

        private static void Tag(CuiElementContainer c, string parent, string text, string kind, float x, float y, out float w)
        {
            w = TW(text, 10) + 16;
            string bg = kind == "green" ? "0.498 0.831 0.604 0.08" : kind == "gold" ? "0.851 0.737 0.514 0.08" : "0.639 0.086 0.184 0.2";
            string ln = kind == "green" ? "0.498 0.831 0.604 0.14" : kind == "gold" ? "0.851 0.737 0.514 0.14" : "0.941 0.651 0.694 0.14";
            string fg = kind == "green" ? MGreen : kind == "gold" ? MGold : MPink;
            BBox(c, parent, bg, ln, x, y, w, 20, 6f);
            Text(c, parent, text, 10, x, y, w, 20, TextAnchor.MiddleCenter, fg);
        }

        private static void Tag(CuiElementContainer c, string parent, string text, string kind, float x, float y) { float w; Tag(c, parent, text, kind, x, y, out w); }

        private static void TagRight(CuiElementContainer c, string parent, string text, string kind, float right, float y)
        {
            Tag(c, parent, text, kind, right - TW(text, 10) - 16, y);
        }

        private static void Kbd(CuiElementContainer c, string parent, string key, float x, float y)
        {
            float w = Mathf.Max(20, TW(key, 10) + 10);
            BBox(c, parent, "1 1 1 0.06", "1 1 1 0.16", x, y, w, 20, 5f);
            Text(c, parent, key, 10, x, y, w, 20, TextAnchor.MiddleCenter, MInk);
        }

        private static void Panel(CuiElementContainer c, string parent, float x, float y, float w, float h, bool featured = false)
        {
            if (featured) BBox(c, parent, "0.639 0.086 0.184 0.13", "0.941 0.651 0.694 0.27", x, y, w, h, 16f);
            else BBox(c, parent, MTile, MLine, x, y, w, h, 16f);
        }

        private static void TileIcon(CuiElementContainer c, string parent, string icon, float x, float y, float s = 40f)
        {
            Box(c, parent, MWineSoft, x, y, s, s, s * 0.3f);
            Ic(c, parent, icon, x + s * 0.25f, y + s * 0.25f, s * 0.5f, MPink);
        }

        private static void Section(CuiElementContainer c, string parent, string title, string right, float x, float y, float w)
        {
            Text(c, parent, title, 15, x, y, w, 22, TextAnchor.MiddleLeft, MInk);
            if (!string.IsNullOrEmpty(right)) Text(c, parent, right, 11, x, y, w, 22, TextAnchor.MiddleRight, MMuted, FontReg);
        }

        private static void Stat(CuiElementContainer c, string parent, string value, string label, float x, float y, float w, float h, string color = MInk)
        {
            Panel(c, parent, x, y, w, h);
            Text(c, parent, value, 24, x, y + 10, w, 30, TextAnchor.MiddleCenter, color);
            Text(c, parent, label, 11, x, y + 42, w, 16, TextAnchor.MiddleCenter, MMuted, FontReg);
        }

        private static void Avatar(CuiElementContainer c, string parent, string name, float x, float y, float s)
        {
            Box(c, parent, "0.557 0.149 0.231 1", x, y, s, s, s * 0.32f);
            Text(c, parent, string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpper(), Mathf.RoundToInt(s * 0.5f), x, y, s, s, TextAnchor.MiddleCenter, "0.969 0.851 0.875 1");
        }

        private void Tabs(CuiElementContainer c, string parent, ulong id, string page, string[] keys, float x, float y, out string active)
        {
            active = MenuTab(id, page, keys[0]);
            if (Array.IndexOf(keys, active) < 0) active = keys[0];
            float w = 10f;
            foreach (var k in keys) w += TW(T(id, "Tab_" + k), 12) + 28 + 6;
            BBox(c, parent, "0 0 0 0.07", MLine, x, y, w - 6 + 4, 42, 14f);
            float tx = x + 5;
            foreach (var k in keys)
            {
                string label = T(id, "Tab_" + k);
                float bw = TW(label, 12) + 28;
                bool a = k == active;
                if (a) BBox(c, parent, "0.639 0.086 0.184 0.25", "0.941 0.651 0.694 0.3", tx, y + 5, bw, 32, 10f);
                Text(c, parent, label, 12, tx, y + 5, bw, 32, TextAnchor.MiddleCenter, a ? "1 1 1 1" : MMuted);
                Hit(c, parent, $"hub.ui tab {page} {k}", tx, y + 5, bw, 32);
                tx += bw + 6;
            }
        }

        // ---------- карточка режима из конфига
        private void ModeCard(CuiElementContainer c, string parent, BasePlayer player, ModeCfg m, float x, float y, float w, float h, string foot = null)
        {
            var id = Id(player);
            bool here = ModeOf(player) == m.Id;
            string reason;
            bool can = CanEnter(player, m, out reason);
            Panel(c, parent, x, y, w, h, here);
            string icon;
            if (!ModeIcon.TryGetValue(m.Id, out icon)) icon = "mini";
            TileIcon(c, parent, icon, x + 16, y + 16);
            int n = GetModeOnline(m.Id);
            Text(c, parent, m.ComingSoon ? T(id, "SoonShort") : "● " + T(id, "InGame", n), 11, x + 60, y + 16, w - 76, 20, TextAnchor.MiddleRight, m.ComingSoon ? MMuted : MGreen, FontReg);
            Text(c, parent, ModeName(m, id), 17, x + 16, y + 68, w - 32, 22, TextAnchor.MiddleLeft, MInk);
            Text(c, parent, ModeDesc(m, id), 12, x + 16, y + 92, w - 32, 18, TextAnchor.MiddleLeft, MMuted, FontReg);
            if (foot != null) Text(c, parent, foot, 11, x + 16, y + 112, w - 32, 16, TextAnchor.MiddleLeft, MMuted, FontReg);
            float by = y + h - 46;
            if (m.ComingSoon) Pill(c, parent, T(id, "SoonBtn"), "clock", null, "off", x + 16, by, w - 32, 32);
            else if (here) Pill(c, parent, T(id, "YouHere"), "check", null, "ghost", x + 16, by, w - 32, 32);
            else if (!can) Pill(c, parent, T(id, "Locked"), "lock", "hub.ui play " + m.Id, "off", x + 16, by, w - 32, 32);
            else Pill(c, parent, T(id, "PlayBtn"), null, "hub.ui play " + m.Id, "primary", x + 16, by, w - 32, 32, 12, true);
        }

        private List<ModeCfg> ModesOf(string page) => _config.Modes.Where(m => m.Enabled && (ModePage.ContainsKey(m.Id) ? ModePage[m.Id] : "mini") == page).ToList();

        private int OnlineOf(string page) => ModesOf(page).Sum(m => GetModeOnline(m.Id));

        // ---------- окно
        private void OpenMenu(BasePlayer player, string page = null)
        {
            if (player == null || !player.IsConnected) return;
            var id = Id(player);
            if (page != null) _menuPage[id] = page;
            if (!_menuPage.TryGetValue(id, out page) || !MenuPages.Any(p => p[0] == page)) page = "home";
            _menuShown.Add(id);
            var d = D(id);
            const string R = UiMain;

            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0.03 0.02 0.02 0.55" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", UiMenuDim);
            Root(c, R + ".line", UiMenuDim, MLine, "0.5 0.5", "0.5 0.5", $"{-MW / 2f - 1} {-MH / 2f - 1}", $"{MW / 2f + 1} {MH / 2f + 1}", 26f, true);
            Root(c, R, UiMenuDim, MGlass, "0.5 0.5", "0.5 0.5", $"{-MW / 2f} {-MH / 2f}", $"{MW / 2f} {MH / 2f}", 25f, true);

            // боковая панель
            Box(c, R, "0.031 0.024 0.024 0.16", 0, 0, SideW, MH, 25f);
            Box(c, R, MLine, SideW, 0, 1, MH, 0f);
            Text(c, R, _config.ServerName, 18, 24, 22, SideW - 30, 24, TextAnchor.MiddleLeft, MInk);
            Text(c, R, "STUDIO", 10, 24, 46, SideW - 30, 14, TextAnchor.MiddleLeft, MMuted);
            float ny = 82f;
            for (int i = 0; i < MenuPages.Length - 1; i++)
            {
                if (i == 7) { Box(c, R, MLine, 22, ny + 4, SideW - 44, 1, 0f); ny += 12f; }
                NavItem(c, R, id, MenuPages[i][0], MenuPages[i][1], page, ny);
                ny += 44f;
            }
            NavItem(c, R, id, "settings", "settings", page, MH - 128f);
            // карточка игрока внизу
            bool vip = IsVipOf(id);
            Avatar(c, R, player.displayName, 22, MH - 70, 34);
            Text(c, R, Clip(player.displayName, 14), 13, 64, MH - 72, SideW - 70, 18, TextAnchor.MiddleLeft, MInk);
            Text(c, R, (vip ? "VIP · " : "") + T(id, "CoinsShort", Num(d.Coins)), 11, 64, MH - 54, SideW - 70, 16, TextAnchor.MiddleLeft, MMuted, FontReg);

            // шапка
            Box(c, R, MWine, SideW + 22, 16, 44, 44, 14f);
            Ic(c, R, MenuPages.First(p => p[0] == page)[1], SideW + 33, 27, 22, "1 1 1 1");
            Text(c, R, T(id, "Page_" + page).ToUpper(), 19, SideW + 80, 16, MW - SideW - 160, 26, TextAnchor.MiddleCenter, MInk);
            Text(c, R, T(id, "PageSub_" + page), 12, SideW + 80, 40, MW - SideW - 160, 18, TextAnchor.MiddleCenter, MMuted, FontReg);
            Box(c, R, "1 1 1 0.055", MW - 66, 16, 44, 44, 14f);
            Ic(c, R, "close", MW - 55, 27, 22, MInk);
            Hit(c, R, "hub.ui close", MW - 66, 16, 44, 44);
            Box(c, R, "0.639 0.086 0.184 0.5", SideW + 40, 74, MW - SideW - 80, 2, 0f);

            // подвал
            Box(c, R, MLine, SideW + 1, MH - 40, MW - SideW - 1, 1, 0f);
            Kbd(c, R, "P", CX, MH - 30);
            Text(c, R, T(id, "FootMenu"), 11, CX + 26, MH - 30, 200, 20, TextAnchor.MiddleLeft, MMuted, FontReg);
            string cur = NameOf(ModeOf(player), id);
            Text(c, R, "● " + cur, 11, CX, MH - 30, CW, 20, TextAnchor.MiddleCenter, MGreen, FontReg);
            Text(c, R, T(id, "FootOnline", Real.Count(p => p.IsConnected), _config.MaxPlayers), 11, CX, MH - 30, CW, 20, TextAnchor.MiddleRight, MMuted, FontReg);

            switch (page)
            {
                case "home": PageHome(c, player, d); break;
                case "build": PageBuild(c, player); break;
                case "film": PageFilm(c, player); break;
                case "arena": PageArena(c, player); break;
                case "aim": PageAim(c, player); break;
                case "mini": PageMini(c, player); break;
                case "mm": PageMm(c, player); break;
                case "shop": PageShop(c, player, d); break;
                case "profile": PageProfile(c, player, d); break;
                case "group": PageGroup(c, player); break;
                case "settings": PageSettings(c, player, d); break;
            }

            CuiHelper.DestroyUi(player, UiMenuDim);
            CuiHelper.DestroyUi(player, UiMain);
            CuiHelper.DestroyUi(player, UiPopup + ".dim");
            CuiHelper.DestroyUi(player, UiPopup);
            CuiHelper.AddUi(player, c);
        }

        private void NavItem(CuiElementContainer c, string R, ulong id, string page, string icon, string active, float y)
        {
            bool a = page == active;
            if (a)
            {
                BBox(c, R, MWineSoft, MRoseLine, 12, y, SideW - 24, 40, 12f);
                Box(c, R, MPink, 11, y + 11, 3, 18, 1.5f);
            }
            Ic(c, R, icon, 24, y + 11, 18, a ? MPink : MMuted);
            Text(c, R, T(id, "Page_" + page), 14, 53, y, SideW - 66, 40, TextAnchor.MiddleLeft, a ? "1 1 1 1" : MInk, a ? Font : FontReg);
            if (!a) Hit(c, R, "hub.ui page " + page, 12, y, SideW - 24, 40);
        }

        private bool IsVipOf(ulong id)
        {
            var shop = P("Shop");
            return shop != null && shop.Call("IsVip", id) is bool && (bool)shop.Call("IsVip", id);
        }

        private static string Num(int n) => n.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ");

        private static string Clip(string s, int n) => string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        // самый частый режим игрока — для «Продолжить»
        private string LastMode(PData d)
        {
            if (d.Mode != Lobby && Mode(d.Mode) != null) return d.Mode;
            var best = d.Minutes.Where(kv => kv.Key != Lobby && Mode(kv.Key) != null && Mode(kv.Key).Enabled && !Mode(kv.Key).ComingSoon).OrderByDescending(kv => kv.Value).FirstOrDefault();
            return best.Key ?? (Mode(_config.JoinMode) != null ? _config.JoinMode : _config.Modes.First().Id);
        }

        private List<ulong> TeamOf(BasePlayer player)
        {
            var list = new List<ulong>();
            try
            {
                var team = RelationshipManager.ServerInstance?.FindTeam(player.currentTeam);
                if (team != null)
                {
                    list.Add(team.teamLeader);
                    foreach (var m in team.members) if (m != team.teamLeader) list.Add(m);
                }
            }
            catch { }
            return list;
        }

        private string NameById(ulong uid)
        {
            var p = BasePlayer.FindByID(uid);
            if (p != null) return p.displayName;
            PData d;
            return _players.TryGetValue(uid, out d) && !string.IsNullOrEmpty(d.Name) ? d.Name : uid.ToString();
        }

        // ---------- Главная
        private void PageHome(CuiElementContainer c, BasePlayer player, PData d)
        {
            var id = Id(player);
            const string R = UiMain;
            const float LW = 600f, RX = CX + LW + 12f, RW = CW - LW - 12f;

            // приветствие
            BBox(c, R, "0.639 0.086 0.184 0.08", "0.941 0.651 0.694 0.23", CX, CY, LW, 158, 19f);
            Box(c, R, "0.62 0.082 0.188 0.12", CX + LW - 260, CY + 1, 259, 156, 18f);
            Text(c, R, "A / W", 96, CX + LW - 300, CY + 52, 290, 110, TextAnchor.LowerRight, "0.941 0.651 0.694 0.035");
            Text(c, R, T(id, "Welcome", player.displayName).ToUpper(), 11, CX + 22, CY + 20, LW - 44, 16, TextAnchor.MiddleLeft, MPink);
            Text(c, R, T(id, "HomeTitle"), 28, CX + 22, CY + 38, LW - 44, 38, TextAnchor.MiddleLeft, MInk);
            var last = LastMode(d);
            string lastName = NameOf(last, id);
            Text(c, R, ModeOf(player) == last ? T(id, "HomeHere", lastName) : T(id, "HomeSub", lastName), 13, CX + 22, CY + 78, LW - 44, 18, TextAnchor.MiddleLeft, MMuted, FontReg);
            if (ModeOf(player) == last)
                Pill(c, R, T(id, "Page_" + (ModePage.ContainsKey(last) ? ModePage[last] : "mini")), "arrow", "hub.ui page " + (ModePage.ContainsKey(last) ? ModePage[last] : "mini"), "primary", CX + 22, CY + 106, 200, 34, 13);
            else
                Pill(c, R, T(id, "Continue", lastName), ModeIcon.ContainsKey(last) ? ModeIcon[last] : "play", "hub.ui play " + last, "primary", CX + 22, CY + 106, Mathf.Max(200, TW(T(id, "Continue", lastName), 13) + 60), 34, 13);

            // режимы
            int total = Real.Count(p => p.IsConnected);
            Section(c, R, T(id, "ChooseWhat"), T(id, "PlayersOnline", total), CX, CY + 172, LW);
            string[][] cards = { new[] { "film", "film" }, new[] { "arena", "arena" }, new[] { "aim", "aim" } };
            float cw = (LW - 20) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float x = CX + i * (cw + 10), y = CY + 200;
                Panel(c, R, x, y, cw, 158);
                TileIcon(c, R, cards[i][1], x + 16, y + 16);
                Text(c, R, "● " + T(id, "InGame", OnlineOf(cards[i][0])), 11, x + 60, y + 16, cw - 76, 20, TextAnchor.MiddleRight, MGreen, FontReg);
                Text(c, R, T(id, "Page_" + cards[i][0]), 17, x + 16, y + 66, cw - 32, 22, TextAnchor.MiddleLeft, MInk);
                Text(c, R, T(id, "CardSub_" + cards[i][0]), 12, x + 16, y + 88, cw - 32, 18, TextAnchor.MiddleLeft, MMuted, FontReg);
                Pill(c, R, T(id, "Open"), null, "hub.ui page " + cards[i][0], "primary", x + 16, y + 114, cw - 32, 30, 12, true);
            }

            // прогресс
            Section(c, R, T(id, "Progress"), null, CX, CY + 372, LW);
            int mins = d.Minutes.Values.Sum();
            float sw = (LW - 14) / 3f;
            var ranked = P("Ranked");
            string rank = ranked?.Call("GetBestRank", id) as string;
            object mmr = ranked?.Call("GetBestMmr", id);
            Stat(c, R, (mins / 60) + " " + T(id, "H"), T(id, "TTime"), CX, CY + 400, sw, 72);
            Stat(c, R, rank ?? "—", rank != null && mmr is int ? T(id, "RankMmr", Num((int)mmr)) : T(id, "NoRank"), CX + sw + 7, CY + 400, sw, 72, rank != null ? MGold : MInk);
            Stat(c, R, d.Achs.Count + " / " + Achs.Count, T(id, "TAchs"), CX + 2 * (sw + 7), CY + 400, sw, 72);

            // профиль и баланс
            bool vip = IsVipOf(id);
            Panel(c, R, RX, CY, RW, 150);
            Avatar(c, R, player.displayName, RX + 16, CY + 16, 43);
            Text(c, R, Clip(player.displayName, 18), 15, RX + 70, CY + 16, RW - 86, 22, TextAnchor.MiddleLeft, MInk);
            Tag(c, R, vip ? "VIP" : T(id, "PlayerTag"), vip ? "gold" : "", RX + 70, CY + 39);
            Text(c, R, T(id, "YourBalance"), 12, RX + 16, CY + 72, RW - 32, 30, TextAnchor.MiddleLeft, MMuted, FontReg);
            Text(c, R, Num(d.Coins), 23, RX + 16, CY + 70, RW - 32, 32, TextAnchor.MiddleRight, MGold);
            Pill(c, R, T(id, "OpenShop"), null, "hub.ui page shop", "ghost", RX + 16, CY + 106, RW - 32, 32, 12, true);

            // группа
            var team = TeamOf(player);
            Panel(c, R, RX, CY + 160, RW, 176);
            Text(c, R, T(id, "YourGroup"), 14, RX + 16, CY + 174, RW - 80, 20, TextAnchor.MiddleLeft, MInk);
            TagRight(c, R, Mathf.Max(1, team.Count) + " / 5", "", RX + RW - 16, CY + 174);
            var others = team.Where(u => u != id).Take(2).ToList();
            if (others.Count == 0) Text(c, R, T(id, "GroupAlone"), 12, RX + 16, CY + 202, RW - 32, 70, TextAnchor.MiddleLeft, MMuted, FontReg);
            for (int i = 0; i < others.Count; i++)
            {
                float y = CY + 204 + i * 42;
                var on = BasePlayer.FindByID(others[i]);
                Avatar(c, R, NameById(others[i]), RX + 16, y, 34);
                Text(c, R, Clip(NameById(others[i]), 16), 13, RX + 58, y, RW - 90, 18, TextAnchor.MiddleLeft, MInk);
                Text(c, R, on != null && on.IsConnected ? NameOf(ModeOf(on), id) : T(id, "Offline"), 11, RX + 58, y + 17, RW - 90, 16, TextAnchor.MiddleLeft, MMuted, FontReg);
                Box(c, R, on != null && on.IsConnected ? MGreen : MMuted, RX + RW - 24, y + 14, 6, 6, 3f);
            }
            Pill(c, R, T(id, "Invite"), "plus", "hub.ui chat party", "ghost", RX + 16, CY + 290, RW - 32, 32);

            // заметка студии
            Panel(c, R, RX, CY + 346, RW, 126);
            Text(c, R, T(id, "NoteCap").ToUpper(), 11, RX + 16, CY + 360, RW - 32, 16, TextAnchor.MiddleLeft, MPink);
            Text(c, R, T(id, "NoteTitle"), 15, RX + 16, CY + 378, RW - 32, 20, TextAnchor.MiddleLeft, MInk);
            Text(c, R, T(id, "NoteText"), 12, RX + 16, CY + 398, RW - 32, 34, TextAnchor.UpperLeft, MMuted, FontReg);
            Pill(c, R, T(id, "ToStudio"), null, "hub.ui page film", "ghost", RX + 16, CY + 432, 130, 30, 12, true);
        }

        // ---------- Стройка
        private static readonly string[][] BuildActions =
        {
            new[] { "BActWindow", "build", "build" }, new[] { "BActPlot", "map", "plot" }, new[] { "BActBp", "layers", "bp" },
            new[] { "BActFly", "eye", "fly" }, new[] { "BActRaid", "arena", "build raid" }, new[] { "BActHome", "home", "home" },
            new[] { "BActElec", "sun", "build elec" }, new[] { "BActSpawn", "map", "build spawn" }
        };

        private void PageBuild(CuiElementContainer c, BasePlayer player)
        {
            var id = Id(player);
            const string R = UiMain;
            var m = Mode("build");
            HeroMode(c, player, m, "build", T(id, "BuildHeroCap"), T(id, "BuildHeroTitle"), T(id, "BuildHeroSub"));
            bool inBuild = ModeOf(player) == "build";
            Box(c, R, "0.62 0.082 0.188 0.094", CX, CY + 172, CW, 38, 11f);
            Text(c, R, inBuild ? T(id, "BuildToolsHere") : T(id, "BuildToolsAway"), 12, CX + 14, CY + 172, CW - 28, 38, TextAnchor.MiddleLeft, "0.804 0.741 0.753 1", FontReg);
            Section(c, R, T(id, "QuickActions"), T(id, "QuickActionsSub"), CX, CY + 222, CW);
            float cw = (CW - 30) / 4f;
            for (int i = 0; i < BuildActions.Length; i++)
            {
                var a = BuildActions[i];
                float x = CX + (i % 4) * (cw + 10), y = CY + 250 + (i / 4) * 140;
                Panel(c, R, x, y, cw, 130);
                TileIcon(c, R, a[1], x + 16, y + 16);
                Text(c, R, T(id, a[0]), 16, x + 66, y + 16, cw - 82, 20, TextAnchor.MiddleLeft, MInk);
                Text(c, R, "/" + a[2], 11, x + 66, y + 36, cw - 82, 16, TextAnchor.MiddleLeft, MPink, FontReg);
                Text(c, R, T(id, a[0] + "Sub"), 12, x + 16, y + 62, cw - 32, 18, TextAnchor.MiddleLeft, MMuted, FontReg);
                Pill(c, R, T(id, "Run"), null, "hub.ui chat " + a[2], inBuild ? "primary" : "ghost", x + 16, y + 88, cw - 32, 30, 12, true);
            }
        }

        // большой блок режима: подпись, заголовок, онлайн, кнопка войти
        private void HeroMode(CuiElementContainer c, BasePlayer player, ModeCfg m, string icon, string cap, string title, string sub)
        {
            var id = Id(player);
            const string R = UiMain;
            BBox(c, R, "0.639 0.086 0.184 0.08", "0.941 0.651 0.694 0.23", CX, CY, CW, 160, 19f);
            Box(c, R, "0.62 0.082 0.188 0.12", CX + CW - 340, CY + 1, 339, 158, 18f);
            Text(c, R, "A / W", 96, CX + CW - 380, CY + 52, 370, 110, TextAnchor.LowerRight, "0.941 0.651 0.694 0.035");
            Text(c, R, cap.ToUpper(), 11, CX + 22, CY + 20, CW - 44, 16, TextAnchor.MiddleLeft, MPink);
            Text(c, R, title, 28, CX + 22, CY + 38, CW - 300, 38, TextAnchor.MiddleLeft, MInk);
            Text(c, R, sub, 13, CX + 22, CY + 78, CW - 300, 18, TextAnchor.MiddleLeft, MMuted, FontReg);
            if (m == null) return;
            int n = GetModeOnline(m.Id);
            Text(c, R, "● " + T(id, "InGame", n), 12, CX + CW - 240, CY + 20, 218, 18, TextAnchor.MiddleRight, MGreen, FontReg);
            string reason;
            if (m.ComingSoon) Pill(c, R, T(id, "SoonBtn"), "clock", null, "off", CX + 22, CY + 108, 210, 34, 13);
            else if (ModeOf(player) == m.Id) Pill(c, R, T(id, "YouHere"), "check", null, "ghost", CX + 22, CY + 108, 210, 34, 13);
            else if (!CanEnter(player, m, out reason)) Pill(c, R, T(id, "Locked"), "lock", "hub.ui play " + m.Id, "off", CX + 22, CY + 108, 210, 34, 13);
            else Pill(c, R, T(id, "EnterMode", ModeName(m, id)), icon, "hub.ui play " + m.Id, "primary", CX + 22, CY + 108, Mathf.Max(210, TW(T(id, "EnterMode", ModeName(m, id)), 13) + 60), 34, 13);
        }

        // ---------- Съёмки
        private static readonly string[][] FilmFeatures =
        {
            new[] { "FTakes", "film" }, new[] { "FCamera", "camera" }, new[] { "FFx", "star" },
            new[] { "FWeather", "sun" }, new[] { "FKits", "layers" }, new[] { "FMotor", "group" },
        };

        private void PageFilm(CuiElementContainer c, BasePlayer player)
        {
            var id = Id(player);
            const string R = UiMain;
            const float LW = 600f, RX = CX + LW + 12f, RW = CW - LW - 12f;
            HeroMode(c, player, Mode("film"), "film", T(id, "FilmHeroCap"), T(id, "FilmHeroTitle"), T(id, "FilmHeroSub"));
            Section(c, R, T(id, "FilmWhat"), null, CX, CY + 172, LW);
            float cw = (LW - 20) / 3f;
            for (int i = 0; i < FilmFeatures.Length; i++)
            {
                var f = FilmFeatures[i];
                float x = CX + (i % 3) * (cw + 10), y = CY + 200 + (i / 3) * 140;
                Panel(c, R, x, y, cw, 130);
                TileIcon(c, R, f[1], x + 16, y + 16);
                Text(c, R, T(id, f[0]), 16, x + 16, y + 64, cw - 32, 20, TextAnchor.MiddleLeft, MInk);
                Text(c, R, T(id, f[0] + "Sub"), 12, x + 16, y + 86, cw - 32, 34, TextAnchor.UpperLeft, MMuted, FontReg);
            }

            Panel(c, R, RX, CY + 172, RW, 300);
            Text(c, R, T(id, "Hotkeys"), 15, RX + 16, CY + 186, RW - 32, 20, TextAnchor.MiddleLeft, MInk);
            string[] keys = { "F5", "F6", "F7", "F8", "F9", "F10", "F11" };
            for (int i = 0; i < keys.Length; i++)
            {
                float y = CY + 216 + i * 28;
                Kbd(c, R, keys[i], RX + 16, y);
                Text(c, R, T(id, "Hk" + keys[i]), 12, RX + 56, y, RW - 72, 20, TextAnchor.MiddleLeft, MInk, FontReg);
            }
            Pill(c, R, T(id, "OpenStudio"), "film", "hub.ui chat vr", "primary", RX + 16, CY + 420, RW - 32, 36, 13);
        }

        // ---------- Арены
        private void PageArena(CuiElementContainer c, BasePlayer player)
        {
            var id = Id(player);
            const string R = UiMain;
            string tab;
            Tabs(c, R, id, "arena", new[] { "all", "ffa", "duels" }, CX, CY, out tab);
            float y = CY + 56;
            if (tab == "ranked")
            {
                var ranked = P("Ranked");
                string rank = ranked?.Call("GetBestRank", id) as string;
                object mmr = ranked?.Call("GetBestMmr", id);
                Panel(c, R, CX, y, CW, 70);
                Box(c, R, "0.851 0.737 0.514 0.12", CX + 16, y + 13, 44, 44, 14f);
                Ic(c, R, "cup", CX + 27, y + 24, 22, MGold);
                Text(c, R, rank ?? T(id, "NoRank"), 21, CX + 74, y + 12, 400, 26, TextAnchor.MiddleLeft, MInk);
                Text(c, R, mmr is int && (int)mmr > 0 ? T(id, "RankMmr", Num((int)mmr)) : T(id, "RankHint"), 12, CX + 74, y + 38, 500, 18, TextAnchor.MiddleLeft, MMuted, FontReg);
                Pill(c, R, T(id, "Leaderboard"), "cup", "hub.ui chat top", "ghost", CX + CW - 196, y + 19, 180, 32);
                y += 80;
                // каждый ММ — плашка на всю ширину
                for (int i = 0; i < MmLadders.Length; i++) LadderStrip(c, R, player, MmLadders[i], CX, y + i * 66, CW, 58);
                return;
            }
            Text(c, R, T(id, "ArenaHint"), 12, CX, y, CW - 380, 34, TextAnchor.MiddleLeft, MMuted, FontReg);
            Pill(c, R, T(id, "DuelBtn"), "arena", "hub.ui chat duel", "ghost", CX + CW - 370, y, 180, 34);
            Pill(c, R, T(id, "ArenaBrowser"), "layers", "hub.ui chat arena", "ghost", CX + CW - 180, y, 180, 34);
            y += 46;

            var list = ModesOf("arena").Where(m =>
                (tab == "all" && !m.Id.StartsWith("ranked")) || (tab == "ffa" && (m.Id == "ffa" || m.Id == "primitive" || m.Id == "maze" || m.Id == "walls" || m.Id == "bows")) || (tab == "ranked" && m.Id.StartsWith("ranked")) || (tab == "duels" && (m.Id == "duels" || m.Id == "speargun"))).ToList();
            int cols = list.Count > 6 || (tab == "ranked" && list.Count > 3) ? 4 : 3;
            float cw = (CW - 10 * (cols - 1)) / cols, ch = 182f;
            int maxRows = Mathf.Max(1, Mathf.FloorToInt((MH - 52 - y + 10) / (ch + 10)));
            for (int i = 0; i < list.Count && i < cols * maxRows; i++)
            {
                var m = list[i];
                ModeCard(c, R, player, m, CX + (i % cols) * (cw + 10), y + (i / cols) * (ch + 10), cw, ch,
                    m.Id.StartsWith("ranked") ? T(id, "RankedFoot") : T(id, "ArenaFoot"));
            }
            if (list.Count == 0) Empty(c, R, T(id, "NoModes"), CX, y, CW, 120);
        }

        // ---------- плашка матчмейкинга: на всю ширину, свой стиль (винная полоса слева, ранг, поиск, кнопка)
        private static readonly string[][] MmLadders =
        {
            new[] { "ak", "cup", "ranked ak" }, new[] { "mix", "star", "ranked mix" }, new[] { "t2", "cup", "ranked t2" },
            new[] { "prim", "flag", "ranked prim" }, new[] { "bow", "flag", "ranked bow" }, new[] { "heli", "mini", "mg helimm" },
        };

        private void MmStrip(CuiElementContainer c, string R, BasePlayer player, string key, string icon, string cmd, float x, float y, float w, float h,
            string rank, string rankColor, int mmr, int queue, string tag)
        {
            var id = Id(player);
            // корпус: тёмно-винный, тонкая розовая рамка, яркая полоса слева и мягкое свечение справа
            BBox(c, R, "0.11 0.06 0.07 1", "0.941 0.651 0.694 0.22", x, y, w, h, 14f);
            Box(c, R, "0.639 0.086 0.184 0.16", x + w - 284, y + 1, 283, h - 2, 13f);
            Box(c, R, MWine, x, y + 8, 4, h - 16, 2f);
            TileIcon(c, R, icon, x + 16, y + (h - 40) / 2f);
            Text(c, R, tag.ToUpper(), 9, x + 68, y + 8, 300, 12, TextAnchor.MiddleLeft, MPink);
            Text(c, R, T(id, "Mm_" + key), 16, x + 68, y + 19, w - 520, 20, TextAnchor.MiddleLeft, MInk);
            Text(c, R, T(id, "MmD_" + key), 11, x + 68, y + 39, w - 520, 16, TextAnchor.MiddleLeft, MMuted, FontReg);
            // ранг и MMR
            float rx = x + w - 440;
            if (mmr >= 0) Text(c, R, rank ?? T(id, "NoRank"), 14, rx, y + 12, 150, 20, TextAnchor.MiddleLeft, rank != null && rankColor != null ? rankColor : MInk);
            if (mmr >= 0) Text(c, R, mmr > 0 ? mmr + " MMR" : "—", 11, rx, y + 32, 150, 16, TextAnchor.MiddleLeft, MMuted, FontReg);
            // поиск
            Text(c, R, "● " + T(id, "MmQueue", queue), 11, x + w - 270, y, 104, h, TextAnchor.MiddleLeft, queue > 0 ? MGreen : MMuted, FontReg);
            Pill(c, R, T(id, "MmFind"), null, "hub.ui chat " + cmd, "primary", x + w - 160, y + (h - 34) / 2f, 144, 34, 12, true);
        }

        private void LadderStrip(CuiElementContainer c, string R, BasePlayer player, string[] l, float x, float y, float w, float h)
        {
            var info = P("Ranked")?.Call("GetLadderCard", Id(player), l[0]) as Dictionary<string, object>;
            int mmr = info != null && info["mmr"] is int ? (int)info["mmr"] : 0;
            int queue = info != null && info["queue"] is int ? (int)info["queue"] : 0;
            if (l[0] == "heli") { var q = P("Minigames")?.Call("HeliQueueCount"); if (q is int) queue = (int)q; }
            MmStrip(c, R, player, l[0], l[1], l[2], x, y, w, h, info?["rank"] as string, info?["color"] as string, mmr, queue, T(Id(player), l[0] == "heli" ? "MmTagHeli" : "MmTagRanked"));
        }

        private static void Empty(CuiElementContainer c, string R, string text, float x, float y, float w, float h)
        {
            BBox(c, R, "0.071 0.055 0.055 1", "1 1 1 0.1", x, y, w, h, 14f);
            Text(c, R, text, 13, x, y, w, h, TextAnchor.MiddleCenter, MMuted, FontReg);
        }

        // ---------- Аим-трен
        // каждый режим аим-трена — своя плашка: [режим AimTrain, иконка, команда чата]
        private static readonly string[][] AimCards =
        {
            new[] { "targets", "aim", "aim targets" }, new[] { "strafers", "group", "aim strafers" }, new[] { "flow", "star", "aim flow" },
            new[] { "shooters", "arena", "aim shooters" }, new[] { "mirror", "eye", "aim mirror" }, new[] { "bow", "flag", "aim bow" },
            new[] { "fcbot", "lock", "aim fcbot" },
        };

        private void PageAim(CuiElementContainer c, BasePlayer player)
        {
            var id = Id(player);
            const string R = UiMain;
            // шапка
            BBox(c, R, "0.639 0.086 0.184 0.08", "0.941 0.651 0.694 0.23", CX, CY, CW, 96, 19f);
            Text(c, R, "A / W", 72, CX + CW - 300, CY + 18, 290, 78, TextAnchor.LowerRight, "0.941 0.651 0.694 0.035");
            Text(c, R, T(id, "AimCap").ToUpper(), 11, CX + 22, CY + 14, CW - 44, 16, TextAnchor.MiddleLeft, MPink);
            Text(c, R, T(id, "AimTitle"), 24, CX + 22, CY + 32, CW - 300, 32, TextAnchor.MiddleLeft, MInk);
            Text(c, R, T(id, "AimSub3"), 12, CX + 22, CY + 66, CW - 300, 18, TextAnchor.MiddleLeft, MMuted, FontReg);
            var aimMode = Mode("aim");
            int online = (aimMode != null ? GetModeOnline(aimMode.Id) : 0) + (Mode("facecheck") != null ? GetModeOnline("facecheck") : 0);
            Text(c, R, "● " + T(id, "InGame", online), 12, CX + CW - 240, CY + 14, 218, 18, TextAnchor.MiddleRight, MGreen, FontReg);

            // плашки режимов 4×2
            float cw = (CW - 30) / 4f, ch = 150f, y0 = CY + 108;
            for (int i = 0; i < AimCards.Length; i++)
            {
                var a = AimCards[i];
                float x = CX + (i % 4) * (cw + 10), y = y0 + (i / 4) * (ch + 10);
                Panel(c, R, x, y, cw, ch);
                TileIcon(c, R, a[1], x + 14, y + 14);
                Text(c, R, T(id, "AimM_" + a[0]), 16, x + 14, y + 60, cw - 28, 22, TextAnchor.MiddleLeft, MInk);
                Text(c, R, T(id, "AimMD_" + a[0]), 11, x + 14, y + 82, cw - 28, 18, TextAnchor.MiddleLeft, MMuted, FontReg);
                Pill(c, R, T(id, "PlayBtn"), null, "hub.ui chat " + a[2], "primary", x + 14, y + ch - 42, cw - 28, 30, 12, true);
            }

            // полоска статистики стрельбы
            float sy = y0 + 2 * (ch + 10);
            float sh = MH - 52 - 12 - sy;
            Panel(c, R, CX, sy, CW, sh);
            var st = P("Arenas")?.Call("GetArenaStats", id) as Dictionary<string, int>;
            int shots = 0, hits = 0, kills = 0, deaths = 0, hs = 0, streak = 0;
            if (st != null) { st.TryGetValue("shots", out shots); st.TryGetValue("hits", out hits); st.TryGetValue("kills", out kills); st.TryGetValue("deaths", out deaths); st.TryGetValue("headshots", out hs); st.TryGetValue("beststreak", out streak); }
            string[][] cells =
            {
                new[] { T(id, "Accuracy"), shots > 0 ? Mathf.RoundToInt(100f * hits / shots) + "%" : "—" },
                new[] { T(id, "KD"), deaths > 0 ? (kills / (float)deaths).ToString("0.00", CultureInfo.InvariantCulture) : kills.ToString() },
                new[] { T(id, "Headshots"), Num(hs) }, new[] { T(id, "BestStreak"), streak.ToString() },
            };
            Text(c, R, T(id, "YourShooting"), 12, CX + 16, sy + 6, 300, 16, TextAnchor.MiddleLeft, MInk);
            float qw = (CW - 32 - 30) / 4f;
            for (int i = 0; i < cells.Length; i++)
            {
                float x = CX + 16 + i * (qw + 10);
                Text(c, R, cells[i][1], 18, x, sy + 22, qw, 22, TextAnchor.MiddleLeft, i == 0 ? MGold : MInk);
                Text(c, R, cells[i][0], 10, x, sy + 44, qw, 14, TextAnchor.MiddleLeft, MMuted, FontReg);
            }
        }

        // ---------- ММ: все матчмейкинги сверху вниз, справа у каждого — лидеры
        private void PageMm(CuiElementContainer c, BasePlayer player)
        {
            var id = Id(player);
            const string R = UiMain;
            var rows = MmLadders.Select(l => new[] { l[0], l[1], l[2] }).Concat(new[] { new[] { "facecheck", "group", "fc" } }).ToArray();
            float rh = 72f, gap = 5f, y = CY - 4;
            float lx = CX + CW - 330;   // колонка лидеров
            for (int i = 0; i < rows.Length; i++, y += rh + gap)
            {
                var key = rows[i][0];
                bool fc = key == "facecheck";
                // корпус в стиле плашки ММ
                BBox(c, R, "0.11 0.06 0.07 1", "0.941 0.651 0.694 0.22", CX, y, CW, rh, 14f);
                Box(c, R, MWine, CX, y + 9, 4, rh - 18, 2f);
                Box(c, R, "0.639 0.086 0.184 0.12", lx - 8, y + 1, CX + CW - lx + 7, rh - 2, 13f);
                TileIcon(c, R, rows[i][1], CX + 16, y + (rh - 40) / 2f);
                Text(c, R, T(id, "Mm_" + key), 17, CX + 68, y + 13, 260, 24, TextAnchor.MiddleLeft, MInk);
                Text(c, R, T(id, "MmS_" + key), 11, CX + 68, y + 38, 260, 18, TextAnchor.MiddleLeft, MMuted, FontReg);
                // мой ранг и кто в поиске
                int queue = 0; string myRank = null, myCol = null; int myMmr = -1;
                if (!fc)
                {
                    var info = P("Ranked")?.Call("GetLadderCard", id, key) as Dictionary<string, object>;
                    if (info != null) { myRank = info["rank"] as string; myCol = info["color"] as string; myMmr = info["mmr"] is int ? (int)info["mmr"] : 0; queue = info["queue"] is int ? (int)info["queue"] : 0; }
                    if (key == "heli") { var q = P("Minigames")?.Call("HeliQueueCount"); if (q is int) queue = (int)q; }
                    Text(c, R, myRank ?? T(id, "NoRank"), 13, CX + 330, y + 12, 110, 18, TextAnchor.MiddleLeft, myCol ?? MInk);
                    Text(c, R, myMmr > 0 ? myMmr + " MMR" : "—", 10, CX + 330, y + 31, 110, 14, TextAnchor.MiddleLeft, MMuted, FontReg);
                }
                else
                {
                    queue = Mode("facecheck") != null ? GetModeOnline("facecheck") : 0;
                    Text(c, R, T(id, "MmNoRating"), 11, CX + 330, y, 110, rh, TextAnchor.MiddleLeft, MMuted, FontReg);
                }
                Text(c, R, "● " + T(id, "MmQueue", queue), 10, CX + 330, y + 50, 110, 14, TextAnchor.MiddleLeft, queue > 0 ? MGreen : MMuted, FontReg);
                Pill(c, R, T(id, "MmFindShort"), null, "hub.ui chat " + rows[i][2], "primary", lx - 126, y + (rh - 32) / 2f, 108, 32, 12, true);

                // лидеры: топ-3 в строку
                Text(c, R, T(id, "MmLeaders").ToUpper(), 9, lx + 6, y + 6, 200, 12, TextAnchor.MiddleLeft, MPink);
                var top = fc ? P("AimTrain")?.Call("GetFcTop", 3) as List<Dictionary<string, object>>
                             : P("Ranked")?.Call("GetLadderTop", key, 3) as List<Dictionary<string, object>>;
                float cw3 = (CX + CW - lx - 16) / 3f;
                string[] medal = { MGold, "0.80 0.80 0.84 1", "0.80 0.55 0.38 1" };
                if (top == null || top.Count == 0) Text(c, R, T(id, "MmNoLeaders"), 11, lx + 6, y + 20, CX + CW - lx - 12, rh - 24, TextAnchor.MiddleLeft, MMuted, FontReg);
                else
                    for (int k = 0; k < top.Count && k < 3; k++)
                    {
                        float cx = lx + 4 + k * cw3;
                        Box(c, R, "1 1 1 0.04", cx, y + 22, cw3 - 6, 42, 9f);
                        Text(c, R, (k + 1).ToString(), 15, cx + 6, y + 22, 16, 42, TextAnchor.MiddleCenter, medal[k]);
                        Text(c, R, Clip(top[k]["name"] as string, 11), 12, cx + 24, y + 25, cw3 - 32, 18, TextAnchor.MiddleLeft, MInk);
                        string val = fc ? T(id, "MmWins", top[k]["wins"]) : top[k]["mmr"] + " MMR";
                        Text(c, R, val, 10, cx + 24, y + 44, cw3 - 32, 16, TextAnchor.MiddleLeft, fc ? MMuted : (top[k]["color"] as string ?? MMuted), FontReg);
                    }
                Hit(c, R, fc ? "hub.ui chat fc" : "hub.ui chat top", lx - 8, y, CX + CW - lx + 8, rh);
            }
        }

        // ---------- Мини-игры
        private void PageMini(CuiElementContainer c, BasePlayer player)
        {
            var id = Id(player);
            const string R = UiMain;
            Box(c, R, "0.62 0.082 0.188 0.094", CX, CY, CW, 38, 11f);
            Text(c, R, T(id, "MiniBanner"), 12, CX + 14, CY, CW - 28, 38, TextAnchor.MiddleLeft, "0.804 0.741 0.753 1", FontReg);
            var list = ModesOf("mini").Where(m => m.Id != "helimm").ToList();
            float cw = (CW - 20) / 3f, ch = 200f;
            for (int i = 0; i < list.Count && i < 6; i++)
            {
                var m = list[i];
                float x = CX + (i % 3) * (cw + 10), y = CY + 52 + (i / 3) * (ch + 10);
                ModeCard(c, R, player, m, x, y, cw, ch - 10);
                Text(c, R, T(id, "Rules_" + m.Id), 11, x + 16, y + 112, cw - 32, 16, TextAnchor.MiddleLeft, MMuted, FontReg);
            }
            if (list.Count == 0) Empty(c, R, T(id, "NoModes"), CX, CY + 52, CW, 120);
            Text(c, R, T(id, "MiniFoot"), 11, CX, MH - 70, CW, 20, TextAnchor.MiddleLeft, MMuted, FontReg);
        }

        // ---------- Магазин
        private static readonly string[][] ShopCats =
        {
            new[] { "skins", "layers" }, new[] { "title", "star" }, new[] { "color", "sun" }, new[] { "vip", "cup" },
        };

        private void PageShop(CuiElementContainer c, BasePlayer player, PData d)
        {
            var id = Id(player);
            const string R = UiMain;
            Text(c, R, T(id, "ShopHint"), 12, CX, CY, CW - 200, 30, TextAnchor.MiddleLeft, MMuted, FontReg);
            Ic(c, R, "coin", CX + CW - 150, CY + 7, 16, MGold);
            Text(c, R, Num(d.Coins), 16, CX + CW - 130, CY, 130, 30, TextAnchor.MiddleRight, MGold);
            bool vip = IsVipOf(id);
            float cw = (CW - 10) / 2f, ch = 196f;
            for (int i = 0; i < ShopCats.Length; i++)
            {
                var s = ShopCats[i];
                float x = CX + (i % 2) * (cw + 10), y = CY + 42 + (i / 2) * (ch + 10);
                bool own = s[0] == "vip" && vip;
                Panel(c, R, x, y, cw, ch, own);
                Tag(c, R, T(id, "Cat_" + s[0]).ToUpper(), "", x + 16, y + 16);
                if (own) TagRight(c, R, T(id, "Owned"), "green", x + cw - 16, y + 16);
                Box(c, R, "0.62 0.082 0.188 0.1", x + 16, y + 46, cw - 32, 70, 12f);
                if (s[0] == "color") Text(c, R, $"<color=#c54b67>{Clip(player.displayName, 14)}</color>", 26, x + 16, y + 46, cw - 32, 70, TextAnchor.MiddleCenter, MInk);
                else if (s[0] == "title") Text(c, R, $"<color=#f0a6b1>[{T(id, "TitleSample")}]</color> {Clip(player.displayName, 12)}", 20, x + 16, y + 46, cw - 32, 70, TextAnchor.MiddleCenter, MInk);
                else Ic(c, R, s[1], x + cw / 2f - 20, y + 61, 40, MPink);
                Text(c, R, T(id, "ShopCat_" + s[0]), 12, x + 16, y + 124, cw - 32, 18, TextAnchor.MiddleLeft, MMuted, FontReg);
                Pill(c, R, T(id, "Open"), null, "hub.ui shop " + s[0], "primary", x + 16, y + ch - 46, cw - 32, 32, 12, true);
            }
        }

        // ---------- Профиль
        private void PageProfile(CuiElementContainer c, BasePlayer player, PData d)
        {
            var id = Id(player);
            const string R = UiMain;
            bool vip = IsVipOf(id);
            Panel(c, R, CX, CY, CW, 88);
            Avatar(c, R, player.displayName, CX + 16, CY + 14, 60);
            Text(c, R, Clip(player.displayName, 22), 24, CX + 90, CY + 14, 400, 32, TextAnchor.MiddleLeft, MInk);
            if (vip) Tag(c, R, "VIP", "gold", CX + 96 + TW(Clip(player.displayName, 22), 24), CY + 20);
            string clan = P("Social")?.Call("GetClanTag", id) as string;
            Text(c, R, T(id, "ProfileSince", d.FirstSeen ?? "—") + (string.IsNullOrEmpty(clan) ? "" : " · [" + clan + "]"), 12, CX + 90, CY + 48, 500, 18, TextAnchor.MiddleLeft, MMuted, FontReg);
            var ranked = P("Ranked");
            string rank = ranked?.Call("GetBestRank", id) as string;
            object mmr = ranked?.Call("GetBestMmr", id);
            Ic(c, R, "cup", CX + CW - 200, CY + 22, 18, MGold);
            Text(c, R, rank ?? T(id, "NoRank"), 16, CX + CW - 178, CY + 18, 162, 26, TextAnchor.MiddleRight, MGold);
            Text(c, R, mmr is int && (int)mmr > 0 ? Num((int)mmr) + " MMR" : "", 11, CX + CW - 178, CY + 44, 162, 16, TextAnchor.MiddleRight, MMuted, FontReg);

            string tab;
            Tabs(c, R, id, "profile", new[] { "overview", "achs" }, CX, CY + 100, out tab);
            float y = CY + 154;
            if (tab == "achs")
            {
                Text(c, R, T(id, "AchOpened", d.Achs.Count, Achs.Count), 12, CX, y, CW - 80, 20, TextAnchor.MiddleLeft, MMuted, FontReg);
                TagRight(c, R, Mathf.RoundToInt(d.Achs.Count * 100f / Mathf.Max(1, Achs.Count)) + "%", "gold", CX + CW, y);
                var ordered = Achs.OrderByDescending(a => d.Achs.Contains(a.Key)).ThenByDescending(a => Progress(d, a)).Take(24).ToList();
                float aw = (CW - 20) / 3f, ah = 38f;
                for (int i = 0; i < ordered.Count; i++)
                {
                    var a = ordered[i];
                    bool done = d.Achs.Contains(a.Key);
                    float x = CX + (i % 3) * (aw + 10), ay = y + 28 + (i / 3) * (ah + 5);
                    BBox(c, R, done ? "1 1 1 0.03" : "1 1 1 0.012", done ? MLine : "1 1 1 0.04", x, ay, aw, ah, 12f);
                    Ic(c, R, done ? "cup" : "lock", x + 12, ay + 10, 18, done ? MGold : "0.851 0.737 0.514 0.4");
                    Text(c, R, IsEn(id) ? a.En : a.Ru, 13, x + 42, ay + 2, aw - 110, 18, TextAnchor.MiddleLeft, done ? MInk : "0.937 0.914 0.906 0.45");
                    int v; d.Stats.TryGetValue(a.Stat, out v);
                    Text(c, R, done ? T(id, "AchDone") : (IsEn(id) ? a.DescEn : a.DescRu), 10, x + 42, ay + 20, aw - 54, 15, TextAnchor.MiddleLeft, done ? MMuted : "0.639 0.596 0.588 0.45", FontReg);
                    Text(c, R, done ? (a.Coins > 0 ? "+" + a.Coins : "") : Mathf.Min(v, a.Goal) + "/" + a.Goal, 11, x + aw - 70, ay + 2, 58, 18, TextAnchor.MiddleRight, done ? MGold : MMuted, FontReg);
                }
                return;
            }

            int mins = d.Minutes.Values.Sum();
            var st = P("Arenas")?.Call("GetArenaStats", id) as Dictionary<string, int>;
            int k = 0, de = 0;
            if (st != null) { st.TryGetValue("kills", out k); st.TryGetValue("deaths", out de); }
            float sw = (CW - 21) / 4f;
            Stat(c, R, (mins / 60) + " " + T(id, "H"), T(id, "TTime"), CX, y, sw, 72);
            Stat(c, R, st == null ? "—" : de > 0 ? (k / (float)de).ToString("0.00", CultureInfo.InvariantCulture) : k.ToString(), "K / D", CX + sw + 7, y, sw, 72);
            Stat(c, R, Num(d.Coins), T(id, "TCoins"), CX + 2 * (sw + 7), y, sw, 72, MGold);
            Stat(c, R, d.Achs.Count + " / " + Achs.Count, T(id, "TAchs"), CX + 3 * (sw + 7), y, sw, 72);
            y += 86;

            float hw = (CW - 10) / 2f, ph = MH - 52 - y;
            Panel(c, R, CX, y, hw, ph);
            Text(c, R, T(id, "TimeByModeCap"), 15, CX + 16, y + 12, hw - 32, 20, TextAnchor.MiddleLeft, MInk);
            var byMode = d.Minutes.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(5).ToList();
            int max = byMode.Count > 0 ? byMode[0].Value : 1;
            for (int i = 0; i < byMode.Count; i++)
            {
                float ry = y + 42 + i * 34;
                Text(c, R, NameOf(byMode[i].Key, id), 12, CX + 16, ry, hw - 100, 16, TextAnchor.MiddleLeft, MInk, FontReg);
                Text(c, R, (byMode[i].Value / 60f).ToString("0.#", CultureInfo.InvariantCulture) + " " + T(id, "H"), 12, CX + hw - 96, ry, 80, 16, TextAnchor.MiddleRight, MMuted, FontReg);
                Bar(c, R, CX + 16, ry + 20, hw - 32, 5, byMode[i].Value / (float)max, MWine);
            }
            if (byMode.Count == 0) Text(c, R, T(id, "NoData"), 12, CX, y, hw, ph, TextAnchor.MiddleCenter, MMuted, FontReg);

            float rx = CX + hw + 10;
            Panel(c, R, rx, y, hw, ph);
            Text(c, R, T(id, "StatsCap"), 15, rx + 16, y + 12, hw - 32, 20, TextAnchor.MiddleLeft, MInk);
            var extra = new List<string>();
            foreach (var pl in plugins.GetAll())
            {
                if (pl == null || pl == this) continue;
                var r = pl.Call("OnHubProfile", id, IsEn(id) ? "en" : "ru") as string;
                if (!string.IsNullOrEmpty(r)) extra.Add(r);
            }
            Text(c, R, extra.Count == 0 ? T(id, "StatsSoon") : string.Join("\n", extra.ToArray()), 12, rx + 16, y + 42, hw - 32, ph - 54, TextAnchor.UpperLeft, extra.Count == 0 ? MMuted : MInk, FontReg);
        }

        // ---------- Группа (команда Rust из Social)
        private void PageGroup(CuiElementContainer c, BasePlayer player)
        {
            var id = Id(player);
            const string R = UiMain;
            const float LW = 600f, RX = CX + LW + 12f, RW = CW - LW - 12f;
            var team = TeamOf(player);
            if (team.Count == 0) team.Add(id);
            bool leader = team[0] == id;
            Text(c, R, T(id, "GroupOf", NameById(team[0])), 15, CX, CY, LW - 200, 22, TextAnchor.MiddleLeft, MInk);
            Text(c, R, T(id, "GroupCount", team.Count), 12, CX, CY + 22, LW - 200, 18, TextAnchor.MiddleLeft, MMuted, FontReg);
            Pill(c, R, T(id, "Invite"), "plus", team.Count >= 5 ? null : "hub.ui chat party", team.Count >= 5 ? "off" : "primary", CX + LW - 170, CY + 4, 170, 34);

            Panel(c, R, CX, CY + 52, LW, 16 + team.Count * 54);
            for (int i = 0; i < team.Count; i++)
            {
                float y = CY + 60 + i * 54;
                var uid = team[i];
                var on = BasePlayer.FindByID(uid);
                bool online = on != null && on.IsConnected;
                if (i > 0) Box(c, R, MLine, CX + 16, y - 1, LW - 32, 1, 0f);
                Avatar(c, R, NameById(uid), CX + 16, y + 8, 34);
                string nm = Clip(NameById(uid), 20);
                Text(c, R, nm, 13, CX + 60, y + 7, 300, 18, TextAnchor.MiddleLeft, MInk);
                if (i == 0) Tag(c, R, T(id, "LeaderTag"), "gold", CX + 66 + TW(nm, 13), y + 6);
                Text(c, R, (uid == id ? T(id, "You") + " · " : "") + (online ? NameOf(ModeOf(on), id) : T(id, "Offline")), 11, CX + 60, y + 26, 300, 16, TextAnchor.MiddleLeft, MMuted, FontReg);
                if (leader && uid != id) Pill(c, R, T(id, "Kick"), null, "hub.ui kick " + uid, "ghost", CX + LW - 126, y + 11, 110, 30);
                else Box(c, R, online ? MGreen : MMuted, CX + LW - 24, y + 22, 6, 6, 3f);
            }
            float sy = CY + 52 + 16 + team.Count * 54 + 12;
            float sw = (LW - 10) / 2f;
            int free = Mathf.Min(4, 5 - team.Count);
            for (int i = 0; i < free; i++)
            {
                float x = CX + (i % 2) * (sw + 10), y = sy + (i / 2) * 86;
                if (y + 76 > MH - 52) break;
                BBox(c, R, "0.071 0.055 0.055 1", "1 1 1 0.11", x, y, sw, 76, 14f);
                Ic(c, R, "plus", x + sw / 2f - 10, y + 16, 20, MMuted);
                Text(c, R, T(id, "FreeSlot"), 12, x, y + 42, sw, 18, TextAnchor.MiddleCenter, MMuted, FontReg);
                Hit(c, R, "hub.ui chat party", x, y, sw, 76);
            }
            if (team.Count > 1 || leader) { }
            if (team.Count > 1) Pill(c, R, T(id, "LeaveGroup"), "back", "hub.ui chat party leave", "ghost", CX, MH - 92, 180, 32);

            Panel(c, R, RX, CY, RW, 196);
            Text(c, R, T(id, "PlayTogether"), 15, RX + 16, CY + 14, RW - 32, 20, TextAnchor.MiddleLeft, MInk);
            Text(c, R, T(id, "PlayTogetherSub"), 12, RX + 16, CY + 38, RW - 32, 40, TextAnchor.UpperLeft, MMuted, FontReg);
            Pill(c, R, T(id, "TeamArena"), null, "hub.ui page arena", "primary", RX + 16, CY + 98, RW - 32, 34, 12, true);
            Pill(c, R, T(id, "TeamBuild"), "build", "hub.ui play build", "ghost", RX + 16, CY + 142, RW - 32, 34);

            string clan = P("Social")?.Call("GetClanTag", id) as string;
            Panel(c, R, RX, CY + 206, RW, 170);
            Text(c, R, string.IsNullOrEmpty(clan) ? T(id, "NoClanTitle") : T(id, "ClanTitle", clan), 15, RX + 16, CY + 220, RW - 32, 20, TextAnchor.MiddleLeft, MInk);
            Text(c, R, string.IsNullOrEmpty(clan) ? T(id, "NoClanSub") : T(id, "ClanSub"), 12, RX + 16, CY + 244, RW - 32, 40, TextAnchor.UpperLeft, MMuted, FontReg);
            Pill(c, R, T(id, "OpenClans"), "flag", "hub.ui chat clan", "ghost", RX + 16, CY + 290, RW - 32, 32);
            Pill(c, R, T(id, "Tournaments"), "cup", "hub.ui chat tour", "ghost", RX + 16, CY + 330, RW - 32, 32);
        }

        // ---------- Настройки
        private void PageSettings(CuiElementContainer c, BasePlayer player, PData d)
        {
            var id = Id(player);
            const string R = UiMain;
            float hw = (CW - 10) / 2f, rx = CX + hw + 10;

            Panel(c, R, CX, CY, hw, 300);
            Text(c, R, T(id, "SetUi"), 15, CX + 16, CY + 14, hw - 32, 20, TextAnchor.MiddleLeft, MInk);
            SetRow(c, R, T(id, "SetHud"), T(id, "SetHudSub"), CX, CY + 46, hw);
            for (int k = 0; k < 3; k++)
            {
                bool a = d.HudSize == k;
                float bx = CX + hw - 16 - (3 - k) * 40 + 4;
                if (a) BBox(c, R, "0.639 0.086 0.184 0.25", "0.941 0.651 0.694 0.3", bx, CY + 54, 36, 32, 10f);
                else BBox(c, R, "1 1 1 0.04", MLine, bx, CY + 54, 36, 32, 10f);
                Text(c, R, "SML"[k].ToString(), 13, bx, CY + 54, 36, 32, TextAnchor.MiddleCenter, a ? "1 1 1 1" : MMuted);
                if (!a) Hit(c, R, "hub.ui hud2 " + k, bx, CY + 54, 36, 32);
            }
            SetRow(c, R, T(id, "SetWelcome"), T(id, "SetWelcomeSub"), CX, CY + 104, hw);
            Pill(c, R, T(id, "Show"), "eye", "hub.ui welcome", "ghost", CX + hw - 136, CY + 112, 120, 32);
            SetRow(c, R, T(id, "SetLang"), T(id, "SetLangSub"), CX, CY + 162, hw);
            TagRight(c, R, IsEn(id) ? "ENGLISH" : "РУССКИЙ", "", CX + hw - 16, CY + 172);
            SetRow(c, R, T(id, "SetCoins"), T(id, "SetCoinsSub", _config.CoinsPerHour, _config.DailyCap), CX, CY + 220, hw);

            Panel(c, R, rx, CY, hw, 300);
            Text(c, R, T(id, "SetKeys"), 15, rx + 16, CY + 14, hw - 32, 20, TextAnchor.MiddleLeft, MInk);
            string[][] keys = { new[] { "P", "KeyMenu" }, new[] { "F6", "KeyStudio" }, new[] { "F5", "KeyCam" }, new[] { "/hub", "KeyLobby" }, new[] { "/hud s|m|l", "KeyHud" } };
            for (int i = 0; i < keys.Length; i++)
            {
                float y = CY + 46 + i * 44;
                if (i > 0) Box(c, R, MLine, rx + 16, y, hw - 32, 1, 0f);
                Text(c, R, T(id, keys[i][1]), 13, rx + 16, y + 2, hw - 140, 40, TextAnchor.MiddleLeft, MInk, FontReg);
                float kw = Mathf.Max(20, TW(keys[i][0], 10) + 10);
                Kbd(c, R, keys[i][0], rx + hw - 16 - kw, y + 12);
            }
            Text(c, R, T(id, "BindHint"), 11, rx + 16, CY + 268, hw - 32, 22, TextAnchor.MiddleLeft, MMuted, FontReg);

            Panel(c, R, CX, CY + 312, CW, 112);
            Text(c, R, T(id, "SetLeave"), 15, CX + 16, CY + 326, CW - 32, 20, TextAnchor.MiddleLeft, MInk);
            Text(c, R, T(id, "SetLeaveSub", _config.LeaveDelay), 12, CX + 16, CY + 350, CW - 240, 36, TextAnchor.UpperLeft, MMuted, FontReg);
            bool inLobby = ModeOf(player) == Lobby;
            Pill(c, R, inLobby ? T(id, "InLobby") : T(id, "ToLobby", _config.LeaveDelay), "home", inLobby ? null : "hub.ui lobby", inLobby ? "off" : "primary", CX + CW - 216, CY + 340, 200, 36, 13);
        }

        private static void SetRow(CuiElementContainer c, string R, string title, string sub, float x, float y, float w)
        {
            Box(c, R, MLine, x + 16, y, w - 32, 1, 0f);
            Text(c, R, title, 13, x + 16, y + 8, w - 170, 20, TextAnchor.MiddleLeft, MInk);
            Text(c, R, sub, 11, x + 16, y + 28, w - 170, 22, TextAnchor.UpperLeft, MMuted, FontReg);
        }

        private void ShowModePopup(BasePlayer player, string mode)
        {
            var m = Mode(mode);
            if (m == null) return;
            var id = Id(player);
            const float W = 460f, H = 260f;
            var c = new CuiElementContainer();
            Window(c, UiPopup, id, ModeName(m, id).Substring(0, 1).ToUpper(), ModeName(m, id), ModeDesc(m, id), W, H, "hub.ui popup.close");
            var R = UiPopup;
            int n = GetModeOnline(mode);
            Text(c, R, m.ComingSoon ? T(id, "SoonShort") : T(id, "InGame", n), 15, 0, 88, W, 30, TextAnchor.MiddleCenter, m.ComingSoon ? ColDim : ColGreen);
            Text(c, R, T(id, "Rules_" + mode), 12, M, 118, W - 2 * M, 54, TextAnchor.MiddleCenter, ColDim, FontReg);
            float bw = (W - 2 * M - G) / 2f;
            if (m.ComingSoon) Btn(c, R, T(id, "SoonBtn"), "", ColBtn, 13, M, H - M - 46, bw, 46, ColDim, 14f);
            else Btn(c, R, T(id, "PlayBtn"), "hub.ui play " + mode, ColWine, 14, M, H - M - 46, bw, 46, ColText, 14f);
            Btn(c, R, T(id, "AllModes"), "hub.ui menu", ColBtn, 13, M + bw + G, H - M - 46, bw, 46, ColText, 14f);
            DestroyWindow(player, UiPopup);
            AddUiScaled(player, c);
        }

        #endregion

        private static float Progress(PData d, AchDef a)
        {
            int v;
            d.Stats.TryGetValue(a.Stat, out v);
            return Mathf.Clamp01(v / (float)a.Goal);
        }

        #region Достижения и размер HUD

        private class AchDef
        {
            public string Key, Stat, Ru, En, DescRu, DescEn, Title;
            public int Goal, Coins;
            public AchDef(string key, string stat, int goal, int coins, string ru, string en, string descRu, string descEn, string title = null)
            { Key = key; Stat = stat; Goal = goal; Coins = coins; Ru = ru; En = en; DescRu = descRu; DescEn = descEn; Title = title; }
        }

        // stat: счётчики, которые присылают плагины режимов через AchAdd / AchMax
        private static readonly List<AchDef> Achs = new List<AchDef>
        {
            new AchDef("first_blood", "arena_kills", 1, 10, "Первая кровь", "First blood", "Убей игрока на арене", "Get a kill in the arenas"),
            new AchDef("kills_100", "arena_kills", 100, 100, "Сотня", "Hundred", "100 убийств на аренах", "100 arena kills"),
            new AchDef("kills_1000", "arena_kills", 1000, 500, "Мясник", "Butcher", "1000 убийств на аренах", "1000 arena kills", "Мясник"),
            new AchDef("hs_100", "arena_hs", 100, 150, "Снайпер", "Sniper", "100 хедшотов", "100 headshot kills", "Снайпер"),
            new AchDef("streak_10", "arena_streak", 10, 100, "Неудержимый", "Unstoppable", "Серия из 10 убийств", "10 kill streak"),
            new AchDef("duel_10", "duel_wins", 10, 100, "Дуэлянт", "Duelist", "10 побед в дуэлях", "Win 10 duels"),
            new AchDef("duel_50", "duel_wins", 50, 300, "Мастер дуэлей", "Duel master", "50 побед в дуэлях", "Win 50 duels", "Дуэлянт"),
            new AchDef("gungame_1", "gungame_wins", 1, 50, "Ган-гейм", "Gun game", "Пройди всю лестницу оружия", "Finish the gun ladder"),
            new AchDef("ranked_1", "ranked_wins", 1, 50, "Первая победа", "First win", "Выиграй рейтинговый матч", "Win a ranked match"),
            new AchDef("ranked_50", "ranked_wins", 50, 400, "Ветеран ранкеда", "Ranked veteran", "50 побед в ранкеде", "50 ranked wins"),
            new AchDef("rank_gold", "ranked_rank", 3, 200, "Золото", "Gold", "Дойди до ранга Золото", "Reach Gold rank"),
            new AchDef("rank_legend", "ranked_rank", 6, 1000, "Легенда", "Legend", "Дойди до ранга Легенда", "Reach Legend rank", "Легенда"),
            new AchDef("fc_25", "fc_wins", 25, 150, "Фейсчекер", "Facechecker", "25 выигранных фейсчеков", "Win 25 facechecks"),
            new AchDef("aim_20", "aim_sessions", 20, 100, "Аимер", "Aimer", "20 тренировок в аим-трене", "20 aim training sessions"),
            new AchDef("roam_50", "roam_kills", 50, 150, "Роумер", "Roamer", "50 убийств в роуме", "50 roam kills"),
            new AchDef("heli_10", "heli_kills", 10, 150, "Пилот", "Pilot", "10 сбитых хели", "Down 10 helis", "Пилот"),
            new AchDef("bedwars_5", "bedwars_wins", 5, 150, "Защитник кровати", "Bed defender", "5 побед в Bedwars", "Win 5 Bedwars games"),
            new AchDef("raid_5", "raidwars_wins", 5, 150, "Рейдер", "Raider", "5 побед в «Рейд против рейда»", "Win 5 Raid Wars", "Рейдер"),
            new AchDef("tour_1", "tour_wins", 1, 300, "Чемпион", "Champion", "Выиграй турнир", "Win a tournament", "Чемпион"),
            new AchDef("team_tour_1", "team_tour_wins", 1, 300, "Командный чемпион", "Team champion", "Выиграй командный турнир", "Win a team tournament"),
            new AchDef("build_1000", "build_blocks", 1000, 100, "Строитель", "Builder", "Поставь 1000 блоков в стройке", "Place 1000 blocks"),
            new AchDef("bp_5", "bp_saved", 5, 100, "Архитектор", "Architect", "Сохрани 5 чертежей", "Save 5 blueprints", "Архитектор"),
            new AchDef("time_10h", "play_minutes", 600, 100, "Завсегдатай", "Regular", "10 часов на сервере", "10 hours online"),
            new AchDef("modes_8", "modes_played", 8, 150, "Всё попробовал", "Tried it all", "Сыграй в 8 разных режимов", "Play 8 different modes"),
        };

        private static readonly float[] HudScales = { 0.65f, 0.8f, 1f };

        [HookMethod("GetHudScale")]
        public float GetHudScale(ulong id) => HudScales[Mathf.Clamp(D(id).HudSize, 0, 2)];

        [HookMethod("AchAdd")]
        public void AchAdd(ulong id, string stat, int amount)
        {
            if (id == 0UL || amount == 0 || string.IsNullOrEmpty(stat)) return;
            var d = D(id);
            int cur;
            d.Stats.TryGetValue(stat, out cur);
            d.Stats[stat] = cur + amount;
            _dirty = true;
            CheckAch(id, stat);
        }

        [HookMethod("AchMax")]
        public void AchMax(ulong id, string stat, int value)
        {
            if (id == 0UL || string.IsNullOrEmpty(stat)) return;
            var d = D(id);
            int cur;
            if (d.Stats.TryGetValue(stat, out cur) && cur >= value) return;
            d.Stats[stat] = value;
            _dirty = true;
            CheckAch(id, stat);
        }

        [HookMethod("GetAchTitles")]
        public List<string> GetAchTitles(ulong id)
        {
            var d = D(id);
            return Achs.Where(a => a.Title != null && d.Achs.Contains(a.Key)).Select(a => a.Title).ToList();
        }

        private void CheckAch(ulong id, string stat)
        {
            var d = D(id);
            int v;
            d.Stats.TryGetValue(stat, out v);
            foreach (var a in Achs)
            {
                if (a.Stat != stat || v < a.Goal || d.Achs.Contains(a.Key)) continue;
                d.Achs.Add(a.Key);
                if (a.Coins > 0) AddPrize(id, a.Coins, "");
                var p = BasePlayer.FindByID(id);
                if (p != null && p.IsConnected)
                {
                    AchPopup(p, a);
                    if (_config.AchBroadcast) foreach (var o in BasePlayer.activePlayerList) if (o != p) o.ChatMessage(T(Id(o), "AchAll", p.displayName, IsEn(Id(o)) ? a.En : a.Ru));
                }
            }
        }

        private const string UiAch = "Hub.Ach";

        private void AchPopup(BasePlayer p, AchDef a)
        {
            var id = Id(p);
            var c = new CuiElementContainer();
            const float w = 360f, h = 74f;
            c.Add(new CuiPanel { Image = { Color = "0.09 0.075 0.075 0.94" }, RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = $"{-w / 2f} {-h - 90}", OffsetMax = $"{w / 2f} -90" } }, "Hud", UiAch);
            c.Add(new CuiPanel { Image = { Color = ColWine }, RectTransform = { AnchorMin = "0 0", AnchorMax = "0 1", OffsetMin = "0 0", OffsetMax = "6 0" } }, UiAch);
            c.Add(new CuiLabel { Text = { Text = T(id, "AchGot"), FontSize = 10, Align = TextAnchor.UpperLeft, Color = ColRose, Font = Font }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "18 0", OffsetMax = "-12 -8" } }, UiAch);
            c.Add(new CuiLabel { Text = { Text = (IsEn(id) ? a.En : a.Ru).ToUpper(), FontSize = 17, Align = TextAnchor.MiddleLeft, Color = ColText, Font = Font }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "18 6", OffsetMax = "-90 -14" } }, UiAch);
            c.Add(new CuiLabel { Text = { Text = IsEn(id) ? a.DescEn : a.DescRu, FontSize = 10, Align = TextAnchor.LowerLeft, Color = ColDim, Font = FontReg }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "18 8", OffsetMax = "-90 0" } }, UiAch);
            if (a.Coins > 0) c.Add(new CuiLabel { Text = { Text = "+" + a.Coins, FontSize = 18, Align = TextAnchor.MiddleRight, Color = ColGold, Font = Font }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "0 0", OffsetMax = "-14 0" } }, UiAch);
            CuiHelper.DestroyUi(p, UiAch);
            AddUiScaled(p, c);
            timer.Once(5f, () => { if (p != null && p.IsConnected) CuiHelper.DestroyUi(p, UiAch); });
        }

        [ChatCommand("hud")]
        private void CmdHud(BasePlayer player, string command, string[] args)
        {
            var id = Id(player);
            var a = args.Length > 0 ? args[0].ToLower() : "";
            int v = a == "s" ? 0 : a == "m" ? 1 : a == "l" ? 2 : -1;
            if (v < 0) { Reply(player, T(id, "HudHelp", "SML"[D(id).HudSize])); return; }
            D(id).HudSize = v;
            _dirty = true;
            Reply(player, T(id, "HudSet", "SML"[v]));
        }

        // ---------- уменьшение HUD (тот же код, что в плагинах режимов) ----------
        private static readonly HashSet<string> HudRootLayers = new HashSet<string> { "Hud", "Hud.Menu", "Under", "Overlay", "Overall" };

        private static string ScaleOffset(string v, float k)
        {
            if (string.IsNullOrEmpty(v)) return v;
            var parts = v.Split(' ');
            float x, y;
            if (parts.Length != 2
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return v;
            return (x * k).ToString("0.##", CultureInfo.InvariantCulture) + " " + (y * k).ToString("0.##", CultureInfo.InvariantCulture);
        }

        private void AddUiScaled(BasePlayer p, CuiElementContainer c)
        {
            float k = GetHudScale(Id(p));
            if (Mathf.Abs(k - 1f) > 0.01f)
            {
                var scaled = new HashSet<string>();
                foreach (var e in c)
                {
                    bool root = HudRootLayers.Contains(e.Parent ?? "");
                    if (root) { if (e.Components.Any(x => x is CuiNeedsCursorComponent || x is CuiNeedsKeyboardComponent)) continue; }
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
            CuiHelper.AddUi(p, c);
        }

        #endregion

        #region Интерфейс: профиль

        private void ShowProfile(BasePlayer viewer, ulong target)
        {
            var id = Id(viewer);
            var d = D(target);
            bool own = id == target;
            const float W = 980f, H = 620f;
            var c = new CuiElementContainer();
            int totalMin = d.Minutes.Values.Sum();
            Window(c, UiMain, id, string.IsNullOrEmpty(d.Name) ? "P" : d.Name.Substring(0, 1).ToUpper(), T(id, "ProfileTitle", d.Name ?? target.ToString()), T(id, "ProfileSub", d.FirstSeen ?? "—", d.LastSeen ?? "—"), W, H, "hub.ui close");
            var R = UiMain;
            float top = M + 52 + 2 + M;

            // плитки + размер HUD (только в своём профиле)
            var tiles = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(d.Coins.ToString(), T(id, "TCoins")),
                new KeyValuePair<string, string>((totalMin / 60) + " " + T(id, "H"), T(id, "TTime")),
                new KeyValuePair<string, string>(d.Achs.Count + " / " + Achs.Count, T(id, "TAchs")),
                new KeyValuePair<string, string>(NameOf(d.Mode, id), T(id, "TNow")),
            };
            int n = tiles.Count + (own ? 1 : 0);
            float tw = (W - 2 * M - (n - 1) * G) / n;
            for (int i = 0; i < tiles.Count; i++)
            {
                float x = M + i * (tw + G);
                Box(c, R, ColPanel, x, top, tw, 64, 14f);
                Text(c, R, tiles[i].Key, 20, x, top + 8, tw, 28, TextAnchor.MiddleCenter, i == 0 ? ColGold : ColText);
                Text(c, R, tiles[i].Value, 10, x, top + 38, tw, 16, TextAnchor.MiddleCenter, ColDim, FontReg);
            }
            if (own)
            {
                float x = M + tiles.Count * (tw + G);
                Box(c, R, ColPanel, x, top, tw, 64, 14f);
                float cw3 = (tw - 24 - 8) / 3f;
                for (int k = 0; k < 3; k++)
                    Btn(c, R, "SML"[k].ToString(), "hub.ui hud " + k, d.HudSize == k ? ColWine : ColBtn, 13, x + 12 + k * (cw3 + 4), top + 8, cw3, 28, d.HudSize == k ? ColText : ColDim, 9f);
                Text(c, R, T(id, "THud"), 10, x, top + 40, tw, 16, TextAnchor.MiddleCenter, ColDim, FontReg);
            }
            top += 64 + G;

            float cw = (W - 2 * M - G) / 2f, ph = 168f;
            Box(c, R, ColPanel, M, top, cw, ph, 16f);
            Text(c, R, T(id, "TimeByMode"), 10, M + 14, top + 10, cw - 28, 16, TextAnchor.MiddleLeft, ColDim);
            var byMode = d.Minutes.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(4).ToList();
            int max = byMode.Count > 0 ? byMode[0].Value : 1;
            for (int i = 0; i < byMode.Count; i++)
            {
                float y = top + 32 + i * 33;
                Text(c, R, NameOf(byMode[i].Key, id), 12, M + 14, y, cw - 100, 16, TextAnchor.MiddleLeft, ColText, FontReg);
                Text(c, R, (byMode[i].Value / 60f).ToString("0.#") + " " + T(id, "H"), 12, M + cw - 84, y, 70, 16, TextAnchor.MiddleRight, ColDim);
                Bar(c, R, M + 14, y + 19, cw - 28, 5, byMode[i].Value / (float)max, ColWine);
            }
            if (byMode.Count == 0) Text(c, R, T(id, "NoData"), 12, M, top, cw, ph, TextAnchor.MiddleCenter, ColDim, FontReg);

            float rx = M + cw + G;
            Box(c, R, ColPanel, rx, top, cw, ph, 16f);
            Text(c, R, T(id, "Stats"), 10, rx + 14, top + 10, cw - 28, 16, TextAnchor.MiddleLeft, ColDim);
            var extra = new List<string>();
            foreach (var pl in plugins.GetAll())
            {
                if (pl == null || pl == this) continue;
                var r = pl.Call("OnHubProfile", target, IsEn(id) ? "en" : "ru") as string;
                if (!string.IsNullOrEmpty(r)) extra.Add(r);
            }
            if (extra.Count == 0) Text(c, R, T(id, "StatsSoon"), 11, rx + 14, top + 32, cw - 28, ph - 40, TextAnchor.UpperLeft, ColDim, FontReg);
            else Text(c, R, string.Join("\n", extra.ToArray()), 11, rx + 14, top + 32, cw - 28, ph - 40, TextAnchor.UpperLeft, ColText, FontReg);
            top += ph + G;

            // достижения: 6 × 4
            float ah = H - M - top;
            Box(c, R, ColPanel, M, top, W - 2 * M, ah, 16f);
            Text(c, R, T(id, "Achievements", d.Achs.Count, Achs.Count), 10, M + 14, top + 8, W - 2 * M - 28, 16, TextAnchor.MiddleLeft, ColDim);
            const int cols = 6, rows = 4;
            float gx = M + 12, gy = top + 30, bw = (W - 2 * M - 24 - (cols - 1) * 6) / cols, bh = (ah - 30 - 12 - (rows - 1) * 6) / rows;
            var ordered = Achs.OrderByDescending(a => d.Achs.Contains(a.Key)).ThenByDescending(a => Progress(d, a)).Take(cols * rows).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                var a = ordered[i];
                bool done = d.Achs.Contains(a.Key);
                float p = Progress(d, a);
                float x = gx + (i % cols) * (bw + 6), y = gy + (i / cols) * (bh + 6);
                Box(c, R, done ? ColWine : ColBtn, x, y, bw, bh, 10f);
                Text(c, R, IsEn(id) ? a.En : a.Ru, 11, x + 9, y + 4, bw - 18, 18, TextAnchor.MiddleLeft, done ? ColText : ColDim);
                int v; d.Stats.TryGetValue(a.Stat, out v);
                Text(c, R, done ? (a.Coins > 0 ? "+" + a.Coins : "OK") : Mathf.Min(v, a.Goal) + "/" + a.Goal, 9, x + 9, y + 4, bw - 18, 18, TextAnchor.MiddleRight, done ? ColGold : ColDim, FontReg);
                if (!done) Bar(c, R, x + 9, y + bh - 12, bw - 18, 4, p, ColRose);
                else Text(c, R, IsEn(id) ? a.DescEn : a.DescRu, 9, x + 9, y + bh - 20, bw - 18, 16, TextAnchor.MiddleLeft, ColRose, FontReg);
            }

            DestroyWindow(viewer, UiMain);
            AddUiScaled(viewer, c);
        }

        #endregion

        #region Интерфейс: тосты и значок лобби

        private void Toast(BasePlayer player, string text, bool warn)
        {
            if (player == null || !player.IsConnected) return;
            var c = new CuiElementContainer();
            Root(c, UiToast, "Hud", warn ? "0.40 0.06 0.11 0.92" : "0.07 0.065 0.065 0.90", "0.5 1", "0.5 1", "-240 -76", "240 -34", 20f, false);
            c.Add(new CuiLabel { Text = { Text = text, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = ColText, Font = Font }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, UiToast);
            CuiHelper.DestroyUi(player, UiToast);
            AddUiScaled(player, c);
            var p = player;
            timer.Once(3f, () => { if (p != null && p.IsConnected && !_transfer.ContainsKey(Id(p))) CuiHelper.DestroyUi(p, UiToast); });
        }

        private void ShowBadge(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;
            var id = Id(player);
            var c = new CuiElementContainer();
            Root(c, UiBadge, "Hud", "0.07 0.065 0.065 0.86", "0 1", "0 1", "16 -54", "336 -16", 19f, false);
            c.Add(new CuiLabel { Text = { Text = T(id, "Badge", D(id).Coins), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText, Font = Font }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, UiBadge);
            CuiHelper.DestroyUi(player, UiBadge);
            AddUiScaled(player, c);
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
            // --- меню хаба 1.3
            new[] { "BActWindow", "Окно стройки", "Build window" },
            new[] { "BActPlot", "Мой участок", "My plot" },
            new[] { "BActBp", "Чертежи", "Blueprints" },
            new[] { "BActFly", "Полёт", "Fly" },
            new[] { "BActRaid", "Рейд-тест", "Raid test" },
            new[] { "BActHome", "Домой", "Home" },
            new[] { "FTakes", "Дубли", "Takes" },
            new[] { "FCamera", "Камера", "Camera" },
            new[] { "FFx", "Эффекты", "Effects" },
            new[] { "FWeather", "Погода", "Weather" },
            new[] { "FKits", "Наборы", "Kits" },
            new[] { "FMotor", "Мотор для всех", "Action for all" },
            new[] { "Page_home", "Главная", "Home" },
            new[] { "Page_build", "Стройка", "Build" },
            new[] { "Page_film", "Съёмки", "Filming" },
            new[] { "Page_arena", "Арены", "Arenas" },
            new[] { "Page_aim", "Аим-трен", "Aim train" },
            new[] { "Page_mini", "Мини-игры", "Minigames" },
            new[] { "Page_mm", "ММ", "Matchmaking" }, new[] { "PageSub_mm", "Все матчмейкинги и лидеры", "All matchmaking and leaders" },
            new[] { "MmS_ak", "одинаковый АК · 1v1–5v5", "same AK · 1v1–5v5" }, new[] { "MmS_mix", "случайная пушка каждый раунд", "random gun every round" },
            new[] { "MmS_t2", "Томпсон, MP5, SAR", "Thompson, MP5, SAR" }, new[] { "MmS_prim", "лук, Eoka, револьвер, копьё", "bow, Eoka, revolver, spear" },
            new[] { "MmS_bow", "лук и арбалет", "bow & crossbow" }, new[] { "MmS_heli", "атак-хели и мини · до 3 побед", "attack heli & mini · first to 3" },
            new[] { "MmS_facecheck", "против игрока · до 7 побед", "vs a player · first to 7" },
            new[] { "MmLeaders", "Лидеры", "Leaders" }, new[] { "MmNoLeaders", "пока никто не играл — будь первым", "no one yet — be the first" },
            new[] { "MmWins", "{0} побед", "{0} wins" }, new[] { "MmFindShort", "Найти", "Find" }, new[] { "MmNoRating", "без рейтинга", "unrated" },
            new[] { "Page_shop", "Магазин", "Shop" },
            new[] { "Page_profile", "Профиль", "Profile" },
            new[] { "Page_group", "Группа", "Group" },
            new[] { "Page_settings", "Настройки", "Settings" },
            new[] { "PageSub_home", "Всё важное — в одном месте", "Everything in one place" },
            new[] { "PageSub_build", "Свобода строить. Пространство создавать.", "Freedom to build" },
            new[] { "PageSub_film", "Твоя сцена. Твои истории.", "Your scene. Your stories." },
            new[] { "PageSub_arena", "Выбери соперника и свой формат", "Pick your opponent and format" },
            new[] { "PageSub_aim", "Точность начинается с практики", "Accuracy starts with practice" },
            new[] { "PageSub_mini", "Другие правила. Тот же Rust.", "Different rules. Same Rust." },
            new[] { "PageSub_shop", "Твой стиль — твой выбор", "Your style, your choice" },
            new[] { "PageSub_profile", "Прогресс, достижения и статистика", "Progress, achievements and stats" },
            new[] { "PageSub_group", "Лучше вместе", "Better together" },
            new[] { "PageSub_settings", "Сделай интерфейс удобным для себя", "Make the interface yours" },
            new[] { "Tab_all", "Все", "All" },
            new[] { "Tab_ffa", "FFA / TDM", "FFA / TDM" },
            new[] { "Tab_ranked", "Ранкед", "Ranked" },
            new[] { "Tab_duels", "Дуэли", "Duels" },
            new[] { "Tab_overview", "Обзор", "Overview" },
            new[] { "Tab_achs", "Достижения", "Achievements" },
            new[] { "CardSub_film", "Боты, камеры и постановки", "Bots, cameras and scenes" },
            new[] { "CardSub_arena", "FFA, дуэли и ранкед", "FFA, duels and ranked" },
            new[] { "CardSub_aim", "Мишени, боты, фейсчеки", "Targets, bots, facechecks" },
            new[] { "FootMenu", "открыть / закрыть меню", "open / close menu" },
            new[] { "FootOnline", "онлайн {0} / {1}", "online {0} / {1}" },
            new[] { "CoinsShort", "{0} монет", "{0} coins" },
            new[] { "Welcome", "С возвращением, {0}", "Welcome back, {0}" },
            new[] { "HomeTitle", "Что создадим сегодня?", "What shall we make today?" },
            new[] { "HomeSub", "Последний режим — «{0}». Можно продолжить с того же места.", "Last mode — {0}. Pick up where you left off." },
            new[] { "HomeHere", "Ты сейчас в режиме «{0}».", "You are in {0} right now." },
            new[] { "Continue", "Продолжить: {0}", "Continue: {0}" },
            new[] { "ChooseWhat", "Выбери, чем заняться", "Pick what to do" },
            new[] { "PlayersOnline", "{0} игроков онлайн", "{0} players online" },
            new[] { "Open", "Открыть", "Open" },
            new[] { "Progress", "Твой прогресс", "Your progress" },
            new[] { "RankMmr", "ранг · {0} MMR", "rank · {0} MMR" },
            new[] { "NoRank", "без ранга", "unranked" },
            new[] { "PlayerTag", "ИГРОК", "PLAYER" },
            new[] { "YourBalance", "Твой баланс", "Your balance" },
            new[] { "OpenShop", "Открыть магазин", "Open shop" },
            new[] { "YourGroup", "Твоя группа", "Your group" },
            new[] { "GroupAlone", "Пока играешь один. Позови друзей — режимы и арены можно проходить вместе.", "Playing solo. Invite friends to play modes together." },
            new[] { "Offline", "не в сети", "offline" },
            new[] { "Invite", "Пригласить", "Invite" },
            new[] { "NoteCap", "Заметка студии / 01", "Studio note / 01" },
            new[] { "NoteTitle", "Истории начинаются с кадра", "Stories start with a frame" },
            new[] { "NoteText", "Собери сцену, запиши дубль и найди свой ракурс.", "Set up a scene, record a take, find your angle." },
            new[] { "ToStudio", "В студию", "To studio" },
            new[] { "BuildHeroCap", "Креатив-стройка", "Creative build" },
            new[] { "BuildHeroTitle", "Строй без ограничений", "Build without limits" },
            new[] { "BuildHeroSub", "Полёт, бесплатные материалы, симметрия, чертежи и рейд-тест своей базы.", "Flight, free materials, symmetry, blueprints and raid tests." },
            new[] { "BuildToolsHere", "Ты в стройке — команды ниже сработают сразу.", "You are in build mode — the actions below work right away." },
            new[] { "BuildToolsAway", "Инструменты работают только в режиме «Стройка». Сначала войди в режим.", "Tools only work in Build mode. Enter the mode first." },
            new[] { "QuickActions", "Быстрые действия", "Quick actions" },
            new[] { "QuickActionsSub", "то же, что команды в чате", "same as chat commands" },
            new[] { "Run", "Выполнить", "Run" },
            new[] { "BActWindowSub", "инструменты, зоны, погода, HUD", "tools, zones, weather, HUD" },
            new[] { "BActPlotSub", "занять, отдать, позвать друга", "claim, release, add a friend" },
            new[] { "BActBpSub", "библиотека построек", "library of builds" },
            new[] { "BActFlySub", "включить или выключить", "toggle on or off" },
            new[] { "BActRaidSub", "откат, друг рейдит, путь до ТШ", "rollback, friend raid, path to TC" },
            new[] { "BActHomeSub", "на точку /sethome", "to your /sethome spot" },
            new[] { "EnterMode", "Войти: {0}", "Enter: {0}" },
            new[] { "FilmHeroCap", "Студия VideoRecorder", "VideoRecorder studio" },
            new[] { "FilmHeroTitle", "Снимай ролики с ботами", "Shoot videos with bots" },
            new[] { "FilmHeroSub", "Записывай дубли — боты повторят каждое движение, а камера пролетит по точкам.", "Record takes — bots repeat every move, the camera flies a path." },
            new[] { "FilmWhat", "Что есть в студии", "What the studio has" },
            new[] { "FTakesSub", "каждая запись — бот-актёр со своим таймингом", "each take is a bot actor with its own timing" },
            new[] { "FCameraSub", "полёт по точкам, облёт, штатив", "fly-through, orbit, tripod" },
            new[] { "FFxSub", "взрывы, дым, ракеты по таймлайну", "explosions, smoke, rockets on the timeline" },
            new[] { "FWeatherSub", "10 пресетов и время суток", "10 presets and time of day" },
            new[] { "FKitsSub", "сохрани инвентарь и надень на дубль", "save an inventory, put it on a take" },
            new[] { "FMotorSub", "общий отсчёт для всей команды", "one countdown for the whole team" },
            new[] { "Hotkeys", "Горячие клавиши", "Hotkeys" },
            new[] { "HkF5", "Кинокамера", "Cine camera" },
            new[] { "HkF6", "Панель студии", "Studio panel" },
            new[] { "HkF7", "Сцена: пуск / стоп", "Scene: play / stop" },
            new[] { "HkF8", "Запись дубля", "Record a take" },
            new[] { "HkF9", "Пауза", "Pause" },
            new[] { "HkF10", "Слоу-мо", "Slow-mo" },
            new[] { "HkF11", "Мотор для всех", "Action for all" },
            new[] { "OpenStudio", "Открыть студию", "Open studio" },
            new[] { "RankHint", "Сыграй рейтинговый матч, чтобы получить ранг", "Play a ranked match to get a rank" },
            new[] { "Leaderboard", "Таблица лидеров", "Leaderboard" },
            new[] { "ArenaHint", "Играй сразу или собери команду в «Группе».", "Jump in or gather a team in Group." },
            new[] { "DuelBtn", "Вызвать на дуэль", "Challenge to a duel" },
            new[] { "ArenaBrowser", "Все арены", "All arenas" },
            new[] { "RankedFoot", "Равные условия · без преимуществ VIP", "Equal kits · no VIP perks" },
            new[] { "ArenaFoot", "Быстрый вход · твой инвентарь сохранён", "Quick join · your inventory is kept" },
            new[] { "NoModes", "Здесь пока нет режимов", "No modes here yet" },
            new[] { "AimCap", "Личная тренировка", "Personal training" },
            new[] { "AimTitle", "Чище прицел. Быстрее реакция.", "Cleaner aim. Faster reactions." },
            new[] { "AimSub", "Мишени, боты, зеркало и фейсчеки — с бесконечными патронами.", "Targets, bots, mirror and facechecks — with infinite ammo." },
            new[] { "MmFind", "Найти матч", "Find match" }, new[] { "MmQueue", "{0} в поиске", "{0} searching" },
            new[] { "MmTagRanked", "Матчмейкинг · рейтинг", "Matchmaking · ranked" }, new[] { "MmTagHeli", "Матчмейкинг · вертолёты", "Matchmaking · helicopters" }, new[] { "MmTagFc", "Матчмейкинг · 1 на 1", "Matchmaking · 1v1" },
            new[] { "Mm_ak", "Ранкед: калаши", "Ranked: AK" }, new[] { "MmD_ak", "АК у всех одинаковый · 1v1–5v5", "same AK for all · 1v1–5v5" },
            new[] { "Mm_mix", "Ранкед: разные ганы", "Ranked: mixed guns" }, new[] { "MmD_mix", "каждый раунд случайная пушка и сет", "random gun and kit each round" },
            new[] { "Mm_t2", "Ранкед: тир 2", "Ranked: Tier 2" }, new[] { "MmD_t2", "Томпсон, MP5, SAR · роадсайн", "Thompson, MP5, SAR · roadsign" },
            new[] { "Mm_prim", "Ранкед: примитив", "Ranked: Primitive" }, new[] { "MmD_prim", "лук, арбалет, Eoka, револьвер, копьё", "bow, crossbow, Eoka, revolver, spear" },
            new[] { "Mm_bow", "Ранкед: луки", "Ranked: Bows" }, new[] { "MmD_bow", "лук и арбалет · шкурный сет", "bow & crossbow · hide set" },
            new[] { "Mm_heli", "Вертолётный ММ", "Heli MM" }, new[] { "MmD_heli", "атак-хели 1v1 и 2v2, мини с ракетами · до 3 побед", "attack heli 1v1 & 2v2, rocket minis · first to 3" },
            new[] { "Mm_facecheck", "Фейсчеки 1v1", "Facechecks 1v1" }, new[] { "MmD_facecheck", "против живого игрока · дверь по таймеру · до 7 побед", "vs a real player · timed door · first to 7" },
            new[] { "AimSub3", "Выбери режим — откроется аим-трен с ним. Фейсчеки 1v1 — в разделе «ММ».", "Pick a mode. Facechecks 1v1 are in the MM section." },
            new[] { "AimSub2", "Выбери режим — откроется аим-трен с ним. Патроны бесконечные.", "Pick a mode — aim train opens with it. Infinite ammo." },
            new[] { "AimM_targets", "Мишени", "Targets" }, new[] { "AimMD_targets", "стоят на дистанции 10–250 м", "standing at 10–250 m" },
            new[] { "AimM_strafers", "Стрейферы", "Strafers" }, new[] { "AimMD_strafers", "бегают и прыгают", "run and jump" },
            new[] { "AimM_flow", "Aim Flow", "Aim Flow" }, new[] { "AimMD_flow", "всегда в движении, веди прицел", "always moving, track them" },
            new[] { "AimM_shooters", "Стреляют в ответ", "Shoot back" }, new[] { "AimMD_shooters", "убей первым", "kill first" },
            new[] { "AimM_mirror", "Бот-зеркало", "Mirror bot" }, new[] { "AimMD_mirror", "повторяет твои движения", "copies your moves" },
            new[] { "AimM_bow", "Лук", "Bow" }, new[] { "AimMD_bow", "лук и арбалет, 10–70 м", "bow & crossbow, 10–70 m" },
            new[] { "AimM_fcbot", "Фейсчек с ботом", "Facecheck vs bot" }, new[] { "AimMD_fcbot", "дверь по таймеру", "timed door" },
            new[] { "AimM_facecheck", "Фейсчеки 1v1", "Facechecks 1v1" }, new[] { "AimMD_facecheck", "против игрока, до 7 побед", "vs a player, first to 7" },
            new[] { "AimFoot_aim", "мишени · Aim Flow · лук · зеркало", "targets · Aim Flow · bow · mirror" },
            new[] { "AimFoot_facecheck", "дверь по таймеру · игрок или бот", "timed door · player or bot" },
            new[] { "YourShooting", "Твоя стрельба", "Your shooting" },
            new[] { "Accuracy", "точность на аренах", "arena accuracy" },
            new[] { "HitsOf", "{0} из {1} попаданий", "{0} of {1} hits" },
            new[] { "KD", "K / D", "K / D" },
            new[] { "Headshots", "Хедшоты", "Headshots" },
            new[] { "BestStreak", "Лучшая серия", "Best streak" },
            new[] { "NoStats", "Статистика появится после первых боёв на аренах.", "Stats appear after your first arena fights." },
            new[] { "AimCmds", "Окна тренировок", "Training windows" },
            new[] { "AimMenuBtn", "Аим-трен · /aim", "Aim train · /aim" },
            new[] { "FcMenuBtn", "Фейсчеки · /fc", "Facechecks · /fc" },
            new[] { "AimCmdsSub", "Настройки оружия и сложности — в окне тренировки.", "Weapon and difficulty settings are in the training window." },
            new[] { "MiniBanner", "Выход из мини-игры возвращает в хаб. Инвентарь других режимов сохранится.", "Leaving a minigame returns you to the hub. Other modes keep their inventories." },
            new[] { "MiniFoot", "Правила и счёт — в окне режима: /mg", "Rules and scores are in the mode window: /mg" },
            new[] { "Rules_roam", "Открытая PvP-зона: лут можно потерять", "Open PvP zone: you can lose loot" },
            new[] { "Rules_heli", "Экипаж: пилот и стрелок, побеждает лучший счёт", "Crew: pilot and gunner, best score wins" },
            new[] { "Rules_bedwars", "Пока кровать цела — команда возрождается", "While your bed stands, your team respawns" },
            new[] { "Rules_raidwars", "Сначала строим базу, затем рейдим соперника", "Build first, then raid the other team" },
            new[] { "Rules_ffa", "Каждый сам за себя, оружие задаёт арена", "Free for all, the arena sets the gun" },
            new[] { "Rules_duels", "1 на 1 или своя комната с паролем", "1v1 or your own room with a password" },
            new[] { "Rules_ranked_ak", "Рейтинговые матчи с АК, от 1v1 до 5v5", "Ranked AK matches, 1v1 to 5v5" },
            new[] { "Rules_ranked_mix", "Случайное оружие и набор каждый раунд", "Random gun and kit every round" },
            new[] { "Rules_ranked_t2", "Томпсон, MP5 или SAR и дорожная броня", "Thompson, MP5 or SAR with roadsign armor" },
            new[] { "Rules_maze", "Лабиринт из ледяных стен, каждый раунд новый", "Ice-wall maze, new every round" },
            new[] { "Rules_walls", "Пати на пати среди больших стен", "Party vs party among high walls" },
            new[] { "Rules_bows", "Лук и арбалет, все против всех", "Bow and crossbow, free for all" },
            new[] { "Rules_speargun", "Гарпуны и водолазный сет", "Spearguns and diving gear" },
            new[] { "Rules_helimm", "1v1 / 2v2, до 3 побед, рейтинг в /ranked", "1v1 / 2v2, first to 3, rated in /ranked" },
            new[] { "BActElec", "Электрика", "Electricity" }, new[] { "BActElecSub", "схемы, копия, нагрузка", "circuits, copy, power view" },
            new[] { "BActSpawn", "Спавн", "Spawn" }, new[] { "BActSpawnSub", "деревья, руда, ящики, турели", "trees, ore, crates, turrets" },
            new[] { "BActZone", "PvP на участке", "Plot PvP" }, new[] { "BActZoneSub", "включи бой на своём участке", "enable fights on your plot" },
            new[] { "Rules_ranked_prim", "Луки, арбалет, Eoka и револьвер", "Bows, crossbow, Eoka and revolver" },
            new[] { "Rules_primitive", "Копья, луки и арбалеты", "Spears, bows and crossbows" },
            new[] { "Rules_aim", "Тренировка без урона по игрокам", "Practice with no player damage" },
            new[] { "Rules_facecheck", "Дверь открывается — кто быстрее", "The door opens — who is faster" },
            new[] { "Rules_build", "Креатив: полёт и бесплатные материалы", "Creative: flight and free materials" },
            new[] { "Rules_film", "Съёмки с ботами и камерой", "Filming with bots and a camera" },
            new[] { "ShopHint", "Косметика и удобство. Без преимуществ в бою.", "Cosmetics and comfort. No combat advantage." },
            new[] { "Cat_skins", "Скины", "Skins" },
            new[] { "Cat_title", "Титулы", "Titles" },
            new[] { "Cat_color", "Цвет ника", "Name color" },
            new[] { "Cat_vip", "VIP", "VIP" },
            new[] { "ShopCat_skins", "Скины на оружие и одежду — работают во всех режимах", "Weapon and clothing skins — in every mode" },
            new[] { "ShopCat_title", "Титул рядом с именем в чате", "A title next to your name in chat" },
            new[] { "ShopCat_color", "Цвет имени в чате", "Your name color in chat" },
            new[] { "ShopCat_vip", "Титулы, цвета, светящиеся глаза и приоритет входа", "Titles, colors, glowing eyes and join priority" },
            new[] { "Owned", "КУПЛЕНО", "OWNED" },
            new[] { "TitleSample", "Режиссёр", "Director" },
            new[] { "ProfileSince", "На сервере с {0}", "On the server since {0}" },
            new[] { "AchOpened", "Открыто {0} из {1} достижений", "{0} of {1} achievements unlocked" },
            new[] { "AchDone", "получено", "unlocked" },
            new[] { "TimeByModeCap", "Время по режимам", "Time by mode" },
            new[] { "StatsCap", "Статистика", "Stats" },
            new[] { "GroupOf", "Группа {0}", "{0}'s group" },
            new[] { "GroupCount", "{0} / 5 участников · вход по приглашению", "{0} / 5 members · invite only" },
            new[] { "LeaderTag", "ЛИДЕР", "LEADER" },
            new[] { "You", "ты", "you" },
            new[] { "Kick", "Исключить", "Kick" },
            new[] { "FreeSlot", "Свободное место", "Free slot" },
            new[] { "LeaveGroup", "Выйти из группы", "Leave group" },
            new[] { "PlayTogether", "Играйте вместе", "Play together" },
            new[] { "PlayTogetherSub", "Лидер выбирает режим — группа идёт следом.", "The leader picks a mode, the group follows." },
            new[] { "TeamArena", "Командная арена", "Team arena" },
            new[] { "TeamBuild", "Совместная стройка", "Build together" },
            new[] { "NoClanTitle", "Клан", "Clan" },
            new[] { "NoClanSub", "Ты пока не в клане. Создай свой или прими приглашение.", "Not in a clan yet. Create one or accept an invite." },
            new[] { "ClanTitle", "Клан [{0}]", "Clan [{0}]" },
            new[] { "ClanSub", "Чат клана — /c, участники и приглашения — в окне клана.", "Clan chat is /c, members and invites are in the clan window." },
            new[] { "OpenClans", "Окно клана", "Clan window" },
            new[] { "Tournaments", "Турниры", "Tournaments" },
            new[] { "SetUi", "Интерфейс", "Interface" },
            new[] { "SetHud", "Размер HUD", "HUD size" },
            new[] { "SetHudSub", "масштаб панелей во время игры", "panel scale while playing" },
            new[] { "SetWelcome", "Табличка при входе", "Welcome screen" },
            new[] { "SetWelcomeSub", "правила и онлайн по режимам", "rules and online by mode" },
            new[] { "Show", "Показать", "Show" },
            new[] { "SetLang", "Язык", "Language" },
            new[] { "SetLangSub", "как в настройках игры", "same as your game settings" },
            new[] { "SetCoins", "Монеты", "Coins" },
            new[] { "SetCoinsSub", "{0} в час онлайна · до {1} в день", "{0} per hour online · up to {1} a day" },
            new[] { "SetKeys", "Управление", "Controls" },
            new[] { "KeyMenu", "Меню хаба", "Hub menu" },
            new[] { "KeyStudio", "Панель студии", "Studio panel" },
            new[] { "KeyCam", "Кинокамера", "Cine camera" },
            new[] { "KeyLobby", "Вернуться в лобби", "Back to lobby" },
            new[] { "KeyHud", "Размер HUD", "HUD size" },
            new[] { "BindHint", "Если P не работает: F1 → bind p hub.menu", "If P does nothing: F1 → bind p hub.menu" },
            new[] { "SetLeave", "Лобби", "Lobby" },
            new[] { "SetLeaveSub", "Возврат через {0} с и только вне боя. Инвентарь режима сохранится.", "Back in {0}s, only out of combat. Your mode inventory is kept." },
            new[] { "NoPlugin", "Плагин {0} не загружен", "Plugin {0} is not loaded" },
            new[] { "Lobby", "Лобби", "Lobby" },
            new[] { "WelcomeTo", "ДОБРО ПОЖАЛОВАТЬ НА", "WELCOME TO" },
            new[] { "Rules", "ПРАВИЛА", "RULES" },
            new[] { "OnlineModes", "ОНЛАЙН ПО РЕЖИМАМ", "ONLINE BY MODE" },
            new[] { "ChooseMode", "ВЫБРАТЬ РЕЖИМ", "CHOOSE MODE" },
            new[] { "StayLobby", "ОСТАТЬСЯ В ЛОББИ", "STAY IN LOBBY" },
            new[] { "MenuTitle", "РЕЖИМЫ", "MODES" },
            new[] { "MenuSub", "выбери, чем заняться · /menu · bind p hub.menu", "pick what to play · /menu · bind p hub.menu" },
            new[] { "PlayBtn", "ИГРАТЬ", "PLAY" },
            new[] { "YouHere", "ТЫ ЗДЕСЬ", "YOU ARE HERE" },
            new[] { "SoonBtn", "СКОРО", "SOON" },
            new[] { "SoonShort", "скоро", "soon" },
            new[] { "Locked", "НЕ ДОСТУПНО", "LOCKED" },
            new[] { "AllModes", "ВСЕ РЕЖИМЫ", "ALL MODES" },
            new[] { "InGame", "{0} в игре", "{0} playing" },
            new[] { "InLobby", "ТЫ В ЛОББИ", "YOU ARE IN LOBBY" },
            new[] { "ToLobby", "В ЛОББИ · {0} с", "TO LOBBY · {0}s" },
            new[] { "ProfileBtn", "ПРОФИЛЬ", "PROFILE" },
            new[] { "CoinsBalance", "Монет: {0}", "Coins: {0}" },
            new[] { "TAchs", "достижений", "achievements" },
            new[] { "THud", "размер HUD", "HUD size" },
            new[] { "Achievements", "ДОСТИЖЕНИЯ · {0} из {1}", "ACHIEVEMENTS · {0} of {1}" },
            new[] { "AchGot", "ДОСТИЖЕНИЕ ПОЛУЧЕНО", "ACHIEVEMENT UNLOCKED" },
            new[] { "AchAll", "{0} получил достижение «{1}»", "{0} unlocked \"{1}\"" },
            new[] { "HudHelp", "Размер HUD сейчас {0}. /hud s — маленький, /hud m — средний, /hud l — большой", "HUD size is {0}. /hud s · /hud m · /hud l" },
            new[] { "HudSet", "Размер HUD: {0}", "HUD size: {0}" },
            new[] { "CapReached", "Дневной лимит монет ({0}) набран — завтра снова", "Daily coin limit ({0}) reached — come back tomorrow" },
            new[] { "CoinsPlus", "+{0} монет {1}", "+{0} coins {1}" },
            new[] { "CoinsMinus", "−{0} монет {1}", "−{0} coins {1}" },
            new[] { "Badge", "ЛОББИ · P — меню · /hub · монет: {0}", "LOBBY · P — menu · /hub · coins: {0}" },
            new[] { "Entered", "Режим: {0}", "Mode: {0}" },
            new[] { "Leaving", "{0} через {1}… не получай урон", "{0} in {1}… don't take damage" },
            new[] { "LeaveCancelled", "Переход отменён — ты получил урон", "Cancelled — you took damage" },
            new[] { "InCombat", "Нельзя: ты в бою ещё {0} с", "Can't: you are in combat for {0}s" },
            new[] { "AlreadyLobby", "Ты уже в лобби", "You are already in the lobby" },
            new[] { "NoMode", "Такого режима нет", "No such mode" },
            new[] { "Soon", "{0} — скоро", "{0} — coming soon" },
            new[] { "NoAccess", "Нет доступа к режиму {0}", "No access to {0}" },
            new[] { "NoSpawn", "{0}: админ ещё не поставил точки спавна", "{0}: no spawn points yet" },
            new[] { "NoLobby", "Лобби ещё не размечено (/hubadmin lobby)", "Lobby is not set yet" },
            new[] { "ZoneBack", "Это граница зоны режима", "Mode zone border" },
            new[] { "ProfileTitle", "ПРОФИЛЬ · {0}", "PROFILE · {0}" },
            new[] { "ProfileSub", "с нами с {0} · последний заход {1}", "since {0} · last seen {1}" },
            new[] { "TCoins", "монет", "coins" },
            new[] { "TTime", "на сервере", "played" },
            new[] { "TModes", "режимов сыграно", "modes played" },
            new[] { "TNow", "сейчас", "now" },
            new[] { "H", "ч", "h" },
            new[] { "TimeByMode", "ВРЕМЯ ПО РЕЖИМАМ", "TIME BY MODE" },
            new[] { "Stats", "СТАТИСТИКА", "STATS" },
            new[] { "StatsSoon", "Ранги, K/D, хедшоты и ачивки появятся здесь, когда будут установлены плагины Ranked, Arenas и AimTrain.", "Ranks, K/D, headshots and achievements will show up here once Ranked, Arenas and AimTrain are installed." },
            new[] { "NoData", "пока пусто", "nothing yet" },
            new[] { "NotFound", "Игрок «{0}» не найден", "Player '{0}' not found" },
            new[] { "NoPerm", "Нет прав", "No permission" },
            new[] { "AdminHelp",
                "<color=#f0a6b1>/hubadmin</color> — разметка (стой на месте точки):\n" +
                "lobby · clearlobby — точки спавна лобби\n" +
                "spawn <режим> · clearspawns <режим> — спавны режима\n" +
                "zone <режим> <радиус> — держать игроков в зоне\n" +
                "portal <режим> [радиус] — портал в лобби\n" +
                "npc <режим> [имя] — NPC с меню режима (E)\n" +
                "npc cmd:shop Торговец — NPC, который открывает чат-команду (/shop)\n" +
                "delnear — удалить точки/порталы/NPC рядом\n" +
                "show — показать всё на 15 с · list — список · tp <режим|lobby>\n" +
                "coins <ник> <кол-во> — выдать монеты",
                "<color=#f0a6b1>/hubadmin</color> — setup (stand on the spot):\n" +
                "lobby · clearlobby · spawn <mode> · clearspawns <mode>\n" +
                "zone <mode> <radius> · portal <mode> [radius] · npc <mode> [name]\n" +
                "delnear · show · list · tp <mode|lobby> · coins <name> <amount>" },
            new[] { "AdmLobby", "Точка лобби добавлена (всего {0})", "Lobby point added ({0})" },
            new[] { "AdmSpawn", "Спавн режима {0} добавлен (всего {1})", "Spawn for {0} added ({1})" },
            new[] { "AdmZone", "Зона режима {0}: радиус {1} м (0 = выкл)", "Zone {0}: radius {1} m" },
            new[] { "AdmPortal", "Портал в {0} поставлен, радиус {1} м", "Portal to {0} placed, radius {1} m" },
            new[] { "AdmNpc", "NPC режима {0} поставлен (смотрит на тебя)", "NPC for {0} placed" },
            new[] { "AdmDeleted", "Удалено рядом: {0}", "Removed nearby: {0}" },
            new[] { "AdmCleared", "Очищено", "Cleared" },
            new[] { "AdmModes", "Режимы: {0}", "Modes: {0}" },
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
