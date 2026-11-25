// =====================================================================================
//  BuildServer 1.0 — всё для билд/креатив-сервера Rust (Oxide / Carbon)
// =====================================================================================
//  Полёт и бессмертие, бесплатные предметы из меню, мгновенное улучшение/понижение/ремонт
//  всей постройки, режим удаления, авто-шкаф и авто-замки, рейд-тест с откатом базы,
//  личные участки, дома/телепорты/телепорт по метке на карте, свой день/ночь,
//  набор строителя при спавне. Встроенный креатив Rust включается из конфига.
//
//  1.1: окно в стиле хаба — инструменты (симметрия, стаки стен/полов, кисть фундаментов,
//  улучшение с фильтром, авто-двери/окна/люки, турели, печи, undo/redo, сносы, транспорт),
//  библиотека построек (Blueprints), зоны-биомы с VIP, окружение (погода + своё время).
//  1.2: HUD — цена постройки / апкип / рейд-калькулятор справа сверху, панель материала
//  и скина внизу (новые блоки сразу ставятся выбранным материалом и скином).
//
//  Рейд-тест с откатом использует CopyPaste (umod.org/plugins/copy-paste), остальное — нет.
//  Вместе с ним хорошо работают Blueprints (чертежи баз) и VideoRecorder (съёмка).
//
//  Права:  buildserver.use    — меню /build и базовые функции
//          buildserver.fly    — полёт (noclip)
//          buildserver.god    — бессмертие
//          buildserver.items  — бесплатные предметы
//          buildserver.admin  — без ограничений участков, чужие постройки
//          buildserver.weather — менять погоду (она общая на сервер!)
//  Админ:  /bsadmin zone|slot|grid|zones|delzone — зоны-биомы и точки участков
//  Чат:    /build (или /b) — меню · /fly · /god · /up <0-4> · /down · /remove · /tpm
//          /home [имя] · /sethome <имя> · /tpr <ник> · /tpa · /back · /day · /night
//          /plot claim|unclaim|add <ник>|remove <ник> · /raidtest start|restore
// =====================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Network;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("BuildServer", "Claude", "1.3.1")]
    [Description("Набор функций билд/креатив-сервера: полёт, предметы, стройка, рейд-тест, участки, телепорты")]
    public class BuildServer : RustPlugin
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
        [PluginReference] private Plugin CopyPaste, Blueprints, Hub;

        #region Конфиг

        private const string PermUse = "buildserver.use";
        private const string PermFly = "buildserver.fly";
        private const string PermGod = "buildserver.god";
        private const string PermItems = "buildserver.items";
        private const string PermAdmin = "buildserver.admin";
        private const string PermWeather = "buildserver.weather";
        private const string PermVip = "shop.vip";
        private const string UiMain = "BS.Main";
        private const string UiToast = "BS.Toast";
        private const string UiPopup = "BS.Popup";
        private const string CodeLockPrefab = "assets/prefabs/locks/keypad/lock.code.prefab";

        private Configuration _config;

        private class KitEntry
        {
            [JsonProperty("Предмет")] public string Item;
            [JsonProperty("Кол-во")] public int Amount = 1;
        }

        private class Configuration
        {
            [JsonProperty("Встроенный креатив: включить при старте")] public bool CreativeOnStart = true;
            [JsonProperty("Встроенный креатив: для всех игроков (creative.allusers)")] public bool CreativeAllUsers = true;
            [JsonProperty("Встроенный креатив: строить бесплатно")] public bool FreeBuild = true;
            [JsonProperty("Встроенный креатив: без проверок установки")] public bool FreePlacement = false;
            [JsonProperty("Встроенный креатив: ремонт без кулдауна")] public bool FreeRepair = true;
            [JsonProperty("Встроенный креатив: без лимитов электрики")] public bool UnlimitedIO = true;
            [JsonProperty("Отключить гниение построек (decay.scale 0)")] public bool NoDecay = true;
            [JsonProperty("Бесконечные ресурсы: креатив Rust игрокам с buildserver.use (стройка, улучшение, ремонт, электрика без ресурсов)")] public bool PerPlayerCreative = true;

            [JsonProperty("Полёт: давать флаг админа на время полёта (нужен клиенту для noclip)")] public bool FlyWithAdminFlag = true;
            [JsonProperty("Авто-авторизация в своём шкафу")] public bool AutoAuthCupboard = true;
            [JsonProperty("Авто-кодовый замок на двери")] public bool AutoCodeLock = true;
            [JsonProperty("Код авто-замка по умолчанию")] public string DefaultCode = "1234";
            [JsonProperty("Набор строителя при спавне")] public bool SpawnKit = true;
            [JsonProperty("Набор строителя")]
            public List<KitEntry> BuilderKit = new List<KitEntry>
            {
                new KitEntry { Item = "building.planner" }, new KitEntry { Item = "hammer" },
                new KitEntry { Item = "cupboard.tool" }, new KitEntry { Item = "wiretool" },
                new KitEntry { Item = "door.hinged.metal", Amount = 5 }, new KitEntry { Item = "lock.code", Amount = 5 },
            };
            [JsonProperty("Рейд-тест: набор")]
            public List<KitEntry> RaidKit = new List<KitEntry>
            {
                new KitEntry { Item = "rocket.launcher" }, new KitEntry { Item = "ammo.rocket.basic", Amount = 24 },
                new KitEntry { Item = "explosive.timed", Amount = 20 }, new KitEntry { Item = "explosive.satchel", Amount = 30 },
                new KitEntry { Item = "rifle.ak" }, new KitEntry { Item = "ammo.rifle.explosive", Amount = 256 },
            };
            [JsonProperty("Рейд с другом: минуты на выбор")] public List<int> FriendRaidMinutes = new List<int> { 5, 10, 15, 30 };
            [JsonProperty("Рейд с другом: выдать другу рейд-набор")] public bool FriendRaidKit = true;
            [JsonProperty("Рейд с другом: по окончании вернуть базу")] public bool FriendRaidAutoRestore = true;
            [JsonProperty("Спавн: предел объектов у игрока")] public int SpawnLimit = 40;
            [JsonProperty("Спавн: объекты")] public List<SpawnDef> Spawns = DefaultSpawns();
            [JsonProperty("Электрика: радиус копирования и подсчёта (м)")] public float ElecCopyRadius = 6f;
            [JsonProperty("Электрика: радиус показа нагрузки (м)")] public float ElecViewRadius = 15f;
            [JsonProperty("Электрика: своих схем у игрока")] public int ElecSlots = 5;
            [JsonProperty("Электрика: библиотека схем")] public List<Circuit> Circuits = DefaultCircuits();
            [JsonProperty("Участки: радиус по умолчанию (м)")] public float PlotRadius = 40f;
            [JsonProperty("Участки: максимум у игрока")] public int MaxPlots = 2;
            [JsonProperty("Участки: защита от урона чужим")] public bool PlotDamageProtection = true;
            [JsonProperty("Дома: максимум точек")] public int MaxHomes = 5;
            [JsonProperty("Телепорт по метке на карте (для всех с правом)")] public bool MapMarkerTeleport = true;
            [JsonProperty("Запрос телепорта действует (сек)")] public float TprTimeout = 30f;
            [JsonProperty("Скруглённые углы интерфейса")] public bool RoundedCorners = true;
            [JsonProperty("Материалы и скины для панели внизу")] public List<GradeSkin> Skins = DefaultSkins();
            [JsonProperty("Транспорт (до 12)")] public List<VehicleEntry> Vehicles = DefaultVehicles();
            [JsonProperty("Зоны-биомы")]
            public List<ZoneEntry> Zones = new List<ZoneEntry>
            {
                new ZoneEntry { Key = "snow", Ru = "Снег", En = "Snow", Biome = "snow", Size = 46f },
                new ZoneEntry { Key = "grass", Ru = "Трава", En = "Grass", Biome = "grass", Size = 46f },
                new ZoneEntry { Key = "sand", Ru = "Песок", En = "Sand", Biome = "sand", Size = 46f },
                new ZoneEntry { Key = "vsnow", Ru = "Снег", En = "Snow", Biome = "snow", Size = 100f, Vip = true, Tags = "Скала" },
                new ZoneEntry { Key = "vgrass", Ru = "Трава", En = "Grass", Biome = "grass", Size = 100f, Vip = true, Tags = "Скала" },
                new ZoneEntry { Key = "vsand", Ru = "Песок", En = "Sand", Biome = "sand", Size = 100f, Vip = true },
                new ZoneEntry { Key = "lake", Ru = "Озеро", En = "Ice lake", Biome = "lake", Size = 80f, Vip = true },
                new ZoneEntry { Key = "cave", Ru = "Пещера", En = "Cave", Biome = "cave", Size = 60f, Vip = true },
                new ZoneEntry { Key = "swamp", Ru = "Болото", En = "Swamp", Biome = "swamp", Size = 80f, Vip = true },
            };
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                Config.Settings.ObjectCreationHandling = ObjectCreationHandling.Replace; _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
                if (_config.Spawns == null || _config.Spawns.Count == 0) _config.Spawns = DefaultSpawns();
                if (_config.Circuits == null || _config.Circuits.Count == 0) _config.Circuits = DefaultCircuits();
                if (_config.FriendRaidMinutes == null || _config.FriendRaidMinutes.Count == 0) _config.FriendRaidMinutes = new List<int> { 5, 10, 15, 30 };
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

        private class Home
        {
            public string Name;
            public float X, Y, Z;
        }

        private class Plot
        {
            public ulong Owner;
            public string OwnerName;
            public string Name;
            public float X, Y, Z, Radius;
            public string Zone;
            public bool Private;
            public bool Pvp;                 // хозяин включил PvP: на участке бьют всех, бессмертие не действует
            public List<ulong> Members = new List<ulong>();
        }

        private class PlayerSettings
        {
            public bool God = true;
            public bool GodByAdmin;   // бессмертие включил админ (/god ник) — действует и без права buildserver.god
            public bool RemoveMode;
            public bool AutoLock = true;
            public bool AutoAuth = true;
            public bool MapTp = true;
            public bool Kit = true;
            public string Code;
            public float Time = -1f;
            public int ItemAmount = 1;
            public List<Home> Homes = new List<Home>();
            public ToolSettings Tools = new ToolSettings();
        }

        private class StoredData
        {
            public Dictionary<ulong, PlayerSettings> Players = new Dictionary<ulong, PlayerSettings>();
            public List<Plot> Plots = new List<Plot>();
            public Dictionary<string, float> Env = new Dictionary<string, float>();
        }

        private StoredData _data;
        private bool _dirty;

        private void LoadData()
        {
            try { _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("BuildServer"); }
            catch { _data = null; }
            if (_data == null) _data = new StoredData();
            if (_data.Players == null) _data.Players = new Dictionary<ulong, PlayerSettings>();
            if (_data.Plots == null) _data.Plots = new List<Plot>();
            if (_data.Env == null) _data.Env = new Dictionary<string, float>();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("BuildServer", _data);
            _dirty = false;
        }

        private PlayerSettings P(ulong id)
        {
            PlayerSettings s;
            if (!_data.Players.TryGetValue(id, out s)) { _data.Players[id] = s = new PlayerSettings(); _dirty = true; }
            if (s.Homes == null) s.Homes = new List<Home>();
            if (s.Tools == null) s.Tools = new ToolSettings();
            return s;
        }

        #endregion

        #region Общее

        private static ulong Id(BasePlayer p) => (ulong)p.userID;

        private bool Has(BasePlayer p, string perm) => p != null && (p.IsAdmin || permission.UserHasPermission(p.UserIDString, perm));

        private bool IsAdminLike(BasePlayer p) => Has(p, PermAdmin);

        private static object TryCall(object target, string method, params object[] args)
        {
            if (target == null) return null;
            foreach (var m in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != method) continue;
                var ps = m.GetParameters();
                if (ps.Length < args.Length) continue;
                bool ok = true;
                var full = new object[ps.Length];
                for (int i = 0; i < ps.Length && ok; i++)
                {
                    if (i < args.Length)
                    {
                        if (args[i] != null && !ps[i].ParameterType.IsInstanceOfType(args[i])) ok = false;
                        else full[i] = args[i];
                    }
                    else if (ps[i].IsOptional) full[i] = ps[i].DefaultValue == DBNull.Value ? null : ps[i].DefaultValue;
                    else ok = false;
                }
                if (!ok) continue;
                try { return m.Invoke(target, full) ?? true; } catch { return null; }
            }
            return null;
        }

        private static void ServerCmd(string cmd, string value) => ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), cmd, value);

        private static void WithAdmin(BasePlayer player, Action action)
        {
            bool wasAdmin = player.IsAdmin;
            if (!wasAdmin)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                player.SendNetworkUpdateImmediate();
            }
            try { action(); }
            finally
            {
                if (!wasAdmin)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                    player.SendNetworkUpdateImmediate();
                }
            }
        }

        private BaseEntity LookEntity(BasePlayer player, float dist = 12f)
        {
            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, dist, LayerMask.GetMask("Construction", "Deployed", "Default"), QueryTriggerInteraction.Ignore))
                return null;
            return hit.GetEntity();
        }

        private bool CanModify(BasePlayer player, BaseEntity e)
        {
            if (e == null) return false;
            if (IsAdminLike(player) || e.OwnerID == Id(player) || e.OwnerID == 0) return true;
            var priv = e.GetBuildingPrivilege();
            return priv != null && priv.IsAuthed(player);
        }

        private static void Give(BasePlayer player, string shortname, int amount)
        {
            var item = ItemManager.CreateByName(shortname, Mathf.Max(1, amount), 0UL);
            if (item != null) player.GiveItem(item);
        }

        #endregion

        #region Хуки

        private void Init()
        {
            foreach (var p in new[] { PermUse, PermFly, PermGod, PermItems, PermAdmin, PermWeather }) permission.RegisterPermission(p, this);
            LoadData();
        }

        private Timer _tick, _save;

        private void OnServerInitialized()
        {
            RegisterLang();
            InitRoundedSprite();
            if (_config.CreativeOnStart)
            {
                ServerCmd("creative.allusers", _config.CreativeAllUsers ? "true" : "false");
                ServerCmd("creative.freebuild", _config.FreeBuild ? "true" : "false");
                ServerCmd("creative.freeplacement", _config.FreePlacement ? "true" : "false");
                ServerCmd("creative.freerepair", _config.FreeRepair ? "true" : "false");
                ServerCmd("creative.unlimitedio", _config.UnlimitedIO ? "true" : "false");
            }
            if (_config.NoDecay) ServerCmd("decay.scale", "0");
            if (_config.Vehicles == null || _config.Vehicles.Count == 0) { _config.Vehicles = DefaultVehicles(); SaveConfig(); }
            if (_config.Zones == null) _config.Zones = new List<ZoneEntry>();
            ApplyEnv();
            if (_config.Skins == null || _config.Skins.Count == 0) { _config.Skins = DefaultSkins(); SaveConfig(); }
            timer.Every(2f, HudTick);
            foreach (var p in BasePlayer.activePlayerList) RefreshHud(p);
            _tick = timer.Every(1f, Tick);
            timer.Every(1f, SyncCreative);
            _save = timer.Every(60f, () => { if (_dirty) SaveData(); });
        }

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList) if (p.HasPlayerFlag(BasePlayer.PlayerFlags.CreativeMode)) p.SetPlayerFlag(BasePlayer.PlayerFlags.CreativeMode, false);
            foreach (var id in _flying.ToList())
            {
                var p = BasePlayer.FindByID(id);
                if (p != null) StopFly(p, false);
            }
            foreach (var p in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(p, UiMain);
                CuiHelper.DestroyUi(p, UiToast);
                CuiHelper.DestroyUi(p, UiPopup);
                CuiHelper.DestroyUi(p, UiCost);
                CuiHelper.DestroyUi(p, UiBar);
            }
            SaveData();
        }

        // Креатив Rust (бесплатная стройка/улучшение/ремонт) только тем, у кого сейчас buildserver.use — Hub выдаёт его в «Стройке»
        private void SyncCreative()
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                bool want = _config.PerPlayerCreative && permission.UserHasPermission(p.UserIDString, PermUse);
                if (p.HasPlayerFlag(BasePlayer.PlayerFlags.CreativeMode) != want) p.SetPlayerFlag(BasePlayer.PlayerFlags.CreativeMode, want);
            }
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            var s = P(Id(player));
            if (s.Time >= 0f) timer.Once(3f, () => { if (player != null && player.IsConnected) SetClientTime(player, s.Time); });
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (_flying.Contains(Id(player))) StopFly(player, false);
            _currentPlot.Remove(Id(player));
            _barShown.Remove(Id(player));
            _hudKey.Remove(Id(player));
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (!_config.SpawnKit || !Has(player, PermUse) || !P(Id(player)).Kit) return;
            foreach (var k in _config.BuilderKit) Give(player, k.Item, k.Amount);
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null) return null;
            var victim = entity as BasePlayer;
            var atk0 = info?.InitiatorPlayer;
            // PvP-участок: если и жертва, и стрелок внутри — урон проходит, бессмертие не действует
            if (victim != null && atk0 != null && atk0 != victim)
            {
                var vp = PlotAt(victim.transform.position);
                if (vp != null && vp.Pvp && PlotAt(atk0.transform.position) == vp) return null;
            }
            if (victim != null && (ulong)victim.userID > 76561197960265728UL && P(Id(victim)).God && (Has(victim, PermGod) || P(Id(victim)).GodByAdmin))
                return true;

            if (!_config.PlotDamageProtection || victim != null) return null;
            var attacker = info?.InitiatorPlayer;
            var plot = PlotAt(entity.transform.position);
            if (plot == null) return null;
            if (FriendMayHit(plot, attacker)) return null;
            if (attacker == null)
            {
                // взрывчатка/огонь без игрока — защищаем, если не идёт рейд-тест владельца или рейд друга
                return _raidTests.ContainsKey(plot.Owner) || _friendRaids.ContainsKey(plot.Owner) ? null : (object)true;
            }
            if (IsMember(plot, attacker) || IsAdminLike(attacker)) return null;
            return true;
        }

        private object CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            var player = planner?.GetOwnerPlayer();
            if (player == null) return null;
            var pos = target.entity != null ? target.entity.transform.position : target.position;
            var plot = PlotAt(pos);
            if (plot == null || IsMember(plot, player) || IsAdminLike(player)) return null;
            Toast(player, T(Id(player), "PlotNoBuild", plot.OwnerName ?? "?"), true);
            return false;
        }

        private void OnEntityBuilt(Planner planner, GameObject go)
        {
            var player = planner?.GetOwnerPlayer();
            var entity = go?.ToBaseEntity();
            if (player == null || entity == null) return;
            var s = P(Id(player));

            if (_config.AutoAuthCupboard && s.AutoAuth && entity is BuildingPrivlidge)
                NextTick(() => { if (entity != null && !entity.IsDestroyed) TryCall(entity, "AddPlayer", player); });

            var door = entity as Door;
            if (door != null && _config.AutoCodeLock && s.AutoLock)
                NextTick(() => AddCodeLock(player, door));

            if (Has(player, PermUse)) ProcessBuilt(player, entity);
        }

        private void AddCodeLock(BasePlayer player, Door door)
        {
            if (door == null || door.IsDestroyed || door.GetSlot(BaseEntity.Slot.Lock) != null || !door.HasSlot(BaseEntity.Slot.Lock)) return;
            var lockEnt = GameManager.server.CreateEntity(CodeLockPrefab, Vector3.zero, Quaternion.identity) as CodeLock;
            if (lockEnt == null) return;
            lockEnt.gameObject.Identity();
            lockEnt.SetParent(door, door.GetSlotAnchorName(BaseEntity.Slot.Lock));
            lockEnt.OwnerID = Id(player);
            lockEnt.Spawn();
            door.SetSlot(BaseEntity.Slot.Lock, lockEnt);
            lockEnt.code = string.IsNullOrEmpty(P(Id(player)).Code) ? _config.DefaultCode : P(Id(player)).Code;
            lockEnt.whitelistPlayers.Add(Id(player));
            using (var lockFlags = lockEnt.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate)) lockFlags.Set(BaseEntity.Flags.Locked, true);
            lockEnt.SendNetworkUpdate();
        }

        private object OnHammerHit(BasePlayer player, HitInfo info)
        {
            if (player == null || info?.HitEntity == null || !P(Id(player)).RemoveMode) return null;
            var e = info.HitEntity;
            if (e is BasePlayer) return null;
            if (!CanModify(player, e)) { Toast(player, T(Id(player), "NotYours"), true); return true; }
            e.Kill(BaseNetworkable.DestroyMode.Gib);
            return true;
        }

        private void OnMapMarkerAdded(BasePlayer player, ProtoBuf.MapNote note)
        {
            if (player == null || note == null || !_config.MapMarkerTeleport || !Has(player, PermUse) || !P(Id(player)).MapTp) return;
            var pos = note.worldPosition;
            pos.y = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(pos) + 1f : pos.y + 1f;
            DoTeleport(player, pos);
            Toast(player, T(Id(player), "TpMarker"), false);
        }

        #endregion

        #region Полёт и бессмертие

        private readonly HashSet<ulong> _flying = new HashSet<ulong>();
        private readonly Dictionary<ulong, bool> _wasAdmin = new Dictionary<ulong, bool>();

        private void ToggleFly(BasePlayer player)
        {
            if (!Has(player, PermFly)) { Reply(player, "NoPerm"); return; }
            if (_flying.Contains(Id(player))) StopFly(player, true);
            else StartFly(player);
        }

        private void StartFly(BasePlayer player)
        {
            var id = Id(player);
            _flying.Add(id);
            _wasAdmin[id] = player.IsAdmin;
            if (_config.FlyWithAdminFlag && !player.IsAdmin)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                player.SendNetworkUpdateImmediate();
            }
            player.SendConsoleCommand("noclip");
            Toast(player, T(id, "FlyOn"), false);
        }

        private void StopFly(BasePlayer player, bool reply)
        {
            var id = Id(player);
            if (!_flying.Remove(id)) return;
            player.SendConsoleCommand("noclip");
            bool was;
            if (_config.FlyWithAdminFlag && _wasAdmin.TryGetValue(id, out was) && !was)
            {
                timer.Once(1.5f, () =>
                {
                    if (player == null || _flying.Contains(id)) return;
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                    player.SendNetworkUpdateImmediate();
                });
            }
            _wasAdmin.Remove(id);
            if (reply) Toast(player, T(id, "FlyOff"), false);
        }

        private void Tick()
        {
            foreach (var id in _flying.ToList())
            {
                var p = BasePlayer.FindByID(id);
                if (p == null || !p.IsConnected) { _flying.Remove(id); continue; }
                TryCall(p, "PauseFlyHackDetection", 3f);
                TryCall(p, "PauseSpeedHackDetection", 3f);
                TryCall(p, "PauseVehicleNoClipDetection", 3f);
            }

            foreach (var p in BasePlayer.activePlayerList)
            {
                var id = Id(p);
                if ((Has(p, PermGod) || P(id).GodByAdmin) && P(id).God && p.metabolism != null)
                {
                    p.metabolism.calories.value = p.metabolism.calories.max;
                    p.metabolism.hydration.value = p.metabolism.hydration.max;
                    p.metabolism.radiation_poison.value = 0f;
                    p.metabolism.bleeding.value = 0f;
                }
                var plot = PlotAt(p.transform.position);
                Plot was;
                _currentPlot.TryGetValue(id, out was);
                if (plot != was)
                {
                    _currentPlot[id] = plot;
                    if (plot != null)
                        Toast(p, IsMember(plot, p) ? T(id, "PlotEnterOwn", plot.Name) : T(id, "PlotEnter", plot.Name, plot.OwnerName), false);
                    else if (was != null) Toast(p, T(id, "PlotLeave"), false);
                }
                if (plot != null && plot.Private && !IsMember(plot, p) && !IsAdminLike(p)) PushOutOfPlot(p, plot);
            }

            // просроченные запросы телепорта
            foreach (var kv in _tpRequests.Where(x => Time.realtimeSinceStartup - x.Value.At > _config.TprTimeout).ToList())
            {
                _tpRequests.Remove(kv.Key);
                var t = BasePlayer.FindByID(kv.Key);
                if (t != null) CuiHelper.DestroyUi(t, UiPopup);
            }
        }

        private void SetClientTime(BasePlayer player, float hour)
        {
            WithAdmin(player, () => player.SendConsoleCommand("admintime", hour));
        }

        #endregion

        #region Стройка

        private static readonly string[] GradeKeys = { "G0", "G1", "G2", "G3", "G4" };

        private BuildingManager.Building LookBuilding(BasePlayer player, out BaseEntity looked)
        {
            looked = LookEntity(player);
            var dec = looked as DecayEntity;
            if (dec == null) return null;
            return dec.GetBuilding();
        }

        private void ChangeBuildingGrade(BasePlayer player, int grade)
        {
            BaseEntity looked;
            var building = BuildingNear(player, out looked);
            if (building == null) { Reply(player, "LookAtBuilding"); return; }
            if (!CanModify(player, looked)) { Reply(player, "NotYours"); return; }
            int n = 0;
            foreach (var block in building.buildingBlocks.ToList())
            {
                if (block == null || block.IsDestroyed || !MatchFilter(block, P(Id(player)).Tools.UpFilter)) continue;
                var g = (BuildingGrade.Enum)Mathf.Clamp(grade, 0, 4);
                if (TryCall(block, "ChangeGrade", g, false, true) == null) block.SetGrade(g);
                block.SetHealth(block.MaxHealth());
                block.SendNetworkUpdate();
                n++;
            }
            Toast(player, T(Id(player), "Upgraded", n, T(Id(player), GradeKeys[Mathf.Clamp(grade, 0, 4)])), false);
        }

        private void RepairBuilding(BasePlayer player)
        {
            BaseEntity looked;
            var building = LookBuilding(player, out looked);
            if (building == null) { Reply(player, "LookAtBuilding"); return; }
            int n = 0;
            foreach (var e in building.buildingBlocks.Cast<BaseCombatEntity>().Concat(building.decayEntities.Cast<BaseCombatEntity>()).Distinct().ToList())
            {
                if (e == null || e.IsDestroyed) continue;
                e.SetHealth(e.MaxHealth());
                e.SendNetworkUpdate();
                n++;
            }
            Toast(player, T(Id(player), "Repaired", n), false);
        }

        private void RemoveBuilding(BasePlayer player)
        {
            BaseEntity looked;
            var building = LookBuilding(player, out looked);
            if (building == null) { Reply(player, "LookAtBuilding"); return; }
            if (!CanModify(player, looked)) { Reply(player, "NotYours"); return; }
            var all = building.decayEntities.Cast<BaseEntity>().Concat(building.buildingBlocks.Cast<BaseEntity>()).Distinct().ToList();
            foreach (var e in all) if (e != null && !e.IsDestroyed) e.Kill();
            Toast(player, T(Id(player), "Removed", all.Count), false);
        }

        private void ToggleRemove(BasePlayer player)
        {
            var s = P(Id(player));
            s.RemoveMode = !s.RemoveMode;
            _dirty = true;
            Toast(player, s.RemoveMode ? T(Id(player), "RemoveOn") : T(Id(player), "RemoveOff"), s.RemoveMode);
        }

        #endregion

        #region Рейд-тест с откатом

        private class RaidTest
        {
            public string File;
            public HashSet<ulong> Ids = new HashSet<ulong>();
            public int Total;
        }

        private readonly Dictionary<ulong, RaidTest> _raidTests = new Dictionary<ulong, RaidTest>();

        private void RaidStart(BasePlayer player)
        {
            var id = Id(player);
            if (CopyPaste == null || !CopyPaste.IsLoaded) { Reply(player, "NoCopyPaste"); return; }
            BaseEntity looked;
            var building = LookBuilding(player, out looked);
            if (building == null) { Reply(player, "LookAtBuilding"); return; }
            if (!CanModify(player, looked)) { Reply(player, "NotYours"); return; }

            var test = new RaidTest { File = "bs_raidtest_" + id };
            foreach (var e in building.decayEntities.Cast<BaseEntity>().Concat(building.buildingBlocks.Cast<BaseEntity>()))
                if (e != null && e.net != null) test.Ids.Add(e.net.ID.Value);
            test.Total = test.Ids.Count;

            var args = new[] { "method", "building", "deployables", "true", "inventories", "true" };
            object result = CopyPaste.Call("TryCopyFromSteamId", id, test.File, args, (Action)(() => { }));
            if (result is string) { Reply(player, "CopyError", (string)result); return; }

            _raidTests[id] = test;
            foreach (var k in _config.RaidKit) Give(player, k.Item, k.Amount);
            Reply(player, "RaidStarted", test.Total);
            CloseUi(player);
        }

        private void RaidRestore(BasePlayer player)
        {
            var id = Id(player);
            RaidTest test;
            if (!_raidTests.TryGetValue(id, out test)) { Reply(player, "NoRaidTest"); return; }
            var path = "copypaste/" + test.File;
            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(path)) { Reply(player, "NoRaidTest"); return; }

            var df = Interface.Oxide.DataFileSystem.GetDatafile(path);
            var def = df["default"] as Dictionary<string, object>;
            var dp = def?["position"] as Dictionary<string, object>;
            if (dp == null) { Reply(player, "NoRaidTest"); return; }
            var origin = new Vector3(F(dp["x"]), F(dp["y"]), F(dp["z"]));
            float rot = def.ContainsKey("rotationdiff") ? F(def["rotationdiff"]) : 0f;

            int alive = 0;
            foreach (var netId in test.Ids)
            {
                var e = BaseNetworkable.serverEntities.Find(new NetworkableId(netId)) as BaseEntity;
                if (e == null || e.IsDestroyed) continue;
                alive++;
                e.Kill();
            }
            int destroyed = test.Total - alive;
            _raidTests.Remove(id);

            var args = new[] { "deployables", "true", "inventories", "true", "stability", "false", "auth", "true", "entityowner", "true", "autoheight", "false" };
            timer.Once(0.5f, () =>
            {
                object result = CopyPaste.Call("TryPasteFromVector3", origin, rot, test.File, args, (Action)(() => { }), (Action<BaseEntity>)(e => { }), 0);
                if (result is string) Reply(player, "PasteError", (string)result);
            });
            Reply(player, "RaidRestored", destroyed, test.Total);
        }

        private static float F(object o)
        {
            float v;
            return o != null && float.TryParse(o.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
        }

        #endregion

        #region Участки

        private readonly Dictionary<ulong, Plot> _currentPlot = new Dictionary<ulong, Plot>();

        private Plot PlotAt(Vector3 pos)
        {
            foreach (var p in _data.Plots)
            {
                float dx = pos.x - p.X, dz = pos.z - p.Z;
                if (dx * dx + dz * dz <= p.Radius * p.Radius) return p;
            }
            return null;
        }

        private static bool IsMember(Plot plot, BasePlayer p) => plot.Owner == Id(p) || plot.Members.Contains(Id(p));

        private void ClaimPlot(BasePlayer player)
        {
            var id = Id(player);
            if (!IsAdminLike(player) && _data.Plots.Count(p => p.Owner == id) >= _config.MaxPlots) { Reply(player, "PlotLimit", _config.MaxPlots); return; }
            var pos = player.transform.position;
            float r = _config.PlotRadius;
            var clash = _data.Plots.FirstOrDefault(p => new Vector2(p.X - pos.x, p.Z - pos.z).magnitude < p.Radius + r);
            if (clash != null) { Reply(player, "PlotClash", clash.OwnerName); return; }
            var plot = new Plot { Owner = id, OwnerName = player.displayName, Name = T(id, "PlotN", _data.Plots.Count(p => p.Owner == id) + 1), X = pos.x, Z = pos.z, Radius = r };
            _data.Plots.Add(plot);
            _dirty = true;
            ShowPlot(player, plot);
            Reply(player, "PlotClaimed", plot.Name, r);
        }

        private void UnclaimPlot(BasePlayer player)
        {
            var plot = PlotAt(player.transform.position);
            if (plot == null || (plot.Owner != Id(player) && !IsAdminLike(player))) { Reply(player, "NotInOwnPlot"); return; }
            _data.Plots.Remove(plot);
            _dirty = true;
            Reply(player, "PlotRemoved", plot.Name);
        }

        private void PlotMember(BasePlayer player, string name, bool add)
        {
            var plot = PlotAt(player.transform.position);
            if (plot == null || plot.Owner != Id(player)) { Reply(player, "NotInOwnPlot"); return; }
            var target = BasePlayer.Find(name);
            if (target == null) { Reply(player, "PlayerNotFound"); return; }
            if (add) { if (!plot.Members.Contains(Id(target))) plot.Members.Add(Id(target)); }
            else plot.Members.Remove(Id(target));
            _dirty = true;
            Reply(player, add ? "MemberAdded" : "MemberRemoved", target.displayName, plot.Name);
        }

        private void ShowPlot(BasePlayer player, Plot plot)
        {
            var col = IsMember(plot, player) ? new Color(0.94f, 0.65f, 0.69f) : new Color(1f, 0.3f, 0.35f);
            WithAdmin(player, () =>
            {
                const int seg = 48;
                for (int i = 0; i < seg; i++)
                {
                    float a1 = i * Mathf.PI * 2f / seg, a2 = (i + 1) * Mathf.PI * 2f / seg;
                    var p1 = new Vector3(plot.X + Mathf.Cos(a1) * plot.Radius, 0f, plot.Z + Mathf.Sin(a1) * plot.Radius);
                    var p2 = new Vector3(plot.X + Mathf.Cos(a2) * plot.Radius, 0f, plot.Z + Mathf.Sin(a2) * plot.Radius);
                    p1.y = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(p1) + 0.5f : player.transform.position.y;
                    p2.y = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(p2) + 0.5f : player.transform.position.y;
                    player.SendConsoleCommand("ddraw.line", 15f, col, p1, p2);
                }
                var c = new Vector3(plot.X, player.transform.position.y + 3f, plot.Z);
                player.SendConsoleCommand("ddraw.text", 15f, col, c, $"<size=20>{plot.Name}</size>\n{plot.OwnerName}");
            });
        }

        #endregion

        #region Телепорты

        private class TpRequest
        {
            public ulong From;
            public float At;
        }

        private readonly Dictionary<ulong, TpRequest> _tpRequests = new Dictionary<ulong, TpRequest>();
        private readonly Dictionary<ulong, Vector3> _back = new Dictionary<ulong, Vector3>();

        private void DoTeleport(BasePlayer player, Vector3 pos)
        {
            _back[Id(player)] = player.transform.position;
            if (player.isMounted) TryCall(player.GetMounted(), "DismountPlayer", player, true);
            player.Teleport(pos);
        }

        private void SetHome(BasePlayer player, string name)
        {
            var s = P(Id(player));
            name = string.IsNullOrEmpty(name) ? "home" : name.Trim();
            var h = s.Homes.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (h == null)
            {
                if (s.Homes.Count >= _config.MaxHomes && !IsAdminLike(player)) { Reply(player, "HomeLimit", _config.MaxHomes); return; }
                s.Homes.Add(h = new Home { Name = name });
            }
            var p = player.transform.position;
            h.X = p.x; h.Y = p.y; h.Z = p.z;
            _dirty = true;
            Reply(player, "HomeSet", name);
        }

        private void GoHome(BasePlayer player, string name)
        {
            var s = P(Id(player));
            var h = string.IsNullOrEmpty(name) ? s.Homes.FirstOrDefault() : s.Homes.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (h == null) { Reply(player, "NoHome"); return; }
            DoTeleport(player, new Vector3(h.X, h.Y + 0.2f, h.Z));
            Toast(player, T(Id(player), "TpHome", h.Name), false);
        }

        private void RequestTp(BasePlayer player, BasePlayer target)
        {
            if (target == null || target == player) { Reply(player, "PlayerNotFound"); return; }
            _tpRequests[Id(target)] = new TpRequest { From = Id(player), At = Time.realtimeSinceStartup };
            Reply(player, "TprSent", target.displayName);
            ShowTpPopup(target, player);
        }

        private void AcceptTp(BasePlayer target)
        {
            TpRequest req;
            if (!_tpRequests.TryGetValue(Id(target), out req)) { Reply(target, "NoTpr"); return; }
            _tpRequests.Remove(Id(target));
            CuiHelper.DestroyUi(target, UiPopup);
            var from = BasePlayer.FindByID(req.From);
            if (from == null) return;
            DoTeleport(from, target.transform.position + Vector3.up * 0.2f);
            Toast(from, T(Id(from), "TprDone", target.displayName), false);
        }

        #endregion

        #region Команды

        [ChatCommand("build")]
        private void CmdBuild(BasePlayer player, string command, string[] args) { if (!Has(player, PermUse)) return; if (args.Length > 0 && Tabs.Contains(args[0])) Ui(Id(player)).Tab = args[0]; OpenUi(player); }

        [ChatCommand("b")]
        private void CmdB(BasePlayer player, string command, string[] args) => CmdBuild(player, command, args);

        [ChatCommand("fly")]
        private void CmdFly(BasePlayer player, string command, string[] args) => ToggleFly(player);

        [ChatCommand("god")]
        private void CmdGod(BasePlayer player, string command, string[] args)
        {
            if (args.Length > 0) { GodOther(player, args); return; }
            if (!Has(player, PermGod)) { Reply(player, "NoPerm"); return; }
            var s = P(Id(player));
            s.God = !s.God;
            _dirty = true;
            Toast(player, s.God ? T(Id(player), "GodOn") : T(Id(player), "GodOff"), false);
        }

        // /god ник [on|off] — админ включает/выключает бессмертие другому игроку
        private void GodOther(BasePlayer admin, string[] args)
        {
            if (!IsAdminLike(admin)) { Reply(admin, "NoPerm"); return; }
            string last = args[args.Length - 1].ToLower();
            bool? want = last == "on" || last == "вкл" ? true : last == "off" || last == "выкл" ? (bool?)false : null;
            var target = FindPlayer(string.Join(" ", want.HasValue ? args.Take(args.Length - 1).ToArray() : args));
            if (target == null) { Reply(admin, "PlayerNotFound"); return; }
            var s = P(Id(target));
            s.God = want ?? !s.God;
            s.GodByAdmin = s.God;
            _dirty = true;
            Toast(target, s.God ? T(Id(target), "GodOn") : T(Id(target), "GodOff"), false);
            if (target != admin) Toast(admin, target.displayName + ": " + (s.God ? T(Id(admin), "GodOn") : T(Id(admin), "GodOff")), false);
        }

        [ChatCommand("up")]
        private void CmdUp(BasePlayer player, string command, string[] args)
        {
            if (!Has(player, PermUse)) return;
            int g;
            ChangeBuildingGrade(player, args.Length > 0 && int.TryParse(args[0], out g) ? g : 4);
        }

        [ChatCommand("down")]
        private void CmdDown(BasePlayer player, string command, string[] args) { if (Has(player, PermUse)) ChangeBuildingGrade(player, 0); }

        [ChatCommand("remove")]
        private void CmdRemove(BasePlayer player, string command, string[] args) { if (Has(player, PermUse)) ToggleRemove(player); }

        [ChatCommand("day")]
        private void CmdDay(BasePlayer player, string command, string[] args) { P(Id(player)).Time = 12f; _dirty = true; SetClientTime(player, 12f); }

        [ChatCommand("night")]
        private void CmdNight(BasePlayer player, string command, string[] args) { P(Id(player)).Time = 0f; _dirty = true; SetClientTime(player, 0f); }

        [ChatCommand("sethome")]
        private void CmdSetHome(BasePlayer player, string command, string[] args) { if (Has(player, PermUse)) SetHome(player, string.Join(" ", args)); }

        [ChatCommand("home")]
        private void CmdHome(BasePlayer player, string command, string[] args) { if (Has(player, PermUse)) GoHome(player, string.Join(" ", args)); }

        [ChatCommand("tpr")]
        private void CmdTpr(BasePlayer player, string command, string[] args) { if (CanTp(player)) RequestTp(player, FindPlayer(string.Join(" ", args))); }

        // /tp ник — к игроку: админ сразу, остальные запросом (второй принимает /tpa). Стройка и Съёмки.
        [ChatCommand("tp")]
        private void CmdTp(BasePlayer player, string command, string[] args)
        {
            if (!CanTp(player)) { Reply(player, "NoPerm"); return; }
            var target = FindPlayer(string.Join(" ", args));
            if (target == null || target == player) { Reply(player, "PlayerNotFound"); return; }
            if (!IsAdminLike(player)) { RequestTp(player, target); return; }
            DoTeleport(player, target.transform.position + Vector3.up * 0.2f);
            Toast(player, T(Id(player), "TprDone", target.displayName), false);
        }

        private bool CanTp(BasePlayer p) => Has(p, PermUse) || permission.UserHasPermission(p.UserIDString, "videorecorder.use");

        private static BasePlayer FindPlayer(string q)
        {
            if (string.IsNullOrEmpty(q)) return null;
            q = q.Trim();
            return BasePlayer.activePlayerList.FirstOrDefault(p => p.UserIDString == q || string.Equals(p.displayName, q, StringComparison.OrdinalIgnoreCase))
                ?? BasePlayer.activePlayerList.FirstOrDefault(p => p.displayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [ChatCommand("tpa")]
        private void CmdTpa(BasePlayer player, string command, string[] args) => AcceptTp(player);

        [ChatCommand("back")]
        private void CmdBack(BasePlayer player, string command, string[] args)
        {
            Vector3 pos;
            if (!_back.TryGetValue(Id(player), out pos)) { Reply(player, "NoBack"); return; }
            DoTeleport(player, pos);
        }

        [ChatCommand("plot")]
        private void CmdPlot(BasePlayer player, string command, string[] args)
        {
            if (!Has(player, PermUse)) return;
            var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            var rest = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()) : "";
            switch (sub)
            {
                case "claim": ClaimPlot(player); break;
                case "unclaim": UnclaimPlot(player); break;
                case "add": PlotMember(player, rest, true); break;
                case "remove": PlotMember(player, rest, false); break;
                default:
                    var plot = PlotAt(player.transform.position);
                    if (plot != null) ShowPlot(player, plot); else Reply(player, "PlotHelp");
                    break;
            }
        }

        [ChatCommand("raidtest")]
        private void CmdRaid(BasePlayer player, string command, string[] args)
        {
            if (!Has(player, PermUse)) return;
            if (args.Length > 0 && args[0] == "restore") RaidRestore(player); else RaidStart(player);
        }

        [ConsoleCommand("bs.fly")]
        private void CcFly(ConsoleSystem.Arg arg) { var p = arg.Player(); if (p != null) ToggleFly(p); }

        [ConsoleCommand("bs.remove")]
        private void CcRemove(ConsoleSystem.Arg arg) { var p = arg.Player(); if (Has(p, PermUse)) ToggleRemove(p); }

        [ConsoleCommand("bs.ui")]
        private void CcUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!Has(player, PermUse)) return;
            var id = Id(player);
            var s = P(id);
            var ui = Ui(id);
            var action = arg.GetString(0);
            string rest = arg.Args != null && arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1).ToArray()).Trim() : "";
            int n = arg.GetInt(1, 0);

            switch (action)
            {
                case "close": CloseUi(player); return;
                case "tab":
                    if (rest == "library")
                    {
                        if (Blueprints != null && Blueprints.IsLoaded) { CloseUi(player); Blueprints.Call("OpenUi", player); }
                        else Toast(player, T(id, "NoBlueprints"), true);
                        return;
                    }
                    if (Blueprints != null && Blueprints.IsLoaded) Blueprints.Call("CloseUi", player);
                    ui.Tab = rest; ui.Page = 0; break;

                // --- инструменты
                case "sym.toggle": if (s.Tools.SymMode > 0) s.Tools.SymMode = 0; else { s.Tools.SymMode = 3; if (!s.Tools.SymSet) SetSymCenter(player); } break;
                case "sym.mode": s.Tools.SymMode = (s.Tools.SymMode + n + 6) % 6; if (s.Tools.SymMode > 0) ShowSymCenter(player); break;
                case "sym.center": SetSymCenter(player); break;
                case "t.wall": s.Tools.WallStack = !s.Tools.WallStack; break;
                case "wall.count": s.Tools.WallCount = Mathf.Clamp(s.Tools.WallCount + n, 1, 10); break;
                case "wall.gap": s.Tools.WallGap = Mathf.Clamp(s.Tools.WallGap + n * 0.25f, 0f, 3f); break;
                case "t.floor": s.Tools.FloorStack = !s.Tools.FloorStack; break;
                case "floor.count": s.Tools.FloorCount = Mathf.Clamp(s.Tools.FloorCount + n, 1, 10); break;
                case "t.brush": s.Tools.Brush = !s.Tools.Brush; break;
                case "brush.size": s.Tools.BrushSize = new[] { 3, 5, 7, 9 }[Mathf.Clamp(n, 0, 3)]; s.Tools.Brush = true; break;
                case "grade": ShiftGrade(player, n); break;
                case "upfilter": s.Tools.UpFilter = Mathf.Clamp(n, 0, 7); break;
                case "t.doors": s.Tools.AutoDoors = !s.Tools.AutoDoors; break;
                case "doortab": ui.DoorTab = Mathf.Clamp(n, 0, 1); break;
                case "door1": if (DoorsSingle.Contains(rest)) { s.Tools.DoorSingle = rest; s.Tools.AutoDoors = true; } break;
                case "door2": if (DoorsDouble.Contains(rest)) { s.Tools.DoorDouble = rest; s.Tools.AutoDoors = true; } break;
                case "t.doorlock": s.Tools.DoorLock = !s.Tools.DoorLock; break;
                case "t.dooropen": s.Tools.DoorOpen = !s.Tools.DoorOpen; break;
                case "t.windows": s.Tools.AutoWindows = !s.Tools.AutoWindows; break;
                case "wintab": ui.WinTab = Mathf.Clamp(n, 0, 1); break;
                case "window": if (WinBars.Contains(rest) || WinEmb.Contains(rest)) { s.Tools.Window = rest; s.Tools.AutoWindows = true; } break;
                case "t.hatch": s.Tools.AutoHatch = !s.Tools.AutoHatch; break;
                case "hatchtab": ui.HatchTab = Mathf.Clamp(n, 0, 1); break;
                case "hatch1": if (HatchSq.Contains(rest)) { s.Tools.HatchSquare = rest; s.Tools.AutoHatch = true; } break;
                case "hatch2": if (HatchTr.Contains(rest)) { s.Tools.HatchTri = rest; s.Tools.AutoHatch = true; } break;
                case "t.hatchlock": s.Tools.HatchLock = !s.Tools.HatchLock; break;
                case "t.turret": s.Tools.TurretFill = !s.Tools.TurretFill; break;
                case "turrettab": ui.TurretTab = Mathf.Clamp(n, 0, 3); break;
                case "turret": if (TurretGuns.Any(g => g.Contains(rest))) { s.Tools.TurretGun = rest; s.Tools.TurretFill = true; } break;
                case "t.furnaces": s.Tools.Furnaces = !s.Tools.Furnaces; break;
                case "t.free": s.Tools.FreeBuild = !s.Tools.FreeBuild; break;
                case "t.stab": s.Tools.NoStability = !s.Tools.NoStability; break;
                case "q.wall": s.Tools.WallStack = true; s.Tools.WallCount = Mathf.Clamp(n, 1, 10); s.Tools.WallGap = 0f; break;
                case "q.wallgap": s.Tools.WallStack = true; s.Tools.WallGap = s.Tools.WallGap > 0f ? 0f : 1.5f; break;
                case "q.floor": s.Tools.FloorStack = true; s.Tools.FloorCount = Mathf.Clamp(n, 1, 10); break;
                case "q.reset": s.Tools.WallStack = false; s.Tools.FloorStack = false; s.Tools.Brush = false; s.Tools.SymMode = 0; break;
                case "wipe": Wipe(player, Mathf.Clamp(n, 0, 2)); break;
                case "undo": Undo(player); break;
                case "redo": Redo(player); break;
                case "plan": Give(player, "building.planner", 1); Give(player, "hammer", 1); break;
                case "strip":
                    if (ui.ConfirmStrip && Time.realtimeSinceStartup - ui.ConfirmAt < 5f) { player.inventory.Strip(); ui.ConfirmStrip = false; }
                    else { ui.ConfirmStrip = true; ui.ConfirmAt = Time.realtimeSinceStartup; }
                    break;
                case "car": SpawnVehicle(player, n); CloseUi(player); return;
                case "plot.private": { var pl = PlotAt(player.transform.position); if (pl != null && (pl.Owner == id || IsAdminLike(player))) pl.Private = !pl.Private; else Toast(player, T(id, "PlotNotOwner"), true); } break;
                case "plot.save": PlotSave(player); break;
                case "plot.load": PlotLoad(player); break;
                case "plot.clear":
                    if (ui.ConfirmClear && Time.realtimeSinceStartup - ui.ConfirmAt < 5f) { PlotClear(player); ui.ConfirmClear = false; }
                    else { ui.ConfirmClear = true; ui.ConfirmAt = Time.realtimeSinceStartup; }
                    break;

                // --- зоны и окружение
                case "zone": TakeZone(player, rest); return;
                case "env.add":
                {
                    if (!CanWeather(player)) break;
                    int idx = n, dir = arg.GetInt(2, 1);
                    var v = idx < WeatherVars.Length ? WeatherVars[Mathf.Max(0, idx)] : CloudVars[Mathf.Clamp(idx - WeatherVars.Length, 0, CloudVars.Length - 1)];
                    float cur = EnvGet(v), step = player.serverInput != null && player.serverInput.IsDown(BUTTON.SPRINT) ? 0.2f : 0.1f;
                    float nv = cur < 0f ? (dir > 0 ? 0f : -1f) : cur + dir * step;
                    EnvSet(v, nv > EnvMax(v) ? EnvMax(v) : nv);
                    break;
                }
                case "env.reset":
                    if (!CanWeather(player)) break;
                    EnvSet(n < WeatherVars.Length ? WeatherVars[Mathf.Max(0, n)] : CloudVars[Mathf.Clamp(n - WeatherVars.Length, 0, CloudVars.Length - 1)], -1f);
                    break;
                case "env.resetall":
                    if (!CanWeather(player)) break;
                    foreach (var v in n == 0 ? WeatherVars : CloudVars) EnvSet(v, -1f);
                    break;
                case "time.add":
                {
                    float cur = s.Time < 0f ? 12f : s.Time;
                    s.Time = (cur + n * 0.5f + 24f) % 24f;
                    SetClientTime(player, s.Time);
                    break;
                }
                case "fly": ToggleFly(player); break;
                case "god": if (Has(player, PermGod)) { s.God = !s.God; } break;
                case "time": s.Time = n < 0 ? -1f : n; SetClientTime(player, s.Time); break;
                case "kit": foreach (var k in _config.BuilderKit) Give(player, k.Item, k.Amount); Toast(player, T(id, "KitGiven"), false); break;
                case "opt.kit": s.Kit = !s.Kit; break;
                case "opt.lock": s.AutoLock = !s.AutoLock; break;
                case "opt.auth": s.AutoAuth = !s.AutoAuth; break;
                case "opt.maptp": s.MapTp = !s.MapTp; break;
                case "code": s.Code = new string(rest.Where(char.IsDigit).Take(4).ToArray()); if (s.Code.Length != 4) s.Code = null; break;

                case "cat": ui.Category = rest; ui.Page = 0; break;
                case "search": ui.Search = rest; ui.Page = 0; break;
                case "page": ui.Page = Mathf.Max(0, ui.Page + n); break;
                case "amount": s.ItemAmount = Mathf.Clamp(n, 1, 100000); break;
                case "give":
                    if (!Has(player, PermItems)) { Reply(player, "NoPerm"); break; }
                    {
                        var def = ItemManager.FindItemDefinition(n);
                        if (def != null)
                        {
                            int amount = s.ItemAmount >= 100000 ? def.stackable : Mathf.Min(s.ItemAmount, Mathf.Max(1, def.stackable) * 10);
                            var item = ItemManager.Create(def, Mathf.Max(1, amount), 0UL);
                            if (item != null) player.GiveItem(item);
                        }
                    }
                    break;

                case "up": ChangeBuildingGrade(player, n); break;
                case "repair": RepairBuilding(player); break;
                case "removeall":
                    if (ui.ConfirmRemoveAll && Time.realtimeSinceStartup - ui.ConfirmAt < 5f) { RemoveBuilding(player); ui.ConfirmRemoveAll = false; }
                    else { ui.ConfirmRemoveAll = true; ui.ConfirmAt = Time.realtimeSinceStartup; }
                    break;
                case "removemode": ToggleRemove(player); break;

                case "raid.start": RaidStart(player); return;
                case "raid.restore": RaidRestore(player); break;
                case "raid.kit": foreach (var k in _config.RaidKit) Give(player, k.Item, k.Amount); break;
                case "raid.path": RaidPath(player); break;
                case "fr.pick": { ulong fp; if (ulong.TryParse(rest, out fp)) _friendPick[id] = fp; } break;
                case "fr.min": _friendMin[id] = n; break;
                case "fr.start": FriendRaidStart(player); break;
                case "fr.stop": FriendRaidStop(id, false); break;
                case "el.place": if (n >= 0 && n < _config.Circuits.Count) PlaceCircuit(player, _config.Circuits[n]); break;
                case "el.copy": CopyCircuit(player); break;
                case "el.paste": { List<Circuit> mine; if (Circuits().Saved.TryGetValue(id, out mine) && n >= 0 && n < mine.Count) PlaceCircuit(player, mine[n]); } break;
                case "el.del": { List<Circuit> mine; if (Circuits().Saved.TryGetValue(id, out mine) && n >= 0 && n < mine.Count) { mine.RemoveAt(n); SaveCircuits(); } } break;
                case "el.cost": CircuitCost(player); break;
                case "el.load": ToggleLoad(player); break;
                case "sp.spawn": SpawnObject(player, n); break;
                case "sp.clear": SpawnClear(player); break;
                case "plot.pvp":
                {
                    var pl0 = PlotAt(player.transform.position);
                    if (pl0 == null || pl0.Owner != id) { Reply(player, "NotInOwnPlot"); break; }
                    pl0.Pvp = !pl0.Pvp; _dirty = true;
                    foreach (var bp in BasePlayer.activePlayerList) if (PlotAt(bp.transform.position) == pl0) Toast(bp, T(Id(bp), pl0.Pvp ? "PvpOn" : "PvpOff", pl0.Name), pl0.Pvp);
                    break;
                }

                case "plot.claim": ClaimPlot(player); break;
                case "plot.unclaim": UnclaimPlot(player); break;
                case "plot.show": { var pl = PlotAt(player.transform.position); if (pl != null) ShowPlot(player, pl); } break;
                case "plot.kick":
                {
                    var pl = PlotAt(player.transform.position);
                    ulong mid;
                    if (pl != null && pl.Owner == id && ulong.TryParse(rest, out mid)) pl.Members.Remove(mid);
                    break;
                }
                case "plot.add":
                {
                    var pl = PlotAt(player.transform.position);
                    ulong mid;
                    if (pl != null && pl.Owner == id && ulong.TryParse(rest, out mid) && !pl.Members.Contains(mid)) pl.Members.Add(mid);
                    break;
                }

                case "home.set": SetHome(player, ui.HomeName); ui.HomeName = ""; break;
                case "home.name": ui.HomeName = rest; break;
                case "home.go": if (n >= 0 && n < s.Homes.Count) { GoHome(player, s.Homes[n].Name); CloseUi(player); return; } break;
                case "home.del": if (n >= 0 && n < s.Homes.Count) s.Homes.RemoveAt(n); break;
                case "tpr": { ulong tid; if (ulong.TryParse(rest, out tid)) RequestTp(player, BasePlayer.FindByID(tid)); } break;
                case "tpa": AcceptTp(player); return;
                case "tpdeny": _tpRequests.Remove(id); CuiHelper.DestroyUi(player, UiPopup); return;
                case "back": { Vector3 pos; if (_back.TryGetValue(id, out pos)) { DoTeleport(player, pos); CloseUi(player); return; } } break;
            }
            _dirty = true;
            OpenUi(player);
            if (action.StartsWith("plot.") || action.StartsWith("sym") || action == "t.free") RefreshHud(player);
        }

        #endregion

        #region Инструменты стройки

        private class ToolSettings
        {
            public int SymMode;
            public bool SymSet;
            public float SymX, SymY, SymZ, SymYaw;
            public bool WallStack;
            public int WallCount = 3;
            public float WallGap;
            public bool FloorStack;
            public int FloorCount = 3;
            public bool Brush;
            public int BrushSize = 3;
            public int UpFilter;
            public bool AutoDoors = true;
            public string DoorSingle = "door.hinged.metal";
            public string DoorDouble = "door.double.hinged.metal";
            public bool DoorLock = true;
            public bool DoorOpen;
            public bool AutoWindows;
            public string Window = "wall.window.bars.metal";
            public bool AutoHatch;
            public string HatchSquare = "floor.ladder.hatch";
            public string HatchTri = "floor.triangle.ladder.hatch";
            public bool HatchLock = true;
            public bool TurretFill = true;
            public string TurretGun = "rifle.ak";
            public bool Furnaces;
            public bool FreeBuild = true;
            public bool NoStability = true;
            public int AutoGrade = -1;
            public ulong Skin;
            public bool ShowAllSkins;
            public bool HudCost = true;
            public bool HudBar = true;
            public bool CostTwig;
            public bool CostUpkeep;
            public bool CostDeploy;
        }

        private class VehicleEntry
        {
            [JsonProperty("Название")] public string Ru;
            [JsonProperty("Название EN")] public string En;
            [JsonProperty("Префаб")] public string Prefab;
            [JsonProperty("Топливо")] public int Fuel;
        }

        private static List<VehicleEntry> DefaultVehicles() => new List<VehicleEntry>
        {
            new VehicleEntry { Ru = "Машина 2", En = "Car 2", Prefab = "assets/content/vehicles/modularcar/2module_car_spawned.entity.prefab", Fuel = 200 },
            new VehicleEntry { Ru = "Машина 3", En = "Car 3", Prefab = "assets/content/vehicles/modularcar/3module_car_spawned.entity.prefab", Fuel = 200 },
            new VehicleEntry { Ru = "Машина 4", En = "Car 4", Prefab = "assets/content/vehicles/modularcar/4module_car_spawned.entity.prefab", Fuel = 200 },
            new VehicleEntry { Ru = "Седан", En = "Sedan", Prefab = "assets/content/vehicles/sedan_a/sedantest.entity.prefab", Fuel = 0 },
            new VehicleEntry { Ru = "Мотоцикл", En = "Motorbike", Prefab = "assets/content/vehicles/bikes/motorbike.prefab", Fuel = 200 },
            new VehicleEntry { Ru = "Велосипед", En = "Bicycle", Prefab = "assets/content/vehicles/bikes/pedalbike.prefab", Fuel = 0 },
            new VehicleEntry { Ru = "Трайк", En = "Trike", Prefab = "assets/content/vehicles/bikes/pedaltrike.prefab", Fuel = 0 },
            new VehicleEntry { Ru = "Снегоход", En = "Snowmobile", Prefab = "assets/content/vehicles/snowmobiles/snowmobile.prefab", Fuel = 200 },
            new VehicleEntry { Ru = "Лошадь", En = "Horse", Prefab = "assets/rust.ai/nextai/testridablehorse.prefab", Fuel = 0 },
            new VehicleEntry { Ru = "Миникоптер", En = "Minicopter", Prefab = "assets/content/vehicles/minicopter/minicopter.entity.prefab", Fuel = 300 },
            new VehicleEntry { Ru = "Скрап-хели", En = "Scrap heli", Prefab = "assets/content/vehicles/scrap heli carrier/scraptransporthelicopter.prefab", Fuel = 300 },
            new VehicleEntry { Ru = "Атак-хели", En = "Attack heli", Prefab = "assets/content/vehicles/attackhelicopter/attackhelicopter.entity.prefab", Fuel = 300 },
        };

        private static readonly string[] DoorsSingle = { "door.hinged.wood", "door.hinged.metal", "door.hinged.toptier" };
        private static readonly string[] DoorsDouble = { "door.double.hinged.wood", "door.double.hinged.metal", "door.double.hinged.toptier", "wall.frame.garagedoor" };
        private static readonly string[] WinBars = { "wall.window.bars.wood", "wall.window.bars.metal", "wall.window.bars.toptier", "wall.window.glass.reinforced" };
        private static readonly string[] WinEmb = { "shutter.wood.a", "shutter.metal.embrasure.a", "shutter.metal.embrasure.b" };
        private static readonly string[] HatchSq = { "floor.ladder.hatch", "floor.grill" };
        private static readonly string[] HatchTr = { "floor.triangle.ladder.hatch", "floor.triangle.grill" };
        private static readonly string[][] TurretGuns =
        {
            new[] { "rifle.ak", "rifle.lr300", "rifle.semiauto", "rifle.m39" },
            new[] { "pistol.revolver", "pistol.semiauto", "pistol.python", "pistol.m92" },
            new[] { "smg.thompson", "smg.2", "smg.mp5" },
            new[] { "shotgun.pump", "shotgun.spas12", "lmg.m249", "rifle.bolt" },
        };

        private static string AmmoFor(string gun)
        {
            if (gun.StartsWith("shotgun")) return "ammo.shotgun";
            if (gun.StartsWith("pistol") || gun.StartsWith("smg")) return "ammo.pistol";
            return "ammo.rifle";
        }

        private static bool IsWall(string sp) => sp.StartsWith("wall");
        private static bool IsFloor(string sp) => sp.StartsWith("floor");

        // 0 всё · 1 фундаменты · 2 стены · 3 полы · 4 проёмы · 5 окна · 6 лестницы · 7 крыши
        private static bool MatchFilter(BuildingBlock b, int f)
        {
            if (f <= 0 || b == null) return true;
            var sp = b.ShortPrefabName ?? "";
            switch (f)
            {
                case 1: return sp.StartsWith("foundation");
                case 2: return sp == "wall" || sp == "wall.half" || sp == "wall.low";
                case 3: return sp.StartsWith("floor");
                case 4: return sp == "wall.doorway" || sp == "wall.frame";
                case 5: return sp == "wall.window";
                case 6: return sp.Contains("stair");
                case 7: return sp.StartsWith("roof") || sp.Contains("ramp");
            }
            return true;
        }

        private readonly Dictionary<ulong, List<List<BaseEntity>>> _undo = new Dictionary<ulong, List<List<BaseEntity>>>();
        private readonly Dictionary<ulong, List<List<Spec>>> _redo = new Dictionary<ulong, List<List<Spec>>>();
        private readonly Dictionary<ulong, BaseEntity> _vehicles = new Dictionary<ulong, BaseEntity>();

        private class Spec
        {
            public string Prefab;
            public Vector3 Pos;
            public Quaternion Rot;
            public int Grade = -1;
        }

        private static List<T> Stack<T>(Dictionary<ulong, List<T>> d, ulong id)
        {
            List<T> l;
            if (!d.TryGetValue(id, out l)) d[id] = l = new List<T>();
            return l;
        }

        private static bool Occupied(uint prefabId, Vector3 pos)
        {
            var list = new List<BaseEntity>();
            Vis.Entities(pos, 0.2f, list, LayerMask.GetMask("Construction", "Deployed"), QueryTriggerInteraction.Collide);
            foreach (var e in list)
                if (e != null && !e.IsDestroyed && e.prefabID == prefabId && (e.transform.position - pos).sqrMagnitude < 0.02f) return true;
            return false;
        }

        private BaseEntity CopyBlock(BaseEntity src, Vector3 pos, Quaternion rot, ulong owner, bool noStability)
        {
            if (src == null || Occupied(src.prefabID, pos)) return null;
            var e = GameManager.server.CreateEntity(src.PrefabName, pos, rot);
            if (e == null) return null;
            e.OwnerID = owner;
            var b = e as BuildingBlock;
            var sb = src as BuildingBlock;
            if (b != null)
            {
                b.blockDefinition = PrefabAttribute.server.Find<Construction>(b.prefabID);
                if (sb != null) b.grade = sb.grade;
                b.grounded = noStability;
            }
            var sde = src as DecayEntity;
            if (sde != null) TryCall(e, "AttachToBuilding", sde.buildingID);
            e.Spawn();
            if (b != null)
            {
                if (sb != null) b.SetGrade(sb.grade);
                b.SetHealth(b.MaxHealth());
                b.SendNetworkUpdate();
            }
            return e;
        }

        private IEnumerable<KeyValuePair<Vector3, Quaternion>> SymCopies(ToolSettings t, Vector3 p, Quaternion q)
        {
            var c = new Vector3(t.SymX, t.SymY, t.SymZ);
            var r = Quaternion.Euler(0f, t.SymYaw, 0f);
            var inv = Quaternion.Inverse(r);
            var lp = inv * (p - c);
            var lf = inv * (q * Vector3.forward);
            var lu = inv * (q * Vector3.up);
            var ops = new List<int[]>();
            switch (t.SymMode)
            {
                case 1: ops.Add(new[] { 1, 0, 0 }); break;
                case 2: ops.Add(new[] { 0, 1, 0 }); break;
                case 3: ops.Add(new[] { 1, 0, 0 }); ops.Add(new[] { 0, 1, 0 }); ops.Add(new[] { 1, 1, 0 }); break;
                case 4: ops.Add(new[] { 0, 0, 180 }); break;
                case 5: ops.Add(new[] { 0, 0, 90 }); ops.Add(new[] { 0, 0, 180 }); ops.Add(new[] { 0, 0, 270 }); break;
            }
            foreach (var op in ops)
            {
                Vector3 v = lp, f = lf, u = lu;
                if (op[0] == 1) { v.x = -v.x; f.x = -f.x; u.x = -u.x; }
                if (op[1] == 1) { v.z = -v.z; f.z = -f.z; u.z = -u.z; }
                if (op[2] != 0)
                {
                    var a = Quaternion.Euler(0f, op[2], 0f);
                    v = a * v; f = a * f; u = a * u;
                }
                yield return new KeyValuePair<Vector3, Quaternion>(c + r * v, r * Quaternion.LookRotation(f, u));
            }
        }

        private void ProcessBuilt(BasePlayer player, BaseEntity entity)
        {
            var id = Id(player);
            var t = P(id).Tools;
            var group = new List<BaseEntity> { entity };
            var block = entity as BuildingBlock;

            if (block != null)
            {
                if (t.NoStability) block.grounded = true;
                if (t.AutoGrade >= 0) ApplyGrade(block, t.AutoGrade, t.Skin);
                var sp = block.ShortPrefabName ?? "";
                var tr = block.transform;

                if (t.Brush && t.BrushSize > 1 && sp == "foundation")
                {
                    int k = t.BrushSize / 2;
                    for (int i = -k; i <= k; i++)
                        for (int j = -k; j <= k; j++)
                        {
                            if (i == 0 && j == 0) continue;
                            var e = CopyBlock(block, tr.position + tr.rotation * new Vector3(i * 3f, 0f, j * 3f), tr.rotation, id, t.NoStability);
                            if (e != null) group.Add(e);
                        }
                }

                foreach (var b in group.ToList())
                {
                    if (t.WallStack && IsWall(sp))
                        for (int i = 1; i <= t.WallCount; i++)
                        {
                            var e = CopyBlock(b, b.transform.position + Vector3.up * (3f + t.WallGap) * i, b.transform.rotation, id, t.NoStability);
                            if (e != null) group.Add(e);
                        }
                    if (t.FloorStack && IsFloor(sp))
                        for (int i = 1; i <= t.FloorCount; i++)
                        {
                            var e = CopyBlock(b, b.transform.position + Vector3.up * 3f * i, b.transform.rotation, id, t.NoStability);
                            if (e != null) group.Add(e);
                        }
                }

                if (t.SymMode > 0 && t.SymSet)
                    foreach (var b in group.ToList())
                        foreach (var kv in SymCopies(t, b.transform.position, b.transform.rotation))
                        {
                            var e = CopyBlock(b, kv.Key, kv.Value, id, t.NoStability);
                            if (e != null) group.Add(e);
                        }

                Ach(id, "build_blocks", group.Count(x => x is BuildingBlock));
                NextTick(() =>
                {
                    foreach (var b in group.ToList()) ApplyAuto(player, b as BuildingBlock, t, group);
                    PushUndo(id, group);
                });
                return;
            }

            var turret = entity as AutoTurret;
            if (turret != null && t.TurretFill)
                NextTick(() =>
                {
                    if (turret == null || turret.IsDestroyed || turret.inventory == null) return;
                    var gun = ItemManager.CreateByName(t.TurretGun, 1, 0UL);
                    if (gun != null && !gun.MoveToContainer(turret.inventory, 0)) gun.Remove();
                    var ammo = ItemManager.CreateByName(AmmoFor(t.TurretGun), 256, 0UL);
                    if (ammo != null && !ammo.MoveToContainer(turret.inventory)) ammo.Remove();
                    turret.SendNetworkUpdate();
                });

            var oven = entity as BaseOven;
            if (oven != null && t.Furnaces && (entity.ShortPrefabName ?? "").Contains("furnace"))
                NextTick(() =>
                {
                    if (oven == null || oven.IsDestroyed || oven.inventory == null) return;
                    foreach (var kv in new[] { new KeyValuePair<string, int>("wood", 1000), new KeyValuePair<string, int>("metal.ore", 1000) })
                    {
                        var it = ItemManager.CreateByName(kv.Key, kv.Value, 0UL);
                        if (it != null && !it.MoveToContainer(oven.inventory)) it.Remove();
                    }
                    TryCall(oven, "StartCooking");
                });

            PushUndo(id, group);
        }

        private static string DeployPrefab(string shortname)
        {
            var def = ItemManager.FindItemDefinition(shortname);
            var dep = def != null ? def.GetComponent<ItemModDeployable>() : null;
            return dep != null && dep.entityPrefab != null ? dep.entityPrefab.resourcePath : null;
        }

        private void ApplyAuto(BasePlayer player, BuildingBlock b, ToolSettings t, List<BaseEntity> group)
        {
            if (b == null || b.IsDestroyed) return;
            var sp = b.ShortPrefabName ?? "";
            string item = null;
            bool isDoor = false, isHatch = false;
            if (t.AutoDoors && sp == "wall.doorway") { item = t.DoorSingle; isDoor = true; }
            else if (t.AutoDoors && sp == "wall.frame") { item = t.DoorDouble; isDoor = true; }
            else if (t.AutoWindows && sp == "wall.window") item = t.Window;
            else if (t.AutoHatch && sp == "floor.frame") { item = t.HatchSquare; isHatch = true; }
            else if (t.AutoHatch && sp == "floor.triangle.frame") { item = t.HatchTri; isHatch = true; }
            if (item == null) return;

            var prefab = DeployPrefab(item);
            if (prefab == null) return;
            var e = GameManager.server.CreateEntity(prefab, b.transform.position, b.transform.rotation);
            if (e == null) return;
            e.OwnerID = Id(player);
            TryCall(e, "AttachToBuilding", b.buildingID);
            e.Spawn();
            group.Add(e);

            var door = e as Door;
            if (door == null) return;
            if ((isDoor && t.DoorLock) || (isHatch && t.HatchLock)) AddCodeLock(player, door);
            if (isDoor && t.DoorOpen) using (var fl = door.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate)) fl.Set(BaseEntity.Flags.Open, true);
        }

        private void PushUndo(ulong id, List<BaseEntity> group)
        {
            var u = Stack(_undo, id);
            u.Add(group);
            if (u.Count > 30) u.RemoveAt(0);
            Stack(_redo, id).Clear();
        }

        private void Undo(BasePlayer player)
        {
            var id = Id(player);
            var u = Stack(_undo, id);
            if (u.Count == 0) { Toast(player, T(id, "UndoEmpty"), true); return; }
            var g = u[u.Count - 1];
            u.RemoveAt(u.Count - 1);
            var specs = new List<Spec>();
            foreach (var e in g)
            {
                if (e == null || e.IsDestroyed) continue;
                if (e.GetParentEntity() == null)
                    specs.Add(new Spec { Prefab = e.PrefabName, Pos = e.transform.position, Rot = e.transform.rotation, Grade = e is BuildingBlock ? (int)((BuildingBlock)e).grade : -1 });
                e.Kill();
            }
            var r = Stack(_redo, id);
            r.Add(specs);
            if (r.Count > 30) r.RemoveAt(0);
            Toast(player, T(id, "Undone", specs.Count), false);
        }

        private void Redo(BasePlayer player)
        {
            var id = Id(player);
            var r = Stack(_redo, id);
            if (r.Count == 0) { Toast(player, T(id, "RedoEmpty"), true); return; }
            var specs = r[r.Count - 1];
            r.RemoveAt(r.Count - 1);
            var t = P(id).Tools;
            var group = new List<BaseEntity>();
            DecayEntity first = null;
            foreach (var s in specs.OrderBy(x => x.Grade < 0 ? 1 : 0))
            {
                var e = GameManager.server.CreateEntity(s.Prefab, s.Pos, s.Rot);
                if (e == null) continue;
                e.OwnerID = id;
                var b = e as BuildingBlock;
                if (b != null)
                {
                    b.blockDefinition = PrefabAttribute.server.Find<Construction>(b.prefabID);
                    if (s.Grade >= 0) b.grade = (BuildingGrade.Enum)s.Grade;
                    b.grounded = t.NoStability;
                }
                if (first != null) TryCall(e, "AttachToBuilding", first.buildingID);
                e.Spawn();
                if (b != null) { if (s.Grade >= 0) b.SetGrade((BuildingGrade.Enum)s.Grade); b.SetHealth(b.MaxHealth()); b.SendNetworkUpdate(); }
                if (first == null) first = e as DecayEntity;
                group.Add(e);
            }
            Stack(_undo, id).Add(group);
            Toast(player, T(id, "Redone", group.Count), false);
        }

        private void SetSymCenter(BasePlayer player)
        {
            var id = Id(player);
            var t = P(id).Tools;
            RaycastHit hit;
            BuildingBlock under = null;
            if (Physics.Raycast(player.transform.position + Vector3.up * 0.5f, Vector3.down, out hit, 6f, LayerMask.GetMask("Construction"), QueryTriggerInteraction.Ignore))
                under = hit.GetEntity() as BuildingBlock;
            if (under == null) { Toast(player, T(id, "SymNeedFoundation"), true); return; }
            var p = under.transform.position;
            t.SymX = p.x; t.SymY = p.y; t.SymZ = p.z;
            t.SymYaw = under.transform.rotation.eulerAngles.y;
            t.SymSet = true;
            if (t.SymMode == 0) t.SymMode = 3;
            _dirty = true;
            ShowSymCenter(player);
            Toast(player, T(id, "SymSet"), false);
        }

        private void ShowSymCenter(BasePlayer player)
        {
            var t = P(Id(player)).Tools;
            if (!t.SymSet) return;
            var c = new Vector3(t.SymX, t.SymY + 0.2f, t.SymZ);
            var r = Quaternion.Euler(0f, t.SymYaw, 0f);
            WithAdmin(player, () =>
            {
                player.SendConsoleCommand("ddraw.line", 15f, Color.red, c - r * Vector3.right * 30f, c + r * Vector3.right * 30f);
                player.SendConsoleCommand("ddraw.line", 15f, Color.red, c - r * Vector3.forward * 30f, c + r * Vector3.forward * 30f);
                player.SendConsoleCommand("ddraw.sphere", 15f, Color.white, c, 0.5f);
            });
        }

        private BuildingManager.Building BuildingNear(BasePlayer player, out BaseEntity anchor)
        {
            var b = LookBuilding(player, out anchor);
            if (b != null) return b;
            RaycastHit hit;
            if (Physics.Raycast(player.transform.position + Vector3.up * 0.5f, Vector3.down, out hit, 6f, LayerMask.GetMask("Construction"), QueryTriggerInteraction.Ignore))
            {
                anchor = hit.GetEntity();
                var de = anchor as DecayEntity;
                if (de != null) return de.GetBuilding();
            }
            return null;
        }

        // 0 — всё выше меня, 1 — только деплои, 2 — до фундамента
        private void Wipe(BasePlayer player, int mode)
        {
            var id = Id(player);
            BaseEntity anchor;
            var building = BuildingNear(player, out anchor);
            if (building == null) { Reply(player, "LookAtBuilding"); return; }
            if (!CanModify(player, anchor)) { Reply(player, "NotYours"); return; }
            float level = player.transform.position.y + 0.3f;
            int n = 0;
            var all = building.decayEntities.Cast<BaseEntity>().Concat(building.buildingBlocks.Cast<BaseEntity>()).Distinct().ToList();
            foreach (var e in all)
            {
                if (e == null || e.IsDestroyed) continue;
                var b = e as BuildingBlock;
                bool kill;
                if (mode == 0) kill = e.transform.position.y > level;
                else if (mode == 1) kill = b == null;
                else kill = b == null || !(b.ShortPrefabName ?? "").StartsWith("foundation");
                if (!kill) continue;
                e.Kill();
                n++;
            }
            Toast(player, T(id, "Wiped", n), false);
        }

        private void ShiftGrade(BasePlayer player, int delta)
        {
            var id = Id(player);
            BaseEntity anchor;
            var building = BuildingNear(player, out anchor);
            if (building == null) { Reply(player, "LookAtBuilding"); return; }
            if (!CanModify(player, anchor)) { Reply(player, "NotYours"); return; }
            int f = P(id).Tools.UpFilter, n = 0;
            foreach (var block in building.buildingBlocks.ToList())
            {
                if (block == null || block.IsDestroyed || !MatchFilter(block, f)) continue;
                var g = (BuildingGrade.Enum)Mathf.Clamp((int)block.grade + delta, 0, 4);
                if (g == block.grade) continue;
                if (TryCall(block, "ChangeGrade", g, false, true) == null) block.SetGrade(g);
                block.SetHealth(block.MaxHealth());
                block.SendNetworkUpdate();
                n++;
            }
            Toast(player, T(id, delta > 0 ? "GradedUp" : "GradedDown", n), false);
        }

        private void SpawnVehicle(BasePlayer player, int idx)
        {
            var id = Id(player);
            if (idx < 0 || idx >= _config.Vehicles.Count) return;
            var v = _config.Vehicles[idx];
            BaseEntity old;
            if (_vehicles.TryGetValue(id, out old) && old != null && !old.IsDestroyed) old.Kill();
            var fwd = player.eyes.HeadForward();
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            var pos = player.transform.position + fwd * 5f;
            if (TerrainMeta.HeightMap != null) pos.y = Mathf.Max(pos.y, TerrainMeta.HeightMap.GetHeight(pos)) + 0.5f;
            var e = GameManager.server.CreateEntity(v.Prefab, pos, Quaternion.LookRotation(fwd));
            if (e == null) { Toast(player, T(id, "VehicleFail"), true); return; }
            e.OwnerID = id;
            e.Spawn();
            _vehicles[id] = e;
            if (v.Fuel > 0) AddFuel(e, v.Fuel);
            Toast(player, T(id, "VehicleSpawned", Lang(id) == "ru" ? v.Ru : v.En), false);
        }

        private void AddFuel(BaseEntity vehicle, int amount)
        {
            var fs = TryCall(vehicle, "GetFuelSystem");
            if (fs != null && !(fs is bool) && (TryCall(fs, "AddStartingFuel", (float)amount) != null || TryCall(fs, "AddStartingFuel", amount) != null)) return;
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

        private void PlotSave(BasePlayer player)
        {
            var id = Id(player);
            if (CopyPaste == null || !CopyPaste.IsLoaded) { Reply(player, "NoCopyPaste"); return; }
            BaseEntity looked;
            if (LookBuilding(player, out looked) == null) { Reply(player, "LookAtBuilding"); return; }
            var args = new[] { "method", "building", "deployables", "true", "inventories", "true" };
            object result = CopyPaste.Call("TryCopyFromSteamId", id, "bs_plot_" + id, args, (Action)(() => Toast(player, T(id, "PlotSaved"), false)));
            if (result is string) Reply(player, "CopyError", (string)result);
        }

        private void PlotLoad(BasePlayer player)
        {
            var id = Id(player);
            if (CopyPaste == null || !CopyPaste.IsLoaded) { Reply(player, "NoCopyPaste"); return; }
            if (!PasteSaved(player, "bs_plot_" + id)) Toast(player, T(id, "PlotNoSave"), true);
            else Toast(player, T(id, "PlotLoaded"), false);
        }

        private bool PasteSaved(BasePlayer player, string file)
        {
            var path = "copypaste/" + file;
            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(path)) return false;
            var df = Interface.Oxide.DataFileSystem.GetDatafile(path);
            var def = df["default"] as Dictionary<string, object>;
            var dp = def?["position"] as Dictionary<string, object>;
            if (dp == null) return false;
            var origin = new Vector3(F(dp["x"]), F(dp["y"]), F(dp["z"]));
            float rot = def.ContainsKey("rotationdiff") ? F(def["rotationdiff"]) : 0f;
            var args = new[] { "deployables", "true", "inventories", "true", "stability", "false", "auth", "true", "entityowner", "true", "autoheight", "false" };
            object result = CopyPaste.Call("TryPasteFromVector3", origin, rot, file, args, (Action)(() => { }), (Action<BaseEntity>)(e => { }), 0);
            if (result is string) { Reply(player, "PasteError", (string)result); return false; }
            return true;
        }

        private void PlotClear(BasePlayer player)
        {
            var id = Id(player);
            var plot = PlotAt(player.transform.position);
            if (plot == null || (plot.Owner != id && !IsAdminLike(player))) { Toast(player, T(id, "PlotNotOwner"), true); return; }
            int n = 0;
            foreach (var net in BaseNetworkable.serverEntities.ToList())
            {
                var e = net as DecayEntity;
                if (e == null || e.IsDestroyed) continue;
                float dx = e.transform.position.x - plot.X, dz = e.transform.position.z - plot.Z;
                if (dx * dx + dz * dz > plot.Radius * plot.Radius) continue;
                e.Kill();
                n++;
            }
            Toast(player, T(id, "Wiped", n), false);
        }

        private void PushOutOfPlot(BasePlayer p, Plot plot)
        {
            var dir = new Vector3(p.transform.position.x - plot.X, 0f, p.transform.position.z - plot.Z);
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            var pos = new Vector3(plot.X, 0f, plot.Z) + dir.normalized * (plot.Radius + 4f);
            pos.y = TerrainMeta.HeightMap != null ? TerrainMeta.HeightMap.GetHeight(pos) + 1f : p.transform.position.y;
            DoTeleport(p, pos);
            Toast(p, T(Id(p), "PlotPrivate", plot.OwnerName ?? "?"), true);
        }

        private object OnPayForPlacement(BasePlayer player, Planner planner, Construction construction)
            => player != null && Has(player, PermUse) && P(Id(player)).Tools.FreeBuild ? (object)false : null;

        private object OnPayForUpgrade(BasePlayer player, BuildingBlock block, ConstructionGrade gradeTarget)
            => player != null && Has(player, PermUse) && P(Id(player)).Tools.FreeBuild ? (object)false : null;

        #endregion

        #region Окружение и зоны

        // погода: конвары сервера, -1 = по умолчанию (естественная погода)
        private static readonly string[] WeatherVars =
        {
            "fog", "rain", "rainbow", "thunder", "wind",
            "atmosphere_brightness", "atmosphere_contrast", "atmosphere_directionality", "atmosphere_mie", "atmosphere_rayleigh",
        };
        private static readonly string[] CloudVars =
        {
            "cloud_attenuation", "cloud_brightness", "cloud_coloring", "cloud_coverage",
            "cloud_opacity", "cloud_saturation", "cloud_scattering", "cloud_sharpness", "cloud_size",
        };

        private static float EnvMax(string v) => v.StartsWith("atmosphere") || v == "cloud_brightness" ? 2f : 1f;

        private float EnvGet(string v)
        {
            float f;
            return _data.Env != null && _data.Env.TryGetValue(v, out f) ? f : -1f;
        }

        private void EnvSet(string v, float value)
        {
            value = Mathf.Round(value * 100f) / 100f;
            if (value < 0f) { _data.Env.Remove(v); value = -1f; }
            else _data.Env[v] = value;
            ServerCmd("weather." + v, value.ToString("0.##", CultureInfo.InvariantCulture));
            _dirty = true;
        }

        private void ApplyEnv()
        {
            if (_data.Env == null) return;
            foreach (var kv in _data.Env) ServerCmd("weather." + kv.Key, kv.Value.ToString("0.##", CultureInfo.InvariantCulture));
        }

        private bool CanWeather(BasePlayer p) => Has(p, PermWeather);

        private void EnvRow(CuiElementContainer c, ulong id, string v, int idx, bool can, float x, float y, float w, float h)
        {
            float val = EnvGet(v);
            Text(c, T(id, "W_" + v), 12, x, y, w * 0.42f, h);
            float bx = x + w * 0.42f;
            Btn(c, "×", "bs.ui env.reset " + idx, ColBtn, 12, bx, y + (h - 28) / 2f, 28, 28, can ? ColRose : ColDim, 8f);
            float sx = bx + 34, sw = x + w - sx;
            Box(c, ColRow, sx, y + (h - 28) / 2f, sw, 28, null, UiMain, 8f);
            if (val >= 0f)
            {
                float fill = Mathf.Clamp01(val / EnvMax(v));
                if (fill > 0.02f) Box(c, ColWine, sx, y + (h - 28) / 2f, sw * fill, 28, null, UiMain, 8f);
            }
            Text(c, val < 0f ? T(id, "Default") : val.ToString("0.0#", CultureInfo.InvariantCulture), 12, sx, y + (h - 28) / 2f, sw, 28, TextAnchor.MiddleCenter);
            if (can)
            {
                Btn(c, "−", "bs.ui env.add " + idx + " -1", "0 0 0 0", 16, sx, y + (h - 28) / 2f, 36, 28, ColText, 0f);
                Btn(c, "+", "bs.ui env.add " + idx + " 1", "0 0 0 0", 16, sx + sw - 36, y + (h - 28) / 2f, 36, 28, ColText, 0f);
            }
        }

        private void BuildEnv(CuiElementContainer c, BasePlayer player, float x, float y, float w)
        {
            var id = Id(player);
            bool can = CanWeather(player);
            float colW = (w - 40) / 2f, rx = x + colW + 40;

            SideHead(c, T(id, "EWeather"), x, y, colW);
            if (can) Btn(c, T(id, "ResetAll"), "bs.ui env.resetall 0", "0 0 0 0", 10, x + colW - 120, y, 120, 20, ColRose, 0f);
            SideHead(c, T(id, "EClouds"), rx, y, colW);
            if (can) Btn(c, T(id, "ResetAll"), "bs.ui env.resetall 1", "0 0 0 0", 10, rx + colW - 120, y, 120, 20, ColRose, 0f);
            float ry = y + 32, footer = 26f;
            float rowH = (Bottom - footer - ry) / 10f;

            for (int i = 0; i < WeatherVars.Length; i++)
                EnvRow(c, id, WeatherVars[i], i, can, x, ry + i * rowH, colW, rowH);
            for (int i = 0; i < CloudVars.Length; i++)
                EnvRow(c, id, CloudVars[i], WeatherVars.Length + i, can, rx, ry + i * rowH, colW, rowH);

            // своё время: видишь только ты
            var s = P(id);
            float ty = ry + 9 * rowH;
            Text(c, T(id, "MyTimeShort"), 12, rx, ty, colW * 0.42f, rowH, TextAnchor.MiddleLeft, ColRose);
            float bx = rx + colW * 0.42f;
            Btn(c, "×", "bs.ui time -1", ColBtn, 12, bx, ty + (rowH - 28) / 2f, 28, 28, ColRose, 8f);
            float sx = bx + 34, sw = rx + colW - sx;
            Box(c, ColRow, sx, ty + (rowH - 28) / 2f, sw, 28, null, UiMain, 8f);
            if (s.Time >= 0f) Box(c, ColRed, sx, ty + (rowH - 28) / 2f, Mathf.Max(28f, sw * s.Time / 24f), 28, null, UiMain, 8f);
            Text(c, s.Time < 0f ? T(id, "ServerTime") : s.Time.ToString("0.0", CultureInfo.InvariantCulture), 12, sx, ty + (rowH - 28) / 2f, sw, 28, TextAnchor.MiddleCenter);
            Btn(c, "−", "bs.ui time.add -1", "0 0 0 0", 16, sx, ty + (rowH - 28) / 2f, 36, 28, ColText, 0f);
            Btn(c, "+", "bs.ui time.add 1", "0 0 0 0", 16, sx + sw - 36, ty + (rowH - 28) / 2f, 36, 28, ColText, 0f);

            Text(c, can ? T(id, "EnvFooter") : T(id, "EnvFooterNoPerm"), 10, x, Bottom - footer, w, footer, TextAnchor.MiddleCenter, ColDim, FontReg);
        }

        // ---------- зоны (биомы) ----------

        private class ZoneEntry
        {
            [JsonProperty("Ключ")] public string Key;
            [JsonProperty("Название")] public string Ru;
            [JsonProperty("Название EN")] public string En;
            [JsonProperty("Биом (snow/grass/sand/forest/desert/swamp/cave/lake)")] public string Biome = "grass";
            [JsonProperty("Размер участка (м)")] public float Size = 46f;
            [JsonProperty("Только VIP")] public bool Vip;
            [JsonProperty("Картинка (URL, можно пусто)")] public string Image = "";
            [JsonProperty("Метки (через запятую)")] public string Tags = "";
            [JsonProperty("Точки участков")] public List<float[]> Slots = new List<float[]>();
        }

        private static string BiomeColor(string b)
        {
            switch (b)
            {
                case "snow": return "0.42 0.45 0.50 1";
                case "sand": case "desert": return "0.45 0.33 0.21 1";
                case "forest": return "0.17 0.26 0.15 1";
                case "swamp": return "0.22 0.26 0.17 1";
                case "cave": return "0.16 0.17 0.20 1";
                case "lake": return "0.24 0.33 0.41 1";
                default: return "0.24 0.33 0.18 1";
            }
        }

        private bool IsVip(BasePlayer p) => IsAdminLike(p) || permission.UserHasPermission(p.UserIDString, PermVip);

        private bool SlotTaken(float[] s) => _data.Plots.Any(p => Mathf.Abs(p.X - s[0]) < 1f && Mathf.Abs(p.Z - s[2]) < 1f);

        private int FreeSlots(ZoneEntry z) => z.Slots.Count(s => !SlotTaken(s));

        private void TakeZone(BasePlayer player, string key)
        {
            var id = Id(player);
            var z = _config.Zones.FirstOrDefault(x => x.Key == key);
            if (z == null) return;
            if (z.Vip && !IsVip(player)) { Toast(player, T(id, "ZoneVip"), true); return; }
            var mine = _data.Plots.FirstOrDefault(p => p.Owner == id && p.Zone == key);
            if (mine != null) { GoToPlot(player, mine); return; }
            if (!IsAdminLike(player) && _data.Plots.Count(p => p.Owner == id) >= _config.MaxPlots) { Toast(player, T(id, "PlotLimitShort", _config.MaxPlots), true); return; }
            var slot = z.Slots.FirstOrDefault(s => !SlotTaken(s));
            if (slot == null) { Toast(player, T(id, "ZoneFull"), true); return; }
            var plot = new Plot { Owner = id, OwnerName = player.displayName, Name = Lang(id) == "ru" ? z.Ru : z.En, X = slot[0], Y = slot[1], Z = slot[2], Radius = z.Size / 2f, Zone = key };
            _data.Plots.Add(plot);
            _dirty = true;
            GoToPlot(player, plot);
            ShowPlot(player, plot);
            Toast(player, T(id, "ZoneTaken", plot.Name, z.Size), false);
        }

        private void GoToPlot(BasePlayer player, Plot plot)
        {
            var pos = new Vector3(plot.X, plot.Y, plot.Z);
            if (TerrainMeta.HeightMap != null) pos.y = Mathf.Max(pos.y, TerrainMeta.HeightMap.GetHeight(pos)) + 1f;
            DoTeleport(player, pos);
            CloseUi(player);
        }

        private void ZoneCard(CuiElementContainer c, BasePlayer player, ZoneEntry z, float x, float y, float w, float h, bool locked)
        {
            var id = Id(player);
            int free = FreeSlots(z);
            bool mine = _data.Plots.Any(p => p.Owner == id && p.Zone == z.Key);
            Box(c, mine ? ColRed : ColPanel, x - 2, y - 2, w + 4, h + 4, null, UiMain, 12f);
            Box(c, BiomeColor(z.Biome), x, y, w, h, null, UiMain, 10f);
            if (!string.IsNullOrEmpty(z.Image))
                c.Add(new CuiElement
                {
                    Parent = UiMain,
                    Components =
                    {
                        new CuiRawImageComponent { Url = z.Image },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x + 3} {-y - h + 3}", OffsetMax = $"{x + w - 3} {-y - 32}" }
                    }
                });
            Box(c, "0.07 0.065 0.065 0.86", x, y, w, 30, null, UiMain, 10f);
            Text(c, (Lang(id) == "ru" ? z.Ru : z.En).ToUpper(), 12, x + 10, y, w - 90, 30);
            Text(c, $"{z.Size:0} × {z.Size:0}", 11, x + w - 90, y, 80, 30, TextAnchor.MiddleRight, ColDim);
            float ty = y + 36;
            foreach (var tag in (z.Tags ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Take(3))
            {
                Box(c, ColWine, x + 8, ty, 8 + tag.Length * 7f, 18, null, UiMain, 5f);
                Text(c, tag.ToUpper(), 9, x + 8, ty, 8 + tag.Length * 7f, 18, TextAnchor.MiddleCenter);
                ty += 22;
            }
            string state = mine ? T(id, "ZoneMine") : locked ? T(id, "ZoneVipShort") : T(id, "ZoneFree", free, z.Slots.Count);
            float sw = Mathf.Min(w - 16, 160f);
            Box(c, "0.07 0.065 0.065 0.86", x + w - sw - 8, y + h - 30, sw, 22, null, UiMain, 7f);
            Text(c, state, 10, x + w - sw - 8, y + h - 30, sw, 22, TextAnchor.MiddleCenter, mine ? ColRose : free == 0 ? "0.92 0.42 0.48 1" : ColText);
            if (locked) Box(c, "0.05 0.04 0.04 0.45", x, y, w, h, null, UiMain, 10f);
            Btn(c, "", "bs.ui zone " + z.Key, "0 0 0 0", 9, x, y, w, h, ColText, 0f);
        }

        private void BuildPlots(CuiElementContainer c, BasePlayer player, float x, float y, float w)
        {
            var id = Id(player);
            bool vip = IsVip(player);
            var here = PlotAt(player.transform.position);
            var normal = _config.Zones.Where(z => !z.Vip).ToList();
            var vipZones = _config.Zones.Where(z => z.Vip).ToList();

            // верх: текущий участок слева, обычные зоны справа
            float topH = (Bottom - y) * 0.52f;
            float curW = 300f;
            Box(c, ColPanel, x, y, curW, topH, null, UiMain, 12f);
            Box(c, ColWine, x, y, curW, 30, null, UiMain, 12f);
            Text(c, T(id, "ZoneCurrent"), 12, x + 12, y, curW - 24, 30);
            float iy = y + 40;
            if (here == null)
                Text(c, T(id, "ZoneNone"), 11, x + 14, iy, curW - 28, topH - 50, TextAnchor.UpperLeft, ColDim, FontReg);
            else
            {
                Text(c, here.Name, 16, x + 14, iy, curW - 28, 24); iy += 26;
                Text(c, T(id, "ZoneOwner", here.OwnerName, here.Radius * 2f, here.Members.Count), 10, x + 14, iy, curW - 28, 34, TextAnchor.UpperLeft, ColDim, FontReg); iy += 40;
                float hw = (curW - 28 - 6) / 2f;
                Btn(c, T(id, "PlotShow"), "bs.ui plot.show", ColBtn, 11, x + 14, iy, hw, 30);
                bool can = here.Owner == id || IsAdminLike(player);
                if (can) Btn(c, T(id, "PlotUnclaim"), "bs.ui plot.unclaim", ColBtn, 11, x + 14 + hw + 6, iy, hw, 30, "0.92 0.42 0.48 1");
                iy += 38;
                if (here.Owner == id)
                {
                    Box(c, here.Pvp ? "0.620 0.082 0.188 0.35" : ColRow, x + 14, iy, curW - 28, 28, null, UiMain, 8f);
                    Text(c, T(id, "PlotPvp"), 11, x + 24, iy, curW - 120, 28);
                    Pill(c, id, here.Pvp, "bs.ui plot.pvp", x + curW - 14 - 78, iy + 4);
                    iy += 34;
                    Text(c, T(id, "PlotMembers"), 10, x + 14, iy, curW - 28, 16, TextAnchor.MiddleLeft, ColDim); iy += 20;
                    foreach (var mId in here.Members.Take(2))
                    {
                        var mp = BasePlayer.FindByID(mId);
                        Box(c, ColRow, x + 14, iy, curW - 28, 26, null, UiMain, 8f);
                        Text(c, mp?.displayName ?? mId.ToString(), 11, x + 24, iy, curW - 80, 26);
                        Btn(c, "X", "bs.ui plot.kick " + mId, ColBtn, 10, x + curW - 14 - 30, iy + 3, 26, 20, "0.92 0.42 0.48 1");
                        iy += 30;
                    }
                    var online = BasePlayer.activePlayerList.Where(p => p != player && !here.Members.Contains(Id(p))).Take(2).ToList();
                    for (int k = 0; k < online.Count && iy + 26 < y + topH - 8; k++)
                    {
                        Btn(c, "+ " + online[k].displayName, "bs.ui plot.add " + Id(online[k]), ColBtn, 10, x + 14 + k * (hw + 6), iy, hw, 26);
                    }
                }
            }

            float zx = x + curW + G2, zw = x + w - zx;
            Text(c, T(id, "ZoneSmall"), 18, zx, y, zw, 30, TextAnchor.MiddleCenter);
            int n = Mathf.Max(1, Mathf.Min(4, normal.Count));
            float cardW = (zw - (n - 1) * G2) / n;
            for (int i = 0; i < normal.Count && i < 4; i++)
                ZoneCard(c, player, normal[i], zx + i * (cardW + G2), y + 40, cardW, topH - 40, false);
            if (normal.Count == 0) Text(c, T(id, "ZoneNoZones"), 11, zx, y + 40, zw, topH - 40, TextAnchor.MiddleCenter, ColDim, FontReg);

            // низ: VIP-зоны
            float vy = y + topH + G2, vh = Bottom - vy;
            Box(c, ColPanel, x, vy, w, vh, null, UiMain, 12f);
            Box(c, ColRed, x, vy, 30, vh, null, UiMain, 12f);
            Text(c, "V\nI\nP", 13, x, vy, 30, vh, TextAnchor.MiddleCenter);
            Text(c, T(id, "ZoneVipTitle"), 15, x + 46, vy + 8, 400, 26);
            Text(c, vip ? T(id, "ZoneVipYes") : T(id, "ZoneVipBuy"), 11, x + w - 420, vy + 8, 404, 26, TextAnchor.MiddleRight, vip ? ColRose : ColDim, FontReg);
            int vn = Mathf.Max(1, Mathf.Min(6, vipZones.Count));
            float vx = x + 46, vw = w - 46 - 14;
            float vcw = (vw - (vn - 1) * G2) / vn;
            for (int i = 0; i < vipZones.Count && i < 6; i++)
                ZoneCard(c, player, vipZones[i], vx + i * (vcw + G2), vy + 44, vcw, vh - 44 - 14, !vip);
            if (vipZones.Count == 0) Text(c, T(id, "ZoneNoZones"), 11, vx, vy + 44, vw, vh - 58, TextAnchor.MiddleCenter, ColDim, FontReg);
        }

        [ChatCommand("bsadmin")]
        private void CmdBsAdmin(BasePlayer player, string command, string[] args)
        {
            if (!IsAdminLike(player)) { Reply(player, "NoPerm"); return; }
            var id = Id(player);
            var sub = args.Length > 0 ? args[0].ToLower() : "";
            var pos = player.transform.position;
            switch (sub)
            {
                case "zone":
                {
                    // /bsadmin zone <ключ> <биом> <размер> [vip] [Название]
                    if (args.Length < 4) break;
                    float size;
                    float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out size);
                    bool vip = args.Length > 4 && args[4].ToLower() == "vip";
                    string name = string.Join(" ", args.Skip(vip ? 5 : 4).ToArray());
                    var z = _config.Zones.FirstOrDefault(x => x.Key == args[1]);
                    if (z == null) _config.Zones.Add(z = new ZoneEntry { Key = args[1] });
                    z.Biome = args[2].ToLower();
                    z.Size = size > 4f ? size : 46f;
                    z.Vip = vip;
                    if (name.Length > 0) { z.Ru = name; z.En = name; }
                    if (string.IsNullOrEmpty(z.Ru)) { z.Ru = z.Key; z.En = z.Key; }
                    SaveConfig();
                    player.ChatMessage($"Зона {z.Key}: {z.Biome}, {z.Size} м, VIP={z.Vip}");
                    return;
                }
                case "slot":
                {
                    var z = args.Length > 1 ? _config.Zones.FirstOrDefault(x => x.Key == args[1]) : null;
                    if (z == null) break;
                    z.Slots.Add(new[] { pos.x, pos.y, pos.z });
                    SaveConfig();
                    player.ChatMessage($"Зона {z.Key}: участок #{z.Slots.Count} в {pos}");
                    return;
                }
                case "grid":
                {
                    // /bsadmin grid <ключ> <столбцов> <рядов> — сетка участков от тебя вперёд-вправо, шаг = размер + 10 м
                    var z = args.Length > 3 ? _config.Zones.FirstOrDefault(x => x.Key == args[1]) : null;
                    int cols, rows;
                    if (z == null || !int.TryParse(args[2], out cols) || !int.TryParse(args[3], out rows)) break;
                    float step = z.Size + 10f;
                    for (int i = 0; i < cols; i++)
                        for (int j = 0; j < rows; j++)
                        {
                            var p = pos + new Vector3(i * step, 0f, j * step);
                            if (TerrainMeta.HeightMap != null) p.y = TerrainMeta.HeightMap.GetHeight(p);
                            z.Slots.Add(new[] { p.x, p.y, p.z });
                        }
                    SaveConfig();
                    player.ChatMessage($"Зона {z.Key}: участков теперь {z.Slots.Count}");
                    return;
                }
                case "delzone":
                    if (args.Length > 1) { _config.Zones.RemoveAll(x => x.Key == args[1]); SaveConfig(); player.ChatMessage("Удалено"); return; }
                    break;
                case "zones":
                    player.ChatMessage(string.Join("\n", _config.Zones.Select(z => $"{z.Key} · {z.Biome} · {z.Size} м · {FreeSlots(z)}/{z.Slots.Count} · VIP={z.Vip}").ToArray()));
                    return;
            }
            player.ChatMessage("/bsadmin zone <ключ> <snow|grass|sand|forest|swamp|cave|lake> <размер> [vip] [Название]\n/bsadmin slot <ключ> — участок здесь\n/bsadmin grid <ключ> <столбцов> <рядов>\n/bsadmin zones · /bsadmin delzone <ключ>");
        }

        #endregion

        #region HUD стройки: цена постройки, рейд-калькулятор, панель материала

        private const string UiCost = "BS.Cost";
        private const string UiBar = "BS.Bar";

        private class GradeSkin
        {
            [JsonProperty("Материал (0 солома … 4 МВК)")] public int Grade;
            [JsonProperty("Название")] public string Ru;
            [JsonProperty("Название EN")] public string En;
            [JsonProperty("Скин (0 = обычный)")] public ulong Skin;
            [JsonProperty("Цвет плитки")] public string Color = "0.5 0.5 0.5 1";
        }

        private static List<GradeSkin> DefaultSkins() => new List<GradeSkin>
        {
            new GradeSkin { Grade = 0, Ru = "Солома", En = "Twig", Color = "0.60 0.50 0.34 1" },
            new GradeSkin { Grade = 1, Ru = "Дерево", En = "Wood", Color = "0.52 0.34 0.20 1" },
            new GradeSkin { Grade = 1, Ru = "Фронтир", En = "Frontier", Skin = 10232, Color = "0.62 0.44 0.26 1" },
            new GradeSkin { Grade = 2, Ru = "Камень", En = "Stone", Color = "0.56 0.54 0.51 1" },
            new GradeSkin { Grade = 2, Ru = "Брутализм", En = "Brutalist", Skin = 10225, Color = "0.47 0.45 0.43 1" },
            new GradeSkin { Grade = 2, Ru = "Кирпич", En = "Brick", Skin = 10223, Color = "0.54 0.29 0.22 1" },
            new GradeSkin { Grade = 2, Ru = "Адоби", En = "Adobe", Skin = 10220, Color = "0.65 0.52 0.36 1" },
            new GradeSkin { Grade = 3, Ru = "Металл", En = "Metal", Color = "0.43 0.43 0.45 1" },
            new GradeSkin { Grade = 3, Ru = "Контейнер", En = "Container", Skin = 10221, Color = "0.50 0.25 0.20 1" },
            new GradeSkin { Grade = 4, Ru = "МВК", En = "HQM", Color = "0.32 0.37 0.44 1" },
        };

        // урон одной штуки по стене материала (≈ HP стены / сколько нужно): ракета, C4, сэтчел, взрывной патрон
        private static readonly string[] RaidItems = { "ammo.rocket.basic", "explosive.timed", "explosive.satchel", "ammo.rifle.explosive" };
        private static readonly float[] WallHp = { 10f, 250f, 500f, 1000f, 2000f };
        private static readonly int[][] WallRaid =
        {
            new[] { 1, 1, 1, 1 },
            new[] { 2, 1, 3, 49 },
            new[] { 4, 2, 10, 185 },
            new[] { 8, 4, 23, 400 },
            new[] { 15, 8, 46, 799 },
        };

        private static int RaidCount(BaseCombatEntity e, int item)
        {
            if (e == null) return 0;
            int g;
            var b = e as BuildingBlock;
            if (b != null) g = (int)b.grade;
            else
            {
                var sp = e.ShortPrefabName ?? "";
                g = sp.Contains("toptier") ? 4 : sp.Contains("metal") || sp.Contains("garage") ? 3 : sp.Contains("stone") ? 2 : 1;
            }
            g = Mathf.Clamp(g, 0, 4);
            float perItem = WallHp[g] / WallRaid[g][item];
            return Mathf.Max(1, Mathf.CeilToInt(e.Health() / perItem));
        }

        private static object Member(object o, string name)
        {
            if (o == null) return null;
            var t = o.GetType();
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(o);
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return p != null ? p.GetValue(o, null) : null;
        }

        private static void AddAmounts(Dictionary<int, int> to, object list, float mult = 1f)
        {
            var en = list as System.Collections.IEnumerable;
            if (en == null) return;
            foreach (var ia in en)
            {
                var def = Member(ia, "itemDef") as ItemDefinition;
                var amt = Member(ia, "amount");
                if (def == null || amt == null) continue;
                int n = Mathf.RoundToInt(Convert.ToSingle(amt) * mult), cur;
                to[def.itemid] = (to.TryGetValue(def.itemid, out cur) ? cur : 0) + n;
            }
        }

        private Dictionary<string, ItemDefinition> _deployItems;

        private ItemDefinition DeployItem(BaseEntity e)
        {
            if (_deployItems == null)
            {
                _deployItems = new Dictionary<string, ItemDefinition>();
                foreach (var def in ItemManager.itemList)
                {
                    var dep = def.GetComponent<ItemModDeployable>();
                    var path = dep != null && dep.entityPrefab != null ? dep.entityPrefab.resourcePath : null;
                    if (!string.IsNullOrEmpty(path) && !_deployItems.ContainsKey(path)) _deployItems[path] = def;
                }
            }
            ItemDefinition d;
            return e != null && e.PrefabName != null && _deployItems.TryGetValue(e.PrefabName, out d) ? d : null;
        }

        private Dictionary<int, int> BuildingCost(BuildingManager.Building b, ToolSettings t)
        {
            var cost = new Dictionary<int, int>();
            foreach (var block in b.buildingBlocks.ToList())
            {
                if (block == null || block.IsDestroyed) continue;
                if (!t.CostTwig && (int)block.grade == 0) continue;
                AddAmounts(cost, TryCall(block, "BuildCost"));
            }
            foreach (var e in b.decayEntities.ToList())
            {
                if (e == null || e.IsDestroyed || e is BuildingBlock) continue;
                var def = DeployItem(e);
                if (def == null) continue;
                if (t.CostDeploy)
                {
                    var bp = Member(def, "Blueprint");
                    AddAmounts(cost, Member(bp, "ingredients"));
                }
                else { int cur; cost[def.itemid] = (cost.TryGetValue(def.itemid, out cur) ? cur : 0) + 1; }
            }
            return cost;
        }

        private Dictionary<int, int> UpkeepCost(BuildingManager.Building b)
        {
            var cost = new Dictionary<int, int>();
            var priv = TryCall(b, "GetDominatingBuildingPrivilege") as BuildingPrivlidge;
            if (priv == null) return cost;
            try
            {
                var m = priv.GetType().GetMethod("CalculateUpkeepCostAmounts", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null) return cost;
                var list = Activator.CreateInstance(m.GetParameters()[0].ParameterType);
                m.Invoke(priv, new[] { list });
                AddAmounts(cost, list);
            }
            catch { }
            return cost;
        }

        private readonly Dictionary<ulong, string> _hudKey = new Dictionary<ulong, string>();
        private readonly HashSet<ulong> _barShown = new HashSet<ulong>();

        private void HudTick()
        {
            foreach (var p in BasePlayer.activePlayerList.ToList())
            {
                var id = Id(p);
                if (!Has(p, PermUse)) { _hudKey.Remove(id); if (_barShown.Remove(id)) { CuiHelper.DestroyUi(p, UiCost); CuiHelper.DestroyUi(p, UiBar); } continue; }
                var t = P(id).Tools;
                if (t.HudBar && _barShown.Add(id)) DrawBar(p);
                if (!t.HudBar && _barShown.Remove(id)) CuiHelper.DestroyUi(p, UiBar);
                if (t.HudCost) DrawCost(p, false); else if (_hudKey.Remove(id)) CuiHelper.DestroyUi(p, UiCost);
            }
        }

        private void DrawCost(BasePlayer player, bool force)
        {
            var id = Id(player);
            var t = P(id).Tools;
            var ui = Ui(id);
            BaseEntity anchor;
            var building = BuildingNear(player, out anchor);
            var rows = new List<KeyValuePair<int, string>>();
            string title, foot = "";

            if (ui.CostPage == 1)
            {
                var target = anchor as BaseCombatEntity;
                title = target == null ? T(id, "HudRaid") : T(id, "HudRaidOf", RaidName(id, target));
                for (int i = 0; i < RaidItems.Length; i++)
                {
                    var def = ItemManager.FindItemDefinition(RaidItems[i]);
                    if (def != null) rows.Add(new KeyValuePair<int, string>(def.itemid, target == null ? "—" : RaidCount(target, i).ToString()));
                }
                if (building != null)
                {
                    int total = building.buildingBlocks.Where(x => x != null && !x.IsDestroyed).Sum(x => RaidCount(x, 0));
                    foot = T(id, "HudRaidAll", total);
                }
            }
            else
            {
                title = T(id, t.CostUpkeep ? "HudUpkeep" : "HudCost");
                if (building != null)
                {
                    var cost = t.CostUpkeep ? UpkeepCost(building) : BuildingCost(building, t);
                    foreach (var kv in cost.Where(x => x.Value > 0).OrderByDescending(x => x.Value).Take(10))
                        rows.Add(new KeyValuePair<int, string>(kv.Key, kv.Value.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ")));
                }
                if (building == null) foot = T(id, "HudLook");
            }

            string key = ui.CostPage + "|" + title + "|" + string.Join(";", rows.Select(r => r.Key + "=" + r.Value).ToArray()) + "|" + foot + t.CostTwig + t.CostUpkeep + t.CostDeploy;
            string old;
            if (!force && _hudKey.TryGetValue(id, out old) && old == key) return;
            _hudKey[id] = key;

            const float w = 230f, rh = 24f;
            float h = 28 + Mathf.Max(1, rows.Count) * rh + (ui.CostPage == 0 ? 3 * 20 + 12 : 0) + (foot.Length > 0 ? 22 : 0) + 32;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0.07 0.065 0.065 0.88" }, RectTransform = { AnchorMin = "1 1", AnchorMax = "1 1", OffsetMin = $"{-w - 16} {-h - 16}", OffsetMax = "-16 -16" } }, "Hud", UiCost);
            HudBtn(c, UiCost, "<", "bs.hud page", "0 0 0 0", 0, 0, 26, 28, ColRose, h);
            HudBox(c, UiCost, ColWine, 28, 3, w - 56, 22, h);
            HudText(c, UiCost, title, 11, 28, 3, w - 56, 22, TextAnchor.MiddleCenter, ColText, h);
            HudBtn(c, UiCost, ">", "bs.hud page", "0 0 0 0", w - 26, 0, 26, 28, ColRose, h);
            float y = 28;
            if (rows.Count == 0) { HudText(c, UiCost, "—", 11, 0, y, w, rh, TextAnchor.MiddleCenter, ColDim, h); y += rh; }
            for (int i = 0; i < rows.Count; i++)
            {
                HudBox(c, UiCost, i % 2 == 0 ? "1 1 1 0.06" : "1 1 1 0.03", 0, y, w, rh, h);
                c.Add(new CuiElement { Parent = UiCost, Components = { new CuiImageComponent { ItemId = rows[i].Key }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"6 {-y - rh + 3}", OffsetMax = $"{6 + rh - 6} {-y - 3}" } } });
                HudText(c, UiCost, rows[i].Value, 12, 0, y, w - 10, rh, TextAnchor.MiddleRight, ColText, h);
                y += rh;
            }
            if (ui.CostPage == 0)
            {
                y += 6;
                HudCheck(c, id, "HudTwig", t.CostTwig, "bs.hud twig", y, h); y += 20;
                HudCheck(c, id, "HudShowUpkeep", t.CostUpkeep, "bs.hud upkeep", y, h); y += 20;
                HudCheck(c, id, "HudBreakdown", t.CostDeploy, "bs.hud deploy", y, h); y += 20;
                y += 6;
            }
            if (foot.Length > 0) { HudText(c, UiCost, foot, 10, 10, y, w - 20, 22, TextAnchor.MiddleLeft, ColDim, h); y += 22; }
            HudBtn(c, UiCost, ui.CostPage == 0 ? T(id, "HudRaidBtn") : T(id, "HudCostBtn"), "bs.hud page", ColWine, 0, y, w, 32, ColText, h);

            CuiHelper.DestroyUi(player, UiCost);
            AddUiScaled(player, c);
        }

        private string RaidName(ulong id, BaseCombatEntity e)
        {
            var b = e as BuildingBlock;
            if (b != null) return T(id, GradeKeys[Mathf.Clamp((int)b.grade, 0, 4)]).ToUpper();
            var def = DeployItem(e);
            return def != null ? def.displayName.english.ToUpper() : (e.ShortPrefabName ?? "?").ToUpper();
        }

        // элементы HUD позиционируются от верхнего левого угла панели высотой h
        private static void HudBox(CuiElementContainer c, string parent, string color, float x, float y, float w, float hh, float h)
            => c.Add(new CuiPanel { Image = { Color = color }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - hh}", OffsetMax = $"{x + w} {-y}" } }, parent);

        private static void HudText(CuiElementContainer c, string parent, string text, int size, float x, float y, float w, float hh, TextAnchor align, string color, float h)
            => c.Add(new CuiLabel { Text = { Text = text, FontSize = size, Align = align, Color = color, Font = Font }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - hh}", OffsetMax = $"{x + w} {-y}" } }, parent);

        private static void HudBtn(CuiElementContainer c, string parent, string text, string cmd, string color, float x, float y, float w, float hh, string textColor, float h, int fs = 11)
            => c.Add(new CuiButton { Button = { Command = cmd, Color = color }, Text = { Text = text, FontSize = fs, Align = TextAnchor.MiddleCenter, Color = textColor, Font = Font }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - hh}", OffsetMax = $"{x + w} {-y}" } }, parent);

        private void HudCheck(CuiElementContainer c, ulong id, string key, bool on, string cmd, float y, float h)
        {
            HudBox(c, UiCost, on ? ColWine : "0 0 0 0.5", 10, y + 3, 13, 13, h);
            HudText(c, UiCost, T(id, key), 10, 30, y, 190, 19, TextAnchor.MiddleLeft, ColText, h);
            HudBtn(c, UiCost, "", cmd, "0 0 0 0", 0, y, 230, 19, ColText, h);
        }

        private void DrawBar(BasePlayer player)
        {
            var id = Id(player);
            var t = P(id).Tools;
            var skins = _config.Skins ?? new List<GradeSkin>();
            var c = new CuiElementContainer();
            const float w = 340f, h = 112f;
            c.Add(new CuiPanel { Image = { Color = "0.07 0.065 0.065 0.88" }, RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = $"{-640} 18", OffsetMax = $"{-640 + w} {18 + h}" } }, "Hud", UiBar);

            var shown = skins.Where(s => t.AutoGrade < 0 || s.Grade == t.AutoGrade || t.ShowAllSkins).Take(7).ToList();
            float tile = 40f, tx = (w - shown.Count * tile - (shown.Count - 1) * 5) / 2f;
            for (int i = 0; i < shown.Count; i++)
            {
                var s = shown[i];
                bool sel = s.Grade == t.AutoGrade && s.Skin == t.Skin;
                if (sel) HudBox(c, UiBar, ColRed, tx + i * (tile + 5) - 2, 6, tile + 4, tile + 4, h);
                HudBox(c, UiBar, s.Color, tx + i * (tile + 5), 8, tile, tile, h);
                HudBtn(c, UiBar, "", "bs.hud skin " + skins.IndexOf(s), "0 0 0 0", tx + i * (tile + 5), 8, tile, tile, ColText, h);
            }
            var cur = skins.FirstOrDefault(s => s.Grade == t.AutoGrade && s.Skin == t.Skin);
            bool ru = Lang(id) == "ru";
            string gname = t.AutoGrade < 0 ? T(id, "HudAsPlaced") : T(id, GradeKeys[t.AutoGrade]);
            HudText(c, UiBar, T(id, "HudSelected", gname, cur == null ? "—" : ru ? cur.Ru : cur.En), 10, 8, 52, w - 16, 16, TextAnchor.MiddleLeft, ColDim, h);

            float bw = (w - 16 - 5 * 3) / 6f;
            string[] grades = { "G0", "G1", "G2", "G3", "G4" };
            for (int g = 0; g < 5; g++)
                HudBtn(c, UiBar, T(id, grades[g]).ToUpper(), "bs.hud grade " + g, t.AutoGrade == g ? ColWine : "0 0 0 0.5", 8 + g * (bw + 3), 70, bw, 18, ColText, h, 9);
            HudBtn(c, UiBar, T(id, "HudSkins"), "bs.hud allskins", t.ShowAllSkins ? ColWine : "0 0 0 0.5", 8 + 5 * (bw + 3), 70, bw, 18, ColText, h, 9);
            string[] acts = { "HudMenu", "HudSym", "LLoad", "LSave", "LClear", "LPrivate" };
            string[] cmds = { "bs.hud menu", "bs.hud sym", "bs.ui plot.load", "bs.ui plot.save", "bs.ui plot.clear", "bs.ui plot.private" };
            var plot = PlotAt(player.transform.position);
            for (int i = 0; i < acts.Length; i++)
            {
                bool on = (i == 1 && t.SymMode > 0) || (i == 5 && plot != null && plot.Private);
                HudBtn(c, UiBar, T(id, acts[i] + "Short"), cmds[i], on ? ColWine : "0 0 0 0.5", 8 + i * (bw + 3), 91, bw, 18, i == 4 ? "0.92 0.42 0.48 1" : ColText, h, 9);
            }
            CuiHelper.DestroyUi(player, UiBar);
            AddUiScaled(player, c);
        }

        private void RefreshHud(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;
            var t = P(Id(player)).Tools;
            if (Has(player, PermUse) && t.HudBar) { DrawBar(player); _barShown.Add(Id(player)); } else { CuiHelper.DestroyUi(player, UiBar); _barShown.Remove(Id(player)); }
            if (Has(player, PermUse) && t.HudCost) DrawCost(player, true); else { CuiHelper.DestroyUi(player, UiCost); _hudKey.Remove(Id(player)); }
        }

        private void ApplyGrade(BuildingBlock b, int grade, ulong skin)
        {
            if (b == null || grade < 0) return;
            var g = (BuildingGrade.Enum)Mathf.Clamp(grade, 0, 4);
            if (TryCall(b, "ChangeGradeAndSkin", g, skin, false, true) == null)
            {
                if (TryCall(b, "ChangeGrade", g, false, true) == null) b.SetGrade(g);
                b.skinID = skin;
            }
            b.SetHealth(b.MaxHealth());
            b.SendNetworkUpdate();
        }

        [ConsoleCommand("bs.hud")]
        private void CcHud(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!Has(player, PermUse)) return;
            var id = Id(player);
            var t = P(id).Tools;
            var ui = Ui(id);
            int n = arg.GetInt(1, 0);
            switch (arg.GetString(0))
            {
                case "page": ui.CostPage = 1 - ui.CostPage; break;
                case "twig": t.CostTwig = !t.CostTwig; break;
                case "upkeep": t.CostUpkeep = !t.CostUpkeep; break;
                case "deploy": t.CostDeploy = !t.CostDeploy; break;
                case "grade": t.AutoGrade = t.AutoGrade == n ? -1 : Mathf.Clamp(n, 0, 4); t.Skin = 0; break;
                case "skin":
                    if (_config.Skins != null && n >= 0 && n < _config.Skins.Count) { t.AutoGrade = _config.Skins[n].Grade; t.Skin = _config.Skins[n].Skin; }
                    break;
                case "allskins": t.ShowAllSkins = !t.ShowAllSkins; break;
                case "menu": OpenUi(player); return;
                case "sym": if (t.SymMode > 0) t.SymMode = 0; else { t.SymMode = 3; if (!t.SymSet) SetSymCenter(player); } break;
                case "bar": t.HudBar = !t.HudBar; break;
                case "cost": t.HudCost = !t.HudCost; break;
            }
            _dirty = true;
            RefreshHud(player);
        }

        #endregion

        #region 1.3: рейд с другом, путь до ТШ, мгновенный крафт, электрика, спавн, PvP

        // ---------- мгновенный бесплатный крафт: во время рейд-теста и рейда с другом ----------

        private bool FreeCraft(BasePlayer p)
        {
            if (p == null) return false;
            var id = Id(p);
            if (_raidTests.ContainsKey(id)) return true;
            float now = Time.realtimeSinceStartup;
            return _friendRaids.Values.Any(f => (f.Owner == id || f.Friend == id) && now < f.Until);
        }

        private object CanCraft(ItemCrafter crafter, ItemBlueprint bp, int amount, bool free)
        {
            var p = Member(crafter, "owner") as BasePlayer;
            return p != null && FreeCraft(p) ? (object)true : null;
        }

        private object OnItemCraft(ItemCraftTask task, BasePlayer owner, Item fromTempBlueprint)
        {
            if (owner == null || task?.blueprint?.targetItem == null || !FreeCraft(owner)) return null;
            var it = ItemManager.Create(task.blueprint.targetItem, Mathf.Max(1, task.blueprint.amountToCreate) * Mathf.Max(1, task.amount), 0UL);
            if (it != null) owner.GiveItem(it);
            return false;
        }

        // ---------- друг рейдит базу с таймером ----------

        private class FriendRaid { public ulong Owner, Friend; public string FriendName; public float Until; public Timer T; }
        private readonly Dictionary<ulong, FriendRaid> _friendRaids = new Dictionary<ulong, FriendRaid>();
        private readonly Dictionary<ulong, ulong> _friendPick = new Dictionary<ulong, ulong>();
        private readonly Dictionary<ulong, int> _friendMin = new Dictionary<ulong, int>();

        private bool FriendMayHit(Plot plot, BasePlayer attacker)
        {
            FriendRaid f;
            return plot != null && attacker != null && _friendRaids.TryGetValue(plot.Owner, out f) && f.Friend == Id(attacker) && Time.realtimeSinceStartup < f.Until;
        }

        private void FriendRaidStart(BasePlayer player)
        {
            var id = Id(player);
            ulong fid;
            if (!_friendPick.TryGetValue(id, out fid)) { Toast(player, T(id, "FrPick"), true); return; }
            var friend = BasePlayer.FindByID(fid);
            if (friend == null || !friend.IsConnected) { Toast(player, T(id, "PlayerNotFound"), true); return; }
            if (!_raidTests.ContainsKey(id)) { RaidStart(player); if (!_raidTests.ContainsKey(id)) return; }
            int min; if (!_friendMin.TryGetValue(id, out min)) min = _config.FriendRaidMinutes.FirstOrDefault();
            FriendRaidStop(id, false);
            var fr = new FriendRaid { Owner = id, Friend = fid, FriendName = friend.displayName, Until = Time.realtimeSinceStartup + min * 60f };
            fr.T = timer.Once(min * 60f, () => FriendRaidStop(id, true));
            _friendRaids[id] = fr;
            Toast(player, T(id, "FrStarted", friend.displayName, min), false);
            Toast(friend, T(fid, "FrInvited", player.displayName, min), false);
            if (_config.FriendRaidKit) foreach (var k in _config.RaidKit) Give(friend, k.Item, k.Amount);
        }

        private void FriendRaidStop(ulong owner, bool timeUp)
        {
            FriendRaid f;
            if (!_friendRaids.TryGetValue(owner, out f)) return;
            _friendRaids.Remove(owner);
            f.T?.Destroy();
            var op = BasePlayer.FindByID(owner);
            var fp = BasePlayer.FindByID(f.Friend);
            if (fp != null) Toast(fp, T(f.Friend, timeUp ? "FrTimeUp" : "FrStopped"), true);
            if (op != null)
            {
                Toast(op, T(owner, timeUp ? "FrTimeUp" : "FrStopped"), true);
                if (timeUp && _config.FriendRaidAutoRestore) RaidRestore(op);
            }
        }

        // ---------- путь до шкафа: где базу пробить дешевле всего ----------

        private static readonly int[] RaidSulfur = { 1400, 2200, 480, 25 };   // ракета, C4, сэтчел, взрывной патрон
        private class PathReport { public string Dir; public int Sulfur; public List<string> Lines = new List<string>(); public List<Vector3> Points = new List<Vector3>(); }
        private readonly Dictionary<ulong, PathReport> _pathReport = new Dictionary<ulong, PathReport>();

        private void RaidPath(BasePlayer player)
        {
            var id = Id(player);
            BaseEntity looked;
            var building = LookBuilding(player, out looked);
            if (building == null) { Reply(player, "LookAtBuilding"); return; }
            var tc = building.decayEntities.OfType<BuildingPrivlidge>().FirstOrDefault();
            if (tc == null) { Toast(player, T(id, "PathNoTc"), true); return; }
            var target = tc.transform.position + Vector3.up * 0.6f;
            var dirs = new List<KeyValuePair<string, Vector3>>();
            string[] names = { "DirN", "DirNE", "DirE", "DirSE", "DirS", "DirSW", "DirW", "DirNW" };
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                dirs.Add(new KeyValuePair<string, Vector3>(names[i], new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a))));
            }
            dirs.Add(new KeyValuePair<string, Vector3>("DirTop", Vector3.up));
            int mask = LayerMask.GetMask("Construction", "Deployed");
            PathReport best = null;
            foreach (var d in dirs)
            {
                var origin = target + d.Value * 45f;
                var hits = Physics.RaycastAll(origin, d.Value * -1f, 45f, mask, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToList();
                var rep = new PathReport { Dir = T(id, d.Key) };
                var seen = new HashSet<BaseEntity>();
                foreach (var h in hits)
                {
                    var e = h.GetEntity() as BaseCombatEntity;
                    if (e == null || e == tc || !seen.Add(e)) continue;
                    if (e.Health() <= 0f) continue;
                    // самый дешёвый по сере способ пробить этот слой
                    int bestItem = 0, bestS = int.MaxValue;
                    for (int k = 0; k < RaidItems.Length; k++) { int s = RaidCount(e, k) * RaidSulfur[k]; if (s < bestS) { bestS = s; bestItem = k; } }
                    rep.Sulfur += bestS;
                    rep.Lines.Add($"{RaidName(id, e)} — {RaidCount(e, bestItem)} × {T(id, "RI_" + bestItem)}");
                    rep.Points.Add(e.transform.position + Vector3.up * 1.2f);
                }
                if (rep.Lines.Count == 0) continue;
                if (best == null || rep.Sulfur < best.Sulfur) best = rep;
            }
            if (best == null) { Toast(player, T(id, "PathOpen"), false); _pathReport.Remove(id); return; }
            best.Points.Add(target);
            _pathReport[id] = best;
            WithAdmin(player, () =>
            {
                var col = new Color(0.94f, 0.65f, 0.69f);
                for (int i = 0; i + 1 < best.Points.Count; i++) player.SendConsoleCommand("ddraw.arrow", 30f, col, best.Points[i], best.Points[i + 1], 0.4f);
                for (int i = 0; i < best.Lines.Count; i++) player.SendConsoleCommand("ddraw.text", 30f, col, best.Points[i], $"<size=14>{i + 1}. {best.Lines[i]}</size>");
                player.SendConsoleCommand("ddraw.text", 30f, Color.white, target, "<size=18>ТШ</size>");
            });
        }

        // ---------- спавн объектов ----------

        private class SpawnDef
        {
            [JsonProperty("Группа (trees | ore | loot | npc)")] public string Cat;
            [JsonProperty("Название")] public string Name;
            [JsonProperty("Название EN")] public string En;
            [JsonProperty("Префаб")] public string Prefab;
        }

        private static List<SpawnDef> DefaultSpawns()
        {
            Func<string, string, string, string, SpawnDef> S = (c, n, e, p) => new SpawnDef { Cat = c, Name = n, En = e, Prefab = p };
            return new List<SpawnDef>
            {
                S("trees", "Сосна", "Pine", "assets/bundled/prefabs/autospawn/resource/v3_temp_forest_pine/pine_a.prefab"),
                S("trees", "Дуб", "Oak", "assets/bundled/prefabs/autospawn/resource/v3_temp_forest/oak_b.prefab"),
                S("trees", "Берёза", "Birch", "assets/bundled/prefabs/autospawn/resource/v3_temp_forest/birch_big_temp.prefab"),
                S("trees", "Пальма", "Palm", "assets/bundled/prefabs/autospawn/resource/v3_arid_forest/palm_tree_tall_a_entity.prefab"),
                S("trees", "Куст", "Bush", "assets/bundled/prefabs/autospawn/resource/v3_bushes_temp/bush_willow_a.prefab"),
                S("ore", "Камень", "Stone ore", "assets/bundled/prefabs/autospawn/resource/ores/stone-ore.prefab"),
                S("ore", "Металл", "Metal ore", "assets/bundled/prefabs/autospawn/resource/ores/metal-ore.prefab"),
                S("ore", "Сера", "Sulfur ore", "assets/bundled/prefabs/autospawn/resource/ores/sulfur-ore.prefab"),
                S("ore", "Валун", "Boulder", "assets/bundled/prefabs/autospawn/resource/rocks/rock_formation_a.prefab"),
                S("loot", "Обычный ящик", "Crate", "assets/bundled/prefabs/radtown/crate_normal_2.prefab"),
                S("loot", "Военный ящик", "Military crate", "assets/bundled/prefabs/radtown/crate_normal.prefab"),
                S("loot", "Элитный ящик", "Elite crate", "assets/bundled/prefabs/radtown/crate_elite.prefab"),
                S("loot", "Ящик с инструментами", "Tool crate", "assets/bundled/prefabs/radtown/crate_tools.prefab"),
                S("loot", "Бочка", "Barrel", "assets/bundled/prefabs/radtown/loot_barrel_1.prefab"),
                S("npc", "Учёный", "Scientist", "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roam.prefab"),
                S("npc", "Турель", "Auto turret", "assets/prefabs/npc/autoturret/autoturret_deployed.prefab"),
                S("npc", "ПВО", "SAM site", "assets/prefabs/npc/sam_site_turret/sam_site_turret_deployed.prefab"),
                S("npc", "Огнемётная турель", "Flame turret", "assets/prefabs/npc/flame turret/flameturret.deployed.prefab"),
                S("npc", "Дробовик-ловушка", "Shotgun trap", "assets/prefabs/deployable/single shot trap/guntrap.deployed.prefab"),
            };
        }

        private static readonly string[] SpawnCats = { "trees", "ore", "loot", "npc" };
        private readonly Dictionary<ulong, List<BaseEntity>> _spawned = new Dictionary<ulong, List<BaseEntity>>();

        private Vector3? LookPoint(BasePlayer player, float dist = 40f)
        {
            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, dist, LayerMask.GetMask("Terrain", "World", "Construction", "Default"), QueryTriggerInteraction.Ignore)) return null;
            return hit.point;
        }

        private void SpawnObject(BasePlayer player, int index)
        {
            var id = Id(player);
            if (index < 0 || index >= _config.Spawns.Count) return;
            var def = _config.Spawns[index];
            var at = LookPoint(player);
            if (at == null) { Toast(player, T(id, "SpLook"), true); return; }
            var plot = PlotAt(at.Value);
            if (plot != null && !IsMember(plot, player) && !IsAdminLike(player)) { Toast(player, T(id, "PlotNoBuild", plot.OwnerName ?? "?"), true); return; }
            List<BaseEntity> list;
            if (!_spawned.TryGetValue(id, out list)) _spawned[id] = list = new List<BaseEntity>();
            list.RemoveAll(e => e == null || e.IsDestroyed);
            if (!IsAdminLike(player) && list.Count >= _config.SpawnLimit) { Toast(player, T(id, "SpLimit", _config.SpawnLimit), true); return; }
            var e2 = GameManager.server.CreateEntity(def.Prefab, at.Value, Quaternion.Euler(0f, player.eyes.rotation.eulerAngles.y + 180f, 0f));
            if (e2 == null) { Toast(player, T(id, "SpBad"), true); return; }
            e2.OwnerID = id;
            e2.enableSaving = false;
            e2.Spawn();
            list.Add(e2);
            Toast(player, T(id, "SpDone", Lang(id) == "ru" ? def.Name : def.En ?? def.Name, list.Count, _config.SpawnLimit), false);
        }

        private void SpawnClear(BasePlayer player)
        {
            List<BaseEntity> list;
            if (!_spawned.TryGetValue(Id(player), out list)) return;
            foreach (var e in list) if (e != null && !e.IsDestroyed) e.Kill();
            list.Clear();
        }

        // ---------- электрика: библиотека, копирование, стоимость, нагрузка ----------

        private class CircuitPart { public string Prefab; public float X, Y, Z, Yaw; }
        private class CircuitLink { public int From, FromSlot, To, ToSlot; }
        private class Circuit
        {
            [JsonProperty("Название")] public string Name;
            [JsonProperty("Название EN")] public string En;
            [JsonProperty("Описание")] public string Desc;
            [JsonProperty("Детали")] public List<CircuitPart> Parts = new List<CircuitPart>();
            [JsonProperty("Провода")] public List<CircuitLink> Links = new List<CircuitLink>();
        }

        private const string PGen = "assets/prefabs/deployable/playerioents/generators/generator.small.prefab";
        private const string PSolar = "assets/prefabs/deployable/playerioents/generators/solar_panels_roof/solarpanel.large.deployed.prefab";
        private const string PBattery = "assets/prefabs/deployable/playerioents/batteries/smallrechargablebattery.deployed.prefab";
        private const string PSwitch = "assets/prefabs/deployable/playerioents/simpleswitch/switch.prefab";
        private const string PLight = "assets/prefabs/deployable/playerioents/lights/simplelight.prefab";
        private const string PSplitter = "assets/prefabs/deployable/playerioents/gates/splitter/splitter.prefab";
        private const string PHbhf = "assets/prefabs/deployable/playerioents/detectors/hbhfsensor/hbhfsensor.deployed.prefab";
        private const string PDoorCtl = "assets/prefabs/deployable/playerioents/doormanipulators/doorcontroller.deployed.prefab";
        private const string PTurret = "assets/prefabs/npc/autoturret/autoturret_deployed.prefab";

        private static Circuit C(string n, string en, string d, object[][] parts, int[][] links) => new Circuit
        {
            Name = n, En = en, Desc = d,
            Parts = parts.Select(p => new CircuitPart { Prefab = (string)p[0], X = Convert.ToSingle(p[1]), Y = Convert.ToSingle(p[2]), Z = Convert.ToSingle(p[3]) }).ToList(),
            Links = links.Select(l => new CircuitLink { From = l[0], FromSlot = l[1], To = l[2], ToSlot = l[3] }).ToList(),
        };

        private static List<Circuit> DefaultCircuits() => new List<Circuit>
        {
            C("Свет по кнопке", "Light switch", "генератор › выключатель › лампа",
                new[] { new object[] { PGen, -1.5, 0, 0 }, new object[] { PSwitch, 0, 0.8, 0 }, new object[] { PLight, 1.5, 0.8, 0 } },
                new[] { new[] { 0, 0, 1, 0 }, new[] { 1, 0, 2, 0 } }),
            C("Солнце + батарея", "Solar + battery", "солнечная панель › батарея › лампа",
                new[] { new object[] { PSolar, -2, 0, 0 }, new object[] { PBattery, 0, 0, 0 }, new object[] { PLight, 1.5, 0.8, 0 } },
                new[] { new[] { 0, 0, 1, 0 }, new[] { 1, 0, 2, 0 } }),
            C("Турель на батарее", "Turret on battery", "генератор › батарея › турель",
                new[] { new object[] { PGen, -2, 0, 0 }, new object[] { PBattery, 0, 0, 0 }, new object[] { PTurret, 2, 0, 0 } },
                new[] { new[] { 0, 0, 1, 0 }, new[] { 1, 0, 2, 0 } }),
            C("Сенсор и дверь", "Sensor door", "генератор › сенсор HBHF › контроллер двери",
                new[] { new object[] { PGen, -2, 0, 0 }, new object[] { PHbhf, 0, 1.2, 0 }, new object[] { PDoorCtl, 2, 1, 0 } },
                new[] { new[] { 0, 0, 1, 0 }, new[] { 1, 0, 2, 0 } }),
            C("Сплиттер на 3 лампы", "Splitter x3", "генератор › сплиттер › три лампы",
                new[] { new object[] { PGen, -2, 0, 0 }, new object[] { PSplitter, 0, 0.8, 0 }, new object[] { PLight, 1.5, 0.8, -1.2 }, new object[] { PLight, 1.5, 0.8, 0 }, new object[] { PLight, 1.5, 0.8, 1.2 } },
                new[] { new[] { 0, 0, 1, 0 }, new[] { 1, 0, 2, 0 }, new[] { 1, 1, 3, 0 }, new[] { 1, 2, 4, 0 } }),
        };

        private class CircuitStore { public Dictionary<ulong, List<Circuit>> Saved = new Dictionary<ulong, List<Circuit>>(); }
        private CircuitStore _circuits;

        private CircuitStore Circuits()
        {
            if (_circuits != null) return _circuits;
            try { _circuits = Interface.Oxide.DataFileSystem.ReadObject<CircuitStore>("BuildServer_circuits"); } catch { }
            if (_circuits == null) _circuits = new CircuitStore();
            if (_circuits.Saved == null) _circuits.Saved = new Dictionary<ulong, List<Circuit>>();
            return _circuits;
        }

        private void SaveCircuits() => Interface.Oxide.DataFileSystem.WriteObject("BuildServer_circuits", Circuits());

        private static Vector3 Rot(Vector3 v, float yaw) => Quaternion.Euler(0f, yaw, 0f) * v;

        private void PlaceCircuit(BasePlayer player, Circuit cir)
        {
            var id = Id(player);
            var at = LookPoint(player, 20f);
            if (at == null) { Toast(player, T(id, "SpLook"), true); return; }
            var plot = PlotAt(at.Value);
            if (plot != null && !IsMember(plot, player) && !IsAdminLike(player)) { Toast(player, T(id, "PlotNoBuild", plot.OwnerName ?? "?"), true); return; }
            float yaw = player.eyes.rotation.eulerAngles.y;
            var ents = new List<IOEntity>();
            foreach (var part in cir.Parts)
            {
                var pos = at.Value + Rot(new Vector3(part.X, part.Y, part.Z), yaw);
                var e = GameManager.server.CreateEntity(part.Prefab, pos, Quaternion.Euler(0f, yaw + part.Yaw + 180f, 0f));
                if (e == null) { ents.Add(null); continue; }
                e.OwnerID = id;
                e.Spawn();
                ents.Add(e as IOEntity);
            }
            int wired = 0;
            foreach (var l in cir.Links)
                if (l.From < ents.Count && l.To < ents.Count && Wire(ents[l.From], l.FromSlot, ents[l.To], l.ToSlot)) wired++;
            Toast(player, T(id, "ElPlaced", ents.Count(e => e != null), wired), false);
        }

        // провод: выход src[slot] → вход dst[slot]; линия — от ручки к ручке, в координатах источника
        private static bool Wire(IOEntity src, int outSlot, IOEntity dst, int inSlot)
        {
            if (src == null || dst == null || src.outputs == null || dst.inputs == null) return false;
            if (outSlot >= src.outputs.Length || inSlot >= dst.inputs.Length) return false;
            try
            {
                var o = src.outputs[outSlot]; var i = dst.inputs[inSlot];
                o.connectedTo.Set(dst); o.connectedToSlot = inSlot; o.connectedTo.Init();
                i.connectedTo.Set(src); i.connectedToSlot = outSlot; i.connectedTo.Init();
                o.linePoints = new[] { o.handlePosition, src.transform.InverseTransformPoint(dst.transform.TransformPoint(i.handlePosition)) };
                src.MarkDirtyForceUpdateOutputs();
                src.SendNetworkUpdate(); dst.SendNetworkUpdate();
                return true;
            }
            catch { return false; }
        }

        private List<IOEntity> IoAround(Vector3 center, float r)
        {
            var list = new List<IOEntity>();
            Vis.Entities(center, r, list, LayerMask.GetMask("Deployed", "Default"), QueryTriggerInteraction.Ignore);
            return list.Where(e => e != null && !e.IsDestroyed).Distinct().ToList();
        }

        private void CopyCircuit(BasePlayer player)
        {
            var id = Id(player);
            var at = LookPoint(player, 20f) ?? player.transform.position;
            var ents = IoAround(at, _config.ElecCopyRadius).Where(e => CanModify(player, e)).Take(60).ToList();
            if (ents.Count == 0) { Toast(player, T(id, "ElNone"), true); return; }
            float yaw = player.eyes.rotation.eulerAngles.y;
            var cir = new Circuit { Name = T(id, "ElMine", (Circuits().Saved.ContainsKey(id) ? Circuits().Saved[id].Count : 0) + 1) };
            foreach (var e in ents)
            {
                var local = Rot(e.transform.position - at, -yaw);
                cir.Parts.Add(new CircuitPart { Prefab = e.PrefabName, X = local.x, Y = local.y, Z = local.z, Yaw = e.transform.rotation.eulerAngles.y - yaw - 180f });
            }
            for (int a = 0; a < ents.Count; a++)
            {
                var outs = ents[a].outputs ?? new IOSlot[0];
                for (int s = 0; s < outs.Length; s++)
                {
                    var to = outs[s].connectedTo?.Get(true);
                    int b = to == null ? -1 : ents.IndexOf(to);
                    if (b >= 0) cir.Links.Add(new CircuitLink { From = a, FromSlot = s, To = b, ToSlot = outs[s].connectedToSlot });
                }
            }
            List<Circuit> mine;
            if (!Circuits().Saved.TryGetValue(id, out mine)) Circuits().Saved[id] = mine = new List<Circuit>();
            if (mine.Count >= _config.ElecSlots) mine.RemoveAt(0);
            mine.Add(cir);
            SaveCircuits();
            Toast(player, T(id, "ElCopied", cir.Parts.Count, cir.Links.Count), false);
        }

        private readonly Dictionary<ulong, Dictionary<int, int>> _elecCost = new Dictionary<ulong, Dictionary<int, int>>();

        private void CircuitCost(BasePlayer player)
        {
            var id = Id(player);
            var at = LookPoint(player, 20f) ?? player.transform.position;
            var cost = new Dictionary<int, int>();
            int n = 0;
            foreach (var e in IoAround(at, _config.ElecCopyRadius))
            {
                var def = DeployItem(e);
                if (def == null) continue;
                n++;
                AddAmounts(cost, Member(Member(def, "Blueprint"), "ingredients"));
            }
            _elecCost[id] = cost;
            Toast(player, T(id, "ElCost", n), false);
        }

        private readonly HashSet<ulong> _loadView = new HashSet<ulong>();
        private Timer _loadTimer;

        private void LoadTick()
        {
            foreach (var id in _loadView.ToList())
            {
                var p = BasePlayer.FindByID(id);
                if (p == null || !p.IsConnected) { _loadView.Remove(id); continue; }
                var ents = IoAround(p.transform.position, _config.ElecViewRadius).Take(40).ToList();
                if (ents.Count == 0) continue;
                WithAdmin(p, () =>
                {
                    foreach (var e in ents)
                    {
                        int inW = e.currentEnergy, use = 0, outW = 0;
                        try { use = e.ConsumptionAmount(); outW = e.GetPassthroughAmount(0); } catch { }
                        var col = inW > 0 ? new Color(0.5f, 0.83f, 0.6f) : new Color(0.88f, 0.34f, 0.42f);
                        p.SendConsoleCommand("ddraw.text", 1.1f, col, e.transform.position + Vector3.up * 0.7f, $"<size=13>{T(id, "ElLoadFmt", inW, use, outW)}</size>");
                    }
                });
            }
            if (_loadView.Count == 0) { _loadTimer?.Destroy(); _loadTimer = null; }
        }

        private void ToggleLoad(BasePlayer player)
        {
            var id = Id(player);
            if (!_loadView.Remove(id)) _loadView.Add(id);
            if (_loadView.Count > 0 && _loadTimer == null) _loadTimer = timer.Every(1f, LoadTick);
        }

        // ---------- интерфейс: вкладки «Электрика» и «Спавн», блоки рейда ----------

        private void BuildElec(CuiElementContainer c, BasePlayer player, float x, float y, float w)
        {
            var id = Id(player);
            float hw = Col(w, 2), ph = Bottom - y;
            // слева — библиотека
            Box(c, ColPanel, x, y, hw, ph, null, UiMain, 12f);
            Box(c, ColWine, x, y, hw, 30, null, UiMain, 12f);
            Text(c, T(id, "ElLib"), 12, x + 12, y, hw - 24, 30);
            float iy = y + 40;
            for (int i = 0; i < _config.Circuits.Count && i < 7; i++)
            {
                var cir = _config.Circuits[i];
                Box(c, ColRow, x + 10, iy, hw - 20, 62, null, UiMain, 10f);
                Text(c, Lang(id) == "ru" ? cir.Name : cir.En ?? cir.Name, 13, x + 22, iy + 6, hw - 150, 22);
                Text(c, cir.Desc, 10, x + 22, iy + 30, hw - 150, 26, TextAnchor.UpperLeft, ColDim, FontReg);
                Btn(c, T(id, "ElPlace"), "bs.ui el.place " + i, ColWine, 11, x + hw - 124, iy + 15, 104, 32);
                iy += 70;
            }
            Text(c, T(id, "ElLibHint"), 10, x + 14, y + ph - 40, hw - 28, 32, TextAnchor.MiddleLeft, ColDim, FontReg);

            // справа — мои схемы, стоимость, нагрузка
            float rx = x + hw + G;
            float mh = 260f;
            Box(c, ColPanel, rx, y, hw, mh, null, UiMain, 12f);
            Text(c, T(id, "ElMineTitle", _config.ElecSlots), 12, rx + 14, y + 8, hw - 28, 22);
            Btn(c, T(id, "ElCopy", _config.ElecCopyRadius), "bs.ui el.copy", ColWine, 12, rx + 14, y + 36, hw - 28, 34);
            List<Circuit> mine;
            Circuits().Saved.TryGetValue(id, out mine);
            float my = y + 80;
            if (mine == null || mine.Count == 0) Text(c, T(id, "ElMineEmpty"), 11, rx + 14, my, hw - 28, 60, TextAnchor.UpperLeft, ColDim, FontReg);
            else
                for (int i = 0; i < mine.Count && i < _config.ElecSlots; i++)
                {
                    Box(c, ColRow, rx + 14, my, hw - 28, 32, null, UiMain, 8f);
                    Text(c, $"{mine[i].Name} · {mine[i].Parts.Count} {T(id, "ElParts")} · {mine[i].Links.Count} {T(id, "ElWires")}", 11, rx + 24, my, hw - 200, 32);
                    Btn(c, T(id, "ElPaste"), "bs.ui el.paste " + i, ColBtn, 10, rx + hw - 14 - 140, my + 4, 100, 24);
                    Btn(c, "X", "bs.ui el.del " + i, ColBtn, 10, rx + hw - 14 - 34, my + 4, 28, 24, "0.92 0.42 0.48 1");
                    my += 38;
                }

            float cy = y + mh + G, ch = Bottom - cy;
            Box(c, ColPanel, rx, cy, hw, ch, null, UiMain, 12f);
            Text(c, T(id, "ElLoad"), 12, rx + 14, cy + 8, hw - 120, 22);
            Pill(c, id, _loadView.Contains(id), "bs.ui el.load", rx + hw - 14 - 72, cy + 10);
            Text(c, T(id, "ElLoadHint"), 10, rx + 14, cy + 32, hw - 28, 30, TextAnchor.UpperLeft, ColDim, FontReg);
            Btn(c, T(id, "ElCostBtn", _config.ElecCopyRadius), "bs.ui el.cost", ColBtn, 12, rx + 14, cy + 66, hw - 28, 32);
            Dictionary<int, int> cost;
            if (_elecCost.TryGetValue(id, out cost))
            {
                if (cost.Count == 0) Text(c, T(id, "ElNone"), 11, rx + 14, cy + 108, hw - 28, 24, TextAnchor.MiddleLeft, ColDim, FontReg);
                int k = 0;
                foreach (var kv in cost.OrderByDescending(v => v.Value).Take(8))
                {
                    float ix = rx + 14 + (k % 8) * 54f, iy2 = cy + 108;
                    Box(c, ColRow, ix, iy2, 48, 48, null, UiMain, 8f);
                    ItemIcon(c, kv.Key, ix + 6, iy2 + 4, 36);
                    Text(c, "×" + kv.Value, 9, ix, iy2 + 34, 44, 14, TextAnchor.MiddleRight, ColText, Font);
                    k++;
                }
            }
        }

        private void BuildSpawn(CuiElementContainer c, BasePlayer player, float x, float y, float w)
        {
            var id = Id(player);
            float cw4 = Col(w, 4), foot = 46f, ph = Bottom - y - foot - G;
            for (int ci = 0; ci < SpawnCats.Length; ci++)
            {
                float cx = x + ci * (cw4 + G);
                Box(c, ColPanel, cx, y, cw4, ph, null, UiMain, 12f);
                Box(c, ColWine, cx, y, cw4, 30, null, UiMain, 12f);
                Text(c, T(id, "SpCat_" + SpawnCats[ci]), 12, cx, y, cw4, 30, TextAnchor.MiddleCenter);
                float iy = y + 40;
                for (int i = 0; i < _config.Spawns.Count; i++)
                {
                    var d = _config.Spawns[i];
                    if (d.Cat != SpawnCats[ci] || iy + 36 > y + ph - 6) continue;
                    Btn(c, Lang(id) == "ru" ? d.Name : d.En ?? d.Name, "bs.ui sp.spawn " + i, ColRow, 12, cx + 10, iy, cw4 - 20, 34);
                    iy += 40;
                }
            }
            List<BaseEntity> list;
            int n = _spawned.TryGetValue(id, out list) ? list.Count(e => e != null && !e.IsDestroyed) : 0;
            float fy = Bottom - foot;
            Box(c, ColPanel, x, fy, w, foot, null, UiMain, 12f);
            Text(c, T(id, "SpFoot", n, _config.SpawnLimit), 11, x + 16, fy, w - 220, foot, TextAnchor.MiddleLeft, ColDim, FontReg);
            Btn(c, T(id, "SpClear"), "bs.ui sp.clear", ColBtn, 12, x + w - 196, fy + 7, 180, 32, "0.92 0.42 0.48 1");
        }

        // блок «друг рейдит» и «путь до ТШ» во вкладке рейда
        private float BuildRaidExtra(CuiElementContainer c, BasePlayer player, float x, float y, float w)
        {
            var id = Id(player);
            float hw = Col(w, 2), bh = 168f;
            // друг рейдит
            Box(c, ColPanel, x, y, hw, bh, null, UiMain, 12f);
            Text(c, T(id, "FrTitle"), 12, x + 14, y + 8, hw - 28, 20);
            FriendRaid fr;
            if (_friendRaids.TryGetValue(id, out fr))
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(fr.Until - Time.realtimeSinceStartup));
                Text(c, T(id, "FrActive", fr.FriendName, left / 60, left % 60), 13, x + 14, y + 40, hw - 28, 60, TextAnchor.MiddleCenter, ColRose);
                Btn(c, T(id, "FrStop"), "bs.ui fr.stop", ColBtn, 12, x + 14, y + bh - 46, hw - 28, 34);
            }
            else
            {
                ulong pick; _friendPick.TryGetValue(id, out pick);
                var online = BasePlayer.activePlayerList.Where(p => p != player && p.IsConnected).OrderBy(p => p.displayName).Take(4).ToList();
                float cw2 = (hw - 28 - 6) / 2f;
                for (int i = 0; i < online.Count; i++)
                    Btn(c, online[i].displayName, "bs.ui fr.pick " + Id(online[i]), Id(online[i]) == pick ? ColWine : ColRow, 11, x + 14 + (i % 2) * (cw2 + 6), y + 34 + (i / 2) * 32, cw2, 28);
                if (online.Count == 0) Text(c, T(id, "FrNoOne"), 11, x + 14, y + 34, hw - 28, 28, TextAnchor.MiddleLeft, ColDim, FontReg);
                int min; if (!_friendMin.TryGetValue(id, out min)) min = _config.FriendRaidMinutes.FirstOrDefault();
                var opts = _config.FriendRaidMinutes.Take(4).ToList();
                float sw = (hw - 28 - (opts.Count - 1) * 6) / Mathf.Max(1, opts.Count);
                for (int i = 0; i < opts.Count; i++)
                    Btn(c, T(id, "FrMin", opts[i]), "bs.ui fr.min " + opts[i], opts[i] == min ? ColWine : ColBtn, 11, x + 14 + i * (sw + 6), y + 100, sw, 26);
                Btn(c, T(id, "FrStart"), "bs.ui fr.start", ColRed, 12, x + 14, y + bh - 36, hw - 28, 28);
            }

            // путь до ТШ
            float px = x + hw + G;
            Box(c, ColPanel, px, y, hw, bh, null, UiMain, 12f);
            Text(c, T(id, "PathTitle"), 12, px + 14, y + 8, hw - 150, 20);
            Btn(c, T(id, "PathBtn"), "bs.ui raid.path", ColWine, 11, px + hw - 14 - 120, y + 6, 120, 26);
            PathReport rep;
            if (_pathReport.TryGetValue(id, out rep))
            {
                Text(c, T(id, "PathBest", rep.Dir, rep.Lines.Count, rep.Sulfur), 12, px + 14, y + 38, hw - 28, 20, TextAnchor.MiddleLeft, ColRose);
                for (int i = 0; i < rep.Lines.Count && i < 5; i++)
                    Text(c, $"{i + 1}. {rep.Lines[i]}", 10, px + 14, y + 60 + i * 20, hw - 28, 20, TextAnchor.MiddleLeft, ColText, FontReg);
                if (rep.Lines.Count > 5) Text(c, T(id, "PathMore", rep.Lines.Count - 5), 10, px + 14, y + 160 - 4, hw - 28, 12, TextAnchor.MiddleLeft, ColDim, FontReg);
            }
            else Text(c, T(id, "PathHint"), 11, px + 14, y + 38, hw - 28, bh - 50, TextAnchor.UpperLeft, ColDim, FontReg);
            return y + bh + G;
        }

        #endregion

        #region Интерфейс

        private class UiState
        {
            public string Tab = "tools";
            public string Category = "Construction";
            public string Search = "";
            public int Page;
            public string HomeName = "";
            public bool ConfirmRemoveAll, ConfirmStrip, ConfirmClear;
            public float ConfirmAt;
            public int DoorTab, WinTab, HatchTab, TurretTab, CostPage;
        }

        private readonly Dictionary<ulong, UiState> _ui = new Dictionary<ulong, UiState>();

        private UiState Ui(ulong id)
        {
            UiState s;
            if (!_ui.TryGetValue(id, out s)) _ui[id] = s = new UiState();
            return s;
        }

        private const string ColBg = "0.071 0.055 0.055 0.97";
        private const string ColPanel = "1 1 1 0.03";
        private const string ColRow = "1 1 1 0.045";
        private const string ColRowAlt = "1 1 1 0.03";
        private const string ColBtn = "1 1 1 0.07";
        private const string ColWine = "0.620 0.082 0.188 1";
        private const string ColRed = "0.620 0.082 0.188 1";
        private const string ColText = "0.937 0.914 0.906 1";
        private const string ColDim = "0.639 0.596 0.588 1";
        private const string ColRose = "0.94 0.65 0.69 1";
        private const string Font = "robotocondensed-bold.ttf";
        private const string FontReg = "robotocondensed-regular.ttf";
        private const float W = 1240f, H = 690f;

        private const string RoundPngB64 = "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAACgklEQVR42u2bMWsUQRiGn5k7sbNKEBGxsUnAJo0BQVJohHTW/gWr9Km0Su1fsAkEa2MnHGiTRjgLbeQ4gpDGK5LC3f0s9p1ks7kLdmZ3vg+W290Zjnmf+WZnBuYNZsaCCMAAKPR8G3gOPAPWgLvALdX7n2HADJgCh8BH4APwS+VDoFS9yyIXAIhApfsV4BXwArhDN+IIeA+8Bb7N0dTAZ9a+BvodmtlrMzux8yjNrDCzStd1idSeQm1McSINw5a2s6udAQOly33gHfBY7wuVhY5kgEnHUM8j4CXws6Hx0hBIBavAgcb4H/1JV4TPA1EAN/SN2ATGTQgJQBof94AvGutFg2DXI2k5Ah4Bk6Q5qneDKO31UHyaCQpp25PWAIQoEiWwA6w30r5vMZS2dWktgRjMLAAPgK+qFDs85v/lm1ApGx4CP6JebgM3dd9X8WlxZ9K6DVgwsyV9GZcalfocado7BlYjsAUsZ9D77SxYBrai5kZbtFbucRYYsBm1sQmZ9H4zCwKwFszst3Z1lhGEpHUW7Ir9cA4RyTwcgANwAA7AATgAB+AAHIADcAAOwAE4AAfgAByAA3AADsABOAAH4AAcgANwAA7AATgAB5AHgBkXj4+RyREZgFmkPkWdK4BppLaZ5HpM7jBSewNyPSZ34EdldbMv4WUGvV9K6z5w7MflJfg7sMtFn2AfI5m/dqX5zDCRQHyidlQUPXSNJE2fgSfKhMpNU6pQKTUmwFMtjpLHxjo+5pMHaiptE2mt2nuBUgVjasPkiHN3VdExEMkvmNxwI2kat42Tcc4UMaB2WG4Ab4DThnmyahiR7Rqu7Eq1MajNp9KwMc816ubpKwBkY5//C56fx7Wog3HqAAAAAElFTkSuQmCC";
        private static string _roundPng;

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
            catch (Exception ex) { PrintWarning("Rounded UI disabled: " + ex.Message); }
        }

        // иконки и крестик в стиле VideoRecorder (иконки берём у Hub)
        private string HubIconPng(string n) => Hub != null && Hub.IsLoaded ? Hub.Call("GetIconPng", n) as string : null;

        private void HubIcon(CuiElementContainer c, string icon, float x, float y, float s, string color)
        {
            var png = HubIconPng(icon);
            if (png == null) return;
            c.Add(new CuiElement { Parent = UiMain, Components = { new CuiRawImageComponent { Png = png, Color = color },
                new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - s}", OffsetMax = $"{x + s} {-y}" } } });
        }

        private void CloseBtn(CuiElementContainer c, string cmd, float x, float y)
        {
            Box(c, ColBtn, x, y, 34, 34, null, UiMain, 10f);
            bool ic = HubIconPng("close") != null;
            if (ic) HubIcon(c, "close", x + 9, y + 9, 16, ColText);
            Btn(c, ic ? "" : "×", cmd, "0 0 0 0", 18, x, y, 34, 34, ColText, 0f);
        }

        private static void Box(CuiElementContainer c, string color, float x, float y, float w, float h, string name = null, string parent = UiMain, float r = 10f)
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

        private static void Text(CuiElementContainer c, string text, int size, float x, float y, float w, float h,
            TextAnchor align = TextAnchor.MiddleLeft, string color = ColText, string font = Font, string parent = UiMain)
        {
            c.Add(new CuiLabel
            {
                Text = { Text = text, FontSize = size, Align = align, Color = color, Font = font },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - h}", OffsetMax = $"{x + w} {-y}" }
            }, parent);
        }

        private static string Nice(string t)
        {
            if (string.IsNullOrEmpty(t) || t.Length < 5 || t != t.ToUpperInvariant() || t == t.ToLowerInvariant()) return t;
            return t.Substring(0, 1) + t.Substring(1).ToLowerInvariant();
        }

        private static void Btn(CuiElementContainer c, string text, string cmd, string color, int size, float x, float y, float w, float h,
            string textColor = ColText, float r = 10f, string parent = UiMain)
        {
            if (r >= 10f) r = h <= 40f ? h / 2f : 14f;
            bool round = _roundPng != null && r > 0f;
            if (round) Box(c, color, x, y, w, h, null, parent, r);
            c.Add(new CuiButton
            {
                Button = { Command = cmd, Color = round ? "0 0 0 0" : color },
                Text = { Text = Nice(text), FontSize = size, Align = TextAnchor.MiddleCenter, Color = textColor, Font = Font },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - h}", OffsetMax = $"{x + w} {-y}" }
            }, parent);
        }

        private static void Input(CuiElementContainer c, string name, string value, string hint, string cmd, float x, float y, float w, float h)
        {
            Box(c, ColRow, x, y, w, h, name, UiMain, 9f);
            if (string.IsNullOrEmpty(value)) Text(c, hint, 11, 10, 0, w - 20, h, TextAnchor.MiddleLeft, "0.45 0.41 0.40 1", FontReg, name);
            c.Add(new CuiElement
            {
                Parent = name,
                Components =
                {
                    new CuiInputFieldComponent { Text = value ?? "", Command = cmd, FontSize = 12, Align = TextAnchor.MiddleLeft, CharsLimit = 32, Color = ColText },
                    new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "10 0", OffsetMax = "-10 0" }
                }
            });
        }

        private void Toggle(CuiElementContainer c, ulong id, string key, string hint, bool on, string cmd, float x, float y, float w)
        {
            Box(c, ColRow, x, y, w, 44, null, UiMain, 12f);
            Text(c, T(id, key), 12, x + 12, y + 4, w - 80, 20);
            if (hint != null) Text(c, T(id, hint), 9, x + 12, y + 22, w - 80, 16, TextAnchor.MiddleLeft, ColDim, FontReg);
            Btn(c, on ? T(id, "On") : T(id, "Off"), cmd, on ? ColWine : ColBtn, 10, x + w - 62, y + 10, 52, 24);
        }

        private static void ItemIcon(CuiElementContainer c, int itemId, float x, float y, float s)
        {
            c.Add(new CuiElement
            {
                Parent = UiMain,
                Components =
                {
                    new CuiImageComponent { ItemId = itemId },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - s}", OffsetMax = $"{x + s} {-y}" }
                }
            });
        }

        private void CloseUi(BasePlayer player) => CuiHelper.DestroyUi(player, UiMain);

        private static readonly string[] Tabs = { "tools", "library", "zones", "env", "items", "raid", "elec", "spawn", "tp" };
        private static readonly string[] TabKeys = { "TabTools", "TabLibrary", "TabZones", "TabEnv", "TabItems", "TabRaid", "TabElec", "TabSpawn", "TabTp" };

        private void OpenUi(BasePlayer player)
        {
            var id = Id(player);
            var ui = Ui(id);
            if (Array.IndexOf(Tabs, ui.Tab) < 0) ui.Tab = "tools";
            var c = new CuiElementContainer();

            if (_roundPng != null)
                c.Add(new CuiElement
                {
                    Name = UiMain,
                    Parent = "Overlay",
                    Components =
                    {
                        new RoundedImage { Png = _roundPng, Color = ColBg, Ppu = 16f / 22f },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-W / 2f} {-H / 2f}", OffsetMax = $"{W / 2f} {H / 2f}" },
                        new CuiNeedsCursorComponent()
                    }
                });
            else
                c.Add(new CuiPanel { Image = { Color = ColBg }, RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-W / 2f} {-H / 2f}", OffsetMax = $"{W / 2f} {H / 2f}" }, CursorEnabled = true }, "Overlay", UiMain);

            // шапка: лого над боковой колонкой, вкладки над контентом, крестик
            // шапка в стиле VideoRecorder: плитка с иконкой, название, вкладки на тёмной дорожке, круглый крестик
            Box(c, ColWine, M, 9, 40, 40, null, UiMain, 12f);
            HubIcon(c, "build", M + 10, 19, 20, "1 1 1 1");
            Text(c, "ASH <color=#f0a6b1>&</color> WINE", 17, M + 52, 9, SideW - 52, 22, TextAnchor.MiddleLeft, ColText);
            Text(c, T(id, "Sub"), 10, M + 52, 31, SideW - 52, 16, TextAnchor.MiddleLeft, ColDim, FontReg);
            float cx = M + SideW + G2, cw = W - cx - M;
            float tw = (cw - 40 - G - (Tabs.Length - 1) * 6) / Tabs.Length;
            Box(c, "0 0 0 0.3", cx - 4, 8, cw - 40 - 6, 44, null, UiMain, 12f);
            for (int i = 0; i < Tabs.Length; i++)
            {
                bool a = ui.Tab == Tabs[i];
                Btn(c, T(id, TabKeys[i]), "bs.ui tab " + Tabs[i], a ? ColWine : "0 0 0 0", 12, cx + i * (tw + 6), 12, tw, 36, a ? ColText : ColDim, 10f);
            }
            CloseBtn(c, "bs.ui close", W - M - 36, 13);

            if (ui.Tab == "tools") BuildToolsSide(c, player, M, Top, SideW);
            else BuildInfoSide(c, player, M, Top, SideW);

            switch (ui.Tab)
            {
                case "items": BuildItems(c, player, ui, cx, Top, cw); break;
                case "raid": BuildRaid(c, player, cx, Top, cw); break;
                case "elec": BuildElec(c, player, cx, Top, cw); break;
                case "spawn": BuildSpawn(c, player, cx, Top, cw); break;
                case "zones": BuildPlots(c, player, cx, Top, cw); break;
                case "env": BuildEnv(c, player, cx, Top, cw); break;
                case "tp": BuildTp(c, player, ui, cx, Top, cw); break;
                default: BuildTools(c, player, ui, cx, Top, cw); break;
            }

            CuiHelper.DestroyUi(player, UiMain);
            AddUiScaled(player, c);
        }

        // сетка: поля M, зазоры G, колонка слева SideW, контент всегда доходит до Bottom
        private const float M = 16f, G = 8f, G2 = 14f, Top = 74f, SideW = 290f, Bottom = H - 16f;
        private static float Col(float w, int n) => (w - (n - 1) * G) / n;

        private static void Section(CuiElementContainer c, string title, float x, float y, float w)
            => Text(c, title, 10, x, y, w, 18, TextAnchor.MiddleLeft, ColDim);

        // заголовок раздела с винной чертой, как у карточек
        private static void SideHead(CuiElementContainer c, string title, float x, float y, float w)
        {
            Text(c, title, 13, x, y, w, 20, TextAnchor.MiddleLeft, ColText);
            Box(c, ColWine, x, y + 21, w, 2, null, UiMain, 0f);
        }

        // строка сайдбара: подпись + ВЫКЛ/ВКЛ
        private void SideToggle(CuiElementContainer c, ulong id, string key, bool on, string cmd, float x, float y, float w, float h)
        {
            Box(c, ColRow, x, y, w, h, null, UiMain, 8f);
            Text(c, T(id, key), 10, x + 9, y, w - 70, h);
            Pill(c, id, on, cmd, x + w - 62, y + (h - 19) / 2f, 56f);
        }

        private void Pill(CuiElementContainer c, ulong id, bool on, string cmd, float x, float y, float pw = 72f)
        {
            float half = pw / 2f;
            Box(c, ColBtn, x, y, pw, 19, null, UiMain, 7f);
            Btn(c, T(id, "Off"), cmd, on ? "0 0 0 0" : ColRed, 8, x, y, half, 19, on ? ColDim : ColText, 7f);
            Btn(c, T(id, "On"), cmd, on ? ColWine : "0 0 0 0", 8, x + half, y, half, 19, on ? ColText : ColDim, 7f);
        }

        private void SideBtn(CuiElementContainer c, string text, string cmd, float x, float y, float w, float h, string color = ColRow, string textColor = ColText)
        {
            Box(c, color, x, y, w, h, null, UiMain, 8f);
            c.Add(new CuiButton
            {
                Button = { Command = cmd, Color = "0 0 0 0" },
                Text = { Text = text, FontSize = 11, Align = TextAnchor.MiddleLeft, Color = textColor, Font = Font },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x + 10} {-y - h}", OffsetMax = $"{x + w} {-y}" }
            }, UiMain);
        }

        private void BuildToolsSide(CuiElementContainer c, BasePlayer player, float x, float y, float w)
        {
            var id = Id(player);
            var s = P(id);
            var t = s.Tools;
            var ui = Ui(id);
            const float rh = 25f, rg = 5f, hh = 28f;
            float hw = (w - rg) / 2f;

            SideHead(c, T(id, "SQuick"), x, y, w); y += hh;
            SideBtn(c, T(id, "QWall3"), "bs.ui q.wall 3", x, y, hw, rh);
            SideBtn(c, T(id, "QWipeLevel"), "bs.ui wipe 0", x + hw + rg, y, hw, rh); y += rh + rg;
            SideBtn(c, T(id, "QWallGap"), "bs.ui q.wallgap", x, y, hw, rh);
            SideBtn(c, T(id, "QWipeDeploy"), "bs.ui wipe 1", x + hw + rg, y, hw, rh); y += rh + rg;
            SideBtn(c, T(id, "QFloor3"), "bs.ui q.floor 3", x, y, hw, rh);
            SideBtn(c, T(id, "QWipeFound"), "bs.ui wipe 2", x + hw + rg, y, hw, rh); y += rh + rg;
            SideBtn(c, T(id, "QReset"), "bs.ui q.reset", x, y, hw, rh);
            SideToggle(c, id, "QFurnaces", t.Furnaces, "bs.ui t.furnaces", x + hw + rg, y, hw, rh); y += rh + rg + 6;

            SideHead(c, T(id, "SPersonal"), x, y, w); y += hh;
            SideToggle(c, id, "PFly", _flying.Contains(id), "bs.ui fly", x, y, hw, rh);
            SideToggle(c, id, "PFree", t.FreeBuild, "bs.ui t.free", x + hw + rg, y, hw, rh); y += rh + rg;
            SideToggle(c, id, "PGod", s.God && Has(player, PermGod), "bs.ui god", x, y, hw, rh);
            SideToggle(c, id, "PStab", !t.NoStability, "bs.ui t.stab", x + hw + rg, y, hw, rh); y += rh + rg;
            SideToggle(c, id, "PAuth", s.AutoAuth, "bs.ui opt.auth", x, y, hw, rh);
            SideToggle(c, id, "PMapTp", s.MapTp, "bs.ui opt.maptp", x + hw + rg, y, hw, rh); y += rh + rg;
            SideBtn(c, T(id, "PPlan"), "bs.ui plan", x, y, hw, rh);
            bool sure = ui.ConfirmStrip && Time.realtimeSinceStartup - ui.ConfirmAt < 5f;
            SideBtn(c, sure ? T(id, "PStripSure") : T(id, "PStrip"), "bs.ui strip", x + hw + rg, y, hw, rh, sure ? ColRed : ColRow); y += rh + rg;
            float qw = (hw - rg) / 2f;
            SideBtn(c, T(id, "PUndo"), "bs.ui undo", x, y, qw, rh);
            SideBtn(c, T(id, "PRedo"), "bs.ui redo", x + qw + rg, y, qw, rh);
            SideBtn(c, T(id, "PKit"), "bs.ui kit", x + hw + rg, y, hw, rh); y += rh + rg + 8;

            var plot = PlotAt(player.transform.position);
            SideHead(c, T(id, "SPlot"), x, y, w); y += hh;
            SideToggle(c, id, "LPrivate", plot != null && plot.Private, "bs.ui plot.private", x, y, hw, rh);
            SideBtn(c, T(id, "LShow"), "bs.ui plot.show", x + hw + rg, y, hw, rh); y += rh + rg;
            SideBtn(c, T(id, "LSave"), "bs.ui plot.save", x, y, hw, rh);
            SideBtn(c, T(id, "LLoad"), "bs.ui plot.load", x + hw + rg, y, hw, rh); y += rh + rg;
            bool sureClear = ui.ConfirmClear && Time.realtimeSinceStartup - ui.ConfirmAt < 5f;
            SideBtn(c, sureClear ? T(id, "LClearSure") : T(id, "LClear"), "bs.ui plot.clear", x, y, w, rh, sureClear ? ColRed : ColRow, sureClear ? ColText : "0.92 0.42 0.48 1"); y += rh + rg + 8;

            SideHead(c, T(id, "STransport"), x, y, w); y += hh;
            float tw4 = (w - 3 * rg) / 4f;
            bool ru = Lang(id) == "ru";
            for (int i = 0; i < _config.Vehicles.Count && i < 12; i++)
            {
                float bx = x + (i % 4) * (tw4 + rg), by = y + (i / 4) * (rh + rg);
                Btn(c, ru ? _config.Vehicles[i].Ru : _config.Vehicles[i].En, "bs.ui car " + i, ColRow, 10, bx, by, tw4, rh, ColText, 8f);
            }
        }

        private void BuildInfoSide(CuiElementContainer c, BasePlayer player, float x, float y, float w)
        {
            var id = Id(player);
            var s = P(id);
            bool flying = _flying.Contains(id);
            Btn(c, flying ? T(id, "FlyStop") : T(id, "FlyStart"), "bs.ui fly", flying ? ColRed : ColWine, 14, x, y, w, 48, ColText, 12f);
            y += 48 + G;
            Btn(c, T(id, "KitBtn"), "bs.ui kit", ColBtn, 12, x, y, w, 36, ColText, 10f);
            y += 36 + G;
            SideToggle(c, id, "PGod", s.God && Has(player, PermGod), "bs.ui god", x, y, w, 30); y += 30 + 5;
            SideToggle(c, id, "OptKit", s.Kit, "bs.ui opt.kit", x, y, w, 30); y += 30 + 5;
            SideToggle(c, id, "OptLock", s.AutoLock, "bs.ui opt.lock", x, y, w, 30); y += 30 + 5;
            SideToggle(c, id, "OptHudCost", s.Tools.HudCost, "bs.hud cost", x, y, w, 30); y += 30 + 5;
            SideToggle(c, id, "OptHudBar", s.Tools.HudBar, "bs.hud bar", x, y, w, 30); y += 30 + G;
            Box(c, ColPanel, x, y, w, Bottom - y, null, UiMain, 12f);
            Text(c, T(id, "Binds"), 10, x + 14, y + 12, w - 28, Bottom - y - 24, TextAnchor.UpperLeft, ColDim, FontReg);
        }

        // ---------- карточки инструментов ----------

        private void CardHead(CuiElementContainer c, ulong id, string key, float x, float y, float w, bool? on, string cmd)
        {
            Box(c, ColPanel, x, y, w, 34, null, UiMain, 10f);
            Text(c, T(id, key), 12, x + 12, y, w - 100, 34);
            if (on.HasValue) Pill(c, id, on.Value, cmd, x + w - 84, y + 7);
        }

        private void Arrows(CuiElementContainer c, string label, string cmdLeft, string cmdRight, float x, float y, float w, float h)
        {
            Btn(c, "<", cmdLeft, ColBtn, 13, x, y, h, h, ColRose, 8f);
            Box(c, ColRow, x + h + 4, y, w - 2 * h - 8, h, null, UiMain, 8f);
            Text(c, label, 12, x + h + 4, y, w - 2 * h - 8, h, TextAnchor.MiddleCenter);
            Btn(c, ">", cmdRight, ColBtn, 13, x + w - h, y, h, h, ColRose, 8f);
        }

        private void Seg(CuiElementContainer c, string[] labels, int active, string cmdPrefix, float x, float y, float w, float h)
        {
            float sw = (w - (labels.Length - 1) * 4) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
                Btn(c, labels[i], cmdPrefix + " " + i, i == active ? ColWine : ColBtn, 10, x + i * (sw + 4), y, sw, h, i == active ? ColText : ColDim, 8f);
        }

        private void IconRow(CuiElementContainer c, string[] items, string selected, string cmdPrefix, float x, float y, float w, int slots)
        {
            float s = Mathf.Min(58f, (w - (slots - 1) * 6) / slots);
            float sx = x + (w - (slots * s + (slots - 1) * 6)) / 2f;
            for (int i = 0; i < slots; i++)
            {
                float ix = sx + i * (s + 6);
                bool has = i < items.Length;
                bool sel = has && items[i] == selected;
                if (sel) Box(c, ColRed, ix - 2, y - 2, s + 4, s + 4, null, UiMain, 10f);
                Box(c, has ? ColRow : ColRowAlt, ix, y, s, s, null, UiMain, 9f);
                if (!has) continue;
                var def = ItemManager.FindItemDefinition(items[i]);
                if (def != null) ItemIcon(c, def.itemid, ix + 7, y + 7, s - 14);
                Btn(c, "", cmdPrefix + " " + items[i], "0 0 0 0", 9, ix, y, s, s, ColText, 0f);
            }
        }

        private void MiniToggle(CuiElementContainer c, ulong id, string key, bool on, string cmd, float x, float y, float w)
        {
            Box(c, ColRow, x, y, w, 28, null, UiMain, 8f);
            Text(c, T(id, key), 10, x + 9, y, w - 70, 28);
            Pill(c, id, on, cmd, x + w - 62, y + 4.5f, 56f);
        }

        private void BuildTools(CuiElementContainer c, BasePlayer player, UiState ui, float x, float y, float w)
        {
            var id = Id(player);
            var t = P(id).Tools;
            const float cg = 10f;
            float cw = (w - 2 * cg) / 3f, ch = (Bottom - y - 2 * cg) / 3f;
            float[] cx = { x, x + cw + cg, x + 2 * (cw + cg) };
            float[] cy = { y, y + ch + cg, y + 2 * (ch + cg) };
            float pad = 12f;

            // --- 1. Симметрия
            {
                float X = cx[0], Y = cy[0];
                Box(c, ColPanel, X, Y, cw, ch, null, UiMain, 12f);
                CardHead(c, id, "KSym", X, Y, cw, t.SymMode > 0, "bs.ui sym.toggle");
                Arrows(c, T(id, "Sym" + t.SymMode), "bs.ui sym.mode -1", "bs.ui sym.mode 1", X + pad, Y + 44, cw - 2 * pad, 30);
                string info = t.SymSet ? T(id, "SymInfoSet", Mathf.RoundToInt(t.SymX), Mathf.RoundToInt(t.SymZ)) : T(id, "SymInfo");
                Text(c, info, 10, X + pad, Y + 80, cw - 2 * pad, ch - 80 - 52, TextAnchor.MiddleCenter, ColDim, FontReg);
                Btn(c, T(id, "SymCenter"), "bs.ui sym.center", ColWine, 14, X, Y + ch - 42, cw, 42, ColText, 12f);
            }

            // --- 2. Стак стен
            {
                float X = cx[1], Y = cy[0];
                Box(c, ColPanel, X, Y, cw, ch, null, UiMain, 12f);
                CardHead(c, id, "KWall", X, Y, cw, t.WallStack, "bs.ui t.wall");
                float pw = 56f;
                int shown = Mathf.Min(t.WallCount + 1, 5);
                float bh = Mathf.Min(22f, (ch - 60f) / shown - 3f);
                float by = Y + ch - 14 - bh;
                for (int i = 0; i < shown; i++)
                {
                    Box(c, i == 0 ? ColRed : ColBtn, X + pad + 8, by, pw - 16, bh, null, UiMain, 4f);
                    by -= bh + 3 + Mathf.Min(6f, t.WallGap * 4f);
                }
                float rx = X + pad + pw + 6, rw = cw - (rx - X) - pad;
                Arrows(c, T(id, "WallCount", t.WallCount), "bs.ui wall.count -1", "bs.ui wall.count 1", rx, Y + 48, rw, 28);
                Arrows(c, T(id, "WallGap", t.WallGap.ToString("0.00", CultureInfo.InvariantCulture)), "bs.ui wall.gap -1", "bs.ui wall.gap 1", rx, Y + 84, rw, 28);
                Text(c, T(id, "WallHint"), 9, rx, Y + 118, rw, ch - 128, TextAnchor.UpperLeft, ColDim, FontReg);
            }

            // --- 3. Стак полов
            {
                float X = cx[2], Y = cy[0];
                Box(c, ColPanel, X, Y, cw, ch, null, UiMain, 12f);
                CardHead(c, id, "KFloor", X, Y, cw, t.FloorStack, "bs.ui t.floor");
                int shown = Mathf.Min(t.FloorCount + 1, 6);
                float fw = cw - 2 * pad - 60, fx = X + pad + 30, fy = Y + ch - 54;
                for (int i = 0; i < shown; i++)
                    Box(c, i == 0 ? ColRed : ColBtn, fx + i * 5, fy - i * 10, fw - i * 10, 7, null, UiMain, 3f);
                Arrows(c, T(id, "FloorCount", t.FloorCount), "bs.ui floor.count -1", "bs.ui floor.count 1", X + pad, Y + 48, cw - 2 * pad, 28);
                Text(c, T(id, "FloorHint"), 9, X + pad, Y + ch - 40, cw - 2 * pad, 32, TextAnchor.MiddleCenter, ColDim, FontReg);
            }

            // --- 4. Улучшение
            {
                float X = cx[0], Y = cy[1];
                Box(c, ColPanel, X, Y, cw, ch, null, UiMain, 12f);
                CardHead(c, id, "KUpgrade", X, Y, cw, null, null);
                float hw = (cw - 2 * pad - 6) / 2f;
                Btn(c, T(id, "UpOne"), "bs.ui grade 1", ColWine, 12, X + pad, Y + 44, hw, 34, ColText, 9f);
                Btn(c, T(id, "DownOne"), "bs.ui grade -1", ColBtn, 12, X + pad + hw + 6, Y + 44, hw, 34, ColText, 9f);
                Text(c, T(id, "ForceAll"), 9, X + pad, Y + 82, cw - 2 * pad, 16, TextAnchor.MiddleLeft, ColDim);
                float gw = (cw - 2 * pad - 4 * 4) / 5f;
                for (int g = 0; g < 5; g++)
                    Btn(c, T(id, GradeKeys[g]), "bs.ui up " + g, g == 4 ? ColRed : ColBtn, 9, X + pad + g * (gw + 4), Y + 100, gw, 26, ColText, 7f);
                Text(c, T(id, "UpFilter"), 9, X + pad, Y + 130, cw - 2 * pad, 16, TextAnchor.MiddleLeft, ColDim);
                float fh = Mathf.Max(18f, (ch - 150 - 10 - 4) / 2f);
                float fwid = (cw - 2 * pad - 3 * 4) / 4f;
                for (int f = 0; f < 8; f++)
                    Btn(c, T(id, "F" + f), "bs.ui upfilter " + f, t.UpFilter == f ? ColWine : ColBtn, 9, X + pad + (f % 4) * (fwid + 4), Y + 148 + (f / 4) * (fh + 4), fwid, fh, t.UpFilter == f ? ColText : ColDim, 6f);
            }

            // --- 5. Кисть фундаментов
            {
                float X = cx[1], Y = cy[1];
                Box(c, ColPanel, X, Y, cw, ch, null, UiMain, 12f);
                CardHead(c, id, "KBrush", X, Y, cw, t.Brush, "bs.ui t.brush");
                int[] sizes = { 3, 5, 7, 9 };
                Seg(c, sizes.Select(v => v + "×" + v).ToArray(), Array.IndexOf(sizes, t.BrushSize), "bs.ui brush.size", X + pad, Y + 44, cw - 2 * pad, 26);
                float gs = Mathf.Min(cw - 2 * pad, ch - 120);
                float cell = gs / 9f, gx = X + (cw - gs) / 2f, gy = Y + 78;
                int k = t.BrushSize / 2;
                for (int i = 0; i < 9; i++)
                    for (int j = 0; j < 9; j++)
                    {
                        bool inside = Mathf.Abs(i - 4) <= k && Mathf.Abs(j - 4) <= k;
                        string col = i == 4 && j == 4 ? ColRed : inside && t.Brush ? ColWine : ColRow;
                        Box(c, col, gx + i * cell + 1, gy + j * cell + 1, cell - 2, cell - 2, null, UiMain, 0f);
                    }
                Text(c, T(id, "BrushHint", t.BrushSize), 9, X + pad, gy + gs + 4, cw - 2 * pad, Y + ch - (gy + gs + 4) - 6, TextAnchor.MiddleCenter, ColDim, FontReg);
            }

            // --- 6. Авто-двери
            {
                float X = cx[2], Y = cy[1];
                Box(c, ColPanel, X, Y, cw, ch, null, UiMain, 12f);
                CardHead(c, id, "KDoors", X, Y, cw, t.AutoDoors, "bs.ui t.doors");
                Seg(c, new[] { T(id, "DSingle"), T(id, "DDouble") }, ui.DoorTab, "bs.ui doortab", X + pad, Y + 44, cw - 2 * pad, 26);
                if (ui.DoorTab == 0) IconRow(c, DoorsSingle, t.DoorSingle, "bs.ui door1", X + pad, Y + 78, cw - 2 * pad, 4);
                else IconRow(c, DoorsDouble, t.DoorDouble, "bs.ui door2", X + pad, Y + 78, cw - 2 * pad, 4);
                float hw = (cw - 2 * pad - 6) / 2f;
                MiniToggle(c, id, "DLock", t.DoorLock, "bs.ui t.doorlock", X + pad, Y + ch - 38, hw);
                MiniToggle(c, id, "DOpen", t.DoorOpen, "bs.ui t.dooropen", X + pad + hw + 6, Y + ch - 38, hw);
            }

            // --- 7. Авто-окна
            {
                float X = cx[0], Y = cy[2];
                Box(c, ColPanel, X, Y, cw, ch, null, UiMain, 12f);
                CardHead(c, id, "KWindows", X, Y, cw, t.AutoWindows, "bs.ui t.windows");
                Seg(c, new[] { T(id, "WBars"), T(id, "WEmb") }, ui.WinTab, "bs.ui wintab", X + pad, Y + 44, cw - 2 * pad, 26);
                IconRow(c, ui.WinTab == 0 ? WinBars : WinEmb, t.Window, "bs.ui window", X + pad, Y + 78, cw - 2 * pad, 4);
                Text(c, T(id, "WinHint"), 9, X + pad, Y + ch - 40, cw - 2 * pad, 32, TextAnchor.MiddleCenter, ColDim, FontReg);
            }

            // --- 8. Авто-люки
            {
                float X = cx[1], Y = cy[2];
                Box(c, ColPanel, X, Y, cw, ch, null, UiMain, 12f);
                CardHead(c, id, "KHatch", X, Y, cw, t.AutoHatch, "bs.ui t.hatch");
                Seg(c, new[] { T(id, "HSquare"), T(id, "HTri") }, ui.HatchTab, "bs.ui hatchtab", X + pad, Y + 44, cw - 2 * pad, 26);
                if (ui.HatchTab == 0) IconRow(c, HatchSq, t.HatchSquare, "bs.ui hatch1", X + pad, Y + 78, cw - 2 * pad, 4);
                else IconRow(c, HatchTr, t.HatchTri, "bs.ui hatch2", X + pad, Y + 78, cw - 2 * pad, 4);
                MiniToggle(c, id, "DLock", t.HatchLock, "bs.ui t.hatchlock", X + pad, Y + ch - 38, cw - 2 * pad);
            }

            // --- 9. Турели
            {
                float X = cx[2], Y = cy[2];
                Box(c, ColPanel, X, Y, cw, ch, null, UiMain, 12f);
                CardHead(c, id, "KTurret", X, Y, cw, t.TurretFill, "bs.ui t.turret");
                Seg(c, new[] { T(id, "TRifles"), T(id, "TPistols"), T(id, "TSmg"), T(id, "TMisc") }, ui.TurretTab, "bs.ui turrettab", X + pad, Y + 44, cw - 2 * pad, 26);
                IconRow(c, TurretGuns[Mathf.Clamp(ui.TurretTab, 0, 3)], t.TurretGun, "bs.ui turret", X + pad, Y + 78, cw - 2 * pad, 4);
                Text(c, T(id, "TurretHint"), 9, X + pad, Y + ch - 40, cw - 2 * pad, 32, TextAnchor.MiddleCenter, ColDim, FontReg);
            }
        }

        private static readonly string[] Cats = { "Construction", "Items", "Resources", "Tool", "Weapon", "Ammunition", "Attire", "Electrical", "Fun", "Medical", "Food", "Traps", "Component", "Misc" };

        private void BuildItems(CuiElementContainer c, BasePlayer player, UiState ui, float x, float y, float w)
        {
            var id = Id(player);
            var s = P(id);
            if (!Has(player, PermItems))
            {
                Box(c, ColPanel, x, y, w, Bottom - y, null, UiMain, 12f);
                Text(c, T(id, "NoPerm"), 12, x, y, w, Bottom - y, TextAnchor.MiddleCenter, ColDim);
                return;
            }

            int[] amounts = { 1, 10, 100, 1000, 100000 };
            string[] al = { "×1", "×10", "×100", "×1000", T(id, "Stack") };
            const float aw = 56f;
            float sw = w - amounts.Length * (aw + G);
            Input(c, UiMain + ".search", ui.Search, T(id, "SearchHint"), "bs.ui search", x, y, sw, 30);
            for (int i = 0; i < amounts.Length; i++)
                Btn(c, al[i], "bs.ui amount " + amounts[i], s.ItemAmount == amounts[i] ? ColWine : ColBtn, 10, x + sw + G + i * (aw + G), y, aw, 30);
            y += 30 + G;

            float cw = Col(w, 7);
            for (int i = 0; i < Cats.Length; i++)
            {
                bool a = ui.Category == Cats[i] && string.IsNullOrEmpty(ui.Search);
                Btn(c, T(id, "C" + Cats[i]), "bs.ui cat " + Cats[i], a ? ColWine : ColBtn, 9, x + (i % 7) * (cw + G), y + (i / 7) * (24 + G), cw, 24);
            }
            y += 2 * 24 + G + G + 4;

            IEnumerable<ItemDefinition> q = ItemManager.itemList;
            if (!string.IsNullOrEmpty(ui.Search))
                q = q.Where(d => d.displayName.english.IndexOf(ui.Search, StringComparison.OrdinalIgnoreCase) >= 0 || d.shortname.IndexOf(ui.Search, StringComparison.OrdinalIgnoreCase) >= 0);
            else q = q.Where(d => d.category.ToString() == ui.Category);
            var list = q.OrderBy(d => d.displayName.english).ToList();

            const int cols = 9;
            float cell = Col(w, cols);
            float pagerY = Bottom - 30f;
            float avail = pagerY - G - y;
            int rows = Mathf.Max(1, Mathf.FloorToInt(avail / (cell + 18f)));
            float step = avail / rows;
            int per = cols * rows;
            int pages = Mathf.Max(1, Mathf.CeilToInt(list.Count / (float)per));
            ui.Page = Mathf.Clamp(ui.Page, 0, pages - 1);
            for (int i = 0; i < per && ui.Page * per + i < list.Count; i++)
            {
                var d = list[ui.Page * per + i];
                float ix = x + (i % cols) * (cell + G), iy = y + (i / cols) * step;
                Box(c, ColRow, ix, iy, cell, cell, null, UiMain, 10f);
                ItemIcon(c, d.itemid, ix + 8, iy + 8, cell - 16);
                Btn(c, "", "bs.ui give " + d.itemid, "0 0 0 0", 10, ix, iy, cell, cell, ColText, 0f);
                Text(c, d.displayName.english, 8, ix - 2, iy + cell + 1, cell + 4, 16, TextAnchor.UpperCenter, ColDim, FontReg);
            }
            if (list.Count == 0) Text(c, T(id, "ItemsCount", 0), 11, x, y, w, avail, TextAnchor.MiddleCenter, ColDim, FontReg);

            // пейджер строго по центру, счётчик слева, подсказка справа
            Box(c, ColPanel, x, pagerY, w, 30, null, UiMain, 10f);
            Text(c, T(id, "ItemsCount", list.Count), 10, x + 12, pagerY, w / 2f - 70, 30, TextAnchor.MiddleLeft, ColDim, FontReg);
            Btn(c, "<", "bs.ui page -1", pages > 1 ? ColBtn : ColRow, 12, x + w / 2f - 60, pagerY + 3, 30, 24);
            Text(c, $"{ui.Page + 1}/{pages}", 11, x + w / 2f - 26, pagerY, 52, 30, TextAnchor.MiddleCenter, ColText);
            Btn(c, ">", "bs.ui page 1", pages > 1 ? ColBtn : ColRow, 12, x + w / 2f + 30, pagerY + 3, 30, 24);
            Text(c, "×" + (s.ItemAmount >= 100000 ? T(id, "Stack") : s.ItemAmount.ToString()), 10, x + w / 2f + 70, pagerY, w / 2f - 82, 30, TextAnchor.MiddleRight, ColDim, FontReg);
        }

        private void BuildRaid(CuiElementContainer c, BasePlayer player, float x, float y, float w)
        {
            var id = Id(player);
            RaidTest test;
            bool active = _raidTests.TryGetValue(id, out test);

            Box(c, "0.620 0.082 0.188 0.18", x, y, w, 70, null, UiMain, 14f);
            Text(c, T(id, "RaidTitle"), 15, x + 16, y + 6, w - 32, 22, TextAnchor.MiddleCenter);
            Text(c, T(id, "RaidHint2"), 10, x + 16, y + 30, w - 32, 36, TextAnchor.UpperCenter, ColDim, FontReg);
            y += 70 + G;

            // блок действий всегда одной высоты — набор ниже не прыгает
            const float actH = 104f;
            float hw = Col(w, 2);
            if (!active)
                Btn(c, T(id, "RaidStart"), "bs.ui raid.start", ColRed, 15, x, y, w, actH, ColText, 14f);
            else
            {
                int alive = test.Ids.Count(n => BaseNetworkable.serverEntities.Find(new NetworkableId(n)) != null);
                Box(c, ColRow, x, y, w, 48, null, UiMain, 14f);
                Text(c, T(id, "RaidProgress", test.Total - alive, test.Total), 14, x, y, w, 48, TextAnchor.MiddleCenter);
                Btn(c, T(id, "RaidRestore"), "bs.ui raid.restore", ColWine, 14, x, y + 48 + G, hw, 48, ColText, 14f);
                Btn(c, T(id, "RaidMoreKit"), "bs.ui raid.kit", ColBtn, 13, x + hw + G, y + 48 + G, hw, 48, ColText, 14f);
            }
            y += actH + G;
            y = BuildRaidExtra(c, player, x, y, w);

            // рейд-набор: панель до низа, иконки по центру
            float ph = Bottom - y;
            Box(c, ColPanel, x, y, w, ph, null, UiMain, 12f);
            Text(c, T(id, "RaidKitTitle"), 10, x, y + 10, w, 18, TextAnchor.MiddleCenter, ColDim);
            var kit = _config.RaidKit.Select(k => new { k, def = ItemManager.FindItemDefinition(k.Item) }).Where(e => e.def != null).Take(12).ToList();
            const float iw = 56f;
            int perRow = Mathf.Min(10, Mathf.Max(1, kit.Count));
            int kitRows = Mathf.Max(1, Mathf.CeilToInt(kit.Count / (float)perRow));
            float gridH = kitRows * iw + (kitRows - 1) * G;
            float gy = y + (ph - gridH) / 2f;
            for (int i = 0; i < kit.Count; i++)
            {
                int row = i / perRow, inRow = Mathf.Min(perRow, kit.Count - row * perRow);
                float rowW = inRow * iw + (inRow - 1) * G;
                float ix = x + (w - rowW) / 2f + (i % perRow) * (iw + G), iy = gy + row * (iw + G);
                Box(c, ColRow, ix, iy, iw, iw, null, UiMain, 10f);
                ItemIcon(c, kit[i].def.itemid, ix + 10, iy + 8, iw - 20);
                Text(c, "×" + kit[i].k.Amount, 9, ix, iy + iw - 16, iw - 6, 14, TextAnchor.MiddleRight, ColText, Font);
            }
        }

        private void BuildTp(CuiElementContainer c, BasePlayer player, UiState ui, float x, float y, float w)
        {
            var id = Id(player);
            var s = P(id);
            float hw = Col(w, 2);

            Section(c, T(id, "Homes", s.Homes.Count, _config.MaxHomes), x, y, w);
            y += 22;
            Input(c, UiMain + ".home", ui.HomeName, T(id, "HomeHint"), "bs.ui home.name", x, y, hw, 32);
            Btn(c, T(id, "HomeSetBtn"), "bs.ui home.set", ColWine, 12, x + hw + G, y, hw, 32);
            y += 32 + G;

            const int homeRows = 4;
            float listH = homeRows * 32 + (homeRows + 1) * G;
            Box(c, ColPanel, x, y, w, listH, null, UiMain, 12f);
            if (s.Homes.Count == 0) Text(c, T(id, "NoHomes"), 11, x, y, w, listH, TextAnchor.MiddleCenter, ColDim, FontReg);
            for (int i = 0; i < s.Homes.Count && i < homeRows; i++)
            {
                var h = s.Homes[i];
                float ry = y + G + i * (32 + G);
                Box(c, ColRow, x + G, ry, w - 2 * G, 32, null, UiMain, 9f);
                Text(c, $"{h.Name}  <color=#8a807e>· {Mathf.RoundToInt(h.X)} / {Mathf.RoundToInt(h.Z)}</color>", 12, x + G + 12, ry, w - 180, 32);
                Btn(c, T(id, "Go"), "bs.ui home.go " + i, ColWine, 10, x + w - G - 4 - 26 - G - 90, ry + 4, 90, 24);
                Btn(c, "X", "bs.ui home.del " + i, ColBtn, 11, x + w - G - 4 - 26, ry + 4, 26, 24, "0.92 0.42 0.48 1");
            }
            y += listH + G;

            Btn(c, T(id, "BackBtn"), "bs.ui back", ColBtn, 12, x, y, hw, 36);
            Box(c, ColRow, x + hw + G, y, hw, 36, null, UiMain, 10f);
            Text(c, T(id, "MapTpInfo"), 10, x + hw + G, y, hw, 36, TextAnchor.MiddleCenter, ColDim, FontReg);
            y += 36 + G + 4;

            Section(c, T(id, "TprTitle"), x, y, w);
            y += 22;
            Box(c, ColPanel, x, y, w, Bottom - y, null, UiMain, 12f);
            var others = BasePlayer.activePlayerList.Where(p => p != player).OrderBy(p => p.displayName).Take(9).ToList();
            float bw = Col(w - 2 * G, 3);
            for (int k = 0; k < others.Count; k++)
                Btn(c, others[k].displayName, "bs.ui tpr " + Id(others[k]), ColBtn, 10, x + G + (k % 3) * (bw + G), y + G + (k / 3) * (30 + G), bw, 30);
            if (others.Count == 0) Text(c, T(id, "NoOneOnline"), 11, x, y, w, Bottom - y, TextAnchor.MiddleCenter, ColDim, FontReg);
        }

        private void ShowTpPopup(BasePlayer target, BasePlayer from)
        {
            var id = Id(target);
            var c = new CuiElementContainer();
            const float w = 360f, h = 120f;
            if (_roundPng != null)
                c.Add(new CuiElement
                {
                    Name = UiPopup,
                    Parent = "Overlay",
                    Components =
                    {
                        new RoundedImage { Png = _roundPng, Color = ColBg, Ppu = 1f },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = $"{-w / 2f} 140", OffsetMax = $"{w / 2f} {140 + h}" },
                        new CuiNeedsCursorComponent()
                    }
                });
            else
                c.Add(new CuiPanel { Image = { Color = ColBg }, RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.5 0", OffsetMin = $"{-w / 2f} 140", OffsetMax = $"{w / 2f} {140 + h}" }, CursorEnabled = true }, "Overlay", UiPopup);
            Text(c, T(id, "TprPopup", from.displayName), 14, 16, 12, w - 32, 24, TextAnchor.MiddleLeft, ColText, Font, UiPopup);
            Text(c, T(id, "TprPopupHint", _config.TprTimeout.ToString("0")), 10, 16, 36, w - 32, 18, TextAnchor.MiddleLeft, ColDim, FontReg, UiPopup);
            Btn(c, T(id, "Accept"), "bs.ui tpa", ColWine, 12, 16, 66, (w - 40) / 2f, 38, ColText, 12f, UiPopup);
            Btn(c, T(id, "Decline"), "bs.ui tpdeny", ColBtn, 12, 24 + (w - 40) / 2f, 66, (w - 40) / 2f, 38, ColText, 12f, UiPopup);
            CuiHelper.DestroyUi(target, UiPopup);
            AddUiScaled(target, c);
        }

        private void Toast(BasePlayer player, string text, bool warn)
        {
            if (player == null || !player.IsConnected) return;
            var c = new CuiElementContainer();
            if (_roundPng != null)
                c.Add(new CuiElement
                {
                    Name = UiToast,
                    Parent = "Hud",
                    Components =
                    {
                        new RoundedImage { Png = _roundPng, Color = warn ? "0.40 0.06 0.11 0.92" : "0.07 0.065 0.065 0.88", Ppu = 1f },
                        new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-220 -72", OffsetMax = "220 -32" }
                    }
                });
            else
                c.Add(new CuiPanel { Image = { Color = warn ? "0.40 0.06 0.11 0.92" : "0.07 0.065 0.065 0.88" }, RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-220 -72", OffsetMax = "220 -32" } }, "Hud", UiToast);
            c.Add(new CuiLabel { Text = { Text = text, FontSize = 13, Align = TextAnchor.MiddleCenter, Color = ColText, Font = Font }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, UiToast);
            CuiHelper.DestroyUi(player, UiToast);
            AddUiScaled(player, c);
            var p = player;
            timer.Once(3f, () => { if (p != null && p.IsConnected) CuiHelper.DestroyUi(p, UiToast); });
        }

        #endregion

        #region Локализация

        private string Lang(ulong id) => lang.GetLanguage(id.ToString()) == "ru" ? "ru" : "en";

        private string T(ulong id, string key, params object[] args)
        {
            var msg = lang.GetMessage(key, this, id.ToString());
            if (args == null || args.Length == 0) return msg;
            try { return string.Format(msg, args); } catch { return msg; }
        }

        private void Reply(BasePlayer player, string key, params object[] args)
        {
            if (player == null || !player.IsConnected) return;
            SendReply(player, "<color=#e0384e>[Билд]</color> " + T(Id(player), key, args));
        }

        private void RegisterLang()
        {
            var ru = new Dictionary<string, string>();
            var en = new Dictionary<string, string>();
            foreach (var m in Messages) { ru[m[0]] = m[1]; en[m[0]] = m[2]; }
            lang.RegisterMessages(en, this, "en");
            lang.RegisterMessages(ru, this, "ru");
        }

        private static readonly string[][] Messages =
        {
            new[] { "NoPerm", "Нет доступа.", "No access." },
            new[] { "Title", "БИЛД-СЕРВЕР", "BUILD SERVER" },
            new[] { "Sub", "стройка · креатив", "build · creative" },
            new[] { "SubPlot", "ты на участке «{0}» · {1}", "you are on plot \"{0}\" · {1}" },
            new[] { "TabMain", "ГЛАВНОЕ", "MAIN" },
            new[] { "TabItems", "ПРЕДМЕТЫ", "ITEMS" },
            new[] { "TabBuild", "СТРОЙКА", "BUILD" },
            new[] { "TabRaid", "РЕЙД-ТЕСТ", "RAID TEST" },
            new[] { "TabElec", "ЭЛЕКТРИКА", "ELECTRIC" }, new[] { "TabSpawn", "СПАВН", "SPAWN" },
            new[] { "RaidHint2", "Наведись на базу и начни тест: база сохранится, крафт станет мгновенным и бесплатным. После — одна кнопка вернёт базу. Можно пустить друга рейдить по таймеру и посчитать самый дешёвый путь до шкафа.", "Aim at your base and start: it's saved, crafting becomes instant and free. One button restores it. Let a friend raid on a timer, or find the cheapest path to the TC." },
            new[] { "FrTitle", "ДРУГ РЕЙДИТ", "FRIEND RAIDS" }, new[] { "FrPick", "Выбери, кого пустить", "Pick who raids" }, new[] { "FrNoOne", "Никого онлайн", "No one online" },
            new[] { "FrMin", "{0} мин", "{0} min" }, new[] { "FrStart", "ПУСТИТЬ РЕЙДИТЬ", "LET THEM RAID" }, new[] { "FrStop", "ОСТАНОВИТЬ", "STOP" },
            new[] { "FrActive", "{0} рейдит твою базу\nосталось {1}:{2:00}", "{0} is raiding your base\n{1}:{2:00} left" },
            new[] { "FrStarted", "{0} может рейдить твою базу {1} мин. Крафт мгновенный.", "{0} may raid your base for {1} min. Crafting is instant." },
            new[] { "FrInvited", "{0} пустил тебя рейдить свою базу на {1} мин. Крафт мгновенный и бесплатный.", "{0} lets you raid their base for {1} min. Crafting is instant and free." },
            new[] { "FrTimeUp", "Время рейда вышло", "Raid time is up" }, new[] { "FrStopped", "Рейд с другом остановлен", "Friend raid stopped" },
            new[] { "PathTitle", "ПУТЬ ДО ШКАФА", "PATH TO TC" }, new[] { "PathBtn", "ПОСЧИТАТЬ", "CALCULATE" },
            new[] { "PathHint", "Наведись на свою базу и нажми «Посчитать». Проверю 8 сторон и крышу, найду, где до шкафа дешевле всего по сере, и нарисую путь стрелками на 30 секунд.", "Aim at your base and press Calculate: 8 sides and the roof are checked, the cheapest path to the TC by sulfur is drawn for 30 seconds." },
            new[] { "PathBest", "Дешевле всего: {0} · слоёв {1} · {2} серы", "Cheapest: {0} · {1} layers · {2} sulfur" }, new[] { "PathMore", "…и ещё {0}", "…and {0} more" },
            new[] { "PathNoTc", "В этой постройке нет шкафа", "No TC in this building" }, new[] { "PathOpen", "До шкафа можно дойти без взрывчатки", "The TC is reachable without explosives" },
            new[] { "DirN", "север", "north" }, new[] { "DirNE", "северо-восток", "north-east" }, new[] { "DirE", "восток", "east" }, new[] { "DirSE", "юго-восток", "south-east" },
            new[] { "DirS", "юг", "south" }, new[] { "DirSW", "юго-запад", "south-west" }, new[] { "DirW", "запад", "west" }, new[] { "DirNW", "северо-запад", "north-west" }, new[] { "DirTop", "крыша", "roof" },
            new[] { "RI_0", "ракет", "rockets" }, new[] { "RI_1", "C4", "C4" }, new[] { "RI_2", "сэтчелей", "satchels" }, new[] { "RI_3", "разрывных", "explo ammo" },
            new[] { "ElLib", "БИБЛИОТЕКА СХЕМ", "CIRCUIT LIBRARY" }, new[] { "ElPlace", "ПОСТАВИТЬ", "PLACE" },
            new[] { "ElLibHint", "Схема ставится туда, куда смотришь, и сразу соединяется проводами.", "The circuit is placed where you look and wired up right away." },
            new[] { "ElMineTitle", "МОИ СХЕМЫ · до {0}", "MY CIRCUITS · up to {0}" }, new[] { "ElCopy", "КОПИРОВАТЬ СХЕМУ (радиус {0} м от прицела)", "COPY CIRCUIT ({0} m around aim)" },
            new[] { "ElMineEmpty", "Пока пусто. Собери схему, наведись на неё и нажми «Копировать» — детали и провода сохранятся.", "Empty. Build a circuit, aim at it and press Copy — parts and wires are saved." },
            new[] { "ElMine", "Схема {0}", "Circuit {0}" }, new[] { "ElParts", "дет.", "parts" }, new[] { "ElWires", "пров.", "wires" }, new[] { "ElPaste", "ВСТАВИТЬ", "PASTE" },
            new[] { "ElLoad", "ПОКАЗ НАГРУЗКИ", "POWER VIEW" }, new[] { "ElLoadHint", "Над каждой деталью в 15 м: сколько ватт приходит, сколько тратит и сколько отдаёт дальше.", "Above every part within 15 m: watts in, used and passed on." },
            new[] { "ElLoadFmt", "вход {0} · расход {1} · выход {2}", "in {0} · use {1} · out {2}" },
            new[] { "ElCostBtn", "СТОИМОСТЬ СХЕМЫ (радиус {0} м)", "CIRCUIT COST ({0} m)" }, new[] { "ElCost", "Посчитано деталей: {0}", "Parts counted: {0}" },
            new[] { "ElNone", "Рядом нет электрики", "No electrics nearby" }, new[] { "ElPlaced", "Поставлено деталей: {0}, проводов: {1}", "Placed {0} parts, {1} wires" }, new[] { "ElCopied", "Сохранено: {0} деталей, {1} проводов", "Saved: {0} parts, {1} wires" },
            new[] { "SpCat_trees", "ДЕРЕВЬЯ И КУСТЫ", "TREES & BUSHES" }, new[] { "SpCat_ore", "КАМНИ И РУДА", "ROCKS & ORE" }, new[] { "SpCat_loot", "ЯЩИКИ С ЛУТОМ", "LOOT CRATES" }, new[] { "SpCat_npc", "НПС И ТУРЕЛИ", "NPC & TURRETS" },
            new[] { "SpFoot", "Поставлено тобой: {0} из {1}. Объект появляется там, куда смотришь; убрать — молотком или кнопкой справа.", "Spawned by you: {0} of {1}. Appears where you look; remove with the hammer or the button." },
            new[] { "SpClear", "УБРАТЬ ВСЁ МОЁ", "REMOVE ALL MINE" }, new[] { "SpLook", "Наведись на землю или постройку", "Aim at the ground or a building" },
            new[] { "SpLimit", "Предел: {0} объектов", "Limit: {0} objects" }, new[] { "SpBad", "Этот объект не создаётся — проверь префаб в конфиге", "This object can't be spawned — check the prefab in the config" },
            new[] { "SpDone", "{0} · {1}/{2}", "{0} · {1}/{2}" },
            new[] { "PlotPvp", "PvP на участке", "PvP on plot" }, new[] { "PvpOn", "На участке «{0}» включён PvP — урон проходит, бессмертия нет", "PvP is ON at {0} — damage is on, no god mode" }, new[] { "PvpOff", "На участке «{0}» PvP выключен", "PvP is OFF at {0}" },
            new[] { "TabPlots", "УЧАСТКИ", "PLOTS" },
            new[] { "TabTp", "ТЕЛЕПОРТ", "TELEPORT" },
            new[] { "TabTools", "ИНСТРУМЕНТЫ", "BUILD TOOLS" },
            new[] { "HudCost", "ЦЕНА ПОСТРОЙКИ", "RESOURCE COST" },
            new[] { "HudUpkeep", "СОДЕРЖАНИЕ (АПКИП)", "UPKEEP" },
            new[] { "HudRaid", "РЕЙД: НАВЕДИСЬ", "RAID: AIM AT IT" },
            new[] { "HudRaidOf", "РЕЙД: {0}", "RAID: {0}" },
            new[] { "HudRaidAll", "всё здание: {0} ракет", "whole building: {0} rockets" },
            new[] { "HudLook", "наведись на постройку или встань на неё", "aim at or stand on a building" },
            new[] { "HudTwig", "Считать солому", "Include twig costs" },
            new[] { "HudShowUpkeep", "Показать содержание (апкип)", "Show upkeep" },
            new[] { "HudBreakdown", "Деплои на ресурсы", "Breakdown deployables" },
            new[] { "HudRaidBtn", "РЕЙД-КАЛЬКУЛЯТОР", "RAID COUNTER" },
            new[] { "HudCostBtn", "ЦЕНА ПОСТРОЙКИ", "RESOURCE COST" },
            new[] { "HudAsPlaced", "как ставишь", "as placed" },
            new[] { "HudSelected", "МАТЕРИАЛ: <color=#f0a6b1>{0}</color>    СКИН: <color=#f0a6b1>{1}</color>", "SELECTED: <color=#f0a6b1>{0}</color>    SKIN: <color=#f0a6b1>{1}</color>" },
            new[] { "HudSkins", "СКИНЫ", "SKINS" },
            new[] { "HudMenuShort", "МЕНЮ", "MENU" },
            new[] { "HudSymShort", "СИММ.", "SYMM." },
            new[] { "LLoadShort", "ЗАГРУЗИТЬ", "LOAD" },
            new[] { "LSaveShort", "СОХРАНИТЬ", "SAVE" },
            new[] { "LClearShort", "ОЧИСТИТЬ", "CLEAR" },
            new[] { "LPrivateShort", "ЗАКРЫТ", "PRIVATE" },
            new[] { "OptHudCost", "Панель цены и рейда", "Cost & raid panel" },
            new[] { "OptHudBar", "Панель материала внизу", "Material bar" },
            new[] { "TabLibrary", "ПОСТРОЙКИ", "BUILDS" },
            new[] { "TabZones", "ЗОНЫ", "ZONES" },
            new[] { "TabEnv", "ОКРУЖЕНИЕ", "ENVIRONMENT" },
            new[] { "NoBlueprints", "Плагин Blueprints не загружен", "Blueprints plugin is not loaded" },
            new[] { "SQuick", "БЫСТРО", "QUICK OPTIONS" },
            new[] { "QWall3", "Стены ×3", "Wallstack ×3" },
            new[] { "QWallGap", "Стены с зазором", "Wide gap walls" },
            new[] { "QFloor3", "Полы ×3", "Floorstack ×3" },
            new[] { "QReset", "Сбросить всё", "Reset tools" },
            new[] { "QWipeLevel", "Снести выше меня", "Wipe to level" },
            new[] { "QWipeDeploy", "Снести деплои", "Wipe deployables" },
            new[] { "QWipeFound", "До фундамента", "Wipe to foundation" },
            new[] { "QFurnaces", "Печи", "Furnaces" },
            new[] { "SPersonal", "ЛИЧНОЕ", "PERSONAL" },
            new[] { "PFly", "Полёт", "NoClip" },
            new[] { "PFree", "Бесплатно", "Free build" },
            new[] { "PGod", "Бессмертие", "God mode" },
            new[] { "PStab", "Прочность", "Stability" },
            new[] { "PAuth", "Авто-шкаф", "Auto TC" },
            new[] { "PMapTp", "ТП метка", "Map TP" },
            new[] { "PPlan", "План + молоток", "Plan + hammer" },
            new[] { "PStrip", "Очистить инвентарь", "Clear inventory" },
            new[] { "PStripSure", "Точно? Ещё раз", "Sure? Click again" },
            new[] { "PUndo", "Отменить", "Undo" },
            new[] { "PRedo", "Вернуть", "Redo" },
            new[] { "PKit", "Набор строителя", "Builder kit" },
            new[] { "SPlot", "УЧАСТОК", "PLOT" },
            new[] { "LPrivate", "Закрытый", "Private" },
            new[] { "LShow", "Показать границу", "Show border" },
            new[] { "LSave", "Сохранить постройку", "Save build" },
            new[] { "LLoad", "Загрузить постройку", "Load build" },
            new[] { "LClear", "Очистить участок", "Clear plot" },
            new[] { "LClearSure", "Всё снесётся! Нажми ещё раз", "Everything goes! Click again" },
            new[] { "STransport", "ТРАНСПОРТ", "TRANSPORT" },
            new[] { "KSym", "СИММЕТРИЯ", "SYMMETRY" },
            new[] { "Sym0", "ВЫКЛ", "NONE" },
            new[] { "Sym1", "ЗЕРКАЛО ЛЕВО-ПРАВО", "MIRROR X" },
            new[] { "Sym2", "ЗЕРКАЛО ПЕРЕД-ЗАД", "MIRROR Z" },
            new[] { "Sym3", "ЗЕРКАЛО 4 СТОРОНЫ", "MIRROR 4-WAY" },
            new[] { "Sym4", "ПОВОРОТ 180°", "ROTATE 180°" },
            new[] { "Sym5", "ПОВОРОТ 90° ×4", "ROTATE 90° ×4" },
            new[] { "SymInfo", "Встань на фундамент в центре базы\nи нажми «Центр здесь»", "Stand on the middle foundation\nand press Set center" },
            new[] { "SymInfoSet", "Центр: {0} / {1}\nвсё, что ставишь, повторится", "Center: {0} / {1}\neverything you place is mirrored" },
            new[] { "SymCenter", "ЦЕНТР ЗДЕСЬ", "SET CENTER" },
            new[] { "SymSet", "Центр симметрии установлен — красные линии на 15 сек", "Symmetry center set — red lines for 15s" },
            new[] { "SymNeedFoundation", "Встань на фундамент или пол", "Stand on a foundation or floor" },
            new[] { "KWall", "СТАК СТЕН", "WALL STACKING" },
            new[] { "WallCount", "+{0} сверху", "+{0} above" },
            new[] { "WallGap", "зазор {0} м", "gap {0} m" },
            new[] { "WallHint", "Ставишь стену — сверху появится ещё столько же", "Place a wall — more appear above it" },
            new[] { "KFloor", "СТАК ПОЛОВ", "FLOOR STACKING" },
            new[] { "FloorCount", "+{0} этажей", "+{0} floors" },
            new[] { "FloorHint", "Ставишь пол — этажи над ним появятся сами", "Place a floor — floors above appear" },
            new[] { "KUpgrade", "УЛУЧШЕНИЕ", "UPGRADE" },
            new[] { "UpOne", "ВЫШЕ +1", "UPGRADE +1" },
            new[] { "DownOne", "НИЖЕ −1", "DOWNGRADE −1" },
            new[] { "ForceAll", "ВСЁ ЗДАНИЕ В:", "FORCE ALL:" },
            new[] { "UpFilter", "ТОЛЬКО:", "ONLY:" },
            new[] { "F0", "ВСЁ", "ALL" },
            new[] { "F1", "ФУНД.", "FOUND." },
            new[] { "F2", "СТЕНЫ", "WALLS" },
            new[] { "F3", "ПОЛЫ", "FLOORS" },
            new[] { "F4", "ПРОЁМЫ", "DOORWAYS" },
            new[] { "F5", "ОКНА", "WINDOWS" },
            new[] { "F6", "ЛЕСТН.", "STAIRS" },
            new[] { "F7", "КРЫШИ", "ROOFS" },
            new[] { "GradedUp", "Улучшено: {0}", "Upgraded: {0}" },
            new[] { "GradedDown", "Понижено: {0}", "Downgraded: {0}" },
            new[] { "KBrush", "КИСТЬ ФУНДАМЕНТОВ", "FOUNDATION BRUSH" },
            new[] { "BrushHint", "Ставишь фундамент — заливается {0}×{0}", "Place a foundation — fills {0}×{0}" },
            new[] { "KDoors", "АВТО-ДВЕРИ", "AUTO DOORS" },
            new[] { "DSingle", "ОДИНАРНЫЕ", "SINGLE" },
            new[] { "DDouble", "ДВОЙНЫЕ", "DOUBLE" },
            new[] { "DLock", "Код-замок", "Code lock" },
            new[] { "DOpen", "Открыты", "Open" },
            new[] { "KWindows", "АВТО-ОКНА", "AUTO WINDOWS" },
            new[] { "WBars", "РЕШЁТКИ", "WINDOW" },
            new[] { "WEmb", "АМБРАЗУРЫ", "EMBRASURE" },
            new[] { "WinHint", "Ставишь стену с окном — сразу с решёткой", "Window walls get bars right away" },
            new[] { "KHatch", "АВТО-ЛЮКИ", "AUTO HATCH" },
            new[] { "HSquare", "КВАДРАТ", "SQUARE" },
            new[] { "HTri", "ТРЕУГОЛЬНИК", "TRIANGLE" },
            new[] { "KTurret", "ТУРЕЛИ", "TURRET AUTOFILL" },
            new[] { "TRifles", "ВИНТ.", "RIFLES" },
            new[] { "TPistols", "ПИСТ.", "PISTOLS" },
            new[] { "TSmg", "ПП", "SMG" },
            new[] { "TMisc", "ДРУГОЕ", "MISC" },
            new[] { "TurretHint", "В поставленную турель: ган + 256 патронов", "New turrets get the gun + 256 ammo" },
            new[] { "UndoEmpty", "Нечего отменять", "Nothing to undo" },
            new[] { "RedoEmpty", "Нечего вернуть", "Nothing to redo" },
            new[] { "Undone", "Отменено: {0}", "Undone: {0}" },
            new[] { "Redone", "Возвращено: {0}", "Redone: {0}" },
            new[] { "Wiped", "Снесено: {0}", "Wiped: {0}" },
            new[] { "VehicleFail", "Не удалось заспавнить транспорт", "Couldn't spawn the vehicle" },
            new[] { "VehicleSpawned", "{0} рядом с тобой", "{0} spawned next to you" },
            new[] { "PlotSaved", "Постройка сохранена", "Build saved" },
            new[] { "PlotLoaded", "Постройка загружается…", "Loading build…" },
            new[] { "PlotNoSave", "Нет сохранённой постройки", "No saved build" },
            new[] { "PlotNotOwner", "Это не твой участок", "Not your plot" },
            new[] { "PlotPrivate", "Участок {0} закрыт", "{0}'s plot is private" },
            new[] { "EWeather", "ПОГОДА", "WEATHER" },
            new[] { "EClouds", "ОБЛАКА · ВРЕМЯ", "CLOUDS · TIME" },
            new[] { "ResetAll", "Сбросить всё", "Reset all" },
            new[] { "Default", "ПО УМОЛЧАНИЮ", "DEFAULT" },
            new[] { "MyTimeShort", "Моё время", "My time" },
            new[] { "EnvFooter", "Погода общая на весь сервер · Shift + клик — шаг ×2 · время видишь только ты", "Weather is server-wide · Shift+click — double step · time is only yours" },
            new[] { "EnvFooterNoPerm", "Погоду меняет админ · своё время можешь менять ты", "Only admins change weather · your own time is yours" },
            new[] { "W_fog", "Туман", "Fog" },
            new[] { "W_rain", "Дождь", "Rain" },
            new[] { "W_rainbow", "Радуга", "Rainbow" },
            new[] { "W_thunder", "Гроза", "Thunder" },
            new[] { "W_wind", "Ветер", "Wind" },
            new[] { "W_atmosphere_brightness", "Яркость неба", "Atmosphere brightness" },
            new[] { "W_atmosphere_contrast", "Контраст неба", "Atmosphere contrast" },
            new[] { "W_atmosphere_directionality", "Направленность", "Atmosphere directionality" },
            new[] { "W_atmosphere_mie", "Дымка (Mie)", "Atmosphere Mie" },
            new[] { "W_atmosphere_rayleigh", "Рассеяние (Rayleigh)", "Atmosphere Rayleigh" },
            new[] { "W_cloud_attenuation", "Затухание облаков", "Cloud attenuation" },
            new[] { "W_cloud_brightness", "Яркость облаков", "Cloud brightness" },
            new[] { "W_cloud_coloring", "Цвет облаков", "Cloud coloring" },
            new[] { "W_cloud_coverage", "Облачность", "Cloud coverage" },
            new[] { "W_cloud_opacity", "Плотность", "Cloud opacity" },
            new[] { "W_cloud_saturation", "Насыщенность", "Cloud saturation" },
            new[] { "W_cloud_scattering", "Рассеивание", "Cloud scattering" },
            new[] { "W_cloud_sharpness", "Резкость", "Cloud sharpness" },
            new[] { "W_cloud_size", "Размер облаков", "Cloud size" },
            new[] { "ZoneCurrent", "ТЕКУЩИЙ УЧАСТОК", "CURRENT PLOT" },
            new[] { "ZoneNone", "Ты не на участке.\nВыбери зону справа — получишь свободный участок и сразу окажешься на нём.", "You're not on a plot.\nPick a zone on the right to get a free plot and go there." },
            new[] { "ZoneOwner", "{0} · {1:0} м · участников {2}", "{0} · {1:0} m · members {2}" },
            new[] { "ZoneSmall", "УЧАСТКИ", "PLOTS" },
            new[] { "ZoneVipTitle", "VIP-ЗОНЫ · БОЛЬШЕ МЕСТА", "VIP ZONES · MORE SPACE" },
            new[] { "ZoneVipYes", "У тебя VIP — выбирай любую", "You have VIP — pick any" },
            new[] { "ZoneVipBuy", "VIP — в магазине хаба (/shop)", "Get VIP in the hub shop (/shop)" },
            new[] { "ZoneFree", "{0} / {1} СВОБОДНО", "{0} / {1} AVAILABLE" },
            new[] { "ZoneMine", "МОЙ УЧАСТОК · ТП", "MY PLOT · TP" },
            new[] { "ZoneVipShort", "ТОЛЬКО VIP", "VIP ONLY" },
            new[] { "ZoneVip", "Эта зона только для VIP", "This zone is VIP only" },
            new[] { "ZoneFull", "В этой зоне нет свободных участков", "No free plots in this zone" },
            new[] { "ZoneTaken", "Участок «{0}» ({1:0} м) твой", "Plot \"{0}\" ({1:0} m) is yours" },
            new[] { "ZoneNoZones", "Зоны не настроены: /bsadmin zone и /bsadmin grid", "No zones yet: /bsadmin zone and /bsadmin grid" },
            new[] { "PlotLimitShort", "Максимум участков: {0}", "Plot limit: {0}" },
            new[] { "Binds", "Бинды:\nbind v bs.fly — полёт\nbind g bs.remove — удаление\n/build или /b — меню", "Binds:\nbind v bs.fly — fly\nbind g bs.remove — remove\n/build or /b — menu" },
            new[] { "On", "ВКЛ", "ON" },
            new[] { "Off", "ВЫКЛ", "OFF" },
            new[] { "FlyStart", "ПОЛЁТ", "FLY" },
            new[] { "FlyStop", "ВЫКЛЮЧИТЬ ПОЛЁТ", "STOP FLYING" },
            new[] { "FlyHint", "noclip · бинд: bind v bs.fly", "noclip · bind v bs.fly" },
            new[] { "FlyOn", "Полёт включён · Shift быстрее, Ctrl медленнее", "Flying on · Shift faster, Ctrl slower" },
            new[] { "FlyOff", "Полёт выключен", "Flying off" },
            new[] { "GodOn", "Бессмертие включено", "God mode on" },
            new[] { "GodOff", "Бессмертие выключено", "God mode off" },
            new[] { "KitBtn", "НАБОР СТРОИТЕЛЯ", "BUILDER KIT" },
            new[] { "KitHint", "план, молоток, шкаф, двери, замки, ресурсы", "plan, hammer, TC, doors, locks, resources" },
            new[] { "KitGiven", "Набор строителя выдан", "Builder kit given" },
            new[] { "OptGod", "Бессмертие", "God mode" },
            new[] { "OptGodHint", "без урона, голода и радиации", "no damage, hunger or radiation" },
            new[] { "OptKit", "Набор при спавне", "Kit on spawn" },
            new[] { "OptKitHint", "выдавать после смерти", "give after respawn" },
            new[] { "OptAuth", "Авто-шкаф", "Auto TC auth" },
            new[] { "OptAuthHint", "авторизация в своём шкафу", "authorize in your cupboard" },
            new[] { "OptLock", "Авто-замок на двери", "Auto code lock" },
            new[] { "OptLockHint", "кодовый замок с твоим кодом", "code lock with your code" },
            new[] { "OptMapTp", "Телепорт по метке", "Map marker TP" },
            new[] { "OptMapTpHint", "поставь метку на карте — и ты там", "place a map marker — you're there" },
            new[] { "LockCode", "Код замков", "Lock code" },
            new[] { "LockCodeHint", "4 цифры для авто-замков", "4 digits for auto locks" },
            new[] { "MyTime", "МОЁ ВРЕМЯ СУТОК (ВИДИШЬ ТОЛЬКО ТЫ)", "MY TIME OF DAY (ONLY YOU)" },
            new[] { "ServerTime", "сервер", "server" },
            new[] { "CreativeInfo", "Встроенный креатив Rust: бесплатная стройка, ремонт без кулдауна и электрика без лимитов включаются из конфига. Постройки не гниют. Погоду и сложные съёмки настраивает VideoRecorder, базы копирует Blueprints.", "Rust built-in creative: free building, instant repair and unlimited IO are set in config. No decay. Weather and filming — VideoRecorder, base copies — Blueprints." },

            new[] { "SearchHint", "поиск предмета…", "search item…" },
            new[] { "Stack", "СТАК", "STACK" },
            new[] { "ItemsCount", "предметов: {0} · клик — получить", "items: {0} · click to get" },
            new[] { "CConstruction", "СТРОЙКА", "BUILD" },
            new[] { "CItems", "ДЕПЛОИ", "ITEMS" },
            new[] { "CResources", "РЕСУРСЫ", "RESOURCES" },
            new[] { "CTool", "ИНСТРУМЕНТ", "TOOLS" },
            new[] { "CWeapon", "ОРУЖИЕ", "WEAPONS" },
            new[] { "CAmmunition", "ПАТРОНЫ", "AMMO" },
            new[] { "CAttire", "ОДЕЖДА", "ATTIRE" },
            new[] { "CElectrical", "ЭЛЕКТРИКА", "ELECTRIC" },
            new[] { "CFun", "РАЗВЛЕЧ.", "FUN" },
            new[] { "CMedical", "МЕДИЦИНА", "MEDICAL" },
            new[] { "CFood", "ЕДА", "FOOD" },
            new[] { "CTraps", "ЛОВУШКИ", "TRAPS" },
            new[] { "CComponent", "КОМПОНЕНТЫ", "COMPONENTS" },
            new[] { "CMisc", "РАЗНОЕ", "MISC" },

            new[] { "LookHint", "Наведи прицел на свою постройку — действия применяются ко всей базе сразу.", "Aim at your building — actions apply to the whole base." },
            new[] { "UpgradeTitle", "УЛУЧШИТЬ / ПОНИЗИТЬ ВСЮ БАЗУ", "UPGRADE / DOWNGRADE WHOLE BASE" },
            new[] { "G0", "СОЛОМА", "TWIG" },
            new[] { "G1", "ДЕРЕВО", "WOOD" },
            new[] { "G2", "КАМЕНЬ", "STONE" },
            new[] { "G3", "МЕТАЛЛ", "METAL" },
            new[] { "G4", "МВК", "HQM" },
            new[] { "RepairAll", "ПОЧИНИТЬ ВСЁ", "REPAIR ALL" },
            new[] { "RemoveModeOff", "РЕЖИМ УДАЛЕНИЯ: ВЫКЛ", "REMOVE MODE: OFF" },
            new[] { "RemoveModeOn", "РЕЖИМ УДАЛЕНИЯ: ВКЛ (бей молотком)", "REMOVE MODE: ON (hit with hammer)" },
            new[] { "RemoveAll", "СНЕСТИ ВСЮ ПОСТРОЙКУ", "DEMOLISH WHOLE BUILDING" },
            new[] { "RemoveAllSure", "ТОЧНО СНЕСТИ? НАЖМИ ЕЩЁ РАЗ", "SURE? PRESS AGAIN" },
            new[] { "BuildTips", "Команды:\n/up 4 — вся база в МВК · /up 2 — в камень · /down — в солому\n/remove — режим удаления молотком (или bind g bs.remove)\nДвери сами получают кодовый замок, шкаф сам тебя авторизует.\n\nЧертежи баз (копировать и ставить) — плагин Blueprints: /bp", "Commands:\n/up 4 — whole base to HQM · /up 2 — stone · /down — twig\n/remove — hammer remove mode (or bind g bs.remove)\nDoors get a code lock, TC authorizes you.\n\nBase blueprints — Blueprints plugin: /bp" },
            new[] { "Upgraded", "Изменено блоков: {0} → {1}", "Changed {0} blocks → {1}" },
            new[] { "Repaired", "Починено: {0}", "Repaired: {0}" },
            new[] { "Removed", "Снесено объектов: {0}", "Removed {0} entities" },
            new[] { "RemoveOn", "Режим удаления: бей молотком по объекту", "Remove mode: hit an entity with a hammer" },
            new[] { "RemoveOff", "Режим удаления выключен", "Remove mode off" },
            new[] { "LookAtBuilding", "Наведи прицел на блок постройки.", "Aim at a building block." },
            new[] { "NotYours", "Это не твоя постройка.", "That's not your building." },

            new[] { "RaidTitle", "РЕЙД-ТЕСТ С ОТКАТОМ", "RAID TEST WITH ROLLBACK" },
            new[] { "RaidHint", "Наведись на свою базу и начни тест: база сохранится, ты получишь рейд-набор. Взрывай как хочешь — потом одной кнопкой база вернётся в исходный вид, а ты увидишь, сколько блоков пробил.", "Aim at your base and start: it's saved and you get a raid kit. Blow it up — then one button restores the base and shows how many blocks you broke." },
            new[] { "RaidStart", "НАЧАТЬ РЕЙД-ТЕСТ (НАВЕДИСЬ НА БАЗУ)", "START RAID TEST (AIM AT BASE)" },
            new[] { "RaidProgress", "Разрушено {0} из {1} объектов", "Destroyed {0} of {1} entities" },
            new[] { "RaidRestore", "ОТКАТИТЬ БАЗУ", "RESTORE BASE" },
            new[] { "RaidMoreKit", "ЕЩЁ ВЗРЫВЧАТКИ", "MORE EXPLOSIVES" },
            new[] { "RaidKitTitle", "РЕЙД-НАБОР", "RAID KIT" },
            new[] { "RaidStarted", "Рейд-тест начат: база сохранена ({0} объектов), рейд-набор выдан. Откат — /raidtest restore", "Raid test started: base saved ({0} entities). Restore — /raidtest restore" },
            new[] { "RaidRestored", "База откатывается. Было разрушено {0} из {1}.", "Restoring base. Destroyed {0} of {1}." },
            new[] { "NoRaidTest", "Нет активного рейд-теста.", "No active raid test." },
            new[] { "NoCopyPaste", "Для рейд-теста нужен плагин CopyPaste.", "Raid test needs the CopyPaste plugin." },
            new[] { "CopyError", "Не удалось сохранить базу: {0}", "Couldn't save the base: {0}" },
            new[] { "PasteError", "Не удалось откатить базу: {0}", "Couldn't restore the base: {0}" },

            new[] { "PlotN", "Участок {0}", "Plot {0}" },
            new[] { "PlotFree", "Здесь свободно", "This area is free" },
            new[] { "PlotFreeHint", "Займи участок радиусом {0} м: чужие не смогут тут строить и ломать.", "Claim a {0} m plot: others can't build or break here." },
            new[] { "PlotClaim", "ЗАНЯТЬ УЧАСТОК", "CLAIM PLOT" },
            new[] { "PlotInfo", "радиус {0} м · участников {1}", "radius {0} m · members {1}" },
            new[] { "PlotShow", "ПОКАЗАТЬ ГРАНИЦУ", "SHOW BORDER" },
            new[] { "PlotUnclaim", "ОСВОБОДИТЬ", "UNCLAIM" },
            new[] { "NoPlots", "Участков пока нет — займи свободное место сверху", "No plots yet — claim a free spot above" },
            new[] { "NoHomes", "Точек пока нет — впиши название и нажми «+ Точка здесь»", "No homes yet — type a name and press \"+ Home here\"" },
            new[] { "PlotMembers", "УЧАСТНИКИ (могут строить и ломать)", "MEMBERS (can build and break)" },
            new[] { "PlotAddTitle", "ДОБАВИТЬ ИГРОКА", "ADD PLAYER" },
            new[] { "MyPlots", "МОИ УЧАСТКИ ({0}/{1})", "MY PLOTS ({0}/{1})" },
            new[] { "PlotLimit", "Лимит участков: {0}.", "Plot limit: {0}." },
            new[] { "PlotClash", "Слишком близко к участку {0}.", "Too close to {0}'s plot." },
            new[] { "PlotClaimed", "Участок «{0}» занят, радиус {1} м.", "Plot \"{0}\" claimed, radius {1} m." },
            new[] { "PlotRemoved", "Участок «{0}» освобождён.", "Plot \"{0}\" released." },
            new[] { "NotInOwnPlot", "Встань на свой участок.", "Stand on your plot." },
            new[] { "MemberAdded", "{0} добавлен в «{1}».", "{0} added to \"{1}\"." },
            new[] { "MemberRemoved", "{0} убран из «{1}».", "{0} removed from \"{1}\"." },
            new[] { "PlotNoBuild", "Здесь участок {0} — строить нельзя", "This is {0}'s plot — no building" },
            new[] { "PlotEnter", "Участок «{0}» · {1}", "Plot \"{0}\" · {1}" },
            new[] { "PlotEnterOwn", "Твой участок «{0}»", "Your plot \"{0}\"" },
            new[] { "PlotLeave", "Свободная зона", "Free area" },
            new[] { "PlotHelp", "/plot claim · /plot unclaim · /plot add <ник> · /plot remove <ник>", "/plot claim · /plot unclaim · /plot add <name> · /plot remove <name>" },
            new[] { "PlayerNotFound", "Игрок не найден.", "Player not found." },

            new[] { "Homes", "ДОМА ({0}/{1})", "HOMES ({0}/{1})" },
            new[] { "HomeHint", "название точки: база, шахта, лес…", "point name: base, mine, forest…" },
            new[] { "HomeSetBtn", "+ ТОЧКА ЗДЕСЬ", "+ POINT HERE" },
            new[] { "Go", "ТП", "TP" },
            new[] { "BackBtn", "НАЗАД (ГДЕ БЫЛ)", "BACK (LAST SPOT)" },
            new[] { "MapTpInfo", "Метка на карте = телепорт туда", "Map marker = teleport there" },
            new[] { "TprTitle", "ЗАПРОС ТЕЛЕПОРТА К ИГРОКУ", "TELEPORT REQUEST TO PLAYER" },
            new[] { "NoOneOnline", "Больше никого нет онлайн.", "No one else is online." },
            new[] { "HomeLimit", "Лимит точек: {0}.", "Home limit: {0}." },
            new[] { "HomeSet", "Точка «{0}» сохранена.", "Home \"{0}\" saved." },
            new[] { "NoHome", "Нет такой точки. /sethome <имя>", "No such home. /sethome <name>" },
            new[] { "TpHome", "Телепорт: {0}", "Teleported: {0}" },
            new[] { "TpMarker", "Телепорт по метке", "Teleported to marker" },
            new[] { "TprSent", "Запрос отправлен: {0}", "Request sent: {0}" },
            new[] { "TprPopup", "{0} хочет телепортироваться к тебе", "{0} wants to teleport to you" },
            new[] { "TprPopupHint", "Запрос действует {0} с · /tpa — принять", "Expires in {0}s · /tpa to accept" },
            new[] { "Accept", "ПРИНЯТЬ", "ACCEPT" },
            new[] { "Decline", "ОТКЛОНИТЬ", "DECLINE" },
            new[] { "TprDone", "Ты у {0}", "You're at {0}" },
            new[] { "NoTpr", "Нет запросов телепорта.", "No teleport requests." },
            new[] { "NoBack", "Некуда возвращаться.", "Nowhere to go back to." },
        };

        #endregion
    }
}
