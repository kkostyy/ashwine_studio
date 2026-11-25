// =====================================================================================
//  Minigames 1.0 — большие режимы хаб-сервера Rust (Oxide / Carbon)
// =====================================================================================
//  РОУМ      — открытая зона, вход в любой момент: выбор кита и транспорта (машина, лошадь,
//              миникоптер), лут с трупов, команда Rust = своя группа, счёт убийств.
//  ХЕЛИ-БОИ  — команды на атак-хели (пилот + стрелок), хели возрождаются на площадках,
//              граница зоны и потолок высоты, до N убийств или по времени.
//  BEDWARS   — 2–8 команд на островах: у каждой «кровать» (спальник), генераторы ресурсов,
//              магазин; пока кровать цела — возрождаешься, последняя команда побеждает.
//  РЕЙД vs РЕЙД — фаза стройки (у каждой команды своя зона и ресурсы), затем фаза рейда:
//              кто первым уничтожит шкаф противника; по времени — у кого больше уцелело.
//
//  Общее: очередь → отсчёт → матч; команды поровну; HUD со счётом и таймером;
//  всё построенное и выпавшее убирается после матча. Работает с Hub (режимы heli/roam/
//  bedwars/raidwars) и без него.
//
//  Права:  minigames.admin — разметка карт
//  Чат:    /mg <roam|heli|bedwars|raidwars> — меню режима · /mg leave · /shopbw — магазин Bedwars
//  Админ:  /mgadmin — команды разметки
// =====================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Minigames", "Claude", "1.3.2")]
    [Description("Роум, хели-бои, Bedwars и рейд против рейда")]
    public class Minigames : RustPlugin
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
        [PluginReference] private Plugin Hub, Ranked;

        #region Константы

        private const string PermAdmin = "minigames.admin";
        private const string UiMain = "Mg.Main";
        private const string UiHud = "Mg.Hud";
        private const string UiCenter = "Mg.Center";
        private const string UiToast = "Mg.Toast";

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

        private static readonly string[] Types = { "roam", "heli", "bedwars", "raidwars" };
        private const int MaxTeams = 8;
        private static readonly string[] TeamNames = { "Красные", "Синие", "Зелёные", "Жёлтые", "Фиолетовые", "Бирюзовые", "Оранжевые", "Белые" };
        private static readonly string[] TeamNamesEn = { "Red", "Blue", "Green", "Yellow", "Purple", "Cyan", "Orange", "White" };
        private static readonly string[] TeamCols = { "0.88 0.34 0.42 1", "0.44 0.71 1 1", "0.44 0.81 0.50 1", "0.91 0.77 0.42 1", "0.72 0.52 0.95 1", "0.36 0.85 0.85 1", "0.98 0.60 0.30 1", "0.92 0.92 0.92 1" };
        private static readonly string[] TeamHex = { "#e0566b", "#6fb6ff", "#6fcf7f", "#e7c46a", "#b885f2", "#5cd9d9", "#fa994d", "#ebebeb" };

        private const string PrefabCar = "assets/content/vehicles/modularcar/2module_car_spawned.entity.prefab";
        private const string PrefabHorse = "assets/rust.ai/nextai/testridablehorse.prefab";
        private const string PrefabMini = "assets/content/vehicles/minicopter/minicopter.entity.prefab";
        private const string PrefabAttackHeli = "assets/content/vehicles/attackhelicopter/attackhelicopter.entity.prefab";
        private const string PrefabBed = "assets/prefabs/deployable/sleeping bag/sleepingbag_leather_deployed.prefab";
        private const string PrefabCupboard = "assets/prefabs/deployable/tool cupboard/cupboard.tool.deployed.prefab";

        #endregion

        #region Конфиг

        private class KitEntry
        {
            [JsonProperty("Предмет")] public string Item;
            [JsonProperty("Кол-во")] public int Amount = 1;
            [JsonProperty("Куда (main/belt/wear)")] public string Container = "main";
            [JsonProperty("Модули")] public List<string> Mods = new List<string>();
        }

        private class Kit
        {
            [JsonProperty("ID")] public string Id;
            [JsonProperty("Название")] public string Name;
            [JsonProperty("Описание")] public string Desc;
            [JsonProperty("Предметы")] public List<KitEntry> Items = new List<KitEntry>();
        }

        private class ShopItem
        {
            [JsonProperty("Название")] public string Name;
            [JsonProperty("Предмет")] public string Item;
            [JsonProperty("Кол-во")] public int Amount = 1;
            [JsonProperty("Цена: предмет-валюта")] public string Currency = "wood";
            [JsonProperty("Цена: кол-во")] public int Price = 16;
            [JsonProperty("Модули")] public List<string> Mods = new List<string>();
        }

        private class Configuration
        {
            [JsonProperty("Отсчёт до старта, когда набралось минимум (сек)")] public int StartCountdown = 30;
            [JsonProperty("Минимум игроков: хели / bedwars / рейд")] public int[] MinPlayers = { 2, 2, 2 };
            [JsonProperty("Максимум игроков: хели / bedwars / рейд")] public int[] MaxPlayers = { 8, 16, 10 };
            [JsonProperty("Возрождение (сек)")] public float Respawn = 5f;

            [JsonProperty("Роум: киты")] public List<Kit> RoamKits = DefaultRoamKits();
            [JsonProperty("Роум: транспорт исчезает без водителя через (сек)")] public float RoamVehicleTtl = 240f;
            [JsonProperty("Роум: монеты за убийство")] public int RoamKillCoins = 2;

            [JsonProperty("Хели: убийств до победы")] public int HeliKills = 10;
            [JsonProperty("Хели: длина матча (мин)")] public int HeliMinutes = 12;
            [JsonProperty("Хели: потолок высоты над землёй (м)")] public float HeliCeiling = 250f;
            [JsonProperty("Хели: новый хели через (сек)")] public float HeliRespawn = 15f;
            [JsonProperty("Хели: топливо (низкосорт.)")] public int HeliFuel = 200;
            [JsonProperty("Хели: набор игрока")] public List<KitEntry> HeliKit = new List<KitEntry>
            {
                new KitEntry { Item = "rifle.ak", Container = "belt", Mods = new List<string> { "weapon.mod.holosight" } },
                new KitEntry { Item = "ammo.rifle", Amount = 256 }, new KitEntry { Item = "syringe.medical", Amount = 3, Container = "belt" },
                new KitEntry { Item = "metal.facemask", Container = "wear" }, new KitEntry { Item = "roadsign.jacket", Container = "wear" },
                new KitEntry { Item = "hoodie", Container = "wear" }, new KitEntry { Item = "pants", Container = "wear" }, new KitEntry { Item = "shoes.boots", Container = "wear" },
            };
            [JsonProperty("Хели: боезапас в хели (что поместится)")] public List<KitEntry> HeliAmmo = new List<KitEntry>
            {
                new KitEntry { Item = "ammo.rocket.hv", Amount = 12 }, new KitEntry { Item = "ammo.rifle", Amount = 500 },
            };
            [JsonProperty("Хели: монеты победителям")] public int HeliWinCoins = 30;

            [JsonProperty("Вертолётный ММ: побед в раундах до конца матча")] public int HmRounds = 3;
            [JsonProperty("Вертолётный ММ: отсчёт перед раундом (сек)")] public int HmCountdown = 6;
            [JsonProperty("Вертолётный ММ: пауза между раундами (сек)")] public int HmBetween = 5;
            [JsonProperty("Вертолётный ММ: окно MMR на старте")] public int HmWindow = 150;
            [JsonProperty("Вертолётный ММ: окно растёт на (за 15 сек)")] public int HmWindowStep = 75;
            [JsonProperty("Вертолётный ММ: монеты за победу")] public int HmWinCoins = 20;
            [JsonProperty("Вертолётный ММ: топливо")] public int HmFuel = 150;
            [JsonProperty("Вертолётный ММ: набор стрелка мини (ракеты)")] public List<KitEntry> HmMiniGunner = new List<KitEntry>
            {
                new KitEntry { Item = "homingmissile.launcher", Container = "belt" }, new KitEntry { Item = "ammo.rocket.seeker", Amount = 8 },
                new KitEntry { Item = "rocket.launcher", Container = "belt" }, new KitEntry { Item = "ammo.rocket.basic", Amount = 6 },
                new KitEntry { Item = "flare", Amount = 4, Container = "belt" },
                new KitEntry { Item = "metal.facemask", Container = "wear" }, new KitEntry { Item = "roadsign.jacket", Container = "wear" },
                new KitEntry { Item = "hoodie", Container = "wear" }, new KitEntry { Item = "pants", Container = "wear" },
            };

            [JsonProperty("Bedwars: кровать — здоровье")] public float BedHealth = 600f;
            [JsonProperty("Bedwars: генератор острова раз в (сек)")] public float GenIsland = 2f;
            [JsonProperty("Bedwars: генератор острова даёт")] public KitEntry GenIslandItem = new KitEntry { Item = "wood", Amount = 10 };
            [JsonProperty("Bedwars: доп. генератор острова раз в (сек)")] public float GenIsland2 = 8f;
            [JsonProperty("Bedwars: доп. генератор острова даёт")] public KitEntry GenIslandItem2 = new KitEntry { Item = "metal.fragments", Amount = 5 };
            [JsonProperty("Bedwars: каменный генератор острова раз в (сек, 0 = выкл)")] public float GenIslandStone = 4f;
            [JsonProperty("Bedwars: каменный генератор даёт")] public KitEntry GenIslandStoneItem = new KitEntry { Item = "stones", Amount = 10 };
            [JsonProperty("Bedwars: игроков в команде (1–4, 0 = поровну)")] public int BedwarsTeamSize = 0;
            [JsonProperty("Bedwars: центральный генератор раз в (сек)")] public float GenCenter = 20f;
            [JsonProperty("Bedwars: центральный генератор даёт")] public KitEntry GenCenterItem = new KitEntry { Item = "scrap", Amount = 3 };
            [JsonProperty("Bedwars: стартовый набор")] public List<KitEntry> BwKit = new List<KitEntry>
            {
                new KitEntry { Item = "stone.pickaxe", Container = "belt" }, new KitEntry { Item = "building.planner", Container = "belt" },
                new KitEntry { Item = "hammer", Container = "belt" }, new KitEntry { Item = "bandage", Amount = 2, Container = "belt" },
                new KitEntry { Item = "hoodie", Container = "wear" }, new KitEntry { Item = "pants", Container = "wear" },
            };
            [JsonProperty("Bedwars: магазин")] public List<ShopItem> BwShop = new List<ShopItem>
            {
                new ShopItem { Name = "Камень ×1000", Item = "stones", Amount = 1000, Currency = "wood", Price = 100 },
                new ShopItem { Name = "Дерево ×1000", Item = "wood", Amount = 1000, Currency = "metal.fragments", Price = 30 },
                new ShopItem { Name = "Деревянная баррикада", Item = "barricade.wood", Currency = "wood", Price = 60 },
                new ShopItem { Name = "Лестница", Item = "ladder.wooden.wall", Currency = "wood", Price = 40 },
                new ShopItem { Name = "Деревянный сет", Item = "wood.armor.jacket", Currency = "wood", Price = 120 },
                new ShopItem { Name = "Роадсайн", Item = "roadsign.jacket", Currency = "metal.fragments", Price = 40 },
                new ShopItem { Name = "МВК-маска", Item = "metal.facemask", Currency = "scrap", Price = 6 },
                new ShopItem { Name = "Арбалет + 20 стрел", Item = "crossbow", Currency = "wood", Price = 150 },
                new ShopItem { Name = "Тесак", Item = "salvaged.cleaver", Currency = "metal.fragments", Price = 30 },
                new ShopItem { Name = "Томпсон", Item = "smg.thompson", Currency = "metal.fragments", Price = 80 },
                new ShopItem { Name = "Патроны пист. ×64", Item = "ammo.pistol", Amount = 64, Currency = "metal.fragments", Price = 15 },
                new ShopItem { Name = "AK + голо", Item = "rifle.ak", Currency = "scrap", Price = 12, Mods = new List<string> { "weapon.mod.holosight" } },
                new ShopItem { Name = "Патроны 5.56 ×64", Item = "ammo.rifle", Amount = 64, Currency = "metal.fragments", Price = 25 },
                new ShopItem { Name = "Шприц ×2", Item = "syringe.medical", Amount = 2, Currency = "metal.fragments", Price = 20 },
                new ShopItem { Name = "Сачель", Item = "explosive.satchel", Amount = 1, Currency = "scrap", Price = 4 },
                new ShopItem { Name = "C4", Item = "explosive.timed", Currency = "scrap", Price = 10 },
            };
            [JsonProperty("Bedwars: монеты победителям")] public int BwWinCoins = 40;

            [JsonProperty("Рейд: фаза стройки (мин)")] public int RwBuildMinutes = 7;
            [JsonProperty("Рейд: фаза рейда (мин)")] public int RwRaidMinutes = 15;
            [JsonProperty("Рейд: ресурсы каждому на стройку")] public List<KitEntry> RwBuildKit = new List<KitEntry>
            {
                new KitEntry { Item = "building.planner", Container = "belt" }, new KitEntry { Item = "hammer", Container = "belt" },
                new KitEntry { Item = "wood", Amount = 10000 }, new KitEntry { Item = "stones", Amount = 10000 },
                new KitEntry { Item = "metal.fragments", Amount = 4000 }, new KitEntry { Item = "metal.refined", Amount = 100 },
                new KitEntry { Item = "door.hinged.metal", Amount = 3 }, new KitEntry { Item = "door.double.hinged.metal", Amount = 1 },
                new KitEntry { Item = "lock.code", Amount = 4 }, new KitEntry { Item = "wall.frame.garagedoor", Amount = 1 },
            };
            [JsonProperty("Рейд: набор на рейд")] public List<KitEntry> RwRaidKit = new List<KitEntry>
            {
                new KitEntry { Item = "rifle.ak", Container = "belt", Mods = new List<string> { "weapon.mod.holosight" } },
                new KitEntry { Item = "ammo.rifle", Amount = 256 }, new KitEntry { Item = "explosive.timed", Amount = 4 },
                new KitEntry { Item = "rocket.launcher", Container = "belt" }, new KitEntry { Item = "ammo.rocket.basic", Amount = 6 },
                new KitEntry { Item = "syringe.medical", Amount = 3, Container = "belt" },
                new KitEntry { Item = "metal.facemask", Container = "wear" }, new KitEntry { Item = "metal.plate.torso", Container = "wear" },
                new KitEntry { Item = "roadsign.kilt", Container = "wear" }, new KitEntry { Item = "hoodie", Container = "wear" },
                new KitEntry { Item = "pants", Container = "wear" }, new KitEntry { Item = "shoes.boots", Container = "wear" },
            };
            [JsonProperty("Рейд: монеты победителям")] public int RwWinCoins = 50;

            [JsonProperty("Режимы Hub: роум / хели / bedwars / рейд")] public string[] HubModes = { "roam", "heli", "bedwars", "raidwars" };
            [JsonProperty("Скруглённые углы интерфейса")] public bool RoundedCorners = true;
        }

        private static KitEntry W(string i) => new KitEntry { Item = i, Container = "wear" };
        private static KitEntry B(string i, int a = 1, params string[] m) => new KitEntry { Item = i, Amount = a, Container = "belt", Mods = m.ToList() };
        private static KitEntry Mn(string i, int a) => new KitEntry { Item = i, Amount = a };

        private static List<Kit> DefaultRoamKits() => new List<Kit>
        {
            new Kit { Id = "assault", Name = "Штурм", Desc = "AK · роадсайн · 3 шприца", Items = new List<KitEntry> { B("rifle.ak", 1, "weapon.mod.holosight"), Mn("ammo.rifle", 200), B("syringe.medical", 3), W("coffeecan.helmet"), W("roadsign.jacket"), W("roadsign.kilt"), W("hoodie"), W("pants"), W("shoes.boots") } },
            new Kit { Id = "sniper", Name = "Снайпер", Desc = "болт 8x · кевлар · питон", Items = new List<KitEntry> { B("rifle.bolt", 1, "weapon.mod.small.scope"), B("pistol.python"), Mn("ammo.rifle", 60), Mn("ammo.pistol", 60), B("syringe.medical", 2), W("metal.facemask"), W("heavy.plate.jacket"), W("heavy.plate.pants"), W("shoes.boots") } },
            new Kit { Id = "cqb", Name = "Ближний", Desc = "томпсон · МВК · 4 шприца", Items = new List<KitEntry> { B("smg.thompson", 1, "weapon.mod.holosight"), Mn("ammo.pistol", 200), B("syringe.medical", 4), W("metal.facemask"), W("metal.plate.torso"), W("roadsign.kilt"), W("hoodie"), W("pants"), W("shoes.boots") } },
            new Kit { Id = "medic", Name = "Медик", Desc = "MP5 · 10 шприцов · бинты", Items = new List<KitEntry> { B("smg.mp5"), Mn("ammo.pistol", 200), B("syringe.medical", 10), B("bandage", 5), W("wood.armor.helmet"), W("wood.armor.jacket"), W("wood.armor.pants"), W("hoodie"), W("shoes.boots") } },
        };

        private Configuration _config;

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { Config.Settings.ObjectCreationHandling = ObjectCreationHandling.Replace; _config = Config.ReadObject<Configuration>(); if (_config == null) throw new Exception(); }
            catch { PrintWarning("Конфиг повреждён — создаю новый"); LoadDefaultConfig(); }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        #endregion

        #region Данные: карты

        private class Pt
        {
            public float X, Y, Z, Yaw;
            [JsonIgnore] public Vector3 V => new Vector3(X, Y, Z);
            public static Pt From(Vector3 v, float yaw) => new Pt { X = v.x, Y = v.y, Z = v.z, Yaw = yaw };
        }

        private class GameMap
        {
            public string Id, Name, Type;
            public Pt Center;
            public float Radius = 150f;
            public bool Enabled = true;
            public List<List<Pt>> Spawns = new List<List<Pt>> { new List<Pt>(), new List<Pt>(), new List<Pt>(), new List<Pt>() };   // по командам; для роума — команда 0
            public List<Pt> Beds = new List<Pt>();       // bedwars: по команде
            public List<Pt> Gens = new List<Pt>();       // bedwars: генератор острова по команде
            public List<Pt> CenterGens = new List<Pt>(); // bedwars: центр
            public List<Pt> Pads = new List<Pt>();       // хели: площадки (чётные — красные, нечётные — синие); роум: точки транспорта
            public List<Pt> Zones = new List<Pt>();      // рейд: центр зоны команды
            public float ZoneRadius = 35f;
            public int Teams = 2;
        }

        private class StoredData
        {
            public Dictionary<string, GameMap> Maps = new Dictionary<string, GameMap>();
            public Dictionary<ulong, Dictionary<string, int>> Wins = new Dictionary<ulong, Dictionary<string, int>>();
        }

        private StoredData _data;

        private void LoadData()
        {
            try { Interface.Oxide.DataFileSystem.GetFile("Minigames").Settings.ObjectCreationHandling = ObjectCreationHandling.Replace; _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("Minigames"); } catch { _data = null; }
            if (_data == null) _data = new StoredData();
            if (_data.Maps == null) _data.Maps = new Dictionary<string, GameMap>();
            if (_data.Wins == null) _data.Wins = new Dictionary<ulong, Dictionary<string, int>>();
            foreach (var m in _data.Maps.Values) while (m.Spawns.Count < MaxTeams) m.Spawns.Add(new List<Pt>());
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("Minigames", _data);

        #endregion

        #region Игры

        private class Game
        {
            public string Id, Type;
            public GameMap Map;
            public string State = "queue";    // queue | countdown | build | live | ended   (роум — всегда live)
            public List<ulong> Players = new List<ulong>();
            public Dictionary<ulong, int> Team = new Dictionary<ulong, int>();
            public int[] Score = new int[MaxTeams];
            public Dictionary<ulong, int> Kills = new Dictionary<ulong, int>();
            public float StateUntil;
            public List<BaseEntity> Spawned = new List<BaseEntity>();   // хели, кровати, транспорт, шкафы
            public HashSet<BaseNetworkable> Built = new HashSet<BaseNetworkable>();
            public BaseEntity[] Beds = new BaseEntity[MaxTeams];
            public bool[] BedAlive = { true, true, true, true, true, true, true, true };
            public float NextGenS;
            public HashSet<int> Eliminated = new HashSet<int>();
            public Dictionary<int, BaseEntity> Helis = new Dictionary<int, BaseEntity>();      // площадка → хели
            public Dictionary<int, float> HeliRespawnAt = new Dictionary<int, float>();
            public BaseEntity[] Cupboards = new BaseEntity[2];
            public Dictionary<ulong, BaseEntity> RoamVehicle = new Dictionary<ulong, BaseEntity>();
            public Dictionary<BaseEntity, float> VehicleIdle = new Dictionary<BaseEntity, float>();
            public float NextGen, NextGen2, NextGenC;
            public int[] StartBlocks = new int[2];
        }

        private readonly Dictionary<string, Game> _games = new Dictionary<string, Game>();
        private readonly Dictionary<ulong, Game> _in = new Dictionary<ulong, Game>();
        private readonly Dictionary<ulong, string> _roamKit = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> _roamVeh = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> _pendingJoin = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, Timer> _respawn = new Dictionary<ulong, Timer>();

        private static ulong Id(BasePlayer p) => (ulong)p.userID;

        private bool IsAdmin(BasePlayer p) => p != null && (p.IsAdmin || permission.UserHasPermission(p.UserIDString, PermAdmin));

        private Game GameOf(BasePlayer p) { Game g; return p != null && _in.TryGetValue(Id(p), out g) ? g : null; }

        private string HubModeOf(string type) { int i = Array.IndexOf(Types, type); return i >= 0 && i < _config.HubModes.Length ? _config.HubModes[i] : null; }

        private int TypeIdx(string type) => type == "heli" ? 0 : type == "bedwars" ? 1 : 2;

        private bool IsEn(ulong id) => lang.GetLanguage(id.ToString()) != "ru";

        private string TeamName(int t, ulong viewer) => IsEn(viewer) ? TeamNamesEn[t] : TeamNames[t];

        private static object Call(object target, string method, params object[] args)
        {
            if (target == null) return null;
            foreach (var m in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != method || m.GetParameters().Length != args.Length) continue;
                try { return m.Invoke(target, args) ?? true; } catch { return null; }
            }
            return null;
        }

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
            timer.Every(1f, Tick);
            timer.Every(1f, HmTick);
        }

        private void Unload()
        {
            foreach (var g in _games.Values.ToList()) EndGame(g, -1, false);
            foreach (var hm in _hms.ToList()) HmEnd(hm, -1, true);
            foreach (var p in BasePlayer.activePlayerList)
                foreach (var ui in new[] { UiMain, UiHud, UiCenter, UiToast }) { CuiHelper.DestroyUi(p, ui); CuiHelper.DestroyUi(p, ui + ".dim"); }
            SaveData();
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason) { if (player != null) { HmLeave(player, false); Leave(player, false); } }

        private void OnHubModeChanged(BasePlayer player, string from, string to)
        {
            if (player == null) return;
            var hmm = HmOf(player);
            if (hmm != null && to != HubModeOf("heli") && hmm.State != "ended") HmLeave(player, false);
            var g = GameOf(player);
            if (g != null && HubModeOf(g.Type) != to) Leave(player, false);
            string pending;
            if (_pendingJoin.TryGetValue(Id(player), out pending))
            {
                _pendingJoin.Remove(Id(player));
                timer.Once(0.5f, () => { if (player != null && player.IsConnected) Join(player, pending); });
            }
        }

        #endregion

        #region Вход и выход

        private GameMap PickMap(string type)
        {
            var busy = new HashSet<string>(_games.Values.Where(g => g.Type != "roam").Select(g => g.Map.Id));
            return _data.Maps.Values.Where(m => m.Enabled && m.Type == type && !busy.Contains(m.Id) && m.Spawns[0].Count > 0)
                .OrderBy(m => UnityEngine.Random.Range(0, 1000)).FirstOrDefault();
        }

        // для роума — одна постоянная игра на карту; для остальных — очередь на свободную карту
        private Game GameFor(string type)
        {
            if (type == "roam")
            {
                var map = _data.Maps.Values.FirstOrDefault(m => m.Enabled && m.Type == "roam" && m.Spawns[0].Count > 0);
                if (map == null) return null;
                Game g;
                if (!_games.TryGetValue("roam_" + map.Id, out g)) { g = new Game { Id = "roam_" + map.Id, Type = "roam", Map = map, State = "live" }; _games[g.Id] = g; }
                return g;
            }
            var queued = _games.Values.FirstOrDefault(g => g.Type == type && (g.State == "queue" || g.State == "countdown") && g.Players.Count < _config.MaxPlayers[TypeIdx(type)]);
            if (queued != null) return queued;
            var m2 = PickMap(type);
            if (m2 == null) return null;
            var ng = new Game { Id = type + "_" + m2.Id, Type = type, Map = m2 };
            _games[ng.Id] = ng;
            return ng;
        }

        private void Join(BasePlayer player, string type)
        {
            var id = Id(player);
            if (player.IsDead()) return;
            var cur = GameOf(player);
            if (cur != null && cur.Type == type) { OpenMenu(player, type); return; }
            var hubMode = HubModeOf(type);
            if (Hub != null && !string.IsNullOrEmpty(hubMode) && Hub.Call("GetMode", player) as string != hubMode)
            {
                _pendingJoin[id] = type;
                Hub.Call("SendToMode", player, hubMode, false);
                return;
            }
            var g = GameFor(type);
            if (g == null) { Toast(player, T(id, "NoMap"), true); return; }
            if (cur != null) Leave(player, false);
            if (g.Type != "roam" && g.State != "queue" && g.State != "countdown") { Toast(player, T(id, "Running"), true); return; }

            g.Players.Add(id);
            _in[id] = g;
            g.Kills[id] = 0;
            CloseAll(player);
            if (g.Type == "roam")
            {
                g.Team[id] = 0;
                SpawnRoam(player, g);
                Toast(player, T(id, "RoamJoined"), false);
                return;
            }
            Broadcast(g, T(0, "QueueJoined", player.displayName, g.Players.Count, _config.MaxPlayers[TypeIdx(g.Type)]), false);
            TryStartCountdown(g);
            DrawHudAll(g);
        }

        private void Leave(BasePlayer player, bool toHall)
        {
            var id = Id(player);
            _pendingJoin.Remove(id);
            Timer t;
            if (_respawn.TryGetValue(id, out t)) { t?.Destroy(); _respawn.Remove(id); }
            var g = GameOf(player);
            if (g == null) return;
            _in.Remove(id);
            g.Players.Remove(id);
            CuiHelper.DestroyUi(player, UiHud);
            _hudJson.Remove(Id(player));
            BaseEntity veh;
            if (g.RoamVehicle.TryGetValue(id, out veh)) { g.RoamVehicle.Remove(id); if (veh != null && !veh.IsDestroyed) veh.Kill(); }
            if (toHall && player.IsConnected && !player.IsDead())
            {
                player.inventory.Strip();
                var mode = HubModeOf(g.Type);
                if (Hub != null && !string.IsNullOrEmpty(mode)) Hub.Call("SendToMode", player, mode, true);
            }
            if (g.Type == "roam") { if (g.Players.Count == 0) { CleanupGame(g); _games.Remove(g.Id); } return; }
            if (g.State == "queue" || g.State == "countdown")
            {
                if (g.Players.Count < _config.MinPlayers[TypeIdx(g.Type)]) g.State = "queue";
                if (g.Players.Count == 0) _games.Remove(g.Id);
                DrawHudAll(g);
                return;
            }
            CheckWin(g);
        }

        private void TryStartCountdown(Game g)
        {
            if (g.State != "queue" || g.Players.Count < _config.MinPlayers[TypeIdx(g.Type)]) return;
            g.State = "countdown";
            g.StateUntil = Time.realtimeSinceStartup + _config.StartCountdown;
            Broadcast(g, T(0, "StartingIn", _config.StartCountdown), false);
        }

        #endregion

        #region Старт и ход матчей

        // ключ пати: игроки одной команды Rust попадают в одну команду мини-игры
        private static ulong PartyKey(ulong id)
        {
            var p = BasePlayer.FindByID(id);
            return p != null && p.currentTeam != 0UL ? p.currentTeam : id;
        }

        private void StartGame(Game g)
        {
            int teams = g.Type == "bedwars" ? Mathf.Clamp(g.Map.Teams, 2, MaxTeams) : 2;
            // команды Rust (пати) держим вместе: сначала группы целиком, потом одиночки — в самую маленькую команду
            int cap = g.Type == "bedwars" && _config.BedwarsTeamSize > 0 ? _config.BedwarsTeamSize : Mathf.CeilToInt(g.Players.Count / (float)teams);
            var groups = g.Players.GroupBy(PartyKey).Select(x => x.ToList()).OrderByDescending(x => x.Count).ThenBy(x => UnityEngine.Random.Range(0, 100000)).ToList();
            var size = new int[MaxTeams];
            foreach (var grp in groups)
                foreach (var id in grp)
                {
                    int best = -1;
                    // своя пати — туда же, если есть место
                    foreach (var mate in grp) { int mt; if (g.Team.TryGetValue(mate, out mt) && size[mt] < cap) { best = mt; break; } }
                    if (best < 0) best = Enumerable.Range(0, teams).OrderBy(t => size[t]).First();
                    g.Team[id] = best; size[best]++;
                }
            for (int t = 0; t < MaxTeams; t++) { g.Score[t] = 0; g.BedAlive[t] = t < teams; }
            g.Eliminated.Clear();
            for (int t = teams; t < MaxTeams; t++) g.Eliminated.Add(t);

            float now = Time.realtimeSinceStartup;
            switch (g.Type)
            {
                case "heli":
                    g.State = "live";
                    g.StateUntil = now + _config.HeliMinutes * 60f;
                    for (int i = 0; i < g.Map.Pads.Count; i++) SpawnHeli(g, i);
                    break;
                case "bedwars":
                    g.State = "live";
                    g.StateUntil = 0f;
                    for (int t = 0; t < teams && t < g.Map.Beds.Count; t++)
                    {
                        var bed = Spawn(g, PrefabBed, g.Map.Beds[t]);
                        if (bed is BaseCombatEntity) { ((BaseCombatEntity)bed).InitializeHealth(_config.BedHealth, _config.BedHealth); }
                        g.Beds[t] = bed;
                    }
                    g.NextGen = now + _config.GenIsland; g.NextGen2 = now + _config.GenIsland2; g.NextGenC = now + _config.GenCenter; g.NextGenS = now + _config.GenIslandStone;
                    break;
                case "raidwars":
                    g.State = "build";
                    g.StateUntil = now + _config.RwBuildMinutes * 60f;
                    break;
            }
            foreach (var id in g.Players.ToList())
            {
                var p = BasePlayer.FindByID(id);
                if (p == null) continue;
                Spawn(p, g, true);
                Center(p, $"{T(id, "Type_" + g.Type)}\n<size=18><color={TeamHex[g.Team[id]]}>{TeamName(g.Team[id], id)}</color></size>", ColText, 3f);
            }
            DrawHudAll(g);
        }

        private BaseEntity Spawn(Game g, string prefab, Pt pt)
        {
            var e = GameManager.server.CreateEntity(prefab, pt.V, Quaternion.Euler(0f, pt.Yaw, 0f));
            if (e == null) return null;
            e.enableSaving = false;
            e.Spawn();
            g.Spawned.Add(e);
            return e;
        }

        private Pt SpawnPoint(Game g, ulong id)
        {
            int team = g.Team.ContainsKey(id) ? g.Team[id] : 0;
            var list = g.Map.Spawns[Mathf.Clamp(team, 0, g.Map.Spawns.Count - 1)];
            if (list.Count == 0) list = g.Map.Spawns[0];
            if (list.Count == 0) return null;
            if (g.Type != "roam") return list[UnityEngine.Random.Range(0, list.Count)];
            // роум — подальше от людей
            var others = g.Players.Where(x => x != id).Select(BasePlayer.FindByID).Where(p => p != null && !p.IsDead()).Select(p => p.transform.position).ToList();
            if (others.Count == 0) return list[UnityEngine.Random.Range(0, list.Count)];
            return list.OrderByDescending(pt => others.Min(o => Vector3.Distance(o, pt.V))).First();
        }

        private void Spawn(BasePlayer p, Game g, bool first)
        {
            var id = Id(p);
            var pt = SpawnPoint(g, id);
            if (pt != null) p.Teleport(pt.V + Vector3.up * 0.2f);
            Heal(p);
            p.inventory.Strip();
            switch (g.Type)
            {
                case "heli": GiveKit(p, _config.HeliKit); break;
                case "bedwars": GiveKit(p, _config.BwKit); break;
                case "raidwars":
                    if (g.State == "build") GiveKit(p, _config.RwBuildKit);
                    else GiveKit(p, _config.RwRaidKit);
                    break;
            }
        }

        private void SpawnRoam(BasePlayer p, Game g)
        {
            var id = Id(p);
            var pt = SpawnPoint(g, id);
            if (pt != null) p.Teleport(pt.V + Vector3.up * 0.2f);
            Heal(p);
            p.inventory.Strip();
            string kitId; if (!_roamKit.TryGetValue(id, out kitId)) kitId = _config.RoamKits.FirstOrDefault()?.Id;
            var kit = _config.RoamKits.FirstOrDefault(k => k.Id == kitId) ?? _config.RoamKits.FirstOrDefault();
            if (kit != null) GiveKit(p, kit.Items);
            string veh; if (!_roamVeh.TryGetValue(id, out veh)) veh = "none";
            SpawnRoamVehicle(p, g, veh);
            DrawHud(p, g);
        }

        private void SpawnRoamVehicle(BasePlayer p, Game g, string veh)
        {
            var id = Id(p);
            BaseEntity old;
            if (g.RoamVehicle.TryGetValue(id, out old) && old != null && !old.IsDestroyed) old.Kill();
            g.RoamVehicle.Remove(id);
            string prefab = veh == "car" ? PrefabCar : veh == "horse" ? PrefabHorse : veh == "mini" ? PrefabMini : null;
            if (prefab == null) return;
            var pos = p.transform.position + p.eyes.HeadForward() * 4f;
            var e = GameManager.server.CreateEntity(prefab, new Vector3(pos.x, p.transform.position.y + 1f, pos.z), Quaternion.Euler(0f, p.eyes.rotation.eulerAngles.y, 0f));
            if (e == null) return;
            e.enableSaving = false;
            e.OwnerID = id;
            e.Spawn();
            AddFuel(e, 100);
            g.RoamVehicle[id] = e;
            g.VehicleIdle[e] = Time.realtimeSinceStartup;
        }

        private void SpawnHeli(Game g, int pad)
        {
            if (pad >= g.Map.Pads.Count) return;
            var e = Spawn(g, PrefabAttackHeli, g.Map.Pads[pad]);
            if (e == null) { PrintWarning("Хели не создан — проверь префаб атак-хели в этой версии Rust"); return; }
            AddFuel(e, _config.HeliFuel);
            // боезапас: кладём во все контейнеры хели, что поместится
            NextTick(() =>
            {
                if (e == null || e.IsDestroyed) return;
                foreach (var child in e.children.ToList())
                {
                    var sc = child as StorageContainer;
                    if (sc == null || sc.inventory == null) continue;
                    foreach (var a in _config.HeliAmmo)
                    {
                        var it = ItemManager.CreateByName(a.Item, a.Amount, 0UL);
                        if (it != null && !it.MoveToContainer(sc.inventory)) it.Remove();
                    }
                }
            });
            g.Helis[pad] = e;
        }

        private void AddFuel(BaseEntity vehicle, int amount)
        {
            if (vehicle == null || amount <= 0) return;
            var fs = Call(vehicle, "GetFuelSystem");
            if (fs != null && Call(fs, "AddStartingFuel", (float)amount) != null) return;
            if (fs != null && Call(fs, "AddStartingFuel", amount) != null) return;
            // запасной путь: контейнер топлива среди детей
            NextTick(() =>
            {
                if (vehicle == null || vehicle.IsDestroyed) return;
                foreach (var child in vehicle.children.ToList())
                {
                    var sc = child as StorageContainer;
                    if (sc?.inventory == null) continue;
                    var fuel = ItemManager.CreateByName("lowgradefuel", amount, 0UL);
                    if (fuel != null && !fuel.MoveToContainer(sc.inventory)) fuel.Remove();
                    else break;
                }
            });
        }

        private void GiveKit(BasePlayer p, List<KitEntry> kit)
        {
            foreach (var k in kit)
            {
                var item = ItemManager.CreateByName(k.Item, Mathf.Max(1, k.Amount), 0UL);
                if (item == null) continue;
                if (k.Mods != null && item.contents != null)
                    foreach (var m in k.Mods) { var mod = ItemManager.CreateByName(m, 1, 0UL); if (mod != null && !mod.MoveToContainer(item.contents)) mod.Remove(); }
                var proj = item.GetHeldEntity() as BaseProjectile;
                if (proj?.primaryMagazine != null) proj.primaryMagazine.contents = proj.primaryMagazine.capacity;
                var c = k.Container == "wear" ? p.inventory.containerWear : k.Container == "belt" ? p.inventory.containerBelt : p.inventory.containerMain;
                if (!item.MoveToContainer(c) && !item.MoveToContainer(p.inventory.containerMain)) item.Remove();
            }
        }

        private static void Heal(BasePlayer p)
        {
            try
            {
                p.health = p.MaxHealth();
                var m = p.metabolism;
                if (m != null) { m.bleeding.value = 0f; m.calories.value = m.calories.max; m.hydration.value = m.hydration.max; m.SendChanges(); }
                p.SendNetworkUpdate();
            }
            catch { }
        }

        private void Tick()
        {
            float now = Time.realtimeSinceStartup;
            foreach (var g in _games.Values.ToList())
            {
                switch (g.State)
                {
                    case "countdown":
                        if (g.Players.Count < _config.MinPlayers[TypeIdx(g.Type)]) { g.State = "queue"; break; }
                        if (now >= g.StateUntil) StartGame(g);
                        break;
                    case "build":
                        if (now >= g.StateUntil) StartRaidPhase(g);
                        break;
                    case "live":
                        if (g.Type == "heli")
                        {
                            if (now >= g.StateUntil) { EndGame(g, g.Score[0] == g.Score[1] ? -1 : (g.Score[0] > g.Score[1] ? 0 : 1), false); continue; }
                            foreach (var kv in g.HeliRespawnAt.ToList()) if (now >= kv.Value) { g.HeliRespawnAt.Remove(kv.Key); SpawnHeli(g, kv.Key); }
                            foreach (var kv in g.Helis.ToList())
                            {
                                if (kv.Value == null || kv.Value.IsDestroyed) { g.Helis.Remove(kv.Key); g.HeliRespawnAt[kv.Key] = now + _config.HeliRespawn; continue; }
                                CheckHeliBounds(g, kv.Value);
                            }
                        }
                        else if (g.Type == "bedwars") Generators(g, now);
                        else if (g.Type == "raidwars" && now >= g.StateUntil) RaidTimeout(g);
                        else if (g.Type == "roam") RoamVehicles(g, now);
                        break;
                }
                // граница зоны для игроков
                if (g.Map.Center != null && g.Map.Radius > 0 && (g.State == "live" || g.State == "build"))
                    foreach (var id in g.Players.ToList())
                    {
                        var p = BasePlayer.FindByID(id);
                        if (p == null || p.IsDead() || p.isMounted) continue;
                        if (Vector3.Distance(p.transform.position, g.Map.Center.V) > g.Map.Radius) { var pt = SpawnPoint(g, id); if (pt != null) { p.Teleport(pt.V + Vector3.up * 0.2f); Toast(p, T(id, "Border"), true); } }
                        if (g.State == "build" && g.Map.Zones.Count > 1)
                        {
                            var z = g.Map.Zones[g.Team[id] % g.Map.Zones.Count];
                            if (Vector3.Distance(p.transform.position, z.V) > g.Map.ZoneRadius + 5f) { var pt = SpawnPoint(g, id); if (pt != null) { p.Teleport(pt.V + Vector3.up * 0.2f); Toast(p, T(id, "OwnZone"), true); } }
                        }
                    }
                if (g.State != "queue") DrawHudAll(g);
            }
        }

        private void CheckHeliBounds(Game g, BaseEntity heli)
        {
            var pos = heli.transform.position;
            float ground = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(pos) : 0f;
            bool outR = g.Map.Center != null && Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(g.Map.Center.X, 0f, g.Map.Center.Z)) > g.Map.Radius;
            bool outH = pos.y - ground > _config.HeliCeiling;
            if (!outR && !outH) return;
            var ce = heli as BaseCombatEntity;
            if (ce != null) ce.Hurt(ce.MaxHealth() * 0.08f, Rust.DamageType.Generic, null, false);
            foreach (var id in g.Players)
            {
                var p = BasePlayer.FindByID(id);
                if (p != null && p.isMounted && p.GetMounted() != null && Vector3.Distance(p.transform.position, pos) < 8f)
                    Toast(p, outH ? T(id, "Ceiling") : T(id, "HeliBorder"), true);
            }
        }

        private void Generators(Game g, float now)
        {
            int teams = Mathf.Clamp(g.Map.Teams, 2, MaxTeams);
            if (now >= g.NextGen)
            {
                g.NextGen = now + _config.GenIsland;
                for (int t = 0; t < teams && t < g.Map.Gens.Count; t++) if (!g.Eliminated.Contains(t)) Drop(g.Map.Gens[t], _config.GenIslandItem);
            }
            if (_config.GenIslandStone > 0f && now >= g.NextGenS)
            {
                g.NextGenS = now + _config.GenIslandStone;
                for (int t = 0; t < teams && t < g.Map.Gens.Count; t++) if (!g.Eliminated.Contains(t)) Drop(g.Map.Gens[t], _config.GenIslandStoneItem);
            }
            if (now >= g.NextGen2)
            {
                g.NextGen2 = now + _config.GenIsland2;
                for (int t = 0; t < teams && t < g.Map.Gens.Count; t++) if (!g.Eliminated.Contains(t)) Drop(g.Map.Gens[t], _config.GenIslandItem2);
            }
            if (now >= g.NextGenC)
            {
                g.NextGenC = now + _config.GenCenter;
                foreach (var c in g.Map.CenterGens) Drop(c, _config.GenCenterItem);
            }
        }

        private void Drop(Pt pt, KitEntry e)
        {
            var item = ItemManager.CreateByName(e.Item, e.Amount, 0UL);
            if (item == null) return;
            var dropped = item.Drop(pt.V + Vector3.up * 1.2f, Vector3.zero);
            if (dropped == null) item.Remove();
        }

        private void RoamVehicles(Game g, float now)
        {
            foreach (var kv in g.VehicleIdle.ToList())
            {
                var v = kv.Key;
                if (v == null || v.IsDestroyed) { g.VehicleIdle.Remove(v); continue; }
                bool driven = BasePlayer.activePlayerList.Any(p => p.isMounted && Vector3.Distance(p.transform.position, v.transform.position) < 4f);
                if (driven) g.VehicleIdle[v] = now;
                else if (now - kv.Value > _config.RoamVehicleTtl) { g.VehicleIdle.Remove(v); v.Kill(); }
            }
        }

        private void StartRaidPhase(Game g)
        {
            g.State = "live";
            g.StateUntil = Time.realtimeSinceStartup + _config.RwRaidMinutes * 60f;
            // шкаф команды = цель. Если команда не поставила свой шкаф — ставим в центр её зоны
            for (int t = 0; t < 2; t++)
            {
                var zone = t < g.Map.Zones.Count ? g.Map.Zones[t] : null;
                BaseEntity tc = null;
                if (zone != null)
                    tc = g.Built.OfType<BaseEntity>().FirstOrDefault(e => e != null && !e.IsDestroyed && e is BuildingPrivlidge && Vector3.Distance(e.transform.position, zone.V) <= g.Map.ZoneRadius);
                if (tc == null && zone != null) tc = Spawn(g, PrefabCupboard, zone);
                g.Cupboards[t] = tc;
                g.StartBlocks[t] = CountBlocks(g, t);
            }
            foreach (var id in g.Players.ToList())
            {
                var p = BasePlayer.FindByID(id);
                if (p == null || p.IsDead()) continue;
                p.inventory.Strip();
                GiveKit(p, _config.RwRaidKit);
                Center(p, T(id, "RaidPhase"), ColRed, 3f);
            }
        }

        private int CountBlocks(Game g, int team)
        {
            if (team >= g.Map.Zones.Count) return 0;
            var z = g.Map.Zones[team];
            return g.Built.OfType<BaseEntity>().Count(e => e != null && !e.IsDestroyed && e is BuildingBlock && Vector3.Distance(e.transform.position, z.V) <= g.Map.ZoneRadius);
        }

        private void RaidTimeout(Game g)
        {
            // у кого уцелела большая доля базы — тот победил
            float a = g.StartBlocks[0] > 0 ? CountBlocks(g, 0) / (float)g.StartBlocks[0] : 0f;
            float b = g.StartBlocks[1] > 0 ? CountBlocks(g, 1) / (float)g.StartBlocks[1] : 0f;
            EndGame(g, Mathf.Approximately(a, b) ? -1 : (a > b ? 0 : 1), false);
        }

        private void CheckWin(Game g)
        {
            if (g.State != "live" && g.State != "build") return;
            var alive = g.Players.Select(id => g.Team[id]).Distinct().ToList();
            if (g.Type == "bedwars")
            {
                var teamsLeft = Enumerable.Range(0, Mathf.Clamp(g.Map.Teams, 2, MaxTeams)).Where(t => !g.Eliminated.Contains(t) && (g.BedAlive[t] || g.Players.Any(id => g.Team[id] == t && !IsOut(g, id)))).ToList();
                if (teamsLeft.Count <= 1) EndGame(g, teamsLeft.Count == 1 ? teamsLeft[0] : -1, false);
                return;
            }
            if (alive.Count <= 1) EndGame(g, alive.Count == 1 ? alive[0] : -1, true);
        }

        private readonly HashSet<ulong> _out = new HashSet<ulong>();
        private bool IsOut(Game g, ulong id) => _out.Contains(id);

        private void EndGame(Game g, int winner, bool forfeit)
        {
            if (g.State == "ended") return;
            g.State = "ended";
            int coins = g.Type == "heli" ? _config.HeliWinCoins : g.Type == "bedwars" ? _config.BwWinCoins : _config.RwWinCoins;
            foreach (var id in g.Players.ToList())
            {
                var p = BasePlayer.FindByID(id);
                bool won = winner >= 0 && g.Team.ContainsKey(id) && g.Team[id] == winner;
                Hub?.Call("ModeEvent", id, g.Type, won);
                if (won)
                {
                    Dictionary<string, int> w;
                    if (!_data.Wins.TryGetValue(id, out w)) _data.Wins[id] = w = new Dictionary<string, int>();
                    int v; w.TryGetValue(g.Type, out v); w[g.Type] = v + 1;
                    if (g.Type == "bedwars" || g.Type == "raidwars") Ach(id, g.Type + "_wins");
                    if (Hub != null && coins > 0) Hub.Call("AddCoins", id, coins, T(id, "ForWin"));
                }
                if (p != null)
                    Center(p, (winner < 0 ? T(id, "Draw") : won ? T(id, "Victory") : T(id, "Defeat")) +
                        (winner >= 0 ? $"\n<size=16><color={TeamHex[winner]}>{TeamName(winner, id)}</color>{(forfeit ? " · " + T(id, "Forfeit") : "")}</size>" : ""), winner < 0 ? ColDim : won ? ColGreen : ColRed, 5f);
            }
            SaveData();
            var players = g.Players.ToList();
            timer.Once(5f, () =>
            {
                foreach (var id in players)
                {
                    _out.Remove(id);
                    var p = BasePlayer.FindByID(id);
                    if (p != null && _in.ContainsKey(id) && _in[id] == g) Leave(p, true);
                    else _in.Remove(id);
                }
                CleanupGame(g);
                _games.Remove(g.Id);
            });
        }

        private void CleanupGame(Game g)
        {
            foreach (var e in g.Spawned) if (e != null && !e.IsDestroyed) e.Kill();
            foreach (var e in g.Built) if (e != null && !e.IsDestroyed) e.Kill();
            foreach (var v in g.RoamVehicle.Values) if (v != null && !v.IsDestroyed) v.Kill();
            g.Spawned.Clear(); g.Built.Clear(); g.RoamVehicle.Clear(); g.VehicleIdle.Clear();
            // выпавшие предметы в зоне
            if (g.Map.Center != null && g.Type != "roam")
                foreach (var d in BaseNetworkable.serverEntities.OfType<DroppedItem>().ToList())
                    if (d != null && !d.IsDestroyed && Vector3.Distance(d.transform.position, g.Map.Center.V) <= g.Map.Radius) d.Kill();
        }

        #endregion

        #region Урон, смерть, постройки

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return null;
            var attacker = info.InitiatorPlayer;

            // кровать bedwars: свою не ломаем
            foreach (var g in _games.Values)
            {
                if (g.Type != "bedwars") continue;
                int bed = Array.IndexOf(g.Beds, entity as BaseEntity);
                if (bed < 0) continue;
                if (attacker == null || !g.Team.ContainsKey(Id(attacker)) || g.Team[Id(attacker)] == bed || g.State != "live") return true;
                return null;
            }

            var hmr = HmDamage(entity, attacker);
            if (hmr != null) return hmr;

            var victim = entity as BasePlayer;
            if (victim == null)
            {
                // фаза стройки рейда — постройки не ломаются
                foreach (var g in _games.Values) if (g.Type == "raidwars" && g.State == "build" && g.Built.Contains(entity)) return true;
                return null;
            }
            var vg = GameOf(victim);
            if (vg == null) return attacker != null && GameOf(attacker) != null && attacker != victim ? (object)true : null;
            if (vg.State == "queue" || vg.State == "countdown" || vg.State == "build" || vg.State == "ended") return true;
            if (_out.Contains(Id(victim))) return true;
            if (attacker == null || attacker == victim) return null;
            var ag = GameOf(attacker);
            if (ag != vg) return true;
            if (vg.Type == "roam")
            {
                // своя команда Rust — без урона
                if (victim.currentTeam != 0UL && victim.currentTeam == attacker.currentTeam) return true;
                return null;
            }
            if (vg.Team.ContainsKey(Id(victim)) && vg.Team.ContainsKey(Id(attacker)) && vg.Team[Id(victim)] == vg.Team[Id(attacker)]) return true;
            return null;
        }

        private object CanBeWounded(BasePlayer player, HitInfo info)
        {
            var g = GameOf(player);
            return g != null && g.Type != "roam" ? (object)false : null;
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null) return;
            HmEntityDeath(entity);
            foreach (var g in _games.Values.ToList())
            {
                if (g.Type == "bedwars")
                {
                    int bed = Array.IndexOf(g.Beds, entity as BaseEntity);
                    if (bed >= 0 && g.BedAlive[bed])
                    {
                        g.BedAlive[bed] = false;
                        var who = info?.InitiatorPlayer;
                        foreach (var id in g.Players) { var p = BasePlayer.FindByID(id); if (p != null) Toast(p, T(id, "BedDestroyed", TeamHex[bed], TeamName(bed, id), who?.displayName ?? "?"), g.Team[id] == bed); }
                        CheckWin(g);
                    }
                }
                else if (g.Type == "raidwars" && g.State == "live")
                {
                    int tc = Array.IndexOf(g.Cupboards, entity as BaseEntity);
                    if (tc >= 0) { EndGame(g, 1 - tc, false); return; }
                }
            }
        }

        private void OnPlayerDeath(BasePlayer victim, HitInfo info)
        {
            if (HmPlayerDeath(victim, info)) return;
            var g = GameOf(victim);
            if (g == null || g.State == "ended") return;
            var vid = Id(victim);
            var killer = info?.InitiatorPlayer;
            if (killer != null && killer != victim && GameOf(killer) == g)
            {
                var kid = Id(killer);
                int k; g.Kills.TryGetValue(kid, out k); g.Kills[kid] = k + 1;
                if ((g.Type == "roam" || g.Type == "heli") && !(Hub != null && Hub.Call("SameIp", kid, vid) is bool && (bool)Hub.Call("SameIp", kid, vid))) Ach(kid, g.Type + "_kills");
                if (g.Team.ContainsKey(kid) && g.Type != "bedwars") g.Score[g.Team[kid]]++;
                if (g.Type == "roam" && Hub != null && _config.RoamKillCoins > 0 && !(Hub.Call("CanEarnFrom", kid, vid) is bool && !(bool)Hub.Call("CanEarnFrom", kid, vid))) Hub.Call("AddCoins", kid, _config.RoamKillCoins, "");
                if (g.Type == "heli" && g.Team.ContainsKey(kid) && g.Score[g.Team[kid]] >= _config.HeliKills) { EndGame(g, g.Team[kid], false); return; }
            }
            if (g.Type == "bedwars")
            {
                int t = g.Team.ContainsKey(vid) ? g.Team[vid] : 0;
                if (!g.BedAlive[t])
                {
                    _out.Add(vid);
                    Toast(victim, T(vid, "Eliminated"), true);
                    if (!g.Players.Any(id => g.Team[id] == t && !_out.Contains(id))) g.Eliminated.Add(t);
                    CheckWin(g);
                    return;
                }
            }
            if (g.Type != "roam") victim.inventory.Strip();
            Timer old;
            if (_respawn.TryGetValue(vid, out old)) old?.Destroy();
            var pl = victim;
            _respawn[vid] = timer.Once(g.Type == "roam" ? 3f : _config.Respawn, () =>
            {
                _respawn.Remove(vid);
                if (pl != null && pl.IsConnected && pl.IsDead() && GameOf(pl) == g) pl.Respawn();
            });
        }

        private object OnHubRespawn(BasePlayer player)
        {
            var hm = HmOf(player);
            if (hm != null) { var hp = HmSpawnPt(hm, Id(player)); return new BasePlayer.SpawnPoint { pos = hp + Vector3.up * 0.2f, rot = Quaternion.identity }; }
            var g = GameOf(player);
            if (g == null || g.State == "queue" || g.State == "countdown") return null;
            if (_out.Contains(Id(player))) return null;
            var pt = SpawnPoint(g, Id(player));
            if (pt == null) return null;
            return new BasePlayer.SpawnPoint { pos = pt.V + Vector3.up * 0.2f, rot = Quaternion.Euler(0f, pt.Yaw, 0f) };
        }

        private object OnHubRespawned(BasePlayer player, string mode)
        {
            var hm = HmOf(player);
            if (hm != null) { Heal(player); player.inventory.Strip(); HmKit(player, hm); DrawHmHud(player, hm); return true; }
            var g = GameOf(player);
            if (g == null || g.State == "queue" || g.State == "countdown") return null;
            if (_out.Contains(Id(player))) { timer.Once(0.5f, () => { if (player != null) Leave(player, true); }); return true; }
            if (g.Type == "roam") { timer.Once(0.2f, () => { if (player != null && player.IsConnected) SpawnRoam(player, g); }); return true; }
            Heal(player);
            player.inventory.Strip();
            switch (g.Type)
            {
                case "heli": GiveKit(player, _config.HeliKit); break;
                case "bedwars": GiveKit(player, _config.BwKit); break;
                case "raidwars": GiveKit(player, g.State == "build" ? _config.RwBuildKit : _config.RwRaidKit); break;
            }
            return true;
        }

        private object OnPlayerRespawn(BasePlayer player, BasePlayer.SpawnPoint sp) => Hub != null ? null : OnHubRespawn(player);

        private void OnPlayerRespawned(BasePlayer player) { if (Hub == null) OnHubRespawned(player, null); }

        private void OnEntityBuilt(Planner planner, GameObject go)
        {
            var player = planner?.GetOwnerPlayer();
            var e = go?.ToBaseEntity();
            var g = GameOf(player);
            if (g == null || e == null) return;
            g.Built.Add(e);
        }

        // строить можно только в своей зоне (рейд) и только во время матча
        private object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            var player = planner?.GetOwnerPlayer();
            var g = GameOf(player);
            if (g == null) return null;
            if (g.Type == "heli" || g.Type == "roam" || g.State == "queue" || g.State == "countdown") return false;
            if (g.Type == "raidwars" && g.Map.Zones.Count > 1)
            {
                var z = g.Map.Zones[g.Team[Id(player)] % g.Map.Zones.Count];
                var pos = target.entity != null ? target.entity.transform.position : target.position;
                if (Vector3.Distance(pos, z.V) > g.Map.ZoneRadius) { Toast(player, T(Id(player), "OwnZone"), true); return false; }
                if (g.State != "build") { Toast(player, T(Id(player), "NoBuildRaid"), true); return false; }
            }
            return null;
        }

        #endregion

        #region Магазин Bedwars

        private void BuyBw(BasePlayer player, int idx)
        {
            var id = Id(player);
            var g = GameOf(player);
            if (g == null || g.Type != "bedwars" || g.State != "live") return;
            if (idx < 0 || idx >= _config.BwShop.Count) return;
            var s = _config.BwShop[idx];
            var def = ItemManager.FindItemDefinition(s.Currency);
            if (def == null) return;
            if (player.inventory.GetAmount(def.itemid) < s.Price) { Toast(player, T(id, "NotEnough", s.Price, CurName(s.Currency, id)), true); return; }
            player.inventory.Take(null, def.itemid, s.Price);
            var item = ItemManager.CreateByName(s.Item, s.Amount, 0UL);
            if (item != null)
            {
                if (s.Mods != null && item.contents != null) foreach (var m in s.Mods) { var mod = ItemManager.CreateByName(m, 1, 0UL); if (mod != null && !mod.MoveToContainer(item.contents)) mod.Remove(); }
                player.GiveItem(item);
            }
            if (s.Item == "crossbow") { var arrows = ItemManager.CreateByName("arrow.wooden", 20, 0UL); if (arrows != null) player.GiveItem(arrows); }
            OpenBwShop(player);
        }

        private string CurName(string sn, ulong id)
        {
            switch (sn) { case "wood": return T(id, "CurWood"); case "metal.fragments": return T(id, "CurMetal"); case "scrap": return T(id, "CurScrap"); default: return sn; }
        }

        #endregion

        #region Команды

        [ChatCommand("mg")]
        private void CmdMg(BasePlayer player, string c, string[] a)
        {
            if (a.Length > 0 && a[0] == "leave") { HmLeave(player, true); Leave(player, true); return; }
            if (a.Length > 0 && (a[0] == "helimm" || a[0] == "hmm")) { OpenHmMenu(player); return; }
            string type = a.Length > 0 && Types.Contains(a[0]) ? a[0] : GameOf(player)?.Type ?? "roam";
            OpenMenu(player, type);
        }

        [ChatCommand("shopbw")]
        private void CmdBwShop(BasePlayer player, string c, string[] a) => OpenBwShop(player);

        [ConsoleCommand("mg.ui")]
        private void CcUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs()) return;
            var id = Id(player);
            string a1 = arg.GetString(1);
            switch (arg.GetString(0))
            {
                case "close": CloseAll(player); break;
                case "type": OpenMenu(player, a1); break;
                case "join": Join(player, a1); break;
                case "leave": CloseAll(player); Leave(player, true); break;
                case "kit": _roamKit[id] = a1; OpenMenu(player, "roam"); break;
                case "veh": _roamVeh[id] = a1; OpenMenu(player, "roam"); break;
                case "roam.spawn":
                {
                    var g = GameOf(player);
                    CloseAll(player);
                    if (g != null && g.Type == "roam") { if (Hub != null && Hub.Call("IsInCombat", player) is bool && (bool)Hub.Call("IsInCombat", player)) { Toast(player, T(id, "InCombat"), true); break; } SpawnRoam(player, g); }
                    else Join(player, "roam");
                    break;
                }
                case "hm": OpenHmMenu(player); break;
                case "hm.fmt": _hmFmt[id] = a1; OpenHmMenu(player); break;
                case "hm.ranked": _hmRanked[id] = !(_hmRanked.ContainsKey(id) && _hmRanked[id]); OpenHmMenu(player); break;
                case "hm.join": { string f; if (!_hmFmt.TryGetValue(id, out f)) f = "a1"; HeliQueue(player, f, _hmRanked.ContainsKey(id) && _hmRanked[id]); break; }
                case "hm.cancel": HmCancel(player); OpenHmMenu(player); break;
                case "bw.buy": BuyBw(player, int.Parse(a1)); break;
                case "bw.shop": OpenBwShop(player); break;
            }
        }

        [ChatCommand("mgadmin")]
        private void CmdAdmin(BasePlayer player, string c, string[] a)
        {
            var id = Id(player);
            if (!IsAdmin(player)) { Reply(player, T(id, "NoPerm")); return; }
            if (a.Length == 0) { Reply(player, T(id, "AdminHelp")); return; }
            var pos = player.transform.position;
            float yaw = player.eyes != null ? player.eyes.rotation.eulerAngles.y : 0f;
            var pt = Pt.From(pos, yaw);
            string key = a.Length > 1 ? a[1].ToLower() : null;
            GameMap m = key != null && _data.Maps.ContainsKey(key) ? _data.Maps[key] : null;
            int n = a.Length > 2 ? Mathf.Clamp(ParseInt(a[2]), 0, MaxTeams - 1) : 0;
            switch (a[0].ToLower())
            {
                case "create":
                    if (key == null || a.Length < 3 || !Types.Contains(a[2])) { Reply(player, "/mgadmin create <id> <roam|heli|bedwars|raidwars> [радиус]"); return; }
                    _data.Maps[key] = new GameMap { Id = key, Name = key, Type = a[2], Center = pt, Radius = a.Length > 3 ? ParseF(a[3], 150f) : (a[2] == "roam" ? 400f : 150f) };
                    break;
                case "spawn": if (m == null) return; while (m.Spawns.Count < MaxTeams) m.Spawns.Add(new List<Pt>()); m.Spawns[n].Add(pt); Reply(player, $"спавн команды {n}: {m.Spawns[n].Count}"); break;
                case "bed": if (m == null) return; while (m.Beds.Count <= n) m.Beds.Add(pt); m.Beds[n] = pt; break;
                case "gen": if (m == null) return; while (m.Gens.Count <= n) m.Gens.Add(pt); m.Gens[n] = pt; break;
                case "centergen": if (m == null) return; m.CenterGens.Add(pt); break;
                case "pad": if (m == null) return; m.Pads.Add(pt); Reply(player, $"площадок: {m.Pads.Count}"); break;
                case "zone": if (m == null) return; while (m.Zones.Count <= n) m.Zones.Add(pt); m.Zones[n] = pt; if (a.Length > 3) m.ZoneRadius = ParseF(a[3], 35f); break;
                case "teams": if (m == null) return; m.Teams = Mathf.Clamp(ParseInt(a[2]), 2, MaxTeams); Reply(player, $"команд: {m.Teams} (2, 4 или 8 — по числу островов)"); break;
                case "radius": if (m == null) return; m.Radius = ParseF(a[2], 150f); m.Center = pt; break;
                case "clear": if (m == null) return; foreach (var l in m.Spawns) l.Clear(); m.Beds.Clear(); m.Gens.Clear(); m.CenterGens.Clear(); m.Pads.Clear(); m.Zones.Clear(); break;
                case "delete": if (key != null) _data.Maps.Remove(key); break;
                case "enable": if (m != null) m.Enabled = true; break;
                case "disable": if (m != null) m.Enabled = false; break;
                case "list":
                    Reply(player, _data.Maps.Count == 0 ? "—" : string.Join("\n", _data.Maps.Values.Select(x => $"{x.Id} [{x.Type}] R{x.Radius} спавны {string.Join("/", x.Spawns.Select(s => s.Count.ToString()).ToArray())} кровати {x.Beds.Count} ген {x.Gens.Count}+{x.CenterGens.Count} площадки {x.Pads.Count} зоны {x.Zones.Count}{(x.Enabled ? "" : " выкл")}").ToArray()));
                    return;
                case "start":
                {
                    var g = _games.Values.FirstOrDefault(x => x.Type != "roam" && (x.State == "queue" || x.State == "countdown") && x.Players.Count > 0);
                    if (g != null) StartGame(g);
                    return;
                }
                case "stop":
                {
                    foreach (var g in _games.Values.Where(x => x.Type != "roam").ToList()) EndGame(g, -1, false);
                    return;
                }
                default: Reply(player, T(id, "AdminHelp")); return;
            }
            SaveData();
            Reply(player, T(id, "AdmOk"));
        }

        private static int ParseInt(string s) { int i; return int.TryParse(s, out i) ? i : 0; }
        private static float ParseF(string s, float d) { float f; return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f) ? f : d; }

        private void Reply(BasePlayer player, string msg) => player.ChatMessage($"<color=#f0a6b1>[Minigames]</color> {msg}");

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

        private static void ItemIcon(CuiElementContainer c, string parent, string shortname, float x, float y, float s)
        {
            var def = ItemManager.FindItemDefinition(shortname);
            if (def == null) return;
            c.Add(new CuiElement { Parent = parent, Components = { new CuiImageComponent { ItemId = def.itemid }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - s}", OffsetMax = $"{x + s} {-y}" } } });
        }

        private const float M = 18f, G = 10f;

        private void Window(CuiElementContainer c, string icon, string title, string sub, float w, float h)
        {
            c.Add(new CuiPanel { Image = { Color = "0.03 0.02 0.02 0.5" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", UiMain + ".dim");
            Root(c, UiMain, UiMain + ".dim", ColBg, "0.5 0.5", "0.5 0.5", $"{-w / 2f} {-h / 2f}", $"{w / 2f} {h / 2f}", 18f, true);
            Box(c, UiMain, ColWine, M, M, 40, 40, 12f);
            Text(c, UiMain, icon, 13, M, M, 40, 40, TextAnchor.MiddleCenter);
            Text(c, UiMain, title, 18, M + 50, M - 2, w - 2 * (M + 50), 26, TextAnchor.MiddleCenter);
            Text(c, UiMain, sub, 11, M + 50, M + 22, w - 2 * (M + 50), 18, TextAnchor.MiddleCenter, ColDim, FontReg);
            Btn(c, UiMain, "X", "mg.ui close", ColBtn, 14, w - M - 40, M, 40, 40);
            Box(c, UiMain, ColWine, 0, M + 52, w, 2, 0f);
        }

        private void DestroyWindow(BasePlayer p, string name) { CuiHelper.DestroyUi(p, name + ".dim"); CuiHelper.DestroyUi(p, name); }

        private void CloseAll(BasePlayer p) => DestroyWindow(p, UiMain);

        #endregion

        #region Вертолётный ММ

        // форматы: a1 — атак-хели 1 на 1, a2 — атак-хели 2 на 2 (пилот + стрелок), m2 — мини 2 на 2 (пилот + стрелок с ракетами)
        private static readonly string[] HmFormats = { "a1", "a2", "m2" };
        private static int HmSize(string f) => f == "a1" ? 1 : 2;
        private static bool HmMini(string f) => f == "m2";

        private class HmEntry
        {
            public List<ulong> Members = new List<ulong>();
            public string Fmt;
            public bool Ranked;
            public float Since;
            public int Avg;
        }

        private class HeliMatch
        {
            public string Fmt;
            public bool Ranked;
            public GameMap Map;
            public List<ulong>[] Teams = { new List<ulong>(), new List<ulong>() };
            public int[] Score = new int[2];
            public int Round;
            public string State = "countdown";      // countdown | live | between | ended
            public float Until;
            public HashSet<ulong> Dead = new HashSet<ulong>();
            public List<BaseEntity>[] Veh = { new List<BaseEntity>(), new List<BaseEntity>() };
            public IEnumerable<ulong> All => Teams[0].Concat(Teams[1]);
            public int TeamOf(ulong id) => Teams[0].Contains(id) ? 0 : Teams[1].Contains(id) ? 1 : -1;
        }

        private readonly List<HmEntry> _hmQueue = new List<HmEntry>();
        private readonly List<HeliMatch> _hms = new List<HeliMatch>();
        private readonly Dictionary<ulong, HeliMatch> _hmIn = new Dictionary<ulong, HeliMatch>();
        private readonly Dictionary<ulong, string> _hmFmt = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, bool> _hmRanked = new Dictionary<ulong, bool>();

        private HeliMatch HmOf(BasePlayer p) { HeliMatch m; return p != null && _hmIn.TryGetValue(Id(p), out m) ? m : null; }
        private HmEntry HmQueueOf(ulong id) => _hmQueue.FirstOrDefault(e => e.Members.Contains(id));

        private int HmMmr(ulong id, string fmt) => Ranked != null && Ranked.Call("GetLadderMmr", id, "heli", HmSize(fmt)) is int ? (int)Ranked.Call("GetLadderMmr", id, "heli", HmSize(fmt)) : 1000;

        private List<ulong> HmParty(BasePlayer player)
        {
            if (player.currentTeam == 0UL) return new List<ulong> { Id(player) };
            var list = BasePlayer.activePlayerList.Where(p => p.currentTeam == player.currentTeam && p.IsConnected).Select(Id).ToList();
            if (!list.Contains(Id(player))) list.Add(Id(player));
            return list;
        }

        [HookMethod("HeliQueueCount")] public int HeliQueueCount() => _hmQueue.Sum(e => e.Members.Count);
        [HookMethod("HeliMatchCount")] public int HeliMatchCount() => _hms.Count;

        // вызывается из Ranked (кнопка «Искать вертолётный бой») и из меню /mg helimm
        [HookMethod("HeliQueue")]
        public void HeliQueue(BasePlayer player, string fmt, bool ranked)
        {
            if (player == null) return;
            var id = Id(player);
            if (!HmFormats.Contains(fmt)) fmt = "a1";
            if (HmOf(player) != null) { Toast(player, T(id, "HmInMatch"), true); return; }
            if (HmQueueOf(id) != null) { Toast(player, T(id, "HmAlready"), false); OpenHmMenu(player); return; }
            if (!_data.Maps.Values.Any(m => m.Enabled && m.Type == "heli" && m.Pads.Count >= 2)) { Toast(player, T(id, "HmNoMap"), true); return; }
            var party = HmParty(player);
            if (party.Count > HmSize(fmt)) { Toast(player, T(id, "HmPartyBig", HmSize(fmt)), true); return; }
            if (party.Any(x => _hmIn.ContainsKey(x) || HmQueueOf(x) != null)) { Toast(player, T(id, "HmPartyBusy"), true); return; }
            var e = new HmEntry { Members = party, Fmt = fmt, Ranked = ranked, Since = Time.realtimeSinceStartup };
            e.Avg = Mathf.RoundToInt((float)party.Average(x => HmMmr(x, fmt)));
            _hmQueue.Add(e);
            foreach (var m in party) { var p = BasePlayer.FindByID(m); if (p != null) Toast(p, T(m, "HmQueued", T(m, "Hm_" + fmt)), false); }
            _hmFmt[id] = fmt; _hmRanked[id] = ranked;
            OpenHmMenu(player);
            HmTryMatch();
        }

        private void HmCancel(BasePlayer player)
        {
            var e = HmQueueOf(Id(player));
            if (e == null) return;
            _hmQueue.Remove(e);
            foreach (var m in e.Members) { var p = BasePlayer.FindByID(m); if (p != null) Toast(p, T(m, "HmLeftQueue"), false); }
        }

        private int HmWin(HmEntry e) => _config.HmWindow + _config.HmWindowStep * Mathf.FloorToInt((Time.realtimeSinceStartup - e.Since) / 15f);

        private GameMap HmFreeMap()
        {
            var busy = new HashSet<string>(_games.Values.Where(g => g.Type == "heli").Select(g => g.Map.Id).Concat(_hms.Select(h => h.Map.Id)));
            return _data.Maps.Values.Where(m => m.Enabled && m.Type == "heli" && m.Pads.Count >= 2 && !busy.Contains(m.Id))
                .OrderBy(m => UnityEngine.Random.Range(0, 1000)).FirstOrDefault();
        }

        // подбор: по формату и типу (рейтинг/обычный), MMR в окне, пати целиком, один IP — не соперники
        private void HmTryMatch()
        {
            foreach (var fmt in HmFormats)
                foreach (var ranked in new[] { true, false })
                {
                    int size = HmSize(fmt);
                    var q = _hmQueue.Where(e => e.Fmt == fmt && e.Ranked == ranked).OrderBy(e => e.Since).ToList();
                    while (q.Count > 0)
                    {
                        var anchor = q[0];
                        var cands = q.Where(e => !ranked || Mathf.Abs(e.Avg - anchor.Avg) <= Mathf.Max(HmWin(anchor), HmWin(e))).OrderBy(e => Mathf.Abs(e.Avg - anchor.Avg)).ToList();
                        var teams = new[] { new List<HmEntry>(), new List<HmEntry>() };
                        int[] fill = new int[2];
                        foreach (var e in cands)
                        {
                            int n = e.Members.Count;
                            Func<int, bool> fits = t => fill[t] + n <= size && !teams[1 - t].Any(o => o.Members.Any(x => e.Members.Any(y => Hub != null && Hub.Call("SameIp", x, y) is bool && (bool)Hub.Call("SameIp", x, y))));
                            int team = e == anchor ? 0 : fits(0) && (!fits(1) || fill[0] <= fill[1]) ? 0 : fits(1) ? 1 : -1;
                            if (team < 0 || !fits(team)) continue;
                            teams[team].Add(e); fill[team] += n;
                            if (fill[0] == size && fill[1] == size) break;
                        }
                        if (fill[0] != size || fill[1] != size) { q.RemoveAt(0); continue; }
                        var map = HmFreeMap();
                        if (map == null) return;
                        foreach (var e in teams[0].Concat(teams[1])) { _hmQueue.Remove(e); q.Remove(e); }
                        HmCreate(fmt, ranked, map, teams[0].SelectMany(e => e.Members).ToList(), teams[1].SelectMany(e => e.Members).ToList());
                    }
                }
        }

        private void HmCreate(string fmt, bool ranked, GameMap map, List<ulong> a, List<ulong> b)
        {
            var m = new HeliMatch { Fmt = fmt, Ranked = ranked, Map = map };
            m.Teams[0].AddRange(a); m.Teams[1].AddRange(b);
            _hms.Add(m);
            var mode = HubModeOf("heli");
            foreach (var id in m.All)
            {
                _hmIn[id] = m;
                var p = BasePlayer.FindByID(id);
                if (p == null) continue;
                CloseAll(p);
                Leave(p, false);
                if (Hub != null && !string.IsNullOrEmpty(mode) && Hub.Call("GetMode", p) as string != mode) Hub.Call("SendToMode", p, mode, true);
                Center(p, T(id, "HmFound", T(id, "Hm_" + fmt)) + (ranked ? "\n<size=12>" + T(id, "HmRankedTag") + "</size>" : ""), ColGold, 3f);
            }
            timer.Once(1.5f, () => HmStartRound(m));
        }

        private Vector3 HmSpawnPt(HeliMatch m, ulong id)
        {
            int t = Mathf.Max(0, m.TeamOf(id));
            var list = m.Map.Spawns.Count > t ? m.Map.Spawns[t] : null;
            if (list != null && list.Count > 0) return list[(int)(id % (ulong)list.Count)].V;
            var pad = m.Map.Pads[t % m.Map.Pads.Count].V;
            int idx = m.Teams[t].IndexOf(id);
            return pad + new Vector3(6f + idx * 2f, 0.5f, 0f);
        }

        private void HmKit(BasePlayer p, HeliMatch m)
        {
            var id = Id(p);
            int t = m.TeamOf(id);
            bool gunner = HmMini(m.Fmt) && t >= 0 && m.Teams[t].IndexOf(id) == 1;
            GiveKit(p, gunner ? _config.HmMiniGunner : _config.HeliKit);
        }

        private BaseEntity HmVehicle(HeliMatch m, int team, Pt pad)
        {
            var e = GameManager.server.CreateEntity(HmMini(m.Fmt) ? PrefabMini : PrefabAttackHeli, pad.V + Vector3.up * 0.3f, Quaternion.Euler(0f, pad.Yaw, 0f));
            if (e == null) { PrintWarning("Вертолётный ММ: не создан транспорт — проверь префабы"); return null; }
            e.enableSaving = false;
            e.Spawn();
            AddFuel(e, _config.HmFuel);
            if (!HmMini(m.Fmt))
                NextTick(() =>
                {
                    if (e == null || e.IsDestroyed) return;
                    foreach (var child in e.children.ToList())
                    {
                        var sc = child as StorageContainer;
                        if (sc?.inventory == null) continue;
                        foreach (var a in _config.HeliAmmo) { var it = ItemManager.CreateByName(a.Item, a.Amount, 0UL); if (it != null && !it.MoveToContainer(sc.inventory)) it.Remove(); }
                    }
                });
            m.Veh[team].Add(e);
            return e;
        }

        private void HmClearVeh(HeliMatch m)
        {
            for (int t = 0; t < 2; t++) { foreach (var e in m.Veh[t]) if (e != null && !e.IsDestroyed) e.Kill(); m.Veh[t].Clear(); }
        }

        private void HmStartRound(HeliMatch m)
        {
            if (!_hms.Contains(m) || m.State == "ended") return;
            m.Round++;
            m.Dead.Clear();
            HmClearVeh(m);
            // красные — чётные площадки, синие — нечётные; на 1v1 и 2v2 — по одной машине на команду или игрока
            for (int t = 0; t < 2; t++)
            {
                var pads = m.Map.Pads.Where((pt, i) => i % 2 == t).ToList();
                if (pads.Count == 0) pads.Add(m.Map.Pads[t % m.Map.Pads.Count]);
                int need = m.Fmt == "a1" ? m.Teams[t].Count : 1;
                for (int i = 0; i < need; i++) HmVehicle(m, t, pads[i % pads.Count]);
            }
            foreach (var id in m.All.ToList())
            {
                var p = BasePlayer.FindByID(id);
                if (p == null || !p.IsConnected) continue;
                if (p.IsDead()) { p.Respawn(); continue; }
                if (p.isMounted) p.EnsureDismounted();
                p.Teleport(HmSpawnPt(m, id) + Vector3.up * 0.2f);
                Heal(p);
                p.inventory.Strip();
                HmKit(p, m);
            }
            m.State = "countdown";
            m.Until = Time.realtimeSinceStartup + _config.HmCountdown;
            foreach (var id in m.All) { var p = BasePlayer.FindByID(id); if (p != null) { Center(p, T(id, "HmRound", m.Round) + "\n<size=13>" + T(id, "HmBoard") + "</size>", ColText, 2.5f); DrawHmHud(p, m); } }
        }

        private bool HmTeamOut(HeliMatch m, int t)
        {
            bool allDead = m.Teams[t].All(x => m.Dead.Contains(x) || BasePlayer.FindByID(x) == null);
            bool noVeh = m.Veh[t].Count > 0 && m.Veh[t].All(e => e == null || e.IsDestroyed);
            return allDead || noVeh;
        }

        private void HmTick()
        {
            float now = Time.realtimeSinceStartup;
            if (_hmQueue.Count > 0) HmTryMatch();
            foreach (var m in _hms.ToList())
            {
                if (m.State == "countdown" && now >= m.Until)
                {
                    m.State = "live";
                    foreach (var id in m.All) { var p = BasePlayer.FindByID(id); if (p != null) Center(p, T(id, "HmGo"), ColGreen, 1.5f); }
                }
                else if (m.State == "live")
                {
                    // зона: за радиусом и выше потолка машина теряет ХП
                    for (int t = 0; t < 2; t++) foreach (var e in m.Veh[t]) if (e != null && !e.IsDestroyed) HmBounds(m, e);
                    bool o0 = HmTeamOut(m, 0), o1 = HmTeamOut(m, 1);
                    if (o0 || o1) HmEndRound(m, o0 && o1 ? -1 : o0 ? 1 : 0);
                }
                else if (m.State == "between" && now >= m.Until) HmStartRound(m);
                if (m.State != "ended") foreach (var id in m.All) { var p = BasePlayer.FindByID(id); if (p != null && p.IsConnected) DrawHmHud(p, m); }
            }
        }

        private void HmBounds(HeliMatch m, BaseEntity veh)
        {
            var pos = veh.transform.position;
            float ground = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(pos) : 0f;
            bool outR = m.Map.Center != null && Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(m.Map.Center.X, 0f, m.Map.Center.Z)) > m.Map.Radius;
            bool outH = pos.y - ground > _config.HeliCeiling;
            if (!outR && !outH) return;
            var ce = veh as BaseCombatEntity;
            if (ce != null) ce.Hurt(ce.MaxHealth() * 0.08f, Rust.DamageType.Generic, null, false);
            foreach (var id in m.All)
            {
                var p = BasePlayer.FindByID(id);
                if (p != null && p.isMounted && Vector3.Distance(p.transform.position, pos) < 8f) Toast(p, outH ? T(id, "Ceiling") : T(id, "HeliBorder"), true);
            }
        }

        private void HmEndRound(HeliMatch m, int winner)
        {
            if (m.State != "live") return;
            if (winner >= 0) m.Score[winner]++;
            if (winner >= 0 && m.Score[winner] >= Mathf.Max(1, _config.HmRounds)) { HmEnd(m, winner, false); return; }
            m.State = "between";
            m.Until = Time.realtimeSinceStartup + _config.HmBetween;
            foreach (var id in m.All)
            {
                var p = BasePlayer.FindByID(id);
                if (p == null) continue;
                int t = m.TeamOf(id);
                Center(p, (winner < 0 ? T(id, "HmDraw") : winner == t ? T(id, "HmRoundWin") : T(id, "HmRoundLose")) + $"\n<size=18>{m.Score[t]} : {m.Score[1 - t]}</size>", winner == t ? ColGreen : winner < 0 ? ColText : ColRed, 3f);
            }
        }

        private void HmEnd(HeliMatch m, int winner, bool forfeit)
        {
            if (m.State == "ended") return;
            m.State = "ended";
            _hms.Remove(m);
            HmClearVeh(m);
            Dictionary<ulong, int> deltas = null;
            if (winner >= 0 && m.Ranked && Ranked != null)
                deltas = Ranked.Call("ReportExternal", "heli", HmSize(m.Fmt), m.Teams[winner].ToList(), m.Teams[1 - winner].ToList(), $"{m.Score[winner]}:{m.Score[1 - winner]}") as Dictionary<ulong, int>;
            var mode = HubModeOf("heli");
            foreach (var id in m.All.ToList())
            {
                _hmIn.Remove(id);
                int t = m.TeamOf(id);
                bool win = t == winner;
                if (winner >= 0) { Hub?.Call("ModeEvent", id, "helimm", win); if (win) Ach(id, "helimm_wins"); }
                if (win && winner >= 0)
                {
                    W(id, "heli");
                    if (Hub != null && _config.HmWinCoins > 0) Hub.Call("AddCoins", id, _config.HmWinCoins, T(id, "HmCoins"));
                }
                var p = BasePlayer.FindByID(id);
                if (p == null || !p.IsConnected) continue;
                CuiHelper.DestroyUi(p, UiHud);
                _hudJson.Remove(id);
                int d = 0; if (deltas != null) deltas.TryGetValue(id, out d);
                string mmr = deltas != null ? $"\n<size=13>MMR {(d >= 0 ? "+" : "")}{d}</size>" : "";
                if (winner >= 0) Center(p, (win ? T(id, "HmWin") : T(id, "HmLose")) + $"\n<size=18>{m.Score[t]} : {m.Score[1 - t]}</size>" + (forfeit ? "\n<size=12>" + T(id, "HmForfeit") + "</size>" : "") + mmr, win ? ColGreen : ColRed, 5f);
                var pl = p;
                timer.Once(4f, () =>
                {
                    if (pl == null || !pl.IsConnected || pl.IsDead() || _hmIn.ContainsKey(Id(pl))) return;
                    if (pl.isMounted) pl.EnsureDismounted();
                    pl.inventory.Strip();
                    if (Hub != null && !string.IsNullOrEmpty(mode)) Hub.Call("SendToMode", pl, mode, true);
                });
            }
        }

        private void W(ulong id, string type)
        {
            Dictionary<string, int> w;
            if (!_data.Wins.TryGetValue(id, out w)) _data.Wins[id] = w = new Dictionary<string, int>();
            int n; w.TryGetValue(type, out n); w[type] = n + 1;
        }

        // выход: из очереди — просто убираем; из матча — техническое поражение команде
        private void HmLeave(BasePlayer player, bool toHall)
        {
            if (player == null) return;
            var id = Id(player);
            var e = HmQueueOf(id);
            if (e != null) { e.Members.Remove(id); if (e.Members.Count == 0) _hmQueue.Remove(e); }
            var m = HmOf(player);
            if (m == null || m.State == "ended") return;
            int t = m.TeamOf(id);
            m.Teams[t].Remove(id);
            _hmIn.Remove(id);
            CuiHelper.DestroyUi(player, UiHud);
            if (m.Teams[t].Count == 0 || m.Teams[t].All(x => BasePlayer.FindByID(x) == null))
            {
                m.Teams[t].Add(id);   // чтобы рейтинг посчитался и ушедшему
                HmEnd(m, 1 - t, true);
                _hmIn.Remove(id);
            }
            if (toHall && player.IsConnected && !player.IsDead() && Hub != null) Hub.Call("SendToMode", player, HubModeOf("heli"), true);
        }

        private object HmDamage(BaseCombatEntity entity, BaseEntity attackerEnt)
        {
            var attacker = attackerEnt as BasePlayer;
            var victim = entity as BasePlayer;
            var vm = HmOf(victim);
            var am = HmOf(attacker);
            if (vm == null && am == null)
            {
                // машины матча бьют только участники этого матча
                foreach (var m in _hms) for (int t = 0; t < 2; t++) if (m.Veh[t].Contains(entity as BaseEntity))
                    {
                        if (m.State != "live") return true;
                        if (attacker != null && HmOf(attacker) != m) return true;
                        if (attacker != null && m.TeamOf(Id(attacker)) == t) return true;   // свою машину не ломаем
                        return null;
                    }
                return null;
            }
            if (victim != null)
            {
                if (vm == null) return attacker != null && attacker != victim ? (object)true : null;
                if (vm.State != "live") return true;
                if (attacker == null || attacker == victim) return null;
                if (am != vm) return true;
                if (vm.TeamOf(Id(attacker)) == vm.TeamOf(Id(victim))) return true;
                return null;
            }
            // игрок матча стреляет по чужой машине/постройке вне матча
            if (am != null) { for (int t = 0; t < 2; t++) if (am.Veh[t].Contains(entity as BaseEntity)) return am.State != "live" || am.TeamOf(Id(attacker)) == t ? (object)true : null; return true; }
            return null;
        }

        private void HmEntityDeath(BaseCombatEntity entity)
        {
            foreach (var m in _hms.ToList())
                for (int t = 0; t < 2; t++)
                    if (m.Veh[t].Contains(entity as BaseEntity) && m.State == "live")
                        foreach (var id in m.All) { var p = BasePlayer.FindByID(id); if (p != null) Toast(p, T(id, m.TeamOf(id) == t ? "HmOursDown" : "HmTheirsDown"), m.TeamOf(id) == t); }
        }

        private bool HmPlayerDeath(BasePlayer victim, HitInfo info)
        {
            var m = HmOf(victim);
            if (m == null) return false;
            var vid = Id(victim);
            if (m.State == "live") m.Dead.Add(vid);
            victim.inventory.Strip();
            Timer old;
            if (_respawn.TryGetValue(vid, out old)) old?.Destroy();
            var pl = victim;
            _respawn[vid] = timer.Once(_config.Respawn, () => { _respawn.Remove(vid); if (pl != null && pl.IsConnected && pl.IsDead() && HmOf(pl) == m) pl.Respawn(); });
            return true;
        }

        private void DrawHmHud(BasePlayer player, HeliMatch m)
        {
            var id = Id(player);
            int t = Mathf.Max(0, m.TeamOf(id));
            var c = new CuiElementContainer();
            const float w = 380f;
            Root(c, UiHud, "Hud", ColHud, "0.5 1", "0.5 1", $"{-w / 2f} -96", $"{w / 2f} -14", 14f, false);
            Text(c, UiHud, $"{T(id, "Hm_" + m.Fmt).ToUpper()} · {m.Map.Name}" + (m.Ranked ? " · " + T(id, "HmRankedShort") : ""), 10, 0, 6, w, 16, TextAnchor.MiddleCenter, ColDim);
            int left = Mathf.Max(0, Mathf.CeilToInt(m.Until - Time.realtimeSinceStartup));
            string phase = m.State == "countdown" ? T(id, "HmStartsIn", left) : m.State == "between" ? T(id, "HmNextIn", left) : T(id, "HmRoundN", m.Round, _config.HmRounds);
            Text(c, UiHud, phase, 13, 0, 22, w, 20, TextAnchor.MiddleCenter, m.State == "live" ? ColText : ColGold);
            for (int k = 0; k < 2; k++)
            {
                int tt = k == 0 ? t : 1 - t;
                float x = 12 + k * (w - 24) / 2f, bw = (w - 24) / 2f - 6;
                Box(c, UiHud, k == 0 ? "1 1 1 0.08" : "0 0 0 0", x, 46, bw, 38, 9f);
                int alive = m.Teams[tt].Count(id2 => !m.Dead.Contains(id2));
                int veh = m.Veh[tt].Count(e => e != null && !e.IsDestroyed);
                Text(c, UiHud, $"<color={TeamHex[tt]}>■</color> {(k == 0 ? T(id, "HmUs") : T(id, "HmThem"))}  <size=18>{m.Score[tt]}</size>", 12, x + 10, 46, bw - 20, 20, TextAnchor.MiddleLeft);
                Text(c, UiHud, T(id, "HmAliveVeh", alive, veh), 10, x + 10, 64, bw - 20, 16, TextAnchor.MiddleLeft, ColDim, FontReg);
            }
            ScaleHud(player, c);
            var json = c.ToJson();
            string old;
            if (_hudJson.TryGetValue(id, out old) && old == json) return;
            _hudJson[id] = json;
            CuiHelper.DestroyUi(player, UiHud);
            CuiHelper.AddUi(player, json);
        }

        private void OpenHmMenu(BasePlayer player)
        {
            var id = Id(player);
            string fmt; if (!_hmFmt.TryGetValue(id, out fmt)) fmt = "a1";
            bool ranked = _hmRanked.ContainsKey(id) && _hmRanked[id];
            const float W = 920f, H = 560f;
            var c = new CuiElementContainer();
            Window(c, "H", T(id, "HmTitle"), T(id, "HmSub", _config.HmRounds), W, H);
            float top = M + 52 + 2 + M;
            float cw = (W - 2 * M - 2 * G) / 3f;
            for (int i = 0; i < HmFormats.Length; i++)
            {
                var f = HmFormats[i];
                bool a = f == fmt;
                float x = M + i * (cw + G);
                int q = _hmQueue.Where(e => e.Fmt == f).Sum(e => e.Members.Count);
                Box(c, UiMain, a ? ColWineSoft : ColPanel, x, top, cw, 132, 16f);
                Box(c, UiMain, "0.48 0.07 0.14 0.45", x + 14, top + 14, 44, 44, 12f);
                Text(c, UiMain, HmMini(f) ? "M" : f == "a1" ? "1" : "2", 18, x + 14, top + 14, 44, 44, TextAnchor.MiddleCenter, ColRose);
                Text(c, UiMain, T(id, "Hm_" + f), 15, x + 70, top + 14, cw - 84, 22);
                Text(c, UiMain, T(id, "HmD_" + f), 10, x + 70, top + 36, cw - 84, 30, TextAnchor.UpperLeft, ColDim, FontReg);
                int mmr = HmMmr(id, f);
                Text(c, UiMain, T(id, "HmCardFoot", mmr, q), 11, x + 14, top + 96, cw - 28, 22, TextAnchor.MiddleLeft, ColText);
                Btn(c, UiMain, "", "mg.ui hm.fmt " + f, "0 0 0 0", 10, x, top, cw, 132, ColText, 0f);
            }
            top += 132 + G;
            // рейтинг вкл/выкл
            Box(c, UiMain, ColPanel, M, top, W - 2 * M, 56, 14f);
            Text(c, UiMain, T(id, "HmRankedRow"), 13, M + 16, top, W - 2 * M - 140, 34, TextAnchor.MiddleLeft);
            Text(c, UiMain, T(id, "HmRankedHint"), 10, M + 16, top + 28, W - 2 * M - 140, 20, TextAnchor.MiddleLeft, ColDim, FontReg);
            Btn(c, UiMain, ranked ? T(id, "On") : T(id, "Off"), "mg.ui hm.ranked", ranked ? ColWine : ColBtn, 12, W - M - 106, top + 12, 90, 32, ColText, 10f);
            top += 56 + G;
            // правила
            float ph = H - M - 50 - G - top;
            Box(c, UiMain, ColPanel, M, top, W - 2 * M, ph, 14f);
            Text(c, UiMain, T(id, "HmRules", _config.HmRounds), 11, M + 16, top + 10, W - 2 * M - 32, ph - 20, TextAnchor.UpperLeft, ColDim, FontReg);
            float by = H - M - 50;
            var e0 = HmQueueOf(id);
            var m0 = HmOf(player);
            if (m0 != null) Btn(c, UiMain, T(id, "HmSurrender"), "mg.ui leave", ColBtn, 14, M, by, W - 2 * M, 50, ColText, 14f);
            else if (e0 != null)
            {
                int secs = Mathf.FloorToInt(Time.realtimeSinceStartup - e0.Since);
                Btn(c, UiMain, T(id, "HmSearching", T(id, "Hm_" + e0.Fmt), secs / 60, secs % 60), "mg.ui hm.cancel", ColBtn, 14, M, by, W - 2 * M, 50, ColText, 14f);
            }
            else Btn(c, UiMain, T(id, ranked ? "HmFindRanked" : "HmFind"), "mg.ui hm.join", ColWine, 15, M, by, W - 2 * M, 50, ColText, 14f);
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        #endregion

        #region Интерфейс: меню режима

        private void OpenMenu(BasePlayer player, string type)
        {
            var id = Id(player);
            const float W = 920f, H = 620f;
            var c = new CuiElementContainer();
            Window(c, T(id, "Icon_" + type), T(id, "Type_" + type), T(id, "TypeSub_" + type), W, H);
            float top = M + 52 + 2 + M;
            // вкладки режимов — равные
            float tw = (W - 2 * M - 3 * 6f) / 4f;
            for (int i = 0; i < Types.Length; i++)
            {
                bool a = Types[i] == type;
                Btn(c, UiMain, T(id, "Type_" + Types[i]), "mg.ui type " + Types[i], a ? ColWine : ColBtn, 12, M + i * (tw + 6), top, tw, 34, a ? ColText : ColDim, 11f);
            }
            top += 34 + G;
            float ph = H - M - 48 - G - top;
            var g = GameOf(player);
            bool inThis = g != null && g.Type == type;

            if (type == "roam")
            {
                string kitSel; if (!_roamKit.TryGetValue(id, out kitSel)) kitSel = _config.RoamKits.FirstOrDefault()?.Id;
                string vehSel; if (!_roamVeh.TryGetValue(id, out vehSel)) vehSel = "none";
                Text(c, UiMain, T(id, "ChooseKit"), 10, M, top, 300, 16, TextAnchor.MiddleLeft, ColDim);
                float kw = (W - 2 * M - 3 * G) / 4f;
                for (int i = 0; i < _config.RoamKits.Count && i < 4; i++)
                {
                    var k = _config.RoamKits[i];
                    bool a = k.Id == kitSel;
                    float x = M + i * (kw + G);
                    Box(c, UiMain, a ? ColWineSoft : ColPanel, x, top + 22, kw, 96, 14f);
                    var weapon = k.Items.FirstOrDefault(e => e.Container == "belt")?.Item;
                    if (weapon != null) ItemIcon(c, UiMain, weapon, x + 12, top + 34, 44);
                    Text(c, UiMain, k.Name, 15, x + 64, top + 34, kw - 74, 22);
                    Text(c, UiMain, k.Desc, 10, x + 64, top + 58, kw - 74, 40, TextAnchor.UpperLeft, ColDim, FontReg);
                    Btn(c, UiMain, "", "mg.ui kit " + k.Id, "0 0 0 0", 10, x, top + 22, kw, 96, ColText, 0f);
                }
                float vy = top + 22 + 96 + G + 6;
                Text(c, UiMain, T(id, "ChooseVeh"), 10, M, vy, 300, 16, TextAnchor.MiddleLeft, ColDim);
                var vehs = new[] { "none", "car", "horse", "mini" };
                for (int i = 0; i < 4; i++)
                    Btn(c, UiMain, T(id, "Veh_" + vehs[i]), "mg.ui veh " + vehs[i], vehSel == vehs[i] ? ColWine : ColBtn, 13, M + i * (kw + G), vy + 22, kw, 44, vehSel == vehs[i] ? ColText : ColDim, 13f);
                float iy = vy + 22 + 44 + G;
                Box(c, UiMain, ColPanel, M, iy, W - 2 * M, top + ph - iy, 14f);
                Game rg = _games.Values.FirstOrDefault(x => x.Type == "roam");
                int online = rg?.Players.Count ?? 0;
                var best = rg?.Kills.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{BasePlayer.FindByID(kv.Key)?.displayName ?? "?"} {kv.Value}").ToArray() ?? new string[0];
                Text(c, UiMain, T(id, "RoamInfo", online, best.Length > 0 ? string.Join(" · ", best) : "—"), 12, M + 16, iy, W - 2 * M - 32, top + ph - iy, TextAnchor.MiddleLeft, ColDim, FontReg);
                float bw = (W - 2 * M - G) / 2f;
                Btn(c, UiMain, inThis ? T(id, "Respawn") : T(id, "ToBattle"), "mg.ui roam.spawn", ColWine, 15, M, H - M - 48, inThis ? bw : W - 2 * M, 48, ColText, 14f);
                if (inThis) Btn(c, UiMain, T(id, "LeaveBtn"), "mg.ui leave", ColBtn, 13, M + bw + G, H - M - 48, bw, 48, ColText, 14f);
            }
            else
            {
                // описание + состояние очереди
                float cw = (W - 2 * M - G) / 2f;
                Box(c, UiMain, ColPanel, M, top, cw, ph, 16f);
                Text(c, UiMain, T(id, "Rules_" + type), 12, M + 16, top + 14, cw - 32, ph - 28, TextAnchor.UpperLeft, ColText, FontReg);
                float rx = M + cw + G;
                Box(c, UiMain, ColPanel, rx, top, cw, ph, 16f);
                var q = _games.Values.FirstOrDefault(x => x.Type == type && (x.State == "queue" || x.State == "countdown"));
                var running = _games.Values.Where(x => x.Type == type && x.State != "queue" && x.State != "countdown" && x.State != "ended").ToList();
                int ti = TypeIdx(type);
                Text(c, UiMain, T(id, "QueueTitle", q?.Players.Count ?? 0, _config.MinPlayers[ti], _config.MaxPlayers[ti]), 10, rx + 14, top + 10, cw - 28, 16, TextAnchor.MiddleLeft, ColDim);
                if (q != null)
                {
                    for (int i = 0; i < q.Players.Count && i < 8; i++)
                    {
                        Box(c, UiMain, q.Players[i] == id ? ColWineSoft : ColRow, rx + 12, top + 34 + i * 38, cw - 24, 32, 9f);
                        Text(c, UiMain, BasePlayer.FindByID(q.Players[i])?.displayName ?? "?", 12, rx + 24, top + 34 + i * 38, cw - 48, 32);
                    }
                    if (q.State == "countdown") Text(c, UiMain, T(id, "StartsIn", Mathf.CeilToInt(q.StateUntil - Time.realtimeSinceStartup)), 14, rx, top + ph - 60, cw, 24, TextAnchor.MiddleCenter, ColGold);
                }
                else Text(c, UiMain, T(id, "QueueEmpty"), 12, rx, top, cw, ph - 40, TextAnchor.MiddleCenter, ColDim, FontReg);
                if (running.Count > 0) Text(c, UiMain, T(id, "Running", running.Count), 11, rx, top + ph - 34, cw, 20, TextAnchor.MiddleCenter, ColDim, FontReg);
                float bw = (W - 2 * M - G) / 2f;
                if (inThis) Btn(c, UiMain, T(id, "LeaveBtn"), "mg.ui leave", ColBtn, 14, M, H - M - 48, W - 2 * M, 48, ColText, 14f);
                else Btn(c, UiMain, T(id, "JoinQueue"), "mg.ui join " + type, ColWine, 15, M, H - M - 48, W - 2 * M, 48, ColText, 14f);
            }
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        private void OpenBwShop(BasePlayer player)
        {
            var id = Id(player);
            var g = GameOf(player);
            if (g == null || g.Type != "bedwars") { Toast(player, T(id, "NotBedwars"), true); return; }
            const float W = 900f, H = 600f;
            var c = new CuiElementContainer();
            Window(c, "$", T(id, "BwShop"), T(id, "BwShopSub"), W, H);
            float top = M + 52 + 2 + M;
            // валюты
            var curs = new[] { "wood", "metal.fragments", "scrap" };
            float cw0 = (W - 2 * M - 2 * G) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var def = ItemManager.FindItemDefinition(curs[i]);
                int have = def != null ? player.inventory.GetAmount(def.itemid) : 0;
                Box(c, UiMain, ColPanel, M + i * (cw0 + G), top, cw0, 40, 12f);
                ItemIcon(c, UiMain, curs[i], M + i * (cw0 + G) + 10, top + 6, 28);
                Text(c, UiMain, $"{CurName(curs[i], id)}: {have}", 14, M + i * (cw0 + G) + 46, top, cw0 - 56, 40, TextAnchor.MiddleLeft, i == 2 ? ColGold : ColText);
            }
            top += 40 + G;
            const int cols = 4;
            float cw = (W - 2 * M - (cols - 1) * G) / cols;
            int rows = Mathf.CeilToInt(Mathf.Min(16, _config.BwShop.Count) / (float)cols);
            float ch = Mathf.Min(110f, (H - M - top - (rows - 1) * G) / Mathf.Max(1, rows));
            for (int i = 0; i < _config.BwShop.Count && i < 16; i++)
            {
                var s = _config.BwShop[i];
                float x = M + (i % cols) * (cw + G), y = top + (i / cols) * (ch + G);
                Box(c, UiMain, ColPanel, x, y, cw, ch, 14f);
                ItemIcon(c, UiMain, s.Item, x + 10, y + 10, 40);
                Text(c, UiMain, s.Name, 12, x + 58, y + 8, cw - 66, 40, TextAnchor.UpperLeft);
                Btn(c, UiMain, $"{s.Price} {CurName(s.Currency, id)}", "mg.ui bw.buy " + i, s.Currency == "scrap" ? "0.55 0.42 0.12 1" : ColWine, 11, x + 8, y + ch - 36, cw - 16, 28, ColText, 10f);
            }
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        #endregion

        #region Интерфейс: HUD

        private void DrawHudAll(Game g) { foreach (var id in g.Players) { var p = BasePlayer.FindByID(id); if (p != null && p.IsConnected) DrawHud(p, g); } }

        private void DrawHud(BasePlayer player, Game g)
        {
            var id = Id(player);
            var c = new CuiElementContainer();
            int myTeam = g.Team.ContainsKey(id) ? g.Team[id] : 0;
            if (g.Type == "roam")
            {
                int k; g.Kills.TryGetValue(id, out k);
                Root(c, UiHud, "Hud", ColHud, "0 1", "0 1", "16 -70", "256 -16", 14f, false);
                Text(c, UiHud, T(id, "Type_roam").ToUpper() + $" · {g.Players.Count} " + T(id, "Players"), 10, 12, 6, 216, 16, TextAnchor.MiddleLeft, ColDim);
                Text(c, UiHud, T(id, "YourKills", k) + "   ·   " + T(id, "MenuHint"), 12, 12, 24, 216, 22, TextAnchor.MiddleLeft);
            }
            else
            {
                float now = Time.realtimeSinceStartup;
                int left = Mathf.Max(0, Mathf.CeilToInt(g.StateUntil - now));
                string phase = g.State == "queue" ? T(id, "Waiting", g.Players.Count, _config.MinPlayers[TypeIdx(g.Type)]) :
                    g.State == "countdown" ? T(id, "StartsIn", left) :
                    g.State == "build" ? T(id, "BuildPhase", left / 60, left % 60) :
                    g.Type == "raidwars" ? T(id, "RaidPhaseHud", left / 60, left % 60) :
                    g.Type == "heli" ? T(id, "HeliHud", _config.HeliKills, left / 60, left % 60) : T(id, "BwHud");
                int teams = g.Type == "bedwars" ? Mathf.Clamp(g.Map.Teams, 2, MaxTeams) : 2;
                float w = 380f;
                int rowsN = g.Type == "bedwars" && teams > 4 ? 2 : 1;
                Root(c, UiHud, "Hud", ColHud, "0.5 1", "0.5 1", $"{-w / 2f} -{(g.Type == "bedwars" ? 104 + (rowsN - 1) * 40 : 82)}", $"{w / 2f} -14", 14f, false);
                Text(c, UiHud, $"{T(id, "Type_" + g.Type).ToUpper()} · {g.Map.Name}", 10, 0, 6, w, 16, TextAnchor.MiddleCenter, ColDim);
                Text(c, UiHud, phase, 14, 0, 22, w, 22, TextAnchor.MiddleCenter, g.State == "build" ? ColGold : ColText);
                if (g.Type == "bedwars")
                {
                    int perRow = Mathf.CeilToInt(teams / (float)rowsN);
                    float tw = (w - 24f) / perRow;
                    for (int t = 0; t < teams; t++)
                    {
                        int alive = g.Players.Count(x => g.Team[x] == t && !_out.Contains(x));
                        float tx = 12 + (t % perRow) * tw, ty = 48 + (t / perRow) * 40;
                        Box(c, UiHud, t == myTeam ? "1 1 1 0.08" : "0 0 0 0", tx, ty, tw - 6, 36, 9f);
                        string bedTxt = teams > 4 ? (g.BedAlive[t] ? "●" : "×") : (g.BedAlive[t] ? T(id, "BedOk") : T(id, "BedLost"));
                        Text(c, UiHud, $"<color={TeamHex[t]}>■</color> {bedTxt} · {alive}", 11, tx, ty, tw - 6, 36, TextAnchor.MiddleCenter, g.Eliminated.Contains(t) ? ColDim : ColText);
                    }
                }
                else if (g.State != "queue" && g.State != "countdown")
                {
                    Text(c, UiHud, $"<color={TeamHex[0]}>{TeamName(0, id)} {g.Score[0]}</color>   :   <color={TeamHex[1]}>{g.Score[1]} {TeamName(1, id)}</color>", 16, 0, 44, w, 28, TextAnchor.MiddleCenter);
                }
            }
            // перерисовываем только если что-то изменилось — меньше трафика и мерцания
            ScaleHud(player, c);
            var json = c.ToJson();
            string old;
            if (_hudJson.TryGetValue(id, out old) && old == json) return;
            _hudJson[id] = json;
            CuiHelper.DestroyUi(player, UiHud);
            CuiHelper.AddUi(player, json);
        }

        private readonly Dictionary<ulong, string> _hudJson = new Dictionary<ulong, string>();

        private void Center(BasePlayer player, string text, string color, float seconds)
        {
            if (player == null || !player.IsConnected) return;
            var c = new CuiElementContainer();
            c.Add(new CuiLabel { Text = { Text = text, FontSize = 30, Align = TextAnchor.MiddleCenter, Color = color, Font = Font }, RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-420 40", OffsetMax = "420 190" } }, "Hud", UiCenter);
            CuiHelper.DestroyUi(player, UiCenter);
            AddUiScaled(player, c);
            var p = player;
            timer.Once(seconds, () => { if (p != null && p.IsConnected) CuiHelper.DestroyUi(p, UiCenter); });
        }

        private void Toast(BasePlayer player, string text, bool warn)
        {
            if (player == null || !player.IsConnected) return;
            var c = new CuiElementContainer();
            Root(c, UiToast, "Hud", warn ? "0.40 0.06 0.11 0.92" : "0.07 0.065 0.065 0.90", "0.5 1", "0.5 1", "-260 -150", "260 -110", 20f, false);
            c.Add(new CuiLabel { Text = { Text = text, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = ColText, Font = Font }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, UiToast);
            CuiHelper.DestroyUi(player, UiToast);
            AddUiScaled(player, c);
            var p = player;
            timer.Once(3f, () => { if (p != null && p.IsConnected) CuiHelper.DestroyUi(p, UiToast); });
        }

        private void Broadcast(Game g, string text, bool warn) { foreach (var id in g.Players) { var p = BasePlayer.FindByID(id); if (p != null) Toast(p, text, warn); } }

        #endregion

        #region Профиль

        private string OnHubProfile(ulong userId, string lng)
        {
            Dictionary<string, int> w;
            if (!_data.Wins.TryGetValue(userId, out w) || w.Count == 0) return null;
            bool en = lng == "en";
            int v;
            Func<string, int> G0 = k => w.TryGetValue(k, out v) ? v : 0;
            return en ? $"<color=#f0a6b1>MINIGAMES</color>\nWins: heli {G0("heli")} · bedwars {G0("bedwars")} · raid wars {G0("raidwars")}"
                      : $"<color=#f0a6b1>МИНИ-ИГРЫ</color>\nПобед: хели {G0("heli")} · bedwars {G0("bedwars")} · рейд {G0("raidwars")}";
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
            new[] { "On", "ВКЛ", "ON" }, new[] { "Off", "ВЫКЛ", "OFF" },
            new[] { "HmTitle", "ВЕРТОЛЁТНЫЙ ММ", "HELI MATCHMAKING" }, new[] { "HmSub", "подбор по MMR · раунды до {0} побед · /mg helimm", "MMR matchmaking · first to {0} · /mg helimm" },
            new[] { "Hm_a1", "Атак-хели 1v1", "Attack heli 1v1" }, new[] { "Hm_a2", "Атак-хели 2v2", "Attack heli 2v2" }, new[] { "Hm_m2", "Мини 2v2 с ракетами", "Mini 2v2 rockets" },
            new[] { "HmD_a1", "один на один, сам пилот и сам стреляешь ракетами", "one on one, you fly and fire rockets" },
            new[] { "HmD_a2", "пилот + стрелок у турели, один хели на команду", "pilot + turret gunner, one heli per team" },
            new[] { "HmD_m2", "пилот + стрелок с самонаводящимися ракетами", "pilot + gunner with homing missiles" },
            new[] { "HmCardFoot", "MMR {0}   ·   в поиске: {1}", "MMR {0}   ·   searching: {1}" },
            new[] { "HmRankedRow", "Рейтинговый матч (лестница «Вертолёты» в /ranked)", "Ranked match (Helicopters ladder in /ranked)" },
            new[] { "HmRankedHint", "Выкл — обычный бой без MMR; вкл — победа и поражение меняют рейтинг.", "Off — casual, no MMR; on — wins and losses change your rating." },
            new[] { "HmRules", "• Машины стоят на площадках: красные — чётные, синие — нечётные.\n• Отсчёт — садись (E), раунд начинается — можно стрелять.\n• Раунд выигрывает тот, кто сбил все машины соперника или выбил весь экипаж.\n• Матч — до {0} побед в раундах, каждый раунд новые машины.\n• За границей зоны и выше потолка машина теряет ХП.\n• Вышел из матча — техническое поражение команде.", "• Vehicles wait on pads.\n• Board during the countdown, fight when the round starts.\n• Destroy every enemy vehicle or the whole crew to win the round.\n• First to {0} round wins.\n• Leaving the zone or the ceiling damages the vehicle.\n• Leaving the match is a forfeit." },
            new[] { "HmFind", "ИСКАТЬ БОЙ", "FIND FIGHT" }, new[] { "HmFindRanked", "ИСКАТЬ РЕЙТИНГОВЫЙ БОЙ", "FIND RANKED FIGHT" },
            new[] { "HmSearching", "Поиск: {0} · {1}:{2:00} — нажми, чтобы отменить", "Searching: {0} · {1}:{2:00} — click to cancel" },
            new[] { "HmSurrender", "СДАТЬСЯ И ВЫЙТИ", "SURRENDER AND LEAVE" },
            new[] { "HmInMatch", "Ты уже в вертолётном матче", "You are already in a heli match" }, new[] { "HmAlready", "Ты уже в поиске", "You are already searching" },
            new[] { "HmNoMap", "Нет карты для вертолётов (/mgadmin create <id> heli)", "No heli map (/mgadmin create <id> heli)" },
            new[] { "HmPartyBig", "В пати больше {0} — для этого формата слишком много", "Party is bigger than {0}" }, new[] { "HmPartyBusy", "Кто-то из пати уже в поиске или в матче", "Someone in your party is busy" },
            new[] { "HmQueued", "Поиск: {0}", "Searching: {0}" }, new[] { "HmLeftQueue", "Поиск отменён", "Search cancelled" },
            new[] { "HmFound", "Бой найден: {0}", "Fight found: {0}" }, new[] { "HmRankedTag", "рейтинговый", "ranked" }, new[] { "HmRankedShort", "рейтинг", "ranked" },
            new[] { "HmRound", "РАУНД {0}", "ROUND {0}" }, new[] { "HmBoard", "садись в машину — скоро старт", "board your vehicle — starting soon" }, new[] { "HmGo", "В БОЙ!", "FIGHT!" },
            new[] { "HmStartsIn", "старт раунда через {0}", "round starts in {0}" }, new[] { "HmNextIn", "следующий раунд через {0}", "next round in {0}" }, new[] { "HmRoundN", "раунд {0} · до {1} побед", "round {0} · first to {1}" },
            new[] { "HmUs", "Мы", "Us" }, new[] { "HmThem", "Они", "Them" }, new[] { "HmAliveVeh", "в строю: {0} · машин: {1}", "alive: {0} · vehicles: {1}" },
            new[] { "HmRoundWin", "РАУНД ВАШ", "ROUND WON" }, new[] { "HmRoundLose", "РАУНД ПРОИГРАН", "ROUND LOST" }, new[] { "HmDraw", "НИЧЬЯ", "DRAW" },
            new[] { "HmWin", "ПОБЕДА", "VICTORY" }, new[] { "HmLose", "ПОРАЖЕНИЕ", "DEFEAT" }, new[] { "HmForfeit", "соперник вышел", "opponent left" },
            new[] { "HmCoins", "вертолётный бой", "heli fight" },
            new[] { "HmOursDown", "Наша машина сбита!", "Our vehicle is down!" }, new[] { "HmTheirsDown", "Машина соперника сбита!", "Enemy vehicle down!" },
            new[] { "Type_roam", "Роум", "Roam" }, new[] { "Type_heli", "Хели-бои", "Heli battles" }, new[] { "Type_bedwars", "Bedwars", "Bedwars" }, new[] { "Type_raidwars", "Рейд против рейда", "Raid wars" },
            new[] { "Icon_roam", "R", "R" }, new[] { "Icon_heli", "H", "H" }, new[] { "Icon_bedwars", "B", "B" }, new[] { "Icon_raidwars", "W", "W" },
            new[] { "TypeSub_roam", "открытая зона · транспорт · лут с трупов · /mg roam", "open zone · vehicles · loot · /mg roam" },
            new[] { "TypeSub_heli", "атак-хели · пилот + стрелок · /mg heli", "attack heli · pilot + gunner · /mg heli" },
            new[] { "TypeSub_bedwars", "острова · кровати · генераторы · магазин /shopbw", "islands · beds · generators · /shopbw" },
            new[] { "TypeSub_raidwars", "строим, потом рейдим друг друга · /mg raidwars", "build, then raid each other" },
            new[] { "ChooseKit", "ВЫБЕРИ КИТ", "CHOOSE A KIT" }, new[] { "ChooseVeh", "ТРАНСПОРТ НА СТАРТЕ", "STARTING VEHICLE" },
            new[] { "Veh_none", "Пешком", "On foot" }, new[] { "Veh_car", "Машина", "Car" }, new[] { "Veh_horse", "Лошадь", "Horse" }, new[] { "Veh_mini", "Миникоптер", "Minicopter" },
            new[] { "RoamInfo", "Сейчас в роуме: {0}. Лучшие по убийствам: {1}\nЛут с трупов твой. Своя команда Rust (Tab) не получает урон друг от друга. Транспорт без водителя исчезает через 4 минуты.", "Players in roam: {0}. Top killers: {1}" },
            new[] { "ToBattle", "В БОЙ", "TO BATTLE" }, new[] { "Respawn", "НОВЫЙ КИТ И ТРАНСПОРТ", "NEW KIT & VEHICLE" }, new[] { "LeaveBtn", "ВЫЙТИ", "LEAVE" },
            new[] { "JoinQueue", "ВСТАТЬ В ОЧЕРЕДЬ", "JOIN QUEUE" },
            new[] { "QueueTitle", "ОЧЕРЕДЬ · {0} (минимум {1}, максимум {2})", "QUEUE · {0} (min {1}, max {2})" },
            new[] { "QueueEmpty", "Очередь пуста — встань первым", "Queue is empty — be first" },
            new[] { "Running", "Сейчас идёт матчей: {0}", "Matches running: {0}" },
            new[] { "StartsIn", "Старт через {0} с", "Starting in {0}s" }, new[] { "StartingIn", "Набралось игроков — старт через {0} с", "Enough players — starting in {0}s" },
            new[] { "QueueJoined", "{0} в очереди ({1}/{2})", "{0} queued ({1}/{2})" },
            new[] { "Rules_heli", "• Две команды, на площадках стоят атак-хели.\n• Садись пилотом или стрелком (E у двери).\n• Очко — за каждого убитого врага.\n• Сбитый хели появится снова через 15 с.\n• За границей зоны и выше потолка хели получает урон.\n• Победа — первыми набрать нужное число убийств или больше к концу времени.", "• Two teams with attack helis\n• Pilot or gunner\n• Point per kill\n• Helis respawn in 15s\n• Leaving the zone damages the heli" },
            new[] { "Rules_bedwars", "• 2–4 команды, у каждой свой остров и кровать.\n• Пока кровать цела — ты возрождаешься.\n• Генератор острова даёт дерево и металл, центр — скрап.\n• /shopbw — магазин: блоки, оружие, броня, взрывчатка.\n• Сломай чужие кровати и добей команды.\n• Последняя живая команда побеждает.", "• 2–4 teams, each with an island and a bed\n• Respawn while your bed stands\n• Generators give resources, /shopbw to buy\n• Last team standing wins" },
            new[] { "Rules_raidwars", "• Фаза 1 — стройка: у команды своя зона и ресурсы. Ставь шкаф, двери, замки. Урона нет.\n• Фаза 2 — рейд: выдаётся рейд-набор (C4, ракеты, АК).\n• Цель — уничтожить шкаф противника.\n• Не уничтожили — побеждает тот, у кого уцелела большая часть базы.\n• После матча всё построенное убирается.", "• Phase 1: build in your zone\n• Phase 2: raid kit, destroy the enemy cupboard\n• On timeout, the better-preserved base wins" },
            new[] { "NoMap", "Для этого режима ещё нет карты (/mgadmin)", "No map for this mode yet" },
            new[] { "RoamJoined", "Ты в роуме! /mg — сменить кит или транспорт", "You're in roam! /mg to change kit or vehicle" },
            new[] { "Players", "игроков", "players" }, new[] { "YourKills", "убийств: {0}", "kills: {0}" }, new[] { "MenuHint", "/mg — меню", "/mg — menu" },
            new[] { "Waiting", "ждём игроков {0}/{1}", "waiting {0}/{1}" },
            new[] { "BuildPhase", "СТРОЙКА · {0}:{1:00}", "BUILD · {0}:{1:00}" }, new[] { "RaidPhaseHud", "РЕЙД · {0}:{1:00} · цель — шкаф врага", "RAID · {0}:{1:00} · destroy their cupboard" },
            new[] { "HeliHud", "до {0} убийств · {1}:{2:00}", "to {0} kills · {1}:{2:00}" }, new[] { "BwHud", "сломай чужие кровати", "break the enemy beds" },
            new[] { "BedOk", "кровать", "bed" }, new[] { "BedLost", "без кровати", "no bed" },
            new[] { "RaidPhase", "РЕЙД! Ломай шкаф противника", "RAID! Destroy their cupboard" },
            new[] { "BedDestroyed", "Кровать <color={0}>{1}</color> уничтожена · {2}", "<color={0}>{1}</color> bed destroyed · {2}" },
            new[] { "Eliminated", "Кровати нет — ты выбыл", "No bed — you're out" },
            new[] { "Victory", "ПОБЕДА", "VICTORY" }, new[] { "Defeat", "ПОРАЖЕНИЕ", "DEFEAT" }, new[] { "Draw", "НИЧЬЯ", "DRAW" }, new[] { "Forfeit", "соперники вышли", "opponents left" },
            new[] { "ForWin", "за победу", "for the win" },
            new[] { "Border", "Граница зоны", "Zone border" }, new[] { "OwnZone", "Только своя зона", "Your zone only" },
            new[] { "NoBuildRaid", "В фазе рейда строить нельзя", "No building during the raid" },
            new[] { "HeliBorder", "Граница зоны — хели получает урон!", "Zone border — the heli is taking damage!" },
            new[] { "Ceiling", "Слишком высоко — снижайся!", "Too high — descend!" },
            new[] { "InCombat", "Нельзя: ты в бою", "Can't: you're in combat" },
            new[] { "BwShop", "МАГАЗИН ОСТРОВА", "ISLAND SHOP" }, new[] { "BwShopSub", "валюта — с генераторов · /shopbw", "currency comes from generators · /shopbw" },
            new[] { "NotBedwars", "Магазин работает только в Bedwars", "Shop works only in Bedwars" },
            new[] { "NotEnough", "Нужно {0} {1}", "You need {0} {1}" },
            new[] { "CurWood", "дерево", "wood" }, new[] { "CurMetal", "металл", "metal" }, new[] { "CurScrap", "скрап", "scrap" },
            new[] { "NoPerm", "Нет прав", "No permission" }, new[] { "AdmOk", "Сохранено", "Saved" },
            new[] { "AdminHelp",
                "<color=#f0a6b1>/mgadmin</color> (стой на месте точки)\n" +
                "create <id> <roam|heli|bedwars|raidwars> [радиус] — карта, центр здесь\n" +
                "spawn <id> <команда 0-3> — точка появления (роум: всегда 0)\n" +
                "pad <id> — площадка хели (хели) / точка транспорта\n" +
                "bed <id> <команда> · gen <id> <команда> · centergen <id> · teams <id> <2-4> — Bedwars\n" +
                "zone <id> <команда> [радиус] — зона базы (рейд)\n" +
                "radius <id> <м> · clear · delete · enable · disable · list · start · stop",
                "<color=#f0a6b1>/mgadmin</color> create · spawn · pad · bed · gen · centergen · teams · zone · radius · clear · delete · list · start · stop" },
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
