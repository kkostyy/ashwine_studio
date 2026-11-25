// =====================================================================================
//  AimTrain 1.0 — аим-трен и фейсчеки для хаб-сервера Rust (Oxide / Carbon)
// =====================================================================================
//  Аим-трен на своей дорожке: мишени, стрейферы (бегают, прыгают, приседают), боты,
//  которые стреляют в ответ, бот-зеркало (повторяет твои движения в реальном времени),
//  фейсчек против бота. Сложность, дистанция, число ботов, любое оружие и модули,
//  бесконечные патроны. Статистика: точность, хедшоты, время реакции, дистанция, рекорды.
//
//  Фейсчеки: комнаты «дверь» (стена исчезает через случайные 1–3 с) и «углы»,
//  против игрока (очередь 1v1, до 7 побед) или против бота.
//
//  Работает вместе с Hub (режимы aim / facecheck), может и без него.
//  Права:  aimtrain.admin — разметка дорожек и комнат
//  Чат:    /aim — меню · /aim stop · /fc — фейсчеки · /fc leave
//  Админ:  /aimadmin — команды разметки
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
    [Info("AimTrain", "Claude", "1.3.1")]
    [Description("Аим-трен: мишени, стрейферы, стрелки, бот-зеркало; фейсчеки против игрока и бота")]
    public class AimTrain : RustPlugin
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
        private static AimTrain _ins;

        #region Константы

        private const string PermAdmin = "aimtrain.admin";
        private const string PlayerPrefab = "assets/prefabs/player/player.prefab";
        private const ulong BotIdBase = 8800000UL;

        private const string UiMain = "At.Main";
        private const string UiHud = "At.Hud";
        private const string UiRec = "At.Rec";
        private const string UiSpray = "At.Spray";
        private const string UiZones = "At.Zones";
        private const string UiCenter = "At.Center";
        private const string UiToast = "At.Toast";

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
        private const string Font = "robotocondensed-bold.ttf";
        private const string FontReg = "robotocondensed-regular.ttf";

        private const int FlagDucked = 1, FlagJumped = 2, FlagOnGround = 4;

        #endregion

        #region Конфиг

        private class Diff
        {
            [JsonProperty("Название")] public string Name;
            [JsonProperty("Скорость стрейфа (м/с)")] public float Speed;
            [JsonProperty("Смена направления: от (сек)")] public float SwitchMin;
            [JsonProperty("Смена направления: до (сек)")] public float SwitchMax;
            [JsonProperty("Шанс прыжка при смене")] public float Jump;
            [JsonProperty("Шанс присесть при смене")] public float Crouch;
            [JsonProperty("Реакция бота-стрелка (мс)")] public int Reaction;
            [JsonProperty("Точность бота-стрелка (0–1)")] public float Accuracy;
            [JsonProperty("Выстрел раз в (сек)")] public float FireRate;
            [JsonProperty("Урон за попадание")] public float Damage;
            [JsonProperty("Одежда ботов")] public List<string> Wear = new List<string>();
        }

        private class WeaponOpt
        {
            [JsonProperty("Предмет")] public string Item;
            [JsonProperty("Название")] public string Name;
        }

        private class Configuration
        {
            [JsonProperty("Сложности")] public List<Diff> Diffs = new List<Diff>
            {
                new Diff { Name = "Легко", Speed = 2.0f, SwitchMin = 1.2f, SwitchMax = 2.5f, Jump = 0f, Crouch = 0f, Reaction = 650, Accuracy = 0.15f, FireRate = 0.5f, Damage = 9f, Wear = new List<string> { "hoodie", "pants" } },
                new Diff { Name = "Средне", Speed = 3.5f, SwitchMin = 0.6f, SwitchMax = 1.6f, Jump = 0.10f, Crouch = 0.10f, Reaction = 420, Accuracy = 0.30f, FireRate = 0.2f, Damage = 11f, Wear = new List<string> { "hoodie", "pants", "wood.armor.jacket" } },
                new Diff { Name = "Сложно", Speed = 5.0f, SwitchMin = 0.3f, SwitchMax = 1.0f, Jump = 0.22f, Crouch = 0.15f, Reaction = 280, Accuracy = 0.45f, FireRate = 0.13f, Damage = 13f, Wear = new List<string> { "metal.facemask", "roadsign.jacket", "hoodie", "pants" } },
                new Diff { Name = "Инсейн", Speed = 5.5f, SwitchMin = 0.15f, SwitchMax = 0.6f, Jump = 0.35f, Crouch = 0.25f, Reaction = 190, Accuracy = 0.6f, FireRate = 0.12f, Damage = 15f, Wear = new List<string> { "metal.facemask", "metal.plate.torso", "roadsign.kilt", "hoodie", "pants" } },
            };
            [JsonProperty("Оружие")] public List<WeaponOpt> Weapons = new List<WeaponOpt>
            {
                new WeaponOpt { Item = "rifle.ak", Name = "AK" }, new WeaponOpt { Item = "rifle.lr300", Name = "LR-300" },
                new WeaponOpt { Item = "smg.mp5", Name = "MP5" }, new WeaponOpt { Item = "smg.thompson", Name = "Томпсон" },
                new WeaponOpt { Item = "rifle.bolt", Name = "Болт" }, new WeaponOpt { Item = "rifle.l96", Name = "L96" },
                new WeaponOpt { Item = "pistol.python", Name = "Питон" }, new WeaponOpt { Item = "lmg.m249", Name = "M249" },
            };
            [JsonProperty("Модули")] public List<WeaponOpt> Mods = new List<WeaponOpt>
            {
                new WeaponOpt { Item = "weapon.mod.holosight", Name = "Голо" }, new WeaponOpt { Item = "weapon.mod.small.scope", Name = "8x" },
                new WeaponOpt { Item = "weapon.mod.silencer", Name = "Глушитель" }, new WeaponOpt { Item = "weapon.mod.lasersight", Name = "Лазер" },
            };
            [JsonProperty("Версия конфига")] public int ConfigVersion = 3;
            [JsonProperty("Дистанции (м)")] public List<int> Distances = new List<int> { 10, 50, 100, 150, 200, 250 };
            [JsonProperty("Рисунок очереди: показывать после очереди от (выстрелов)")] public int SprayMinShots = 3;
            [JsonProperty("Рисунок очереди: держать на экране (сек)")] public float SprayShow = 4f;
            [JsonProperty("Броня ботов на выбор (id → одежда)")] public Dictionary<string, List<string>> ArmorSets = new Dictionary<string, List<string>>
            {
                ["none"] = new List<string> { "hoodie", "pants" },
                ["wood"] = new List<string> { "wood.armor.helmet", "wood.armor.jacket", "wood.armor.pants", "hoodie", "pants" },
                ["roadsign"] = new List<string> { "coffeecan.helmet", "roadsign.jacket", "roadsign.kilt", "hoodie", "pants" },
                ["hqm"] = new List<string> { "metal.facemask", "metal.plate.torso", "roadsign.kilt", "hoodie", "pants" },
                ["heavy"] = new List<string> { "heavy.plate.helmet", "heavy.plate.jacket", "heavy.plate.pants" },
            };
            [JsonProperty("Лук: оружие")] public List<WeaponOpt> BowWeapons = new List<WeaponOpt>
            {
                new WeaponOpt { Item = "bow.hunting", Name = "Лук" }, new WeaponOpt { Item = "crossbow", Name = "Арбалет" },
                new WeaponOpt { Item = "bow.compound", Name = "Блочный" },
            };
            [JsonProperty("Лук: дистанции (м)")] public List<int> BowDistances = new List<int> { 10, 20, 30, 50, 70 };
            [JsonProperty("Лук: скорость мишеней (доля от сложности)")] public float BowSpeed = 0.45f;
            [JsonProperty("Aim Flow: глубина рывков вперёд-назад (м)")] public float FlowDepth = 3f;
            [JsonProperty("Aim Flow: шанс серии ADAD")] public float FlowAdad = 0.25f;
            [JsonProperty("Максимум ботов")] public int MaxBots = 6;
            [JsonProperty("Ширина дорожки по умолчанию (м)")] public float LaneWidth = 14f;
            [JsonProperty("Новый бот после убийства через (сек)")] public float BotRespawn = 0.6f;
            [JsonProperty("Частота сетевых обновлений ботов (раз в сек)")] public float NetRate = 20f;

            [JsonProperty("Фейсчек: побед до конца матча")] public int FcRounds = 7;
            [JsonProperty("Фейсчек: дверь открывается через, от (сек)")] public float FcDelayMin = 1f;
            [JsonProperty("Фейсчек: дверь открывается через, до (сек)")] public float FcDelayMax = 3f;
            [JsonProperty("Фейсчек: префаб «двери» (исчезает при открытии)")] public string FcWallPrefab = "assets/prefabs/building/wall.external.high.stone/wall.external.high.stone.prefab";
            [JsonProperty("Фейсчек: сложность бота (0–3)")] public int FcBotDiff = 2;
            [JsonProperty("Фейсчек: оружие")] public string FcWeapon = "rifle.ak";
            [JsonProperty("Фейсчек: модули")] public List<string> FcMods = new List<string> { "weapon.mod.holosight" };
            [JsonProperty("Фейсчек: одежда")] public List<string> FcWear = new List<string> { "metal.facemask", "roadsign.jacket", "roadsign.kilt", "hoodie", "pants", "shoes.boots" };
            [JsonProperty("Монеты: победа в фейсчек-матче против игрока")] public int CoinsFcWin = 10;

            [JsonProperty("Режим Hub для аим-трена")] public string HubAim = "aim";
            [JsonProperty("Режим Hub для фейсчеков")] public string HubFc = "facecheck";
            [JsonProperty("Скруглённые углы интерфейса")] public bool RoundedCorners = true;
        }

        private Configuration _config;

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                Config.Settings.ObjectCreationHandling = ObjectCreationHandling.Replace; _config = Config.ReadObject<Configuration>();
                if (_config == null || _config.Diffs == null || _config.Diffs.Count == 0) throw new Exception();
                if (_config.ConfigVersion < 2)
                {
                    // 1.2: дальние дистанции 150–250 м, броня ботов на выбор, рисунок очереди
                    if (_config.Distances == null || _config.Distances.SequenceEqual(new List<int> { 10, 30, 60, 100 })) _config.Distances = new List<int> { 10, 50, 100, 150, 200, 250 };
                    _config.ConfigVersion = 2;
                    PrintWarning("Конфиг обновлён до 1.2: дистанции до 250 м, броня ботов на выбор");
                }
                if (_config.ConfigVersion < 3)
                {
                    // 1.3: Aim Flow и мишени для лука
                    _config.ConfigVersion = 3;
                    PrintWarning("Конфиг обновлён до 1.3: Aim Flow, режим лука");
                }
                var def = new Configuration();
                if (_config.BowWeapons == null || _config.BowWeapons.Count == 0) _config.BowWeapons = def.BowWeapons;
                if (_config.BowDistances == null || _config.BowDistances.Count == 0) _config.BowDistances = def.BowDistances;
                if (_config.ArmorSets == null || _config.ArmorSets.Count == 0) _config.ArmorSets = new Configuration().ArmorSets;
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
            [JsonIgnore] public Vector3 Fwd => Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;
            [JsonIgnore] public Vector3 Right => Quaternion.Euler(0f, Yaw, 0f) * Vector3.right;
            public static Pt From(Vector3 v, float yaw) => new Pt { X = v.x, Y = v.y, Z = v.z, Yaw = yaw };
        }

        private class Lane
        {
            public string Id;
            public string Name;
            public Pt Stand;            // где стоит игрок, Yaw — куда смотрит дорожка
            public float Width = 14f;
        }

        private class Room
        {
            public string Id;
            public string Name;
            public string Type = "door";   // door | corners
            public Pt A, B, Wall;
        }

        private class PStats
        {
            public string Name;
            public int Shots, Hits, Headshots, Kills, Deaths;
            public double ReactSum; public int ReactN;
            public int BestReact;
            public int BestAcc;
            public int BestHsStreak;
            public double DistSum; public int DistN;
            public int FcWins, FcLosses, FcBestReact;
        }

        private class StoredData
        {
            public Dictionary<string, Lane> Lanes = new Dictionary<string, Lane>();
            public Dictionary<string, Room> Rooms = new Dictionary<string, Room>();
        }

        private StoredData _data;
        private Dictionary<ulong, PStats> _stats;
        private bool _dirty;

        private void LoadData()
        {
            try { _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("AimTrain/Map"); } catch { _data = null; }
            if (_data == null) _data = new StoredData();
            if (_data.Lanes == null) _data.Lanes = new Dictionary<string, Lane>();
            if (_data.Rooms == null) _data.Rooms = new Dictionary<string, Room>();
            try { _stats = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PStats>>("AimTrain/Stats"); } catch { _stats = null; }
            if (_stats == null) _stats = new Dictionary<ulong, PStats>();
        }

        private void SaveMap() => Interface.Oxide.DataFileSystem.WriteObject("AimTrain/Map", _data);

        private void SaveStats()
        {
            Interface.Oxide.DataFileSystem.WriteObject("AimTrain/Stats", _stats);
            _dirty = false;
        }

        private PStats St(ulong id, string name = null)
        {
            PStats s;
            if (!_stats.TryGetValue(id, out s)) _stats[id] = s = new PStats();
            if (name != null) s.Name = name;
            _dirty = true;
            return s;
        }

        #endregion

        #region Сессии

        // настройки, которые игрок выбирает в меню
        private class Prefs
        {
            public string Mode = "strafers";       // targets | strafers | flow | shooters | mirror | bow | fcbot
            public int Diff = 1;
            public int Dist = 30;                  // 0 = микс
            public int Bots = 3;
            public string Weapon = "rifle.ak";
            public HashSet<string> Mods = new HashSet<string> { "weapon.mod.holosight" };
            public int MirrorDelay = 100;
            public bool MirrorShoots;
            public string FcType = "door";
            public bool FcVsBot;
            public string Armor = "auto";          // auto = как в сложности, иначе ключ из «Броня ботов на выбор»
        }

        private class AimSession
        {
            public BasePlayer Player;
            public Lane Lane;
            public Prefs P;
            public Diff D;
            public List<BasePlayer> Bots = new List<BasePlayer>();
            public int Shots, Hits, Hs, Kills, Deaths, HsStreak, BestHsStreak;
            public List<int> Reacts = new List<int>();
            public double DistSum; public int DistN;
            public float Started;
            public int LastReact = -1;
            // урон по зонам: 0 голова, 1 тело, 2 руки и ноги
            public float[] ZoneDmg = new float[3];
            public int[] ZoneHits = new int[3];
            public float LastDmg; public int LastZone = -1;
            // текущая очередь для рисунка
            public int BurstShots;
            public List<Vector2> BurstHits = new List<Vector2>();
            public List<int> BurstZones = new List<int>();
            public Timer BurstTimer;
        }

        private class FcGame
        {
            public Room Room;
            public List<BasePlayer> Players = new List<BasePlayer>();   // 1 (против бота) или 2
            public BasePlayer Bot;
            public int[] Score = new int[2];
            public string State = "wait";      // wait | closed | open | between | ended
            public BaseEntity Wall;
            public float OpenAt;
            public int Round;
            public Dictionary<ulong, int> FirstHit = new Dictionary<ulong, int>();
            public int LastReact = -1;
        }

        private readonly Dictionary<ulong, Prefs> _prefs = new Dictionary<ulong, Prefs>();
        private readonly Dictionary<ulong, AimSession> _aim = new Dictionary<ulong, AimSession>();
        private readonly Dictionary<ulong, FcGame> _fc = new Dictionary<ulong, FcGame>();
        private readonly List<FcGame> _games = new List<FcGame>();
        private readonly Dictionary<string, List<ulong>> _queue = new Dictionary<string, List<ulong>> { ["door"] = new List<ulong>(), ["corners"] = new List<ulong>() };
        private readonly Dictionary<BasePlayer, BotInfo> _bots = new Dictionary<BasePlayer, BotInfo>();
        private ulong _nextBotId = BotIdBase;

        private class BotInfo { public AimSession Aim; public FcGame Fc; public float VisibleSince; public bool Hit; }

        private static ulong Id(BasePlayer p) => (ulong)p.userID;

        private bool IsAdmin(BasePlayer p) => p != null && (p.IsAdmin || permission.UserHasPermission(p.UserIDString, PermAdmin));

        private Prefs Pf(ulong id) { Prefs p; if (!_prefs.TryGetValue(id, out p)) _prefs[id] = p = new Prefs(); return p; }

        private string HubMode(BasePlayer p) => Hub != null ? Hub.Call("GetMode", p) as string : null;

        private bool IsBot(BasePlayer p) => p != null && _bots.ContainsKey(p);

        #endregion

        #region Хуки сервера

        private Timer _tick, _save;

        private void Init()
        {
            _ins = this;
            permission.RegisterPermission(PermAdmin, this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            RegisterLang();
            InitRoundedSprite();
            _tick = timer.Every(0.5f, Tick);
            _save = timer.Every(120f, () => { if (_dirty) SaveStats(); });
        }

        private void Unload()
        {
            foreach (var s in _aim.Values.ToList()) StopAim(s.Player, false);
            foreach (var g in _games.ToList()) EndFc(g, -1, false);
            foreach (var b in _bots.Keys.ToList()) KillBot(b);
            foreach (var p in BasePlayer.activePlayerList)
                foreach (var ui in new[] { UiMain, UiHud, UiRec, UiCenter, UiToast }) { CuiHelper.DestroyUi(p, ui); CuiHelper.DestroyUi(p, ui + ".dim"); }
            SaveStats();
            _ins = null;
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            StopAim(player, false);
            LeaveFc(player);
        }

        private void OnHubModeChanged(BasePlayer player, string from, string to)
        {
            if (player == null) return;
            if (to != _config.HubAim) StopAim(player, false);
            if (to != _config.HubFc) LeaveFc(player);
        }

        #endregion

        #region Аим-трен: старт и стоп

        private Lane FreeLane()
        {
            var busy = new HashSet<string>(_aim.Values.Select(a => a.Lane.Id));
            return _data.Lanes.Values.Where(l => l.Stand != null && !busy.Contains(l.Id)).OrderBy(l => l.Id).FirstOrDefault();
        }

        private void StartAim(BasePlayer player)
        {
            if (player != null) Ach(Id(player), "aim_sessions");
            var id = Id(player);
            var p = Pf(id);
            if (p.Mode == "fcbot") { p.FcVsBot = true; StartFcSolo(player); return; }
            if (player.IsDead()) return;
            if (Hub != null && !string.IsNullOrEmpty(_config.HubAim) && HubMode(player) != _config.HubAim)
            {
                Hub.Call("SendToMode", player, _config.HubAim, false);
                Toast(player, T(id, "GoingToMode"), false);
                return;
            }
            StopAim(player, false);
            LeaveFc(player);
            var lane = FreeLane();
            if (lane == null) { Toast(player, T(id, "NoLane"), true); return; }

            var s = new AimSession { Player = player, Lane = lane, P = p, D = _config.Diffs[Mathf.Clamp(p.Diff, 0, _config.Diffs.Count - 1)], Started = Time.realtimeSinceStartup };
            _aim[id] = s;
            St(id, player.displayName);
            CloseAll(player);

            player.Teleport(lane.Stand.V + Vector3.up * 0.1f);
            Heal(player);
            FixWeapon(p);
            GiveWeapon(player, p.Weapon, p.Mode == "bow" ? new List<string>() : p.Mods.ToList(), new List<string>());
            if (p.Mode == "bow") GiveArrows(player, p.Weapon);

            int n = p.Mode == "mirror" ? 1 : Mathf.Clamp(p.Bots, 1, _config.MaxBots);
            for (int i = 0; i < n; i++) SpawnAimBot(s, i, n);
            DrawHud(s);
            Center(player, T(id, "Go_" + p.Mode), ColRose, 2f);
        }

        private bool IsBow(string item) => _config.BowWeapons.Any(w => w.Item == item);

        // в режиме лука — только лук/арбалет, в остальных — только огнестрел
        private void FixWeapon(Prefs p)
        {
            if (p.Mode == "bow") { if (!IsBow(p.Weapon)) p.Weapon = _config.BowWeapons[0].Item; }
            else if (IsBow(p.Weapon)) p.Weapon = _config.Weapons.Count > 0 ? _config.Weapons[0].Item : "rifle.ak";
            var dists = p.Mode == "bow" ? _config.BowDistances : _config.Distances;
            if (p.Dist != 0 && !dists.Contains(p.Dist)) p.Dist = dists[Mathf.Min(1, dists.Count - 1)];
        }

        private static void GiveArrows(BasePlayer player, string weapon)
        {
            var ammo = ItemManager.CreateByName(weapon == "crossbow" ? "arrow.wooden" : "arrow.wooden", 64, 0UL);
            if (ammo != null && !ammo.MoveToContainer(player.inventory.containerMain)) ammo.Remove();
        }

        private void StopAim(BasePlayer player, bool showResult)
        {
            if (player == null) return;
            var id = Id(player);
            AimSession s;
            if (!_aim.TryGetValue(id, out s)) return;
            _aim.Remove(id);
            foreach (var b in s.Bots.ToList()) KillBot(b);
            s.Bots.Clear();
            CuiHelper.DestroyUi(player, UiHud);
            CuiHelper.DestroyUi(player, UiRec);
            CuiHelper.DestroyUi(player, UiSpray);
            CuiHelper.DestroyUi(player, UiZones);
            s.BurstTimer?.Destroy();

            // рекорды
            var st = St(id, player.displayName);
            int acc = s.Shots >= 20 ? Mathf.RoundToInt(100f * Mathf.Min(s.Hits, s.Shots) / s.Shots) : 0;
            bool newAcc = acc > st.BestAcc;
            if (newAcc) st.BestAcc = acc;
            int bestR = s.Reacts.Count > 0 ? s.Reacts.Min() : 0;
            bool newReact = bestR > 0 && (st.BestReact == 0 || bestR < st.BestReact);
            if (newReact) st.BestReact = bestR;
            bool newHs = s.BestHsStreak > st.BestHsStreak;
            if (newHs) st.BestHsStreak = s.BestHsStreak;

            if (player.IsConnected && !player.IsDead()) player.inventory.Strip();
            if (showResult && player.IsConnected) ShowResult(player, s, newAcc, newReact, newHs);
        }

        private Vector3 Ground(Vector3 p)
        {
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out hit, 80f, LayerMask.GetMask("Terrain", "World", "Construction", "Default"), QueryTriggerInteraction.Ignore))
                return hit.point;
            return p;
        }

        private Vector3 AnchorFor(AimSession s, int index, int count, out float lateral)
        {
            var lane = s.Lane;
            var pool = s.P.Mode == "bow" ? _config.BowDistances : _config.Distances;
            int dist = s.P.Dist > 0 ? s.P.Dist : pool[UnityEngine.Random.Range(0, pool.Count)];
            float w = Mathf.Max(4f, lane.Width);
            lateral = count <= 1 ? 0f : -w / 2f + w * index / (count - 1);
            lateral += UnityEngine.Random.Range(-1.5f, 1.5f);
            var pos = lane.Stand.V + lane.Stand.Fwd * dist;
            return Ground(pos);
        }

        private void SpawnAimBot(AimSession s, int index, int count)
        {
            float lateral;
            var anchor = AnchorFor(s, index, count, out lateral);
            var bot = CreateBot(anchor + s.Lane.Stand.Right * lateral, BotWear(s), s.P.Mode == "shooters" || s.P.Mode == "mirror" && s.P.MirrorShoots ? s.P.Weapon : null);
            if (bot == null) return;
            s.Bots.Add(bot);
            _bots[bot] = new BotInfo { Aim = s, VisibleSince = Time.realtimeSinceStartup };
            var drv = bot.gameObject.AddComponent<BotDriver>();
            drv.Init(this, bot, s.Player, s.P.Mode, s.D, anchor, s.Lane.Stand.Right, Mathf.Max(4f, s.Lane.Width) / 2f, lateral);
            if (s.P.Mode == "mirror") drv.SetMirror(s.Lane.Stand.V, s.P.MirrorDelay / 1000f, s.P.MirrorShoots);
        }

        private List<string> BotWear(AimSession s)
        {
            List<string> w;
            return s.P.Armor != "auto" && _config.ArmorSets.TryGetValue(s.P.Armor, out w) ? w : s.D.Wear;
        }

        private BasePlayer CreateBot(Vector3 pos, List<string> wear, string weapon)
        {
            var bot = GameManager.server.CreateEntity(PlayerPrefab, pos, Quaternion.identity) as BasePlayer;
            if (bot == null) return null;
            ulong uid = _nextBotId++;
            if (_nextBotId > BotIdBase + 900000UL) _nextBotId = BotIdBase;
            bot.userID = uid;
            bot.UserIDString = uid.ToString();
            bot.displayName = "Бот";
            bot.enableSaving = false;
            bot.Spawn();
            bot.InitializeHealth(100f, 100f);
            if (bot.IsSleeping()) bot.EndSleeping();
            bot.inventory.Strip();
            foreach (var w in wear)
            {
                var it = ItemManager.CreateByName(w, 1, 0UL);
                if (it != null && !it.MoveToContainer(bot.inventory.containerWear)) it.Remove();
            }
            if (!string.IsNullOrEmpty(weapon))
            {
                var gun = ItemManager.CreateByName(weapon, 1, 0UL);
                if (gun != null)
                {
                    if (gun.MoveToContainer(bot.inventory.containerBelt, 0)) bot.UpdateActiveItem(gun.uid);
                    else gun.Remove();
                }
            }
            bot.SendNetworkUpdateImmediate();
            return bot;
        }

        private void KillBot(BasePlayer bot)
        {
            _bots.Remove(bot);
            if (bot != null && !bot.IsDestroyed) bot.Kill();
        }

        private static void GiveWeapon(BasePlayer player, string weapon, List<string> mods, List<string> wear)
        {
            player.inventory.Strip();
            foreach (var w in wear)
            {
                var it = ItemManager.CreateByName(w, 1, 0UL);
                if (it != null && !it.MoveToContainer(player.inventory.containerWear)) it.Remove();
            }
            var gun = ItemManager.CreateByName(weapon, 1, 0UL);
            if (gun == null) return;
            if (gun.contents != null)
                foreach (var m in mods)
                {
                    var mod = ItemManager.CreateByName(m, 1, 0UL);
                    if (mod != null && !mod.MoveToContainer(gun.contents)) mod.Remove();
                }
            var proj = gun.GetHeldEntity() as BaseProjectile;
            if (proj?.primaryMagazine != null) proj.primaryMagazine.contents = proj.primaryMagazine.capacity;
            if (!gun.MoveToContainer(player.inventory.containerBelt, 0)) { gun.Remove(); return; }
            var med = ItemManager.CreateByName("syringe.medical", 3, 0UL);
            if (med != null && !med.MoveToContainer(player.inventory.containerBelt, 1)) med.Remove();
        }

        private static void Heal(BasePlayer player)
        {
            try
            {
                player.health = player.MaxHealth();
                var m = player.metabolism;
                if (m != null) { m.bleeding.value = 0f; m.calories.value = m.calories.max; m.hydration.value = m.hydration.max; m.SendChanges(); }
                player.SendNetworkUpdate();
            }
            catch { }
        }

        #endregion

        #region Бот: движение, зеркало, стрельба

        private class BotDriver : FacepunchBehaviour
        {
            private AimTrain _pl;
            private BasePlayer _bot, _target;
            private string _mode;
            private Diff _d;
            private Vector3 _anchor, _right;
            private float _half, _lat, _dir = 1f, _nextSwitch, _jumpStart = -10f, _crouchUntil, _lastSend, _nextFire, _seenSince = -1f;
            private bool _frozen;
            private float _reactOverride = -1f;
            // Aim Flow
            private float _flowT, _flowK = 1f, _adadUntil;

            // зеркало
            private bool _mirror, _mirrorShoots;
            private Vector3 _mirrorOrigin;
            private float _mirrorDelay;
            private readonly Queue<KeyValuePair<float, Vector3>> _buf = new Queue<KeyValuePair<float, Vector3>>();
            private readonly Queue<KeyValuePair<float, int>> _flagBuf = new Queue<KeyValuePair<float, int>>();

            public void Init(AimTrain pl, BasePlayer bot, BasePlayer target, string mode, Diff d, Vector3 anchor, Vector3 right, float half, float lateral)
            {
                _pl = pl; _bot = bot; _target = target; _mode = mode; _d = d;
                _anchor = anchor; _right = right; _half = half; _lat = lateral;
                _dir = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                _nextSwitch = Time.realtimeSinceStartup + UnityEngine.Random.Range(d.SwitchMin, d.SwitchMax);
            }

            public void SetMirror(Vector3 origin, float delay, bool shoots) { _mirror = true; _mirrorOrigin = origin; _mirrorDelay = delay; _mirrorShoots = shoots; }

            public void Freeze(bool f) { _frozen = f; if (!f) _seenSince = Time.realtimeSinceStartup; }

            public void SetReaction(float seconds) => _reactOverride = seconds;

            private void Update()
            {
                if (_bot == null || _bot.IsDestroyed || _bot.IsDead()) { Destroy(this); return; }
                if (_target == null || !_target.IsConnected) return;
                float now = Time.realtimeSinceStartup;
                var pos = _anchor + _right * _lat;
                int flags = FlagOnGround;

                if (_mirror)
                {
                    // смещение игрока от точки дорожки по оси «вправо» и высота, с задержкой
                    var off = _target.transform.position - _mirrorOrigin;
                    _buf.Enqueue(new KeyValuePair<float, Vector3>(now, new Vector3(Vector3.Dot(off, _right), off.y, 0f)));
                    _flagBuf.Enqueue(new KeyValuePair<float, int>(now, _target.modelState != null ? _target.modelState.flags : 0));
                    Vector3 use = Vector3.zero; int f = FlagOnGround;
                    while (_buf.Count > 0 && now - _buf.Peek().Key >= _mirrorDelay) use = _buf.Dequeue().Value;
                    while (_flagBuf.Count > 0 && now - _flagBuf.Peek().Key >= _mirrorDelay) f = _flagBuf.Dequeue().Value;
                    if (use != Vector3.zero || _mirrorDelay <= 0f) { _lat = Mathf.Clamp(use.x, -_half * 2f, _half * 2f); }
                    pos = _anchor + _right * _lat + Vector3.up * Mathf.Max(0f, use.y);
                    flags = f;
                }
                else if (!_frozen && _mode == "flow")
                {
                    // Aim Flow: бот всё время в движении — плавные дуги, рывки вперёд-назад, серии ADAD, прыжки и присед
                    _flowT += Time.deltaTime * _d.Speed * 0.32f * (now < _adadUntil ? 2.6f : 1f);
                    if (now >= _nextSwitch)
                    {
                        _nextSwitch = now + UnityEngine.Random.Range(_d.SwitchMin, _d.SwitchMax) * 1.6f;
                        _flowK = UnityEngine.Random.Range(0.6f, 1.5f);
                        if (UnityEngine.Random.value < _pl._config.FlowAdad) _adadUntil = now + UnityEngine.Random.Range(0.6f, 1.4f);
                        if (UnityEngine.Random.value < _d.Jump + 0.1f) _jumpStart = now;
                        else if (UnityEngine.Random.value < _d.Crouch + 0.05f) _crouchUntil = now + UnityEngine.Random.Range(0.3f, 0.8f);
                    }
                    float lat = now < _adadUntil
                        ? _lat + Mathf.Sin(_flowT * 9f) * 0.9f
                        : Mathf.Sin(_flowT * _flowK) * _half * 0.85f + Mathf.Sin(_flowT * 2.3f) * _half * 0.15f;
                    if (now >= _adadUntil) _lat = lat;
                    float depth = Mathf.Sin(_flowT * 0.7f + 1.3f) * _pl._config.FlowDepth;
                    var fwd = new Vector3(-_right.z, 0f, _right.x);
                    float jt = now - _jumpStart;
                    float jy = jt >= 0f && jt < 0.55f ? 4.2f * jt - 7.6f * jt * jt : 0f;
                    pos = _anchor + _right * Mathf.Clamp(lat, -_half, _half) + fwd * depth + Vector3.up * Mathf.Max(0f, jy);
                    if (jy > 0.02f) flags = FlagJumped;
                    if (now < _crouchUntil) flags |= FlagDucked;
                }
                else if (!_frozen && (_mode == "strafers" || _mode == "shooters" || _mode == "fcbot" || _mode == "bow"))
                {
                    if (now >= _nextSwitch)
                    {
                        _dir = -_dir;
                        _nextSwitch = now + UnityEngine.Random.Range(_d.SwitchMin, _d.SwitchMax);
                        if (_mode != "bow" && UnityEngine.Random.value < _d.Jump) _jumpStart = now;
                        else if (UnityEngine.Random.value < _d.Crouch) _crouchUntil = now + UnityEngine.Random.Range(0.3f, 0.9f);
                    }
                    float speed = _d.Speed * (now < _crouchUntil ? 0.45f : 1f) * (_mode == "bow" ? _pl._config.BowSpeed : 1f);
                    _lat += _dir * speed * Time.deltaTime;
                    if (_lat > _half) { _lat = _half; _dir = -1f; }
                    if (_lat < -_half) { _lat = -_half; _dir = 1f; }
                    float jt = now - _jumpStart;
                    float jy = jt >= 0f && jt < 0.55f ? 4.2f * jt - 7.6f * jt * jt : 0f;      // парабола ~0.58 м
                    pos = _anchor + _right * _lat + Vector3.up * Mathf.Max(0f, jy);
                    if (jy > 0.02f) flags = FlagJumped;
                    if (now < _crouchUntil) flags |= FlagDucked;
                }

                // смотрим на игрока
                var eye = pos + Vector3.up * ((flags & FlagDucked) != 0 ? 1.1f : 1.5f);
                var to = _target.eyes.position - eye;
                float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;

                if (now - _lastSend >= 1f / Mathf.Max(5f, _pl._config.NetRate))
                {
                    _lastSend = now;
                    _bot.ServerPosition = pos;
                    _bot.ServerRotation = Quaternion.Euler(0f, yaw, 0f);
                    _bot.viewAngles = new Vector3(pitch, yaw, 0f);
                    _bot.eyes.rotation = Quaternion.Euler(pitch, yaw, 0f);
                    if (_bot.modelState != null) _bot.modelState.flags = flags;
                    _bot.SendNetworkUpdate();
                }

                // стрельба в ответ
                bool shoots = _mode == "shooters" || _mode == "fcbot" || _mirror && _mirrorShoots;
                if (!shoots || _frozen || _target.IsDead()) { _seenSince = -1f; return; }
                bool los = !Physics.Linecast(eye, _target.eyes.position, LayerMask.GetMask("Construction", "World", "Terrain", "Deployed"), QueryTriggerInteraction.Ignore);
                if (!los) { _seenSince = -1f; return; }
                if (_seenSince < 0f) _seenSince = now;
                float react = _reactOverride >= 0f ? _reactOverride : _d.Reaction / 1000f;
                if (now - _seenSince < react || now < _nextFire) return;
                _nextFire = now + _d.FireRate;
                var held = _bot.GetHeldEntity();
                held?.SignalBroadcast(BaseEntity.Signal.Attack, string.Empty);
                if (UnityEngine.Random.value <= _d.Accuracy)
                    _target.Hurt(_d.Damage, Rust.DamageType.Bullet, _bot, true);
            }
        }

        #endregion

        #region Урон, смерть, выстрелы

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var victim = entity as BasePlayer;
            if (victim == null || info == null) return null;
            var attacker = info.InitiatorPlayer;

            BotInfo bi;
            if (_bots.TryGetValue(victim, out bi))
            {
                // боты получают урон только от своего игрока
                var owner = bi.Aim?.Player;
                bool ownerOk = owner != null && attacker == owner || bi.Fc != null && bi.Fc.Players.Contains(attacker);
                if (!ownerOk) return true;
                if (bi.Fc != null && bi.Fc.State != "open") return true;
                OnBotHit(attacker, victim, bi, info);
                return null;
            }

            // игрок в фейсчеке: урон только от соперника и только когда открыто
            FcGame g;
            if (_fc.TryGetValue(Id(victim), out g))
            {
                if (attacker == null) return null;
                if (_bots.ContainsKey(attacker)) return g.State == "open" ? null : (object)true;
                if (!g.Players.Contains(attacker) || attacker == victim || g.State != "open") return true;
                if (!g.FirstHit.ContainsKey(Id(attacker))) g.FirstHit[Id(attacker)] = Mathf.RoundToInt((Time.realtimeSinceStartup - g.OpenAt) * 1000f);
                return null;
            }
            // игрок в аим-трене: урон только от своих ботов
            if (_aim.ContainsKey(Id(victim)) && attacker != null && attacker != victim && !_bots.ContainsKey(attacker)) return true;
            return null;
        }

        private void OnBotHit(BasePlayer attacker, BasePlayer bot, BotInfo bi, HitInfo info)
        {
            float now = Time.realtimeSinceStartup;
            bool hs = info.isHeadshot;
            float dist = Vector3.Distance(attacker.transform.position, bot.transform.position);
            if (bi.Aim != null)
            {
                var s = bi.Aim;
                s.Hits++;
                if (hs) { s.Hs++; s.HsStreak++; if (s.HsStreak > s.BestHsStreak) s.BestHsStreak = s.HsStreak; } else s.HsStreak = 0;
                s.DistSum += dist; s.DistN++;
                var st = St(Id(attacker));
                st.Hits++; if (hs) st.Headshots++; st.DistSum += dist; st.DistN++;
                if (!bi.Hit)
                {
                    bi.Hit = true;
                    int ms = Mathf.RoundToInt((now - bi.VisibleSince) * 1000f);
                    if (ms > 60 && ms < 5000) { s.Reacts.Add(ms); s.LastReact = ms; st.ReactSum += ms; st.ReactN++; }
                }
                if (hs) Center(attacker, $"HEADSHOT{(s.LastReact > 0 && bi.Hit ? " · " + s.LastReact + " " + T(Id(attacker), "Ms") : "")}", ColGold, 0.8f);
                // зона попадания и урон (после брони — хук вызывается после ScaleDamage)
                int zone = hs || (info.boneArea & HitArea.Head) != 0 ? 0 : (info.boneArea & (HitArea.Chest | HitArea.Stomach)) != 0 ? 1 : 2;
                float dmg = info.damageTypes != null ? info.damageTypes.Total() : 0f;
                s.ZoneDmg[zone] += dmg; s.ZoneHits[zone]++; s.LastDmg = dmg; s.LastZone = zone;
                // точка на силуэте: вправо/влево от центра бота и высота от ног
                var off = info.HitPositionWorld - bot.transform.position;
                var right = bot.transform.right; right.y = 0f;
                if (right.sqrMagnitude < 0.01f) right = Vector3.right;
                s.BurstHits.Add(new Vector2(Vector3.Dot(off, right.normalized), off.y));
                s.BurstZones.Add(zone);
                DrawHud(s);
                DrawZones(s);
            }
            else if (bi.Fc != null)
            {
                var g = bi.Fc;
                if (!g.FirstHit.ContainsKey(Id(attacker))) g.FirstHit[Id(attacker)] = Mathf.RoundToInt((now - g.OpenAt) * 1000f);
                var st = St(Id(attacker));
                st.Hits++; if (hs) st.Headshots++;
            }
        }

        private void OnWeaponFired(BaseProjectile projectile, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            if (player == null) return;
            AimSession s;
            if (_aim.TryGetValue(Id(player), out s))
            {
                s.Shots++;
                St(Id(player)).Shots++;
                s.BurstShots++;
                s.BurstTimer?.Destroy();
                var ss = s;
                s.BurstTimer = timer.Once(0.6f, () => FinishBurst(ss));
                // бесконечные патроны
                if (projectile?.primaryMagazine != null)
                {
                    projectile.primaryMagazine.contents = projectile.primaryMagazine.capacity;
                    projectile.SendNetworkUpdateImmediate();
                }
                return;
            }
            if (_fc.ContainsKey(Id(player))) St(Id(player)).Shots++;
        }

        private object CanBeWounded(BasePlayer player, HitInfo info)
        {
            if (player == null) return null;
            return _bots.ContainsKey(player) || _aim.ContainsKey(Id(player)) || _fc.ContainsKey(Id(player)) ? (object)false : null;
        }

        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player == null) return;
            BotInfo bi;
            if (_bots.TryGetValue(player, out bi))
            {
                player.inventory.Strip();
                if (bi.Aim != null)
                {
                    var s = bi.Aim;
                    s.Kills++;
                    St(Id(s.Player)).Kills++;
                    int idx = s.Bots.IndexOf(player);
                    s.Bots.Remove(player);
                    _bots.Remove(player);
                    var dead = player;
                    NextTick(() => { if (dead != null && !dead.IsDestroyed) dead.Kill(); });
                    timer.Once(_config.BotRespawn, () =>
                    {
                        if (!_aim.ContainsKey(Id(s.Player))) return;
                        SpawnAimBot(s, Mathf.Max(0, idx), Mathf.Max(1, s.P.Mode == "mirror" ? 1 : s.P.Bots));
                    });
                    DrawHud(s);
                }
                else if (bi.Fc != null)
                {
                    _bots.Remove(player);
                    var dead = player;
                    NextTick(() => { if (dead != null && !dead.IsDestroyed) dead.Kill(); });
                    FcRoundOver(bi.Fc, 0);
                }
                return;
            }

            AimSession a;
            if (_aim.TryGetValue(Id(player), out a))
            {
                player.inventory.Strip();
                a.Deaths++;
                St(Id(player)).Deaths++;
                Center(player, T(Id(player), "BotGotYou"), ColRed, 2f);
                var pl = player;
                timer.Once(1.5f, () => { if (pl != null && pl.IsConnected && pl.IsDead()) pl.Respawn(); });
                return;
            }

            FcGame g;
            if (_fc.TryGetValue(Id(player), out g))
            {
                player.inventory.Strip();
                int loserSide = g.Players.IndexOf(player);
                FcRoundOver(g, loserSide == 0 ? 1 : 0);
                var pl = player;
                timer.Once(1.5f, () => { if (pl != null && pl.IsConnected && pl.IsDead()) pl.Respawn(); });
            }
        }

        private void OnPlayerCorpseSpawned(BasePlayer player, BaseCorpse corpse)
        {
            if (player == null || corpse == null) return;
            if (_bots.ContainsKey(player) || (ulong)player.userID >= BotIdBase && (ulong)player.userID < BotIdBase + 1000000UL) corpse.Kill();
        }

        private object CanLootPlayer(BasePlayer target, BasePlayer looter) => target != null && _bots.ContainsKey(target) ? (object)false : null;

        // Hub: где возродить
        private object OnHubRespawn(BasePlayer player)
        {
            AimSession a;
            if (_aim.TryGetValue(Id(player), out a))
                return new BasePlayer.SpawnPoint { pos = a.Lane.Stand.V + Vector3.up * 0.1f, rot = Quaternion.Euler(0f, a.Lane.Stand.Yaw, 0f) };
            FcGame g;
            if (_fc.TryGetValue(Id(player), out g))
            {
                var pt = g.Players.IndexOf(player) == 1 ? g.Room.B : g.Room.A;
                return new BasePlayer.SpawnPoint { pos = pt.V + Vector3.up * 0.1f, rot = Quaternion.Euler(0f, pt.Yaw, 0f) };
            }
            return null;
        }

        private object OnHubRespawned(BasePlayer player, string mode)
        {
            AimSession a;
            if (_aim.TryGetValue(Id(player), out a))
            {
                Heal(player);
                GiveWeapon(player, a.P.Weapon, a.P.Mods.ToList(), new List<string>());
                DrawHud(a);
                return true;
            }
            if (_fc.ContainsKey(Id(player))) { Heal(player); return true; }   // набор выдаст следующий раунд
            return null;
        }

        private object OnPlayerRespawn(BasePlayer player, BasePlayer.SpawnPoint sp) => Hub != null ? null : OnHubRespawn(player);

        private void OnPlayerRespawned(BasePlayer player) { if (Hub == null) OnHubRespawned(player, null); }

        #endregion

        #region Фейсчеки

        private Room FreeRoom(string type)
        {
            var busy = new HashSet<string>(_games.Select(g => g.Room.Id));
            return _data.Rooms.Values.Where(r => r.Type == type && r.A != null && r.B != null && !busy.Contains(r.Id)).OrderBy(r => UnityEngine.Random.Range(0, 1000)).FirstOrDefault();
        }

        private bool EnsureFcMode(BasePlayer player)
        {
            if (Hub == null || string.IsNullOrEmpty(_config.HubFc) || HubMode(player) == _config.HubFc) return true;
            Hub.Call("SendToMode", player, _config.HubFc, false);
            Toast(player, T(Id(player), "GoingToMode"), false);
            return false;
        }

        private void StartFcSolo(BasePlayer player)
        {
            var id = Id(player);
            var p = Pf(id);
            if (!EnsureFcMode(player)) return;
            StopAim(player, false);
            LeaveFc(player);
            var room = FreeRoom(p.FcType);
            if (room == null) { Toast(player, T(id, "NoRoom"), true); return; }
            var g = new FcGame { Room = room };
            g.Players.Add(player);
            _games.Add(g);
            _fc[id] = g;
            St(id, player.displayName);
            CloseAll(player);
            StartFcRound(g);
        }

        private void JoinFcQueue(BasePlayer player)
        {
            var id = Id(player);
            var p = Pf(id);
            if (!EnsureFcMode(player)) return;
            if (_fc.ContainsKey(id)) { Toast(player, T(id, "AlreadyFc"), false); return; }
            StopAim(player, false);
            foreach (var q in _queue.Values) q.Remove(id);
            var queue = _queue[p.FcType];
            queue.Add(id);
            Toast(player, T(id, "Queued", queue.Count), false);
            TryMatchQueue(p.FcType);
            if (_fc.ContainsKey(id)) return;
            OpenFcMenu(player);
        }

        private void TryMatchQueue(string type)
        {
            var q = _queue[type];
            q.RemoveAll(x => { var pl = BasePlayer.FindByID(x); return pl == null || !pl.IsConnected; });
            while (q.Count >= 2)
            {
                var room = FreeRoom(type);
                if (room == null) return;
                var a = BasePlayer.FindByID(q[0]);
                var b = BasePlayer.FindByID(q[1]);
                q.RemoveRange(0, 2);
                if (a == null || b == null) continue;
                var g = new FcGame { Room = room };
                g.Players.Add(a); g.Players.Add(b);
                _games.Add(g);
                foreach (var pl in g.Players) { _fc[Id(pl)] = g; St(Id(pl), pl.displayName); CloseAll(pl); }
                StartFcRound(g);
            }
        }

        private void LeaveFc(BasePlayer player)
        {
            if (player == null) return;
            var id = Id(player);
            foreach (var q in _queue.Values) q.Remove(id);
            FcGame g;
            if (!_fc.TryGetValue(id, out g)) return;
            int side = g.Players.IndexOf(player);
            if (g.Players.Count == 2 && g.State != "ended") EndFc(g, side == 0 ? 1 : 0, true);
            else EndFc(g, -1, false);
        }

        private void StartFcRound(FcGame g)
        {
            if (!_games.Contains(g)) return;
            g.Round++;
            g.State = "closed";
            g.FirstHit.Clear();
            if (g.Bot != null) { KillBot(g.Bot); g.Bot = null; }
            if (g.Wall != null && !g.Wall.IsDestroyed) g.Wall.Kill();
            g.Wall = null;

            for (int i = 0; i < g.Players.Count; i++)
            {
                var p = g.Players[i];
                if (p == null || !p.IsConnected) continue;
                if (p.IsDead()) { p.Respawn(); continue; }
                var pt = i == 0 ? g.Room.A : g.Room.B;
                p.Teleport(pt.V + Vector3.up * 0.1f);
                Heal(p);
                GiveWeapon(p, _config.FcWeapon, _config.FcMods, _config.FcWear);
            }
            // мёртвым — набор чуть позже, после возрождения
            timer.Once(0.7f, () =>
            {
                if (!_games.Contains(g)) return;
                for (int i = 0; i < g.Players.Count; i++)
                {
                    var p = g.Players[i];
                    if (p == null || p.IsDead() || p.inventory.containerBelt.itemList.Count > 0) continue;
                    var pt = i == 0 ? g.Room.A : g.Room.B;
                    p.Teleport(pt.V + Vector3.up * 0.1f);
                    Heal(p);
                    GiveWeapon(p, _config.FcWeapon, _config.FcMods, _config.FcWear);
                }
            });

            if (g.Players.Count == 1)
            {
                var d = _config.Diffs[Mathf.Clamp(_config.FcBotDiff, 0, _config.Diffs.Count - 1)];
                var bot = CreateBot(g.Room.B.V, _config.FcWear, _config.FcWeapon);
                if (bot != null)
                {
                    g.Bot = bot;
                    _bots[bot] = new BotInfo { Fc = g };
                    var drv = bot.gameObject.AddComponent<BotDriver>();
                    drv.Init(this, bot, g.Players[0], "fcbot", d, g.Room.B.V, g.Room.B.Right, 1.2f, 0f);
                    drv.Freeze(true);
                }
            }

            if (g.Room.Type == "door" && g.Room.Wall != null && !string.IsNullOrEmpty(_config.FcWallPrefab))
            {
                var wall = GameManager.server.CreateEntity(_config.FcWallPrefab, g.Room.Wall.V, Quaternion.Euler(0f, g.Room.Wall.Yaw, 0f));
                if (wall != null) { wall.enableSaving = false; wall.Spawn(); g.Wall = wall; }
            }

            foreach (var p in g.Players) { if (p != null) { DrawFcHud(p, g); Center(p, g.Room.Type == "door" ? T(Id(p), "FcWait") : T(Id(p), "FcReady"), ColGold, 1.2f); } }
            float delay = UnityEngine.Random.Range(_config.FcDelayMin, _config.FcDelayMax) + 1f;
            timer.Once(delay, () => OpenFc(g));
        }

        private void OpenFc(FcGame g)
        {
            if (!_games.Contains(g) || g.State != "closed") return;
            g.State = "open";
            g.OpenAt = Time.realtimeSinceStartup;
            if (g.Wall != null && !g.Wall.IsDestroyed) g.Wall.Kill();
            g.Wall = null;
            if (g.Bot != null) g.Bot.GetComponent<BotDriver>()?.Freeze(false);
            foreach (var p in g.Players) if (p != null) { Center(p, T(Id(p), "FcGo"), ColGreen, 0.8f); DrawFcHud(p, g); }
        }

        // winnerSide: 0 — игрок A (или игрок против бота), 1 — B / бот
        private void FcRoundOver(FcGame g, int winnerSide)
        {
            if (!_games.Contains(g) || g.State == "between" || g.State == "ended") return;
            g.State = "between";
            g.Score[winnerSide]++;
            BasePlayer winner = winnerSide < g.Players.Count ? g.Players[winnerSide] : null;
            int react;
            if (winner != null && g.FirstHit.TryGetValue(Id(winner), out react) && react > 0)
            {
                g.LastReact = react;
                var st = St(Id(winner));
                st.ReactSum += react; st.ReactN++;
                if (st.FcBestReact == 0 || react < st.FcBestReact) st.FcBestReact = react;
            }
            foreach (var p in g.Players)
            {
                if (p == null) continue;
                bool won = p == winner;
                Center(p, (won ? T(Id(p), "FcWon") : T(Id(p), "FcLost")) + $"\n<size=16>{g.Score[0]} : {g.Score[1]}{(won && g.LastReact > 0 ? "  ·  " + g.LastReact + " " + T(Id(p), "Ms") : "")}</size>", won ? ColGreen : ColRed, 1.8f);
                DrawFcHud(p, g);
            }
            if (g.Score[winnerSide] >= _config.FcRounds) { timer.Once(2f, () => EndFc(g, winnerSide, false)); return; }
            timer.Once(2f, () => StartFcRound(g));
        }

        private void EndFc(FcGame g, int winnerSide, bool forfeit)
        {
            if (!_games.Contains(g)) return;
            g.State = "ended";
            _games.Remove(g);
            if (g.Bot != null) { KillBot(g.Bot); g.Bot = null; }
            if (g.Wall != null && !g.Wall.IsDestroyed) g.Wall.Kill();
            for (int i = 0; i < g.Players.Count; i++)
            {
                var p = g.Players[i];
                if (p == null) continue;
                _fc.Remove(Id(p));
                CuiHelper.DestroyUi(p, UiHud);
                CuiHelper.DestroyUi(p, UiRec);
                if (!p.IsConnected) continue;
                if (winnerSide >= 0)
                {
                    bool won = i == winnerSide;
                    var st = St(Id(p));
                    if (g.Players.Count == 2)
                    {
                        if (won) { st.FcWins++; Ach(Id(p), "fc_wins"); if (Hub != null && _config.CoinsFcWin > 0) Hub.Call("AddCoins", Id(p), _config.CoinsFcWin, T(Id(p), "ForWin")); }
                        else st.FcLosses++;
                    }
                    Center(p, (won ? T(Id(p), "Victory") : T(Id(p), "Defeat")) + $"\n<size=16>{g.Score[0]} : {g.Score[1]}{(forfeit ? " · " + T(Id(p), "Forfeit") : "")}</size>", won ? ColGreen : ColRed, 4f);
                }
                if (!p.IsDead()) p.inventory.Strip();
                if (Hub != null && !p.IsDead()) { var pl = p; timer.Once(3f, () => { if (pl != null && pl.IsConnected && !_fc.ContainsKey(Id(pl))) Hub.Call("SendToMode", pl, _config.HubFc, true); }); }
            }
        }

        #endregion

        #region Тик

        private void Tick()
        {
            foreach (var s in _aim.Values.ToList())
            {
                if (s.Player == null || !s.Player.IsConnected) { _aim.Remove(Id(s.Player)); continue; }
                DrawHud(s);
            }
            foreach (var t in _queue.Keys.ToList()) TryMatchQueue(t);
        }

        #endregion

        #region Команды

        [ChatCommand("aim")]
        private void CmdAim(BasePlayer player, string c, string[] a)
        {
            if (a.Length > 0 && (a[0] == "stop" || a[0] == "leave")) { StopAim(player, true); return; }
            if (a.Length > 0 && AimModes.Contains(a[0]))
            {
                // из меню хаба: плашка режима — сразу выбираем его
                var p = Pf(Id(player));
                p.Mode = a[0];
                FixWeapon(p);
                if (Hub != null && !string.IsNullOrEmpty(_config.HubAim) && HubMode(player) != _config.HubAim)
                {
                    Hub.Call("SendToMode", player, _config.HubAim, false);
                    timer.Once(1.2f, () => { if (player != null && player.IsConnected) OpenAimMenu(player); });
                    return;
                }
            }
            OpenAimMenu(player);
        }

        [ChatCommand("fc")]
        private void CmdFc(BasePlayer player, string c, string[] a)
        {
            if (a.Length > 0 && (a[0] == "leave" || a[0] == "stop")) { LeaveFc(player); Toast(player, T(Id(player), "FcLeft"), false); return; }
            OpenFcMenu(player);
        }

        [ConsoleCommand("aim.ui")]
        private void CcUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs()) return;
            var id = Id(player);
            var p = Pf(id);
            string a1 = arg.GetString(1), a2 = arg.GetString(2);
            switch (arg.GetString(0))
            {
                case "close": CloseAll(player); break;
                case "menu": OpenAimMenu(player); break;
                case "set":
                    switch (a1)
                    {
                        case "mode": if (AimModes.Contains(a2)) { p.Mode = a2; FixWeapon(p); } break;
                        case "diff": p.Diff = Mathf.Clamp(int.Parse(a2), 0, _config.Diffs.Count - 1); break;
                        case "dist": p.Dist = int.Parse(a2); break;
                        case "armor": if (a2 == "auto" || _config.ArmorSets.ContainsKey(a2)) p.Armor = a2; break;
                        case "bots": p.Bots = Mathf.Clamp(p.Bots + int.Parse(a2), 1, _config.MaxBots); break;
                        case "weapon": p.Weapon = a2; break;
                        case "mod": if (!p.Mods.Remove(a2)) p.Mods.Add(a2); break;
                        case "mdelay": p.MirrorDelay = int.Parse(a2); break;
                        case "mshoot": p.MirrorShoots = !p.MirrorShoots; break;
                        case "fctype": p.FcType = a2; break;
                        case "fcbot": p.FcVsBot = a2 == "1"; break;
                    }
                    if (arg.GetString(3) == "fc") OpenFcMenu(player); else OpenAimMenu(player);
                    break;
                case "start": StartAim(player); break;
                case "stop": CloseAll(player); StopAim(player, true); break;
                case "fc.start": if (p.FcVsBot) StartFcSolo(player); else JoinFcQueue(player); break;
                case "fc.cancel": foreach (var q in _queue.Values) q.Remove(id); OpenFcMenu(player); break;
                case "fc.leave": CloseAll(player); LeaveFc(player); break;
            }
        }

        [ChatCommand("aimadmin")]
        private void CmdAdmin(BasePlayer player, string c, string[] a)
        {
            var id = Id(player);
            if (!IsAdmin(player)) { Reply(player, T(id, "NoPerm")); return; }
            if (a.Length == 0) { Reply(player, T(id, "AdminHelp")); return; }
            var pos = player.transform.position;
            float yaw = player.eyes != null ? player.eyes.rotation.eulerAngles.y : 0f;
            string key = a.Length > 1 ? a[1].ToLower() : null;
            switch (a[0].ToLower())
            {
                case "lane":
                    if (key == null) { Reply(player, "/aimadmin lane <id> [ширина]"); return; }
                    _data.Lanes[key] = new Lane { Id = key, Name = key, Stand = Pt.From(pos, yaw), Width = a.Length > 2 ? float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) : _config.LaneWidth };
                    SaveMap(); Reply(player, T(id, "AdmLane", key)); break;
                case "room":
                    if (key == null || a.Length < 3 || (a[2] != "door" && a[2] != "corners")) { Reply(player, "/aimadmin room <id> door|corners"); return; }
                    _data.Rooms[key] = new Room { Id = key, Name = key, Type = a[2] };
                    SaveMap(); Reply(player, T(id, "AdmRoom", key)); break;
                case "a": case "b": case "wall":
                {
                    Room r;
                    if (key == null || !_data.Rooms.TryGetValue(key, out r)) { Reply(player, T(id, "AdminHelp")); return; }
                    var pt = Pt.From(pos, yaw);
                    if (a[0] == "a") r.A = pt; else if (a[0] == "b") r.B = pt; else r.Wall = pt;
                    SaveMap(); Reply(player, T(id, "AdmOk", key)); break;
                }
                case "delete":
                    if (key != null && (_data.Lanes.Remove(key) | _data.Rooms.Remove(key))) { SaveMap(); Reply(player, T(id, "AdmDeleted")); }
                    break;
                case "list":
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var l in _data.Lanes.Values) sb.AppendLine($"дорожка {l.Id}: ширина {l.Width} м");
                    foreach (var r in _data.Rooms.Values) sb.AppendLine($"комната {r.Id} [{r.Type}]: A {(r.A != null ? "+" : "—")} B {(r.B != null ? "+" : "—")} стена {(r.Wall != null ? "+" : "—")}");
                    Reply(player, sb.Length > 0 ? sb.ToString() : T(id, "Empty")); break;
                }
                case "show":
                {
                    bool was = player.IsAdmin;
                    if (!was) { player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true); player.SendNetworkUpdateImmediate(); }
                    try
                    {
                        foreach (var l in _data.Lanes.Values)
                        {
                            player.SendConsoleCommand("ddraw.text", 15f, Color.green, l.Stand.V + Vector3.up * 2f, "дорожка " + l.Id);
                            foreach (var d in _config.Distances) player.SendConsoleCommand("ddraw.line", 15f, Color.yellow, l.Stand.V + l.Stand.Fwd * d - l.Stand.Right * l.Width / 2f + Vector3.up, l.Stand.V + l.Stand.Fwd * d + l.Stand.Right * l.Width / 2f + Vector3.up);
                        }
                        foreach (var r in _data.Rooms.Values)
                        {
                            if (r.A != null) player.SendConsoleCommand("ddraw.text", 15f, Color.red, r.A.V + Vector3.up * 2f, r.Id + " A");
                            if (r.B != null) player.SendConsoleCommand("ddraw.text", 15f, Color.cyan, r.B.V + Vector3.up * 2f, r.Id + " B");
                            if (r.Wall != null) player.SendConsoleCommand("ddraw.text", 15f, Color.white, r.Wall.V + Vector3.up * 2f, r.Id + " дверь");
                        }
                    }
                    finally { if (!was) { player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false); player.SendNetworkUpdateImmediate(); } }
                    break;
                }
                default: Reply(player, T(id, "AdminHelp")); break;
            }
        }

        private void Reply(BasePlayer player, string msg) => player.ChatMessage($"<color=#f0a6b1>[AimTrain]</color> {msg}");

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

        private const float M = 18f, G = 10f;

        private void Window(CuiElementContainer c, string name, string icon, string title, string sub, float w, float h, string closeCmd)
        {
            c.Add(new CuiPanel { Image = { Color = "0.03 0.02 0.02 0.5" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", name + ".dim");
            Root(c, name, name + ".dim", ColBg, "0.5 0.5", "0.5 0.5", $"{-w / 2f} {-h / 2f}", $"{w / 2f} {h / 2f}", 18f, true);
            Box(c, name, ColWine, M, M, 40, 40, 12f);
            Text(c, name, icon, 13, M, M, 40, 40, TextAnchor.MiddleCenter);
            Text(c, name, title, 18, M + 50, M - 2, w - 2 * (M + 50), 26, TextAnchor.MiddleCenter);
            Text(c, name, sub, 11, M + 50, M + 22, w - 2 * (M + 50), 18, TextAnchor.MiddleCenter, ColDim, FontReg);
            Btn(c, name, "X", closeCmd, ColBtn, 14, w - M - 40, M, 40, 40);
            Box(c, name, ColWine, 0, M + 52, w, 2, 0f);
        }

        private void DestroyWindow(BasePlayer p, string name) { CuiHelper.DestroyUi(p, name + ".dim"); CuiHelper.DestroyUi(p, name); }

        private void CloseAll(BasePlayer p) => DestroyWindow(p, UiMain);

        private void Seg(CuiElementContainer c, string parent, float x, float y, float w, float h, IList<string[]> items, string active, int size = 12)
        {
            int n = items.Count;
            float bw = (w - (n - 1) * 6f) / n;
            for (int i = 0; i < n; i++)
            {
                bool a = items[i][0] == active;
                Btn(c, parent, items[i][1], items[i][2], a ? ColWine : ColBtn, size, x + i * (bw + 6f), y, bw, h, a ? ColText : ColDim, 10f);
            }
        }

        #endregion

        #region Интерфейс: меню аим-трена

        private static readonly string[] AimModes = { "targets", "strafers", "flow", "shooters", "mirror", "bow", "fcbot" };

        private void OpenAimMenu(BasePlayer player)
        {
            var id = Id(player);
            var p = Pf(id);
            const float W = 960f, H = 700f;
            var c = new CuiElementContainer();
            Window(c, UiMain, "A", T(id, "AimTitle"), T(id, "AimSub"), W, H, "aim.ui close");
            var R = UiMain;
            float top = M + 52 + 2 + M;

            // 7 режимов
            float mw = (W - 2 * M - (AimModes.Length - 1) * G) / AimModes.Length;
            for (int i = 0; i < AimModes.Length; i++)
            {
                var m = AimModes[i];
                bool a = p.Mode == m;
                float x = M + i * (mw + G);
                Box(c, R, a ? ColWineSoft : ColPanel, x, top, mw, 96, 16f);
                Box(c, R, "0.48 0.07 0.14 0.45", x + mw / 2f - 20, top + 12, 40, 40, 12f);
                Text(c, R, T(id, "MIcon_" + m), 15, x + mw / 2f - 20, top + 12, 40, 40, TextAnchor.MiddleCenter, ColRose);
                Text(c, R, T(id, "M_" + m), 13, x, top + 56, mw, 18, TextAnchor.MiddleCenter);
                Text(c, R, T(id, "MD_" + m), 10, x, top + 74, mw, 16, TextAnchor.MiddleCenter, ColDim, FontReg);
                Btn(c, R, "", "aim.ui set mode " + m, "0 0 0 0", 10, x, top, mw, 96, ColText, 0f);
            }
            top += 96 + G;

            float cw = (W - 2 * M - G) / 2f, ph = H - M - 48 - G - top;
            // слева: сложность, дистанция, боты / настройки зеркала
            Box(c, R, ColPanel, M, top, cw, ph, 16f);
            float y = top + 10, iw = cw - 28;
            Text(c, R, T(id, "Difficulty"), 10, M + 14, y, iw, 16, TextAnchor.MiddleLeft, ColDim); y += 22;
            Seg(c, R, M + 14, y, iw, 32, _config.Diffs.Select((d, i) => new[] { i.ToString(), d.Name, "aim.ui set diff " + i }).ToList(), p.Diff.ToString()); y += 44;
            if (p.Mode == "mirror")
            {
                Text(c, R, T(id, "MirrorDelay"), 10, M + 14, y, iw, 16, TextAnchor.MiddleLeft, ColDim); y += 22;
                Seg(c, R, M + 14, y, iw, 32, new[] { 0, 100, 250 }.Select(d => new[] { d.ToString(), d + " " + T(id, "Ms"), "aim.ui set mdelay " + d }).ToList(), p.MirrorDelay.ToString()); y += 44;
                Box(c, R, ColRow, M + 14, y, iw, 38, 12f);
                Text(c, R, T(id, "MirrorShoots"), 12, M + 26, y, iw - 90, 38, TextAnchor.MiddleLeft, ColText, FontReg);
                Btn(c, R, p.MirrorShoots ? T(id, "On") : T(id, "Off"), "aim.ui set mshoot", p.MirrorShoots ? ColWine : ColBtn, 11, M + 14 + iw - 70, y + 6, 60, 26, ColText, 10f);
                y += 48;
            }
            else
            {
                Text(c, R, T(id, "Distance"), 10, M + 14, y, iw, 16, TextAnchor.MiddleLeft, ColDim); y += 22;
                var dists = (p.Mode == "bow" ? _config.BowDistances : _config.Distances).Select(d => new[] { d.ToString(), d + " м", "aim.ui set dist " + d }).ToList();
                dists.Add(new[] { "0", T(id, "Mix"), "aim.ui set dist 0" });
                Seg(c, R, M + 14, y, iw, 32, dists, p.Dist.ToString()); y += 44;
                Text(c, R, T(id, "BotArmor"), 10, M + 14, y, iw, 16, TextAnchor.MiddleLeft, ColDim); y += 22;
                var armors = new List<string[]> { new[] { "auto", T(id, "ArmorAuto"), "aim.ui set armor auto" } };
                foreach (var k in _config.ArmorSets.Keys.Take(5)) armors.Add(new[] { k, T(id, "Armor_" + k), "aim.ui set armor " + k });
                Seg(c, R, M + 14, y, iw, 32, armors, p.Armor); y += 44;
                Text(c, R, T(id, "BotsCount"), 10, M + 14, y, iw, 16, TextAnchor.MiddleLeft, ColDim); y += 22;
                Btn(c, R, "−", "aim.ui set bots -1", ColBtn, 16, M + 14, y, 40, 32, ColText, 10f);
                Box(c, R, ColRow, M + 60, y, 80, 32, 10f);
                Text(c, R, p.Bots.ToString(), 15, M + 60, y, 80, 32, TextAnchor.MiddleCenter);
                Btn(c, R, "+", "aim.ui set bots 1", ColBtn, 16, M + 146, y, 40, 32, ColText, 10f);
                y += 44;
            }
            // рекорды
            var st = St(id, player.displayName);
            Text(c, R, T(id, "Records"), 10, M + 14, y, iw, 16, TextAnchor.MiddleLeft, ColDim); y += 22;
            var recs = new[]
            {
                new[] { T(id, "BestAcc"), st.BestAcc > 0 ? st.BestAcc + "%" : "—" },
                new[] { T(id, "BestReact"), st.BestReact > 0 ? st.BestReact + " " + T(id, "Ms") : "—" },
                new[] { T(id, "BestHs"), st.BestHsStreak > 0 ? st.BestHsStreak.ToString() : "—" },
            };
            float rw = (iw - 2 * 6f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                Box(c, R, ColRow, M + 14 + i * (rw + 6), y, rw, 50, 12f);
                Text(c, R, recs[i][1], 16, M + 14 + i * (rw + 6), y + 4, rw, 26, TextAnchor.MiddleCenter, i == 0 ? ColGold : ColText);
                Text(c, R, recs[i][0], 9, M + 14 + i * (rw + 6), y + 30, rw, 16, TextAnchor.MiddleCenter, ColDim, FontReg);
            }

            // справа: оружие и модули
            float rx = M + cw + G;
            Box(c, R, ColPanel, rx, top, cw, ph, 16f);
            y = top + 10;
            Text(c, R, T(id, "Weapon"), 10, rx + 14, y, iw, 16, TextAnchor.MiddleLeft, ColDim); y += 22;
            float ww = (iw - 3 * 6f) / 4f;
            var wlist = p.Mode == "bow" ? _config.BowWeapons : _config.Weapons;
            for (int i = 0; i < wlist.Count && i < 12; i++)
            {
                var wpn = wlist[i];
                Btn(c, R, wpn.Name, "aim.ui set weapon " + wpn.Item, p.Weapon == wpn.Item ? ColWine : ColBtn, 11, rx + 14 + (i % 4) * (ww + 6), y + (i / 4) * 38, ww, 32, ColText, 10f);
            }
            y += Mathf.CeilToInt(Mathf.Min(12, wlist.Count) / 4f) * 38 + 10;
            if (p.Mode != "bow") Text(c, R, T(id, "Mods"), 10, rx + 14, y, iw, 16, TextAnchor.MiddleLeft, ColDim);
            else Text(c, R, T(id, "BowHint"), 11, rx + 14, y, iw, 36, TextAnchor.UpperLeft, ColDim, FontReg);
            y += 22;
            for (int i = 0; i < _config.Mods.Count && i < 8 && p.Mode != "bow"; i++)
            {
                var mod = _config.Mods[i];
                Btn(c, R, mod.Name, "aim.ui set mod " + mod.Item, p.Mods.Contains(mod.Item) ? ColWine : ColBtn, 11, rx + 14 + (i % 4) * (ww + 6), y + (i / 4) * 38, ww, 32, ColText, 10f);
            }
            y += Mathf.CeilToInt(Mathf.Min(8, _config.Mods.Count) / 4f) * 38 + 10;
            if (p.Mode != "bow") Text(c, R, T(id, "InfiniteAmmo"), 11, rx + 14, y, iw, 18, TextAnchor.MiddleLeft, ColDim, FontReg);

            bool running = _aim.ContainsKey(id);
            float by = H - M - 48, bw = (W - 2 * M - G) / 2f;
            Btn(c, R, running ? T(id, "Restart") : T(id, "StartBtn"), "aim.ui start", ColWine, 15, M, by, running ? bw : W - 2 * M, 48, ColText, 14f);
            if (running) Btn(c, R, T(id, "StopBtn"), "aim.ui stop", ColBtn, 13, M + bw + G, by, bw, 48, ColText, 14f);
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        private void OpenFcMenu(BasePlayer player)
        {
            var id = Id(player);
            var p = Pf(id);
            const float W = 720f, H = 520f;
            var c = new CuiElementContainer();
            Window(c, UiMain, "F", T(id, "FcTitle"), T(id, "FcSub", _config.FcRounds), W, H, "aim.ui close");
            var R = UiMain;
            float top = M + 52 + 2 + M, iw = W - 2 * M;
            Text(c, R, T(id, "FcType"), 10, M, top, iw, 16, TextAnchor.MiddleLeft, ColDim); top += 22;
            Seg(c, R, M, top, iw, 40, new List<string[]> { new[] { "door", T(id, "FcDoor"), "aim.ui set fctype door fc" }, new[] { "corners", T(id, "FcCorners"), "aim.ui set fctype corners fc" } }, p.FcType, 13);
            top += 52;
            Text(c, R, T(id, "FcVs"), 10, M, top, iw, 16, TextAnchor.MiddleLeft, ColDim); top += 22;
            Seg(c, R, M, top, iw, 40, new List<string[]> { new[] { "0", T(id, "FcVsPlayer"), "aim.ui set fcbot 0 fc" }, new[] { "1", T(id, "FcVsBot"), "aim.ui set fcbot 1 fc" } }, p.FcVsBot ? "1" : "0", 13);
            top += 52;

            var st = St(id, player.displayName);
            float tw = (iw - 3 * G) / 4f;
            var tiles = new[]
            {
                new[] { (st.FcWins + st.FcLosses).ToString(), T(id, "FcMatches") },
                new[] { st.FcWins + st.FcLosses > 0 ? Mathf.RoundToInt(100f * st.FcWins / (st.FcWins + st.FcLosses)) + "%" : "—", T(id, "FcWinrate") },
                new[] { st.FcBestReact > 0 ? st.FcBestReact + " " + T(id, "Ms") : "—", T(id, "BestReact") },
                new[] { _queue[p.FcType].Count.ToString(), T(id, "InQueue") },
            };
            for (int i = 0; i < 4; i++)
            {
                Box(c, R, ColPanel, M + i * (tw + G), top, tw, 70, 14f);
                Text(c, R, tiles[i][0], 20, M + i * (tw + G), top + 8, tw, 30, TextAnchor.MiddleCenter, i == 2 ? ColGold : ColText);
                Text(c, R, tiles[i][1], 10, M + i * (tw + G), top + 42, tw, 16, TextAnchor.MiddleCenter, ColDim, FontReg);
            }
            top += 70 + G;
            int free = _data.Rooms.Values.Count(r => r.Type == p.FcType && r.A != null && r.B != null && !_games.Any(g => g.Room.Id == r.Id));
            Text(c, R, T(id, "FcRooms", free), 11, M, top, iw, 20, TextAnchor.MiddleCenter, free > 0 ? ColDim : ColRed, FontReg);

            bool queued = _queue.Values.Any(q => q.Contains(id));
            bool playing = _fc.ContainsKey(id);
            float by = H - M - 48;
            if (playing) Btn(c, R, T(id, "FcLeaveBtn"), "aim.ui fc.leave", ColBtn, 14, M, by, iw, 48, ColText, 14f);
            else if (queued) Btn(c, R, T(id, "FcCancelQueue"), "aim.ui fc.cancel", ColBtn, 14, M, by, iw, 48, ColText, 14f);
            else Btn(c, R, p.FcVsBot ? T(id, "FcStartBot") : T(id, "FcQueue"), "aim.ui fc.start", ColWine, 15, M, by, iw, 48, ColText, 14f);
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        #endregion

        #region Интерфейс: HUD и итоги

        private void DrawHud(AimSession s)
        {
            var player = s.Player;
            if (player == null || !player.IsConnected) return;
            var id = Id(player);
            var c = new CuiElementContainer();
            const float W = 460f;
            Root(c, UiHud, "Hud", ColHud, "0.5 1", "0.5 1", $"{-W / 2f} -86", $"{W / 2f} -14", 16f, false);
            Text(c, UiHud, $"{T(id, "M_" + s.P.Mode).ToUpper()} · {s.D.Name.ToUpper()}{(s.P.Mode == "mirror" ? "" : " · " + (s.P.Dist > 0 ? s.P.Dist + " М" : T(id, "Mix").ToUpper()))}", 10, 0, 6, W, 16, TextAnchor.MiddleCenter, ColDim);
            int acc = s.Shots > 0 ? Mathf.RoundToInt(100f * Mathf.Min(s.Hits, s.Shots) / s.Shots) : 0;
            int hs = s.Hits > 0 ? Mathf.RoundToInt(100f * s.Hs / s.Hits) : 0;
            int react = s.Reacts.Count > 0 ? Mathf.RoundToInt((float)s.Reacts.Average()) : 0;
            int dist = s.DistN > 0 ? Mathf.RoundToInt((float)(s.DistSum / s.DistN)) : 0;
            int secs = Mathf.FloorToInt(Time.realtimeSinceStartup - s.Started);
            var vals = new[] { acc + "%", hs + "%", react > 0 ? react + " " + T(id, "Ms") : "—", dist > 0 ? dist + " м" : "—", s.Kills.ToString(), $"{secs / 60}:{secs % 60:00}" };
            var lbls = new[] { T(id, "HAcc"), T(id, "HHs"), T(id, "HReact"), T(id, "HDist"), T(id, "HKills"), T(id, "HTime") };
            float cw = (W - 20f) / 6f;
            for (int i = 0; i < 6; i++)
            {
                Text(c, UiHud, vals[i], 17, 10 + i * cw, 22, cw, 26, TextAnchor.MiddleCenter, i == 1 ? ColGold : ColText);
                Text(c, UiHud, lbls[i], 9, 10 + i * cw, 48, cw, 14, TextAnchor.MiddleCenter, ColDim, FontReg);
            }
            CuiHelper.DestroyUi(player, UiHud);
            AddUiScaled(player, c);
        }

        private static readonly string[] ZoneKeys = { "ZHead", "ZBody", "ZLimbs" };

        // урон по зонам — справа сверху
        private void DrawZones(AimSession s)
        {
            var player = s.Player;
            if (player == null || !player.IsConnected) return;
            var id = Id(player);
            var c = new CuiElementContainer();
            Root(c, UiZones, "Hud", ColHud, "1 1", "1 1", "-236 -150", "-16 -14", 16f, false);
            Text(c, UiZones, T(id, "ZonesTitle").ToUpper(), 10, 14, 8, 192, 16, TextAnchor.MiddleLeft, ColDim);
            Text(c, UiZones, T(id, "ZonesAvg"), 9, 14, 8, 192, 16, TextAnchor.MiddleRight, ColDim, FontReg);
            for (int z = 0; z < 3; z++)
            {
                float y = 30 + z * 26;
                int n = s.ZoneHits[z];
                Text(c, UiZones, T(id, ZoneKeys[z]), 12, 14, y, 90, 22, TextAnchor.MiddleLeft, z == 0 ? ColGold : ColText, FontReg);
                Text(c, UiZones, n.ToString(), 11, 104, y, 40, 22, TextAnchor.MiddleRight, ColDim, FontReg);
                Text(c, UiZones, n > 0 ? (s.ZoneDmg[z] / n).ToString("0.0") : "—", 14, 144, y, 62, 22, TextAnchor.MiddleRight, z == 0 ? ColGold : ColText);
            }
            Box(c, UiZones, "1 1 1 0.08", 14, 110, 192, 1, 0f);
            Text(c, UiZones, s.LastZone >= 0 ? T(id, "ZLast", s.LastDmg.ToString("0.0"), T(id, ZoneKeys[s.LastZone]).ToLower()) : "—", 11, 14, 112, 192, 20, TextAnchor.MiddleLeft, ColDim, FontReg);
            CuiHelper.DestroyUi(player, UiZones);
            AddUiScaled(player, c);
        }

        // рисунок очереди: силуэт бота и точки попаданий
        private void FinishBurst(AimSession s)
        {
            if (s == null || s.Player == null || !s.Player.IsConnected || !_aim.ContainsKey(Id(s.Player))) return;
            int shots = s.BurstShots;
            var hits = s.BurstHits.ToList();
            var zones = s.BurstZones.ToList();
            s.BurstShots = 0; s.BurstHits.Clear(); s.BurstZones.Clear();
            if (shots < _config.SprayMinShots) return;
            var player = s.Player;
            var id = Id(player);
            var c = new CuiElementContainer();
            const float W = 196f, H = 300f, SX = 98f, Foot = 262f, PxM = 120f;   // 1 м = 120 px
            Root(c, UiSpray, "Hud", ColHud, "0 0.5", "0 0.5", $"16 {-H / 2f}", $"{16 + W} {H / 2f}", 16f, false);
            Text(c, UiSpray, T(id, "SprayTitle").ToUpper(), 10, 14, 8, W - 28, 16, TextAnchor.MiddleLeft, ColDim);
            Text(c, UiSpray, T(id, "SprayHits", hits.Count, shots), 12, 14, 24, W - 28, 18, TextAnchor.MiddleLeft, ColText);
            // силуэт (рост ~1.8 м)
            string sil = "1 1 1 0.07";
            Box(c, UiSpray, sil, SX - 12, Foot - 1.80f * PxM, 24, 26, 12f);                // голова
            Box(c, UiSpray, sil, SX - 26, Foot - 1.52f * PxM, 52, 0.62f * PxM, 10f);       // корпус
            Box(c, UiSpray, sil, SX - 40, Foot - 1.48f * PxM, 12, 0.56f * PxM, 6f);        // руки
            Box(c, UiSpray, sil, SX + 28, Foot - 1.48f * PxM, 12, 0.56f * PxM, 6f);
            Box(c, UiSpray, sil, SX - 22, Foot - 0.88f * PxM, 20, 0.88f * PxM, 7f);        // ноги
            Box(c, UiSpray, sil, SX + 2, Foot - 0.88f * PxM, 20, 0.88f * PxM, 7f);
            for (int i = 0; i < hits.Count; i++)
            {
                float x = Mathf.Clamp(SX + hits[i].x * PxM, 8, W - 8), y = Mathf.Clamp(Foot - hits[i].y * PxM, 46, H - 10);
                string col = zones[i] == 0 ? ColGold : zones[i] == 1 ? ColRose : "0.75 0.70 0.69 1";
                float d = i == 0 ? 8f : 6f;
                Box(c, UiSpray, col, x - d / 2f, y - d / 2f, d, d, d / 2f);
            }
            Text(c, UiSpray, T(id, "SprayFoot"), 9, 14, H - 22, W - 28, 14, TextAnchor.MiddleLeft, ColDim, FontReg);
            CuiHelper.DestroyUi(player, UiSpray);
            AddUiScaled(player, c);
            var p = player;
            timer.Once(_config.SprayShow, () => { if (p != null && p.IsConnected) CuiHelper.DestroyUi(p, UiSpray); });
        }

        private void DrawFcHud(BasePlayer player, FcGame g)
        {
            var id = Id(player);
            var c = new CuiElementContainer();
            const float W = 400f;
            Root(c, UiHud, "Hud", ColHud, "0.5 1", "0.5 1", $"{-W / 2f} -92", $"{W / 2f} -14", 16f, false);
            Text(c, UiHud, $"{T(id, "FcTitle")} · {(g.Room.Type == "door" ? T(id, "FcDoor") : T(id, "FcCorners")).ToUpper()} · {(g.Players.Count == 2 ? "1v1" : T(id, "FcVsBot").ToUpper())}", 10, 0, 6, W, 16, TextAnchor.MiddleCenter, ColDim);
            string state = g.State == "open" ? T(id, "FcGo") : g.State == "closed" ? T(id, "FcWaitShort") : "";
            Text(c, UiHud, state, 22, 0, 22, W, 30, TextAnchor.MiddleCenter, g.State == "open" ? ColGreen : ColGold);
            string a = g.Players.Count > 0 ? g.Players[0].displayName : "?";
            string b = g.Players.Count > 1 ? g.Players[1].displayName : T(id, "Bot");
            Text(c, UiHud, $"{a} · {g.Score[0]}", 13, 14, 52, W / 2f - 60, 20, TextAnchor.MiddleLeft);
            Text(c, UiHud, T(id, "ToWins", _config.FcRounds), 10, W / 2f - 50, 52, 100, 20, TextAnchor.MiddleCenter, ColDim, FontReg);
            Text(c, UiHud, $"{g.Score[1]} · {b}", 13, W / 2f + 46, 52, W / 2f - 60, 20, TextAnchor.MiddleRight);

            var st = St(id);
            Root(c, UiRec, "Hud", ColHud, "1 1", "1 1", "-216 -100", "-16 -14", 16f, false);
            Text(c, UiRec, T(id, "LastReact"), 10, 12, 8, 110, 20, TextAnchor.MiddleLeft, ColDim, FontReg);
            Text(c, UiRec, g.LastReact > 0 ? g.LastReact + " " + T(id, "Ms") : "—", 12, 120, 8, 68, 20, TextAnchor.MiddleRight);
            Text(c, UiRec, T(id, "BestReact"), 10, 12, 32, 110, 20, TextAnchor.MiddleLeft, ColDim, FontReg);
            Text(c, UiRec, st.FcBestReact > 0 ? st.FcBestReact + " " + T(id, "Ms") : "—", 12, 120, 32, 68, 20, TextAnchor.MiddleRight, ColGold);
            Text(c, UiRec, T(id, "FcRound"), 10, 12, 56, 110, 20, TextAnchor.MiddleLeft, ColDim, FontReg);
            Text(c, UiRec, g.Round.ToString(), 12, 120, 56, 68, 20, TextAnchor.MiddleRight);
            CuiHelper.DestroyUi(player, UiHud);
            CuiHelper.DestroyUi(player, UiRec);
            AddUiScaled(player, c);
        }

        private void ShowResult(BasePlayer player, AimSession s, bool newAcc, bool newReact, bool newHs)
        {
            var id = Id(player);
            const float W = 620f, H = 400f;
            var c = new CuiElementContainer();
            Window(c, UiMain, "A", T(id, "ResultTitle"), $"{T(id, "M_" + s.P.Mode)} · {s.D.Name} · {_config.Weapons.FirstOrDefault(w => w.Item == s.P.Weapon)?.Name ?? s.P.Weapon}", W, H, "aim.ui close");
            var R = UiMain;
            float top = M + 52 + 2 + M;
            int acc = s.Shots > 0 ? Mathf.RoundToInt(100f * Mathf.Min(s.Hits, s.Shots) / s.Shots) : 0;
            int hs = s.Hits > 0 ? Mathf.RoundToInt(100f * s.Hs / s.Hits) : 0;
            int react = s.Reacts.Count > 0 ? Mathf.RoundToInt((float)s.Reacts.Average()) : 0;
            int best = s.Reacts.Count > 0 ? s.Reacts.Min() : 0;
            var tiles = new[]
            {
                new[] { acc + "%", T(id, "HAcc"), newAcc ? "1" : "" }, new[] { hs + "%", T(id, "HHs"), "" }, new[] { s.Kills.ToString(), T(id, "HKills"), "" },
                new[] { react > 0 ? react + " " + T(id, "Ms") : "—", T(id, "AvgReact"), "" }, new[] { best > 0 ? best + " " + T(id, "Ms") : "—", T(id, "BestReact"), newReact ? "1" : "" },
                new[] { s.BestHsStreak.ToString(), T(id, "BestHs"), newHs ? "1" : "" },
            };
            float tw = (W - 2 * M - 2 * G) / 3f;
            for (int i = 0; i < tiles.Length; i++)
            {
                float x = M + (i % 3) * (tw + G), y = top + (i / 3) * (86 + G);
                bool rec = tiles[i][2] == "1";
                Box(c, R, rec ? ColWineSoft : ColPanel, x, y, tw, 86, 14f);
                Text(c, R, tiles[i][0], 24, x, y + 10, tw, 36, TextAnchor.MiddleCenter, rec ? ColGold : ColText);
                Text(c, R, tiles[i][1] + (rec ? " · " + T(id, "NewRecord") : ""), 10, x, y + 52, tw, 18, TextAnchor.MiddleCenter, rec ? ColGold : ColDim, FontReg);
            }
            float by = H - M - 46, bw = (W - 2 * M - G) / 2f;
            Btn(c, R, T(id, "Again"), "aim.ui start", ColWine, 14, M, by, bw, 46, ColText, 14f);
            Btn(c, R, T(id, "Settings"), "aim.ui menu", ColBtn, 13, M + bw + G, by, bw, 46, ColText, 14f);
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        private void Center(BasePlayer player, string text, string color, float seconds)
        {
            if (player == null || !player.IsConnected) return;
            var c = new CuiElementContainer();
            c.Add(new CuiLabel { Text = { Text = text, FontSize = 26, Align = TextAnchor.MiddleCenter, Color = color, Font = Font }, RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-400 40", OffsetMax = "400 150" } }, "Hud", UiCenter);
            CuiHelper.DestroyUi(player, UiCenter);
            AddUiScaled(player, c);
            var p = player;
            timer.Once(seconds, () => { if (p != null && p.IsConnected) CuiHelper.DestroyUi(p, UiCenter); });
        }

        private void Toast(BasePlayer player, string text, bool warn)
        {
            if (player == null || !player.IsConnected) return;
            var c = new CuiElementContainer();
            Root(c, UiToast, "Hud", warn ? "0.40 0.06 0.11 0.92" : "0.07 0.065 0.065 0.90", "0.5 1", "0.5 1", "-240 -140", "240 -98", 20f, false);
            c.Add(new CuiLabel { Text = { Text = text, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = ColText, Font = Font }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, UiToast);
            CuiHelper.DestroyUi(player, UiToast);
            AddUiScaled(player, c);
            var p = player;
            timer.Once(3f, () => { if (p != null && p.IsConnected) CuiHelper.DestroyUi(p, UiToast); });
        }

        #endregion

        #region Профиль и API

        private string OnHubProfile(ulong userId, string lng)
        {
            PStats s;
            if (!_stats.TryGetValue(userId, out s) || s.Shots == 0 && s.FcWins + s.FcLosses == 0) return null;
            bool en = lng == "en";
            string acc = s.Shots > 0 ? Mathf.RoundToInt(100f * Mathf.Min(s.Hits, s.Shots) / s.Shots) + "%" : "—";
            string hs = s.Hits > 0 ? Mathf.RoundToInt(100f * s.Headshots / s.Hits) + "%" : "—";
            string react = s.ReactN > 0 ? Mathf.RoundToInt((float)(s.ReactSum / s.ReactN)) + (en ? " ms" : " мс") : "—";
            return en
                ? $"<color=#f0a6b1>AIM TRAIN</color>\nAccuracy {acc} · Headshots {hs} · Reaction {react}\nBest: {s.BestAcc}% · {s.BestReact} ms · HS streak {s.BestHsStreak}\nFacechecks {s.FcWins}–{s.FcLosses}"
                : $"<color=#f0a6b1>АИМ-ТРЕН</color>\nТочность {acc} · Хедшоты {hs} · Реакция {react}\nРекорды: {s.BestAcc}% · {s.BestReact} мс · серия ХШ {s.BestHsStreak}\nФейсчеки {s.FcWins}–{s.FcLosses}";
        }

        [HookMethod("IsTraining")] public bool IsTraining(BasePlayer player) => player != null && (_aim.ContainsKey(Id(player)) || _fc.ContainsKey(Id(player)));

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
            new[] { "BotArmor", "БРОНЯ БОТОВ — ПРОВЕРКА УРОНА", "BOT ARMOR — DAMAGE TEST" }, new[] { "ArmorAuto", "авто", "auto" },
            new[] { "Armor_none", "без брони", "none" }, new[] { "Armor_wood", "дерево", "wood" }, new[] { "Armor_roadsign", "роадсайн", "roadsign" },
            new[] { "Armor_hqm", "МВК", "HQM" }, new[] { "Armor_heavy", "тяжёлая", "heavy" },
            new[] { "ZonesTitle", "Урон по зонам", "Damage by zone" }, new[] { "ZonesAvg", "попад. · средний", "hits · avg" },
            new[] { "ZHead", "Голова", "Head" }, new[] { "ZBody", "Тело", "Body" }, new[] { "ZLimbs", "Руки и ноги", "Limbs" },
            new[] { "ZLast", "последний: {0} · {1}", "last: {0} · {1}" },
            new[] { "SprayTitle", "Рисунок очереди", "Spray pattern" }, new[] { "SprayHits", "{0} из {1} попали", "{0} of {1} hit" },
            new[] { "SprayFoot", "крупная точка — первый выстрел", "big dot — first shot" },
            new[] { "AimTitle", "АИМ-ТРЕН", "AIM TRAIN" }, new[] { "AimSub", "любое оружие · бесконечные патроны · /aim", "any weapon · infinite ammo · /aim" },
            new[] { "M_targets", "Мишени", "Targets" }, new[] { "MD_targets", "стоят на дистанциях", "standing at range" }, new[] { "MIcon_targets", "T", "T" },
            new[] { "M_strafers", "Стрейферы", "Strafers" }, new[] { "MD_strafers", "бегают и прыгают", "run and jump" }, new[] { "MIcon_strafers", "S", "S" },
            new[] { "M_flow", "Aim Flow", "Aim Flow" }, new[] { "MD_flow", "всегда в движении", "always moving" }, new[] { "MIcon_flow", "~", "~" },
            new[] { "Go_flow", "Aim Flow: веди прицел!", "Aim Flow: track them!" },
            new[] { "M_bow", "Лук", "Bow" }, new[] { "MD_bow", "лук и арбалет", "bow & crossbow" }, new[] { "MIcon_bow", "B", "B" },
            new[] { "Go_bow", "Мишени для лука готовы", "Bow targets ready" },
            new[] { "BowHint", "Стрелы бесконечные. Учитывай падение стрелы на дистанции.", "Infinite arrows. Mind the arrow drop at range." },
            new[] { "M_shooters", "Стреляют в ответ", "Shoot back" }, new[] { "MD_shooters", "убей первым", "kill first" }, new[] { "MIcon_shooters", "!", "!" },
            new[] { "M_mirror", "Бот-зеркало", "Mirror bot" }, new[] { "MD_mirror", "повторяет тебя", "copies you" }, new[] { "MIcon_mirror", "M", "M" },
            new[] { "M_fcbot", "Фейсчек с ботом", "Facecheck vs bot" }, new[] { "MD_fcbot", "дверь по таймеру", "timed door" }, new[] { "MIcon_fcbot", "F", "F" },
            new[] { "Go_targets", "Мишени расставлены", "Targets ready" }, new[] { "Go_strafers", "Стрейферы пошли!", "Strafers go!" },
            new[] { "Go_shooters", "Они стреляют в ответ!", "They shoot back!" }, new[] { "Go_mirror", "Двигайся — зеркало повторит", "Move — the mirror copies you" }, new[] { "Go_fcbot", "", "" },
            new[] { "Difficulty", "СЛОЖНОСТЬ", "DIFFICULTY" }, new[] { "Distance", "ДИСТАНЦИЯ", "DISTANCE" }, new[] { "Mix", "микс", "mix" },
            new[] { "BotsCount", "БОТОВ ОДНОВРЕМЕННО", "BOTS AT ONCE" }, new[] { "Weapon", "ОРУЖИЕ", "WEAPON" }, new[] { "Mods", "МОДУЛИ", "ATTACHMENTS" },
            new[] { "MirrorDelay", "ЗАДЕРЖКА ЗЕРКАЛА", "MIRROR DELAY" }, new[] { "MirrorShoots", "Зеркало стреляет в ответ", "Mirror shoots back" },
            new[] { "On", "ВКЛ", "ON" }, new[] { "Off", "ВЫКЛ", "OFF" }, new[] { "Ms", "мс", "ms" },
            new[] { "InfiniteAmmo", "Патроны бесконечные, магазин пополняется сам.", "Infinite ammo — the magazine refills itself." },
            new[] { "Records", "ТВОИ РЕКОРДЫ", "YOUR RECORDS" }, new[] { "BestAcc", "точность", "accuracy" }, new[] { "BestReact", "лучшая реакция", "best reaction" }, new[] { "BestHs", "серия ХШ", "HS streak" },
            new[] { "StartBtn", "НАЧАТЬ ТРЕНИРОВКУ", "START TRAINING" }, new[] { "Restart", "ПЕРЕЗАПУСТИТЬ", "RESTART" }, new[] { "StopBtn", "ЗАКОНЧИТЬ", "STOP" },
            new[] { "HAcc", "точность", "accuracy" }, new[] { "HHs", "хедшоты", "headshots" }, new[] { "HReact", "реакция", "reaction" },
            new[] { "HDist", "ср. дист.", "avg dist" }, new[] { "HKills", "убито", "kills" }, new[] { "HTime", "время", "time" },
            new[] { "AvgReact", "ср. реакция", "avg reaction" }, new[] { "NewRecord", "РЕКОРД!", "RECORD!" },
            new[] { "ResultTitle", "ИТОГИ ТРЕНИРОВКИ", "TRAINING RESULTS" }, new[] { "Again", "ЕЩЁ РАЗ", "AGAIN" }, new[] { "Settings", "НАСТРОЙКИ", "SETTINGS" },
            new[] { "NoLane", "Все дорожки заняты — подожди минуту", "All lanes are busy" },
            new[] { "GoingToMode", "Переходим в режим — нажми ещё раз после переноса", "Switching mode — press again after the teleport" },
            new[] { "BotGotYou", "Бот оказался быстрее", "The bot was faster" },

            new[] { "FcTitle", "ФЕЙСЧЕК", "FACECHECK" }, new[] { "FcSub", "дверь по таймеру или углы · до {0} побед · /fc", "timed door or corners · first to {0} · /fc" },
            new[] { "FcType", "ТИП", "TYPE" }, new[] { "FcDoor", "Дверь", "Door" }, new[] { "FcCorners", "Углы", "Corners" },
            new[] { "FcVs", "ПРОТИВ КОГО", "OPPONENT" }, new[] { "FcVsPlayer", "Игрок", "Player" }, new[] { "FcVsBot", "Бот", "Bot" }, new[] { "Bot", "Бот", "Bot" },
            new[] { "FcMatches", "матчей", "matches" }, new[] { "FcWinrate", "побед", "win rate" }, new[] { "InQueue", "в очереди", "in queue" },
            new[] { "FcRooms", "свободных комнат: {0}", "free rooms: {0}" },
            new[] { "FcQueue", "ВСТАТЬ В ОЧЕРЕДЬ", "JOIN QUEUE" }, new[] { "FcStartBot", "НАЧАТЬ ПРОТИВ БОТА", "START VS BOT" },
            new[] { "FcCancelQueue", "ВЫЙТИ ИЗ ОЧЕРЕДИ", "LEAVE QUEUE" }, new[] { "FcLeaveBtn", "ВЫЙТИ ИЗ МАТЧА", "LEAVE MATCH" },
            new[] { "Queued", "Ты в очереди ({0})", "Queued ({0})" }, new[] { "AlreadyFc", "Ты уже в матче", "Already in a match" },
            new[] { "NoRoom", "Свободных комнат нет", "No free rooms" }, new[] { "FcLeft", "Ты вышел из фейсчека", "You left the facecheck" },
            new[] { "FcWait", "ЖДИ…", "WAIT…" }, new[] { "FcWaitShort", "ЖДИ…", "WAIT…" }, new[] { "FcReady", "ГОТОВЬСЯ…", "GET READY…" }, new[] { "FcGo", "ОГОНЬ!", "GO!" },
            new[] { "FcWon", "РАУНД ТВОЙ", "ROUND WON" }, new[] { "FcLost", "РАУНД ПРОИГРАН", "ROUND LOST" },
            new[] { "FcRound", "раунд", "round" }, new[] { "LastReact", "последняя реакция", "last reaction" },
            new[] { "ToWins", "до {0}", "to {0}" },
            new[] { "Victory", "ПОБЕДА", "VICTORY" }, new[] { "Defeat", "ПОРАЖЕНИЕ", "DEFEAT" }, new[] { "Forfeit", "соперник вышел", "opponent left" },
            new[] { "ForWin", "за победу", "for the win" },

            new[] { "NoPerm", "Нет прав", "No permission" }, new[] { "Empty", "Пусто", "Empty" },
            new[] { "AdmLane", "Дорожка {0}: стоишь там, где будет игрок; боты встанут впереди по взгляду", "Lane {0} saved" },
            new[] { "AdmRoom", "Комната {0}: теперь a / b — места игроков, wall — где «дверь»", "Room {0}: now set a / b / wall" },
            new[] { "AdmOk", "{0}: сохранено", "{0}: saved" }, new[] { "AdmDeleted", "Удалено", "Deleted" },
            new[] { "AdminHelp",
                "<color=#f0a6b1>/aimadmin</color>\n" +
                "lane <id> [ширина] — дорожка: встань на место игрока лицом к полигону\n" +
                "room <id> door|corners — комната фейсчека\n" +
                "a <id> · b <id> — места двух игроков (смотри друг на друга)\n" +
                "wall <id> — где появляется «дверь» (только для door)\n" +
                "delete <id> · list · show",
                "<color=#f0a6b1>/aimadmin</color> lane <id> [width] · room <id> door|corners · a <id> · b <id> · wall <id> · delete <id> · list · show" },
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
