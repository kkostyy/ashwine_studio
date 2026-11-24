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
    [Info("Hub", "Claude", "1.2.1")]
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
                new ModeCfg { Id = "ffa", Name = "FFA / TDM", NameEn = "FFA / TDM", Desc = "арена = пушка", DescEn = "arena = gun", Inventory = "empty", Kit = ffaKit, OpenChatCommand = "arena" },
                new ModeCfg { Id = "duels", Name = "Дуэли и свои арены", NameEn = "Duels & custom", Desc = "с друзьями, пароль", DescEn = "with friends, password",
                    Inventory = "empty", OpenChatCommand = "arena" },
                new ModeCfg { Id = "facecheck", Name = "Фейсчеки", NameEn = "Facechecks", Desc = "дверь и углы · игрок / бот", DescEn = "door & corners · player / bot",
                    Inventory = "empty", OpenChatCommand = "fc" },
                new ModeCfg { Id = "aim", Name = "Аим-трен", NameEn = "Aim train", Desc = "мишени, боты, зеркало", DescEn = "targets, bots, mirror",
                    Inventory = "kit", Kit = aimKit, AllowPvp = false, OpenChatCommand = "aim" },
                new ModeCfg { Id = "primitive", Name = "Примитив", NameEn = "Primitive", Desc = "копья, луки, спиргнан", DescEn = "spears, bows, speargun",
                    Inventory = "empty", Kit = primKit, OpenChatCommand = "arena" },
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
                case "profile": CuiHelper.DestroyUi(player, UiMain); ShowProfile(player, id); break;
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

        #region Интерфейс: меню режимов

        private void OpenMenu(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;
            var id = Id(player);
            _menuShown.Add(id);
            var modes = _config.Modes.Where(m => m.Enabled).ToList();
            const int cols = 4;
            int rows = Mathf.Max(1, Mathf.CeilToInt(modes.Count / (float)cols));
            const float W = 980f, cardH = 132f;
            float H = M + 52 + 2 + M + rows * cardH + (rows - 1) * G + G + 44 + M;
            var c = new CuiElementContainer();
            Window(c, UiMain, id, "M", T(id, "MenuTitle"), T(id, "MenuSub"), W, H, "hub.ui close");
            var R = UiMain;
            string cur = ModeOf(player);

            float cw = (W - 2 * M - (cols - 1) * G) / cols, top = M + 52 + 2 + M;
            for (int i = 0; i < modes.Count; i++)
            {
                var m = modes[i];
                float x = M + (i % cols) * (cw + G), y = top + (i / cols) * (cardH + G);
                bool here = cur == m.Id;
                string reason;
                bool can = CanEnter(player, m, out reason);
                Box(c, R, here ? ColWineSoft : ColPanel, x, y, cw, cardH, 16f);
                int n = GetModeOnline(m.Id);
                Box(c, R, "0.48 0.07 0.14 0.45", x + 12, y + 12, 34, 34, 10f);
                Text(c, R, ModeName(m, id).Substring(0, 1).ToUpper(), 15, x + 12, y + 12, 34, 34, TextAnchor.MiddleCenter, ColRose);
                Text(c, R, m.ComingSoon ? "—" : "● " + n, 11, x + cw - 70, y + 12, 58, 16, TextAnchor.MiddleRight, n > 0 ? ColGreen : ColDim);
                Text(c, R, ModeName(m, id), 14, x + 12, y + 52, cw - 24, 20, TextAnchor.MiddleLeft, m.ComingSoon ? ColDim : ColText);
                Text(c, R, ModeDesc(m, id), 10, x + 12, y + 72, cw - 24, 16, TextAnchor.MiddleLeft, ColDim, FontReg);
                if (m.ComingSoon) Btn(c, R, T(id, "SoonBtn"), "", "1 1 1 0.05", 11, x + 12, y + cardH - 42, cw - 24, 30, ColDim, 15f);
                else if (here) Btn(c, R, T(id, "YouHere"), "hub.ui play " + m.Id, "1 1 1 0.10", 11, x + 12, y + cardH - 42, cw - 24, 30, ColText, 15f);
                else if (!can) Btn(c, R, T(id, "Locked"), "hub.ui play " + m.Id, ColBtn, 11, x + 12, y + cardH - 42, cw - 24, 30, ColDim, 15f);
                else Btn(c, R, T(id, "PlayBtn"), "hub.ui play " + m.Id, ColWine, 11, x + 12, y + cardH - 42, cw - 24, 30, ColText, 15f);
            }

            float fy = H - M - 44, fw = (W - 2 * M - 2 * G) / 3f;
            Box(c, R, ColPanel, M, fy, fw, 44, 14f);
            Text(c, R, T(id, "CoinsBalance", D(id).Coins), 14, M + 14, fy, fw - 28, 44, TextAnchor.MiddleLeft, ColGold);
            Btn(c, R, cur == Lobby ? T(id, "InLobby") : T(id, "ToLobby", _config.LeaveDelay), "hub.ui lobby", ColBtn, 13, M + fw + G, fy, fw, 44, cur == Lobby ? ColDim : ColText, 14f);
            Btn(c, R, T(id, "ProfileBtn"), "hub.ui profile", ColBtn, 13, M + 2 * (fw + G), fy, fw, 44, ColText, 14f);

            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        private void ShowModePopup(BasePlayer player, string mode)
        {
            var m = Mode(mode);
            if (m == null) return;
            var id = Id(player);
            const float W = 460f, H = 240f;
            var c = new CuiElementContainer();
            Window(c, UiPopup, id, ModeName(m, id).Substring(0, 1).ToUpper(), ModeName(m, id), ModeDesc(m, id), W, H, "hub.ui popup.close");
            var R = UiPopup;
            int n = GetModeOnline(mode);
            Text(c, R, m.ComingSoon ? T(id, "SoonShort") : T(id, "InGame", n), 15, 0, 92, W, 40, TextAnchor.MiddleCenter, m.ComingSoon ? ColDim : ColGreen);
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
            new[] { "Badge", "ЛОББИ · M — меню · /hub · монет: {0}", "LOBBY · M — menu · /hub · coins: {0}" },
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
