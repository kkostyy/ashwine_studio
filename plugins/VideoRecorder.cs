// =====================================================================================
//  VideoRecorder 3.4 — студия постановок для Rust (Oxide / Carbon)
// =====================================================================================
//  Записываешь свои движения -> боты повторяют их: ходьба, бег, присед, прицел,
//  повороты головы, оружие, фонарик/лазер, НАСТОЯЩИЕ выстрелы с трассерами,
//  перезарядка, удары, гранаты, жесты, ранения, ГОЛОС, ТРАНСПОРТ (коптеры,
//  машины, лошади, лодки). Плюс кинокамера, эффекты, маркеры, монтаж дублей.
//
//  Установка: файл в oxide/plugins (или carbon/plugins)
//  Права:     videorecorder.use    — пользоваться плагином
//             videorecorder.paths  — траектории и подписи ботов (через ddraw)
//             videorecorder.world  — погода для всего сервера
//  Чат:       /vr — панель,  /vr help — все команды
//  Бинды:     bind f5 vr.cam   — кинокамера вкл/выкл
//             bind f6 vr.ui    — панель
//             bind f7 vr.play  — запустить/остановить сцену
//             bind f8 vr.rec   — запись
//             bind f9 vr.pause — пауза
//             bind f10 vr.slowmo — слоу-мо
// =====================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("VideoRecorder", "Claude", "3.6.0")]
    [Description("Студия постановок: запись движений, повтор ботами, кинокамера, эффекты и монтаж")]
    public class VideoRecorder : RustPlugin
    {
        #region Константы и конфиг

        private const string PermUse = "videorecorder.use";
        private const string PermPaths = "videorecorder.paths";
        private const string PermWorld = "videorecorder.world";
        private const string PermKits = "videorecorder.kits";
        private const string PlayerPrefab = "assets/prefabs/player/player.prefab";
        private const string ChairPrefab = "assets/bundled/prefabs/static/chair.invisible.static.prefab";

        private const string UiMain = "VR.Main";
        private const string UiPlayhead = "VR.Playhead";
        private const string UiCountdown = "VR.Countdown";
        private const string UiHud = "VR.Hud";
        private const string UiInvite = "VR.Invite";

        private const string FxBeep = "assets/prefabs/locks/keypad/effects/lock.code.updated.prefab";
        private const string FxGo = "assets/prefabs/locks/keypad/effects/lock.code.unlock.prefab";

        private const int EvFire = 1, EvReload = 2, EvMelee = 3, EvThrow = 4, EvGesture = 5;
        private const int BitLight = 1, BitWounded = 2;

        private static VideoRecorder _ins;
        private Configuration _config;

        private class Configuration
        {
            [JsonProperty("Кадров в секунду при записи")] public float Fps = 20f;
            [JsonProperty("Максимальная длина дубля (сек)")] public float MaxSeconds = 600f;
            [JsonProperty("Отсчёт перед записью (сек)")] public int Countdown = 3;
            [JsonProperty("Звук отсчёта")] public bool CountdownSound = true;
            [JsonProperty("Максимум дублей в сцене")] public int MaxTakes = 40;
            [JsonProperty("Максимум сцен у игрока")] public int MaxScenes = 30;
            [JsonProperty("Шагов отмены (Ctrl+Z)")] public int UndoSteps = 15;
            [JsonProperty("Частота сетевых обновлений ботов (раз в сек)")] public float NetworkRate = 30f;
            [JsonProperty("Не слать обновления ботов дальше чем (м), 0 = всегда")] public float NetworkCullDistance = 0f;
            [JsonProperty("Записывать голос")] public bool RecordVoice = true;
            [JsonProperty("Записывать транспорт")] public bool RecordVehicles = true;
            [JsonProperty("Блокировать урон ботам от окружения")] public bool BlockEnvironmentDamage = true;
            [JsonProperty("ИИ Rust заморожен (/ai): учёные и животные стоят и не реагируют")] public bool AiFrozen = false;
            [JsonProperty("Запретить лутать трупы ботов")] public bool BlockBotLoot = true;
            [JsonProperty("Боты носят весь инвентарь актёра (патроны, хилки, стенки — лут с трупа)")] public bool BotCarryInventory = true;
            [JsonProperty("Убирать трупы ботов при остановке сцены")] public bool CleanupCorpses = true;
            [JsonProperty("Длительность показа траекторий (сек)")] public float PathDuration = 20f;
            [JsonProperty("Камера: высота глаз над креслом (м)")] public float CameraEyeOffset = 0.9f;
            [JsonProperty("Камера: частота принудительного поворота взгляда (раз в сек)")] public float CameraLookRate = 30f;
            [JsonProperty("Разрешить менять погоду на всём сервере")] public bool AllowGlobalWeather = false;
            [JsonProperty("Время жизни огня/дыма из эффектов (сек)")] public float FxEntityLifetime = 25f;
            [JsonProperty("Скруглённые углы интерфейса")] public bool RoundedCorners = true;
            [JsonProperty("Максимум наборов инвентаря у игрока")] public int MaxKits = 30;
            [JsonProperty("Разрешить ботам настоящий SteamID игрока (точная копия)")] public bool AllowRealSteamId = true;
            [JsonProperty("Примерка внешности: сколько секунд стоит манекен")] public float PreviewSeconds = 25f;
            [JsonProperty("Мотор для всех: отсчёт (сек)")] public int GroupCountdown = 5;
            [JsonProperty("Мотор для всех: дубли выглядят как их автор")] public bool GroupTakesLookLikeAuthor = true;
            [JsonProperty("Приглашение действует (сек)")] public float InviteSeconds = 60f;
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
            }
            catch
            {
                PrintWarning("Config error — creating new / Конфиг повреждён — создаю новый");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        #endregion

        #region Данные

        private class ItemInfo
        {
            public string s;
            public ulong k;
            public List<string> m;
            [JsonIgnore] public string Key => s + "|" + k + "|" + (m == null ? "" : string.Join(",", m.ToArray()));
        }

        // Вещь из пояса/инвентаря актёра на момент записи: кладётся боту перед смертью, чтобы её можно было залутать
        private class LootInfo
        {
            public string s;
            public ulong k;
            public int n = 1;
            public float c = 1f, mc = 1f;   // состояние и максимум после ремонтов (доли)
            public int b, p;                 // контейнер (0 пояс, 1 инвентарь, 2 одежда) и слот
            public int a = -1;               // патроны в магазине на старте дубля
            public string at;                // тип патронов
            public List<string> m;
        }

        // Что актёр поставил во время записи (стенки, баррикады, блоки): при просмотре бот «ставит» это в тот же момент
        private class BuildInfo
        {
            public float t;
            public string pf;
            public float x, y, z, rx, ry, rz, rw = 1f;
            public int gr = -1;
            public ulong k;
        }

        private class VehicleInfo
        {
            public string p;        // префаб
            public int s = -1;      // место
        }

        [JsonConverter(typeof(FrameConverter))]
        private class Frame
        {
            public float t, x, y, z, p, w;
            public int f, i = -1, h;
            public int v = -1;                          // транспорт (индекс в Vehicles)
            public float vx, vy, vz, qx, qy, qz, qw = 1f;
            public int vf;

            public Frame Clone() => (Frame)MemberwiseClone();
        }

        // Компактное хранение: кадр = массив чисел (в 4-6 раз меньше файл)
        private class FrameConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) => objectType == typeof(Frame);

            private static double R(float v) => Math.Round(v, 3);

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var f = (Frame)value;
                writer.WriteStartArray();
                writer.WriteValue(R(f.t)); writer.WriteValue(R(f.x)); writer.WriteValue(R(f.y)); writer.WriteValue(R(f.z));
                writer.WriteValue(Math.Round(f.p, 1)); writer.WriteValue(Math.Round(f.w, 1));
                writer.WriteValue(f.f); writer.WriteValue(f.i); writer.WriteValue(f.h);
                if (f.v >= 0)
                {
                    writer.WriteValue(f.v);
                    writer.WriteValue(R(f.vx)); writer.WriteValue(R(f.vy)); writer.WriteValue(R(f.vz));
                    writer.WriteValue(R(f.qx)); writer.WriteValue(R(f.qy)); writer.WriteValue(R(f.qz)); writer.WriteValue(R(f.qw));
                    writer.WriteValue(f.vf);
                }
                writer.WriteEndArray();
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var token = JToken.Load(reader);
                var f = new Frame();
                var arr = token as JArray;
                if (arr != null)
                {
                    f.t = arr[0].Value<float>(); f.x = arr[1].Value<float>(); f.y = arr[2].Value<float>(); f.z = arr[3].Value<float>();
                    f.p = arr[4].Value<float>(); f.w = arr[5].Value<float>();
                    if (arr.Count > 6) f.f = arr[6].Value<int>();
                    if (arr.Count > 7) f.i = arr[7].Value<int>();
                    if (arr.Count > 8) f.h = arr[8].Value<int>();
                    if (arr.Count > 17)
                    {
                        f.v = arr[9].Value<int>();
                        f.vx = arr[10].Value<float>(); f.vy = arr[11].Value<float>(); f.vz = arr[12].Value<float>();
                        f.qx = arr[13].Value<float>(); f.qy = arr[14].Value<float>(); f.qz = arr[15].Value<float>(); f.qw = arr[16].Value<float>();
                        f.vf = arr[17].Value<int>();
                    }
                    return f;
                }
                var obj = token as JObject;   // формат версий 1-2
                if (obj != null)
                {
                    f.t = (float?)obj["t"] ?? 0f; f.x = (float?)obj["x"] ?? 0f; f.y = (float?)obj["y"] ?? 0f; f.z = (float?)obj["z"] ?? 0f;
                    f.p = (float?)obj["p"] ?? 0f; f.w = (float?)obj["w"] ?? 0f;
                    f.f = (int?)obj["f"] ?? 0; f.i = (int?)obj["i"] ?? -1; f.h = (int?)obj["h"] ?? 0;
                }
                return f;
            }
        }

        private class TakeEvent
        {
            public float t;
            public int e;
            public float x, y, z;
            public uint g;                               // жест
        }

        private class VoiceChunk
        {
            public float t;
            public byte[] d;
        }

        private class Take
        {
            public string Name;
            public bool Enabled = true;
            public float Duration;
            public float Delay;
            public float Speed = 1f;
            public bool Loop;
            public bool Damage = true;
            public bool God;
            public bool PlayVoice = true;
            public float Health = 100f;
            public float DieAt = -1f;                    // смерть по сценарию (сек дубля)
            public int LookMode;                         // 0 своё лицо дубля, 1 как у автора записи, 2 как у выбранного игрока
            public ulong LookSeed;                       // «лицо» для режима 0
            public ulong LookSteamId;                    // игрок для режима 2
            public string LookName;                      // ник выбранного игрока (для подписи)
            public ulong RecorderId;                     // кто записал дубль
            public bool LookRealId;                      // точная копия: настоящий SteamID
            public List<ItemInfo> Wear = new List<ItemInfo>();
            public List<ItemInfo> Items = new List<ItemInfo>();
            public List<VehicleInfo> Vehicles = new List<VehicleInfo>();
            public List<Frame> Frames = new List<Frame>();
            public List<TakeEvent> Events = new List<TakeEvent>();
            public List<VoiceChunk> Voice = new List<VoiceChunk>();
            public List<LootInfo> Loot = new List<LootInfo>();
            public List<BuildInfo> Builds = new List<BuildInfo>();

            [JsonIgnore] public float SceneLength => Delay + Duration / Mathf.Max(0.05f, Speed);
        }

        private class KitItem
        {
            public string s;            // shortname
            public ulong k;             // скин
            public int a = 1;           // количество
            public int c;               // 0 рюкзак, 1 пояс, 2 одежда
            public int p = -1;          // слот
            public float h = -1f;       // прочность
            public List<string> m;      // модули
            public int am = -1;         // патроны в магазине
            public string at;           // тип патронов
            public string n;            // своё имя предмета
        }

        private class Kit
        {
            public string Name;
            public DateTime Created;
            public List<KitItem> Items = new List<KitItem>();
        }

        private class Marker
        {
            public float t;
            public string n;
        }

        private class FxCue
        {
            public float t;
            public int k;
            public float x, y, z;
        }

        private class CamKey
        {
            public float t;
            public float x, y, z, p, w;
            public int e;                                // кривая скорости до следующей точки: 0 авто, 1 линейно, 2 разгон, 3 торможение, 4 разгон+торможение
        }

        private class CameraSettings
        {
            public int Mode;                             // 0 путь, 1 глаза бота, 2 за спиной, 3 орбита, 4 статичная слежка
            public int Target;
            public float Dist = 4f;
            public float Height = 1.6f;
            public float OrbitSpeed = 20f;
            public bool FreeLook;
            public float Smooth = 0.7f;                  // 0 — линейно, 1 — максимально плавный сплайн
            public int Focus = -1;                       // путь: камера всё время смотрит на этого бота (-1 — нет)
            public List<CamKey> Keys = new List<CamKey>();
        }

        private class WeatherSettings
        {
            public bool Enabled;
            public string Preset;
            // проценты 0..100 (яркость/контраст 0..200), -1 = как на сервере
            public int Clouds = -1, Fog = -1, Rain = -1, Wind = -1, Thunder = -1, Rainbow = -1, Brightness = -1, Contrast = -1;
            public bool ServerTime;        // время для всего сервера (иначе только у команды сцены)
            public bool FreezeTime = true; // остановить смену суток на сервере
        }

        private class Scene
        {
            public string Name;
            public List<Take> Takes = new List<Take>();
            public List<Marker> Markers = new List<Marker>();
            public List<FxCue> Cues = new List<FxCue>();
            public CameraSettings Camera = new CameraSettings();
            public float TimeOfDay = -1f;
            public WeatherSettings Weather = new WeatherSettings();

            [JsonIgnore] public float Length
            {
                get
                {
                    float l = 0f;
                    foreach (var t in Takes) if (t.Enabled) l = Mathf.Max(l, t.SceneLength);
                    foreach (var c in Cues) l = Mathf.Max(l, c.t);
                    foreach (var k in Camera.Keys) l = Mathf.Max(l, k.t);
                    return l;
                }
            }
        }

        private class PlayerData
        {
            public int Current;
            public string NextName = "";
            public bool CleanFrame;
            public bool GodWhileRecording = true;
            public bool InfiniteAmmo = true;
            public bool Nametags;
            public bool RecordVoice = true;
            public float Seek;
            public float GlobalSpeed = 1f;
            public float SlowmoSpeed = 0.3f;
            public List<Scene> Scenes = new List<Scene>();
            public List<Kit> Kits = new List<Kit>();

            [JsonIgnore] public Scene Scene
            {
                get
                {
                    if (Scenes.Count == 0) Scenes.Add(new Scene { Name = "Scene 1" });
                    Current = Mathf.Clamp(Current, 0, Scenes.Count - 1);
                    return Scenes[Current];
                }
            }
        }

        private readonly Dictionary<ulong, PlayerData> _data = new Dictionary<ulong, PlayerData>();

        private PlayerData GetData(ulong id)
        {
            PlayerData d;
            if (_data.TryGetValue(id, out d)) return d;
            try { d = Interface.Oxide.DataFileSystem.ReadObject<PlayerData>("VideoRecorder/" + id); }
            catch (Exception ex) { PrintWarning("Data read error " + id + ": " + ex.Message); d = null; }
            if (d == null) d = new PlayerData();
            if (d.Scenes == null) d.Scenes = new List<Scene>();
            if (d.Kits == null) d.Kits = new List<Kit>();
            foreach (var s in d.Scenes) Normalize(s);
            _data[id] = d;
            return d;
        }

        private static void Normalize(Scene s)
        {
            if (s.Takes == null) s.Takes = new List<Take>();
            if (s.Markers == null) s.Markers = new List<Marker>();
            if (s.Cues == null) s.Cues = new List<FxCue>();
            if (s.Camera == null) s.Camera = new CameraSettings();
            if (s.Camera.Keys == null) s.Camera.Keys = new List<CamKey>();
            if (s.Weather == null) s.Weather = new WeatherSettings();
            foreach (var t in s.Takes)
            {
                if (t.Speed <= 0f) t.Speed = 1f;
                if (t.Vehicles == null) t.Vehicles = new List<VehicleInfo>();
                if (t.Voice == null) t.Voice = new List<VoiceChunk>();
                if (t.Events == null) t.Events = new List<TakeEvent>();
                if (t.Items == null) t.Items = new List<ItemInfo>();
                if (t.Wear == null) t.Wear = new List<ItemInfo>();
                if (t.Loot == null) t.Loot = new List<LootInfo>();
                if (t.Builds == null) t.Builds = new List<BuildInfo>();
                if (t.LookSeed == 0) t.LookSeed = NewLookSeed();
            }
        }

        private readonly HashSet<ulong> _dirty = new HashSet<ulong>();

        private void MarkDirty(ulong id) => _dirty.Add(id);

        private void SaveData(ulong id)
        {
            PlayerData d;
            if (_data.TryGetValue(id, out d))
                Interface.Oxide.DataFileSystem.WriteObject("VideoRecorder/" + id, d);
            _dirty.Remove(id);
        }

        private void FlushDirty()
        {
            foreach (var id in _dirty.ToList()) SaveData(id);
        }

        #endregion

        #region Состояние

        private class Recorder
        {
            public BasePlayer Player;
            public List<BaseEntity> Built = new List<BaseEntity>();   // настоящие постройки актёра за запись — убираются после неё
            public ulong Owner;
            public Take Take;
            public float Start;
            public int ReplaceIndex = -1;
            public int AppendIndex = -1;
            public string LastKey;
            public int LastItem = -1;
            public int LastShownSecond = -1;
            public BaseEntity LastRoot;
            public int LastVehicle = -1;
            public uint LastGesture;
            public GroupRec Group;
        }

        private class GroupRec
        {
            public ulong Owner;
            public List<ulong> Ids = new List<ulong>();
            public bool Stopping;
        }

        private class Member
        {
            public ulong Id;
            public bool Actor = true;
            public bool Ready = true;
        }

        private class Invite
        {
            public ulong Owner;
            public float At;
            public bool Actor = true;
        }

        private class Playback
        {
            public float BaseTime;
            public float ResumeAt;
            public bool Paused;
            public float Speed = 1f;
            public bool Slowmo;
            public float EndTime;
            public int CueIndex;
            public float LastCueTime = -1f;

            public float Time => BaseTime + (Paused ? 0f : (UnityEngine.Time.realtimeSinceStartup - ResumeAt) * Speed);

            public void Pause()
            {
                if (Paused) return;
                BaseTime = Time;
                Paused = true;
            }

            public void Resume()
            {
                if (!Paused) return;
                ResumeAt = UnityEngine.Time.realtimeSinceStartup;
                Paused = false;
            }

            public void SetSpeed(float s)
            {
                BaseTime = Time;
                ResumeAt = UnityEngine.Time.realtimeSinceStartup;
                Speed = s;
            }
        }

        private class BotInfo
        {
            public ulong Owner;
            public Take Take;
        }

        private class UiState
        {
            public string Tab = "takes";
            public int Page;
            public int Selected = -1;
            public int FxType;
            public float ConfirmDeleteScene;
            public string MarkerName = "";
            public string ImportCode = "";
            public string KitName = "";
            public int KitPage;
            public int ConfirmKit = -1;
            public bool LookView;
            public bool PreviewNaked;
            public string LookSteamInput = "";
            public float ConfirmKitAt;
            public bool SceneMenu;
            public bool SceneActions;
            public int ConfirmTake = -1;
            public float ConfirmTakeAt;
            public int FxPage;
            public string LastCode;
            public float CamPathUntil;
        }

        private readonly Dictionary<ulong, Recorder> _recorders = new Dictionary<ulong, Recorder>();
        private readonly Dictionary<ulong, Timer> _countdowns = new Dictionary<ulong, Timer>();
        private readonly Dictionary<ulong, Playback> _playbacks = new Dictionary<ulong, Playback>();
        private readonly Dictionary<ulong, List<BasePlayer>> _bots = new Dictionary<ulong, List<BasePlayer>>();
        private readonly Dictionary<ulong, List<BaseEntity>> _props = new Dictionary<ulong, List<BaseEntity>>();
        private readonly Dictionary<BasePlayer, BotInfo> _botInfo = new Dictionary<BasePlayer, BotInfo>();
        private readonly HashSet<BaseEntity> _propSet = new HashSet<BaseEntity>();
        private readonly HashSet<ulong> _botCorpseIds = new HashSet<ulong>();
        private readonly Dictionary<ulong, ulong> _botIdOwner = new Dictionary<ulong, ulong>();   // ID бота → чья сцена: трупы и выпавшие вещи убираются вместе со сценой
        private readonly Dictionary<ulong, List<string>> _botDropped = new Dictionary<ulong, List<string>>();   // что уже вылетело из рук бота (не класть в труп второй раз)
        private readonly Dictionary<ulong, ulong> _links = new Dictionary<ulong, ulong>();
        private readonly Dictionary<ulong, Invite> _invites = new Dictionary<ulong, Invite>();
        private readonly Dictionary<ulong, List<Member>> _teams = new Dictionary<ulong, List<Member>>();
        private readonly Dictionary<ulong, Timer> _groupCountdowns = new Dictionary<ulong, Timer>();
        private readonly Dictionary<ulong, UiState> _ui = new Dictionary<ulong, UiState>();
        private readonly HashSet<ulong> _uiOpen = new HashSet<ulong>();
        private readonly HashSet<ulong> _hudShown = new HashSet<ulong>();
        private readonly Dictionary<ulong, List<string>> _undo = new Dictionary<ulong, List<string>>();
        private readonly Dictionary<ulong, CameraDriver> _cameras = new Dictionary<ulong, CameraDriver>();
        private Timer _recordTimer, _hudTimer, _fxTimer, _saveTimer;

        private static ulong Id(BasePlayer p) => (ulong)p.userID;

        private ulong OwnerOf(ulong id)
        {
            ulong o;
            return _links.TryGetValue(id, out o) ? o : id;
        }

        private UiState Ui(ulong id)
        {
            UiState s;
            if (!_ui.TryGetValue(id, out s)) _ui[id] = s = new UiState();
            return s;
        }

        private Playback GetPlayback(ulong owner)
        {
            Playback pb;
            return _playbacks.TryGetValue(owner, out pb) ? pb : null;
        }

        private float CurrentTime(ulong owner)
        {
            var pb = GetPlayback(owner);
            return pb != null ? pb.Time : GetData(owner).Seek;
        }

        #endregion

        #region Рефлексия (для API, которое меняется между версиями Rust)

        private static readonly Dictionary<string, MethodInfo> _methodCache = new Dictionary<string, MethodInfo>();

        private static object Call(object target, string method, params object[] args)
        {
            if (target == null) return null;
            var type = target.GetType();
            var key = type.FullName + "." + method + "/" + args.Length;
            MethodInfo mi;
            if (!_methodCache.TryGetValue(key, out mi))
            {
                mi = null;
                foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (m.Name != method) continue;
                    var ps = m.GetParameters();
                    if (ps.Length < args.Length) continue;
                    bool ok = true;
                    for (int i = 0; i < ps.Length && ok; i++)
                    {
                        if (i < args.Length)
                        {
                            if (args[i] != null && !ps[i].ParameterType.IsInstanceOfType(args[i])) ok = false;
                        }
                        else if (!ps[i].IsOptional) ok = false;
                    }
                    if (!ok) continue;
                    mi = m;
                    break;
                }
                _methodCache[key] = mi;
            }
            if (mi == null) throw new MissingMethodException(type.Name, method);

            var pars = mi.GetParameters();
            var full = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
                full[i] = i < args.Length ? args[i] : (pars[i].DefaultValue == DBNull.Value ? null : pars[i].DefaultValue);
            return mi.Invoke(target, full);
        }

        private static bool TryCall(object target, string method, params object[] args)
        {
            try
            {
                Call(target, method, args);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object GetMember(object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var f = type.GetField(name, flags);
            if (f != null) return f.GetValue(target);
            var p = type.GetProperty(name, flags);
            if (p != null) return p.GetValue(target, null);
            return null;
        }

        private static void ForceViewAngles(BasePlayer player, Vector3 angles)
        {
            if (!TryCall(player, "OverrideViewAngles", angles))
                player.viewAngles = angles;
        }

        private static BaseEntity MountRoot(BaseMountable mount)
        {
            if (mount == null) return null;
            BaseEntity veh = null;
            try { veh = Call(mount, "VehicleParent") as BaseEntity; } catch { }
            return veh ?? mount;
        }

        private static List<BaseMountable> Seats(BaseEntity root)
        {
            var result = new List<BaseMountable>();
            var points = GetMember(root, "mountPoints") as System.Collections.IEnumerable;
            if (points != null)
                foreach (var p in points)
                    result.Add(GetMember(p, "mountable") as BaseMountable);
            if (result.Count == 0 && root is BaseMountable) result.Add((BaseMountable)root);
            return result;
        }

        #endregion

        #region Хуки Oxide

        private void Init()
        {
            _ins = this;
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermPaths, this);
            permission.RegisterPermission(PermWorld, this);
            permission.RegisterPermission(PermKits, this);
        }

        private void OnServerInitialized()
        {
            RegisterLang();
            if (_config.AiFrozen) ApplyAi();
            InitRoundedSprite();
            _recordTimer = timer.Every(1f / Mathf.Clamp(_config.Fps, 5f, 60f), TickRecorders);
            _hudTimer = timer.Every(0.5f, TickHud);
            _fxTimer = timer.Every(0.05f, TickScene);
            _saveTimer = timer.Every(30f, FlushDirty);
        }

        private void Unload()
        {
            foreach (var cam in _cameras.Values.ToList()) StopCamera(cam.Viewer, false);
            foreach (var pv in _previews.Values.ToList()) KillEntity(pv);
            foreach (var rec in _recorders.Values.ToList()) StopRecording(rec.Player, false);
            foreach (var owner in _bots.Keys.ToList()) ClearBots(owner);
            foreach (var bot in _botInfo.Keys.ToList()) KillEntity(bot);
            foreach (var p in _propSet.ToList()) KillEntity(p);
            foreach (var p in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(p, UiMain);
                CuiHelper.DestroyUi(p, UiCountdown);
                CuiHelper.DestroyUi(p, UiHud); CuiHelper.DestroyUi(p, UiPbHud);
            }
            if (_weatherTouched)
            {
                foreach (var cv in new[] { "weather.clouds", "weather.fog", "weather.rain", "weather.wind", "weather.thunder", "weather.rainbow", "weather.atmosphere_brightness", "weather.atmosphere_contrast" })
                    ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), cv, "-1");
                ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "env.progresstime", "true");
            }
            foreach (var id in _data.Keys.ToList()) SaveData(id);
            _ins = null;
        }

        private void OnServerSave() => FlushDirty();

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            var id = Id(player);
            StopCamera(player, false);
            CancelCountdown(id);
            if (_recorders.ContainsKey(id)) StopRecording(player, true, true);
            _uiOpen.Remove(id);
            _hudShown.Remove(id);
            LeaveTeam(id, false);
            _invites.Remove(id);
            CancelGroupCountdown(id);
            foreach (var guest in _links.Where(x => x.Value == id).Select(x => x.Key).ToList())
            {
                _links.Remove(guest);
                var g = BasePlayer.FindByID(guest);
                if (g != null) { Reply(g, "HostLeft"); Refresh(g); }
            }
            _teams.Remove(id);
            StopPlayback(id);
            SaveData(id);
        }

        private void OnWeaponFired(BaseProjectile projectile, BasePlayer player, ItemModProjectile mod, ProtoBuf.ProjectileShoot projectiles)
        {
            Recorder rec;
            if (player == null || !_recorders.TryGetValue(Id(player), out rec)) return;
            AddEvent(rec, EvFire, Vector3.zero, 0);
            if (GetData(rec.Owner).InfiniteAmmo && projectile != null && projectile.primaryMagazine != null)
            {
                projectile.primaryMagazine.contents = projectile.primaryMagazine.capacity;
                projectile.SendNetworkUpdateImmediate();
            }
        }

        private object OnWeaponReload(BaseProjectile projectile, BasePlayer player)   // хук переименован в Rust 2.0.79xx (был OnReloadWeapon)
        {
            Recorder rec;
            if (player != null && _recorders.TryGetValue(Id(player), out rec)) AddEvent(rec, EvReload, Vector3.zero, 0);
            return null;
        }

        private object OnMeleeAttack(BasePlayer player, HitInfo info)
        {
            Recorder rec;
            if (player != null && _recorders.TryGetValue(Id(player), out rec)) AddEvent(rec, EvMelee, Vector3.zero, 0);
            return null;
        }

        private void OnExplosiveThrown(BasePlayer player, BaseEntity entity, ThrownWeapon item)
        {
            Recorder rec;
            if (player == null || !_recorders.TryGetValue(Id(player), out rec)) return;
            var target = player.eyes.position + player.eyes.HeadForward() * 20f;
            RaycastHit hit;
            if (Physics.Raycast(player.eyes.position, player.eyes.HeadForward(), out hit, 40f, -5, QueryTriggerInteraction.Ignore))
                target = hit.point;
            AddEvent(rec, EvThrow, target, 0);
        }

        private object OnPlayerVoice(BasePlayer player, ArraySegment<byte> seg)   // Rust теперь отдаёт ArraySegment из общего буфера — копируем
        {
            if (player == null || seg.Array == null || seg.Count == 0 || !_config.RecordVoice) return null;
            var data = new byte[seg.Count];
            Buffer.BlockCopy(seg.Array, seg.Offset, data, 0, seg.Count);
            Recorder rec;
            if (!_recorders.TryGetValue(Id(player), out rec) || !GetData(Id(player)).RecordVoice) return null;
            rec.Take.Voice.Add(new VoiceChunk { t = Time.realtimeSinceStartup - rec.Start, d = data });
            return null;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null) return null;
            if (_propSet.Contains(entity)) return true;

            var victim = entity as BasePlayer;
            if (victim != null)
            {
                BotInfo bi;
                if (_botInfo.TryGetValue(victim, out bi))
                {
                    if (bi.Take.God) return true;
                    if (_config.BlockEnvironmentDamage && (info == null || info.Initiator == null)) return true;
                }
                else
                {
                    var vid = Id(victim);
                    Recorder rec;
                    if (_recorders.TryGetValue(vid, out rec) && GetData(rec.Owner).GodWhileRecording) return true;
                    if (_cameras.ContainsKey(vid)) return true;
                }
            }

            var shooter = info?.Initiator as BasePlayer;
            if (shooter != null)
            {
                BotInfo si;
                if (_botInfo.TryGetValue(shooter, out si) && !si.Take.Damage) return true;
            }
            return null;
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            var bot = entity as BasePlayer;
            if (bot == null || !_botInfo.ContainsKey(bot)) return;
            if (_config.BlockBotLoot)
            {
                bot.inventory.containerBelt.Clear();
                bot.inventory.containerMain.Clear();
                ItemManager.DoRemoves();
            }
            else GiveLoot(bot);
        }

        private void OnPlayerCorpseSpawned(BasePlayer player, PlayerCorpse corpse)
        {
            BotInfo bi;
            if (player == null || corpse == null) return;
            ulong owner;
            if (_botInfo.TryGetValue(player, out bi)) owner = bi.Owner;
            else if (!_botIdOwner.TryGetValue((ulong)player.userID, out owner)) return;   // бот уже снят с учёта — узнаём сцену по ID
            AddProp(owner, corpse);
            if (corpse.net != null) _botCorpseIds.Add(corpse.net.ID.Value);
        }

        // Оружие и вещи, выпавшие у бота при смерти: поднять можно, но при новом просмотре/записи они убираются вместе со сценой
        private void OnItemDropped(Item item, BaseEntity entity)
        {
            var di = entity as DroppedItem;
            ulong owner;
            if (di == null || !_botIdOwner.TryGetValue(di.DroppedBy, out owner)) return;
            AddProp(owner, di);

            // вылетело из рук живого/умирающего бота: прочность и патроны как у актёра в этот момент дубля
            var bot = _botInfo.Keys.FirstOrDefault(b => b != null && (ulong)b.userID == di.DroppedBy);
            BotInfo bi;
            if (bot == null || !_botInfo.TryGetValue(bot, out bi) || bi.Take.Loot == null) return;
            var l = bi.Take.Loot.FirstOrDefault(x => x.b == 0 && x.s == item.info.shortname) ?? bi.Take.Loot.FirstOrDefault(x => x.s == item.info.shortname);
            if (l == null) return;
            List<string> list;
            if (!_botDropped.TryGetValue(di.DroppedBy, out list)) _botDropped[di.DroppedBy] = list = new List<string>();
            list.Add(l.s);
            var drv = bot.GetComponent<ReplayDriver>();
            ApplyState(item, l, bi.Take, drv != null ? drv.TakeTime : -1f);
        }

        // Бота нокнули: оружие вылетает из рук (как при смерти в Rust), бот остаётся лежать
        // Актёр что-то поставил во время записи — запоминаем; после записи настоящая постройка убирается, при просмотре её ставит бот
        private void OnEntityBuilt(Planner planner, GameObject go)
        {
            var player = planner?.GetOwnerPlayer();
            Recorder rec;
            if (player == null || go == null || !_recorders.TryGetValue(Id(player), out rec)) return;
            var e = go.ToBaseEntity();
            if (e == null) return;
            var p = e.transform.position;
            var r = e.transform.rotation;
            var bb = e as BuildingBlock;
            rec.Take.Builds.Add(new BuildInfo { t = Time.realtimeSinceStartup - rec.Start, pf = e.PrefabName, x = p.x, y = p.y, z = p.z, rx = r.x, ry = r.y, rz = r.z, rw = r.w, gr = bb != null ? (int)bb.grade : -1, k = e.skinID });
            rec.Built.Add(e);
        }

        private void SpawnBuild(ulong owner, BuildInfo b)
        {
            var e = GameManager.server.CreateEntity(b.pf, new Vector3(b.x, b.y, b.z), new Quaternion(b.rx, b.ry, b.rz, b.rw));
            if (e == null) return;
            e.enableSaving = false;
            e.skinID = b.k;
            var se = e as StabilityEntity;
            if (se != null) se.grounded = true;
            var bb = e as BuildingBlock;
            if (bb != null) bb.blockDefinition = PrefabAttribute.server.Find<Construction>(bb.prefabID);
            e.Spawn();
            if (bb != null)
            {
                if (b.gr >= 0) bb.SetGrade((BuildingGrade.Enum)b.gr);
                bb.SetHealthToMax();
                bb.SendNetworkUpdate();
            }
            AddProp(owner, e);
        }

        private void OnPlayerWound(BasePlayer player, HitInfo info)
        {
            if (player == null || !_botInfo.ContainsKey(player)) return;
            var drv = player.GetComponent<ReplayDriver>();
            if (drv != null) drv.Downed = true;
            if (player.GetActiveItem() != null)
                player.Belt.DropActive(player.GetDropPosition(), player.GetInheritedDropVelocity() + new Vector3(UnityEngine.Random.Range(-1f, 1f), 2f, UnityEngine.Random.Range(-1f, 1f)));
        }

        private object CanLootEntity(BasePlayer player, PlayerCorpse corpse)
        {
            if (!_config.BlockBotLoot || corpse == null || corpse.net == null) return null;
            return _botCorpseIds.Contains(corpse.net.ID.Value) ? (object)false : null;
        }

        private object CanMountEntity(BasePlayer player, BaseMountable entity)
        {
            if (player == null || entity == null || _botInfo.ContainsKey(player)) return null;
            if (entity == _camMounting || _cameras.Values.Any(c => c.Chair == entity)) return null;   // стул камеры — защищённый реквизит, но садиться на него можно
            var root = MountRoot(entity);
            if (_propSet.Contains(entity) || (root != null && _propSet.Contains(root))) return false;
            return null;
        }

        private void OnEntityDismounted(BaseMountable mount, BasePlayer player)
        {
            if (player == null) return;
            CameraDriver cam;
            if (_cameras.TryGetValue(Id(player), out cam) && !cam.Stopping)
                NextTick(() => StopCamera(player, true));
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            var bot = entity as BasePlayer;
            if (bot != null) _botInfo.Remove(bot);
            var be = entity as BaseEntity;
            if (be != null) _propSet.Remove(be);
        }

        #endregion

        #region Запись

        private void AddEvent(Recorder rec, int type, Vector3 target, uint gesture)
        {
            rec.Take.Events.Add(new TakeEvent
            {
                t = Time.realtimeSinceStartup - rec.Start,
                e = type,
                x = target.x, y = target.y, z = target.z,
                g = gesture
            });
        }

        private static LootInfo ToLoot(Item item)
        {
            var l = new LootInfo { s = item.info.shortname, k = item.skin, n = item.amount };
            if (item.hasCondition) { float full = item.info.condition.max; l.c = item.condition / full; l.mc = item.maxCondition / full; }
            if (item.contents != null && item.contents.itemList.Count > 0) l.m = item.contents.itemList.Select(x => x.info.shortname).ToList();
            var proj = item.GetHeldEntity() as BaseProjectile;
            if (proj != null && proj.primaryMagazine != null) { l.a = proj.primaryMagazine.contents; l.at = proj.primaryMagazine.ammoType?.shortname; }
            return l;
        }

        // Перед смертью бота — инвентарь актёра как на старте дубля: те же контейнеры и слоты, прочность, модули, патроны.
        // Оружие, которое уже вылетело из рук (Rust выбрасывает его до OnEntityDeath), второй раз не кладём.
        private void GiveLoot(BasePlayer bot)
        {
            BotInfo bi;
            if (!_config.BotCarryInventory || !_botInfo.TryGetValue(bot, out bi) || bi.Take.Loot == null || bi.Take.Loot.Count == 0) return;
            var take = bi.Take;
            ulong botId = (ulong)bot.userID;
            List<string> dropped;
            _botDropped.TryGetValue(botId, out dropped);
            _botDropped.Remove(botId);
            var drv = bot.GetComponent<ReplayDriver>();
            float t = drv != null ? drv.TakeTime : -1f;
            var conts = new[] { bot.inventory.containerBelt, bot.inventory.containerMain, bot.inventory.containerWear };
            bool hasWear = take.Loot.Any(x => x.b == 2);
            conts[0].Clear(); conts[1].Clear();
            if (hasWear) conts[2].Clear();
            ItemManager.DoRemoves();
            foreach (var l in take.Loot)
            {
                if (dropped != null && dropped.Remove(l.s)) continue;   // уже лежит на земле
                var item = ItemManager.CreateByName(l.s, Mathf.Max(1, l.n), l.k);
                if (item == null) continue;
                if (l.m != null && item.contents != null)
                    foreach (var m in l.m)
                    {
                        var mod = ItemManager.CreateByName(m, 1, 0UL);
                        if (mod != null && !mod.MoveToContainer(item.contents)) mod.Remove();
                    }
                ApplyState(item, l, take, t);
                var target = conts[Mathf.Clamp(l.b, 0, 2)];
                // без склейки стаков: каждая пачка — в свой слот, как у актёра
                if (!item.MoveToContainer(target, l.p, false) && !item.MoveToContainer(target, -1, false) && !item.MoveToContainer(conts[1])) item.Remove();
            }
        }

        // Прочность и патроны в магазине — как у актёра к моменту t дубля
        private static void ApplyState(Item item, LootInfo l, Take take, float t)
        {
            if (item.hasCondition)
            {
                float full = item.info.condition.max;
                item.maxCondition = Mathf.Clamp(full * l.mc, 1f, full);
                item.condition = Mathf.Clamp(full * l.c, 0f, item.maxCondition);
            }
            var proj = item.GetHeldEntity() as BaseProjectile;
            if (proj != null && proj.primaryMagazine != null && l.a >= 0)
            {
                var ammoDef = string.IsNullOrEmpty(l.at) ? null : ItemManager.FindItemDefinition(l.at);
                if (ammoDef != null) proj.primaryMagazine.ammoType = ammoDef;
                proj.primaryMagazine.contents = AmmoAt(take, l, t, proj.primaryMagazine.capacity);
                proj.SendNetworkUpdateImmediate();
            }
            item.MarkDirty();
        }

        // Патроны в магазине к моменту t дубля: старт минус выстрелы из этого оружия, перезарядка — полный магазин
        private static int AmmoAt(Take take, LootInfo weapon, float t, int capacity)
        {
            int ammo = weapon.a;
            if (t < 0f) return ammo;
            foreach (var ev in take.Events)
            {
                if (ev.t > t) break;
                if (ev.e != EvFire && ev.e != EvReload) continue;
                if (HeldAt(take, ev.t) != weapon.s) continue;
                ammo = ev.e == EvFire ? Mathf.Max(0, ammo - 1) : capacity;
            }
            return ammo;
        }

        // Что было в руках актёра в момент t дубля (по кадрам)
        private static string HeldAt(Take take, float t)
        {
            var frames = take.Frames;
            int lo = 0, hi = frames.Count - 1, idx = -1;
            while (lo <= hi) { int mid = (lo + hi) / 2; if (frames[mid].t <= t) { idx = mid; lo = mid + 1; } else hi = mid - 1; }
            if (idx < 0) return null;
            int i = frames[idx].i;
            return i >= 0 && i < take.Items.Count ? take.Items[i].s : null;
        }

        private static ItemInfo ToInfo(Item item)
        {
            var info = new ItemInfo { s = item.info.shortname, k = item.skin };
            if (item.contents != null && item.contents.itemList.Count > 0)
                info.m = item.contents.itemList.Select(x => x.info.shortname).ToList();
            return info;
        }

        private void PlayFx(BasePlayer player, string prefab)
        {
            if (!_config.CountdownSound || player?.net?.connection == null) return;
            EffectNetwork.Send(new Effect(prefab, player, 0, Vector3.up, Vector3.zero), player.net.connection);
        }

        // «Записать новый дубль», F8, /vr rec: если в команде есть ещё кто-то онлайн — «Мотор» для всех актёров, иначе соло
        private void StartRecordAuto(BasePlayer player)
        {
            if (TeamOnline(OwnerOf(Id(player))).Count > 1) StartGroupCountdown(player);
            else StartCountdown(player, -1, -1);
        }

        // Стоп у лидера (хозяина сцены) — для всех: отменяет общий отсчёт и останавливает групповую запись
        private bool LeaderStop(BasePlayer player)
        {
            var id = Id(player);
            var owner = OwnerOf(id);
            if (id != owner) return false;
            if (_groupCountdowns.ContainsKey(owner)) { CancelGroupCountdown(owner); return true; }
            var rec = _recorders.Values.FirstOrDefault(r => r.Owner == owner && r.Group != null);
            if (rec == null) return false;
            StopGroup(rec.Group);
            return true;
        }

        private void StartCountdown(BasePlayer player, int replaceIndex, int appendIndex)
        {
            var id = Id(player);
            if (_recorders.ContainsKey(id) || _countdowns.ContainsKey(id)) return;
            StopCamera(player, true);

            var owner = OwnerOf(id);
            var scene = GetData(owner).Scene;
            var busy = _recorders.Values.FirstOrDefault(r => r.Owner == owner);
            if (busy != null || _groupCountdowns.ContainsKey(owner))
            {
                Reply(player, "SceneBusy", busy?.Player?.displayName ?? "…");
                return;
            }
            if (replaceIndex < 0 && appendIndex < 0 && scene.Takes.Count >= _config.MaxTakes)
            {
                Reply(player, "TakeLimit", _config.MaxTakes);
                return;
            }

            if (appendIndex >= 0 && appendIndex < scene.Takes.Count)
            {
                var last = scene.Takes[appendIndex].Frames.LastOrDefault();
                if (last != null && last.v < 0) player.Teleport(new Vector3(last.x, last.y, last.z));
            }

            CloseUi(player);
            int left = Mathf.Max(0, _config.Countdown);
            if (left == 0)
            {
                StartRecording(player, replaceIndex, appendIndex);
                return;
            }

            ShowCountdown(player, left);
            PlayFx(player, FxBeep);
            _countdowns[id] = timer.Repeat(1f, left, () =>
            {
                if (player == null || !player.IsConnected) { CancelCountdown(id); return; }
                left--;
                if (left > 0)
                {
                    ShowCountdown(player, left);
                    PlayFx(player, FxBeep);
                    return;
                }
                _countdowns.Remove(id);
                CuiHelper.DestroyUi(player, UiCountdown);
                PlayFx(player, FxGo);
                StartRecording(player, replaceIndex, appendIndex);
            });
        }

        private void CancelCountdown(ulong id)
        {
            Timer t;
            if (_countdowns.TryGetValue(id, out t))
            {
                t?.Destroy();
                _countdowns.Remove(id);
            }
            var p = BasePlayer.FindByID(id);
            if (p != null) CuiHelper.DestroyUi(p, UiCountdown);
        }

        private void StartRecording(BasePlayer player, int replaceIndex, int appendIndex)
        {
            var owner = OwnerOf(Id(player));
            var data = GetData(owner);
            var scene = data.Scene;
            float startTime = data.Seek;
            if (appendIndex >= 0 && appendIndex < scene.Takes.Count) startTime = scene.Takes[appendIndex].SceneLength;

            int skip = replaceIndex >= 0 ? replaceIndex : appendIndex;
            StartPlayback(owner, -1, skip, startTime, 1f, false);
            BeginRecorder(player, owner, replaceIndex, appendIndex, startTime, Time.realtimeSinceStartup, null);
        }

        private Recorder BeginRecorder(BasePlayer player, ulong owner, int replaceIndex, int appendIndex, float startTime, float startAt, GroupRec group)
        {
            var id = Id(player);
            var data = GetData(owner);
            var my = GetData(id);
            var scene = data.Scene;

            string name;
            if (!string.IsNullOrEmpty(my.NextName)) name = my.NextName;
            else if (replaceIndex >= 0 && replaceIndex < scene.Takes.Count) name = scene.Takes[replaceIndex].Name;
            else name = owner == id && group == null ? T(id, "TakeN", scene.Takes.Count + 1) : player.displayName;

            var take = new Take { Name = name, Delay = startTime, RecorderId = id, LookSeed = NewLookSeed() };
            if (group != null && _config.GroupTakesLookLikeAuthor) take.LookMode = 1;
            if (replaceIndex >= 0 && replaceIndex < scene.Takes.Count)
            {
                var old = scene.Takes[replaceIndex];
                take.Enabled = old.Enabled; take.Damage = old.Damage; take.God = old.God;
                take.Health = old.Health; take.Loop = old.Loop; take.PlayVoice = old.PlayVoice;
                take.LookMode = old.LookMode; take.LookSeed = old.LookSeed; take.LookSteamId = old.LookSteamId;
                take.LookName = old.LookName; take.LookRealId = old.LookRealId;
            }
            foreach (var item in player.inventory.containerWear.itemList)
                take.Wear.Add(ToInfo(item));
            if (_config.BotCarryInventory)
            {
                var conts = new[] { player.inventory.containerBelt, player.inventory.containerMain, player.inventory.containerWear };
                for (int b = 0; b < conts.Length; b++)
                    foreach (var item in conts[b].itemList) { var l = ToLoot(item); l.b = b; l.p = item.position; take.Loot.Add(l); }
            }

            var rec = new Recorder
            {
                Player = player,
                Owner = owner,
                Take = take,
                Start = startAt,
                ReplaceIndex = replaceIndex,
                AppendIndex = appendIndex,
                Group = group
            };
            _recorders[id] = rec;
            CaptureFrame(rec);

            if (!my.CleanFrame) ShowRecHud(player, 0);
            Reply(player, group != null ? "RecStartedGroup" : "RecStarted");
            return rec;
        }

        // ---------------- Мотор для всех
        private List<Member> Team(ulong owner)
        {
            List<Member> t;
            if (!_teams.TryGetValue(owner, out t)) _teams[owner] = t = new List<Member>();
            if (!t.Any(m => m.Id == owner)) t.Insert(0, new Member { Id = owner });
            return t;
        }

        private Member MemberOf(ulong id)
        {
            var owner = OwnerOf(id);
            return Team(owner).FirstOrDefault(m => m.Id == id);
        }

        private void LeaveTeam(ulong id, bool notify)
        {
            ulong owner;
            if (!_links.TryGetValue(id, out owner)) return;
            _links.Remove(id);
            List<Member> t;
            if (_teams.TryGetValue(owner, out t)) t.RemoveAll(m => m.Id == id);
            if (!notify) return;
            var host = BasePlayer.FindByID(owner);
            var p = BasePlayer.FindByID(id);
            if (host != null) { Reply(host, "GuestLeft", p?.displayName ?? id.ToString()); Refresh(host); }
        }

        private List<BasePlayer> GroupParticipants(ulong owner)
        {
            var list = new List<BasePlayer>();
            foreach (var m in Team(owner))
            {
                if (!m.Actor || !m.Ready) continue;
                var p = BasePlayer.FindByID(m.Id);
                if (p == null || !p.IsConnected || p.IsDead() || _recorders.ContainsKey(m.Id) || _countdowns.ContainsKey(m.Id)) continue;
                if (m.Id != owner && OwnerOf(m.Id) != owner) continue;
                list.Add(p);
            }
            return list;
        }

        private List<BasePlayer> TeamOnline(ulong owner)
        {
            return Team(owner).Select(m => BasePlayer.FindByID(m.Id)).Where(p => p != null && p.IsConnected).ToList();
        }

        private void StartGroupCountdown(BasePlayer starter)
        {
            var owner = OwnerOf(Id(starter));
            if (_groupCountdowns.ContainsKey(owner)) return;
            if (_recorders.Values.Any(r => r.Owner == owner)) { Reply(starter, "AlreadyRecording"); return; }

            var actors = GroupParticipants(owner);
            if (actors.Count == 0) { Reply(starter, "NoActors"); return; }
            var scene = GetData(owner).Scene;
            if (scene.Takes.Count + actors.Count > _config.MaxTakes) { Reply(starter, "TakeLimit", _config.MaxTakes); return; }

            var everyone = TeamOnline(owner);
            foreach (var p in everyone) { StopCamera(p, false); CloseUi(p); }
            var names = string.Join(", ", actors.Select(p => p.displayName).ToArray());
            foreach (var p in everyone) Reply(p, "MotorAnnounce", starter.displayName, names);

            int left = Mathf.Max(1, _config.GroupCountdown);
            Action show = () =>
            {
                foreach (var p in TeamOnline(owner))
                {
                    ShowCountdown(p, left, actors.Contains(p) ? T(Id(p), "MotorYou") : T(Id(p), "MotorWatch"));
                    PlayFx(p, FxBeep);
                }
            };
            show();
            _groupCountdowns[owner] = timer.Repeat(1f, left, () =>
            {
                left--;
                if (left > 0) { show(); return; }
                _groupCountdowns.Remove(owner);
                foreach (var p in TeamOnline(owner)) { CuiHelper.DestroyUi(p, UiCountdown); PlayFx(p, FxGo); }
                BeginGroup(owner, actors);
            });
        }

        private void CancelGroupCountdown(ulong owner)
        {
            Timer t;
            if (!_groupCountdowns.TryGetValue(owner, out t)) return;
            t?.Destroy();
            _groupCountdowns.Remove(owner);
            foreach (var p in TeamOnline(owner))
            {
                CuiHelper.DestroyUi(p, UiCountdown);
                Reply(p, "MotorCancelled");
            }
        }

        private void BeginGroup(ulong owner, List<BasePlayer> actors)
        {
            var data = GetData(owner);
            float startTime = data.Seek;
            StartPlayback(owner, -1, -1, startTime, 1f, false);
            var group = new GroupRec { Owner = owner };
            float startAt = Time.realtimeSinceStartup;
            foreach (var p in actors)
            {
                if (p == null || !p.IsConnected || p.IsDead() || _recorders.ContainsKey(Id(p))) continue;
                group.Ids.Add(Id(p));
                BeginRecorder(p, owner, -1, -1, startTime, startAt, group);
            }
        }

        private void StopGroup(GroupRec group)
        {
            if (group == null || group.Stopping) return;
            group.Stopping = true;
            foreach (var gid in group.Ids.ToList())
            {
                var p = BasePlayer.FindByID(gid);
                if (p != null) StopRecording(p, true);
                else _recorders.Remove(gid);
            }
            var host = BasePlayer.FindByID(group.Owner);
            foreach (var p in TeamOnline(group.Owner)) Reply(p, "MotorDone", group.Ids.Count);
        }

        private void StopRecording(BasePlayer player, bool save, bool single = false)
        {
            if (player == null) return;
            var id = Id(player);
            Recorder rec;
            if (!_recorders.TryGetValue(id, out rec)) return;
            if (rec.Group != null && !rec.Group.Stopping && save && !single) { StopGroup(rec.Group); return; }
            _recorders.Remove(id);
            if (player.IsConnected) { CuiHelper.DestroyUi(player, UiHud); CuiHelper.DestroyUi(player, UiPbHud); }
            foreach (var be in rec.Built) KillEntity(be);   // при просмотре это поставит бот

            var pb = GetPlayback(rec.Owner);
            if (pb != null && !_recorders.Values.Any(r => r.Owner == rec.Owner)) pb.Pause();

            if (!save) return;

            var take = rec.Take;
            if (take.Frames.Count < 2)
            {
                if (player.IsConnected) Reply(player, "TooShort");
                return;
            }
            take.Duration = take.Frames[take.Frames.Count - 1].t;

            var data = GetData(rec.Owner);
            var scene = data.Scene;
            PushUndo(rec.Owner);

            if (rec.AppendIndex >= 0 && rec.AppendIndex < scene.Takes.Count)
            {
                AppendTake(scene.Takes[rec.AppendIndex], take);
                take = scene.Takes[rec.AppendIndex];
            }
            else if (rec.ReplaceIndex >= 0 && rec.ReplaceIndex < scene.Takes.Count) scene.Takes[rec.ReplaceIndex] = take;
            else scene.Takes.Add(take);

            GetData(id).NextName = "";
            SaveData(rec.Owner);
            if (rec.Owner != id) SaveData(id);

            if (!player.IsConnected) return;
            Reply(player, "TakeSaved", take.Name, take.Duration.ToString("0.0"), take.Frames.Count);
            if (rec.Owner != id)
            {
                var owner = BasePlayer.FindByID(rec.Owner);
                if (owner != null) Reply(owner, "GuestSaved", player.displayName, take.Name);
            }
            OpenUi(player);
        }

        private void TickRecorders()
        {
            if (_recorders.Count == 0) return;
            foreach (var pair in _recorders.ToList())
            {
                var rec = pair.Value;
                var player = rec.Player;
                if (player == null || !player.IsConnected) { _recorders.Remove(pair.Key); continue; }
                if (player.IsDead()) { StopRecording(player, true, true); continue; }

                try { CaptureFrame(rec); }
                catch (Exception ex) { PrintWarning("Capture error: " + ex.Message); }

                float elapsed = Time.realtimeSinceStartup - rec.Start;
                if (elapsed >= _config.MaxSeconds)
                {
                    Reply(player, "MaxLength");
                    StopRecording(player, true);
                    continue;
                }

                int sec = Mathf.FloorToInt(elapsed);
                if (sec != rec.LastShownSecond)
                {
                    rec.LastShownSecond = sec;
                    if (!GetData(pair.Key).CleanFrame) ShowRecHud(player, sec);
                }
            }
        }

        private void CaptureFrame(Recorder rec)
        {
            var player = rec.Player;
            var take = rec.Take;
            var pos = player.transform.position;
            var e = player.eyes.rotation.eulerAngles;
            float pitch = e.x > 180f ? e.x - 360f : e.x;
            float now = Time.realtimeSinceStartup - rec.Start;

            // Предмет в руках
            var active = player.GetActiveItem();
            int bits = 0;
            if (active == null)
            {
                rec.LastKey = null;
                rec.LastItem = -1;
            }
            else
            {
                var info = ToInfo(active);
                var key = info.Key;
                if (key != rec.LastKey)
                {
                    int idx = take.Items.FindIndex(x => x.Key == key);
                    if (idx < 0) { take.Items.Add(info); idx = take.Items.Count - 1; }
                    rec.LastKey = key;
                    rec.LastItem = idx;
                }
                var held = player.GetHeldEntity();
                if (held != null && held.HasFlag(BaseEntity.Flags.On)) bits |= BitLight;
            }
            if (player.HasPlayerFlag(BasePlayer.PlayerFlags.Wounded)) bits |= BitWounded;

            var frame = new Frame
            {
                t = now,
                x = pos.x, y = pos.y, z = pos.z,
                p = pitch, w = e.y,
                f = player.modelState != null ? player.modelState.flags : 0,
                i = rec.LastItem,
                h = bits
            };

            // Транспорт
            if (_config.RecordVehicles)
            {
                var mount = player.GetMounted();
                var root = MountRoot(mount);
                if (root != null && !_propSet.Contains(root))
                {
                    if (root != rec.LastRoot)
                    {
                        rec.LastRoot = root;
                        take.Vehicles.Add(new VehicleInfo { p = root.PrefabName, s = Seats(root).IndexOf(mount) });
                        rec.LastVehicle = take.Vehicles.Count - 1;
                    }
                    var rp = root.transform.position;
                    var rq = root.transform.rotation;
                    frame.v = rec.LastVehicle;
                    frame.vx = rp.x; frame.vy = rp.y; frame.vz = rp.z;
                    frame.qx = rq.x; frame.qy = rq.y; frame.qz = rq.z; frame.qw = rq.w;
                    frame.vf = (int)root.flags;
                }
                else
                {
                    rec.LastRoot = null;
                    rec.LastVehicle = -1;
                }
            }

            // Жесты
            try
            {
                var gesture = GetMember(player, "currentGesture");
                uint gid = 0;
                if (gesture != null)
                {
                    var raw = GetMember(gesture, "gestureId");
                    if (raw != null) gid = Convert.ToUInt32(raw);
                }
                if (gid != 0 && gid != rec.LastGesture) AddEvent(rec, EvGesture, Vector3.zero, gid);
                rec.LastGesture = gid;
            }
            catch { }

            take.Frames.Add(frame);
        }

        private static void AppendTake(Take target, Take add)
        {
            float offset = target.Duration + 0.05f;
            var itemMap = new Dictionary<int, int>();
            for (int i = 0; i < add.Items.Count; i++)
            {
                var key = add.Items[i].Key;
                int idx = target.Items.FindIndex(x => x.Key == key);
                if (idx < 0) { target.Items.Add(add.Items[i]); idx = target.Items.Count - 1; }
                itemMap[i] = idx;
            }
            int vehOffset = target.Vehicles.Count;
            target.Vehicles.AddRange(add.Vehicles);

            foreach (var f in add.Frames)
            {
                var c = f.Clone();
                c.t += offset;
                c.i = c.i >= 0 && itemMap.ContainsKey(c.i) ? itemMap[c.i] : -1;
                if (c.v >= 0) c.v += vehOffset;
                target.Frames.Add(c);
            }
            foreach (var ev in add.Events) { ev.t += offset; target.Events.Add(ev); }
            foreach (var v in add.Voice) { v.t += offset; target.Voice.Add(v); }
            target.Duration = target.Frames[target.Frames.Count - 1].t;
        }

        #endregion

        #region Воспроизведение

        private void StartPlayback(ulong owner, int onlyIndex, int skipIndex, float seek, float speed, bool announce)
        {
            var data = GetData(owner);
            var scene = data.Scene;
            ClearBots(owner);

            var pb = new Playback
            {
                BaseTime = onlyIndex >= 0 ? 0f : Mathf.Max(0f, seek),
                ResumeAt = Time.realtimeSinceStartup,
                Speed = Mathf.Clamp(speed, 0.05f, 4f)
            };
            pb.LastCueTime = pb.BaseTime;
            pb.CueIndex = 0;
            var cues = scene.Cues.OrderBy(c => c.t).ToList();
            while (pb.CueIndex < cues.Count && cues[pb.CueIndex].t < pb.BaseTime) pb.CueIndex++;
            _playbacks[owner] = pb;

            int count = 0;
            bool hasLoop = false;
            float end = onlyIndex >= 0 ? 0f : scene.Length;
            for (int i = 0; i < scene.Takes.Count; i++)
            {
                var take = scene.Takes[i];
                if (i == skipIndex) continue;
                if (onlyIndex >= 0 ? i != onlyIndex : !take.Enabled) continue;
                if (SpawnBot(owner, take, onlyIndex >= 0 ? 0f : take.Delay) == null) continue;
                count++;
                if (take.Loop) hasLoop = true;
                if (onlyIndex >= 0) end = take.Duration / take.Speed;
            }
            pb.EndTime = hasLoop ? 0f : end;

            var host = BasePlayer.FindByID(owner);
            if (host != null) ApplyWeather(host, owner, false);

            if (!announce || host == null) return;
            if (count == 0) Reply(host, "NoTakes");
            else Reply(host, "ScenePlaying", count);
        }

        private void StopPlayback(ulong owner)
        {
            _playbacks.Remove(owner);
            ClearBots(owner);
            foreach (var cam in _cameras.Values.ToList())
                if (cam.Owner == owner) StopCamera(cam.Viewer, true);
        }

        private void SeekTo(ulong owner, float t, bool keepPaused)
        {
            var data = GetData(owner);
            data.Seek = Mathf.Clamp(t, 0f, 3600f);
            var pb = GetPlayback(owner);
            if (pb == null) return;
            bool paused = pb.Paused;
            StartPlayback(owner, -1, -1, data.Seek, pb.Slowmo ? data.SlowmoSpeed : data.GlobalSpeed, false);
            var npb = GetPlayback(owner);
            if (npb != null)
            {
                npb.Slowmo = pb.Slowmo;
                if (paused && keepPaused) npb.Pause();
            }
        }

        private BasePlayer SpawnBot(ulong owner, Take take, float delay)
        {
            if (take.Frames.Count == 0) return null;
            var f0 = take.Frames[0];
            var pos = new Vector3(f0.x, f0.y, f0.z);

            var bot = GameManager.server.CreateEntity(PlayerPrefab, pos, Quaternion.Euler(0f, f0.w, 0f)) as BasePlayer;
            if (bot == null) return null;

            bot.userID = BotUserId(take);
            bot.displayName = take.Name;
            bot.enableSaving = false;
            bot.Spawn();
            bot.InitializeHealth(Mathf.Clamp(take.Health, 1f, 100f), 100f);
            if (bot.IsSleeping()) bot.EndSleeping();

            bot.inventory.Strip();
            foreach (var w in take.Wear)
            {
                var item = CreateItem(w);
                if (item != null && !item.MoveToContainer(bot.inventory.containerWear)) item.Remove();
            }

            _botInfo[bot] = new BotInfo { Owner = owner, Take = take };
            _botIdOwner[(ulong)bot.userID] = owner;
            List<BasePlayer> list;
            if (!_bots.TryGetValue(owner, out list)) _bots[owner] = list = new List<BasePlayer>();
            list.Add(bot);

            var driver = bot.gameObject.AddComponent<ReplayDriver>();
            driver.Init(bot, take, owner, delay);
            bot.SendNetworkUpdateImmediate();
            return bot;
        }

        private static Item CreateItem(ItemInfo info)
        {
            var item = ItemManager.CreateByName(info.s, 1, info.k);
            if (item == null) return null;
            if (info.m != null && item.contents != null)
            {
                foreach (var m in info.m)
                {
                    var mod = ItemManager.CreateByName(m, 1, 0UL);
                    if (mod != null && !mod.MoveToContainer(item.contents)) mod.Remove();
                }
            }
            return item;
        }

        private void AddProp(ulong owner, BaseEntity e)
        {
            if (e == null) return;
            List<BaseEntity> list;
            if (!_props.TryGetValue(owner, out list)) _props[owner] = list = new List<BaseEntity>();
            list.Add(e);
        }

        private BaseEntity SpawnProp(ulong owner, string prefab, Vector3 pos, Quaternion rot, bool protect)
        {
            var e = GameManager.server.CreateEntity(prefab, pos, rot);
            if (e == null) return null;
            e.enableSaving = false;
            e.Spawn();
            AddProp(owner, e);
            if (protect) _propSet.Add(e);
            return e;
        }

        private void ClearBots(ulong owner)
        {
            List<BasePlayer> list;
            if (_bots.TryGetValue(owner, out list))
            {
                foreach (var bot in list.ToList())
                {
                    _botInfo.Remove(bot);
                    KillEntity(bot);
                }
                list.Clear();
            }

            List<BaseEntity> props;
            if (_props.TryGetValue(owner, out props))
            {
                foreach (var p in props.ToList())
                {
                    if (p is PlayerCorpse && !_config.CleanupCorpses) continue;
                    if (p != null && p.net != null) _botCorpseIds.Remove(p.net.ID.Value);
                    if (_cameras.Values.Any(c => c.Chair == p)) continue;
                    _propSet.Remove(p);
                    KillEntity(p);
                    props.Remove(p);
                }
            }
        }

        private static void KillEntity(BaseEntity e)
        {
            if (e != null && !e.IsDestroyed) e.Kill();
        }

        private class ReplayDriver : FacepunchBehaviour
        {
            private BasePlayer _bot;
            private Take _take;
            private ulong _owner;
            private float _delay;
            private int _idx, _ev, _voice;
            private int _item = -2, _bits = -1, _vehicle = -1;
            private float _lastT = -1f;
            public float TakeTime => _lastT;   // текущее время дубля (для патронов в магазине трупа)
            public bool Downed;                 // бота нокнули: лежит, дубль для него дальше не играет
            private int _build;
            private readonly HashSet<int> _builtIdx = new HashSet<int>();
            private float _lastSend;
            private Vector3 _lastPos;
            private float _lastYaw, _lastPitch;
            private bool _dead;
            private readonly Dictionary<int, BaseEntity> _vehicles = new Dictionary<int, BaseEntity>();

            public void Init(BasePlayer bot, Take take, ulong owner, float delay)
            {
                _bot = bot;
                _take = take;
                _owner = owner;
                _delay = delay;
            }

            private void Update()
            {
                if (_bot == null || _bot.IsDestroyed || _bot.IsDead()) { Destroy(this); return; }
                if (_ins == null) return;
                if (Downed) return;
                var pb = _ins.GetPlayback(_owner);
                if (pb == null) return;
                if (_bot.IsSleeping()) _bot.EndSleeping();

                var frames = _take.Frames;
                float dur = Mathf.Max(0.01f, _take.Duration);
                float t = (pb.Time - _delay) * _take.Speed;

                if (t < 0f)
                {
                    Apply(frames[0], frames[0], 0f);
                    return;
                }

                if (_take.Loop) t %= dur;
                else if (t > dur) t = dur;

                if (t < _lastT)
                {
                    _idx = 0;
                    SkipTo(t);
                }
                else if (t - _lastT > 0.5f && _lastT >= 0f)
                {
                    SkipTo(t); // скачок вперёд — не отыгрываем пропущенные выстрелы
                }

                while (_idx < frames.Count - 2 && frames[_idx + 1].t <= t) _idx++;
                var a = frames[_idx];
                var b = frames[Mathf.Min(_idx + 1, frames.Count - 1)];
                float k = b.t > a.t ? Mathf.Clamp01((t - a.t) / (b.t - a.t)) : 1f;
                Apply(a, b, k);
                FireEvents(t);
                FireBuilds(t);
                PlayVoice(t, pb);
                _lastT = t;

                if (!_dead && _take.DieAt >= 0f && t >= _take.DieAt && !_take.God)
                {
                    _dead = true;
                    _bot.Die();
                }
            }

            private void SkipTo(float t)
            {
                _build = 0;
                _ev = 0;
                while (_ev < _take.Events.Count && _take.Events[_ev].t <= t) _ev++;
                _voice = 0;
                while (_voice < _take.Voice.Count && _take.Voice[_voice].t <= t) _voice++;
            }

            private bool Visible()
            {
                float cull = _ins._config.NetworkCullDistance;
                if (cull <= 0f) return true;
                foreach (var p in BasePlayer.activePlayerList)
                    if ((p.transform.position - _bot.transform.position).sqrMagnitude < cull * cull) return true;
                return false;
            }

            private void Apply(Frame a, Frame b, float k)
            {
                var pos = Vector3.Lerp(new Vector3(a.x, a.y, a.z), new Vector3(b.x, b.y, b.z), k);
                float pitch = Mathf.Lerp(a.p, b.p, k);
                float yaw = Mathf.LerpAngle(a.w, b.w, k);
                bool changed = false;

                // Транспорт
                if (a.v != _vehicle)
                {
                    LeaveVehicle();
                    _vehicle = a.v;
                    changed = true;
                }
                bool mounted = false;
                if (a.v >= 0 && a.v < _take.Vehicles.Count)
                {
                    var veh = GetVehicle(a);
                    if (veh != null)
                    {
                        var vpos = Vector3.Lerp(new Vector3(a.vx, a.vy, a.vz), b.v == a.v ? new Vector3(b.vx, b.vy, b.vz) : new Vector3(a.vx, a.vy, a.vz), k);
                        var qa = new Quaternion(a.qx, a.qy, a.qz, a.qw);
                        var qb = b.v == a.v ? new Quaternion(b.qx, b.qy, b.qz, b.qw) : qa;
                        veh.transform.position = vpos;
                        veh.transform.rotation = Quaternion.Slerp(qa, qb, k);
                        veh.flags = (BaseEntity.Flags)a.vf;
                        veh.SendNetworkUpdate();
                        mounted = _bot.GetMounted() != null;
                        if (!mounted) mounted = TryMount(veh, _take.Vehicles[a.v].s);
                    }
                }

                if (!mounted) _bot.ServerPosition = pos;
                var look = Quaternion.Euler(pitch, yaw, 0f);
                _bot.viewAngles = new Vector3(pitch, yaw, 0f);
                _bot.eyes.rotation = look;
                if (!mounted) _bot.ServerRotation = Quaternion.Euler(0f, yaw, 0f);
                if (_bot.modelState != null) _bot.modelState.flags = a.f;

                if (a.i != _item)
                {
                    _item = a.i;
                    SwitchItem(a.i);
                    _bits = -1;
                    changed = true;
                }
                if (a.h != _bits)
                {
                    _bits = a.h;
                    var held = _bot.GetHeldEntity();
                    if (held != null) using (var heldFlags = held.StartSetFlags(BaseEntity.FlagsUpdateMode.SendNetworkUpdate)) heldFlags.Set(BaseEntity.Flags.On, (a.h & BitLight) != 0);
                    _bot.SetPlayerFlag(BasePlayer.PlayerFlags.Wounded, (a.h & BitWounded) != 0);
                    changed = true;
                }

                float now = Time.realtimeSinceStartup;
                float rate = 1f / Mathf.Max(10f, _ins._config.NetworkRate);
                bool moved = (pos - _lastPos).sqrMagnitude > 0.0001f || Mathf.Abs(yaw - _lastYaw) > 0.05f || Mathf.Abs(pitch - _lastPitch) > 0.05f;
                if (changed || (moved && now - _lastSend >= rate && Visible()) || now - _lastSend >= 1f)
                {
                    _lastSend = now;
                    _lastPos = pos;
                    _lastYaw = yaw;
                    _lastPitch = pitch;
                    // Rust 2.0.79xx: позиции игроков рассылает отдельный тик только для подключённых клиентов —
                    // боту шлём сами, иначе клиент видит его рывками раз в 2–3 с и не может попасть на ходу
                    _bot.TransformChanged();            // сетка запросов сервера (попадания) и сетевая ячейка
                    _bot.SendNetworkUpdate_Position();  // позиция и поворот подписчикам
                    _bot.SendModelState();              // бег, присед, на земле — анимация
                    _bot.SendNetworkUpdate();
                }
            }

            private BaseEntity GetVehicle(Frame f)
            {
                BaseEntity veh;
                if (_vehicles.TryGetValue(f.v, out veh) && veh != null && !veh.IsDestroyed) return veh;
                var info = _take.Vehicles[f.v];
                if (string.IsNullOrEmpty(info.p)) return null;
                veh = _ins.SpawnProp(_owner, info.p, new Vector3(f.vx, f.vy, f.vz), new Quaternion(f.qx, f.qy, f.qz, f.qw), true);
                if (veh == null) return null;
                var rb = veh.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true;
                _vehicles[f.v] = veh;
                return veh;
            }

            private bool TryMount(BaseEntity veh, int seat)
            {
                var seats = Seats(veh);
                if (seats.Count == 0) return false;
                var s = seats[Mathf.Clamp(seat, 0, seats.Count - 1)];
                if (s == null) return false;
                return TryCall(s, "MountPlayer", _bot) && _bot.GetMounted() != null;
            }

            private void LeaveVehicle()
            {
                var m = _bot.GetMounted();
                if (m != null && !TryCall(m, "DismountPlayer", _bot, true)) TryCall(m, "DismountPlayer", _bot);
            }

            private void SwitchItem(int index)
            {
                var belt = _bot.inventory.containerBelt;
                foreach (var it in belt.itemList.ToList()) it.Remove();
                ItemManager.DoRemoves();

                if (index < 0 || index >= _take.Items.Count)
                {
                    _bot.UpdateActiveItem(default(ItemId));
                    return;
                }

                var item = CreateItem(_take.Items[index]);
                if (item == null) return;
                if (!item.MoveToContainer(belt, 0)) { item.Remove(); return; }
                _bot.UpdateActiveItem(item.uid);
            }

            // Постройки актёра — в тот же момент дубля (каждая один раз, даже при повторе)
            private void FireBuilds(float t)
            {
                var builds = _take.Builds;
                if (builds == null) return;
                for (; _build < builds.Count && builds[_build].t <= t; _build++)
                    if (_builtIdx.Add(_build)) _ins?.SpawnBuild(_owner, builds[_build]);
            }

            private void FireEvents(float t)
            {
                var events = _take.Events;
                while (_ev < events.Count && events[_ev].t <= t)
                {
                    var ev = events[_ev++];
                    var held = _bot.GetHeldEntity();
                    try
                    {
                        switch (ev.e)
                        {
                            case EvFire:
                            {
                                var gun = held as BaseProjectile;
                                if (gun != null)
                                {
                                    if (gun.primaryMagazine != null) gun.primaryMagazine.contents = gun.primaryMagazine.capacity;
                                    gun.nextAttackTime = 0f;   // выстрел по записи — без кулдауна оружия
                                    var aim = Matrix4x4.TRS(_bot.eyes.position, _bot.eyes.rotation, Vector3.one);   // откуда и куда стрелял актёр
                                    try { gun.ServerUse(new HeldEntityServerUseParams(1f, 1f, aim, true, false)); }
                                    catch (Exception ex)
                                    {
                                        gun.SignalBroadcast(BaseEntity.Signal.Attack, string.Empty);
                                        Interface.Oxide.LogWarning("[VideoRecorder] выстрел бота: " + ex.Message);
                                    }
                                }
                                else held?.SignalBroadcast(BaseEntity.Signal.Attack, string.Empty);
                                break;
                            }
                            case EvReload:
                                held?.SignalBroadcast(BaseEntity.Signal.Reload, string.Empty);
                                break;
                            case EvMelee:
                                held?.SignalBroadcast(BaseEntity.Signal.Attack, string.Empty);
                                break;
                            case EvThrow:
                                if (held != null && !TryCall(held, "ServerThrow", new Vector3(ev.x, ev.y, ev.z)))
                                    held.SignalBroadcast(BaseEntity.Signal.Throw, string.Empty);
                                _item = -2;
                                break;
                            case EvGesture:
                            {
                                var list = GetMember(_bot, "gestureList");
                                var cfg = list != null ? Call(list, "IdToGesture", ev.g) : null;
                                if (cfg != null) TryCall(_bot, "Server_StartGesture", cfg);
                                break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _ins?.PrintWarning("Bot event error: " + ex.Message);
                    }
                }
            }

            private void PlayVoice(float t, Playback pb)
            {
                if (!_take.PlayVoice || pb.Paused || Mathf.Abs(_take.Speed * pb.Speed - 1f) > 0.05f)
                {
                    while (_voice < _take.Voice.Count && _take.Voice[_voice].t <= t) _voice++;
                    return;
                }
                while (_voice < _take.Voice.Count && _take.Voice[_voice].t <= t)
                {
                    var chunk = _take.Voice[_voice++];
                    if (chunk.d != null) try { _bot.OnReceivedVoice(chunk.d); } catch { }   // ReadOnlySpan нельзя передать через рефлексию
                }
            }

            private void OnDestroy()
            {
                foreach (var v in _vehicles.Values)
                {
                    if (v == null || v.IsDestroyed) continue;
                    _ins?._propSet.Remove(v);
                    v.Kill();
                }
            }
        }

        #endregion

        #region Внешность ботов

        // Внешность игрока в Rust (лицо, кожа, волосы, пол) клиент вычисляет из userID.
        // Генератор сидируется младшими 32 битами, поэтому ID «младшие 32 бита как у игрока
        // + свой старший разряд» даёт ту же внешность, но остаётся NPC-диапазоном (< SteamID).
        private const ulong LowMask = 0xFFFFFFFFUL;
        private static uint _botCounter;
        private readonly Dictionary<ulong, BasePlayer> _previews = new Dictionary<ulong, BasePlayer>();

        private static ulong NewLookSeed() => (ulong)(uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue);

        private ulong LookSource(Take take)
        {
            switch (take.LookMode)
            {
                case 1: return take.RecorderId != 0 ? take.RecorderId : take.LookSeed;
                case 2: return take.LookSteamId != 0 ? take.LookSteamId : take.LookSeed;
                default: return take.LookSeed;
            }
        }

        private ulong BotUserId(Take take)
        {
            var src = LookSource(take);
            if (take.LookMode != 0 && take.LookRealId && _config.AllowRealSteamId && src > LowMask) return src;
            _botCounter = (_botCounter % 4000) + 1;
            return (src & LowMask) | ((ulong)_botCounter << 32);
        }

        private void KillPreview(ulong id)
        {
            BasePlayer pv;
            if (_previews.TryGetValue(id, out pv))
            {
                _botInfo.Remove(pv);
                KillEntity(pv);
                _previews.Remove(id);
            }
        }

        private void SpawnPreview(BasePlayer player, Take take, bool naked)
        {
            var id = Id(player);
            KillPreview(id);

            var fwd = player.eyes.HeadForward();
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var pos = player.transform.position + fwd.normalized * 2.2f;
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out hit, 6f, -5, QueryTriggerInteraction.Ignore)) pos = hit.point;
            var look = LookAngles(pos, player.eyes.position);

            var bot = GameManager.server.CreateEntity(PlayerPrefab, pos, Quaternion.Euler(0f, look.y, 0f)) as BasePlayer;
            if (bot == null) return;
            bot.userID = BotUserId(take);
            bot.displayName = take.Name;
            bot.enableSaving = false;
            bot.Spawn();
            bot.InitializeHealth(100f, 100f);
            if (bot.IsSleeping()) bot.EndSleeping();
            bot.inventory.Strip();
            if (!naked)
                foreach (var w in take.Wear)
                {
                    var item = CreateItem(w);
                    if (item != null && !item.MoveToContainer(bot.inventory.containerWear)) item.Remove();
                }
            bot.viewAngles = new Vector3(0f, look.y, 0f);
            bot.eyes.rotation = Quaternion.Euler(0f, look.y, 0f);
            bot.SendNetworkUpdateImmediate();

            _botInfo[bot] = new BotInfo { Owner = id, Take = new Take { Name = take.Name, God = true } };
            _previews[id] = bot;
            var spawned = bot;
            timer.Once(_config.PreviewSeconds, () =>
            {
                BasePlayer cur;
                if (_previews.TryGetValue(id, out cur) && cur == spawned) KillPreview(id);
            });
        }

        #endregion

        #region Сцена: эффекты, конец сцены

        private class FxType
        {
            public string Key;
            public string Prefab;
            public bool Entity;
        }

        private static readonly FxType[] FxTypes =
        {
            new FxType { Key = "FxC4", Prefab = "assets/prefabs/tools/c4/effects/c4_explosion.prefab" },
            new FxType { Key = "FxRocket", Prefab = "assets/prefabs/weapons/rocketlauncher/effects/rocket_explosion.prefab" },
            new FxType { Key = "FxBradley", Prefab = "assets/prefabs/npc/m2bradley/effects/bradley_explosion.prefab" },
            new FxType { Key = "FxGas", Prefab = "assets/bundled/prefabs/fx/gas_explosion_small.fx.prefab" },
            new FxType { Key = "FxSignal", Prefab = "assets/bundled/prefabs/fx/smoke_signal_full.prefab" },
            new FxType { Key = "FxFire", Prefab = "assets/bundled/prefabs/oilfireballsmall.prefab", Entity = true },
            new FxType { Key = "FxSmoke", Prefab = "assets/prefabs/tools/smoke grenade/grenade.smoke.deployed.prefab", Entity = true },
            new FxType { Key = "FxFlare", Prefab = "assets/prefabs/tools/flareold/flare.deployed.prefab", Entity = true },
        };

        private void RunCue(ulong owner, FxCue cue)
        {
            if (cue.k < 0 || cue.k >= FxTypes.Length) return;
            var type = FxTypes[cue.k];
            var pos = new Vector3(cue.x, cue.y, cue.z);
            try
            {
                if (!type.Entity)
                {
                    Effect.server.Run(type.Prefab, pos, Vector3.up);
                    return;
                }
                var e = SpawnProp(owner, type.Prefab, pos, Quaternion.identity, false);
                if (e == null) return;
                TryCall(e, "Explode");
                timer.Once(_config.FxEntityLifetime, () => KillEntity(e));
            }
            catch (Exception ex)
            {
                PrintWarning("FX error: " + ex.Message);
            }
        }

        private void TickScene()
        {
            foreach (var pair in _playbacks.ToList())
            {
                var owner = pair.Key;
                var pb = pair.Value;
                var scene = GetData(owner).Scene;
                float t = pb.Time;

                if (scene.Cues.Count > 0)
                {
                    var cues = scene.Cues.OrderBy(c => c.t).ToList();
                    if (t < pb.LastCueTime)
                    {
                        pb.CueIndex = 0;
                        while (pb.CueIndex < cues.Count && cues[pb.CueIndex].t < t) pb.CueIndex++;
                    }
                    while (pb.CueIndex < cues.Count && cues[pb.CueIndex].t <= t)
                        RunCue(owner, cues[pb.CueIndex++]);
                }
                pb.LastCueTime = t;

                if (pb.EndTime > 0f && !pb.Paused && t > pb.EndTime + 1.5f && !_recorders.Values.Any(r => r.Owner == owner))
                    pb.Pause();
            }
        }

        #endregion

        #region Кинокамера

        private class CameraDriver : FacepunchBehaviour
        {
            public BasePlayer Viewer;
            public BaseEntity Chair;
            public ulong Owner;
            public Vector3 ReturnPos;
            public bool Stopping;
            private float _lastLook;
            private float _lastSend;

            private void Update()
            {
                if (_ins == null || Viewer == null || Chair == null || Chair.IsDestroyed) return;
                var pb = _ins.GetPlayback(Owner);
                var data = _ins.GetData(Owner);
                var cam = data.Scene.Camera;
                float t = pb != null ? pb.Time : data.Seek;

                Vector3 pos;
                Vector3 ang;
                if (!_ins.ComputeCamera(Owner, cam, t, out pos, out ang)) return;

                float now = Time.realtimeSinceStartup;
                Chair.transform.position = pos - Vector3.up * _ins._config.CameraEyeOffset;
                Chair.transform.rotation = Quaternion.Euler(0f, ang.y, 0f);
                if (now - _lastSend >= 1f / 60f)
                {
                    _lastSend = now;
                    Chair.SendNetworkUpdateImmediate();
                }

                if (!cam.FreeLook && now - _lastLook >= 1f / Mathf.Max(5f, _ins._config.CameraLookRate))
                {
                    _lastLook = now;
                    ForceViewAngles(Viewer, ang);
                }
            }
        }

        private static Vector3 LookAngles(Vector3 from, Vector3 to)
        {
            var d = to - from;
            float flat = Mathf.Sqrt(d.x * d.x + d.z * d.z);
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Atan2(d.y, Mathf.Max(0.001f, flat)) * Mathf.Rad2Deg;
            return new Vector3(pitch, yaw, 0f);
        }

        // кривые скорости камеры между точками пути
        private static float EaseK(int e, float k)
        {
            switch (e)
            {
                case 1: return k;
                case 2: return k * k * k;
                case 3: { float u = 1f - k; return 1f - u * u * u; }
                case 4: return k < 0.5f ? 4f * k * k * k : 1f - Mathf.Pow(-2f * k + 2f, 3f) / 2f;
                default: return k;
            }
        }

        private static readonly string[] CamEases = { "Ease0", "Ease1", "Ease2", "Ease3", "Ease4" };

        // фокус на цели: путь задаёт позицию, взгляд — на глаза выбранного бота
        private void FocusAngles(ulong owner, CameraSettings cam, Vector3 pos, ref Vector3 ang)
        {
            if (cam.Focus < 0) return;
            var bot = FindBot(owner, cam.Focus);
            if (bot == null) return;
            ang = LookAngles(pos, bot.eyes.position);
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return (p1 * 2f + (p2 - p0) * t + (p0 * 2f - p1 * 5f + p2 * 4f - p3) * t2 + (p3 - p0 + (p1 - p2) * 3f) * t3) * 0.5f;
        }

        private BasePlayer FindBot(ulong owner, int takeIndex)
        {
            var scene = GetData(owner).Scene;
            if (takeIndex < 0 || takeIndex >= scene.Takes.Count) return null;
            var take = scene.Takes[takeIndex];
            List<BasePlayer> list;
            if (!_bots.TryGetValue(owner, out list)) return null;
            foreach (var b in list)
            {
                BotInfo bi;
                if (b != null && !b.IsDestroyed && _botInfo.TryGetValue(b, out bi) && bi.Take == take) return b;
            }
            return null;
        }

        private bool ComputeCamera(ulong owner, CameraSettings cam, float t, out Vector3 pos, out Vector3 ang)
        {
            pos = Vector3.zero;
            ang = Vector3.zero;

            if (cam.Mode == 0)
            {
                var keys = cam.Keys.OrderBy(q => q.t).ToList();
                if (keys.Count == 0) return false;
                if (keys.Count == 1 || t <= keys[0].t)
                {
                    var k0 = keys[0];
                    pos = new Vector3(k0.x, k0.y, k0.z);
                    ang = new Vector3(k0.p, k0.w, 0f);
                    FocusAngles(owner, cam, pos, ref ang);
                    return true;
                }
                if (t >= keys[keys.Count - 1].t)
                {
                    var kl = keys[keys.Count - 1];
                    pos = new Vector3(kl.x, kl.y, kl.z);
                    ang = new Vector3(kl.p, kl.w, 0f);
                    FocusAngles(owner, cam, pos, ref ang);
                    return true;
                }
                int i = 0;
                while (i < keys.Count - 2 && keys[i + 1].t <= t) i++;
                var a = keys[i];
                var b = keys[i + 1];
                var pa = keys[Mathf.Max(0, i - 1)];
                var pn = keys[Mathf.Min(keys.Count - 1, i + 2)];
                float k = Mathf.Clamp01((t - a.t) / Mathf.Max(0.001f, b.t - a.t));
                float sm = Mathf.Clamp01(cam.Smooth);
                float s = Mathf.Lerp(k, k * k * (3f - 2f * k), sm);
                if (a.e > 0) { k = EaseK(a.e, k); s = k; }
                var lin = Vector3.Lerp(new Vector3(a.x, a.y, a.z), new Vector3(b.x, b.y, b.z), k);
                pos = Vector3.Lerp(lin, CatmullRom(new Vector3(pa.x, pa.y, pa.z), new Vector3(a.x, a.y, a.z), new Vector3(b.x, b.y, b.z), new Vector3(pn.x, pn.y, pn.z), k), sm);
                ang = new Vector3(Mathf.Lerp(a.p, b.p, s), Mathf.LerpAngle(a.w, b.w, s), 0f);
                FocusAngles(owner, cam, pos, ref ang);
                return true;
            }

            var bot = FindBot(owner, cam.Target);
            if (bot == null) return false;
            var eye = bot.eyes.position;
            float yaw = bot.viewAngles.y;

            switch (cam.Mode)
            {
                case 1: // глаза бота
                    pos = eye + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 0.2f;
                    ang = bot.viewAngles;
                    return true;
                case 2: // за спиной
                {
                    var back = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * -cam.Dist;
                    pos = eye + back + Vector3.up * (cam.Height - 1.5f) + Quaternion.Euler(0f, yaw, 0f) * Vector3.right * 0.6f;
                    ang = LookAngles(pos, eye + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 6f);
                    return true;
                }
                case 3: // орбита
                {
                    float a = t * cam.OrbitSpeed * Mathf.Deg2Rad;
                    pos = eye + new Vector3(Mathf.Sin(a) * cam.Dist, cam.Height - 1.5f, Mathf.Cos(a) * cam.Dist);
                    ang = LookAngles(pos, eye);
                    return true;
                }
                default: // 4: камера стоит в первой точке пути и следит за ботом
                {
                    if (cam.Keys.Count == 0) return false;
                    var k0 = cam.Keys.OrderBy(q => q.t).First();
                    pos = new Vector3(k0.x, k0.y, k0.z);
                    ang = LookAngles(pos, eye);
                    return true;
                }
            }
        }

        private BaseEntity _camMounting;   // стул камеры, на который сейчас сажаем

        private void StartCamera(BasePlayer player)
        {
            var id = Id(player);
            if (_cameras.ContainsKey(id) || _recorders.ContainsKey(id)) return;
            var owner = OwnerOf(id);
            var data = GetData(owner);
            var cam = data.Scene.Camera;

            if (GetPlayback(owner) == null) StartPlayback(owner, -1, -1, data.Seek, data.GlobalSpeed, false);

            Vector3 pos, ang;
            if (!ComputeCamera(owner, cam, CurrentTime(owner), out pos, out ang))
            {
                Reply(player, cam.Mode == 0 || cam.Mode == 4 ? "CamNoKeys" : "CamNoBot");
                return;
            }
            if (player.isMounted) { Reply(player, "CamDismount"); return; }

            var chair = SpawnProp(owner, ChairPrefab, pos - Vector3.up * _config.CameraEyeOffset, Quaternion.Euler(0f, ang.y, 0f), true);
            if (chair == null) { Reply(player, "CamFail"); return; }

            var returnPos = player.transform.position;
            _camMounting = chair;
            bool mounted = TryCall(chair, "MountPlayer", player) && player.GetMounted() != null;
            _camMounting = null;
            if (!mounted)
            {
                KillEntity(chair);
                Reply(player, "CamFail");
                return;
            }

            var driver = chair.gameObject.AddComponent<CameraDriver>();
            driver.Viewer = player;
            driver.Chair = chair;
            driver.Owner = owner;
            driver.ReturnPos = returnPos;
            _cameras[id] = driver;
            CloseUi(player);
            Reply(player, "CamOn");
        }

        private void StopCamera(BasePlayer player, bool reply)
        {
            if (player == null) return;
            var id = Id(player);
            CameraDriver cam;
            if (!_cameras.TryGetValue(id, out cam)) return;
            _cameras.Remove(id);
            cam.Stopping = true;

            var chair = cam.Chair;
            if (chair != null && !chair.IsDestroyed)
            {
                if (player.GetMounted() != null && !TryCall(chair, "DismountPlayer", player, true)) TryCall(chair, "DismountPlayer", player);
                _propSet.Remove(chair);
                KillEntity(chair);
            }
            if (player.IsConnected && !player.IsDead())
            {
                player.Teleport(cam.ReturnPos);
                if (reply) Reply(player, "CamOff");
            }
        }

        #endregion

        #region Монтаж дублей и сцены

        private void PushUndo(ulong owner)
        {
            List<string> stack;
            if (!_undo.TryGetValue(owner, out stack)) _undo[owner] = stack = new List<string>();
            var d = GetData(owner);
            stack.Add(d.Current + "|" + JsonConvert.SerializeObject(d.Scene));
            while (stack.Count > Mathf.Max(1, _config.UndoSteps)) stack.RemoveAt(0);
        }

        private bool Undo(ulong owner)
        {
            List<string> stack;
            if (!_undo.TryGetValue(owner, out stack) || stack.Count == 0) return false;
            var raw = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            int sep = raw.IndexOf('|');
            int index;
            if (sep < 0 || !int.TryParse(raw.Substring(0, sep), out index)) return false;
            var scene = JsonConvert.DeserializeObject<Scene>(raw.Substring(sep + 1));
            if (scene == null) return false;
            Normalize(scene);
            var d = GetData(owner);
            StopPlayback(owner);
            if (index >= 0 && index < d.Scenes.Count) d.Scenes[index] = scene;
            else d.Scenes.Add(scene);
            d.Current = Mathf.Clamp(index, 0, d.Scenes.Count - 1);
            MarkDirty(owner);
            return true;
        }

        private int UndoCount(ulong owner)
        {
            List<string> stack;
            return _undo.TryGetValue(owner, out stack) ? stack.Count : 0;
        }

        private static void TrimStart(Take take, float sec)
        {
            if (take.Frames.Count < 3 || sec <= 0f) return;
            sec = Mathf.Min(sec, take.Duration - 0.2f);
            if (sec <= 0f) return;
            take.Frames.RemoveAll(f => f.t < sec);
            foreach (var f in take.Frames) f.t -= sec;
            take.Events.RemoveAll(e => e.t < sec);
            foreach (var e in take.Events) e.t -= sec;
            take.Voice.RemoveAll(v => v.t < sec);
            foreach (var v in take.Voice) v.t -= sec;
            if (take.DieAt >= 0f) take.DieAt = Mathf.Max(0f, take.DieAt - sec);
            take.Duration = take.Frames[take.Frames.Count - 1].t;
        }

        private static void TrimEnd(Take take, float sec)
        {
            if (take.Frames.Count < 3 || sec <= 0f) return;
            float end = Mathf.Max(0.2f, take.Duration - sec);
            take.Frames.RemoveAll(f => f.t > end);
            take.Events.RemoveAll(e => e.t > end);
            take.Voice.RemoveAll(v => v.t > end);
            take.Duration = take.Frames[take.Frames.Count - 1].t;
        }

        // Перенос / поворот / зеркало относительно точки pivot
        private static Vector3 Xf(Vector3 p, Vector3 pivot, Vector3 offset, float angle, bool mirror, float axisYaw)
        {
            var q = p - pivot;
            if (mirror)
            {
                var n = Quaternion.Euler(0f, axisYaw + 90f, 0f) * Vector3.forward;
                q = q - n * (2f * Vector3.Dot(q, n));
            }
            q = Quaternion.Euler(0f, angle, 0f) * q;
            return pivot + q + offset;
        }

        private static float XfYaw(float yaw, float angle, bool mirror, float axisYaw)
        {
            if (mirror) yaw = 2f * axisYaw - yaw;
            return Mathf.Repeat(yaw + angle, 360f);
        }

        private static void TransformTake(Take take, Vector3 pivot, Vector3 offset, float angle, bool mirror, float axisYaw)
        {
            foreach (var f in take.Frames)
            {
                var p = Xf(new Vector3(f.x, f.y, f.z), pivot, offset, angle, mirror, axisYaw);
                f.x = p.x; f.y = p.y; f.z = p.z;
                f.w = XfYaw(f.w, angle, mirror, axisYaw);
                if (f.v >= 0)
                {
                    var vp = Xf(new Vector3(f.vx, f.vy, f.vz), pivot, offset, angle, mirror, axisYaw);
                    f.vx = vp.x; f.vy = vp.y; f.vz = vp.z;
                    var e = new Quaternion(f.qx, f.qy, f.qz, f.qw).eulerAngles;
                    var nq = Quaternion.Euler(e.x, XfYaw(e.y, angle, mirror, axisYaw), mirror ? -e.z : e.z);
                    f.qx = nq.x; f.qy = nq.y; f.qz = nq.z; f.qw = nq.w;
                }
            }
            foreach (var ev in take.Events)
            {
                if (ev.e != EvThrow) continue;
                var p = Xf(new Vector3(ev.x, ev.y, ev.z), pivot, offset, angle, mirror, axisYaw);
                ev.x = p.x; ev.y = p.y; ev.z = p.z;
            }
        }

        private static void TransformScene(Scene scene, Vector3 pivot, Vector3 offset, float angle, bool mirror, float axisYaw)
        {
            foreach (var t in scene.Takes) TransformTake(t, pivot, offset, angle, mirror, axisYaw);
            foreach (var c in scene.Cues)
            {
                var p = Xf(new Vector3(c.x, c.y, c.z), pivot, offset, angle, mirror, axisYaw);
                c.x = p.x; c.y = p.y; c.z = p.z;
            }
            foreach (var k in scene.Camera.Keys)
            {
                var p = Xf(new Vector3(k.x, k.y, k.z), pivot, offset, angle, mirror, axisYaw);
                k.x = p.x; k.y = p.y; k.z = p.z;
                k.w = XfYaw(k.w, angle, mirror, axisYaw);
            }
        }

        private static Vector3 SceneAnchor(Scene scene)
        {
            foreach (var t in scene.Takes)
                if (t.Frames.Count > 0) return new Vector3(t.Frames[0].x, t.Frames[0].y, t.Frames[0].z);
            foreach (var k in scene.Camera.Keys) return new Vector3(k.x, k.y, k.z);
            return Vector3.zero;
        }

        private static Take CloneTake(Take t) => JsonConvert.DeserializeObject<Take>(JsonConvert.SerializeObject(t));

        #endregion

        #region Наборы инвентаря

        private bool CanKits(BasePlayer player) => player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermKits);

        private static void SnapshotContainer(ItemContainer cont, int index, List<KitItem> into)
        {
            if (cont == null) return;
            foreach (var item in cont.itemList)
            {
                var ki = new KitItem
                {
                    s = item.info.shortname,
                    k = item.skin,
                    a = item.amount,
                    c = index,
                    p = item.position,
                    n = string.IsNullOrEmpty(item.name) ? null : item.name
                };
                if (item.hasCondition) ki.h = item.condition;
                if (item.contents != null && item.contents.itemList.Count > 0)
                    ki.m = item.contents.itemList.Select(x => x.info.shortname).ToList();
                var gun = item.GetHeldEntity() as BaseProjectile;
                if (gun != null && gun.primaryMagazine != null)
                {
                    ki.am = gun.primaryMagazine.contents;
                    ki.at = gun.primaryMagazine.ammoType != null ? gun.primaryMagazine.ammoType.shortname : null;
                }
                into.Add(ki);
            }
        }

        private Kit SnapshotKit(BasePlayer player, string name)
        {
            var kit = new Kit { Name = name, Created = DateTime.UtcNow };
            SnapshotContainer(player.inventory.containerMain, 0, kit.Items);
            SnapshotContainer(player.inventory.containerBelt, 1, kit.Items);
            SnapshotContainer(player.inventory.containerWear, 2, kit.Items);
            return kit;
        }

        private static Item BuildKitItem(KitItem ki)
        {
            var item = ItemManager.CreateByName(ki.s, Mathf.Max(1, ki.a), ki.k);
            if (item == null) return null;
            if (!string.IsNullOrEmpty(ki.n)) item.name = ki.n;
            if (ki.h >= 0f && item.hasCondition) item.condition = Mathf.Min(ki.h, item.maxCondition);
            if (ki.m != null && item.contents != null)
            {
                foreach (var m in ki.m)
                {
                    var mod = ItemManager.CreateByName(m, 1, 0UL);
                    if (mod != null && !mod.MoveToContainer(item.contents)) mod.Remove();
                }
            }
            var gun = item.GetHeldEntity() as BaseProjectile;
            if (gun != null && gun.primaryMagazine != null && ki.am >= 0)
            {
                if (!string.IsNullOrEmpty(ki.at))
                {
                    var def = ItemManager.FindItemDefinition(ki.at);
                    if (def != null) gun.primaryMagazine.ammoType = def;
                }
                gun.primaryMagazine.contents = Mathf.Min(ki.am, gun.primaryMagazine.capacity);
            }
            return item;
        }

        // replace = очистить инвентарь перед выдачей
        private int ApplyKit(BasePlayer player, Kit kit, bool replace)
        {
            if (replace)
            {
                player.inventory.Strip();
                ItemManager.DoRemoves();
            }
            int given = 0;
            // одежда первой, чтобы рюкзак/слоты пояса успели появиться
            foreach (var ki in kit.Items.OrderByDescending(x => x.c))
            {
                var item = BuildKitItem(ki);
                if (item == null) continue;
                var cont = ki.c == 2 ? player.inventory.containerWear : ki.c == 1 ? player.inventory.containerBelt : player.inventory.containerMain;
                bool ok = (ki.p >= 0 && item.MoveToContainer(cont, ki.p, true)) || item.MoveToContainer(cont) || player.inventory.GiveItem(item);
                if (!ok) { item.Remove(); continue; }
                given++;
            }
            player.SendNetworkUpdateImmediate();
            return given;
        }

        private static string KitSummary(Kit kit)
        {
            int wear = kit.Items.Count(x => x.c == 2);
            return $"{kit.Items.Count} · {wear}";
        }

        #endregion

        #region Экспорт / импорт

        private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private string ExportScene(ulong owner)
        {
            var scene = GetData(owner).Scene;
            string code = "";
            for (int i = 0; i < 6; i++) code += CodeChars[UnityEngine.Random.Range(0, CodeChars.Length)];
            Interface.Oxide.DataFileSystem.WriteObject("VideoRecorder/shared/" + code, scene);
            return code;
        }

        private bool ImportScene(ulong owner, string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length != 6 || code.Any(ch => CodeChars.IndexOf(ch) < 0)) return false;
            Scene scene;
            try { scene = Interface.Oxide.DataFileSystem.ReadObject<Scene>("VideoRecorder/shared/" + code); }
            catch { return false; }
            if (scene == null || (scene.Takes.Count == 0 && scene.Camera.Keys.Count == 0)) return false;
            Normalize(scene);
            var d = GetData(owner);
            if (d.Scenes.Count >= _config.MaxScenes) return false;
            scene.Name = (scene.Name ?? "Scene") + " [" + code + "]";
            d.Scenes.Add(scene);
            d.Current = d.Scenes.Count - 1;
            MarkDirty(owner);
            return true;
        }

        #endregion

        #region ddraw: траектории, подписи, время суток

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

        private bool CanDraw(BasePlayer player) => player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermPaths);

        private static readonly Color[] PathColors =
        {
            new Color(0.9f, 0.3f, 0.3f), new Color(0.3f, 0.8f, 0.4f), new Color(0.3f, 0.6f, 1f),
            new Color(1f, 0.8f, 0.2f), new Color(0.8f, 0.4f, 1f), new Color(0.2f, 0.9f, 0.9f),
            new Color(1f, 0.5f, 0.1f), new Color(1f, 1f, 1f)
        };

        private void ShowPaths(BasePlayer player, int onlyIndex)
        {
            if (!CanDraw(player)) { Reply(player, "NoPathPerm"); return; }
            var scene = GetData(OwnerOf(Id(player))).Scene;
            float dur = _config.PathDuration;
            WithAdmin(player, () =>
            {
                for (int i = 0; i < scene.Takes.Count; i++)
                {
                    if (onlyIndex >= 0 ? i != onlyIndex : !scene.Takes[i].Enabled) continue;
                    var take = scene.Takes[i];
                    var col = PathColors[i % PathColors.Length];
                    var fr = take.Frames;
                    if (fr.Count < 2) continue;
                    int step = Mathf.Max(1, fr.Count / 150);
                    var prev = new Vector3(fr[0].x, fr[0].y + 0.1f, fr[0].z);
                    for (int j = step; j < fr.Count; j += step)
                    {
                        var cur = new Vector3(fr[j].x, fr[j].y + 0.1f, fr[j].z);
                        player.SendConsoleCommand("ddraw.line", dur, col, prev, cur);
                        prev = cur;
                    }
                    var last = fr[fr.Count - 1];
                    player.SendConsoleCommand("ddraw.text", dur, col, new Vector3(fr[0].x, fr[0].y + 2f, fr[0].z), $"<size=18>{i + 1}. {take.Name}</size>");
                    player.SendConsoleCommand("ddraw.text", dur, col, new Vector3(last.x, last.y + 2f, last.z), $"<size=14>{i + 1}. END</size>");
                    foreach (var ev in take.Events)
                    {
                        if (ev.e != EvFire) continue;
                        var f = fr[Mathf.Clamp(FrameAt(take, ev.t), 0, fr.Count - 1)];
                        player.SendConsoleCommand("ddraw.sphere", dur, col, new Vector3(f.x, f.y + 1.5f, f.z), 0.08f);
                    }
                }
                if (onlyIndex < 0)
                {
                    var keys = scene.Camera.Keys.OrderBy(k => k.t).ToList();
                    for (int j = 0; j < keys.Count; j++)
                    {
                        var kp = new Vector3(keys[j].x, keys[j].y, keys[j].z);
                        player.SendConsoleCommand("ddraw.sphere", dur, Color.white, kp, 0.3f);
                        player.SendConsoleCommand("ddraw.text", dur, Color.white, kp + Vector3.up * 0.5f, $"CAM {j + 1} · {Time2(keys[j].t)}");
                        if (j > 0) player.SendConsoleCommand("ddraw.arrow", dur, Color.white, new Vector3(keys[j - 1].x, keys[j - 1].y, keys[j - 1].z), kp, 0.3f);
                    }
                    foreach (var c in scene.Cues)
                    {
                        var cp = new Vector3(c.x, c.y, c.z);
                        player.SendConsoleCommand("ddraw.sphere", dur, new Color(1f, 0.5f, 0.1f), cp, 0.5f);
                        player.SendConsoleCommand("ddraw.text", dur, new Color(1f, 0.5f, 0.1f), cp + Vector3.up, $"FX {T(Id(player), FxTypes[Mathf.Clamp(c.k, 0, FxTypes.Length - 1)].Key)} · {Time2(c.t)}");
                    }
                }
            });
            Reply(player, "PathsShown", dur.ToString("0"));
        }

        private void DrawNametags(BasePlayer player, ulong owner)
        {
            List<BasePlayer> list;
            if (!_bots.TryGetValue(owner, out list) || list.Count == 0) return;
            WithAdmin(player, () =>
            {
                foreach (var b in list)
                {
                    if (b == null || b.IsDestroyed || b.IsDead()) continue;
                    player.SendConsoleCommand("ddraw.text", 0.55f, Color.white, b.transform.position + Vector3.up * 2.1f, $"<size=14>{b.displayName}</size>");
                }
            });
        }

        private void SetClientTime(BasePlayer player, float hour)
        {
            WithAdmin(player, () => player.SendConsoleCommand("admintime", hour));
        }

        private static int FrameAt(Take take, float t)
        {
            int lo = 0, hi = take.Frames.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (take.Frames[mid].t < t) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        private static readonly string[] Weathers = { "Clear", "Overcast", "Fog", "RainMild", "RainHeavy", "Storm" };

        #endregion

        #region Погода и время суток

        private class WeatherPreset
        {
            public string Key;
            public float Time;
            public int Clouds, Fog, Rain, Wind, Thunder, Rainbow, Brightness, Contrast;
        }

        private static readonly WeatherPreset[] WeatherPresets =
        {
            new WeatherPreset { Key = "PrNoon",     Time = 12f,   Clouds = 0,   Fog = 0,   Rain = 0,   Wind = 10,  Thunder = 0,   Rainbow = 0,   Brightness = 100, Contrast = 100 },
            new WeatherPreset { Key = "PrGolden",   Time = 18.5f, Clouds = 20,  Fog = 5,   Rain = 0,   Wind = 10,  Thunder = 0,   Rainbow = 0,   Brightness = 110, Contrast = 110 },
            new WeatherPreset { Key = "PrDawnFog",  Time = 6f,    Clouds = 30,  Fog = 70,  Rain = 0,   Wind = 5,   Thunder = 0,   Rainbow = 0,   Brightness = 90,  Contrast = 90 },
            new WeatherPreset { Key = "PrOvercast", Time = 13f,   Clouds = 90,  Fog = 10,  Rain = 0,   Wind = 30,  Thunder = 0,   Rainbow = 0,   Brightness = 80,  Contrast = 90 },
            new WeatherPreset { Key = "PrRain",     Time = 15f,   Clouds = 100, Fog = 30,  Rain = 100, Wind = 60,  Thunder = 0,   Rainbow = 0,   Brightness = 70,  Contrast = 90 },
            new WeatherPreset { Key = "PrStorm",    Time = 21f,   Clouds = 100, Fog = 30,  Rain = 100, Wind = 100, Thunder = 100, Rainbow = 0,   Brightness = 60,  Contrast = 110 },
            new WeatherPreset { Key = "PrBlizzard", Time = 14f,   Clouds = 100, Fog = 60,  Rain = 100, Wind = 100, Thunder = 0,   Rainbow = 0,   Brightness = 80,  Contrast = 80 },
            new WeatherPreset { Key = "PrHorror",   Time = 23f,   Clouds = 60,  Fog = 100, Rain = 0,   Wind = 0,   Thunder = 0,   Rainbow = 0,   Brightness = 50,  Contrast = 120 },
            new WeatherPreset { Key = "PrMoon",     Time = 0f,    Clouds = 10,  Fog = 0,   Rain = 0,   Wind = 10,  Thunder = 0,   Rainbow = 0,   Brightness = 100, Contrast = 100 },
            new WeatherPreset { Key = "PrRainbow",  Time = 16f,   Clouds = 50,  Fog = 0,   Rain = 20,  Wind = 20,  Thunder = 0,   Rainbow = 100, Brightness = 110, Contrast = 100 },
        };

        private bool _weatherTouched;

        private bool CanWorld(BasePlayer p) => p != null && (p.IsAdmin || (_config.AllowGlobalWeather && permission.UserHasPermission(p.UserIDString, PermWorld)));

        private static string Pct(int v, float scale) => v < 0 ? "-1" : (v / 100f * scale).ToString("0.##", CultureInfo.InvariantCulture);

        private static void ServerCmd(string cmd, string value)
        {
            ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), cmd, value);
        }

        private void ApplyPreset(WeatherSettings w, Scene scene, int index)
        {
            if (index < 0 || index >= WeatherPresets.Length) return;
            var p = WeatherPresets[index];
            w.Enabled = true;
            w.Preset = p.Key;
            w.Clouds = p.Clouds; w.Fog = p.Fog; w.Rain = p.Rain; w.Wind = p.Wind;
            w.Thunder = p.Thunder; w.Rainbow = p.Rainbow; w.Brightness = p.Brightness; w.Contrast = p.Contrast;
            scene.TimeOfDay = p.Time;
        }

        // Применяет погоду сцены. Погода в Rust общая на сервер; время можно только своей команде.
        private void ApplyWeather(BasePlayer actor, ulong owner, bool announce)
        {
            var scene = GetData(owner).Scene;
            var w = scene.Weather;

            if (scene.TimeOfDay >= 0f)
            {
                if (w.ServerTime && CanWorld(actor))
                {
                    ServerCmd("env.time", scene.TimeOfDay.ToString("0.##", CultureInfo.InvariantCulture));
                    ServerCmd("env.progresstime", w.FreezeTime ? "false" : "true");
                    _weatherTouched = true;
                }
                else
                    foreach (var p in TeamOnline(owner)) SetClientTime(p, scene.TimeOfDay);
            }

            if (!w.Enabled) return;
            if (!CanWorld(actor))
            {
                if (announce) Reply(actor, "NoWeather");
                return;
            }
            ServerCmd("weather.clouds", Pct(w.Clouds, 1f));
            ServerCmd("weather.fog", Pct(w.Fog, 1f));
            ServerCmd("weather.rain", Pct(w.Rain, 1f));
            ServerCmd("weather.wind", Pct(w.Wind, 1f));
            ServerCmd("weather.thunder", Pct(w.Thunder, 1f));
            ServerCmd("weather.rainbow", Pct(w.Rainbow, 1f));
            ServerCmd("weather.atmosphere_brightness", Pct(w.Brightness, 1f));
            ServerCmd("weather.atmosphere_contrast", Pct(w.Contrast, 1f));
            _weatherTouched = true;
            if (announce) Reply(actor, "WeatherApplied");
        }

        private void ResetWeather(BasePlayer actor, ulong owner)
        {
            if (CanWorld(actor))
            {
                foreach (var cv in new[] { "weather.clouds", "weather.fog", "weather.rain", "weather.wind", "weather.thunder", "weather.rainbow", "weather.atmosphere_brightness", "weather.atmosphere_contrast" })
                    ServerCmd(cv, "-1");
                ServerCmd("env.progresstime", "true");
                _weatherTouched = false;
            }
            foreach (var p in TeamOnline(owner)) SetClientTime(p, -1f);
        }

        #endregion

        #region Команды

        private bool HasAccess(BasePlayer player, bool silent = false)
        {
            if (player == null) return false;
            if (permission.UserHasPermission(player.UserIDString, PermUse)) return true;
            if (!silent) Reply(player, "NoAccess");
            return false;
        }

        private static float ParseFloat(string s, float def)
        {
            float v;
            return float.TryParse(s?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        private static string Clip(string s, int max) => s == null ? "" : (s.Length > max ? s.Substring(0, max) : s);

        private void TogglePlay(BasePlayer player)
        {
            var owner = OwnerOf(Id(player));
            if (_playbacks.ContainsKey(owner))
            {
                StopPlayback(owner);
                Reply(player, "SceneStopped");
            }
            else
            {
                var d = GetData(owner);
                StartPlayback(owner, -1, -1, d.Seek, d.GlobalSpeed, true);
            }
        }

        private void TogglePause(BasePlayer player)
        {
            var owner = OwnerOf(Id(player));
            var pb = GetPlayback(owner);
            if (pb == null)
            {
                var d = GetData(owner);
                StartPlayback(owner, -1, -1, d.Seek, d.GlobalSpeed, true);
                return;
            }
            if (pb.Paused) pb.Resume(); else pb.Pause();
        }

        private void ToggleSlowmo(BasePlayer player)
        {
            var owner = OwnerOf(Id(player));
            var pb = GetPlayback(owner);
            if (pb == null) { Reply(player, "NotPlaying"); return; }
            var d = GetData(owner);
            pb.Slowmo = !pb.Slowmo;
            pb.SetSpeed(pb.Slowmo ? d.SlowmoSpeed : d.GlobalSpeed);
        }

        private void ToggleCamera(BasePlayer player)
        {
            if (_cameras.ContainsKey(Id(player))) StopCamera(player, true);
            else StartCamera(player);
        }

        private Vector3 AimPoint(BasePlayer player, float max)
        {
            RaycastHit hit;
            var origin = player.eyes.position;
            var dir = player.eyes.HeadForward();
            if (Physics.Raycast(origin, dir, out hit, max, -5, QueryTriggerInteraction.Ignore)) return hit.point;
            return origin + dir * 10f;
        }

        // /ai off — ИИ Rust (учёные, животные) замирает и не реагирует на игроков; /ai on — как было; /ai — переключить. Только админ.
        // Боты съёмок — не ИИ, их это не касается. Состояние сохраняется в конфиге и держится после перезапуска.
        [ChatCommand("ai")]
        private void CmdAi(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) { player.ChatMessage("<color=#f0a6b1>[VR]</color> Только для админа. / Admin only."); return; }
            string a = args.Length > 0 ? args[0].ToLower() : "";
            _config.AiFrozen = a == "off" || a == "stop" || a == "выкл" ? true : a == "on" || a == "вкл" ? false : !_config.AiFrozen;
            SaveConfig();
            ApplyAi();
            player.ChatMessage(_config.AiFrozen
                ? "<color=#f0a6b1>[VR]</color> ИИ заморожен: учёные и животные стоят и не реагируют. / AI frozen."
                : "<color=#f0a6b1>[VR]</color> ИИ снова работает. / AI back on.");
        }

        private void ApplyAi()
        {
            bool f = _config.AiFrozen;
            ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "ai.think", f ? "false" : "true");
            ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "ai.move", f ? "false" : "true");
            ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), "ai.ignoreplayers", f ? "true" : "false");
        }

        // Чистый кадр: убрать плашки плагина (REC, пауза, время клипа справа сверху) и подписи ботов. Бинд: bind f4 vr.clean; чат: /vr clean
        [ConsoleCommand("vr.clean")]
        private void CmdClean(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (HasAccess(p)) ToggleClean(p);
        }

        private void ToggleClean(BasePlayer player)
        {
            var id = Id(player);
            var my = GetData(id);
            my.CleanFrame = !my.CleanFrame;
            if (my.CleanFrame) { my.Nametags = false; _hudShown.Remove(id); CuiHelper.DestroyUi(player, UiHud); CuiHelper.DestroyUi(player, UiPbHud); }
            MarkDirty(id);
            player.ChatMessage(my.CleanFrame ? "<color=#f0a6b1>[VR]</color> Чистый кадр: плашки скрыты." : "<color=#f0a6b1>[VR]</color> Плашки снова видны.");
        }

        // /cleanup — админ: убрать все трупы (люди, боты, животные), сумки и рюкзаки с трупов и после разделки,
        // вещи на земле, баррикады и высокие внешние стены. /cleanup 100 — только в радиусе 100 м вокруг себя.
        // Не трогает стены построек (BuildingBlock), живых ботов и защищённый реквизит сцен.
        [ChatCommand("cleanup")]
        private void CmdCleanup(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) { player.ChatMessage("<color=#f0a6b1>[VR]</color> Только для админа. / Admin only."); return; }
            float r = 0f;
            if (args.Length > 0) float.TryParse(args[0].Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r);
            var pos = player.transform.position;
            int corpses = 0, bags = 0, items = 0, walls = 0;
            foreach (var e in BaseNetworkable.serverEntities.OfType<BaseEntity>().ToList())
            {
                if (e == null || e.IsDestroyed || _propSet.Contains(e)) continue;
                if (r > 0f && (e.transform.position - pos).sqrMagnitude > r * r) continue;
                if (e is BaseCorpse) corpses++;
                else if (e is DroppedItemContainer) bags++;
                else if (e is DroppedItem) items++;
                else if (e is Barricade || e is SimpleBuildingBlock) walls++;
                else continue;
                e.Kill();
            }
            player.ChatMessage($"<color=#f0a6b1>[VR]</color> {(r > 0f ? $"В радиусе {r:0} м" : "По всей карте")}: трупов {corpses}, сумок {bags}, вещей на земле {items}, стенок {walls}.");
        }

        // /killbots — админ: убрать всех ботов (сцены всех игроков, превью и «потерянных» после сбоев).
        // Не трогает игроков, спящих, NPC хаба (7 700 000+), ботов AimTrain (8 800 000+) и учёных.
        [ChatCommand("killbots")]
        private void CmdKillBots(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin) { player.ChatMessage("<color=#f0a6b1>[VR]</color> Только для админа. / Admin only."); return; }
            int n = _botInfo.Count + _previews.Count;
            foreach (var owner in _playbacks.Keys.Concat(_bots.Keys).Distinct().ToList()) StopPlayback(owner);
            foreach (var bot in _botInfo.Keys.ToList()) KillEntity(bot);
            _botInfo.Clear();
            foreach (var id in _previews.Keys.ToList()) KillPreview(id);
            foreach (var p in BaseNetworkable.serverEntities.OfType<BasePlayer>().ToList())
            {
                if (p == null || p.IsDestroyed || p.IsConnected || p is NPCPlayer) continue;
                ulong uid = (ulong)p.userID;
                if (uid < (1UL << 32) || uid >= (4001UL << 32)) continue;   // только диапазон ID ботов VideoRecorder
                p.Kill();
                n++;
            }
            player.ChatMessage($"<color=#f0a6b1>[VR]</color> Убрано ботов: {n}. / Bots removed: {n}.");
        }

        [ChatCommand("vr")]
        private void CmdChat(BasePlayer player, string command, string[] args)
        {
            if (!HasAccess(player)) return;
            var id = Id(player);
            var owner = OwnerOf(id);
            var data = GetData(owner);
            var sub = args.Length > 0 ? args[0].ToLower() : "";
            string rest = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()) : "";

            switch (sub)
            {
                case "":
                    OpenUi(player);
                    break;
                case "rec":
                    StartRecordAuto(player);
                    break;
                case "stop":
                    if (LeaderStop(player)) break;
                    if (_countdowns.ContainsKey(id)) CancelCountdown(id);
                    else StopRecording(player, true);
                    break;
                case "play":
                    StartPlayback(owner, -1, -1, data.Seek, data.GlobalSpeed, true);
                    break;
                case "pause":
                    TogglePause(player);
                    break;
                case "end":
                case "clear":
                    StopPlayback(owner);
                    Reply(player, "SceneStopped");
                    break;
                case "slowmo":
                    ToggleSlowmo(player);
                    break;
                case "cam":
                    ToggleCamera(player);
                    break;
                case "clean":
                    ToggleClean(player);
                    break;
                case "seek":
                    SeekTo(owner, ParseFloat(args.Length > 1 ? args[1] : null, 0f), false);
                    Reply(player, "SeekSet", data.Seek.ToString("0.0"));
                    break;
                case "undo":
                    Reply(player, Undo(owner) ? "Undone" : "NothingToUndo");
                    break;
                case "rename":
                {
                    int n;
                    if (args.Length < 3 || !int.TryParse(args[1], out n) || n < 1 || n > data.Scene.Takes.Count)
                    {
                        Reply(player, "UsageRename");
                        return;
                    }
                    data.Scene.Takes[n - 1].Name = Clip(string.Join(" ", args.Skip(2).ToArray()), 32);
                    MarkDirty(owner);
                    Reply(player, "Done");
                    break;
                }
                case "scene":
                    if (rest.Length == 0) { Reply(player, "UsageScene"); return; }
                    data.Scene.Name = Clip(rest, 32);
                    MarkDirty(owner);
                    Reply(player, "Done");
                    break;
                case "paths":
                    ShowPaths(player, -1);
                    break;
                case "export":
                    Reply(player, "Exported", ExportScene(owner));
                    break;
                case "import":
                    Reply(player, ImportScene(owner, rest) ? "Imported" : "ImportFail");
                    break;
                case "move":
                {
                    int a, b;
                    if (args.Length < 3 || !int.TryParse(args[1], out a) || !int.TryParse(args[2], out b) ||
                        a < 1 || b < 1 || a > data.Scene.Takes.Count || b > data.Scene.Takes.Count) { Reply(player, "UsageMove"); return; }
                    var t = data.Scene.Takes[a - 1];
                    data.Scene.Takes.RemoveAt(a - 1);
                    data.Scene.Takes.Insert(b - 1, t);
                    MarkDirty(owner);
                    Reply(player, "Done");
                    break;
                }
                case "kit":
                {
                    if (!CanKits(player)) { Reply(player, "NoKitPerm"); return; }
                    var my2 = GetData(id);
                    var op = args.Length > 1 ? args[1].ToLower() : "list";
                    var kname = args.Length > 2 ? Clip(string.Join(" ", args.Skip(2).ToArray()), 32) : "";
                    var found = my2.Kits.FirstOrDefault(k => string.Equals(k.Name, kname, StringComparison.OrdinalIgnoreCase));
                    switch (op)
                    {
                        case "save":
                            if (kname.Length == 0) { Reply(player, "UsageKit"); return; }
                            if (found == null && my2.Kits.Count >= _config.MaxKits) { Reply(player, "KitLimit", _config.MaxKits); return; }
                            var snap = SnapshotKit(player, kname);
                            if (found != null) my2.Kits[my2.Kits.IndexOf(found)] = snap; else my2.Kits.Add(snap);
                            MarkDirty(id);
                            Reply(player, "KitSaved", kname, snap.Items.Count);
                            break;
                        case "load":
                        case "add":
                            if (found == null) { Reply(player, "KitNotFound"); return; }
                            Reply(player, "KitLoaded", found.Name, ApplyKit(player, found, op == "load"));
                            break;
                        case "del":
                            if (found == null) { Reply(player, "KitNotFound"); return; }
                            my2.Kits.Remove(found);
                            MarkDirty(id);
                            Reply(player, "Done");
                            break;
                        case "wear":
                        {
                            // /vr kit wear <номер дубля> <набор>
                            int n;
                            var parts = kname.Split(new[] { ' ' }, 2);
                            if (parts.Length < 2 || !int.TryParse(parts[0], out n) || n < 1 || n > data.Scene.Takes.Count) { Reply(player, "UsageKitWear"); return; }
                            var wk = my2.Kits.FirstOrDefault(k => string.Equals(k.Name, parts[1], StringComparison.OrdinalIgnoreCase));
                            if (wk == null) { Reply(player, "KitNotFound"); return; }
                            PushUndo(owner);
                            data.Scene.Takes[n - 1].Wear = wk.Items.Where(x => x.c == 2).Select(x => new ItemInfo { s = x.s, k = x.k, m = x.m }).ToList();
                            MarkDirty(owner);
                            Reply(player, "KitWearDone", wk.Name, n);
                            break;
                        }
                        default:
                            Reply(player, "KitList", my2.Kits.Count == 0 ? "—" : string.Join(", ", my2.Kits.Select(k => k.Name).ToArray()));
                            break;
                    }
                    break;
                }
                case "invite":
                {
                    if (rest.Length == 0) { Ui(id).Tab = "team"; OpenUi(player); return; }
                    var target = BasePlayer.Find(rest);
                    if (target == null || target == player) { Reply(player, "PlayerNotFound"); return; }
                    SendInvite(player, target);
                    break;
                }
                case "join":
                    AcceptInvite(player);
                    break;
                case "leave":
                    LeaveTeam(id, true);
                    Reply(player, "Left");
                    break;
                case "team":
                    Ui(id).Tab = "team";
                    OpenUi(player);
                    break;
                case "motor":
                    StartGroupCountdown(player);
                    break;
                default:
                    Reply(player, "Help");
                    break;
            }
        }

        [ConsoleCommand("vr.rec")]
        private void CmdRec(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!HasAccess(player)) return;
            var id = Id(player);
            if (LeaderStop(player)) return;
            if (_recorders.ContainsKey(id)) StopRecording(player, true);
            else if (_countdowns.ContainsKey(id)) CancelCountdown(id);
            else if (_groupCountdowns.ContainsKey(OwnerOf(id))) CancelGroupCountdown(OwnerOf(id));
            else StartRecordAuto(player);
        }

        [ConsoleCommand("vr.motor")]
        private void CmdMotor(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!HasAccess(player)) return;
            var owner = OwnerOf(Id(player));
            if (_groupCountdowns.ContainsKey(owner)) CancelGroupCountdown(owner);
            else
            {
                var rec = _recorders.Values.FirstOrDefault(r => r.Owner == owner && r.Group != null);
                if (rec != null) StopGroup(rec.Group);
                else StartGroupCountdown(player);
            }
        }

        // ---------------- Приглашения и команда
        private void SendInvite(BasePlayer host, BasePlayer target)
        {
            var hid = Id(host);
            var tid = Id(target);
            if (_links.ContainsKey(hid)) { Reply(host, "GuestCantInvite"); return; }
            if (!HasAccess(target, true)) { Reply(host, "TargetNoAccess"); return; }
            if (OwnerOf(tid) == hid) { Reply(host, "AlreadyInTeam"); return; }
            _invites[tid] = new Invite { Owner = hid, At = Time.realtimeSinceStartup };
            Reply(host, "InviteSent", target.displayName);
            Reply(target, "InviteGot", host.displayName);
            ShowInvite(target);
            var t = target;
            timer.Once(_config.InviteSeconds, () =>
            {
                Invite inv;
                if (t != null && _invites.TryGetValue(Id(t), out inv) && inv.Owner == hid && Time.realtimeSinceStartup - inv.At >= _config.InviteSeconds - 0.5f)
                {
                    _invites.Remove(Id(t));
                    CuiHelper.DestroyUi(t, UiInvite);
                }
            });
            Refresh(host);
        }

        private void AcceptInvite(BasePlayer player)
        {
            var id = Id(player);
            Invite inv;
            if (!_invites.TryGetValue(id, out inv) || BasePlayer.FindByID(inv.Owner) == null) { Reply(player, "NoInvites"); CuiHelper.DestroyUi(player, UiInvite); return; }
            _invites.Remove(id);
            CuiHelper.DestroyUi(player, UiInvite);
            if (_recorders.ContainsKey(id)) StopRecording(player, true);
            StopPlayback(id);
            LeaveTeam(id, true);
            _links[id] = inv.Owner;
            var team = Team(inv.Owner);
            team.RemoveAll(m => m.Id == id);
            team.Add(new Member { Id = id, Actor = inv.Actor, Ready = true });
            Reply(player, "Joined");
            var host = BasePlayer.FindByID(inv.Owner);
            if (host != null) { Reply(host, "GuestJoined", player.displayName); Refresh(host); }
            Ui(id).Tab = "team";
            OpenUi(player);
        }

        [ConsoleCommand("vr.team")]
        private void CmdTeam(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!HasAccess(player)) return;
            var id = Id(player);
            var action = arg.GetString(0);
            ulong target;
            ulong.TryParse(arg.GetString(1), out target);
            Invite inv;

            switch (action)
            {
                case "accept":
                    AcceptInvite(player);
                    return;
                case "decline":
                    if (_invites.TryGetValue(id, out inv))
                    {
                        _invites.Remove(id);
                        var host = BasePlayer.FindByID(inv.Owner);
                        if (host != null) { Reply(host, "InviteDeclined", player.displayName); Refresh(host); }
                    }
                    CuiHelper.DestroyUi(player, UiInvite);
                    return;
                case "role":
                    if (_invites.TryGetValue(id, out inv)) { inv.Actor = arg.GetString(1) != "cam"; ShowInvite(player); }
                    return;
                case "invite":
                {
                    var t = BasePlayer.FindByID(target);
                    if (t != null) SendInvite(player, t);
                    return;
                }
                case "kick":
                    if (OwnerOf(target) == id && target != id)
                    {
                        LeaveTeam(target, false);
                        var t = BasePlayer.FindByID(target);
                        if (t != null) { Reply(t, "Kicked"); Refresh(t); }
                    }
                    break;
                case "actor":
                {
                    var owner = OwnerOf(id);
                    var m = Team(owner).FirstOrDefault(x => x.Id == (target == 0 ? id : target));
                    if (m != null && (m.Id == id || owner == id)) m.Actor = !m.Actor;
                    foreach (var p in TeamOnline(owner)) if (p != player) Refresh(p);
                    break;
                }
                case "ready":
                {
                    var m = MemberOf(id);
                    if (m != null) m.Ready = !m.Ready;
                    foreach (var p in TeamOnline(OwnerOf(id))) if (p != player) Refresh(p);
                    break;
                }
                case "leave":
                    LeaveTeam(id, true);
                    Reply(player, "Left");
                    break;
                case "motor":
                    StartGroupCountdown(player);
                    return;
            }
            OpenUi(player);
        }

        [ConsoleCommand("vr.play")]
        private void CmdPlay(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!HasAccess(player)) return;
            TogglePlay(player);
            Refresh(player);
        }

        [ConsoleCommand("vr.pause")]
        private void CmdPause(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!HasAccess(player)) return;
            TogglePause(player);
            Refresh(player);
        }

        [ConsoleCommand("vr.slowmo")]
        private void CmdSlowmo(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!HasAccess(player)) return;
            ToggleSlowmo(player);
            Refresh(player);
        }

        [ConsoleCommand("vr.cam")]
        private void CmdCam(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!HasAccess(player)) return;
            ToggleCamera(player);
        }

        private void Refresh(BasePlayer player)
        {
            if (_uiOpen.Contains(Id(player))) OpenUi(player);
        }

        [ConsoleCommand("vr.ui")]
        private void CmdUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!HasAccess(player)) return;
            var id = Id(player);
            var owner = OwnerOf(id);
            var data = GetData(owner);
            var my = GetData(id);
            var scene = data.Scene;
            var cam = scene.Camera;
            var ui = Ui(id);
            var action = arg.Args != null && arg.Args.Length > 0 ? arg.Args[0].ToString() : "";
            int idx = arg.GetInt(1, -1);
            bool valid = idx >= 0 && idx < scene.Takes.Count;
            Take sel = ui.Selected >= 0 && ui.Selected < scene.Takes.Count ? scene.Takes[ui.Selected] : null;
            string rest = arg.Args != null && arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1).ToArray()).Trim() : "";
            float num = ParseFloat(rest, 0f);
            float yaw = player.eyes.rotation.eulerAngles.y;

            if (action != "scenemenu" && action != "sceneactions" && action != "delscene") { ui.SceneMenu = false; ui.SceneActions = false; }

            switch (action)
            {
                // ---------- общее
                case "scenemenu":
                    ui.SceneMenu = !ui.SceneMenu; ui.SceneActions = false;
                    break;
                case "sceneactions":
                    ui.SceneActions = !ui.SceneActions; ui.SceneMenu = false;
                    break;
                case "scenepick":
                    if (idx >= 0 && idx < data.Scenes.Count && idx != data.Current)
                    {
                        StopPlayback(owner);
                        data.Current = idx;
                        ui.Page = 0; ui.Selected = -1;
                    }
                    break;
                case "enableall":
                {
                    bool any = scene.Takes.Any(t => !t.Enabled);
                    foreach (var t in scene.Takes) t.Enabled = any;
                    break;
                }
                case "takemore":
                    if (valid)
                    {
                        if (ui.ConfirmTake == idx && Time.realtimeSinceStartup - ui.ConfirmTakeAt < 5f)
                        {
                            PushUndo(owner);
                            scene.Takes.RemoveAt(idx);
                            ui.ConfirmTake = -1;
                            ui.Selected = -1;
                        }
                        else { ui.ConfirmTake = idx; ui.ConfirmTakeAt = Time.realtimeSinceStartup; }
                    }
                    break;
                case "gspeedcycle":
                {
                    float[] sp = { 0.5f, 1f, 1.5f, 2f };
                    int cur = Array.FindIndex(sp, v => Mathf.Abs(v - data.GlobalSpeed) < 0.01f);
                    data.GlobalSpeed = sp[(cur + 1) % sp.Length];
                    var pbs = GetPlayback(owner);
                    if (pbs != null && !pbs.Slowmo) pbs.SetSpeed(data.GlobalSpeed);
                    break;
                }
                case "t.speedset":
                {
                    float[] sp = { 0.5f, 1f, 1.5f, 2f };
                    if (sel != null) sel.Speed = sp[Mathf.Clamp(arg.GetInt(1, 1), 0, 3)];
                    break;
                }
                case "t.healthset":
                    if (sel != null) sel.Health = Mathf.Clamp(Mathf.Round(num * 100f), 1f, 100f);
                    break;
                case "todset":
                    scene.TimeOfDay = Mathf.Round(Mathf.Clamp01(num) * 24f * 4f) / 4f % 24f;
                    scene.Weather.Preset = null;
                    ApplyWeather(player, owner, false);
                    break;
                case "w.servertimeset":
                    scene.Weather.ServerTime = arg.GetInt(1, 0) == 1;
                    ApplyWeather(player, owner, false);
                    break;
                case "w.freezeset":
                    scene.Weather.FreezeTime = arg.GetInt(1, 0) == 0;
                    ApplyWeather(player, owner, false);
                    break;
                case "w.setp":
                {
                    // w.setp <параметр> <доля 0..1>
                    var w = scene.Weather;
                    var param = arg.Args.Length > 1 ? arg.Args[1].ToString() : "";
                    float f = Mathf.Clamp01(ParseFloat(arg.Args.Length > 2 ? arg.Args[2].ToString() : "0", 0f));
                    int max = param == "bright" || param == "contrast" ? 200 : 100;
                    int v = Mathf.RoundToInt(f * max / 5f) * 5;
                    switch (param)
                    {
                        case "clouds": w.Clouds = v; break;
                        case "fog": w.Fog = v; break;
                        case "rain": w.Rain = v; break;
                        case "wind": w.Wind = v; break;
                        case "thunder": w.Thunder = v; break;
                        case "rainbow": w.Rainbow = v; break;
                        case "bright": w.Brightness = v; break;
                        case "contrast": w.Contrast = v; break;
                    }
                    w.Enabled = true;
                    w.Preset = null;
                    ApplyWeather(player, owner, false);
                    break;
                }
                case "cam.modeset": cam.Mode = Mathf.Clamp(arg.GetInt(1, 0), 0, 4); break;
                case "cam.smooth": cam.Smooth = Mathf.Clamp01(Mathf.Round(num * 20f) / 20f); break;
                case "cam.showpath":
                    if (Time.realtimeSinceStartup < ui.CamPathUntil) ui.CamPathUntil = 0f;
                    else { ShowPaths(player, -1); ui.CamPathUntil = Time.realtimeSinceStartup + _config.PathDuration; }
                    break;
                case "fx.typeset": ui.FxType = Mathf.Clamp(arg.GetInt(1, 0), 0, FxTypes.Length - 1); break;
                case "fx.page":
                    ui.FxPage = Mathf.Clamp(ui.FxPage + arg.GetInt(1, 0), 0, Mathf.Max(0, (scene.Cues.Count - 1) / 3));
                    break;
                case "kit.more":
                    if (ui.ConfirmKit == idx && Time.realtimeSinceStartup - ui.ConfirmKitAt < 15f) ui.ConfirmKit = -1;
                    else { ui.ConfirmKit = idx; ui.ConfirmKitAt = Time.realtimeSinceStartup; }
                    break;
                case "binds":
                {
                    string b = "bind f5 vr.cam; bind f6 vr.ui; bind f7 vr.play; bind f8 vr.rec; bind f9 vr.pause; bind f10 vr.slowmo; bind f11 vr.motor";
                    player.ConsoleMessage(b);
                    foreach (var line in b.Split(';')) player.ConsoleMessage(line.Trim());
                    Reply(player, "BindsCopied");
                    return;
                }
                case "":
                    if (_uiOpen.Contains(id)) CloseUi(player); else OpenUi(player);
                    return;
                case "close":
                    CloseUi(player);
                    return;
                case "tab":
                    ui.Tab = rest;
                    if (rest == "takes") ui.Selected = -1;
                    break;
                case "undo":
                    if (!Undo(owner)) Reply(player, "NothingToUndo");
                    ui.Selected = -1;
                    break;

                // ---------- запись
                case "record":
                    StartRecordAuto(player);
                    return;
                case "rerec":
                    if (valid) StartCountdown(player, idx, -1);
                    return;

                // ---------- транспорт сцены
                case "playall":
                    StartPlayback(owner, -1, -1, data.Seek, data.GlobalSpeed, true);
                    break;
                case "play":
                    if (valid) StartPlayback(owner, idx, -1, 0f, data.GlobalSpeed, true);
                    break;
                case "pause":
                    TogglePause(player);
                    break;
                case "end":
                    StopPlayback(owner);
                    break;
                case "tostart":
                    data.Seek = 0f;
                    StartPlayback(owner, -1, -1, 0f, data.GlobalSpeed, false);
                    break;
                case "seekto":
                    SeekTo(owner, num, true);
                    break;
                case "seek":
                    SeekTo(owner, rest == "0" ? 0f : data.Seek + num, true);
                    break;
                case "gspeed":
                {
                    data.GlobalSpeed = Mathf.Clamp(Mathf.Round((data.GlobalSpeed + num) * 100f) / 100f, 0.25f, 3f);
                    var pb = GetPlayback(owner);
                    if (pb != null && !pb.Slowmo) pb.SetSpeed(data.GlobalSpeed);
                    break;
                }
                case "slowmo":
                    ToggleSlowmo(player);
                    break;
                case "cam":
                    ToggleCamera(player);
                    if (_cameras.ContainsKey(id)) return;
                    break;

                // ---------- дубли
                case "toggle":
                    if (valid) scene.Takes[idx].Enabled = !scene.Takes[idx].Enabled;
                    break;
                case "delete":
                    if (valid)
                    {
                        PushUndo(owner);
                        scene.Takes.RemoveAt(idx);
                        ui.Selected = -1;
                    }
                    break;
                case "settings":
                    if (valid) { ui.Selected = idx; ui.LookView = false; }
                    break;
                case "back":
                    ui.Selected = -1;
                    ui.LookView = false;
                    KillPreview(id);
                    break;
                case "page":
                    ui.Page = Mathf.Max(0, ui.Page + arg.GetInt(1, 0));
                    break;

                // ---------- настройки дубля
                case "t.name":
                    if (sel != null && rest.Length > 0) sel.Name = Clip(rest, 32);
                    break;
                case "t.delay":
                    if (sel != null) sel.Delay = Mathf.Clamp(Mathf.Round((sel.Delay + num) * 10f) / 10f, 0f, 3600f);
                    break;
                case "t.speed":
                    if (sel != null) sel.Speed = Mathf.Clamp(Mathf.Round((sel.Speed + num) * 100f) / 100f, 0.25f, 3f);
                    break;
                case "t.health":
                    if (sel != null) sel.Health = Mathf.Clamp(sel.Health + num, 1f, 100f);
                    break;
                case "t.die":
                    if (sel != null)
                    {
                        if (rest == "off") sel.DieAt = -1f;
                        else sel.DieAt = Mathf.Clamp(Mathf.Round(((sel.DieAt < 0f ? sel.Duration : sel.DieAt) + num) * 10f) / 10f, 0f, sel.Duration);
                    }
                    break;
                case "t.loop": if (sel != null) sel.Loop = !sel.Loop; break;
                case "t.damage": if (sel != null) sel.Damage = !sel.Damage; break;
                case "t.god": if (sel != null) sel.God = !sel.God; break;
                case "t.voice": if (sel != null) sel.PlayVoice = !sel.PlayVoice; break;
                case "t.enabled": if (sel != null) sel.Enabled = !sel.Enabled; break;
                case "t.dup":
                    if (sel != null && scene.Takes.Count < _config.MaxTakes)
                    {
                        PushUndo(owner);
                        var copy = CloneTake(sel);
                        copy.Name = Clip(sel.Name + " 2", 32);
                        scene.Takes.Insert(ui.Selected + 1, copy);
                        ui.Selected++;
                    }
                    break;
                case "t.up":
                    if (sel != null && ui.Selected > 0)
                    {
                        scene.Takes.RemoveAt(ui.Selected);
                        scene.Takes.Insert(--ui.Selected, sel);
                    }
                    break;
                case "t.down":
                    if (sel != null && ui.Selected < scene.Takes.Count - 1)
                    {
                        scene.Takes.RemoveAt(ui.Selected);
                        scene.Takes.Insert(++ui.Selected, sel);
                    }
                    break;
                case "t.tp":
                    if (sel != null && sel.Frames.Count > 0) { var f = sel.Frames[0]; player.Teleport(new Vector3(f.x, f.y, f.z)); }
                    break;
                case "t.tpend":
                    if (sel != null && sel.Frames.Count > 0) { var f = sel.Frames[sel.Frames.Count - 1]; player.Teleport(new Vector3(f.x, f.y, f.z)); }
                    break;
                case "t.path":
                    if (sel != null) ShowPaths(player, ui.Selected);
                    break;
                case "t.rerec":
                    if (sel != null) StartCountdown(player, ui.Selected, -1);
                    return;
                case "t.append":
                    if (sel != null) StartCountdown(player, -1, ui.Selected);
                    return;
                case "t.trims":
                    if (sel != null) { PushUndo(owner); TrimStart(sel, num); }
                    break;
                case "t.trime":
                    if (sel != null) { PushUndo(owner); TrimEnd(sel, num); }
                    break;
                case "t.movehere":
                    if (sel != null && sel.Frames.Count > 0)
                    {
                        PushUndo(owner);
                        var f0 = sel.Frames[0];
                        TransformTake(sel, Vector3.zero, player.transform.position - new Vector3(f0.x, f0.y, f0.z), 0f, false, 0f);
                    }
                    break;
                case "t.rot":
                    if (sel != null && sel.Frames.Count > 0)
                    {
                        PushUndo(owner);
                        var f0 = sel.Frames[0];
                        TransformTake(sel, new Vector3(f0.x, f0.y, f0.z), Vector3.zero, num, false, 0f);
                    }
                    break;
                case "t.mirror":
                    if (sel != null) { PushUndo(owner); TransformTake(sel, player.transform.position, Vector3.zero, 0f, true, yaw); }
                    break;

                // ---------- сцены
                case "prev":
                    data.Current = data.Current <= 0 ? data.Scenes.Count - 1 : data.Current - 1;
                    StopPlayback(owner);
                    ui.Page = 0; ui.Selected = -1;
                    break;
                case "next":
                    data.Current = data.Current >= data.Scenes.Count - 1 ? 0 : data.Current + 1;
                    StopPlayback(owner);
                    ui.Page = 0; ui.Selected = -1;
                    break;
                case "newscene":
                    if (data.Scenes.Count >= _config.MaxScenes) { Reply(player, "SceneLimit"); break; }
                    StopPlayback(owner);
                    data.Scenes.Add(new Scene { Name = T(id, "SceneN", data.Scenes.Count + 1) });
                    data.Current = data.Scenes.Count - 1;
                    ui.Page = 0; ui.Selected = -1;
                    break;
                case "dupscene":
                    if (data.Scenes.Count >= _config.MaxScenes) { Reply(player, "SceneLimit"); break; }
                    {
                        var copy = JsonConvert.DeserializeObject<Scene>(JsonConvert.SerializeObject(scene));
                        Normalize(copy);
                        copy.Name = Clip(scene.Name + " 2", 32);
                        data.Scenes.Insert(data.Current + 1, copy);
                        data.Current++;
                    }
                    break;
                case "scenename":
                    if (rest.Length > 0) scene.Name = Clip(rest, 32);
                    break;
                case "delscene":
                    if (Time.realtimeSinceStartup - ui.ConfirmDeleteScene < 5f)
                    {
                        PushUndo(owner);
                        StopPlayback(owner);
                        data.Scenes.RemoveAt(data.Current);
                        data.Current = Mathf.Max(0, data.Current - 1);
                        ui.ConfirmDeleteScene = 0f;
                        ui.Page = 0; ui.Selected = -1;
                    }
                    else ui.ConfirmDeleteScene = Time.realtimeSinceStartup;
                    break;

                // ---------- маркеры
                case "marker.name":
                    ui.MarkerName = Clip(rest, 24);
                    break;
                case "marker.add":
                    scene.Markers.Add(new Marker { t = CurrentTime(owner), n = string.IsNullOrEmpty(ui.MarkerName) ? T(id, "MarkerN", scene.Markers.Count + 1) : ui.MarkerName });
                    scene.Markers.Sort((a, b) => a.t.CompareTo(b.t));
                    ui.MarkerName = "";
                    break;
                case "marker.go":
                    if (idx >= 0 && idx < scene.Markers.Count)
                    {
                        data.Seek = scene.Markers[idx].t;
                        StartPlayback(owner, -1, -1, data.Seek, data.GlobalSpeed, false);
                    }
                    break;
                case "marker.del":
                    if (idx >= 0 && idx < scene.Markers.Count) scene.Markers.RemoveAt(idx);
                    break;

                // ---------- вся сцена
                case "s.movehere":
                    PushUndo(owner);
                    StopPlayback(owner);
                    TransformScene(scene, Vector3.zero, player.transform.position - SceneAnchor(scene), 0f, false, 0f);
                    break;
                case "s.rot":
                    PushUndo(owner);
                    StopPlayback(owner);
                    TransformScene(scene, player.transform.position, Vector3.zero, num, false, 0f);
                    break;
                case "s.mirror":
                    PushUndo(owner);
                    StopPlayback(owner);
                    TransformScene(scene, player.transform.position, Vector3.zero, 0f, true, yaw);
                    break;
                case "export":
                {
                    var code = ExportScene(owner);
                    ui.LastCode = code;
                    Reply(player, "Exported", code);
                    break;
                }
                case "import.code":
                    ui.ImportCode = Clip(rest, 6);
                    break;
                case "import":
                    if (ImportScene(owner, ui.ImportCode)) { Reply(player, "Imported"); ui.ImportCode = ""; }
                    else Reply(player, "ImportFail");
                    break;
                case "tod":
                    scene.TimeOfDay = scene.TimeOfDay < 0f ? 12f : Mathf.Repeat(scene.TimeOfDay + num, 24f);
                    scene.Weather.Preset = null;
                    ApplyWeather(player, owner, false);
                    break;
                case "tod.reset":
                    scene.TimeOfDay = -1f;
                    foreach (var p in TeamOnline(owner)) SetClientTime(p, -1f);
                    break;
                case "weather":
                    ui.Tab = "weather";
                    break;

                // ---------- погода
                case "w.preset":
                    ApplyPreset(scene.Weather, scene, idx);
                    ApplyWeather(player, owner, true);
                    break;
                case "w.enable":
                    scene.Weather.Enabled = !scene.Weather.Enabled;
                    if (scene.Weather.Enabled) ApplyWeather(player, owner, true); else ResetWeather(player, owner);
                    break;
                case "w.set":
                {
                    // w.set <параметр> <дельта|auto>
                    var w = scene.Weather;
                    var param = arg.Args.Length > 1 ? arg.Args[1].ToString() : "";
                    var val = arg.Args.Length > 2 ? arg.Args[2].ToString() : "0";
                    int max = param == "bright" || param == "contrast" ? 200 : 100;
                    Func<int, int> step = cur => val == "auto" ? -1 : Mathf.Clamp((cur < 0 ? (max == 200 ? 100 : 0) : cur) + (int)ParseFloat(val, 0f), 0, max);
                    switch (param)
                    {
                        case "clouds": w.Clouds = step(w.Clouds); break;
                        case "fog": w.Fog = step(w.Fog); break;
                        case "rain": w.Rain = step(w.Rain); break;
                        case "wind": w.Wind = step(w.Wind); break;
                        case "thunder": w.Thunder = step(w.Thunder); break;
                        case "rainbow": w.Rainbow = step(w.Rainbow); break;
                        case "bright": w.Brightness = step(w.Brightness); break;
                        case "contrast": w.Contrast = step(w.Contrast); break;
                    }
                    w.Enabled = true;
                    w.Preset = null;
                    ApplyWeather(player, owner, false);
                    break;
                }
                case "w.servertime":
                    scene.Weather.ServerTime = !scene.Weather.ServerTime;
                    ApplyWeather(player, owner, false);
                    break;
                case "w.freeze":
                    scene.Weather.FreezeTime = !scene.Weather.FreezeTime;
                    ApplyWeather(player, owner, false);
                    break;
                case "w.apply":
                    ApplyWeather(player, owner, true);
                    break;
                case "w.reset":
                    ResetWeather(player, owner);
                    Reply(player, "WeatherReset");
                    break;

                // ---------- камера
                case "cam.mode": cam.Mode = (int)Mathf.Repeat(cam.Mode + num, 5); break;
                case "cam.target":
                    if (scene.Takes.Count > 0) cam.Target = (int)Mathf.Repeat(cam.Target + num, scene.Takes.Count);
                    break;
                case "cam.dist": cam.Dist = Mathf.Clamp(cam.Dist + num, 0.5f, 50f); break;
                case "cam.height": cam.Height = Mathf.Clamp(cam.Height + num, -5f, 30f); break;
                case "cam.orbit": cam.OrbitSpeed = Mathf.Clamp(cam.OrbitSpeed + num, -120f, 120f); break;
                case "cam.free": cam.FreeLook = !cam.FreeLook; break;
                case "cam.add":
                {
                    var e = player.eyes.rotation.eulerAngles;
                    float t = cam.Keys.Count == 0 ? CurrentTime(owner) : Mathf.Max(CurrentTime(owner), cam.Keys.Max(k => k.t) + 3f);
                    if (GetPlayback(owner) != null) t = CurrentTime(owner);
                    var ep = player.eyes.position;
                    cam.Keys.Add(new CamKey { t = Mathf.Round(t * 10f) / 10f, x = ep.x, y = ep.y, z = ep.z, p = e.x > 180f ? e.x - 360f : e.x, w = e.y });
                    cam.Keys.Sort((a, b) => a.t.CompareTo(b.t));
                    break;
                }
                case "cam.del":
                    if (idx >= 0 && idx < cam.Keys.Count) cam.Keys.RemoveAt(idx);
                    break;
                case "cam.ease":
                    if (idx >= 0 && idx < cam.Keys.Count) cam.Keys[idx].e = (cam.Keys[idx].e + 1) % CamEases.Length;
                    break;
                case "cam.focus":
                    cam.Focus = scene.Takes.Count == 0 ? -1 : cam.Focus + 1 >= scene.Takes.Count ? -1 : cam.Focus + 1;
                    break;
                case "cam.kt":
                    if (idx >= 0 && idx < cam.Keys.Count)
                    {
                        float d = arg.Args.Length > 2 ? ParseFloat(arg.Args[2].ToString(), 0f) : 0f;
                        cam.Keys[idx].t = Mathf.Max(0f, Mathf.Round((cam.Keys[idx].t + d) * 10f) / 10f);
                        cam.Keys.Sort((a, b) => a.t.CompareTo(b.t));
                    }
                    break;
                case "cam.tp":
                    if (idx >= 0 && idx < cam.Keys.Count)
                    {
                        var k = cam.Keys[idx];
                        player.Teleport(new Vector3(k.x, k.y - 1.5f, k.z));
                        ForceViewAngles(player, new Vector3(k.p, k.w, 0f));
                    }
                    break;
                case "cam.clear":
                    PushUndo(owner);
                    cam.Keys.Clear();
                    break;
                case "cam.go":
                    StartCamera(player);
                    if (_cameras.ContainsKey(id)) return;
                    break;

                // ---------- эффекты
                case "fx.type": ui.FxType = (int)Mathf.Repeat(ui.FxType + num, FxTypes.Length); break;
                case "fx.add":
                {
                    var p = AimPoint(player, 150f);
                    scene.Cues.Add(new FxCue { t = Mathf.Round(CurrentTime(owner) * 10f) / 10f, k = ui.FxType, x = p.x, y = p.y, z = p.z });
                    scene.Cues.Sort((a, b) => a.t.CompareTo(b.t));
                    break;
                }
                case "fx.del":
                    if (idx >= 0 && idx < scene.Cues.Count) scene.Cues.RemoveAt(idx);
                    break;
                case "fx.t":
                    if (idx >= 0 && idx < scene.Cues.Count)
                    {
                        float d = arg.Args.Length > 2 ? ParseFloat(arg.Args[2].ToString(), 0f) : 0f;
                        scene.Cues[idx].t = Mathf.Max(0f, Mathf.Round((scene.Cues[idx].t + d) * 10f) / 10f);
                        scene.Cues.Sort((a, b) => a.t.CompareTo(b.t));
                    }
                    break;
                case "fx.test":
                    if (idx >= 0 && idx < scene.Cues.Count) RunCue(owner, scene.Cues[idx]);
                    break;

                // ---------- наборы инвентаря
                case "kit.name":
                    ui.KitName = Clip(rest, 32);
                    break;
                case "kit.save":
                    if (!CanKits(player)) { Reply(player, "NoKitPerm"); break; }
                    {
                        var kn = string.IsNullOrEmpty(ui.KitName) ? T(id, "KitN", my.Kits.Count + 1) : ui.KitName;
                        var ex = my.Kits.FindIndex(k => string.Equals(k.Name, kn, StringComparison.OrdinalIgnoreCase));
                        if (ex < 0 && my.Kits.Count >= _config.MaxKits) { Reply(player, "KitLimit", _config.MaxKits); break; }
                        var snap = SnapshotKit(player, kn);
                        if (ex >= 0) my.Kits[ex] = snap; else my.Kits.Insert(0, snap);
                        ui.KitName = "";
                        Reply(player, "KitSaved", kn, snap.Items.Count);
                    }
                    break;
                case "kit.load":
                case "kit.add":
                    if (!CanKits(player)) { Reply(player, "NoKitPerm"); break; }
                    if (idx >= 0 && idx < my.Kits.Count)
                        Reply(player, "KitLoaded", my.Kits[idx].Name, ApplyKit(player, my.Kits[idx], action == "kit.load"));
                    break;
                case "kit.over":
                    if (!CanKits(player)) { Reply(player, "NoKitPerm"); break; }
                    if (idx >= 0 && idx < my.Kits.Count)
                    {
                        var snap = SnapshotKit(player, my.Kits[idx].Name);
                        my.Kits[idx] = snap;
                        Reply(player, "KitSaved", snap.Name, snap.Items.Count);
                    }
                    break;
                case "kit.del":
                    if (idx >= 0 && idx < my.Kits.Count)
                    {
                        if (ui.ConfirmKit == idx && Time.realtimeSinceStartup - ui.ConfirmKitAt < 15f)
                        {
                            my.Kits.RemoveAt(idx);
                            ui.ConfirmKit = -1;
                        }
                        else { ui.ConfirmKit = idx; ui.ConfirmKitAt = Time.realtimeSinceStartup; }
                    }
                    break;
                case "kit.page":
                    ui.KitPage = Mathf.Max(0, ui.KitPage + arg.GetInt(1, 0));
                    break;
                case "kit.wear":
                    if (sel != null && idx >= 0 && idx < my.Kits.Count)
                    {
                        PushUndo(owner);
                        sel.Wear = my.Kits[idx].Items.Where(x => x.c == 2).Select(x => new ItemInfo { s = x.s, k = x.k, m = x.m }).ToList();
                        Reply(player, "KitWearDone", my.Kits[idx].Name, ui.Selected + 1);
                    }
                    break;

                // ---------- внешность дубля
                case "look":
                    if (sel != null) ui.LookView = true;
                    break;
                case "look.back":
                    ui.LookView = false;
                    KillPreview(id);
                    break;
                case "look.mode":
                    if (sel != null)
                    {
                        sel.LookMode = Mathf.Clamp(arg.GetInt(1, 0), 0, 2);
                        if (sel.LookMode == 1 && sel.RecorderId == 0) sel.RecorderId = id;
                        SpawnPreview(player, sel, ui.PreviewNaked);
                    }
                    break;
                case "look.step":
                    if (sel != null)
                    {
                        sel.LookMode = 0;
                        sel.LookSeed = (ulong)(uint)((long)sel.LookSeed + arg.GetInt(1, 1));
                        SpawnPreview(player, sel, ui.PreviewNaked);
                    }
                    break;
                case "look.rand":
                    if (sel != null)
                    {
                        sel.LookMode = 0;
                        sel.LookSeed = NewLookSeed();
                        SpawnPreview(player, sel, ui.PreviewNaked);
                    }
                    break;
                case "look.player":
                    if (sel != null && arg.Args.Length > 1)
                    {
                        ulong sid;
                        if (ulong.TryParse(arg.Args[1], out sid) && sid != 0)
                        {
                            var target = BasePlayer.FindByID(sid);
                            sel.LookMode = 2;
                            sel.LookSteamId = sid;
                            sel.LookName = target != null ? target.displayName : sid.ToString();
                            SpawnPreview(player, sel, ui.PreviewNaked);
                        }
                    }
                    break;
                case "look.steam":
                    ui.LookSteamInput = Clip(rest, 20);
                    break;
                case "look.steamset":
                    if (sel != null)
                    {
                        ulong sid;
                        if (ulong.TryParse(ui.LookSteamInput, out sid) && sid > 76561197960265728UL)
                        {
                            var target = BasePlayer.FindByID(sid);
                            sel.LookMode = 2;
                            sel.LookSteamId = sid;
                            sel.LookName = target != null ? target.displayName : sid.ToString();
                            ui.LookSteamInput = "";
                            SpawnPreview(player, sel, ui.PreviewNaked);
                        }
                        else Reply(player, "BadSteamId");
                    }
                    break;
                case "look.nick":
                    if (sel != null)
                    {
                        string nick = null;
                        if (sel.LookMode == 2) nick = sel.LookName;
                        else if (sel.LookMode == 1) nick = BasePlayer.FindByID(sel.RecorderId)?.displayName ?? player.displayName;
                        if (!string.IsNullOrEmpty(nick)) sel.Name = Clip(nick, 32);
                    }
                    break;
                case "look.real":
                    if (sel != null) sel.LookRealId = !sel.LookRealId;
                    break;
                case "look.naked":
                    ui.PreviewNaked = !ui.PreviewNaked;
                    if (sel != null && _previews.ContainsKey(id)) SpawnPreview(player, sel, ui.PreviewNaked);
                    break;
                case "look.try":
                    if (sel != null) SpawnPreview(player, sel, ui.PreviewNaked);
                    break;
                case "look.all":
                    if (sel != null)
                    {
                        PushUndo(owner);
                        foreach (var t in scene.Takes)
                        {
                            if (t == sel) continue;
                            t.LookMode = sel.LookMode; t.LookSteamId = sel.LookSteamId; t.LookName = sel.LookName; t.LookRealId = sel.LookRealId;
                            if (sel.LookMode == 1) t.RecorderId = sel.RecorderId;
                        }
                        Reply(player, "LookAllDone", scene.Takes.Count - 1);
                    }
                    break;

                // ---------- личные настройки
                case "name": my.NextName = Clip(rest, 32); break;
                case "opt.clean": my.CleanFrame = !my.CleanFrame; break;
                case "opt.god": data.GodWhileRecording = !data.GodWhileRecording; break;
                case "opt.ammo": data.InfiniteAmmo = !data.InfiniteAmmo; break;
                case "opt.tags": my.Nametags = !my.Nametags; break;
                case "opt.voice": my.RecordVoice = !my.RecordVoice; break;
                case "opt.slowmo": data.SlowmoSpeed = Mathf.Clamp(Mathf.Round((data.SlowmoSpeed + num) * 100f) / 100f, 0.1f, 0.9f); break;
                case "paths":
                    ShowPaths(player, -1);
                    break;
            }

            MarkDirty(owner);
            if (owner != id) MarkDirty(id);
            OpenUi(player);
        }

        #endregion

        #region Интерфейс

        // ================================================================
        //  Интерфейс 3.5 — один в один по доскам артефакта «VideoRecorder — новый интерфейс»:
        //  панель слева 440×680 (отступ 20), док таймлайна справа снизу 780×170.
        //  Все координаты — пиксели макета 1280×720, от левого верхнего угла своего блока.
        // ================================================================

        // ---- палитра макета
        private const string CText = "0.937 0.914 0.906 1";
        private const string CDim = "0.639 0.596 0.588 1";
        private const string CFaint = "0.549 0.506 0.498 1";
        private const string CWhite = "1 1 1 1";
        private const string CSurf = "1 1 1 0.045";
        private const string CSurf2 = "1 1 1 0.07";
        private const string CLine = "1 1 1 0.08";
        private const string CGlass = "0.071 0.055 0.055 0.86";
        private const string CAcc = "0.620 0.082 0.188 1";
        private const string CRec = "0.831 0.122 0.235 1";
        private const string CRose = "0.941 0.651 0.694 1";
        private const string CSoft = "0.639 0.086 0.184 0.18";
        private const string CSoftLine = "0.941 0.651 0.694 0.25";
        private const string CHi = "0.639 0.086 0.184 0.16";
        private const string CHiLine = "0.941 0.651 0.694 0.22";
        private const string CInput = "0 0 0 0.28";
        private const string CDark = "0 0 0 0.3";
        private const string CTrack = "1 1 1 0.14";
        private const string CKbd = "1 1 1 0.1";
        private const string CGreen = "0.30 0.78 0.45 1";
        private static readonly string[] TakeCols = { "1 0.353 0.431 1", "0.949 0.627 0.678 1", "0.851 0.706 0.549 1", "0.722 0.682 0.675 1", "0.698 0.478 0.878 1" };

        // старые имена цветов — их ещё используют подсказки и HUD записи
        private const string ColBg = "0.07 0.065 0.065 0.97";
        private const string ColPanel = "0.105 0.097 0.097 1";
        private const string ColRow = "0.155 0.145 0.145 1";
        private const string ColBtn = "0.215 0.20 0.20 1";
        private const string ColGreen = CAcc;
        private const string ColRed = CRec;
        private const string ColText = CText;
        private const string ColDim = CDim;
        private const string FontName = "robotocondensed-bold.ttf";
        private const string FontReg = "robotocondensed-regular.ttf";

        private const string UiDock = "VR.Dock";
        private const string UiPbHud = "VR.PbHud";
        private const float PW = 440f, PH = 680f;      // панель
        private const float DW = 780f, DH = 170f;      // док
        private const float TrX = 19f, TrY = 53f, TrW = 742f, TrH = 46f;   // дорожка таймлайна в доке
        private const int Segments = 74;
        private const int TakesPerPage = 5;

        // ---- скругления: белый PNG 64×64 (радиус 16) режется 9-slice и красится
        private const string RoundPngB64 = "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAACgklEQVR42u2bMWsUQRiGn5k7sbNKEBGxsUnAJo0BQVJohHTW/gWr9Km0Su1fsAkEa2MnHGiTRjgLbeQ4gpDGK5LC3f0s9p1ks7kLdmZ3vg+W290Zjnmf+WZnBuYNZsaCCMAAKPR8G3gOPAPWgLvALdX7n2HADJgCh8BH4APwS+VDoFS9yyIXAIhApfsV4BXwArhDN+IIeA+8Bb7N0dTAZ9a+BvodmtlrMzux8yjNrDCzStd1idSeQm1McSINw5a2s6udAQOly33gHfBY7wuVhY5kgEnHUM8j4CXws6Hx0hBIBavAgcb4H/1JV4TPA1EAN/SN2ATGTQgJQBof94AvGutFg2DXI2k5Ah4Bk6Q5qneDKO31UHyaCQpp25PWAIQoEiWwA6w30r5vMZS2dWktgRjMLAAPgK+qFDs85v/lm1ApGx4CP6JebgM3dd9X8WlxZ9K6DVgwsyV9GZcalfocado7BlYjsAUsZ9D77SxYBrai5kZbtFbucRYYsBm1sQmZ9H4zCwKwFszst3Z1lhGEpHUW7Ir9cA4RyTwcgANwAA7AATgAB+AAHIADcAAOwAE4AAfgAByAA3AADsABOAAH4AAcgANwAA7AATgAB5AHgBkXj4+RyREZgFmkPkWdK4BppLaZ5HpM7jBSewNyPSZ34EdldbMv4WUGvV9K6z5w7MflJfg7sMtFn2AfI5m/dqX5zDCRQHyidlQUPXSNJE2fgSfKhMpNU6pQKTUmwFMtjpLHxjo+5pMHaiptE2mt2nuBUgVjasPkiHN3VdExEMkvmNxwI2kat42Tcc4UMaB2WG4Ab4DThnmyahiR7Rqu7Eq1MajNp9KwMc816ubpKwBkY5//C56fx7Wog3HqAAAAAElFTkSuQmCC";
        private static string _roundPng;
        private static readonly Dictionary<string, string> IconB64 = new Dictionary<string, string>
        {
            { "play", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAABa0lEQVR4AeyWuxECMQxEORIKIaQV6Agi6AhaIaQQokMvcKYZ8Olj32DGOwgNh/dJG7DdrPw1AFovcGxgbMA4gREh4wDNj//vBuZ5vov25hEaf8CygaPc/RSIq2gndZNjAcAwxs9SAAKQlLnHClDcEiUihahLP/zdC6AYZQtsIy1W3gCApMYqAgAIRJSIFKKm565IgGI2NFYZAICExSoLAAhElIgUoqZnUjZAMesWq1YAgLjEqgKAO0NElIgUoq66pAeAYnhRrHoCAKQ6Vr0BAFGl3gDe4v4mOkzT9JD3r6cnAAxj/CLmAflqni/0APASIycxjajl4++nJQBTroqLhtUKYFFcegAgIkQFUWueqnpZG3CJi0aWAeAWl2wAIkJUELV2v7kXsYGwuGi03gChcYkEICJEBVFrd4X0rBtIjYs2AQtAelxcAZb+d9FMWHqWDVjudXt2AKijTGyODSQOW71qbEAdS2Jz9Rv4AAAA//+Xj9ctAAAABklEQVQDAH2FhWG0iPeEAAAAAElFTkSuQmCC" },
            { "pause", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAABFElEQVR4AexSQQ7CMAxjfAZetOdw4Dl7EbxmxIceGpWQNcrGVE+tpjqxq9i9Xk7+cYCjA2QCTCDoAJ9Q0MAwfbwE1nWdZb9k6wVs/mWpkEJ8rd+TwFNEbrL1AoaaxvUZPejVODDUNG6eewbARd9ErVrhWD1WrfCrf88AlcDRBw7ABIIO8AkFDQzTmUDYwqDASAkErUqiM4EkY92yTMBtVVIjE0gy1i3LBNxWJTUygSRj3bJMwG1VUiMTSDLWLTtkAm/DHqtWaFaPVSv86t+TwEMUWhcBQ03K5kIPenUTMNQ0bp43DzBN0yL7LlsvYIt5mxSFFOKLRLU2D1Cx/+DAAZoh7AgygR3Nbl7FBJq27AiePoEPAAAA//+IGl2UAAAABklEQVQDAI3yTGF5aPPxAAAAAElFTkSuQmCC" },
            { "stop", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAABVUlEQVR4AexSOwrCQBQ0egE7W3sb8QCCpffwCIKfShAE0St4DDvBEwhW6S2tvIFx3tYhZPa5u4Q8eRMhvp3ZmbHbafjHDKQu0BqwBpQJ2F9IGaD6uDWgjlBJ0M4GiqKYAw/gA2hHOIRr7lMG1QBu2gMOELoCE6APaEc4hOsK7j3QYwgpAyCeAhsgA/49wrkF6QyoPayBVW1mv0W5z5I5KgeY/TGz7LlLabAGBp6XYo5RGqwB5iJRds1AlJgrRNrUQEUMCX+yBhKG76StARdDwoc1kDB8J20NuBgSPqyBhOE7abaBtzsV9kFpsAaeYe/u2CkN1sAZEl8g1Aj3kSGnDGRZdgP5CQg1e2jcGXLKgBBDYI3vEXABXoB2hEO4RuDesWS0ARGAUA4sgCGgHeEQrly4WXgZYEVC7puBkOnW4bYGSlOK+NIaiBh2qZQ1UBpLxJeNb+AHAAD//z4a+SMAAAAGSURBVAMAoVF4YfOY87kAAAAASUVORK5CYII=" },
            { "back", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAACJElEQVR4AeyVMU4DMRBFCQUdogZRInEAGjouEirgVEBHxS3oaLgAtAhqRJcmvCftSkixEu+ux0vEIn/ZWMn4v/m2sruz5X8TwNgBTglMCQzswP+4Qsvl8kgNbFbI1zcmgPF7Tn5XzZpl3OCMPXSbe8JaAArtU2iOZo3m7B2wDhnUPqTwE7pGWWMtABVO0R5qh+uT9p+SM+bPqfeCnJnyxiaAvCoDP4V5O27nTaBTtVEBMN7ed++86XYy74dHA8C83bbrdl8vvTQKAOa9553ve4qwOgDm7bidN4GUp0571QAwPvi+p8iqAGDebtt1u5/y0XsvHADzxe57ijIUAPN23M6bQOr8wXshABgPue8p2g4Aqa+v7mHebtt1u7/6gcI7RQEwH3rfU+zFADBvx+28CaTOCtkrBhDiLqNoMYDZbHbHeRfoE1UbxQB0DMQz8xlyZoofRQG0C4QJmISJuBWq4gC6BWKBblirBXPYCAFo3QJhCqZhKu120TkUQKdA+B7C3kU4QANhAiZhIm4VUxUA3ZJEyLuoBiCEAsQUTMNU3Bqk6gC6BaLYuxgFoIEwAZMwEbd6aTQA3ZLE4HcxKoAQChBTMA1TcStbmwDeqPT7l9T1K3vFBxC93sVaAIp+4fQRLRs9sPfNOmRQ2wRMwkSyzlgLYAWKXjIfK9ZXzKGDM9p3kXXORgCrUPRDuf5rygLobLriFyaAis1OHjUlkGxLxc2tT+AHAAD///Sr0GkAAAAGSURBVAMAAL6QYTJWY6AAAAAASUVORK5CYII=" },
            { "edit", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADGUlEQVR4AeyZPWsUURiFd1NYBMRGMIaIWPgLYiWiEUH/RETEQqz8QhBRQVbBFCaWKbRLpf/BCAFtIvZiYRFWLSxERBB08xyY2dy5887OR+7OzELCOblf733nnDt39uPuVGfC//YMNH0DW3MHBoPBPLwacb7owjRuAMEL8BuCN+FqxE36+nCB9kg0agCBT1C3Dg9BH4fpWCfmDmUmGjOAsBVU3YN5WCL2ZFZQIwYQtIygG9DHdzp+QBddGmvMmaZMoXYDCNHK30wp6XRWu93uDDzI2Gvo4hgN83mo1UAk3lr55wi/hsgY16n8hS5m3EZcr81Ajnj/jgxigU7526kPq7UYKCle4p7ybx908dFtxPWxGygrPoq/FAuMyndssU9RPVGM1UAkJmvP+9umkxH/D8WXoYmxGcgQIxF6YIuKV3wva/U1OBYDAcXL7CMJzWJwA4HFp+6UbySogbrFy0wwA02ID2agKfFBDDQpvqQBhSfZtHipqfwMtEF8ZQNtEV/JQJvElzbQNvGlDLRRfGEDbRVfyECbxecaaLv4kQYQr0On3X4Z0TX0kTj3U6UCq9B8I0P8EZJZh06mGOJ1VFLYLLmDwTRA9nPQxzLfjFIrifhnBFrizXhigyLLwFnvKl8Qf9vr03dYnbDd8vtpr1jx9AdHloHz3pU2vLbEa9uk7ghx2maWKYbCI2WALaFjPP8UTCfIw6sTo5W3to1W3jI1nBu6kjLABc5AH1OI7sENqFMzS6T2fG0rHwu0DFiHqC+YcB+eghYk3npGZjGsHzBCcNa6sGXgghU4ok973hL/kjlbUNsvBLdYDOUk5Q4SBgg4ypC//+nKxANebVLbiTz7mbEIdbZPEQTKtUjuA262hAEG/CNtulLQK1KP3hOIf0xpwc9rxVTt++9OTFwIQV8ZXIIuYsF6c5sm5jR8CD+4QW6dsZ+0X0E98BRBoFxr5P7lZksY0AABdymPQ/0u5Qp+w9gf+vNBBLEXKeag3hRDcI6cV8iXQMqARgn8DN/DwoI1zyfz+/BtIPb9/GqbBjQwKdwz0PSdmvg7sA0AAP//OdlgjAAAAAZJREFUAwC1gtJwnXTMiQAAAABJRU5ErkJggg==" },
            { "rerec", "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" },
            { "more", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAABeElEQVR4AeyTsUoDQRCG7yz1FUSxSiO+SgTBPp3YG5/BWFrYCb5B9CkEe8E+D2BlI7J+C0vAdfdy8HMTEybMz93NzL/3z/yXnWbDfz7Aug10B9wBcQP+CYkLlOnugLxC8QB3QFygTHcH5BWKB7gD4gJlujsgr1A8YLsdCCEchhBmYJ5ww3V/1dLoMeNVHUDEOULfwBUYJ0y5vlM741oMaqa84gCIOELdA9gFKZaXmHuk52CZSTfkTHnxtcUBKFyCPVCLWLsoFK15TW2A44K4PHWSJ3i25lUH+ELMqvguNFjzqgO8FsTlqZc8wbM1rzrAHWI+QC1i7b5QtOaVB2jbNgqcIPAT5BFzk9Tzq5ZyZrz48tqfuEHMnIYRuAXP4AnMwCjVuP0bqWbGqw4QpSFmAaZgDE7BNVjEWhdiDzDhdQ7QJfK/1HyAdTvhDrgD4gb8ExIXKNPdAXmF4gHugLhAmT6MA7Ks/gf4AP13NUynOzDMXvufuvEO/AAAAP//QusOfAAAAAZJREFUAwAFL65huJxaKAAAAABJRU5ErkJggg==" },
            { "x", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAACn0lEQVR4AeyY20osMRBFz5zzEccBf1HxgoLgBRUFH7ygv6giPvuq49qQlu5YLZXbiNhDFZ1Up3btnUwyQ/7++eGfScB3L+C0AtMKFM7A9BUqnMDi9N+1AovFYo6vFE/bCADYyfjuFQB8k7r3+APtQ55VDcxjADv8NdoucwkA/D9oF7jGy4+JXdOvYgFLkyJs+QWxuQdcgz3jZgxa4H3boEixiICx0QembdUj/NlcAmaz2ROpZ3hsEnEVB719yCs3Jq/081BT7S/dJUAIAJ7wvMFj24RI8kqEHO2rGO+GWqdxcKzvFiAAgFXQEqGVcIsI5K2ZF3nVUDmXJwkQYqmImuTFJ1mAknJF1CYvLlkClJgqogV58cgWoGSviFbkxaFIgACCCB2H6vZdG/sS8pcErQ17FXJ5nW8JAsaLQGSbt9bptEVczmNgOm2UMwjmdKoIUGFE6PizROh130VeY/ux7HY1AWLgEFGVvGpWFSBA/AV/xWNTTO/ieFG/qgA2rH6N92H0D49Nsf0wJn6X3a8mIBCzTpuYnE4nCY3jWf0qAiCvY9QifwcrOY+BSYRyBsGcTrEAyGs2rVNFG3adjb0OMet0yvoXC9bAigQE8tbMi/yHKESobYnQSmgCBqRSOtkCvOQ7Mq1EZAlIJd9SRLKAXPKtRCQJKCXfQoRbAOR17WFt2Ovw/e74uZ4hx9rA2tgHLhAGuQRAXvdC+oUlZWA6bax/m4NBYx1EKNc6nfao2fxeSOR1PI7xc8URIYxYRJN7oR0Y6XLrjedRKEyz3ALWEUgd/i4x3UUR+tpcXyFBAHjLcxWf09YdEc16FjA7fOvvh1nMLUDZFHnEn9Vu4WAn4ycJaEG6FHMSYM7gEoPTCixxss1S0wqY07LE4I9fgXcAAAD//7zhA+sAAAAGSURBVAMAcIsOcP67YyEAAAAASUVORK5CYII=" },
            { "undo", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADSklEQVR4AeyXSWgUQRSGe1TcDUSRSIxCILihByV6UwwaVPTm4CASQcGbiLjd9KIHT/EgevAgLuAWowcF0YOKBFwDggfNIR7UgAsuGEUv2n6vmdbqnmqtrh46PdLD/3VVve5XVa9eLzXDnBr/5QEMdQLzDOQZSLgC+S2UcAETu+cZSLyECTv4PzPgum6jkHBxUnGvyAATP83IAwL145SZViAAJlzHbNeDrw3YxvoN0xKfCdAMC6EVJpv6xr0uEADOM2A4+BpJZTb8VUywHjbCGXjNxZ/hOTyAh/AW+1d4Cl1QgvHYEyscQKwOmcQaOI/TBzgJkr0GSp0kk7M4UYRzMIjvDVhL3VqxA2DAAhThCaNegXVgq3YcL9LXVZhKPbZiBcAgYxjhGnTBXKiWVtNRH/1vh1hzMr6YjuXW6GGgFaCT3PfdnNgErdBU4EfZAithJ1yAT6DTOIyH4BFjLaA0klEAdDiH3uRhjOr4AOcbmG8RTkAvyKvYoeyH69AJJajn2sUgk6Wo0HwsvYy5iPKfMglAJn2XnqZBWHcwtDCpvfCdupG4tgd2cLE81DcpdeomiIm6E6rNJICjONQ5Dsc/+kF1H7QxkX5KK+HbB8twLoGXMUpfTVQuE0SBMlImAYwIeX+h3c7A++En9cSiH3k25NaRb4fa3xIaByFSJgGozh9pbAMWxl3KQaURu7UI4h3O8lp9T6lqD+PMUw1qPW4A8gDK/ugWnYQZYCA5xyk7EYRkYJXGe7fG5pnCATR7VvuD1d5JHY4g5G0nz5dq7mBxpqsGvx4O4B4nXLCV7KMEW3/f7zCVb6Bql9rw64EAiP4lJ56BrU7Rx6Cts+9HH/KxO+a3y+UWsjCpXP9dBAIQK87y0ZIN1lbabTGYgu9mrq+WOkMdjaa9HAKqCEDOMpFLcARux+CN+FYLxn1BX49BVcXWXhuA6jHE9fBHcmZ4PlkPQF6r6pxrPgB5PtWAnKxnIHwLjeJNFNhUZj2A+yy3bBwpPEldtjNeQw6ZDoA3kfxJOisTLSPfGdlMlptO5m8h+UPUwWzlttF+ZzKdASbuiUy8Au13piYC8KKIOOQBRCxMauY8A9qlTtGYZyDFxdYOlWdAuywpGms+A78AAAD///enDh4AAAAGSURBVAMApn8JcAAIyK8AAAAASUVORK5CYII=" },
            { "plus", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAABpklEQVR4AeyWvUoFMRCFXQXBwl5We8FSX0AfQ8TGQnwksVBQwcfQF9Be+xUsFVtZv8EbkLDJ5uZvvTiXOWSTzEzmzLnFLC8t+E8JTC2gKqAKJHZA/0K+BvZ93wp8Pql3xRSg8BuK6wR8X7IWsSIEKHidag+BsSPO1swm51qEAAVugxVgbJWPHZDdShHIXqgroRJwdabWuSpQq9Oud1QBV2dqnasCtTrteud/KMAc0/Z9v88aBLq1B2zbDY2f+bV2gqH9qAIkM1PlPQlCcY6vbRcchMaLX8fbo1OslwAJ7KmSGqra6BTrJUCpcv97quSoqskUK3A+KgU6L5umeefyFkxlV81PDc73vQQkigTHrJvgYA6c4WvbKQfz5Njg7RNivDZKQKJJ9AoeQkHMI7DtKTR+5vdmJxjaBxEYCvwrZ0pgaiVUAVUgsQP6F0psYHK4KpDcwsQEpRR4oa4vYEy+n80m51qEALPMB0XeAWPXnH2aTc61CAEpkIJlit3iO2iqxC/KihGQaiDRgaCpUvxjUIZATCWRMUogsnHZwlSBbK2MTLTwCnwDAAD//6YngygAAAAGSURBVAMAbn+uYTgHBJoAAAAASUVORK5CYII=" },
            { "minus", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAABAklEQVR4AeySQQ6CUAxExbXxAHAZvQeuPJc7V15CLwMHMK79ThOX2E8ysQ1hSCckQDu/89huFn5pgWyAIiACZAL6hcgA6XYRoCMkB4gAGSDdLgJ0hOQAESADpNtFgI6QHCACZIB0+zoIlFLaUsoB90i1c/BUCeDQFwwaoHuwhq83bH+XuwAG7NDaQ1nV4wx7z9xdAI219/jk7/X2HNwDNk3zRPMNyqorzvDyzN0FrBEDTrh30DFYHbzP8HSruoB1Y9AIPYI1mndNsxaoDcl8rwUy0zdvEbAUMiUCmembtwhYCpkSgcz0zVsELIVMicBk+oEPRSAw7EkrEZiMJfDh4gl8AAAA//8XkEUnAAAABklEQVQDALrPqWEbLkKDAAAAAElFTkSuQmCC" },
            { "down", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAB8ElEQVR4AeyWYUoDMRCFs15CCx6xgtbfYlUU+qOFYo+oIh7C9XslQrqmbnYn26U0JWMm08yb92a6i2fuyD9FwNgDLBMoEzB2oPyEjA00p5cJmFtoBCgTMDbQnF4mYG6hEeC0JlDX9QS7MDZtbzrYnfGTJwD4DZXfsHf8e/asC8xHAH/xr/CTVpIAwM9BW2C6L5sTe+WcZXmsO8CELVsQm3BuXbrceokLFVZj4ZpSxCzCY0xDYPxYPcJ/V5KAqqo+SX3Gmksils1g6hnyK+42yRNyL76m/H8tSYAQANRvdCO/YTOIdJ6Ez9Fz1YBzG2o9NYP7zskCnHMOYD1cMRGaRLIITz7WeZFXDZf66SRAoFYROcmLT2cBSuorIjd5ceklQIldRQxBXjx6C1ByqoihyIuDSYAAvAi9DnUMTQ/2EvJ6zcYe2JXPDXM6+2YBqgiRW/bY22lGXMa2s/S2Uc5OsM8hiwAVRoRef2v5Lbb2d1uupX2dTYDKQeyaPTYJwtulzuvO9pDjT1YBIoQITSImQuT1na5ls+wCxMyLmOPrH8Bv9rmP4eZdgwgQRQg/sF9iE+/j5l+DCRBViH9gX/KHskEFDEU6xC0Cwm6M4ZcJjNH1sGaZQNiNMfwygWjXDxgsEzhgs6OlygSibTlg8Ogn8AMAAP//nLHuRAAAAAZJREFUAwCkRZhhgRf7ywAAAABJRU5ErkJggg==" },
            { "left", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAABvklEQVR4AeyY20oDMRCGu/oQWvARK/ZKkCIeEMQrLfYRtYh3PoCu3w+96IYIbpKZdCFlhibTJvt/mc2BHM0m/mkAtRPYMtAykDkC7RXKHMDs5i0DKUPY9/0cP01pG7ZxzwDC7xHxhr9TPuc7y1wBEPyK2mtcz5U/EZtTTzZ1ktx4TEOESvwiaNNR7/FkcwFA/AsKQ/GEZo9d132okOrmAIjXyF9EBG4Qr/kQ+en/IVOAnfjYyEt89gQWphmAh3gzAC/xJgCe4osDeIsvClBD/EgA/T3utcRLTfYqhPgbOootlWvW+SJLJf3/aVkAiD+h5xUemtb5ZRi0qGcBIOgYD+2bwBfuYlkAvCJbVD7g+yaoFdl53g9albMAJAoInWc2Kge+BELnoCBctpoNIDlAaLLGIBbWEEUAakIUA6gFURSgBkRxAG8IEwBPCDMALwhTgD2ItcqBa4nVFUsQHlc1B5Ac9gmdi2L7xBX7xDTuhYCIbXbTuBdSFuQ7iFvKusz64fuS2GHfCyFyYAi+I3CGzylnn5Vc5gBiB4bwLf45CCZWqgAkao02awDRYXEMtgw4Dnb0US0D0WFxDE4+A78AAAD//77WwAMAAAAGSURBVAMAfrmVYcGOVs8AAAAASUVORK5CYII=" },
            { "layers", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAEJklEQVR4AeyXP28UMRDFL3RQQRMKJOADgKChS5Ag6fkcUNEBBRJQhZICPk0SiqRKRygpgDIVSFDQHe+3uXfy7trrvV0vEtJFMxn/Hb9nz9i3F2b/+d+kBObz+U3p9Sn3aBICAr0lPRDwr9LvlKVbKheXogQAKQX4kZA+lFooH9EnLUqkCAFASWPATcC2OJFRBATaodLccQNO2WJEBhFYAN8XuhxwTgXV0KiYyD4+oyMyjSsRYBEpgAC+0+H7UH3bGxsbuyhl6UdpSvA1KEd6ERDovqFi4DsCfmy0lKXs9rbauogwZiUinQTGAhfYmojEsRSQxYhECZQGXmOhikgUI1IjMDVwYa9JCSJLAgL/Rt5JTo5YxaiQwCRnLcajI1doDIjc1zTWkIkK2MiRZ+6tCAj8JTW8lKYEpwDnVlkmZ2rw0HYROZLuaj5EuBBUjMqeMN+gpyKgwjVpSn6o47f0X8pci/2S/pSm5IyOioBYf1HlhTQmV9T4SMrRDX5wND8r2lWuaz+QrHk5Mem1MP+hryJAQQ17shwd4aJiVPzgFCXSAM4a0cXVCDZC+ZXKlSwJUBMJx2DunmYRTuSAxZk7RJkrBRSXBz5TbsgHgLdysEbAs0Wk2D1tn6EVaELFwLlZwu6wbODJWy9KwB5KEykJ3Bg7CXjQWCJTADe2GgEtdEu66c66nc0CIiQ7xztL/BEW5MiJ/J1oDDFOm4pRIZyI8WSoMEu+NqW3KVuXBNTxQY2fpWcqdyaniJDsJF0u2e/JHyoTFTYB4K3kDEcLj3OGu/9U9ffurwiogZf4sRtl2S12sPO6FBEne+5E5LImfXcc4H4XwGQnT4T5IpWKgAqpl5hdhsihJiQ/xkWk74mssuOMJfTAIIgtuUpLRUAAeImf05DQB2qHSC60fCLN0AIMoZKLcXac0wE4a2rZqDwV5m/0VAQoqOGtLKGAAxWjwjFCpG9o3ZWXO/LdB3gsVDS9JmBjI965dUmABi1EKPBrsLmDdIfKsUIkdyKf5PM0nBiWCUspoNhxfIbdYdkn2Er2GgHP0KKpUPAQW59IJxEPthXoMFTw4a6mNfDkCUYJ2EtpIiWBG2MnAQ8aS2QK4MZWI6CF1i+xd2ZheYXRRbVlHOOt5AxHamOdM+uXONyYZVk5wvXLNZi7flfZccZ2Xa/rl7jrt5HfkfVLvIhTwomfEK1kr12ji8Hhh0supnlFsz8p7Nc2uFWIcXy4q2kNfP0SO6YHnUjJHW8eUTSEmoNc13VpIvzs5njd1bSEBaFV9Ju4uQj1lQgwARWR7nufQefKK4ye19r/2YRocraHxlsGEbArEel7Ip5iyzcAwJPJ6YE5O4qAnYtI3xMZveNe07YIATsTEZ9IM9kNfPSOey3bogTsNCDS6yX2vCF2EgIGIiKd38QeN8b+BQAA///Rn/kAAAAABklEQVQDAISgOI4QvgW+AAAAAElFTkSuQmCC" },
            { "video", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADVklEQVR4AeyYO2hUQRSGd4NIfAtaRBFsTIwYg4pgqwGjAcHKIkRsbFQURS1EEEwhNgoSiyhoIYYgFjYWqyBoK4hojAZfhQrGB4Jvggjr9y/u7r1zZ3fdx+ydhJX/d86cO3fO+e/sPDJNiUn+ryEg7gFsjEBjBKr8AlPzJ5ROp9vhBkdsr/Kjh17PjQDJtsBh+I0WY/COI44R4yscgouIURUyAuhoO728hL1wDnSNuQTog8+JvY2yYjTRwRrevgZnwXpjNgGvkMMSyoqgETha0Zu1e0kjfrmc7hC8EGqeJiVgq/HyG+oH4EZHVN9v6TuILhJaHHTYbNo0w3M8G4eapykJmEkliOPJZHIA3nXEAYKdgCbmm45gncRXUR+B++A0KGyWABlBPglWHNlKxOx6hulQncST8DD2fdgKQ7AJCDWIs0LiLcS/DU/D6TACbwWQ/BayHYVdsCC8E0DimqiDZJyCC2BReCWA5FeT7UO4G5r4g+MiDMEnATvI7B5cDk28wLEWXoAh+CTgIJnZJup5/J0s6Y8pIyhDQORd147PBOgh8T1wAtsKXwVo6ewg8ZvWrAPOuASsCOQQNH9T0abVTfLvsUsiLgGF4n4smbHRoFBHRrOaVwsdV3SsPkO0Wyyp2oUxiyMuAcWzSiQ20WAUEdqNMQvDVwHKWLtwChGDsFkOG30ScJYENYkpQtCuPIIIHadDD1TxScAQCa2Hz6AJHaMf4DwEQ/BJQIKlU+cgnYciRway1h8xunTAzMMrAUoLERNQP5se6l9gUXgnIJstIrQLa8PTrpx1R0pvBShTRGg37sY+Am0TPGET0EFj1+i0BHhn8WlepBGizW0dz3WspshDAr7nqxmrnyVrP3R1N6qVpD8TKf/fB5LUVUneY1g813FawvXXWvbpKQm4mq39K5dS6urD1d2ovqaODITJ4VLOKmIgQhN8L020Ui2jfkwCTuL4CePCJwIrB4r/A4k/gq/UugnjNcZO+APWG/pwu8jhV6WBNQKaKNfpoA3q52TOCdw1h67wh+m1leRvUFaMjAC9TUfjsBfq6nslPld3o23EmAf7YNGJSw4lkRMQbEnHT6Gru9HIUhiMXa5tFVBuJ5H2dXQ0BNTxY1tDNUbA+lnq6Jz0I/AXAAD//6XHyi0AAAAGSURBVAMAN2qicFLqStcAAAAASUVORK5CYII=" },
            { "flame", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAFW0lEQVR4AeyZaahVVRTH7ytosCgxGhyigiYNGkSsL2EDDVoaQUGTZBE0vQwjqCQro5AIyooMCqyo8EMQRZEmZSB+KCGFgjIcEJSnPnEeUfT6+793znbf9fY9476IoKz/W8Pee+31P+fcffY+ntQ4zv+dIHCsb2BH70Cz2RwCRnSSZMcIUPjXFL4FrMN+E90R6QgBCv6cah8GqcwgdmnqxNTRCVDoLAqcAqzcbQMx/KgEKH4qRb0MQjI+FKwbi0aA4i+hmPdBOxlHn0HtGqvGoxGggLdBVr5TaQ89WoSrS9aEhbNyZa+g84PAl72+k9ivJDqaikKAal4HVhT7zARHQFa/ExOu7sYiYH+g/1PSe0Dr/wG0LzMhMdwP1LFjETjLFDGvq6vrMFhP/BPgy2Cc7yBxMrq21CZAIWdShc3TQyyVVzHWAV/G4rwLaouduHRCrvJuBm0HvgxLnaT9PvzDwJdpkG/3zvD7Zdq1CSTZFyY6VZNSQxoSS9H6PaBaZBYkPmqJlHRiEVhg5h1NYdf5MUjMxF8CrHTT9wsbLOrHIvBTYEIVbMP3EFgOrDwKie9B6R92FAJc3c1U9DPwZSIFXegH6LcV/0ZgHzlCDZH7UUYZRCGQTPhion31lO/IhsQecAe2zguoFhkP6bktkRynMIFmszmJ5L3gN3C1zUtR/xH7FfhiX3Cujf6TcT4GVh4j/7022M4vRICEelF9RZJzwS1gPgiJfYy0Rwr164tBohvjLWBlLnO6pdg2+n4hAgx4AogEqk+GMcGAx4OWFcCXQfTL3DZAYgYD7OOkt/V04rlSlMALgUyhl9ChQD/7Agt0aTxLUNsOlJPJkD/DeW2MXAIk0dk2dBUvom0+GOLlnujZMnu5whtkZIE+O2l/A/iiO/6AHwjZuQQYZIsi5OROrL8h8Rz4FlvPNMrJX87KN76hi5ZZlBOtVs4JGUUIjAoN9GK6Ox/ia7+DcqJHJ7R9cB18g7uwH9+eHzIXAfo3ihC4TB092KvkNbWY0ynqj5ZIvrPYdLFzm+ZGNgEeCx3UTzOjHsHP2oBto30Kxb+DLis7zIDTqUF32ISPunl34JSjXZ21i+J0LBxJZB5I5V+M2WAk7V+iq0hoL5S5EuUR6A1UcZ5iFLkCPIStKzQc+yowDWwiVlUuCAzcGIi5UCYBitHj0HS9+41r+1X/X/r0CP1e7b8tucl2kNxaYjHDkkkgGbIs0am6PTU6oG3u3GW4CAG7vxnLD+vs2MWTUy+u0Savnds056xCSW+bpIt4aGtBuJa8xGjlRjmxc7uG1Mi9AzyDWsv/SQck+jWu2DWJXVuRS8dPu3lbxtxRHiEVaJMrtoCJz5dRB+TQtjm0PQ/NOWCq3DugEVwJnXn1ZUFuCi15iyig5diYNhbRjL2YfjoE2QuxhDl/oS1XChFIsujL8q7ETpX2Scsp5KY0UFQz5jb66hHRCxHTiZbNx52XYxQmwBXRkTG0vT2HOX6noIUglwh9bgWLGKODvb8VJ9Qn9zPXyj6rwJ/CBJSLxFoVnpEdgK6oiPRQ4GzwNLgL6Cwt+wNsfXLUI3NzYLxCTzKHiMkuhFIElJEJ9LF2ArY+KaIGyNBGo/E80TlAv50f0LK1fxqKHRLlmkDuT0ONWbHSBJSMibRqjMG2H20JlZa1jBiT5MQsJ5UIaAom1P8BXImtK7sGXVZWMUBn4VFJLtzyUpmApmLivUBnA/0f8Dhi+s6TtXvUb0D99XXucsbOAfsYV1lqEUhnpYgmWAy6gZ5zfRYRqRvocz2QPZg2bbunorXO210u3cpLFAJ2WgrcAVaDP8FSINuetuywSv4RAAAA//+GnmBQAAAABklEQVQDAJ0FaXAbuTEuAAAAAElFTkSuQmCC" },
            { "clap", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADqklEQVR4AeyYXYhNURSA7yD5mSk/NaLhganRUMx4GiYRD/4SpSlNEeVfk5/CPFA8IIkmyZgHqfHklhTiDU00D8zQ5C+kiFDE+K3B9S2dM/bdZ+97z7nnuvdOzrS+WXuvs9bZa+29zzn3nH6xPv4XFZDvBYxWIFoBZwYSiUQx1MAEx+RL5WULkWQpzINdEIfHZPsJbsIT+kfQvuSfF0Ay5bASuqAHusnsDVyGA7AMykGVrfiNVQ22dlYLYNCpsAqaoA0kWZnd0yQwGQZACfiR6X6cMiqAxGS/1qJboBt+QA8DdsIpaIBa8Jssrh6Z4rEYDGkLIDF3vzbSVvdrG+dbA5Jkf7TMLiqwfCOiHd6DKrJiat/YthZAsjvhAVHuft1P27RfMWck74iaVFRUNARqaJ8FVarUjq1tLIDE9xBwECZCJvKFILmjHENvANlOrWhVRtJZBK7I9nPbosvIY4Q0UuEpgKBSAnaDH/llcOpkRothBjRAM9zArxG+gyp7Ge8iyPY5qR5w2mmvA08BBM4H037+jF32fRN6HciyF6PPgypVJLRcNTjtoeiroMogOgthOJhEYkz2XpupgJ+9R/82KpjFEpgJW6AF2kEuwB246StxiCLGg9z/z6Bf4fMIZHJQvkViUjqbCnhtiJCZNphjMYqQ+/yJWPJfGd2nIPf/evRo8Cty4ziK8zjn3DTtYirglsHdc0dgVofBUpALda4hxq8pgeMFWAGjSLoStsEL+mnFUwCBH4jSV0GesPLwWkDCh6EDH7kNnkNvhgrwKx9xlGtB4u/RnsOYi6EV3tIPJJ4CnOg7jnbVWhryY+sSejvIithiOeyRu1jkCb0JvQT2gTxTpHjmIzGLfzbG4GsVWxJdWsRArR+0K7fD1QQdB5n9ILykOCmeUK/YCnjodc2rpZ4iBpsysBUQeC+aTp5Fm+yAStP5bAXoF7EptiBstgJMycmFPJsDuWA94+iiPyz/HA9SQAe3uWu5gMxugy7GXI1GPbKQ+1EB+V6daAWiFQg5A9EWCjmBocOjFQg9hSFPEK1AyAkMHf5frUA1b0W299as2lmWaaBLoJ/T8iFKP0ELhiDvsmF8mxlLl+e6QfrGLcRvfingmTgUCPfJST7DeNIxFuB4bXR0cJX9CGsu1gKo+Ap51MFXyJfIB+U6crluS8BagAQQGAf5QlxNPxfvwuoYVYwtH5TjjG2VlAW4UZxIvvnn5H2Ysdxx9K+DbjpJ2lcBSREF1unzBfwGAAD//xuKB8YAAAAGSURBVAMAbKfbcClsBdgAAAAASUVORK5CYII=" },
            { "sliders", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAACxklEQVR4AeyYO44TQRCGp41EQECMFmJkRAYXgIDHFRAiIUAcCREQLAFX4BHABSBkBcRGIkTErPcra9sa11TtdM2s215prPrVPT1VXfX3q3o8ay74byKw6wk8cwaWy+WBYGiQYivYpr1LAMdvcbwQUH9DGRJsqtibBHB+lWifgCxPabuSH/pKdKvZmwQI8Ca4BLJcpnILlEo1e49AaaA715sI7HoKphmYZmDkCESW0LHni3NfMvY9yhXQuwu03MnvT8sDrdDzbPqPEDD7J5iccT+jkPGKupbXNOT3Ui6wDWd4+tiQCIGOLgHojLvRecFDJMN3/Ev/ZqO8KEQq1PPUJNsLvPe97RECnTWYUvqLh3dgqBymlP4VGnf8i12EgOh3QADPaLwO7gdxDdvn2IySCAFXl0B+gy9B/AlGbvo3G+n4F/gPskj9Z37Yp9IkwEjqtX2YUvFaXfPjlJL8ED3v1/aqEtsDkJC1fYNOBq1Vgs/54VzOe+IwxZyBrAmJBYiu1YbgdX6InPfZvS7NWM1GbTngeewXWfEeXBFgxOZgdY8JlNG1ba5ha3CY9eI9OCPgj3RyBOR+EsFW1zYkivagzMADgh8qkbUtvkJ+ING7B8OdqgjkHiNQzfUehcCnEe5G32XEN8tY8kV0D87FdsY0PaRyG7TvMi951vKChrbOoPxAHxtC8DlfRPaf6B5h+15moIHEd7C+y+DhK9Dyra1DPZofVr7anRKAzhft1yX1R51OS6zOUWfs90QTIVB8jjsEO/bMoj7vHVO3+UOEgNvLmBeQkPNef0+U7ME5to8jBCK6FifXnkA2vicwLtmDP9ALLSHR3zu4o7J3kToBTQScganWPM1AtaF2HHkzUPxFZPbbNNXsTQKcyzpDRm6dcreqZm8SkFGFhGTIwf9K1LJ3CZyS6P0iEj0PkNi6/ZkEvMD2qf3CEzgBAAD//ysdzzUAAAAGSURBVAMAaKl1cJwULuIAAAAASUVORK5CYII=" },
            { "eye", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAG6klEQVR4AeyZe8zXUxzHnye3ucywuWTKrYtZ2Vz/IJHLNNG9VGKjspkYIukekhYtK2xurUULXYnRij/KxhIbhtKYlGKWJWRij9fr93x/v+f8zu/8fr/neXqaZbXP+zmf+zmf7++c8z3nW6ua/fzfgQL+6x/wwC/wv/0F6urqTgLnZzhpXxXaIlOIQTrYMbRrwWZQx4C3gY8ybFMHtK2hHQ1OwLbX1OwCGMDBoA94g1FsAdPAJaANKEfaumCcDrYSuxz0BAchN4uaVQAdXkNvX4Ml4DrQnAEcTFxPsBxsIOcVtE2mJhVAJ23AUnp5G5wGWorOJNFqci8CJ8M3mhpdAIkfJOtm0BuUo98xvAWeAqNAnwz30arTpg9ikvqh3URfD9A2iqoWQLLDwXtkexSkaBfKl4GFHVdbW9sDjAQzwbIMT9Cq64HfcUDfBbS/gZgOR/GYfQJ5xPJUsQASHEXoKnA5SNELKNswuKFgOfgLuSLpA/S9EcdTwIsgRfa5ijFULKJsAQQeTdZ3wcUgpu9QdGUgw8FO+GaRsWAYwS5gc8IWkX1bhA+yyJAXkgUw+CNwWA0uBDH5i3Si4zWxobkyuZyinYn3gdEUkUW4wB1TkUGhpAAGX4vhFXABiGkhiu50mJq7mBqIPIeCThkObbCkOXK6lrpjdT3RFNFFSCl9TUkBOD4M3NtpimgSnQwG/xRpA4HB1oLxwHfEbkyfZditDowDPiDUpUTuPWAolikgpt7EluiLCsDhBqLGgZhuJfFDsTKUiT0eeSXwAbSjDXPLq3sE/Up83Ylg00Rfk7EMBzFNJNattqA3cU7AcA7MPBDTVBLOjZWhTOyxyJ57rqKtRvp8TIybRFlf+nSH83gS+8wn9ty8MlcAisNQ+Ia1hS3QCrgJoBpZYNtqToH9VPhnQDVyNrwTObmt+sY+RH2uAJj7wRkgpE8RBvIkPFnCponinXa9IuuvyE65/JvYqaMOdYGGENu3ICWYrO/+mL4AITnWe1W0IoknxPEKAf6G700CFyKsVBZ3RZYdyB2JddHn38T+imejd6ehKVAcWzDkGfK441nonrwua10Prf0FnkQRTx3n/bfoG0OundBvBp1uDxXy6LbSzgQhxbGhrcATuwFhBgjJ98IsC/BnDg2bCHAXCHVJnl/PeR+/JdclneuVH9Y3hb/HkqN1QarMuLt9E7kMtIBI1yQx9U4ou8+Tuan+hNQTD/VPuJKTrAW8iiGkdjwVV3+oS/IkdVr8HBk7RHIonhUK8D+R4wfaqsSYJuHkcYOmQAst4G7EP0BIEwhwcYe6cvwnkcE38YmRroZ8XlS8U4Sm9aFQjif2dGxxrIv7nlY8AS/f7hL4FMhFvZhA24KyDDM70junNxI7BfQC/YFHgC/xi79OzEJXkYh131+GUzyW8Yx9u78Athp3oq9kAngSnR/ISZYkXupfioy+ZSeis+PXaOXVwRZoLrEePQqKmGHwrieneLxbfY7vHFB/mCORi2sAinjfH0AS5x6miuR+7nWzolNg9Ozv1A1USdYXYHywdOr0y8ZcX4ChKKzKW5JiiMkUcVOoiHlif0HXCXh+oalIz2PtTEz8ZkbdQPR5C9JYENMgYjfmlfkplJMxeB5KvQPmkXBkzqnMH2J3AU+QV+OyCPwI8iTvVLoSnxEgfiPn/XJt1lfqYYwj9s2cU/anqAB1OLjgnLuKeTgXZ5N4al5RriV+FRgAXLAd8WsvDzxXpW5cuDQQffjRy43BPhsMNTWvk6Pkw0JJAVnEENr4rYmqZiwdOAXkq4ION4JNVR0zB3J7nB+diWHzAcIgUELJAujUxdwN79QuMYyO1oPzsLcImQv4Trg5kdA7eLdsTCXmZAF6ZQF+x/EerCqEg19Hp0+DY0JDU3hjwbPEeBkyJ2wRuW68g3uMKDLkhbIF6EARHqudTrk9V10AY29H3sIgFgA/0la9vOPnZd+Pwj4YjyIjyBHPd1Q1nmpdN27xykk4iKQhr6SIOnAnshcLdxPYIjoSaTDwI+0OBrgCzAGjgG/ivrTy6txBvC/4UdiLkEdiQovIo7h3kdRaKHJUqFqAToIiFtO6q6S2N0w5shin3R1IjwN3M+Pk1V2LTh+aEvLm9xzaDvTlw4CtTo0uwFQk3gnc67si+zalaRHynH8ZuW8DFd8RcW9NKiAfTCd+lWuP7CcOp0XFeYpfilxffjTwuugV1Jwpv4q6ZhVgRorwI9QSWs8qHr197b+Pzf+toUnS92jXgjHAj8LXE78UWAyqplOzCwi7YgDbwDTQBbSp5Q92j9V+nhStUUlt+XMpmA5crLjtHbVIAakhOECwPkOLDDbVzz4rINXZvtAdKGBfPNWm5Nzvf4F/AQAA//8WLPZMAAAABklEQVQDABXkYH/aCbO1AAAAAElFTkSuQmCC" },
            { "clock", "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" },
            { "flag", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAACDklEQVR4AeyXvy8EQRTHdxXiR6ETEZ0odC4iUVKhVopK4U+QKESl8B9oVCpRCxWl5ohKgkbhRysiEc36TLKXbPZm9ubdjZklc3nf7Ny7ue973/d1a6cv+eOvKCC0gdGBWjuQZVkDrIIFDxjvZhjGPyEafoTwGpyACw94puYhdUShFQCRmsakiMnN5jVqD0motAIgmAIhop+iI8A6TAI+rRncbjxN0/RVQmkSoOPYJLnoEHdwlWOnnOj0XiKgyXQuXYCmvsA0KMYZ3M1iwmYtEWDDZ7tnV7NxW5PrmPIugLvMPF0tgWKo6d8UE7Zr7wJozNn04Uq8CmD6YxR1Nn24/ApQBTXo6Zbt1QHuMm8IOAfFUM9ajWJCsvYqIG9M9xvYyz8TX7wLwIUruiy7sMzvoysXvAugeRXOXBAISGaZkpNzAQoGQPlRQrkwR14UEgEHMLs8F5QfJaBPdM4kVS+TgOGqL/3iZyu4PCjhNwmQcLjcm0I2CqzDJKCnfy7W1ds3bnGXempPmzMmAbpvuD4PlM8WMzS/rytclZMIcHYeoFHdueK2qlHTZxIBJo6g+Sgg6PgpHh1gCEEjOhB0/BSPDjCEoBEdCDp+ikcHGELQiA5ox+8x+W8deGCI36AVan3felOnq9YBTkzvNHkMshxH5D5Y1y60AlSXNLzOdUKB9QbXWoZRgOqWxl8U1Lqu+AEAAP//DQfW7AAAAAZJREFUAwDTHCtwscqrWAAAAABJRU5ErkJggg==" },
            { "route", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADxklEQVR4AeyZS0hUURjHZ1pFVi617OFGi1rVKlr1oKJNEa1EahOVPdy4TKWspAiCSKEXBZFZqwjcSEG1CVpEtKkoW/WSHhD2WgXT7z/M5PF47r3nqjNHYeT/95zvO6/vf86Z69zPWZkZ/lMREPoAp9UJ5HK55WKaTQkugIBrYT/8QeCvROojsA8uwI5FUAEEuIPo3sImOA8WMZ9KM3xDn+2UkQgmgMCWENUNWAWjMJeGm/RdSOlEEAEElCWaWzAueJrzUB8JzRv2ryACCKIeroUm3mG0Fvie0sQGRNeZjmJ9ygSwQBbqKbKOMpYsvhHa6Mxms70iDcegjWrbIXtSAgi0HvbAx0z2C+op8pAyiVfoY0PXyvaZ9nfTKNYnLICgDzPJC6hS12EO9cngOHO2Q+1+lzXRZ05m2PLlzdQCWKAGPmB0D5xs0EzxH3oqncQ6ChdBE1dNw6ynFsBgPRHWU5YLX1moGzqRSgA7v59ZNkEbH3FcgLugxPmyk/6/YRTUtofr8yeqg7cAgtcRn7MmymFfgstY5CDsg49SUFemgfH3oI1BHA3MNUAZCW8BzHAAzoYmzrBAC9ROmX7vOmP14Wx3DOgotDmaRl1pBKwaHZavPeV3BwyKNALWWJHeYYf+Wr6ym14CuP+Licz+S/gcX3B4CSDKGmjji+0IYfsKyGQyIcJLXrMiIHmPStujcgKl3d/k2SsnkLxHpe0xXU7Alf/Rd6RE9dNFwGor0hG+pnyyfE4zuAC+pij3s9eK7ollR5rBBRDZaWinTJ7h80KiAHaolpn04kExBidoc93dMZ2iDMZWwcu0H4Im9G5x0XTE1WMFsEAxd7nFMclWfEP0USYhNg9EH7N9N7ZeP18z3r46uDJt3H8luVRPZKQAFtErpF7gldqLmkhtOp2kPJDZfp3JWqB9bXBlBglep6K6F50CCF5JJt/cpddCHp3u00dJAQp/OAUwPCp3qVfII7R/gFMF3XklBDaz+9/STholwH770rzKXXazyCkMJZ8oJgylIHsZvQ3WMqc+E1TTI0qAkkn2bLpWts+0d2L45IPqCHgFbIUDUDlVhk4MTgFMqkSVfZxdfDZaYRtLuXKXesn3yQl5/YVlDS84BRRGXiuUxWIplfPwLPTOXdK3pIgToHyk6yrZAamP+tr+stiRArhG+q/hPqKIu6N6gozPXTKoXIgUoAAQcZeyEd6GP2EREtePkZi7pE9JEStAKyNiGDZB/etzJb5G6tWwGXp9Z2dMyZAowFyZgF/CIdMXup5KQOhgXevPeAH/AAAA//+4S6KHAAAABklEQVQDADKOUnDfYyMkAAAAAElFTkSuQmCC" },
            { "user", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAEuElEQVR4AeyZR4hUWRSGq2Y2M+M4A5OcwQnNJIXZTBIDiGmhIrhyIaIiBswIuhFREFRMIKIYwYUJFVREFLMbMWBCBTFhBMWMGXP7/VWvL7dOV716ocpGsfn/OufccMJ794X7+qPMO/73oYCGPoEfzsB7ewZqa2uzsHnAbLUKregSItkaOBfuI+FH8FTAR2qDc+CvtFUMFSuAxEaQ1Uko2Rr5GayDdLWNpOEEY4cgK4LUBZBME7ibbOZCJYoIRSN6FzBnJ/wWPRVSF0D01bADjItOTNBcRHKkKoAjOJjQ7aHFVRoWwD4BFyKvQYuO+BhoG+PYiQsg8M8Emg191GIsgs2y2ewwuCLgUNqaw8XQYja+frGNUe3EBRBASX2C9DGDhIfAx36jdNoeQp2xmbI96poY4Nmx1DQF/GMiHcYeD8thHAOOQR/Wl98XqqcpoJXxvJ4j/NK01TODMRtMx3/GjmwmKoA1+xMRvoQ+7FH1+6xux/6AzyZ2UBQ7UQE4fg0t4vgqNv9j6zCKHSeo88cy0G3ynmvIK3HW8b/5Ke73Pj6L3WbdgFJKogICZ7poAzUnBrIMPs1pIT+M+ZzuQdDHAd+Io6cpwK5j3cunRgg+jTFNoY+jvhFHT1OAnq5PTbBRHOGFUPf2gi61QT3Ihhd0ZDJ6ZsiXaY5mxiig0CFr9jwtY6CFHlanSVav1b2QfeE8Bp2BdunQlBmNrytSkjBxAQpG4PlIvYkiCvAjll6rVyKXwmHQLhuaMtvwobMiPRFTFRBE7IncBeNiBxN6w1RIXQBH8BYZaIOyFvkKloPGaOxg5t4uN7hcf6oCWNvN4BaCnIM9YJSHkcZo7AXmboJ/MC8xEhVA0MZQ7/unidwFJkU3Jp7Fl/bKej5gxkPsAgimd5b9hNGyQVQE2ivvwfc3cb3FKoAA2pQcIshfsBS20aHXau20uqKL0iegb4el8Dcdh4jxJzIyIheAY63VvXjWmyiiAJex+sJGXJhd4BS4BG4NKH0yemeNgf2g5iAKUIO1j1i/IyMhUgE4/AJvm+FX0GIMidXA5fCJ7bS2xsClUMmOtf3YX8PNQUzUcJQtAEcasxE3OgMIhztobUhkFjIRmDudiW2gfCEctIw2ELvsFz0l52aVUEbR3g76UMD/SUAXs98eWw98tGCifSboU42e5nSVRmgBHIHGTJ0IfTzD6EzgS8iKAF8XcaSL/QXSx0RyCP1YFloAnnQ30fpHdZhEwCPOqpCCT+0vphh3uubGmbYCs2QBVK4+e6/XW6P9LFLgMKWh/YTdmY0IcinqWkkW7aCxI7RHX6++z2mvCjgL8j3aONfHg7amzZlhBXR3o/LKTQKsy6vV+yXGGrzfhT5sLq4vrICWblRe0etvXqv+r41lc3EZhBWgPa4biHIcvi3YWDYXl0fRArho9Mr7nRuVV/QpJa9V/9fGahrkVC9y0QIY9Ru0T0HrlCFVg70TKRd9Da8XsFQB1xmpnRMiB+nR7/25Kal+DjJbMRE5SNfOL2f4P0UL4E7wgEGrYB2W0aZ/2tXZVZXEihy/aAHKDif674q+LnyP3l9tb5PEjBS/ZAFKFidX4Q3pDUFil40fWkBDJB035jtfwBsAAAD//0V6KRQAAAAGSURBVAMALxBNcEfHNwIAAAAASUVORK5CYII=" },
            { "nav", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAErUlEQVR4AdSZW4hNURzGHZRLQolEjFwS4UHEAyK3N7e8KaKEkKKEcr+NWyQUoVzKGxNvGDOGl0EpJjNyKZLkkruR2/h909ln1l5n7dl7nzl7bv2/s9b6r/9e6/vWXnutvfa0btXC/1qUgJqamu5guDnmLUIApCeD6xB/Cx6QP0Faa81WACRTYBa4A1ORn0zq2WL83VRodgIg1gbMh1wFuARGg0BrNgIg3R4sh+kzcAYMBUFWmkqlPqiyyQVAugtYD5kX4AgoAGG22wtoMgGQ1oqyCyIirrQH+ShWwehf9QIbXQDEC4BGWiuKRr6LR8aRfnH4Npu+RhMA6cFAc/spBDTXSZz2Ga+myBDSzsC0J4z+RdORuABIjwJaTSrpWKtLW1KXeXekDyQ3ELAI2CZhPl9iAiDtbT536XEWSAGX6RlYQUUBxAvBV67VtFqGz7TXFM4Cn+VVAB3Xt/n4OqbwCOiODID0UfCTsmcryXQCpu0h5q/pUD4vAiAeZ/PRzjqbzodB6BzwkaKt9tStAaZ9opB5fSCfsQYJUGdAD2SUzaeYXqdAeAwoAjWUXbYEZ1dg2gHizTuUqctJAKSjbj4iqQd4NAREXiIyndsZ2m2Dby0w7RuFg8BpsQTQQdTN5w+96YEbAvE54B7lKLaAoF7AtGNc/910mPlIAiDeF2jz0YpR3+ZTTeOK04O5gI4fU45j66zgX5T3gUCrVwCkvc1Hc1xzvUNAS97mo6VwJcRfBsQFuulrLpWDgGmnaOu96bDzTgE0ps2niOAqoKUu0uZDZ++Iz9V0Z+1rC22HXc4SAPnDBGnzmUkaZJpKWqs14rWbT1BgFD99TiNuJDDtPAMSeid9AmioOy2IGIltmbLeZU5S0kYUtLtSHctco5/12uBq0SeAgN8gzAYSsB1oSfyI6JtgK5gEgp4Rwt3GNRr5iVbtZUZfA2S5s4s+AVwUuONlX1rracfvBLAJ3AA/IFQGtoFxlKOY7/U4fcHOdBqa+AQoGhHaCceS3wFug7g2ngs2gluIOE0aaNQPo3IGMK0EDnrdMH2B+SwBiqSBcrARiExHfFNALoIWQrI31waZve4rLtLcV6DgFKAKD4ioBsUgV0HTvbbMFGH9KM8Dpt2nn2umIywfKsBugA7iCtIzYjejsv3OI5/ustLIiC3AbtkhSO9AZpimn1luxej3xGEfWKpoy3dcJCbUGizA0UOZ5esN4f6Wb7VVVnGvfuIiCQGlDhKZdR4xkY+LjnayXHkXwDTQi98bq6eMAPza6SMdF4kNtbwLSPeoj7HpbG0yVb+Mfqzjoq4JQ1IC7GnUE/J6DrRJRj4uhpFXfVICStS4BX0et5dOHRcPWXGxiokI4Dl4DotXwDSt8fZxUZ9TJMKMi5VPRECagT2N7I+3Oi7uT8fmnDSmAJtk6HHRvsBVbioB/yATelwkJtQSE8Bz4NoPPEIXqA89LnrB9aWJCUh3au8Hcutj1xZl8oGkBeiUZvO8wujrXG37cyonI6COiuvdXv9OqotoYC5RAYy09oJVBsfj+MqNcoOziQoQOwjrO5P+ZTqC/FL58onEBYgsxCvBQ+XzjUYRkG/SZnstXsB/AAAA//8CEXB7AAAABklEQVQDAAYRg3DORkdBAAAAAElFTkSuQmCC" },
            { "orbit", "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" },
            { "tripod", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAEAElEQVR4AeyZy4uOURzH5xWFLCYL91w2rNFYWIiZ4g8YbFxCiVKD2TBFgyRkMCMsKJIssJH1YKOUmchSYjaTywa5X/L6fKfneTpz3udy3nMeb6mZft/O73zP7/x+53ve98xzecc1/ed/pQmoVqvzwUpHzCtr34IEsNjxYB8YYkGvwH1HDDHnJegE45njbd4CosKPqHwc+OzoAub1gIdRLtz6zVsApQ6CpSDUlpGgC3hZiIAOr4rpkzrT6WLWSwAf+VxSNwPT+uiscsR54kxrJud0k3D1vQSQfBqw7VqlUnngAiZeBbbNsQmXvq8Al9wNiRkT0JBtziky9gnkbE5Dhnw/gdcpq9vMv0L7XqgdThjFM3cLsG3YJlz6XgL4V6liH6wCurDZ90K3iRFsfhe8ae/I+cYkXH0vAVHy3qgto9FF0CtPiIBjVHwGQu0xCU4AL/MWwEf+k4rLwQ3ga7oiryDXb98E3gJUkMJfwAb8qWAJiO+FduLbtgMiHl+M38zcreA7vrcFCYirsoj34AkYuReCHwS2DcbjtE/BRzvAp1+KAJ/CZc2pQ0BwyT/BGVISNFJANaV+MPWvBCxMWdmiFC6YKl0Atw6TWdVJYNupaMzmg/qlC2A1etifTWubnrgO2GRov1QB7LC+JvtzFtUVxeSE1DdUqgBK2w/rUDXmElMzKYsoTQA7u44ibaDI2ohtLwpyHS9FAAuaRMF67k7PRXOYFmalCGAJh8BM4GqK7XYNzosLFsBO6h3n3rwiGWN6sau5GcNudLAAylwGE4Bp98xO5Nuc5mhuNOzXBAlg99dTthWYpudlXQtMTr6+MhqTH6OVHGvjjk/rLYDCOrhnU4ruhvsBbPsKsQfY1hflsnmnvrcAsqcd3H7u828xVgG2MVS5CdkPTAs60F4C2DEdPvvg/mJV20GRKUaxZpz3gfYSQOVLQIeQJrHTbLF+ZkqINCeKOWONKZdyWnRxt24B7L4Orn3F1eE8XFwuidDXT3MSAkdXaF3Ncd2tLgEsPvPgsrPfXMtGsWkHujeq4ZqqqS4BZNXO6dDhJhYf3IRwcRBRyoF2FsDOhBzcLE3BB9pZACu4CHTYaBLrYSdrD24yPMqpeaiP5uqnVjNQNS6YRJ7vJIDdn0KSNcA0HcIjJlHgZ9VSDuUyp6+m5kSTyPKzktrxs2yCfgc7mHVwXzBuWxrXFOXQ1duM17r0CGpyqb4CUwdMkiLP6R8Fsd2B02vzuD+qZUyv3q8b5BW4zDdxjOnqfdeI74ZLFWzEjLhOAhRJQt2gteDr/eZG2lwjfhMB2sUZ+Nvwi0zvWJW7hXh9rYriR8adBSiaxANA7z8/q18EYofB26I4jRP3CSj3gPqu+AsAAP//fX7UAAAAAAZJREFUAwCNuEFw23ulWAAAAABJRU5ErkJggg==" },
            { "copy", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADIklEQVR4AeyZS2sUQRSFHVFBfESQYHy7MQQUkSxdqX9A3CghulEQRQT/gKAL126CoKALH9G4Ely41D+gAUEDURCUMQQfEF8LwUy+G6Y7VdU1Q0/f2zMJGbnHqrrddU6dKtJze2bliiX+r2ug0wfY8ARqtdoacBAcNoZwrrYyHjXAgk8g8B2MgxfGEM4fdQ2odZExUCceg3Y9KCuEewyt41qBjAEIb4F2hVrLM8CO7Gblm0C7ohfNbRoxzwBEvSCMcySOGOE8PGFsCROtjEMDsbmvK5XKSwtA/gqEkWcN4Zx0rJqcsuTvvOfW/yAJ6U8mgyJtHgOzRYhjczjFGfKPQBL3yP1KBkXaPAaK8Dacw4JPc3EH6KN/hlYVeQzkuaelRbDwKphuaVKDm80X10CntHQpBni2DwBtDTWQx7WZARbcB0bBT4QngLaGmoBrBjwAW+GLhokBBKSm+YDCENgArGIjRMNgEo1jtJlQG4B4F6z3wTpQVkjx9xCtTNmhMgBhhRXLc73MxSMxH6IhGzU/SP5TGYBkDzgE3PjE4BLQ1k/C8RkeN46yadvdRAsG3GlpvyftLXSu8IwfAdr6aQTKqyAMT1NrYFXIzvgtsIo3EaK1bk5rwOXqSL9roCPb7oguixMY5NEldU3mQ8TZiI51wxP4ElnJbXJS11Qxcpf+ogrPAM9uMfCxyQpPYcKy1mkile+SZ6A+5WK9jTWx+2P3tS2XWRCn8Bz1k+A3CEP9DhsSascZA0KIiSe0m8EgSGoak3dY+EwjakAUMPEPjIOkpjF5hxVuSzQ0YClSJlfXQJm7m4d72Z/AVGSX9kdyRVMHIhPlwzZNq06AJ1QVpm/AjWt8Wl8AUj9pcBnS8I1sGk1v01QGEJAI6yP5keQmF6R+0uAGHDuBG3fcgfQtDFyH6CsoO0RDtDwdtQGOVL6Jk19xFkoPT8Jk8AeWs2j9pfVCbUDYIH5K2w8eA9X3/cx3QzZnlMReNJ7RZsLEgLAiMAWGgHwduI9cUkMVbfvh6gHDwPvDhTsNMwMpIx0E34Gkhirays9RsDWPUgw0l7S9uuQNzAEAAP//wfQAXAAAAAZJREFUAwCoW7Bw6pREUQAAAABJRU5ErkJggg==" },
            { "import", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAACkElEQVR4AeyZvUoeQRSG90uRKj9loraBdAnkCpKbSBpTpUgKA/khZUhIULBQEG0U7Ky8Cb0DQW9BxVqs/XxemIVl3D2zs9/MLuLKeZkzP+ec953j4uI+KO74zyhg6AZm7cB0Op0XcorMJgDiuxA/FZyPm96yCIDwY6gugonDImtP8ZNbFgGwfAkegtLkvygnKcdcAlJyNHONAszr6WFz7EAPl2yWGDtgXk8Pm2MHerhks8T97ADvNXPguXk1EZvk6pwvugMU+w43vWWe4f/Bn8nI8Y8EZb6v+FEWJYBiz8i+ChQn/GVtk3knc7G/CVYuYZW1OeatTUGtD3NQr8dTxqotUTRahItZqibCr8vPcrNFCCiKyWRyURTFMvBNItb8xaY55NfZ88mzVKy4GvJbIUqAMlJAv7Nb8j38gFiwE+6MniMvvNgi939/MTSPFqCEFNLDVidCnWgU4cjX3bzIK6fSR6GTAFWwRLD/C/imtaTkVaCzAAUbIt5r38MHb65p55tXsDCTACVwIvRQahqDdRcbE3Pr7MwClBEiPxnrngmWa003r5jazZjFJAJUEBF6CDfkB7DhzgaOtdtOJkDlIPaN0eqEbl5nOJbGkgoQJUSoEzvyPWy7PW95tmlyAaID0c+Mx6C0I9a+lJOUYxYBIgjh14xvwCt8jbjpLZsAUYW4bv5Efi5kFZCLdDXvKKB6G0P4YweGuPVqzVYd4D1e37reMvaJ+SrRJj8oANL61nVGgoOeof96qDZlm80UAPlHhOpbF8Mgpm9rT6zKpgACQ/scqbG0S9dWOpMgf0kvCd4HQ9keHK6s4qYABZLgI+MCeNczFqj9iZqmBQUomkTn4LBnnKt2CK0EhJIMuX/nBdwAAAD//8lodKMAAAAGSURBVAMAWiArcAzqTh0AAAAASUVORK5CYII=" },
            { "sun", "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" },
            { "cloud", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAEiUlEQVR4AeyYeahNWxzH9/OGHm/Q6w29F9KbIjJnChluUiRShhT+IUoylEvKmJTxGhOiyD9Iohv/yBARbq7hSmRKyJCQOen4fE9n7/b5nX3OPueec/ZJzu33ub+1fmut32/99jp7WKuB85n/lRMo9QKWV6C8AnlegfJPKM8LmPfwL3sFYrFY41gsVgFjoRKmwiBoDz/kfXmzcJDzCjCxhjAdTuD/ORyG7bAUVkM11MIr+tTBPPiXelEkpwSYyHRmcQdWQU8Ik9Z0WAg3GFsD3SkXVLJKgMBNoYbImvgf6PpIJwadxE8VNKRcEAlNgGD9iXQFNAFUXqJ40/BwDL8/ovMWOUzrhCC9aNwPP0OQqG0+DYOh2Vf8of+GrjADzkOQdMF4GP95J5E2AZx3JshBCFrus9jbMN+hsAiq4R42B30HzkIVaNXEGbUZlORB4jQy9pyqgQkknO7Dk71Cr7BNZWJdoY5yqNDvPHSjYyVY0QovscZc6oEJ4GAuNAG/vKDSg8msRecsjFvOoHFgRe+OVly0DtDKNobVUxLASQsGzQa/vKEygElcQtdbGL+DwdY3JkcPCd0vV4gvucC/BRD6xEtJAG8bwUolwYN+x7ZfaB0/euFpspn6tqNRD4dHJLEFGlMPlKQE6Pg/vfqCXy4TdIPfUICynlDZuhlPx1rm1gGdIkkJ0Br0plyD3XGcwv3nghzH207IVvRoPkESbe0Am4CeFrbPAWsoUH0SfpbBZpgCA2EE6AFyDW1FH4fVJJF0X9gE/jGjTnO1nhhbQar4fQ2zYCKsh0OwBxZDS4KMASvNMMwDT2wC9qWV1bPe81bAAknoJ1aBy/fglwmswp+uwSbwvduQ0Hp8JorRK5I4QtTJ4JfvqHirE5bABzqXWnYxgbfglz5uxSbwzm1I6KJtRBL+QxWroM+XPaajvq/iJpuAfdN2jPcq/T+9qf2z+N2t2AQuuA0J3ZwbJu1bMNEnCmU/Khswr/gDxyZwMWA2owNsUZt+MgF1T8R/7jaB03R8BH6ZS7bf+g1Rlomtp84QE7OWeyMmW1ICGD9i1MkCypO/KK2EUskcAutTAuWJTj7ilaQE4hbH2YTWEqE8mcKVGOvVIioQcxih9FWKSpKtbi0lAVbhGY2zwMo2HHovENtYyDpxvoEqfO4FKyuY42PXmJKAGuiwDq0tJcqTryntwPEy6FMkJuF3N3Eegk4vUElyi1rGbyHaPdH2TwM8Q6IwE320SGgzNRzfv4KV1xhGcXGTft6BK0BHnS68RPcGHROiSiqaSwWTP2dnkTYBdWSAjkq0ybGvcjVHhVa7HXMJ3NJmTEAzZOB70EYjaDOuLsVAPxPdC4OI3Q9upwsSmoA7ECdLwRPsepVrl6RTu1+o/wbaLem90ZRyc9AG6T+0Tjp0ZNKGsjbs+sbSwZl2gD2w6XxIX5iyNSFIIxgJOlijOb1knYB1gfN38AZewnN4Ck/gIdyHu3AbbsJ1uAp1cAn0Jq1Bn4FTcBKOg2wPbKxM9XonkMmpE2FjOYEIL3ZgqPIKBF6WCI2f/Qp8AgAA//98uTxoAAAABklEQVQDAIL+nnDxr52sAAAAAElFTkSuQmCC" },
            { "fog", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAB0UlEQVR4AeyVTU7DMBSECaxYsEflMnAMhNhwMBaw4BhwmSKxRezD96Sq8s9LYtdy/FK58qhRZDfz2enM5cXGPx2g9QH2E+gnULgD/RUq3MDi5f0Eirew8AfO+wTGcdyh+8bazR3S5Alg+p2Fe/TZWHu8vOJBHSoAC26Y/YisjCc8XWtmVAAmDsjSuMKMiC9/qADDMPwy7QNZGW94+tPMqAAykQXPfN+hh8a6xcsLHtQxCSCzWfiNvhrrR7xMaRZgapGl+yoA/3gL+R/2j9oHEQDmreR/2D9qH3gAmJ/JfxMvTtQHHgAWreU/lrwhXSA63vQASBtr+X80eriI+sADkElAWMn/sH/UPogADhAW8j/sH7UPVACB2IqSAEinFr2g5n64sYsAmG/VC2ruZwFgvnUvRLmfBcDk1r0gmS/Cij5mXyEitXUvRLkfYswCyGQgWvWCmvviydUigEwGokUvqLkvflwlAbgLrF2fBEA61eiFpNwPNzAbAPO1eiEp94sAMF+7FxZzvwiAxbV7QTJfxKPSRtYrRBrV7oXF3A+xsgBkMRC1eiEp98WDq2wAWQxEjV5Iyn15vquTANwfUK9XvNkBVtxs9VH9BNRtWfHm5k/gHwAA//+uow0ZAAAABklEQVQDALe1tH+1QSanAAAAAElFTkSuQmCC" },
            { "rain", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAFP0lEQVR4AeyZeagVVRzHr2aRWklEUpiERVS2WpRFRcsjgiSKoL0sqEgIMSOeIbgigutzRVQUFBVFVJSH/iMuKIj68LmCoqigCCq44K7I9fMd7oxnfnfunZk7430IPr7f+zvn9zvnt8xyzsy89oW7/C9TAcVisUuxWGyAfWEjHAD7wNdh53ocm9QFkFhHOBBuIsFzcC2cB8fASbAZtsKLjNkLh8Jn6d8RpCqARAaSxVE4Eb4P4/ASA0bAQ8xtge/SzhWJCiDwU7CFyEq8K7IWvMmkzfhpgh1p54LYAgj2KZH2QSWAyATF+wcPG/D7EDIz5LCiE4J8gHElfARGQbZhGL6A3dvxh+wBe8N/4Q4YhbdRrsV/5iIqFoDztwiyGkad7m3oXyHfr+BI2AyPoysgj8JtsAnqrIlbZTNUkauJ08noU3UjCyg5XYEne4QuohtAYr3hXtqxYNwO+A4DG6GFzvBoq0zTjywAB0NgN+jiPJ33SGYKMjWYN45Jv0IL7R09OWi9YE9rjOuXFYCT55n0P3Rxmc5nJLEbWTOYP5/J1jeqghYJ3S/7iC/s5Gc4jF3xygrA2wxo0UjwqOvYjovt40cbnpKtNvY1jFocTlLEbNiFfiRCBTDwOUZ9DF3sIeh0V5FDWytUUjd/MLCV3HohyxAqAGvUTjkZfa7ggGzE4QKYFFqaN1HEq3aCLUCrhR2zyipy6vfDz1g4C/aHn8NvoRaQA0gLPRw2U0TovrAFPGNmbeFonTa6XLr4vQQHwb/gNLgGLoWj4AsE+QVadEcxFAawBdhNK9FaH3jLsUERusQacHkNuviTs/CEr7AFPOgbSlLLZ6lZf0ER64j6N3TxAJ3g7MQVcIPBbY0lJHAFuvjI79gCrvqGkrxjLyIl/7GCs6DHl6VmoJ6vPJUtwO60b3ij2v5HO7WbxeN+xxaw0zeU5NPcMKVdsKRpG2EfKtuTl7fg2AJ2ReT3Y4Su3qqHTUDdE97lbgvYwsCT0MUQqr3fVdSzTWytOl+amK3cG0XpQgWgvIlSXxYQAZ6kNQG2FQYTWI8SiAD68uF1QgV4mkJhJlKnCBGgP0eib9CrU4OYXxNKT6WIEOb4vbICOAtnMQ6CFnNxGGwg1phnnzgdYBM+l0GL8eR4yleWFSADA6Yil0MX99GZj2O9WdHMF/jVpxs9JmjjUoL6emGDHEFR9VkIe4DfaB2GFv8RLOqlx45L3MffIgYfg3oy1RPpo7QtLqH4joMburwjzwAD9XXhAvJDaPcGVIV+BH1RjazEj5L9IcaPkm8g+e12XMUCNJAJ+lSidwS7lWsN3q8xOfA6PuQPEYn1aPUJJ/KVtmoBTNSZuEYhOq26sfV0qveD39Fx8Io9+Km4U2OLtROjA9RK4xahy0SXVR/ifAJ17TOsHLEF+FNwMhZ2hl3hIpIbjk33yCnaeqOiexvoEtuZpYT1sqKPad3w3wn+BPVhDXNlJC7AdUFyemPSUZNaO+UUdMEDFu3UdhyRb7sWfk7QToyaCsD799BCu7ivy2r3/cTKWguwD3gHOXJnnGhZ7Y6r6s3UBXB5vIxLfT9CBNAN53Wy2j0nKX5SF4Dvb6CFu8xmtVvfVfu1FGA3HV0+7htTVnvVhK0xVQFcHo/hwF4+C9F5yGr3nKT8SVUAvqPGL0bvI6vd95NYRgWsOJmVRruwNi1/11yMLvgMSDuTvWLgKoZUBcgPSU5D6jLSp42faYfg2QuFmu0hZwk6qQuQT5I8DvWvI3fzkskjtkx2z0nCn5oKSOi7LsPuFVCXw1wlyF1/Bm4BAAD//xRY2vcAAAAGSURBVAMAegbLcM5mKJ0AAAAASUVORK5CYII=" },
            { "bolt", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAEBElEQVR4AdSYy49MQRTGu1kQsRXilRGxsSARktlI2CFmVgRjRjIhIvEHeMVjwWzEWmQSkSAIERmPxIqFhYUFCzsZI2EMNiwIC2m/z0x3qs89tx/31m1tcr65dU9Vnft91bdOVd1Zpf/8ryMCKpVKD1hRxFgVLgDioxB/C8YpX+Qa1QoVAOFzsD0AqnYI39zqTYxrYQIgOgDB48DaYuvIc1+IAMj3Quo6SFi5XB5POHM4oguA/FL4jAHPPnrOPL6oAiCv9/sxhBYAzyY8Zx5fVAEQuQVWgzTrXgGM/gis+0Ej604BkN8B62OgmXWfAMgr49x2mL92fFrQHHd2V645APm0jPMSSkeAte75BSCflnG+wLoPSByXmlUoRV0DiFfK8wt4GecXQbexWL3n2gNCm8L/O3TEKGcSwOinZZxBSL6YIWYFRH999Jy2BUA+LeOMQP6Ogs6g+wRAfgPkvIxzD/InqAtteXhDuUz/TTmwkBgJa/kX4MHL6P0QWHuFYy+wZnedu2nwJAem4HCa/nXWkgA6zqPXA2D3OJ/wbWf0f3CtGe1X1W7iFs7Y2C0JgMMNsAZYE3llHOv/bB0R75Wma+GaCkCxTlX9pVKtT7Wwh5GvZpyq7+8V/zcK10Bsu0Lsr2HQhgIgn3aqOkugm2EgW6Z+CN8SsLlN7KO9Z5prh21FqgDIr6fxZWBtDHInrdO7p90keNoqiPEcHAXW9NpsIU7dXFMjVwDktQ14RIM5IDTtcXaFjsjlRqv7lPeshADIK+MoXdqMo/4X+NdLm3byuU2nhEgaMdPm2jAj7841RUkIwHkJeBkHd+kq/9rN5R8g572KhJo26hvNNWXA6YbO/zoBBNLyP+i0y+saJvZ8Lwh+re7eF4y7jHzTuVYngAdoN/mTaxG2yAaFfNrqrrmmLGa7JO7rBKBYnz3OJ1pFcBD7TRgG8ppr9/HZuabVvY/2iYxD24TVCVAtHU9x3Qjazd9he7tGTBLPmt7ttdbJfdrqTlXSEgLUBBHPQMv527ZVDIN34T2jn3ae2Ems1IwTxqiWXQHVyhxXJYOwe+0wA/lWzxNh/9RyRwVAXqu7t0fS6m7PE6mkw4roAiA5mwfYw8cE/kJW9+gCIL8SlEFo2gZ430y1x9FHgMypuwgB9igpITpJ2W+mIi3ySt1qkwlFCNAvYMmssw7uh9rNOPRJWBECbAZKPBSH/YKBK5v9CwGZM44nsdMCtMeJep7opABlnK2895q83mBm8hUhYFHJp6KMo3Tq12b0RhXAYuVlIFEbYOTb2uOoUyuIKoAHeiOsLxjaeVId36IKYJS/QzE8Po7ia3qqok9miypALCC8n6tepR7KBykXatEFiC3Ex0HdGUD+IvAHAAD//3FC6NkAAAAGSURBVAMAp7WMcGJ40FIAAAAASUVORK5CYII=" },
            { "mic", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAEm0lEQVR4AeyZW6hNWxjH1zqndE6dS87pnONwLh7ccwnhScjlgRC5S0kkl5AXuefypkRCyaVQHnggQhIlD67bgwfZJLftnvutxPL7r+ac+zP2tNZc86Jk7b7/HP/vG2N84/vGHHOuMcf+LveV/2WWQKFQaATae2iU1TylmgDB5sEicIWA34CLHt7IBhaCPLbUJLUECOwPojoCVoAWwPoWl20l9iO0/Y0yFZHjxI4IqDFOzoF+oJyoTQ19finXMEp9Kgkw0DbwH4gq/9NwI0gsiRNgJkcTxVBg5TnKcjDMg5aObKiBjKPv8ECLSRInwLizgJXHKK3z+fxSsNfDYmztwAtgxe1r6yLxNBLo6Iy0iqDvObYctjpsq4EVt6+ti8QTJcAS0Lr/yRnprKNb9bRV4I3x8TdlbEmUAKP+CVx56hqM/tBwnzb1SZwyaQJxxky1TzWBVKczhrPqHYgxaal2qd6BVKczhrNv4w7wa9lUiDFBsbpoLCFK57J3AEc7cKR9TB18KzxTYYyKxiuZAM5+JtqxIJfLFa/jsf1YZBlc8F3xeCUTIMZm4Hvgiz7Ou/kK5V3gir7OXJuvh31KWh9daOiOpw0j5nApl8CDkG7BBs7bIj9y2rRydKu2sQr8AT7uUPrSxCembLA1N3W5kgngXB8nBdsB7u7hL2CzolOJv6xBnOWhXed8cYPzhot20sXgHTE8M3oDWjIBr3WNV/rFAJ945Tqv9Avt72sJeBkYCkaAZVReAu4Mr8FmxfXtJmjbFnmUBA4WW9ZfuhPQr77KDO2H7wRWdOKwBMNesBuIywYNZBt9dQxTNOBT9XoGirp3ccf2zPVFnAR0MDW33kWR6dv2ZpFFu9yg2RxgZR6KfFMEkjwBZukU7nTCRhHIEmYsWK+0eUJNe7AFlJPNNOhAn+CUAl+dsS0AVmpok8oSklPXuWyHGTh4WBnsBZhMRX+wB9wHvohrKfWlzRQQnE7gQw/3Ib+hKRca/lkaZQnpROEAHs4AK3ogjxHAv9ZIcEfBSKD61tS1FAejwDH0QOjbHOUoCCYCLjlJ28Mi5RApAc/JRMpg5uASnfVcIJDeUlwQRC246tql00d3SkukrXQDLa1JRi9JIydAIHoNjgnx9ju24wSkQ9vQRKgPhHZ9ge6E3kBhv8y6ezrdDvqUIpETkBOS0FthungINKNK5A4BrgHTwCAwBIivpdSvrpZMn5D+Mk1lDCUmHgkVJSCPDKBD2cHwVyBM9EM2m4oNQM/OPkpxvWpVh9pA5Gsgvjc1qCljqDgB+WMgBdYVfgskles46IrPsDcRVaUlVgJyyYCXKbU508xeg1cqerhn0Kmd5wtaucROQEMx8GugvZD++9IL23pQaveoZ0Dte9KuFX03AP0rCjWeJErAH5IgCuAEmAm0zntQdxu8BQpQvBt1zcAsoPe8u8ulaeWSSgIhw37A9g/4AegLTjyVgPH3iWSVwCeDZKlUEwid3S9orN6Bz0x2Lfb3wBdx/W74emplJneA16R2lLtMlNuxvTR6ajSTBBQdAU+g1OuzCTzy9pg+FUlmCSgKAq8D+hqTmgk+AgAA//9SnCJYAAAABklEQVQDAJdMUHAFy+q/AAAAAElFTkSuQmCC" },
            { "loop", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAD10lEQVR4AdSZ0YtNQRzH70VKoUjs3aSUbSW05R8g3je1rSQv6vJE64GIbEvR5sHGi5b2gQcPPNhXCu+yhQexXpS72AcJr1yf77bnNmfOzL3nXPfsnKvf18z85jcz3+89Z2bOzC4rdfm/Qgio1+sV0NPObxlcAMRPQ/wzqJG/RJrJggqA8CbYjgPxEMbw3aCc2tQodXAOgWX6rAPTRrKICCqgXC5/hflVYFtqEUEFiDUixkgngG2pRAQXINaI0ERuS0QhBPyPiEwCmFw7wd68gJBp8AjYptdp0naq3FIAZC+CWaDV4i2NnueMIfp3WRUOM3aFVwDBG8BTGlwB20ARbABOW00iTgEErSPoFTgAimTaN1aYhJwCCLgNtoCi2Qwr1qxJKiGAX/8QAcPAtJ8ULoODYF/OcE1ihixNQn6PMiYSAqg8BUz7TqGfxqPgMXiRFxhnELgm8QRjnqAuYS4Bu62o6zTWlm+5O1vkyesjbsTRq8hro3NUlUoxAXSi9361FfnSKne8yLhtkReRmAAcG4FtP2xHJ8uQH6W/zL88bRbMFrDgXKr/IK/zwAXHeE1fGzM+qACIlIF2eJKGpSavFhkEKLyzWFwcztCrRPwl1UrnnbDUJ8wW8IGIPyAy5d9HhTxSRNyk382gQl57Ddn0FhNAB9qwHhjN7+H7bZRzyTLGHJj3dc5c6RVc9TEBCqCjo6T6RXrIHyMf1CB+HwI1gfwUacwSAlQL8Rr4prwLdNT2PY6rP5+PcdZQdxhEdgTfqqig1ClAFT7Qgc6w0T3OSV9ch/z99LMcRLaSzA7QsEwCIK8dU5dPaieM46s0eguQEYlUw0JU5O0d07WOp+qvU0GpBHjIi8M15kruH3oayIeWApqQ146Zed32EfH41zv8X0xfUwEtyGfaMc1BM+S3W7HzPPE50+cVEJo84/dC9DwwTed0sxw/D0Q1NNYdjD1hVa3j3jT1ud0N0fcQ0FL9jgHtvxkkbu8ST4DGr2lYBS7TcS/ve6GHDKylei2paVO8Pk9Mh/IxAZDvw2kfKXEFt08wcM65mACCVNanLdnC2F2Y7OLX14cm2biJcMNDkD6d3zQc4TL6DtOrtB9OVfDLRyUmQEEED5DeAS7TJM77XqgPDvoSHiZ95iJh+hICVEnD46SJGY9Pk3iQ+tzuhuj7I+OkNqcAtaYjTRqXCF1167tIYcHhFSBm3SCiqYAUIrReKyyOJSy1FCAuTZ7EOfaO7jgPeER0x3lAT0FYFKGrQG12usc5i6/Y5wERNwFhnQF0a6F7nFtmXYh8qjlgE0NE03scOz7P8j8AAAD//w98mWMAAAAGSURBVAMA4h0Mf7Ll8V8AAAAASUVORK5CYII=" },
            { "shield", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAExklEQVR4AeyaR4gUWxSGnRd4TzEiiKKIqJjjwoVjzmEjGFBnIwgqIrgQ0yiIGzMKghsTgmAABUEEAyjGMSIYUMxZUTCLiqDj9890V19v3+quqq4yQA//3+fcU/eeUHW6usL8VeMP/0u0gMrKyiawcZL7KPYCSLgmLIOHSPwxfIJ+EE6E/zGOFbEVQHKlcAPZPYPb4BAo/+JQ9O3wOXPWw57osUDOIzsikaZwAbyBk1NwCqwH/aBtU9lYoTWwHDZlHBmhCyCg2SIPibwEtoFhoTVLWfQQn5FbLHABBOkF1SIvCGq2CEMn7mBdDldC6QgnlEO6xV4QQy1W6pzpMGqxw1xtwpnZIiexqkVqI/3wlg0qsrSkpKQ1LIfzYGvsveBGqDkIJ+piVYudIvYNmLfFfAtg8Xyc6SwSpEUOMreMROvDafA04x+ArQIquSZsKIOHYS6kW+wxucz2m+gsgAXaE+pPv3Wy3+RjAWxGYsPhDvS8YN4nuAMOZnIzuBDKF8IXy1M5ZU1wFsAsVV+CtPEOg1qkJwm0hcvgE2yRoLVwKWyLgz5QLfYBaeNvDC1hFvwKyJqIYRaB6kG1yBnGsQK/J6FarBGO58BACFPA8UAeC5xEEZ9wcRTa0FGwbTXCFJC1+HcwFAv41UeheASKR6DAPVBsoQJ3YMHLQxyBgmMl4qBYQCK7NYRTvyPw2uGjgcOWlMkV65UrmF8B95lcCU04L2fNCTHquoMz3X1l8ABmwVkAV4RaoBt2c0Erc5CwbhfwgJy+uWI6C0hNvJuSaTEirfwEOdyK4ftQIFcBRywnnbmt62HZYh8SQzf/HSzHvvfPuQrYjBP7ezANW9KYbgVQ62yybN7QtwB6To8ID3gzq5VJ7KH21Wr8n/jugteJ0MQ+cnlpGkzdt4DUJB2FlFol/uFzO4HyrWNaOOBTt4w7WWX7tnNgSgb25MyWam0P4hw00Y2BHoUgYsVivNlH9zx7fy92X+QsgMXqPz2E+mJ5WMQes4NZU4IP8aXW0TMmc9FnBhNgTuQsQCspQqewRdINqpUOENg+XxtTgqn40O+Lvmt2LguJbZ/Ks5zai7ImpAyrkHYrNcd2hgQ6IyOBtZ1YeBbqcSPCg1pnjTfKoQQqgD2hVhqDn0fQREMGJ0gk9AsL1vRmbQWUD4QHXcaM8kZ5lEAFyAdF6EFvP3Q9Xkd40EuLYyQ0w7PkUZg7kyn6oayDNKFT9wBiSZp2Xz1wAfKA43vIAfANNPEvg3UkthP+j+6EtsFdbFwLtQbhQVebfYmhI+AZ8ymhCpAzAlxD6v3XR6SN8RgukKS+H6gZYGvB6CIcC23ogW5/fN+2N+Qbhy5ADgl0ATkQug51R+zXSXgusgro5Sgq3HXqlY+B+LzCnNCIVICiEFBnj+7oJ6CNWhhWkPgtqNOw3jXUxGbjGIZu+DqPjITIBSgagZ8j9Z1Yjcwgo+l3wu9GSGsG4cM+KWRWB9AKKkD+SeAr1CugcYzVy4iceM/W0VoDdePEMDoKLiAdmmR2o+tNy1akfjcQP0C2LVjaMVfXWKiFI7YClAqJPYWT0LtC/asBogr7+ezEtsnwKXpsiLWAdFYkeRUOY6wr167oI+F1xrEjkQLSWZL0JXg5PU5CfgcAAP//cBtGvQAAAAZJREFUAwB6ZWxwZbF3ygAAAABJRU5ErkJggg==" },
            { "skull", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAFMElEQVR4AeyYW8hUVRTH91dRUWkXSipKyzK7WKQhPQVJL5GZRWhRFPnW7SXCz3oIC4LMblRKNyisXsIXlS6CZQqFWZA9lPRBpOVXRhcIiiIqx99/Zs8+e5Zz9px9ZoYPwWH9Z6291tr/vdc+t33OYe4g/x0qYKIP4NCOQKPRmNZoNOZ6TB1WoQMpgEmeAB4A68F3oMGEd4NPPZo+/LvBOrAUHE+sb+mrACahiT/JLH4Aj4HrQGq1pxFfCFaCcfqvAH0VUrsABr6VSYyB+8ExIFeOo8My8DVci9G1JLsABpsM3ma0N8EU0K+cCsFbcG4Ak7GzJKsABrgA9s/BfFAmewi8A1aARzxkv4s9DspkAYHtjHEuurJULgBindsfw3wOsPIvjjVgxsjIyFRwLXgQPOwhez72meScD94A/wErim1jLF0rNta1XakACE+i9wfgRGBlNY7pTO4O8A12UsgZA7eTNB28CKycjGMTY3Ybi1CnVCqALq8De2i1gguZzL0gdWrQ9UChzx5wF5EbgLhQQWZgvQR6Ss8CWIkbYbHnvE6ZBUxgA7G+BI51EOjWKk7MIIsY244bgm0jWQAEIyQ+BWL5h8bVDLwRPRCBSxf4NZCJGxVklZ9DcFgjWQDJtwB7QT3KgJvxD1TgfB9CPeBQQc7CWgRKpVcBy03Pv2mvAsOSpyHWGKggDwWri1FaAIfuPPJ1MaGCrGalfg+tARue+xVDO8vPxbhbzdICCM8BVp63jiG0n+3CeWkXX9OVKmB2M6P428sKfV80h2Mxxrcw26Ns50JKS1IF2COwo9Wl+OfQas+/Eq0tsvA49hlFRqdFrGr+9s6ertYRmOs6f9oDBQ+T0Q5yJ46lQPdxYRR7jJieHZiF4MvJt4tV6wiYLbILpEzmbKb2KrA5uJx8a8jR3klth52VT6cvQCyT4kZsp04huz2In5R3Q3IsKBPF7oyCufl2a2HnEqhTBewNWS1DW+mW5dystpHQl0Sx3HztSqPu7se4Eds5BcSk8dGI+WL7/6iRmx8vlmhqFWA7XSQmj8+8TqlPomBuvi1A79wRXWHmHAF9ImkfBT3Q7L26YHVOsRdc8auczwWvHYC9hdvTOTCnCtgSslqGdqZ6gXc8bDTBJbj/AlbkW+JzmjFvV83Xi36zX/S3LbI7zNICGFSd7NV/Gys0RQzEtY+fif0E0HvBerR2kzN9jGYh3pfMh/sUeuhtDRVkF33j0zEEZJQWoCB4DcRyFI17QFMgHgejQG9m16OXAVt0M1d/ioFU/n3kHQFisXOIY65XAc+RrVMCFWSUlboitAZkeM7mKRpR/oGt6wfVXZIFsFq/0k2fRlBBjsbayID2QsNdT+C6nJ56wzsSHcty5qDrLfZ12MkClAmBzuuPZEfQdmEzA8cPqyhc3YRD+5xN9BAnKshWxn4mtEqMngX4fjehfwax6Jvme7Gjpq2PYHav8wtcN4OeUqkAVkIPtatgs/fj01nBC/HXEvpeTMfTQCy6CcxjzJ9iZ5ldqQB1hvBLtL7hoDrEnrcdwR4N3dVsiu5mX1lnWbtyAZ4g3t94lzu8bUyEzi1gIuaYHPNQASzPHC7GK+uAvpcBK/usI9XOPQK6G1m+l3F8WBPdvk5nffnIKoA7kQrYxWSHJTsZ47cc8qwCPLHeb71Zouq7s7mzC2CFtGfRJxK7yas/bef+pPNiuLeisyS7ALEz0FqgLw/a0M3D1w9mwzUJrIUnW2oV0B6FQXeALX3CfgNq01fSfRVQaYQhJx30BewHAAD//x5rP6MAAAAGSURBVAMAUgLecJtc4YQAAAAASUVORK5CYII=" },
            { "scissors", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAFQklEQVR4AeyZy6scRRTGe0SyEjGCxijoRo1I9E9QQV0HxQe4UFAjqFFiiIEk1zyMiYb4Niq+CIo7XQTcBXXnQnwtXAjRjYJRgyiYRTbqze/r6Z6cOnO6p2pmuJdALuebqjp1zqnzdZ2Z7q57TnWG/50lsNwbmL0Di4uLK8DaBiuWO/F2/V4CJDsAC+BHHE6C7xuclA5sBwN0yyadBEjsIrI6AvaAK4G1VV+6Z9AfwfZC2rkLcVeDS/oCK5GxeZxWovwa3AImiWy+xef8SYYl88R7Gvtj4Df6j9GGEhLA8hC4HOTKFRi+CeYiJPwagZ4CrexHF+7EGAEM78ZrHbDyDwNdkdtoBZWOdAxHcg++t49GU3aI8TauG4AVfc8WraLtjxFg4nFg5S8GawaDwU5wuIGuzrXoTwAr3tfOTeyT/EGM1gMv+1j3D6/UOCJwvSYMDuD8uxnXXXS/0nkRWPG+dq63T/K68o8GRgdZSz8kwVRVJQQIoro/z1l+5cZ2+KUd0F9JjNW0RYKPko+uvJLv/AJrkYQAiv+Al4FXmHGpvXEddmdJXhESAmyVyuJPTRhcbfq+60vmODH00+ftwjHJv8pEdOWfJ07vlcevloRAramq75q2bXQnXtUO2pbFb6C/H1j5xg76+vjrCxslqeSf7PO1cxEB/QZbG9X0URbcDdaBO8BuDD4D3v9ldBMFf9V81xc2O3kt5BOo2LpPmPgQWNFddgeKw+AjoP65tF5u9Ao/Jvk30EVlo1+7aEcw75YxAo2pfs9/afolzTYS3NflwNzrzD0MvCj5LV6ZMw4JsAt/47wWvAdKZSuJHvBO6FQ2j3g9Y9X8VMnjm94HpGgBiRPgQca3go+BvROqr1K6Gf0HwMtmEh6RoK/ko7JR8kU17xcKd8AaQeJTcCfQw9Qa5q5SH9wFPgf3oYse5GoSJP8W81Hyz+I7U/LE7d4BTaao9AU/yqI/Ve4PnUrjHafWcDMfDwEvuvLbvHKa8cQdyA0KCSUa7YQP8Ry2M1/5NujcCCggiWkndHfVMILKZms0Ma1urgSaJC5oWt/MrWxs4LkSaL6w99oFmv4edmduZdPErJu5ESD5d4mo7wFNIntJXnfuWondxBf12jDzI5sAC3eeCzGnG94DwZp6k1po9dg9QV9Pq3pR30l/ZuklwII550Lvk8X9wItqfnurJNbF9O3T6y50/sERkzLpJEDw3HOhqOaVvK95vRj5F/MNrDMTiZAAQUvOhfwl20HN++R1E9Tjx15vzFgkXqKdSkICRCo9F8KllgWS73wBZ05HM1GyG7loL9QRCj/GCBAo91zo32CtHwJdooLEJhR6G6NJZBNrF5fTGAFC6l2AZiRd50I3YfE/sOJ97dyoDwm9uEQ7oXIq2omIgH9R18tGdC70BRn5cvG+mMQCCe1EREI7EenDQAkBtnBJz4UaElE56TuRVU4JASiWnvOU2rNEKpBQOUUkVE4TSSQECFZ6LnRNmk5VdC7U+rLu1CQSAk3A3HOhS7H3j8bZ50L4JtKQeCVRDgfaCR0mD0fuMyLgt63rXEg/mXrNtCGzzoWsg+1DYiPjqJx0UODXwrQaf6UkSO65kM6K6iDNxyF89S+pZjhdQ4yonAZE848hqKpxArW2qvR7XnIu9DN+uno0s0tDYpeJtAWdHkWMatiNSkjPLSXnQnoPuI4FTv/HZhh7pk/i6fjyMoKsou/LGvVQQgKawinrXAi79cD/p0YhZgZxj4HjfYE6CbROBOg9F2rtlqudSMAmBpnwXMjaLHW/iMBSJ5ez3hlP4BQAAAD//92MMTYAAAAGSURBVAMALnvacIX5aHsAAAAASUVORK5CYII=" },
            { "move", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAELElEQVR4AdSYy27VMBCGc+AdoC08BSxhASsktt0gJBAghFgUUcqC+62IIlWipaILBBIIEAsWfQYuKkt4i5bLIyDB4fuDkyaOk9gnjuhBM8f2eDzz/07jjNmRjPm/XgkMh8MpaZ971BsBgL8G+IaU/lvaXqQXAgB+AtoTaCbHjS0bR2ujEzBAZxwIZ8ycY2p0U1QCAHwMFBd4zKmIxHLai/QTjQDg9Wdz0QPXrPH1cG13iUIAQNrVpp23kUR7Ep0JAP426GbRUNGTuBm6yPbvRADwUwS8h9ryyTYw/ojacp0Yk7YxZNyJAIl+o7/QoqwymENtkU1zRfuAwRAdWToRGAwGP8h8Bc1kHtsFBgJGU5KhmZsvWK9i+14YB3c7EVA2AOjo3EN/N/07tI1ifDL/lUZnj8nOBJQDUJvoT/UbNM+Fr49/Q6itqTzolmm8el4EdFKgEx2p/fFdT65J1CtfKwEC3SXxJvqNfsjHiiXhQg59U7J8t9oiNBIgmMqD4ou5iM1rZxyJG3PJn9i7aBfRTOaxCUM2rrS1Qc1Ce8d1PHY6tysIygZXfJUdtSScBACvo9EGr1QPOUF09qsfqq3ETewFR2CRUL1VmaoQALzYuqrKVRIUP0KVYC0G7W6LS5KQQ6WJ/cVO+KfaSdjobkmJAOCXmHLt/HqSJGvMH/JRYuxHbdnns1Y+LFxD11Fb9CQeFY05ARa+YOIS6pKDGN8H6FN8bXmGISSGcrKkInNgfZ5ZUwIYVFWezoxj0J4Fc3oapgTGAHAtxJQAL44+HG9qvbbfxEswp1VsSkD4MJykdR5V2D+jhwP0PL62nMMQEkM5WVKRZbCeyaw5ARmYcF06NHWAn2nmP/govl9QW776rJUPC6dR5aQpiY5yYcyNJQKyEkAXEledriOstTZRjBpt/ZBpHS+nShfXUb5isMkt1woBzeCogsr1MblGgvTtl1+gtn7IiK1a6IYjrnZemCpTTgLygoSehE1CILx2UjFGUFd8gRcWZ7haAvI2JIrlg+6wo9ZCrfcB8im2644tOE5tJKAVBNXfZLQ7rGI2KflUSGb5lLvJPWkloNUEjXGH9coVms87qAJvR41CgNPD5w6bvwOe/l771ZkAYHS8qRTRnbn1O4F/1Dt2JwKA2c02FcuP7A7rOmoH+OtCUnwxdcf+r/83uhMCtugr+sA2Mr6Pao4mF9e5n0/6dDo9AZ1OJCnuKMNUjqS/5Z+j5WE6WiBGWlWmoxF+OhFQPgDoQ6erqIYhusRaPZWQNRXfzgQUESCXae2yA1OtqDzQmloH34koBJQMEqpX/lWxMtSrs6qsd2+eiUZAaSChI7XpSWjn5SP3KBqVgBBBQk/CRULgNSe3aBqdgJAZEu/UN/rK2MwwXtMLAcED8DHavegE/VO0vUhvBIQW4BuoanwNe9G/AAAA//8MtLtVAAAABklEQVQDADgAnXAXFz5LAAAAAElFTkSuQmCC" },
            { "rotate", "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" },
            { "mirror", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADmklEQVR4AeyXy2oUQRSGZxRcCOpSclmK4pMIvoEYV24jBrOSKMbrMjGYrW505VvoY+hKYoKKIOJO0PH7O9VNd/Xp7qqeLoeQCed0VZ06l/+vqq7OnBgd8b85gVlvYNIdmEwmi9KUJJMRAPhLgH+Wuj7d4SUJAQCfAeoKOna6gu0c/cElCQFQXkJPobmofyEfDNmmIjAkxtZccwKty/MfJo/3DnCzLKGLfRdasehS33jF9d4BCj8hge75ffqv6EcJMa8J2Ef36D+m7SW9CFDwBdU20FyuYzudD7pafM/icw2V6FtxD5tyahyl0QRcoVWviu75y56tbXiRyZNoWVZd7rKtsx9FgALPyeiDx5TJJHtO9xCJ7ZgUwQQAry2+3ZJcR6FlOnhqzdUKCggi4BI2rXxQoUgn7YQWrDOsk8AMwOegg0i0EgC8zqO18u/zKqX2b6nfp/vOCBIJYTCmDk2NBACvLVw7dKs8dxndQX2JeQesF145ldvP2/pOmAQceG/ls7y74/H4Fj0LrGXDNVxcbouEdkILWktWIwD4B3hZ4HdcAaZNsVbVdMTYSNbV2MHHF5G47xsrBAB/HofyF5ZhJlp56zhlk+7RCMrNdzVFPCRUy9qJu2BcKCeqEGDC/zpiGv3h8RPtkmlfYtUp1/jBwLeJZGWnKwRgfkCQ/kmjKUSkNmDeehsU3v07ApdFu1o6Lqqd2dzjGRi/uH7WVAjIgsMjWmv7Wm8DYmq5sMVIFg94vaw6Qn6sjnHtv9YsyPeEhG4ai4ReJBWobKOLt2xuqtZYvmCfKLd1gQi8MNUSmQTk5Uhsqe+pCjz1bBrGvAPFcVGgU62ucrth0Ww5LIWh3GkkICcC12mtnbiC3Rf/vPrzXeOrhoNWXhiMqUNTKwG5QEJbZ5HQdEoVeNVurdFJQNEzIBEEXtiCCMjRkbC+kJqWWi+m7LHa9cWv5AsmoChI6HprOk7Wi6mwGNXKq0ZwTBQBZYWEzqX/UdMN9Enzgbpn+G273MZUsymagFJRSP/6btLXsRH4dWzfGQcJvl9x1O2Sx29iU07McdKLgEpQ8CHtMrpAXz/26YYLMfrG5PHKFR5c8uxNQDkAcYB+U7+PEjtVvGpORUAJZq1zAvMdmHIF0hyh0egjuH6juaj/IR8M2SYhwO2in6BvAap7XvoG2y/Gg0sSAkIJ4Bu0uueX6d+kn0SSERBagOue1+9sDZNoUgJJEHtJjzyBfwAAAP//rqmFQAAAAAZJREFUAwBdd/phCl2aPQAAAABJRU5ErkJggg==" },
            { "pin", "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" },
            { "car", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAACd0lEQVR4AeyWzS4lURDH753MI0hmJjOZWcwsrMcL4BFYWyA+EhZ2YsMLiI+wEBtiY+MZkFhbC0sJicRKWEhw/UradRznns/u20RL/fU5R9e/6l/V3eVL7YP/VALKbmDVgaoDiRWoHqHEAia7Vx1ILmEiwefsQKPR6ATdOaMzphnBHSDpVQIdgb2ccQT3IpxBFiSAAP9gnwBF2RQx/oaQBwmAeBQUbSMhAbwFUJmvEA+Com04i+UVx1sAbH2gA6g2zaYnETP4qyYxJJZ61nIdImBcY7lkv1Cv1/dTAMc8EC4uTRtrrhwLLwG09Dc8UmkuTdsg8bvmLnKRcWxq7r1ZTO347dZLAG6TQDf5nOpnsfsVg6PX184pgEqYXt5dKndqCBp1lHHJXFH9h7LY6tmbtVMAHv2go1bj94sdQJ7rJIb6AKjm9TL7CFhWWbP1HFepWJ6YhVO3Jf1A31sFUGWZvN90pzbuf5DDH1s8qwAcf4KyzZqDS8BN2dkT/xa0NJcAk6MMGZkJRUAflqb4r85iBBzy2Uuavq38yewQBFmMgKAARd9cCSi6wi7+mA7859vcnyGvafzER7JdQLcH/UDdxwhYh2AnQ16T+JlvDV7drDla/6gzvce9S8AJSd+DskxiH9uCWwXwvb7CeRuUZVvkcG0LbhUgjhAMcJX/R2TymiZlymS28X0n9hCxreYUIN4QnYN91qZJGT2ZHXwX/N1pXgIUFv2dcD6jiq9pmcwXJIAu6O+E8xk1Zf18lgdfkAAJTFB5J36x9npGuc9qqXzBAiQbgp4Br2dU7ncBrmi+KAGuhGptvKES0MZiG0NVHTCWpY2HH74DjwAAAP//bAqg8AAAAAZJREFUAwD1IZtw/OWDwQAAAABJRU5ErkJggg==" },
            { "check", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAACHElEQVR4AeyYW07DMBBFExbBQ2wRpAL/tPAB6kdbEKyxCDYR7kGxlJrJq06cVDLyyI/Ynns8Tmx6lp34XwKYOoApAikCgSuQtlDgAgYPTxEIXsKGCYqiuJJdNnTJZhsBCX+S8L3sS+Vb5WaaJYAE76T2UebSWm1mJGYHIKGIXzjlZZ4rL2T/0qwAasQj+jXP828Kvs0GoEH8m8Q/+8JdfRYALeLvnFgrnxwgRDxAkwKEip8UYAjxkwEMJX4SgCHFtwLIWeUuQvcw03xbzeAfUmrKdvpUNn5t6GRZ7UssZ0zo7iJLa3CfNs3HCcuc/jC+8/d+Y9e6CSBn55pgLXNppbYPV+mbayzirZVHvAXV2YUJoNHW3eNGQnpDaMxo4qXTvk5rP3LveKGDZ0AgyGu2q2OLx2tdBDJBcB//pJNnCwlrjYT6ADrKtqnqqQWgkyD4R8KCIBK1ELHEo7ERgA59IWKKR18rAJ26QsQWj7ZOAHQsITiIqFaN7bSR+I0arT2/1digT6XmrU2dAZhBQh6UW+8EBxGmxweJ7zxjDhqHrPQCwLEgeLHfKbcY4kdbeee7NwADBcFWsSLBYyyKeBwdBcBAQRAJCyKaeHQcDcDgEmJFubSl2kbfNqWvvywIgBkkmBP7WuULlWt/PdDzUVIwAKokfC/7oRzbBgGILbrqLwFUV2OKcoqAueoRG1MEIi626SpFwFyWiI0nH4FfAAAA///w+s9vAAAABklEQVQDADH2vmEPTykwAAAAAElFTkSuQmCC" },
            { "crosshair", "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" },
            { "key", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAC2klEQVR4AeyYzWoUQRSFZ9SNv9nGqIgLQ0DxAVypLyAuh0EXCoILwRcQFNy7Ce50oXF0KfgA+gyCBtSVmoSgCOMPLsfvFOlKd09NpipTPV0JHe5J3763qu45VZnb6dnT2uE/jYC6D7A5geYEJtyB5k9owg2cePruPoHBYLAALtSMha2OaegEIDsLeuAXE5fBm5qxDJc+WAJH4VKwggAGXCH7GXTAYZCKHYFIF3yE42Wu1qwAEieIPgMHQap2CGLP4WpPwgog8RKkTB56xsRxyXj8MgJQdAr/PMjbF25ug4s1Qxy+wiFvl+B8TAEjAGcGlO1uu91eBG9rxiLE7oGyGc6ZgH3lLPfvQSr2zkFkv2KZAPljwbHNAXN0rsHkKs27anoLgJw61AqLfNvwcTdtI1ZZfrNS0fMSADn1YT0bstkdYuZvUAH8SvOqMQpeApjcBmXbmwtUnc+VKrpeAuhC/Var9SI3tUfsZ3aP38evLM/aI81LgGZD8ipXfYCP41/DLxixSvOFYrkbbwGaA8lVoA+qbodArtL8UEECQQIYn5xtSwBdR/0+9nvC3HZ2J1gA5LPnQez3hBXWfhIqIkgABcr9PrTeuPFdahwYNyifDxLARFe/JxzN9GwRvBcMEkCXKfd770KeA59S47fnWDMsSIBmUCDr97HfE2ZZ+7pqhCBYgBankPp97PeEda0dikzAmmPiWUesrtA5R+FVxYwAdlRP1x8K5HCfjnALxO73oevdgVP5jWwdzmbTjQAGyMo9+CTBRyB2vw9d7yEc9I0JF2uPMy8v4AHB7yB1E0dxNTytAI5E7esm0T8gVfsLsRtw/cfVmBWgOxKvuM4DfUckQbhJmL7m7MHkNBxfc7VWEKAoA9ZAB+jfhjPEYvf70PXm4TIDusB8cOFkbUiAzeAw4QOI3e9D1/sElZG2pYCRsxJKNAKchzHFYHMCU9xsZ6nmBJzbMsXgjj+B/wAAAP//zqbzkgAAAAZJREFUAwCkRo5/ZcbNJgAAAABJRU5ErkJggg==" },
            { "tag", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAEO0lEQVR4AdSay29NURTG76UDE0ljIiGhHYixSgxIBAPxL4jEM8Rb1aCtpEi9KiHq0TaChgj/hAEDEokoMTOhggQTJCQiqN93e+7NPts+++x7Hm0q6+t+rb3W952zz+nep2ZVZvi/mICJiYkFYE3BWFjmNWoIgPQdEn0ADwrGe2L3E7MUqwkgwVyibwRlWR85LpYRvCaAwEvBbFCmdZYhoi6gTOJmbIm4YHbkrfsE7CL42py4ynzburgT5+3OrG2fgLFqtfowJw5AbATYdgQRhTwTPgF20kab5G1gcaPDU+EC7GXYJULLKbeIYAEQbgHdYBxCb8A49ddAS6KFdqKliDibODFgIEgAJEXwCfEGgHnl22nroXwc+dB0m0dED3MziwgSAKU+sBwk2QoGeoHXyhARKuCgl9nkYNdk4f9ZtIhUAdzeRVBqBaZdpjEETGvFd77ZkVQvUkSqAEj8AaFWDXasVpPeTk09E6kCuFra4H21iGlJ7bP6PuP70erzNvGXiFGHU7CIVAFR8EtR6Su0rHzjzjFE7GAgs4hQAWdI8hIk2VMGzoFMliLihC9okAAS/CLISnAP2HaLjtX4/KbMbMxPuhPHeTl0JwX2CfhrTiLBD7CJvnmgAywDrfRtAz+p5zbiSMQNR6ABRBx19Fd8ApxjJPkCnoMX4JsraJ4+Yu5kvuuZOI2IVYzFzEky5tFoTF0FEboTLhF3ETHHZOITEFtC5qSpqEciblu5tA/bYPb5BGw2Haepfoi834FpS8xGXcA7szOqa79e2Mkpitlsod/sE9ak2NuuJoDb9QmnU8A2nZymU8QghPTFhKJhY40alZoAygoitGV2nZwkIvfJSTmaAQ/rTfy3ANNe0XgEGtYQoB5EaG/iEqHlNGUiIvLbxcnCVjjGNpcxAXLGQSJc+xqJ0JZCbqUB8tqmu8gfg5tOhbHc/wnQKI56+l13opcEmY9/iu0DsYcZ1wWkiNkAnE7GeqKGU4DGmKBALhHBW13FCUVEfo/DX+QTj6uJAhRoqkRkJS+OXgFyKFuEh/wguROvvLgJqQLkRKBSlhPk9WZzLRuRP6zcaQgSoCApIryHDs23EZHvtPtpB5PH17ud1ngMHhHeQ0csCI2iyBOqOQGa4BGhQ0fiyUlzhSLJK17wEpJzHVlFeMiPEDNozdc51MtMAjSZhHqwXYcO552AvH4Buta8yCuWwjaNzAKUCRE6OV1T3YJEaHNY64a8tiA9tUb8xxAxMpNXqFwCFAACuyldIvoh/gxo++t6nw8zdz9zc1luAcoOEYlwLaf61wu5mRhljv1lzxwPrhciQNkgpOWknWSloo5kXIl8kz2aGClMgHJCTEtiPfW3wDb9ZWcdPvquao9lbhcqQCwgeB+0Udfy0TLRXzs76GsH+l8ADBVnhQuoU4OsPn7pQb2uer2/6LI0AUUTTYo34wX8AwAA//9b9x8fAAAABklEQVQDAJFQynC+Mo3nAAAAAElFTkSuQmCC" },
            { "smoke", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAD50lEQVR4AeyYTUgVURTHe63VFhX2QVAgtQjEIgj7EIJ2QRARhJIhhJCFLgLDtCApEoKoTAuiSLKEFlHQNsjCiCBMhaAiihYWYQvT/ev3rzevuXfmvZn3ZoYQRs7fc+fcc//3nPk678ziRQv8L03gf1/A9AqkVyDiGUhvoYgnMPLy0Fcgm81WgbocqiLvbBHAWxZ/YAIQt4BX7DcLxnOYlQ00cxxJ4IjEXzABiBvBV6K7DbYCW2QbwuczOGBPBh2zJhZ+3wQgP0cA98AaECRrcXjAmtPoUIJvbPyeBCA/QxTdoFTpZe2VoEX4XMOnXH7POiMByFdCfhbYMo3hOjiUww30N2BLOxyjYJk9gW05GMV+DNgSlr8bDsWYX28kgDUDbHmKoTaTybSB4RyOygaeAVsaMEyy0QDQfd6MHsA2BTSHMqQUfsWXda82EiA4nYkOl8N5bLvBT5ftzxDbDNjFwSCwRWepDaOeoyG0xtVoW/rhCOK/4FrUif931/EiIwFN4HAVvRpUM+5BFxV8dEuEfoBdZF2sbXcd+w7xOcWEE08/Y0M8CWiWRdPgh8ZhgK/eKvvx/QKC5BMO+1jThw4l+BaMxzcBP1bu4wrgVOIK24dNHoJ12FXcxtC2vMDQhE8NeMTYELgTq8RNkD9ntzngVOI52cBBbIYQ3F2wA2Ml2AzqQCW2BnCfsSFwJFaJ90L+kd2GwU5gi2wj+LwHe+xJgp0H42ACzNvzrNEbKnKl972FINdD+ZhNa0BOCqr1zDxhzUl0KMFXz4zeUJErvScByBV8b6hITKc+1l42Td4jfBKvxH7Bqz6EqcQdBJhWYp6ZQpU+rcSem5qzlVZiz1mxDJykBVGJi1Z6K6f8oec1mp/JDXirJF2JS+LPhZVXBRMg8KQrcSR+JwPfBAhexSzJShwbvyeBXPB+xcxJupBWJQ7TE+s3fbn86g2M/Y0ECF6dlB952EqsnngMnhXGLhxgWwVeMjwObJnBcBM4PbfGni6Q+R44FCPDv2IkgEmVDmVIKT2rFm7j3xQbDQL94jyMVts5ib0e2KI+QT13K69Lp+duxakWKGFUXhRf7D3xpTz9v4G+SujngH5x3sGs8VK0LRcJWn2C5wsHdr37t7PA3bkl0hOfYBNdelRJ0kiQnUEr8OnCJ/GeWE3PRjaaAEHyBocNBDaCDiX46mr49uj2MxCK0M+JTd4BtY9HmH8NbNEH4hZ8toAP9mS5x7El4ARAcLeAPvwuwaaeeBO6Cls90PPAYXwSewJOaAT7C6gnfovWBwFnKladWAKxRlmELJkEimwY91SaQNxntFS+9AqUesbi9l/wV+A3AAAA///ji+3+AAAABklEQVQDAMDZf38qaoJDAAAAAElFTkSuQmCC" },
            { "flare", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAFrklEQVR4AeyZW6gVVRjH91YIohIMJLWohy6WVHR5KcguVIT0IGmWZRFKx9Ki7DGy5FRvQdlFjWNmFzWpjgmBhURFQhTdHnoo7SEKTlREgafySXe//+xZ41qfc1kzZ0+gePj++7vs77K+mTVrrzVnUucI/2u1gV6vN1No8xq11gADf52BjwnIL8NboVYaYMAnMdrbgKPF2I53yiB5Kw0wwFlgMnB0HMJsMHBqq4GBD7Qo4dHfAHP3BDAErii6CpH2g5F+HWrNAapZ+dzE3IEvKDwCdpN0HbxVosZ6CnwCVPNLeCmVNkCyu4g+Dzhajq21JZHcr1DoXuBoNrbFTsnjpQ0Q8Bmwt34JSd/AXpcKa5FvElBOXTA/r2p/6husXJhUjt1udw98GFhalBa09jJdgyn6fitfLAKWhhnDj9bo66UNyJEEj8PXAEtqYjuN5OXYi/MB4EjyD05xnNjJYDv6rcDSmrS2tQd6XvHAQQqJHoI/ByzdhOFNEBD++zBoSsASeg3b34kUfoyiKgcsIA1eNQNjnhLVgAIZwIPwF4GlBVzFhdaI/53YTgPTkZfCAyJGU2ZeYOwr6/CPGrzcoxuQM4mXw/NWoTnYA2KA2j5MxTgtlREDuirQ+soGatzXF+M+azWQprwb/jbwKfl9YKBdsApovu/H4dsU+2UDj4AuNtGz+vCwhcEv8/QosXYDFOkBTZkVVNADOBf9ewY2DX0XeAKcBfzckmV7EvsufE9WDPKN4B2wDP0OeG1S4tpBCqDgerAAvM+ANFX0q3mdvquAfL4mZgqxO8F8sKEipvDrxg2YjJvQTwexdAaO2jLAJkYTboArqTXcriZaRvX7oSVS0NSRzR/t7cTO9w1N5KABEuoMezU8Bz1nm2kKPWD0P9FnMS1Wgx0pHsWmA8043Kcglrq162cNEOzOsB9RoQxj+PpL6YX4+/QUg/7VN0jGpvPx05I9ZLHkbFQ/aYBge4b1auSKyRmXOM37E42Htt/GlKmfZ1JfmEqOGaBRfaVIGkBwazNiFOm8K2iPYwPKchX5l8XY/NJVW+gkDXB79YD5exc5lSHZ2xCnafGHcTzH6L56rq8g/06OX0Cj+sT3G5BAEu1dTkW+pgJ2b/MN/j7pl/gU3yCZaaKH/2HJHr5yctP6yR3wkuhqfEyyMvzm/FP+fModm4GwlwEPg3ngZqAzxXfYpwOfgm06dWvXDxrwM8fKFH0X383Apykoj4Ed4C0gWTbEjDYRq61HZmgiTLiBtKjW859TOYb9hNNKMGFq3ADTQgf8UfgNXMm/GMn5YCOoopdwuICYfcTOBcoxhK0R1W6Agtoya8poC62tgDZzFzGgcaCt9vWMRNtt/1mRrKl0LT5DYJw8l+K3EyjHCPpmUHc5PbQKkSiWdJXtq47sVQiD+wAsBHpg9Y70bMngFvChV0TbcU/tKKfujm+rlGvdAa6QdpBLcrLqXJBj7ugc/G/eF9h0l2ABLaXG2sBSoUQ3QOIXyJVdaWRHo1zZw1YT/N3exu6dkjhi3kPQYQYW0ApinwksJUpUA2nCvLOqBqDtdFACf7u3SfZOgVNf0clOOfraoc+V5IhqorIBEq0mb96St5WrqNNU3v5Gcz/ZqxAr0gHfbiM6xB8Aeoi3yclATawytsPU0gYYvPY1+hGygdsorIfO2sv0slrKldeEfs3PbJpUcZfzYQtvZPD+v49wiaLCV4vkOwiUUy93/WSqfZlvsLIcrC3TSfoqil4TwhJai01rfaIM+oPcWuH8l2d7sG0pq1PaQBp4CfwecCXJ7oe3StTQyzPtiPWO6OKqYpUNkPAfMAJ2VyWr+L6ylounlnbDekunl2POnMujk+ZGFxn/R/uxBgouth58//dBsv5ZUuDe3NzKHWAO2zNucoZuPsziyFYaUDma0Bm78P8D8hkEWmtAg6OJMaCzgNRW8B8AAAD//x3sHsIAAAAGSURBVAMAd8Anf8tau8MAAAAASUVORK5CYII=" },
            { "boom", "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" },
            { "tank", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADHUlEQVR4AeyXvWsVQRTFsxoEv7VSYqGFoER4AVFbI4KtiaCNIsRCBEGxifof+FH5AVEEDQgKoiStqChYBpWIhakkYhBBQREtAvr8nc2+cXbeaibMy+6GbDgnd+buzNxz5857O29B2xz/qxIouoBVBaoKBO5AdYQCNzB4elWB4C0MXKCqQOAGBk+vKhC8hYELzK8K1Ov1DtjdInYEbn483bsCiL7NjAn4tEWcYM2brBUErwQItIooh2Cr0cfau6FPVTMr5pUAqjfC2cJjFvapambFfBMgRilwkGottpWEJHCUhXbNkMcYH4JFTBYxU5hBAlMTrP91q51X81YURd/sYP9NgHL1w4dMeAJd3MDhc3btMdeY44vvDByBmqNKr42i6Aj9FDITQHQNvmXkebgHroB5YzkBt0Mdu6vYNbAJTQkg/DijRuEmWBZsQcgo2pQMzb9IJcCArTy6DMuKK2is2eJSCfDgDnR97/Gdhr1wP9wHdSZDON06inWGOHrzYwzaad2FBkYsmR3A6x6be/g6+fBcgMPwPhyCzwI53TqKpc+f9DxAg41OtGoTY59JgJ52FGPwjlYfQn9gC0ES+zDBdQowBt2Nlp2AcSYPB1jgZ9IuzCQa9FVqa9jZ6MQJUJLVODZDG2/sTsHt1058fc0vky9OgMYS6GKh6yiwn6VF74m2OAHKpE/7Z0fgNqdfZHeHE/wTmj/KFyegBnwJbZzkaK20HUW0Ew16udrhXzQ6dgKvGs7E6kfMIAssTfq5myT2IIGlBWNgNttOYIDHX6GNHjpjLKRL3V6szy+nVozpIZZenmPElwaMwRda12EMkwBnahyPXmbuNXkdfr1UhrH2zXI220PEOgcVG2Mgbb1o/dDwmATk4MEj7ClYVpxA43NbXCoBPWDAJWwXVPkwpYC0dKFN1+qUoKYE9JSBenHo1neR/m9YFH4RWEeplmiim0ZmAhrChEnYD/US2YBP38W6L+VBxVpP7HZ4Fk4SPxP/TMAezQLjcASG3kJ95yuWe4GzJZm2VwJmtG8jx3FVAjludmaoqgKZ25Kjc85X4A8AAAD///RLam4AAAAGSURBVAMAUL3BcDINnrwAAAAASUVORK5CYII=" },
            { "gas", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAFA0lEQVR4AeyYWahWVRTHv9sERjMVJqSZDVAP1UsPPSTZYBNSRNJtoKgIeklCrSyao6SkosEIKkpvlj00vERmhRAiDg/6oiKCKIqz4oQD6vX3l3OO66y7v/vtfc75HgQv//9dw95r7bX2d+bTWif5X1cb6O/vHwXP7eYeda0BCn+NwlfD3eivI7uCrjRAwfdQ7Xswxzv47s2NJmXjDVDoCAr8CXr8mI15fy270QYo8AKqmQPPhx4a+5M5F/qBOnajDVDIH/Ba2A7XMfAbbAyNNcDOfkxVt0KLXRh7oMVo5k6zjjp6Iw1Q0H0U8SL0eAjHg7AfWkwkZpx1VNVrN0AhV7J46KSd0tPT86/I+BvQYyaxV3hnql2rAQo4gwV/hf5m9Q+FT8V/HOi6pP593Djx7zzU2eQ4HVkZtRpgVR3LNyAt1mHo0EGUMB5rLbS4GaNoFD0ZlRtg50az2gRocQhjHDu+G1kCPp3QD+A8DC0mkesW60jRKzXAgmezSOi4f4VClzEWBGNLGdAjBqKEWVnOkjPGqNQAiXVMX4a0mEeBn1hHSGfOh/jnQwvdvd+yjlg9uQF2aiTJ/SVTh8ej+GPxCBP9/WEyuYfjT0JyA2T3xeNqTWJnN0qJIXPXM+9l6DHROzrZSQ2wQ+eQ8BlosQrjW5iKrwlYAy2eZY0h1tFJT2qAZCpeJzBqgXfZUX+nLQbbKcQcZexNaKHcT1tHJz21gRdcws3YoasR7ijMYtYOaBE6RO14SY9ugJ9WLyR6bLAJprOTR6wjRc9iv3Axeg290/namtENtFqtu12Wg9ifw7r4kgT+5ubXYkoYKQ1c5VIsYgd3Ol+ySY4tBC2BFn4tO1bS6zSwvJQpYHDYDRMDQ97lczXbAEWoUX/8+0VLRREzE8cGEf075GDwuXSzHGx+MabCCmMQRc/t/rHXL1qEU7AelXsLR6v1GD5dIo2rpPpcQ5h/eWlGGyO2gbMC8SFfPk3vB7ZhzX04HwxIOzcfjrqhxTagEy1PnMsbc8VLTkwdOtuc/2129Xk4zPll3qR/jpucHTSjGqAg3Wz83bZtA9lK/vFCT5zTGdtAE/6c8A0cZs0B7xTEDkBUA1nUwkzmolMD7zNxKwzBnxO+gQWhoJAvpYHfXYKr2cm7nK8wsx18Dsde6KF36TPlJMf9SF0kEAX8WsWAV1Ia+Ibg/dCijwIutg6r04QKuQbfz9A+///A2C5ih+KfAS0OYHwPoxDdAAtuJ+NX0OISjD7YFsRthL1Ql9brmTgUPX/iVGP+U+OnjOucY2pnRDeQpdJx7X+FsezkXHhpNqetoLDlcLPmwrlM1IcBRIF9aB/AaCQ1wOL6FZ4KZL8D3wqKsjcvXAPBnCfwroSKQRTQVe5x1oi6+uRRSQ0oiAV+QU6BHhfh0NeFLRT5F5wKx8NeKH0OUlclHfP+sCG09Sq5dc5Ij2ZyA8rMQvoYpZcb/xisYZ0XY1H0zjsbqZcW6bpihU545ZiQ5WR6Gio1oCVYUO8CY9CjX+aZ66HYMeT6zA/E2pUb0AIs/D9yFHwJ6vxAREGPGZOZOTLLgVoNtRrQkhSwH36ErqfHJ5HzoE5IRAny/YdHJ/FwYqZBvdXhqo7aDeRLU4wamYG8DSqv7q76eCuOkA/eDvugvxTnaZKlFkoOigmgyLVwcUZ9sY4JS57TnQaSy6gecKqB6nvXTOSpX6CZfaye5aT/BY4BAAD///X8CCgAAAAGSURBVAMAA+pYcCLFN5YAAAAASUVORK5CYII=" },
            { "signal", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADHUlEQVR4AeyYT4hNURzH3zP+Z1L+E01KkQUWZJZISfJvMUkNGzKKWdmIDamRjQWipiQhkQX1VjQpGxbEbEmRwcaImZQm4/l8677Xece73HvuOVejO30//X7nd8/9nd/vnnfvu2/Glcb4X9HAv97AYgf+2x2oVqutsCpiWqhGvX2EKHQSHIYnMEjBQ/A8YpjYJ3gMB2ECcS/K3ADFtEA31byB87AWZoCtmQTa4RK81TmQef1MCShAH40HFHQO5kFSzWeizqmQYyq+szI1wKp3YD24ajMn3gBnOTfAldvFqpvA1gCB09AFWyPkn8H/CLZ2kGunHUw6dm6ABQ6ArSME2srl8jHohUqE/KMcWwTHwZYatGOJxk4NcMWmkH0DmLpCsWfhpxk0fY6NQg+xW2BqIzknmoGkvlMDJG8DW3ftwB/GunfMwy0MtDuYdHJt4FuTZVREk3Di0PfEM42JTg3wMXhHji9gqoePgZ71Zuw3nzmzCJ4CU4PkfG8GkvpODUTJr0W2Zpbh9FFgK7apODadA32guZi6rta9lE6WBk6wll4XMHWtxOun0G3YBhHbTqAfVoAp5ThpBtL4zg2w5Z9ZqAN+gKnFDO5RsJiNnQMVYrrJ7Ztf53aQS00wJb2cG9BSLHwfuxeaSbvwmgNiC9ZWlcDuKAeumzI1oCUp4CZW3wnNvmV1P+h9iSkN0rf1Os61H6cNk5IMUjQQn45CHnJUN+Zl7N/Uy4TlnPMIm1leGlAVFDQE+/HXwAuw9YzAauZ0wTC+F3lroFYNxT3F12+Ci9hR0I16AdvOMTWB60/eG1BpFDoCh/AXwFz8blAjDP0qSANGiePxJ0MwBWuAZ79uaD1tBiI/SBNBGqBgPT47qbgc0UlMrxEM/SpIA5S4FMz3e/lLiHlXqAa8FxqXsGgg7srkFS92IK8rHbdOsQNxVyaveLEDeV3puHWKHYi7MnnFix3I60rHrRNmB0qlVyw4AjXJf1kb+LRBGuDn41eKvA3634+4TszbD3ny1hWkAWWn4D3YhQJ/HzaIgjWgain8g5AfiqANhCrazDvmG/gFAAD///XinkkAAAAGSURBVAMAEK3PYUEMaI0AAAAASUVORK5CYII=" },
            { "bag", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADWklEQVR4AeyZu4tUMRTGZyy28sHq+hZtBEUWm/0HfPWiIIJgLViIgp2KoHainaiVjaWg9RaurSg2lhYW4ij4wNVi7cZfYO6Q+yV7cyY3s+PCyvlIcpJ83zk5wcnOrOus8n9rCUy6gGOrQL/fnwKzA0yNK9GiCRBsF1wHHwh4CbwfYMn5wDXQxVfMiiVAYFuJah7cBvuBz+36zncH/zxrN9MWMUfcmoiApiF5C06AlLk179izMbXQMl8kAYSegL3AavtY+BC0ttYJcJJnieIk8O03g1vg1ADu6jgfw6GdY+/p4Siz0zoBdC8B334yONDtdm+CFwPcwHcI/AG+6V5/ztQvkcBhUbpL0F/F18H3Gd994Jvu9edM/VYJcAXcvV8vSm9k7A9f+wP603DspM22Vgmgug2o/VKHN/7m9avurqqT046UgDstcKQCgnNAba6a15aFlvUjVcScAME8IoAeWPDgfAxr9piRv8bvW9b30HoAh8lMCUC4HbYLYKXsIppbLGKmBCA6CFbaZi2C1gTcO8fCV3LNjIXMmkCsnMcQODrAGVr3qVqNU62uP85+tZimrulYE9DTWOSDaQG8GuAZ7XNQjVOtrn9JZPoprZosCS03gR8hVWvPd2EonUCNXsVqk5kD5SyagN5HFcuMubZNOVWztrga/E9XSK9l0QoomZ5WdSBtWuVUzSh3bgVULEo+olM5i16hDRKMllums4bKucnCkqwAb5LY61BPy6KVWhNwor0jtSmZAASxuxiIsa6txThj2jUdSwKxu6jlrpFmDmKcMe0avSWB2CnETqtGnDGIcca0a9RZCfDmCf5or7FmDOD8EtlWJAEto36/E9HNdumDTrUD4pwKxEodEGc6lLtIBZRERTJjjW5TbtUONlkqoGVUkYC0hUO5VTugtiSgpxD77y4gznQot2oHtDkJ6CkFpC0cyl0kAS2jirSIN9iq3KodbLBUQH+ImOGNMvx2rmSf6PTEkw+6xgQILvaQu4KQ/21byf5luGtGDI0PusYEYEqWkDXjNvfz1bIaqQQ+LbuzaaLs3McmusYEeJ8ssvkpmJTdI4a/TeKNCbiNEJyn3Q1S37aVnt+D9lV0Gy2ZgNsNUQ+kvm0rPe9+knLyjTAl0Mgw4clVn8A/AAAA//8eeaaUAAAABklEQVQDAILIZHA/Gg25AAAAAElFTkSuQmCC" },
            { "shirt", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAEaklEQVR4AeyYW4hNURjHz+FFySUhIXciya0kSu7eeKOIjFxyLcQrT6SUB3dyLXlC8aKMeMIbygNmzOVhpqYxExqeMH7/mb1Pa6+91tnn7H0yTZ3p+8+3vm99l/3tb621z94Dcv38r1pAXzcwUwe6u7ungbFpi8B3NpiY1l9+qQog6VzwmgB1oIXxTXhZFPh8wKmJ8Wswl3HZVFYBJBkKLpLlHVgMQqpBPyQUkji207GpASEp1jv0F8DQUFkKL6kAguaBEtYTdB9w0TKX0qOb6dHvR19Hru0gzziREgsg0ByivAFaJqPgPprvm3DoFzh0oWo0g1vgTZCboZ+8BeCs5XIB17dgEUiinfgMSjLCZjA2u0ASKedb7M8D77JyFoDDdqJ/AWrpQLiLmi2lTpNTls4lnkY5DpjUZArGWLkPINcH18QwSrECMDyLiVo4Eu6iVyjngdXApsP4XwW6y5E56cBVlAeBSb8QVgLF1MnGMEZaurfwP2PPRArAYAIGR4GL2lHW5PP5peA90IZWh1BHaDfSR2Kp9Zvh24BOrk/oNQeL0GFiNQLFXMKMDouvcBcdJ1bkuRMpAI/vwEW6gBkkuW1OIl9CfgZsGo9Crb8HvwN0ctnLBnXuKTGuaRACWTl0zCp2qDZ55BojBeCsyeemNeMG9AfAN8Yu2oKyFpRLKnyry0m5gLrbaM3Xov9p6iIFBBPXAx6yKbRtYyjYnIDtYA36vSASHNlFstmHz1rgWyo5cm7CeTIwyb62nKuAB3jYgfegK0pczBUMxoD1QMev1jzDHvrI//NgAxiD7WV4Etk5dU0PbadYAQT/jZEeWrACreSOTC1IngG+XeAJOAj0tJ2E6TjGs8Ah8Bh0oStK5JqGwQpg0k18dW2mztkBGcRahTL28CHRMGC3GdNeImEzaO2V4v/xHQv05LUnXadVZLOHDrEOaIKkOiLtzbxDcyQcAXYBbVxt7AbGJzRXDvC5i30LaGNcCxRzBLJIR6l4CG1ePVhDucCdBQSzdhdGkeQzcx1Ad2MVPKSTzOlJHMpFObbDMTBPIMVSzA7mlMN+iGoOlzgVK8C1mXU+x6P0auykvVr3f61x90wuZ+fQ5n3kM/YWwDLShjE2sy9EQf+nMKrs4EZwLc6o3gICa2/rgvn/weylHMlZtAAq18Y5ZnhoY+9EXgdsKukFxHYyZP04VGzlCNVHgmsI5RgvWoCsCaBfpzoqhzNeDW6g7wSVpk7FBipEm3wS43NJSRILUAACNQH9TpLoQ0mxfM7oC/7KBez3DUziVHCKT/UPTbWAvu5TtQPVDmS8A9UllPEGZnavdiDzLcwYoNqBjDcws3u1A8YtXMD77PJSgM9CYNNfW1GKXMkO6O3tBUlLgT6CYRqhVNeSyimSto+FtAXo04f/Jb78ohTL/BRZcoRUBfC29IMM90Gl6C4xEz85upKlKkCBSKgPU/rmr2+YWaCPvT1f/RS3XKQuQIkoohW8zIg2xUqLTAWkTVpJv35fwD8AAAD//1YQnc0AAAAGSURBVAMADoSLcDjzn70AAAAASUVORK5CYII=" },
            { "rifle", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAClElEQVR4AeyXvUscQRjGvRSBIAmBNKIhBJIqf0RiCKRKEVKkyEdhI4KIWvsBotgqaiEWIigWFtpYCaL+CxZWYqMnKPjde/7ewVt217nZ2Zu92zsYeZ7d2Xffd/Z55h139VlLk/94A3k30HfAd8BxBfwWclxA53LfAecldJzAd8BxAZ3LfQecl9BxAt8BxwV0LvcdcF5Cxwl8BxwXMFJeKpU6YGcC28NFDdMBRI8j7ATuJLBI7iI5CikMqPyaHBA0xcTD0BZ/qXkhybkbQIiIHxAxKfic3E+wJWKAydph0h7M8v4MItKKp0ThXo6BAYQvEyjCpD2Y5f0+nhfHLIGvMfZwrYUygPjX3P0H88YaAtahNZQBsj/CRsBvROg6PE88joIEygZk3GwsiWCTgW4S4nuxFterPKcaHEmRyYDc1/GiUCjshknSAawGvyj6A9NiieffSJHJwAIJuv24zy/9HPcUGI8yOIO63KSY7i0kr1ZTp9sQ38XzFEwGVEKFQy/CW+Fb7o/BrDCNuH4Y6XDsWhYreF7ZwGEQsR+0kvodZgURP5h2MmUAh9cUrkBbFKk5J/kLzAITzJdavDxYGZABE/zn3AF1+++OeBjbjxffHs/l0yYDXb0pJnt6hLqqEBiQakycwsj+I34MX8Iw9tj/HwiIYU4BNuL1FteRPR3MZDmIGKhQ06mJ7xKrFOdW/WBj4HNMzi3X7+BPGMYJq60+LuFgrcc2BuJvmleIkvf7D85hSCx8XZex0QD7/D0q2qANGs8Aqq+gLbZsE7PMM3aAPS1/b9h8H4bIlX+GstRmNZfRgMyAMNP3Qd7vb8iZlNyAdRwkGhAtCHzyfSBW/l5cSk5etDKQlzib53oDNqtUy5ym78ADAAAA//9BEVoKAAAABklEQVQDADl5M3DED5uWAAAAAElFTkSuQmCC" },
            { "helmet", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADlElEQVR4AeyYyWsUQRTGe1wuruBOEjVijIIXcUEJiJpLIoJ/QRRRDx4iYryIKAQ0iIgevChCDi4gCoKIwQ0XEPUQPXjTCG7EgHHBDcGIjr+X6SkqNVU9U9PJZAIdvm/q1au3VnVXQ0YFI/wvaWC4DzA5geQEYu5A8gjF3MDY7skJxN7CmAGSE7BtYDqdng5r4aqQIk+z2cbVDcoJUGQNbIF34R+K6oUv4OOQIn9krQ/egbvhPNZiI1YDFLEQXqeKl/AYXAfHQBfGslAPj8NX+F6DC5CLRlENkHQiPEnW57ARFosNOHYR6wScgOwN7wZINJMs8mjsYBws7CTQA2J7vydeDZBgEYk64WLowk0W9sPtcH1IkQ8g34IuLGGhkxy1jAWj4AYILM/qQyLPhibeotgMx6dSqUbYBtvhjZAiH0JuEBu4BYoPwwBUM3tErhrGglBQAwScRLQOOAWa2ENh1fAc/GUumnOxgWegFLvXXGc+FXaEORGjkbcBAonNVcLICTAofEaqoxC5URD9ge8RvOqgxGJQkMfoCrlTSuMQpDjHklLvQloDdUjC5RQgL7Ou95bDGCtw/AR1yJXcrCtscmQD7MBEnFphEATq9zdSA4nfMPYDuwq41pMV/c78EOs1g7zw8hFEVGgl5jg1swiRDWAvt4k8/4gKB0n4NDsjQTtyN7znye7QF7cgIOaTIAjaoA555/bpClN2NkBwWTPv+ncEOAr7gY2cUBOTvM8qNibEp4kYk7WFw8g9UEczNlKLrlOycwGLemjufgs71Yc+iyj/rE2+8V/WIIzdkp2HozS4OpRzhqgCNhrWvSS4rOuYf2N+CaahL8TnPDF+6I7MLzL/AnWYtai1qAZWKquMcDszDPwl4SY0VVBuDR9W4bsNPxvMXGYtyieqgbnKKiM8ywy5vxTSA+970nzW9cBmLrMWZWttgJdmNBYzoI73+mSIZTNXZVhTTlprA1jNh3JLMCiYQdXCEAjm6Ugtc2x5XA2Yt4/4/pSfEvGrJY98E3LUrgZyDFEs5Rh9v7ZF2ZNrGTShrlt9waeB0zj6fm2LtT9FLhPWWq1K07Oc564Guij6LywXSC3yn42ceqwNcJ9/x/ICLBecpSbrJWJtQKrGQb6wlcg+X9ehsJ1FLVupwwpnA2KNYzFfWN8vcj77D1KLi5ENuJzKSZ80MNynkZyA9QRKqExOoISbbU2VnIB1W0qoHPEn8B8AAP//SD4cmwAAAAZJREFUAwCJJrRwyl+yDQAAAABJRU5ErkJggg==" },
            { "med", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADPklEQVR4AeyYy2oUQRSGZ7yB12xjVMSFIaK4MDtX6guIgoswuFEQXQi+gKDg3k0QFRTUGF0ICj6AvoAuBA1EV8okBN3EC65k/E6Ybk5V1zQ9VdXdo3Q4f6q66tQ5/9/VU3Om17X+8b9GQN0b2OzAf7EDvV5vAhwbEhMxxAc/QpB+BJEueDUkuqy9z5ogCxIAgR1knwG+1iHGFt/Fsi5IAAEmwXrga5tYeAB4WyEB3KUpkHnGyToNQm3aFZuxqSKBBwogwDiYB98JtABcz/htxkPtDgFcsRfIvQrmwE58nOYUwIJTeH8C8nxvp63L5DPWIfkinE7SZiwjAMc9eMnJspV2VGwbRB7DLbMTGQE4PgWjRB46ayac5tZ66p8hAIX7mDsKtH3m4jI4noMzzJ0GeT56roi/5PxCTG0n4LhLDxgCmBgDtl1tt9uz4HUOnjH3HOT56Lki/rMQuQZsMzjaAjbY3ly/B3XZO0fizXrMFqDngvtst9RIUWqeQWRKEwB5OcmkRopS81QqAPJyfst3SJI3uOZJAtltWTtg10jBNY9NPLkeQkCyZLTaRoDsB8+8nDZptcqYq0o9gl/qQz/K6RS8AxBJThtdUbqq1LsI0z5RTqcgAZC3Txs4DmXBp1OQAKi2+6DxMvnm3+i1sr8oSAC1zypx5oGvPejH8F3fChIgWSFwllYqRF1tXmTMtgsMaJ9x1p5jLMiCBUh2iCyBtNpk7A2w7a32ob9iO/hcRxHgkzjWmkbAgDv5kfE/IDHpLyYXMdtSdoDnW06nJ4roQ8Z+qOto3VIECDsIy+m0m36U04Y4TitNgGRDRBdEOW0kngu2gGWH0yHHWFVDhx2JlvSYIYC7JT8Bv2kH+tepeS4BXUlW0b9CbvutxAocjZtsCGCBmP3Ofi+Dt4CuJKvo3ySnvCWkSe1e2ut3XAJuMPcVjJoJJ+Fm8MoIYIvkuJO65afhWe/FL9Kfh9tvWsMyAmQWxxe08sNc3pOKIC5rMXm1L9Xufji9dDFwChBHFiyDGSA/Wg4ypivJ/H6rFWN+ktxjoAOMDy5cUhsoIPWgQ4APIK02K+pLOUL2fCskID9EvbONgHrvfyv8J2XdAv4CAAD//9gOGUYAAAAGSURBVAMAz0bUcFkKFWsAAAAASUVORK5CYII=" },
            { "ammo", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAB2ElEQVR4AeyYO0oEQRCGd8REfKWrghgaa2akXkAMRUzMPYLgIQzE0Ocx9AyCCKuRgmC4Guv41bDCLHRPd81ID8vWUv/0o/56bzIz0RnxnxXQ9gBtAjaBhh2wv1DDBjY2twk0bmFDBzYBaWCe56sC2cdAuIIYbohTewIk0AU34JMgTwL2fXAFFjgPCXcq/pBxxaFWASSzi88XsAdmwZ/MsdkHPTg7rIWwV/ELo8iHugCSWcb3JZgGPplBcQ13Eaj42KlEVQDJZHi/BVXJoy5EOFKoll8Yxz5UBeB0BWyAsrxyOBrgjbUs2xxUfJq0hE20aAuYd3g+zrLsVIDuBIQkxHfF8PrUFjDp8PRYunso7X3bEH/KZ+i61xbg8tHq3TgV0GqjvcFtAt7WJFLYBBI12hvGJuBtTSKFTSBRo71hbALe1iRS2AQSNdobxibgbU0ihU2ARq/xIr4pYL8OQhLi/4QclPX/MYFzHN4NcMYakhBflZOKHMqsDb22gGeS/AaxIlyBht+LJQtPVQDffvoYyZc2lii5gKXiE+MLm2hRFSBeCXDAKl/Ptljd6HTkvgv3EKj4+FSJugDxTlLv4D6AD+EK4Kn4YhOLWgXEOk/BswJSdLkqxshP4BcAAP//FYAgYwAAAAZJREFUAwB8Z6dhAeDSDwAAAABJRU5ErkJggg==" },
            { "pick", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAACd0lEQVR4AeyZS0iUURTHx9aC6yKiaNkiSwyKICI0FwURRaugxyaComiVratNFAXRKmrhyifoQnws3KgIPnc+QBTRregIgojj7z/OgMycbxzHuXccucP5zTnf+e49j3u+z8V4Klbmn9BAqQcYJnAiJpBIJCqhHhrhB/yFFhhIIVs+3fuIrw4qi9F8wY8QBaiIX+hJColDD3yGd/ACHsGtFLLl070v+Hohzt4J+Al3uC5IDtUAiWrgO6yQTUW8QV+GQqWajW+hn5jL8A3kw5Wf5NUAQe/BCCFH4T2chmLLGQJ+AE1liHwN2AdKzgYI8gDGidIF1yBfWTUWWj5jWdJ1ne9uco/BfexIiWyAjX/Y1QFXIJfo2ddz/ZBF5yv4oOshU+q4VYHzAmjtV3Qf5JKr3Oyklt9oU8wG2KCiX5k79pwq+jlmFUU1wCfogEV8OYU1C6C1jWg1WsUGveB6pzBNeU1Nl6w7ZgMsnAZLmnGeI7GK/o9e5/pIohjwD+4S6CK0Qqbs4JiBLDEbINgmK9shLVMYN/A/gSVsJ0LseXhM8Jugdw+VlDb820kr48tsILXmGfo21LK5GoaxvQi5BqGGZLWgGvSIYWZLZAMEiMMA6E9n9k4PHuUG1bARlS6ygagNx80fGij1RMIEymgCpS7Vzh8eIftc/HnDBPydtZ0pTMA+F3/eMAF/Z21nChOwz8WfN0zA31nbmcIE7HPx5w0T8HfWdqYwAftc/HnDBPydtZ3JzQRisTnSbUFaZM+mL4qpnTTA75lrFKmf4hNo0YRP/wjksrjipAGVSMFP0WcF9ku0E3HWgKql8BUh2xVOG3BV9P64Zd/ALgAAAP//MllbuAAAAAZJREFUAwBQv8dhDWPNpgAAAABJRU5ErkJggg==" },
            { "dot", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAACVUlEQVR4AeyXvWpUURSF54qCTUwZfIKkSGFpFxBsxETtBFHsRBT1AQQTfAEtrURfQCOC2kmewM6YSkiK1CFFUt18K9zJDySZu/Y+IQRO2GvOzczaa+919mHm3guDc/5XDZz1AOsE6gSSO1CPUHID0+l1AuktTArUCSQ3MJ1edAJt2463bfsIfAXLYKPDX9Yv4CEYT3d9QKCIAZqaAO/RXQefwB0wCcY6TLHeBZ/BOtx3YILrdKQN0MhtulgBL8BlMCrEeQlphVwZ5TIeYQMUb8A8pb+BK8AN5ehYvUancZOH/LABBB6ANyBcvMt9y/oYhCJkgB27QbWPoFR8QPNmRCxkgEI67xdZS8UlhJ4BO2wD7JS+PebsSqMT5tC+Opp2mGEbIP0eyJx70o8MadrfSoaBvaL3967KX+i3wlKNGLhmVfDI1z36YBAxoB8it05fvq0dMdC3mQhvy02KGND9jlunL9/Wjhj407ebAG/ZzYkY+O0WMfg/De4uNWLALrJbqd+LrW0baJpGY/7Vrx+LtYj2fysDsm2AHMUrXrZBqZDW84hYyAA7pSk8jRQ8JucJmmvHfHbi2yEDUqSgbqcXdJ3EAlp6DA3JhA2oGoX1RKb7lw39b0I5s52GmbpPTxmQDA0ssk6D76BviDtNrta+OUfy0gakSiOrYJbrGaCp/GD9BzY76KFf7+nIzYgLVvksHUUMDLugqSWgM32LdQqMdZhk1XvzrEtDfom1qIESDbka1YC7Y6X5dQKld9TVO50JuF0k+NVAYvOKpNYJFNnGhMi5n8AOAAAA///v6DonAAAABklEQVQDAGSfhWGIKMk3AAAAAElFTkSuQmCC" },
            { "diamond", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAABgUlEQVR4AezX0YqDMBBG4XVfvO2Tux6I4BKNycw/SQqWSgXj5Hz1pv39+fLXAxj9AJ8nUHoC67q+OUprvNfCnkAKf22Br3S+nerfIYAUTPxeHIaQA07iQxFSQCE+DCEDVMSHICSAhng5wg0wxEsRLoAjXoYwAwTxEoQJIIx3I5oBAfEuRBMgMN6MqAZ0iDchqgAd45sRt4AB8U2IImBgfDXiEpDH7zO7fxZ/ip8CJorfv61LRAaYML6IyAD76m/5zADLsry3+M92zPb+pLZ/XRmAq2nhTIjTeFpPAVyYCHEZT+clgIsTIIrxNBYBLBiIuI2n7xbAogGIqnjaqgAs7IiojqerGsDiDoimeJqaANwQiGiOp6cZwE0BCFM8LSYANwoR5ng6zABuFiBc8TS4AAxwINzx7O8GMMSAkMSztwTAoAaELJ59ZQCGVSCk8ewpBTCwgJDHs58cwNAl/1MUEs9eIQAGH55EWDz7hAEYDoKD86gjFBAVfZz7AI7fxojzPwAAAP//R5ts8QAAAAZJREFUAwANqMZhNHaebQAAAABJRU5ErkJggg==" },
            { "tridown", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAACXUlEQVR4AexXQU7DMBBMeW8PqBLqBfWEekQ9oz6A/qD9QnkAnOEIN7iABGUGTQ6Nk3jt2EmQgnbZuLZnduxG6lwU//xvEjD0BU43MN1AxxOYvkIdD7DzducGTqfTHjnW2FcVOwKw4BL5iRxbsCf2dtaXI2A2mz1hxQY5ttiot7O+HAGavUF9Ro4l2At7cvqpFQClH1i5RI4llurJ6adWAFdhwz3qETl0HNVLbR+NArR6jvqNHCrIzR4a+VsFQPkDdm6RQ8VWPTTytwrQrmvUV2TfQU5yt/J6BeAE3oCwQvYdK3G38noFaPcdap8vNLnICdr2MAnASfwApvVlwnzKmIvTi2kSQBQA8oXe8Tlz7sRlojELKIo/vCv8f0fmCmKTw4wfJAAn8wLkNTJXrMVhxg8SINRb1Edk6iAmsYNwgwXghL7AsECmjoWwg3CDBRAdRDQWBz4nyoMwg+GiBIiF5oImQ8PoQgxiRQFEC8CJpTI+tUbFqiZagAhoMmg2NAwu3EuM4I3lhk4CcAtdjU+jUSkb9NVOAggOEbHGp9WoENuSnQWIhL+TaD409Bau5R7vQt+CJAJwC/ydFGJ8vEbF13g5n0SAwGg+aEI0bCxcw7WNC0ImkgnALViNj8moWEUkEyBCmhCaEQ2dwjmucSZiP0gqALfgMz5mo2IVlFQASSGCL3Sd8QkyKsSyZHIBIqUpoTnRsOAzPyvHyWoWAbiFqvEJNipWhVkEiJzmhCaFyWd9nLZkE4BbKI1PlFGxyswjQOwQsWdqmKVkFZCl4wroJKByIL0Ppxvo/cgrhL8AAAD//z0rQfwAAAAGSURBVAMADTP+YdKvdYcAAAAASUVORK5CYII=" },
            { "circle", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAADZElEQVR4AeyZS0gVYRiGPV2WWZsgJNJVQroQV1ERbcoKgiIio1oVJhTRZZPVTsraVGSBQosoiJKgILBaBLXothErba1QFhQItYlanJ5XZn7mes5c1PkF43v4/pn5v+973zPnnHRcUDPH/80bcG9guVyuL5fLJ6APBmEEfjlorXO6pj31bl3enOsOIK4FemAEIWNwFY7ANmiCJQ5a65yuac+YauAitLAnc2QywNA18JCpQ3AGJJCUKlTTRcUQvQZgNevUkcoAQ1bBXaZ8gt1QgryhHntoMkrvfqhjnTgSG6DxFrp+hAOQuI69SWMRGzvgszOLZfVIJISGR2k1CEthpkMz9IE/mGRQVQOI76XRDVgIsxWadYfZ3aC3WOzcigYo7qTyGBQV5xl8GGIj1gDi11F1HYqOm2hZHyci0gAFKyl4DIuh6JCGR46mkJZIA+y6BcvBlpAWaQrpCRnA6SZ2tYFt0eZo8+kKGeDqBbA1uoPCfAZw2M4GfXhJVsYGNO70KvMZ4ILvIsc2hk+jMYAzfdr1E6ONor2aNnsPjAFO6ru2lmx71PFir3VFeg1sdU/OgWzeRl4DuX6xmGXTRqvXwIrKIqy6usxVM1cNmBfba8C4ct1ZnCMN/LFYcFCa0eq9A9+Duyw+NlrnDRR0lyLvwHBBYrKMNVq9b6FnWToVVGO0GgOlUkmuvhQkKM3YCUfrVI0xMHVUU/PCyTanJ15xQQPm1ng3Wbb2afQZ4NbcR+wHsDXeoFFPS4w+nwHnrJ42O0vr0rmgopABHOoWvQputOD4OdpeBnWEDDgb9AT6h7O2If1ExCEIRaQBnOrrdBe7/0HRIQ070PQ1SkikAW2k4DX5OBQdHWh5Fyci1oAKKOwj90NRcRkNtysNr2hAhTTQI/b9rP/CbIVm7WV21W/EqgakmEb3yHpmOkGe6fjGgI3MHCBXjUQG1IWGb8nNcAX0CpFiIttp9dSfYJuY9T5pi8QG1JDGk3CadSPof+0yOW+oxwOaNNL7FEyyThypDLhdGTIG+zhuhR4YhbShmksUtdKrHfSHcg7TRSYD7giGDsNZ0FurgfMnQd9aT8kS+JsstNY5XdNXc4NqoAv0YzzbskUuA96RCBmHa9AJ26EZah201jld6+XcuLc2z3raDOQRkaf2PwAAAP//+blUcAAAAAZJREFUAwB7xvphS5j++AAAAABJRU5ErkJggg==" },
            { "right", "iVBORw0KGgoAAAANSUhEUgAAADAAAAAwCAYAAABXAvmHAAAByElEQVR4AeyY0UrDQBBFE/0IDfiJFeqDDyJiRSj4JNV+oor44hdoPFeChGVE09mdUNgyw85Omuw9O8lmyUGz578KMHcBawVqBZwzUG8h5wS6T68V0BT2fd/hx4qj3V0BhJ8h+gl/Jr6mDTUXAIKPUHuH6zryFblH+mGmQT2DtZzc42NbREK4ANq2fUX5Gk9NEPdpskTfBSBBQNzQbvHUlhGVcANINRCntBaEKlH0mcgCgPhmLohsAHNBZAWYAyI7QDREEYBIiAkAkjXNIx7sogDCHSA2ihPXEnuV5CZ3iwNIERBLWus9ccHLruPYzhYCMKh7p/3Ax2btpcbH/4xDAJhl7Yt0uxwmitZUR/upJP3/bnEAxGsroVsoVbVF/G2anNovCjCIXxiiJF77J+PQtFQxgAjxQi0CECW+CECk+OwA0eKzAswhPhvAXOKzACBe34KspXLDOp9lqZTQ39y1CiFe34UujYtrnbdeXsZffSkXAENbexmJLz7zjP1tLgBuEe1jzrmSPm590q7IhYlnvMYFoAsg+IH2BO+I9Y2IMM7cAJKK8Bf8TXG0ZwGIFj0erwKMZ+MnDgxqBQIn2xyqVsCclsDk3lfgCwAA//8hfs5cAAAABklEQVQDAHAhlmH5fJt3AAAAAElFTkSuQmCC" },
        };

        private static readonly Dictionary<string, string> _icons = new Dictionary<string, string>();

        private class RoundedImage : ICuiComponent
        {
            [JsonProperty("type")] public string Type => "UnityEngine.UI.Image";
            [JsonProperty("png")] public string Png;
            [JsonProperty("color")] public string Color;
            [JsonProperty("imagetype")] public string ImageType = "Sliced";
            [JsonProperty("slice")] public string Slice = "16 16 16 16";
            [JsonProperty("ppuMultiplier")] public float Ppu = 2f;
        }

        private void InitRoundedSprite()
        {
            _roundPng = null;
            _icons.Clear();
            try
            {
                var net = CommunityEntity.ServerInstance.net.ID;
                if (_config.RoundedCorners) _roundPng = FileStorage.server.StoreByCrc(net, FileStorage.Type.png, Convert.FromBase64String(RoundPngB64)).ToString();
                foreach (var kv in IconB64)
                    _icons[kv.Key] = FileStorage.server.StoreByCrc(net, FileStorage.Type.png, Convert.FromBase64String(kv.Value)).ToString();
            }
            catch (Exception ex)
            {
                PrintWarning("UI images disabled: " + ex.Message);
            }
        }

        // ---------------- примитивы (x, y — от левого верхнего угла родителя)
        private static string Mn(float x, float y, float h) => $"{x.ToString("0.##", CultureInfo.InvariantCulture)} {(-y - h).ToString("0.##", CultureInfo.InvariantCulture)}";
        private static string Mx(float x, float y, float w) => $"{(x + w).ToString("0.##", CultureInfo.InvariantCulture)} {(-y).ToString("0.##", CultureInfo.InvariantCulture)}";

        private static void Box(CuiElementContainer c, string parent, string color, float x, float y, float w, float h, float r = 0f, string name = null)
        {
            r = Mathf.Min(r, Mathf.Min(w, h) / 2f);
            if (r > 0.5f && _roundPng != null)
            {
                c.Add(new CuiElement
                {
                    Name = name ?? CuiHelper.GetGuid(),
                    Parent = parent,
                    Components =
                    {
                        new RoundedImage { Png = _roundPng, Color = color, Ppu = 16f / r },
                        new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = Mn(x, y, h), OffsetMax = Mx(x, y, w) }
                    }
                });
                return;
            }
            c.Add(new CuiPanel { Image = { Color = color }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = Mn(x, y, h), OffsetMax = Mx(x, y, w) } }, parent, name ?? CuiHelper.GetGuid());
        }

        // рамка 1px: внешний прямоугольник цветом линии, внутри — фон
        private static void BBox(CuiElementContainer c, string parent, string bg, string line, float x, float y, float w, float h, float r)
        {
            Box(c, parent, line, x, y, w, h, r);
            // полупрозрачный фон на яркой рамке — сначала глушим рамку тёмной подложкой, иначе цвет рамки просвечивает
            var la = line.Split(' '); var ba = bg.Split(' ');
            if (la.Length == 4 && ba.Length == 4 && float.Parse(la[3], CultureInfo.InvariantCulture) >= 0.3f && float.Parse(ba[3], CultureInfo.InvariantCulture) < 0.95f)
                Box(c, parent, "0.071 0.055 0.055 0.96", x + 1, y + 1, w - 2, h - 2, Mathf.Max(0f, r - 1));
            Box(c, parent, bg, x + 1, y + 1, w - 2, h - 2, Mathf.Max(0f, r - 1));
        }

        private static void Tx(CuiElementContainer c, string parent, string text, int size, float x, float y, float w, float h,
            TextAnchor align = TextAnchor.MiddleLeft, string color = CText, bool bold = true)
        {
            c.Add(new CuiLabel
            {
                Text = { Text = text ?? "", FontSize = size, Align = align, Color = color, Font = bold ? FontName : FontReg },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = Mn(x, y, h), OffsetMax = Mx(x, y, w) }
            }, parent);
        }

        private static void Ic(CuiElementContainer c, string parent, string icon, float x, float y, float s, string color = CText)
        {
            string png;
            if (!_icons.TryGetValue(icon, out png)) return;
            c.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiRawImageComponent { Png = png, Color = color },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = Mn(x, y, s), OffsetMax = Mx(x, y, s) }
                }
            });
        }

        private static void Hit(CuiElementContainer c, string parent, string cmd, float x, float y, float w, float h)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            c.Add(new CuiButton
            {
                Button = { Command = cmd, Color = "0 0 0 0" },
                Text = { Text = "" },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = Mn(x, y, h), OffsetMax = Mx(x, y, w) }
            }, parent);
        }

        // квадратная кнопка-иконка (iconbtn)
        private static void IBtn(CuiElementContainer c, string parent, string icon, string cmd, float x, float y, float s,
            string bg = CSurf, string color = CText, float r = 10f, float icon_s = 16f)
        {
            if (bg != null) Box(c, parent, bg, x, y, s, s, r);
            Ic(c, parent, icon, x + (s - icon_s) / 2f, y + (s - icon_s) / 2f, icon_s, color);
            Hit(c, parent, cmd, x, y, s, s);
        }

        // кнопка с подписью: primary / rec / ghost / outline / soft (как btn() в макете)
        private static void PBtn(CuiElementContainer c, string parent, string text, string icon, string cmd, string kind,
            float x, float y, float w, float h, int fs = 13, float r = -1f)
        {
            if (r < 0f) r = h <= 40 ? h / 2f : 14f;
            string bg = kind == "primary" ? CAcc : kind == "rec" ? CRec : kind == "soft" ? CSoft : kind == "outline" ? null : CSurf;
            string col = kind == "soft" ? CRose : CText;
            if (kind == "outline") BBox(c, parent, "0.071 0.055 0.055 1", "1 1 1 0.14", x, y, w, h, r);
            else if (kind == "soft") BBox(c, parent, bg, CSoftLine, x, y, w, h, r);
            else Box(c, parent, bg, x, y, w, h, r);
            float tw = TextW(text, fs);
            float total = (icon != null ? 15f + 8f : 0f) + tw;
            float sx = x + (w - total) / 2f;
            if (icon != null) { Ic(c, parent, icon, sx, y + (h - 15f) / 2f, 15f, col); sx += 23f; }
            Tx(c, parent, text, fs, sx - 2, y, tw + 8, h, TextAnchor.MiddleLeft, col);
            Hit(c, parent, cmd, x, y, w, h);
        }

        // ширина текста Roboto Condensed (оценка по макету)
        private static float TextW(string s, int fs)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            float w = 0f;
            foreach (var ch in s)
                w += ch == ' ' ? 0.24f : char.IsDigit(ch) ? 0.5f : ch < 128 ? (char.IsUpper(ch) ? 0.56f : 0.47f) : (char.IsUpper(ch) ? 0.62f : 0.53f);
            return w * fs;
        }

        // чип: иконка 12 + текст 11 на подложке (chip() в макете). Возвращает ширину.
        private static float Chip(CuiElementContainer c, string parent, string text, string icon, float x, float y,
            string color = CDim, string bg = CSurf, int fs = 11, string cmd = null)
        {
            float w = 16f + (icon != null ? 16f : 0f) + TextW(text, fs);
            Box(c, parent, bg, x, y, w, 20, 10f);
            float tx = x + 8;
            if (icon != null) { Ic(c, parent, icon, tx, y + 4, 12, color); tx += 16; }
            Tx(c, parent, text, fs, tx, y, w - (tx - x), 20, TextAnchor.MiddleLeft, color, false);
            Hit(c, parent, cmd, x, y, w, 20);
            return w;
        }

        private static void Cap(CuiElementContainer c, string parent, string text, float x, float y, float w = 300f, string color = CFaint)
            => Tx(c, parent, (text ?? "").ToUpper(), 11, x, y, w, 13, TextAnchor.MiddleLeft, color);

        // переключатель 38×22
        private static void Sw(CuiElementContainer c, string parent, bool on, string cmd, float x, float y)
        {
            Box(c, parent, on ? CAcc : CTrack, x, y, 38, 22, 11f);
            Box(c, parent, CWhite, x + (on ? 18 : 2), y + 2, 18, 18, 9f);
            Hit(c, parent, cmd, x, y, 38, 22);
        }

        // степпер «− значение +» 128×32
        private static void Step(CuiElementContainer c, string parent, string value, string cmdMinus, string cmdPlus, float x, float y, float w = 128f)
        {
            Box(c, parent, CSurf, x, y, w, 32, 10f);
            IBtn(c, parent, "minus", cmdMinus, x + 2, y + 2, 28, null, CDim, 8f);
            Tx(c, parent, value, 13, x + 32, y, w - 64, 32, TextAnchor.MiddleCenter, CText);
            IBtn(c, parent, "plus", cmdPlus, x + w - 30, y + 2, 28, null, CDim, 8f);
        }

        // ползунок: дорожка 6, заливка, кружок 16; клик по дорожке ставит значение (cmd + " " + доля 0..1)
        private static void Slider(CuiElementContainer c, string parent, float pct, string cmd, float x, float y, float w, string valText = null, float valW = 44f)
        {
            pct = Mathf.Clamp01(pct);
            float tw = valText != null ? w - valW - 10 : w;
            Box(c, parent, "1 1 1 0.1", x, y + 5, tw, 6, 3f);
            if (pct > 0.01f) Box(c, parent, CAcc, x, y + 5, Mathf.Max(6f, tw * pct), 6, 3f);
            Box(c, parent, CWhite, x + tw * pct - 8, y, 16, 16, 8f);
            if (valText != null) Tx(c, parent, valText, 12, x + tw + 10, y - 1, valW, 18, TextAnchor.MiddleRight, CText);
            if (string.IsNullOrEmpty(cmd)) return;
            const int n = 20;
            for (int i = 0; i < n; i++)
                Hit(c, parent, cmd + " " + ((i + 0.5f) / n).ToString("0.###", CultureInfo.InvariantCulture), x + tw * i / n, y - 4, tw / n + 0.5f, 24);
        }

        private static void Kbd(CuiElementContainer c, string parent, string key, float x, float y, float w = 38f, float h = 23f)
        {
            BBox(c, parent, CKbd, "1 1 1 0.12", x, y, w, h, 6f);
            Tx(c, parent, key, 10, x, y, w, h, TextAnchor.MiddleCenter, CText);
        }

        // поле ввода (inp() в макете)
        private static void Inp(CuiElementContainer c, string parent, string name, string text, string placeholder, string cmd,
            float x, float y, float w, float h = 40f, string icon = null)
        {
            BBox(c, parent, CInput, CLine, x, y, w, h, 11f);
            float tx = x + 12;
            if (icon != null) { Ic(c, parent, icon, tx, y + (h - 15) / 2f, 15, CFaint); tx += 23; }
            if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(placeholder))
                Tx(c, parent, placeholder, 13, tx, y, w - (tx - x) - 10, h, TextAnchor.MiddleLeft, CFaint, false);
            c.Add(new CuiElement
            {
                Name = name,
                Parent = parent,
                Components =
                {
                    new CuiInputFieldComponent { Text = text ?? "", Command = cmd, FontSize = 13, Align = TextAnchor.MiddleLeft, CharsLimit = 40, Color = CText, Font = FontReg },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = Mn(tx, y, h), OffsetMax = Mx(tx, y, w - (tx - x) - 10) }
                }
            });
        }

        // карточка-секция с заголовком (card() в макете); возвращает y начала содержимого
        private static float Card(CuiElementContainer c, string parent, string title, float x, float y, float w, float h)
        {
            BBox(c, parent, CSurf, CLine, x, y, w, h, 14f);
            if (title == null) return y + 11;
            Cap(c, parent, title, x + 11, y + 11, w - 22);
            return y + 11 + 13 + 8;
        }

        // сегменты (seg() в макете)
        private static void Seg(CuiElementContainer c, string parent, string[] labels, int active, string cmdPrefix, float x, float y, float segW = 44f, float h = 28f)
        {
            float w = 4 + labels.Length * segW + (labels.Length - 1) * 2;
            Box(c, parent, CSurf, x, y, w, h + 4, 10f);
            for (int i = 0; i < labels.Length; i++)
            {
                float sx = x + 2 + i * (segW + 2);
                if (i == active) Box(c, parent, CAcc, sx, y + 2, segW, h, 8f);
                Tx(c, parent, labels[i], 12, sx, y + 2, segW, h, TextAnchor.MiddleCenter, i == active ? CWhite : CDim);
                Hit(c, parent, cmdPrefix + " " + i, sx, y + 2, segW, h);
            }
        }

        private static string Time2(float s)
        {
            if (s < 0f) s = 0f;
            int t = Mathf.FloorToInt(s);
            return $"{t / 60:00}:{t % 60:00}";
        }

        private static string Time3(float s)
        {
            if (s < 0f) s = 0f;
            int t = Mathf.FloorToInt(s);
            int d = Mathf.FloorToInt((s - t) * 10f);
            return $"{t / 60:00}:{t % 60:00}.{d}";
        }

        private static string F1(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        private static string F2(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        private void CloseUi(BasePlayer player)
        {
            _uiOpen.Remove(Id(player));
            CuiHelper.DestroyUi(player, UiPlayhead);
            CuiHelper.DestroyUi(player, UiDock);
            CuiHelper.DestroyUi(player, UiMain);
        }

        private void OpenUi(BasePlayer player)
        {
            var id = Id(player);
            if (_recorders.ContainsKey(id) || _countdowns.ContainsKey(id) || _cameras.ContainsKey(id)) return;
            _uiOpen.Add(id);
            if (_hudShown.Remove(id)) { CuiHelper.DestroyUi(player, UiHud); CuiHelper.DestroyUi(player, UiPbHud); }
            var c = BuildUi(player);
            CuiHelper.DestroyUi(player, UiPlayhead);
            CuiHelper.DestroyUi(player, UiDock);
            CuiHelper.DestroyUi(player, UiMain);
            CuiHelper.AddUi(player, c);
            UpdatePlayhead(player);
        }

        private static void Glass(CuiElementContainer c, string name, string parent, string amin, string amax, string omin, string omax, float r, bool cursor)
        {
            var el = new CuiElement { Name = name, Parent = parent };
            if (_roundPng != null) el.Components.Add(new RoundedImage { Png = _roundPng, Color = CLine, Ppu = 16f / r });
            else el.Components.Add(new CuiImageComponent { Color = CGlass });
            el.Components.Add(new CuiRectTransformComponent { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax });
            if (cursor) el.Components.Add(new CuiNeedsCursorComponent());
            c.Add(el);
            if (_roundPng != null)
                c.Add(new CuiElement
                {
                    Parent = name,
                    Components =
                    {
                        new RoundedImage { Png = _roundPng, Color = CGlass, Ppu = 16f / (r - 1) },
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "1 1", OffsetMax = "-1 -1" }
                    }
                });
        }

        private CuiElementContainer BuildUi(BasePlayer player)
        {
            var id = Id(player);
            var owner = OwnerOf(id);
            var data = GetData(owner);
            var scene = data.Scene;
            var ui = Ui(id);
            var pb = GetPlayback(owner);
            var c = new CuiElementContainer();
            const string P = UiMain;

            Glass(c, P, "Overlay", "0 1", "0 1", $"20 {-20 - PH}", $"{20 + PW} -20", 22f, true);

            // ---------------- шапка
            Box(c, P, CAcc, 17, 19, 40, 40, 12f);
            Ic(c, P, "video", 27, 29, 20, CWhite);
            Tx(c, P, "VideoRecorder", 18, 69, 21, 262, 21);
            string sub = owner != id ? T(id, "GuestOf", BasePlayer.FindByID(owner)?.displayName ?? owner.ToString()) : T(id, "Subtitle") + " · 3.5";
            Tx(c, P, sub, 12, 69, 43, 262, 14, TextAnchor.MiddleLeft, CDim, false);
            int undo = UndoCount(owner);
            IBtn(c, P, "undo", "vr.ui undo", 343, 22, 34);
            if (undo > 0)
            {
                Box(c, P, CAcc, 357, 18, 24, 16, 8f);
                Tx(c, P, undo.ToString(), 10, 357, 18, 24, 16, TextAnchor.MiddleCenter, CWhite);
            }
            IBtn(c, P, "x", "vr.ui close", 389, 22, 34);

            bool drill = ui.Tab == "takes" && ui.Selected >= 0 && ui.Selected < scene.Takes.Count;
            if (!drill)
            {
                // ---------------- сцена
                BBox(c, P, CSurf, CLine, 17, 73, 310, 40, 12f);
                Ic(c, P, "clap", 30, 85, 16, CRose);
                string meta = T(id, "SceneMeta", scene.Takes.Count, Time2(scene.Length));
                float mw = TextW(meta, 12);
                Tx(c, P, scene.Name, 14, 56, 73, 289 - mw - 40 - 56 + 17, 40);
                Tx(c, P, meta, 12, 289 - mw, 73, mw + 4, 40, TextAnchor.MiddleLeft, CDim, false);
                Ic(c, P, "down", 299, 86, 15, CDim);
                Hit(c, P, "vr.ui scenemenu", 17, 73, 310, 40);
                IBtn(c, P, "plus", "vr.ui newscene", 335, 73, 40, CSurf, CText, 12f);
                IBtn(c, P, "more", "vr.ui sceneactions", 383, 73, 40, CSurf, CText, 12f);

                // ---------------- вкладки
                Box(c, P, CDark, 17, 125, 406, 42, 12f);
                string[] tabs = { "takes", "camera", "fx", "scene", "weather", "kits", "team", "options" };
                string[] keys = { "TabTakes", "TabCamera", "TabFx", "TabScene", "TabWeather", "TabKits", "TabTeam", "TabOptions" };
                const float tw = (406f - 8f - 7 * 4f) / 8f;
                for (int i = 0; i < tabs.Length; i++)
                {
                    bool a = ui.Tab == tabs[i];
                    float tx = 21 + i * (tw + 4);
                    if (a) Box(c, P, CAcc, tx, 129, tw, 34, 9f);
                    Tx(c, P, T(id, keys[i]), 10, tx - 2, 129, tw + 4, 34, TextAnchor.MiddleCenter, a ? CWhite : CDim);
                    Hit(c, P, "vr.ui tab " + tabs[i], tx, 129, tw, 34);
                }
            }

            switch (ui.Tab)
            {
                case "camera": BuildCamera(c, player, scene, ui); break;
                case "fx": BuildFx(c, player, scene, ui); break;
                case "scene": BuildScene(c, player, data, scene, ui); break;
                case "options": BuildOptions(c, player, data, ui); break;
                case "kits": BuildKits(c, player, ui); break;
                case "team": BuildTeam(c, player, ui); break;
                case "weather": BuildWeather(c, player); break;
                default:
                    if (drill && ui.LookView) BuildLook(c, player, scene, ui);
                    else if (drill) BuildTake(c, player, scene, ui);
                    else BuildTakes(c, player, data, scene, ui);
                    break;
            }

            // выпадающие меню сцены — поверх содержимого
            if (!drill && ui.SceneMenu) BuildSceneMenu(c, player, data);
            if (!drill && ui.SceneActions) BuildSceneActions(c, player, ui);

            BuildDock(c, player, data, scene, pb, "1 0", "1 0", $"{-20 - DW} 20", $"-20 {20 + DH}", true);
            return c;
        }

        private void BuildSceneMenu(CuiElementContainer c, BasePlayer player, PlayerData data)
        {
            var id = Id(player);
            const string P = UiMain;
            int n = Mathf.Min(data.Scenes.Count, 8);
            float h = 8 + n * 36;
            BBox(c, P, "0.09 0.07 0.07 0.98", CLine, 17, 117, 310, h, 12f);
            for (int i = 0; i < n; i++)
            {
                var s = data.Scenes[i];
                float y = 121 + i * 36;
                if (i == data.Current) Box(c, P, CHi, 21, y, 302, 32, 9f);
                Ic(c, P, "clap", 31, y + 8, 16, i == data.Current ? CRose : CDim);
                Tx(c, P, s.Name, 13, 55, y, 180, 32);
                Tx(c, P, T(id, "SceneMeta", s.Takes.Count, Time2(s.Length)), 11, 235, y, 84, 32, TextAnchor.MiddleRight, CDim, false);
                Hit(c, P, "vr.ui scenepick " + i, 21, y, 302, 32);
            }
        }

        private void BuildSceneActions(CuiElementContainer c, BasePlayer player, UiState ui)
        {
            var id = Id(player);
            const string P = UiMain;
            bool confirm = Time.realtimeSinceStartup - ui.ConfirmDeleteScene < 5f;
            BBox(c, P, "0.09 0.07 0.07 0.98", CLine, 243, 117, 180, 120, 12f);
            string[] labels = { T(id, "DupScene"), T(id, "Export"), confirm ? T(id, "Sure") : T(id, "DeleteScene") };
            string[] icons = { "copy", "import", "x" };
            string[] cmds = { "vr.ui dupscene", "vr.ui export", "vr.ui delscene" };
            for (int i = 0; i < 3; i++)
            {
                float y = 121 + i * 38;
                if (i == 2 && confirm) Box(c, P, CRec, 247, y, 172, 34, 9f);
                Ic(c, P, icons[i], 257, y + 9, 16, i == 2 ? (confirm ? CWhite : "0.92 0.42 0.48 1") : CText);
                Tx(c, P, labels[i], 13, 281, y, 136, 34, TextAnchor.MiddleLeft, i == 2 && !confirm ? "0.92 0.42 0.48 1" : CText);
                Hit(c, P, cmds[i], 247, y, 172, 34);
            }
        }

        // ---------------- док: таймлайн и транспорт
        private float TimelineLength(Scene scene, ulong owner)
        {
            var pb = GetPlayback(owner);
            float len = Mathf.Max(scene.Length, GetData(owner).Seek + 1f);
            if (pb != null) len = Mathf.Max(len, pb.Time);
            return Mathf.Max(10f, len);
        }

        private static float NiceStep(float len)
        {
            foreach (var s in new[] { 1f, 2f, 4f, 5f, 10f, 15f, 20f, 30f, 60f, 120f, 300f })
                if (len / s <= 7f) return s;
            return 600f;
        }

        private void BuildDock(CuiElementContainer c, BasePlayer player, PlayerData data, Scene scene, Playback pb,
            string amin, string amax, string omin, string omax, bool interactive, float dw = DW)
        {
            var id = Id(player);
            var owner = OwnerOf(id);
            float len = TimelineLength(scene, owner);
            string D = interactive ? UiDock : UiPbHud + ".dock";
            Glass(c, D, interactive ? "Overlay" : UiPbHud, amin, amax, omin, omax, 22f, false);
            float trW = dw - 38f;

            // шкала времени
            float step = NiceStep(len);
            for (float s = 0f; s <= len + 0.01f; s += step)
            {
                float x = TrX + trW * s / len;
                Tx(c, D, $"{Mathf.FloorToInt(s) / 60}:{Mathf.FloorToInt(s) % 60:00}", 10, x - 20, 33, 40, 11, TextAnchor.MiddleCenter, CFaint, false);
            }

            BBox(c, D, "1 1 1 0.05", CLine, TrX, TrY, trW, TrH, 11f);

            // дубли — цветные дорожки
            int k = 0;
            foreach (var take in scene.Takes)
            {
                if (k >= 5) break;
                float a = Mathf.Clamp01(take.Delay / len), b = take.Loop ? 1f : Mathf.Clamp01(take.SceneLength / len);
                var col = TakeCols[scene.Takes.IndexOf(take) % TakeCols.Length];
                if (!take.Enabled) col = col.Substring(0, col.LastIndexOf(' ')) + " 0.35";
                Box(c, D, col, TrX + 1 + a * (trW - 2), TrY + 9 + k * 6, Mathf.Max(3f, (b - a) * (trW - 2)), 4, 2f);
                k++;
            }

            // маркеры
            foreach (var m in scene.Markers)
            {
                float x = TrX + Mathf.Clamp01(m.t / len) * trW;
                Box(c, D, "0.953 0.914 0.906 1", x - 1, TrY - 1, 2, TrH + 2, 1f);
                float lw = 30f + TextW(m.n, 10);
                Box(c, D, "0.953 0.914 0.906 0.14", x + 3, TrY - 21, lw, 18, 9f);
                Ic(c, D, "flag", x + 10, TrY - 17, 10, "0.953 0.914 0.906 1");
                Tx(c, D, m.n, 10, x + 24, TrY - 21, lw - 24, 18, TextAnchor.MiddleLeft, "0.953 0.914 0.906 1");
            }
            // эффекты — ромбы, точки камеры — треугольники
            foreach (var cu in scene.Cues) Ic(c, D, "diamond", TrX + Mathf.Clamp01(cu.t / len) * trW - 6, TrY + TrH - 16, 13, CRec);
            foreach (var key in scene.Camera.Keys) Ic(c, D, "tridown", TrX + Mathf.Clamp01(key.t / len) * trW - 5, TrY + 1, 10, CWhite);

            // перемотка кликом
            if (interactive)
            {
                float sw = trW / Segments;
                for (int i = 0; i < Segments; i++)
                    Hit(c, D, "vr.ui seekto " + (len * (i + 0.5f) / Segments).ToString("0.0", CultureInfo.InvariantCulture), TrX + i * sw, TrY - 4, sw + 0.5f, TrH + 8);
            }

            // транспорт
            bool playing = pb != null && !pb.Paused;
            IBtn(c, D, "back", interactive ? "vr.ui tostart" : null, 19, 115, 36, CSurf, CText, 18f);
            IBtn(c, D, playing ? "pause" : "play", interactive ? "vr.ui pause" : null, 63, 111, 44, CAcc, CWhite, 22f);
            IBtn(c, D, "stop", interactive ? "vr.ui end" : null, 115, 115, 36, CSurf, CText, 18f);
            Box(c, D, CLine, 165, 121, 1, 24);

            float rx = dw - 19f;   // правый край кнопок
            // Запись F8
            float recW = 138f;
            Box(c, D, CRec, rx - recW, 113, recW, 40, 20f);
            Box(c, D, CWhite, rx - recW + 12, 127, 12, 12, 6f);
            Tx(c, D, T(id, "RecShort"), 13, rx - recW + 32, 113, 60, 40, TextAnchor.MiddleLeft, CWhite);
            Kbd(c, D, "F8", rx - 54, 122);
            Hit(c, D, interactive ? "vr.ui record" : null, rx - recW, 113, recW, 40);
            rx -= recW + 8;
            // Камера (outline)
            PBtn(c, D, T(id, "Camera"), "video", interactive ? "vr.ui cam" : null, "outline", rx - 97, 116, 97, 34, 12, 17f);
            rx -= 97 + 8;
            // скорость сцены
            PBtn(c, D, F2(data.GlobalSpeed) + "×", null, interactive ? "vr.ui gspeedcycle" : null, "ghost", rx - 42, 116, 42, 34, 12, 17f);
            rx -= 42 + 9;
            // слоу-мо
            bool slow = pb != null && pb.Slowmo;
            PBtn(c, D, T(id, "SlowmoBtn", F2(data.SlowmoSpeed)), "clock", interactive ? "vr.ui slowmo" : null, slow ? "primary" : "ghost", rx - 127, 116, 127, 34, 12, 17f);
        }

        private void UpdatePlayhead(BasePlayer player)
        {
            var id = Id(player);
            if (!_uiOpen.Contains(id)) return;
            var owner = OwnerOf(id);
            var scene = GetData(owner).Scene;
            var pb = GetPlayback(owner);
            float len = TimelineLength(scene, owner);
            float t = pb != null ? pb.Time : GetData(owner).Seek;

            var c = new CuiElementContainer();
            c.Add(new CuiElement { Name = UiPlayhead, Parent = UiDock, Components = { new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } } });
            PlayheadInto(c, UiPlayhead, id, t, len, pb, DW);
            CuiHelper.DestroyUi(player, UiPlayhead);
            CuiHelper.AddUi(player, c);
        }

        private void PlayheadInto(CuiElementContainer c, string parent, ulong id, float t, float len, Playback pb, float dw)
        {
            float trW = dw - 38f;
            float x = TrX + Mathf.Clamp01(t / len) * trW;
            if (x - TrX > 2) Box(c, parent, "0.639 0.086 0.184 0.22", TrX + 1, TrY + 1, x - TrX - 1, TrH - 2, Mathf.Min(10f, (x - TrX) / 2f));
            Box(c, parent, CWhite, x - 1, TrY - 5, 2, TrH + 10, 1f);
            Box(c, parent, CWhite, x - 6, TrY - 9, 12, 12, 6f);
            Tx(c, parent, Time3(t), 18, 180, 113, 140, 21, TextAnchor.MiddleLeft, CText);
            string state = pb == null ? T(id, "Stopped") : pb.Paused ? T(id, "Paused") : pb.Slowmo ? T(id, "SlowmoOn") : T(id, "Playing");
            Tx(c, parent, T(id, "OfTime", Time3(len), state.ToLower()), 11, 180, 134, 160, 13, TextAnchor.MiddleLeft, CDim, false);
        }

        // ---------------- вкладка: дубли
        private void BuildTakes(CuiElementContainer c, BasePlayer player, PlayerData data, Scene scene, UiState ui)
        {
            var id = Id(player);
            var my = GetData(id);
            const string P = UiMain;

            Cap(c, P, T(id, "NextName"), 17, 179);
            Inp(c, P, UiMain + ".name", my.NextName, T(id, "NextNameHint"), "vr.ui name", 17, 198, 312, 40, "user");
            Chip(c, P, T(id, "AutoName", scene.Takes.Count + 1), null, 337, 198);

            int pages = Mathf.Max(1, Mathf.CeilToInt(scene.Takes.Count / (float)TakesPerPage));
            ui.Page = Mathf.Clamp(ui.Page, 0, pages - 1);
            Cap(c, P, T(id, "TakesCount", scene.Takes.Count), 17, 253);
            if (pages > 1)
            {
                IBtn(c, P, "left", "vr.ui page -1", 220, 247, 24, null, CDim, 6f, 14f);
                Tx(c, P, $"{ui.Page + 1}/{pages}", 11, 244, 247, 30, 24, TextAnchor.MiddleCenter, CDim);
                IBtn(c, P, "right", "vr.ui page 1", 274, 247, 24, null, CDim, 6f, 14f);
            }
            bool allOn = scene.Takes.Count > 0 && scene.Takes.All(t => t.Enabled);
            Tx(c, P, allOn ? T(id, "DisableAll") : T(id, "EnableAll"), 12, 300, 252, 123, 14, TextAnchor.MiddleRight, CRose);
            Hit(c, P, "vr.ui enableall", 330, 247, 93, 24);

            if (scene.Takes.Count == 0)
            {
                Box(c, P, CSurf, 17, 276, 406, 324, 14f);
                Ic(c, P, "clap", 204, 390, 32, CFaint);
                Tx(c, P, T(id, "EmptyTakes"), 13, 37, 430, 366, 60, TextAnchor.UpperCenter, CDim, false);
            }

            int from = ui.Page * TakesPerPage, to = Mathf.Min(scene.Takes.Count, from + TakesPerPage);
            for (int i = from; i < to; i++)
            {
                var take = scene.Takes[i];
                float y = 276 + (i - from) * 66;
                bool hi = i == scene.Takes.Count - 1;
                BBox(c, P, hi ? CHi : CSurf, hi ? CHiLine : CLine, 17, y, 406, 60, 14f);
                Box(c, P, TakeCols[i % TakeCols.Length], 28, y + 15, 4, 30, 2f);
                if (take.Enabled)
                {
                    Box(c, P, CAcc, 42, y + 19, 22, 22, 7f);
                    Ic(c, P, "check", 46, y + 23, 14, CWhite);
                }
                else
                {
                    Box(c, P, "1 1 1 0.22", 42, y + 19, 22, 22, 7f);
                    Box(c, P, "0.09 0.07 0.07 1", 44, y + 21, 18, 18, 5f);
                }
                Hit(c, P, $"vr.ui toggle {i}", 38, y + 15, 30, 30);

                string op = take.Enabled ? CText : "0.937 0.914 0.906 0.55";
                Tx(c, P, $"{i + 1}. {take.Name}", 14, 74, y + 10, 172, 16, TextAnchor.MiddleLeft, op);
                float cx = 74;
                cx += Chip(c, P, Time3(take.Duration), "clock", cx, y + 30) + 4;
                var tags = new List<KeyValuePair<string, string>>();
                if (take.Delay > 0f) tags.Add(new KeyValuePair<string, string>("clock", "+" + F1(take.Delay) + " с"));
                if (take.Loop) tags.Add(new KeyValuePair<string, string>("loop", T(id, "TagLoop")));
                if (take.DieAt >= 0f) tags.Add(new KeyValuePair<string, string>("skull", T(id, "TagDie") + " " + F1(take.DieAt) + " с"));
                if (!take.Damage) tags.Add(new KeyValuePair<string, string>("shield", T(id, "TagBlank")));
                if (take.God) tags.Add(new KeyValuePair<string, string>("shield", T(id, "TagGod")));
                if (take.Vehicles.Count > 0) tags.Add(new KeyValuePair<string, string>("car", T(id, "TagVehicle")));
                if (take.Voice.Count > 0) tags.Add(new KeyValuePair<string, string>("mic", T(id, "TagVoice")));
                foreach (var tg in tags)
                {
                    float w = 32f + TextW(tg.Value, 11);
                    if (cx + w > 250) break;
                    cx += Chip(c, P, tg.Value, tg.Key, cx, y + 30) + 4;
                }

                IBtn(c, P, "play", $"vr.ui play {i}", 256, y + 14, 32, null, CText, 9f);
                IBtn(c, P, "edit", $"vr.ui settings {i}", 298, y + 14, 32, null, CText, 9f);
                IBtn(c, P, "rerec", $"vr.ui rerec {i}", 340, y + 14, 32, null, CText, 9f);
                bool confirm = ui.ConfirmTake == i && Time.realtimeSinceStartup - ui.ConfirmTakeAt < 5f;
                if (confirm) Box(c, P, CRec, 382, y + 14, 32, 32, 9f);
                IBtn(c, P, confirm ? "x" : "more", $"vr.ui takemore {i}", 382, y + 14, 32, null, confirm ? CWhite : CDim, 9f);
            }

            PBtn(c, P, T(id, "ShowPaths"), "route", "vr.ui paths", "soft", 17, 625, 406, 38, 13, 19f);
        }

        // ---------------- редактор дубля
        private void BuildTake(CuiElementContainer c, BasePlayer player, Scene scene, UiState ui)
        {
            var id = Id(player);
            var take = scene.Takes[ui.Selected];
            const string P = UiMain;
            var col = TakeCols[ui.Selected % TakeCols.Length];

            // хлебные крошки
            Ic(c, P, "left", 17, 78, 14, CRose);
            Tx(c, P, T(id, "TabTakes"), 12, 35, 71, 45, 28, TextAnchor.MiddleLeft, CRose);
            Hit(c, P, "vr.ui back", 15, 71, 60, 28);
            Tx(c, P, "/", 12, 78, 71, 8, 28, TextAnchor.MiddleLeft, CFaint, false);
            Tx(c, P, T(id, "TakeN", ui.Selected + 1), 12, 91, 71, 50, 28, TextAnchor.MiddleLeft, CText);
            int shots = take.Events.Count(e => e.e == EvFire);
            string shotsText = T(id, "Shots", shots);
            float lookW = 92f, sw2 = 32f + TextW(shotsText, 11), dw2 = 32f + TextW(Time3(take.Duration), 11);
            float cx = 423 - lookW - 8 - sw2 - 8 - dw2;
            Chip(c, P, Time3(take.Duration), "clock", cx, 75);
            Chip(c, P, shotsText, "crosshair", cx + dw2 + 8, 75);
            Box(c, P, "0.639 0.086 0.184 0.25", 423 - lookW, 74, lookW, 22, 11f);
            Ic(c, P, "user", 423 - lookW + 9, 79, 12, CRose);
            Tx(c, P, T(id, "Look"), 11, 423 - lookW + 25, 74, lookW - 30, 22, TextAnchor.MiddleLeft, CRose);
            Hit(c, P, "vr.ui look", 423 - lookW, 74, lookW, 22);

            // имя + пуск
            Box(c, P, col, 17, 118, 14, 14, 7f);
            Inp(c, P, UiMain + ".tname", take.Name, T(id, "TakeNameHint"), "vr.ui t.name", 39, 105, 338, 40);
            IBtn(c, P, "play", $"vr.ui play {ui.Selected}", 385, 106, 38, CAcc, CWhite, 11f);

            // тайминг
            Card(c, P, T(id, "Timing"), 17, 153, 406, 149);
            Tx(c, P, T(id, "Delay"), 13, 28, 183, 200, 32, TextAnchor.MiddleLeft, CText, false);
            Step(c, P, (take.Delay >= 0 ? "+" : "") + F1(take.Delay) + " с", "vr.ui t.delay -0.5", "vr.ui t.delay 0.5", 284, 183);
            Tx(c, P, T(id, "Speed"), 13, 28, 221, 150, 32, TextAnchor.MiddleLeft, CText, false);
            float[] speeds = { 0.5f, 1f, 1.5f, 2f };
            int si = Array.FindIndex(speeds, s => Mathf.Abs(s - take.Speed) < 0.01f);
            Seg(c, P, new[] { "0.5×", "1×", "1.5×", "2×" }, si, "vr.ui t.speedset", 226, 221);
            bool die = take.DieAt >= 0f;
            Sw(c, P, die, die ? "vr.ui t.die off" : "vr.ui t.die on", 28, 264);
            Tx(c, P, T(id, "DieAt"), 13, 74, 259, 200, 32, TextAnchor.MiddleLeft, CText, false);
            Step(c, P, die ? Time3(take.DieAt).Substring(3) + " с" : "—", "vr.ui t.die -0.5", "vr.ui t.die 0.5", 284, 259);

            // поведение
            Card(c, P, T(id, "Behaviour"), 17, 310, 406, 131);
            SwCell(c, P, "check", T(id, "InScene"), take.Enabled, "vr.ui t.enabled", 30, 336);
            SwCell(c, P, "loop", T(id, "Loop"), take.Loop, "vr.ui t.loop", 229, 336);
            SwCell(c, P, "crosshair", T(id, "Damage"), take.Damage, "vr.ui t.damage", 30, 368);
            SwCell(c, P, "shield", T(id, "God"), take.God, "vr.ui t.god", 229, 368);
            SwCell(c, P, "mic", T(id, "Voice"), take.PlayVoice, "vr.ui t.voice", 30, 400);
            Ic(c, P, "skull", 229, 408, 14, CDim);
            Slider(c, P, take.Health / 100f, "vr.ui t.healthset", 251, 407, 161, take.Health.ToString("0") + " HP", 44);

            // монтаж
            Card(c, P, T(id, "Trim"), 17, 449, 406, 95);
            Box(c, P, CDark, 28, 479, 384, 34, 9f);
            Box(c, P, "0.851 0.706 0.549 0.22", 51, 479, 326, 34, 0f);
            Box(c, P, col, 51, 479, 3, 34);
            Box(c, P, col, 374, 479, 3, 34);
            Box(c, P, "0.851 0.706 0.549 0.6", 66, 494, 300, 4, 2f);
            for (int i = 0; i < 3; i++) { Box(c, P, "1 1 1 0.06", 31 + i * 7, 481, 3, 30); Box(c, P, "1 1 1 0.06", 380 + i * 10, 481, 3, 30); }
            Hit(c, P, "vr.ui t.trims 0.5", 28, 479, 40, 34);
            Hit(c, P, "vr.ui t.trime 0.5", 372, 479, 40, 34);
            Ic(c, P, "scissors", 28, 520, 13, CDim);
            Tx(c, P, T(id, "TrimStartBtn"), 12, 47, 512, 140, 28, TextAnchor.MiddleLeft, CDim, false);
            Hit(c, P, "vr.ui t.trims 0.5", 26, 512, 150, 28);
            Tx(c, P, T(id, "TrimEndBtn"), 12, 250, 512, 143, 28, TextAnchor.MiddleRight, CDim, false);
            Ic(c, P, "scissors", 399, 520, 13, CDim);
            Hit(c, P, "vr.ui t.trime 0.5", 260, 512, 152, 28);

            // позиция
            string[] icons = { "pin", "flag", "move", "rotate", "rotate", "mirror", "copy" };
            string[] keys = { "TpStart", "TpEnd", "MoveHere", "Rot90", "Rot180", "Mirror", "Copy" };
            string[] cmds = { "vr.ui t.tp", "vr.ui t.tpend", "vr.ui t.movehere", "vr.ui t.rot 90", "vr.ui t.rot 180", "vr.ui t.mirror", "vr.ui t.dup" };
            const float bw = (406f - 6 * 6f) / 7f;
            for (int i = 0; i < 7; i++)
            {
                float bx = 17 + i * (bw + 6);
                Box(c, P, "0 0 0 0.25", bx, 552, bw, 46, 10f);
                Ic(c, P, icons[i], bx + (bw - 15) / 2f, 561, 15, CText);
                Tx(c, P, T(id, keys[i]), 10, bx - 3, 578, bw + 6, 13, TextAnchor.MiddleCenter, CText);
                Hit(c, P, cmds[i], bx, 552, bw, 46);
            }

            PBtn(c, P, T(id, "RerecThis"), "rerec", "vr.ui t.rerec", "rec", 17, 623, 198, 40, 13, 20f);
            PBtn(c, P, T(id, "AppendThis"), "plus", "vr.ui t.append", "outline", 223, 623, 200, 40, 13, 20f);
        }

        private static void SwCell(CuiElementContainer c, string P, string icon, string text, bool on, string cmd, float x, float y)
        {
            Ic(c, P, icon, x, y + 8, 14, CDim);
            Tx(c, P, text, 13, x + 22, y, 120, 30, TextAnchor.MiddleLeft, CText, false);
            Sw(c, P, on, cmd, x + 143, y + 4);
        }

        // ---------------- внешность дубля
        private void BuildLook(CuiElementContainer c, BasePlayer player, Scene scene, UiState ui)
        {
            var id = Id(player);
            var take = scene.Takes[ui.Selected];
            const string P = UiMain;

            Ic(c, P, "left", 17, 78, 14, CRose);
            Tx(c, P, T(id, "TakeN", ui.Selected + 1), 12, 35, 71, 60, 28, TextAnchor.MiddleLeft, CRose);
            Hit(c, P, "vr.ui look.back", 15, 71, 75, 28);
            Tx(c, P, "/", 12, 88, 71, 8, 28, TextAnchor.MiddleLeft, CFaint, false);
            Tx(c, P, T(id, "Look"), 12, 101, 71, 120, 28, TextAnchor.MiddleLeft, CText);

            Box(c, P, TakeCols[ui.Selected % TakeCols.Length], 17, 118, 14, 14, 7f);
            Inp(c, P, UiMain + ".lnick", take.Name, T(id, "TakeNameHint"), "vr.ui t.name", 39, 105, 259, 40, "tag");
            PBtn(c, P, T(id, "NickFromLook"), "copy", "vr.ui look.nick", "ghost", 306, 106, 117, 38, 12, 19f);

            // чья внешность
            Card(c, P, T(id, "WhoseLook"), 17, 153, 406, 201);
            Box(c, P, CSurf, 28, 185, 384, 32, 10f);
            string[] modes = { "LookOwn", "LookMe", "LookPlayer" };
            for (int i = 0; i < 3; i++)
            {
                float sx = 30 + i * 127.5f;
                if (take.LookMode == i) Box(c, P, CAcc, sx, 187, 125, 28, 8f);
                Tx(c, P, T(id, modes[i]), 12, sx, 187, 125, 28, TextAnchor.MiddleCenter, take.LookMode == i ? CWhite : CDim);
                Hit(c, P, "vr.ui look.mode " + i, sx, 187, 125, 28);
            }

            if (take.LookMode == 2)
            {
                var online = BasePlayer.activePlayerList.Where(p => p != null && !_botInfo.ContainsKey(p)).OrderBy(p => p.displayName).Take(8).ToList();
                float px = 28, py = 225;
                foreach (var p in online)
                {
                    string nm = Clip(p.displayName, 14);
                    float w = 24 + 8 + TextW(nm, 12) + 14;
                    if (px + w > 412) { px = 28; py += 38; }
                    if (py > 263) break;
                    bool sel = take.LookSteamId == Id(p);
                    Box(c, P, sel ? CAcc : CSurf, px, py, w, 32, 16f);
                    Box(c, P, CDark, px + 4, py + 4, 24, 24, 12f);
                    Ic(c, P, "user", px + 10, py + 10, 13, sel ? CWhite : CText);
                    Tx(c, P, nm, 12, px + 32, py, w - 34, 32, TextAnchor.MiddleLeft, sel ? CWhite : CText);
                    Hit(c, P, "vr.ui look.player " + Id(p), px, py, w, 32);
                    px += w + 6;
                }
                Inp(c, P, UiMain + ".lsid", ui.LookSteamInput, T(id, "SteamHint"), "vr.ui look.steam", 28, 303, 284, 40, "import");
                PBtn(c, P, T(id, "SteamApply"), null, "vr.ui look.steamset", "ghost", 318, 303, 94, 38, 12, 19f);
            }
            else if (take.LookMode == 1)
            {
                var rec = BasePlayer.FindByID(take.RecorderId);
                Tx(c, P, T(id, "LookMeInfo", rec?.displayName ?? take.RecorderId.ToString()), 13, 28, 229, 384, 110, TextAnchor.UpperLeft, CDim, false);
            }
            else
            {
                Tx(c, P, T(id, "FaceHint"), 13, 28, 229, 384, 110, TextAnchor.UpperLeft, CDim, false);
            }

            // своё лицо — пять соседних лиц, середина выбрана
            bool own = take.LookMode == 0;
            Card(c, P, T(id, "OwnFaceCard"), 17, 362, 406, 157);
            ulong seed = take.LookSeed & 0xFFFFUL;
            float[] fx = { 28, 106, 185, 263, 340 };
            for (int i = 0; i < 5; i++)
            {
                int d = i - 2;
                long n = (long)seed + d;
                if (n < 0) n += 65536;
                bool mid = d == 0;
                if (mid) BBox(c, P, own ? "0.639 0.086 0.184 0.22" : CSurf, own ? CRose : CLine, fx[i], 394, mid ? 74 : 72, 74, 12f);
                else BBox(c, P, CSurf, CLine, fx[i], 394, 72, 74, 12f);
                if (mid && own) Box(c, P, "0.639 0.086 0.184 0.22", fx[i] + 2, 396, 70, 70, 10f);
                Ic(c, P, "user", fx[i] + 23, 410, 26, mid && own ? CText : CDim);
                Tx(c, P, $"#{n % 65536:00000}", 11, fx[i], 440, 72, 13, TextAnchor.MiddleCenter, mid && own ? CText : CDim);
                if (d != 0) Hit(c, P, "vr.ui look.step " + d, fx[i], 394, 72, 74);
            }
            PBtn(c, P, T(id, "FaceRandom"), "rerec", "vr.ui look.rand", "ghost", 28, 476, 384, 32, 12, 16f);
            if (!own) Box(c, P, "0.071 0.055 0.055 0.45", 18, 363, 404, 155, 14f);

            // точная копия по SteamID
            if (_config.AllowRealSteamId)
            {
                BBox(c, P, CSurf, CLine, 17, 527, 406, 46, 12f);
                Sw(c, P, take.LookRealId, "vr.ui look.real", 28, 539);
                Tx(c, P, T(id, "RealId"), 13, 76, 533, 336, 18);
                Tx(c, P, T(id, "RealIdHint"), 11, 76, 550, 336, 15, TextAnchor.MiddleLeft, CDim, false);
            }

            PBtn(c, P, T(id, "TryOnFront"), "user", "vr.ui look.try", "primary", 17, 623, 198, 40, 13, 20f);
            PBtn(c, P, T(id, "LookAll"), "layers", "vr.ui look.all", "outline", 223, 623, 200, 40, 13, 20f);
        }

        // ---------------- вкладка: камера
        private static readonly string[] CamModes = { "CamPath", "CamPov", "CamFollow", "CamModeOrbit", "CamWatch" };
        private static readonly string[] CamIcons = { "route", "user", "nav", "orbit", "tripod" };

        private void BuildCamera(CuiElementContainer c, BasePlayer player, Scene scene, UiState ui)
        {
            var id = Id(player);
            var owner = OwnerOf(id);
            var cam = scene.Camera;
            const string P = UiMain;
            int mode = Mathf.Clamp(cam.Mode, 0, CamModes.Length - 1);

            float[] mx = { 17, 99, 182, 264, 347 };
            for (int i = 0; i < 5; i++)
            {
                bool a = mode == i;
                BBox(c, P, a ? "0.639 0.086 0.184 0.22" : CSurf, a ? "0.941 0.651 0.694 0.45" : CLine, mx[i], 179, 76, 64, 12f);
                string label = T(id, CamModes[i]);
                bool two = TextW(label, 11) > 66;
                Ic(c, P, CamIcons[i], mx[i] + 29, two ? 187 : 193, 18, a ? CWhite : CDim);
                Tx(c, P, label, 11, mx[i] + 3, two ? 209 : 213, 70, two ? 28 : 16, TextAnchor.MiddleCenter, a ? CWhite : CDim);
                Hit(c, P, "vr.ui cam.modeset " + i, mx[i], 179, 76, 64);
            }

            // видоискатель: сетка третей и текущая точка
            BBox(c, P, "0 0 0 0.35", "1 1 1 0.14", 17, 253, 198, 112, 12f);
            Box(c, P, "1 1 1 0.35", 83, 254, 1, 110);
            Box(c, P, "1 1 1 0.35", 149, 254, 1, 110);
            Box(c, P, "1 1 1 0.35", 18, 291, 196, 1);
            Box(c, P, "1 1 1 0.35", 18, 327, 196, 1);
            float now = CurrentTime(owner);
            int curKey = -1;
            for (int i = 0; i < cam.Keys.Count; i++) if (cam.Keys[i].t <= now + 0.01f) curKey = i;
            string camTag = mode == 0 ? (cam.Keys.Count == 0 ? T(id, "NoCamKeys") : $"CAM {Mathf.Max(0, curKey) + 1} · {Time3(now)}") : T(id, CamModes[mode]) + " · " + Time3(now);
            float tw = 30 + TextW(camTag, 10);
            Box(c, P, "0 0 0 0.55", 26, 262, tw, 18, 9f);
            Box(c, P, CRec, 33, 268, 6, 6, 3f);
            Tx(c, P, camTag, 10, 45, 262, tw - 19, 18, TextAnchor.MiddleLeft, CWhite);
            Tx(c, P, T(id, "Grid169"), 10, 30, 343, 176, 15, TextAnchor.MiddleRight, "1 1 1 0.8", false);

            // справа: сглаживание, плавность, свободный обзор, путь в мире
            Tx(c, P, T(id, "Smoothing"), 12, 227, 254, 140, 28, TextAnchor.MiddleLeft, CDim, false);
            Tx(c, P, cam.Smooth > 0.05f ? T(id, "Spline") : T(id, "Linear"), 12, 330, 254, 93, 28, TextAnchor.MiddleRight, CText);
            Tx(c, P, T(id, "Smoothness"), 12, 227, 276, 70, 28, TextAnchor.MiddleLeft, CDim, false);
            Slider(c, P, cam.Smooth, "vr.ui cam.smooth", 296, 282, 127);
            Tx(c, P, T(id, "FreeLook"), 12, 227, 302, 155, 28, TextAnchor.MiddleLeft, CDim, false);
            Sw(c, P, cam.FreeLook, "vr.ui cam.free", 385, 305);
            Tx(c, P, T(id, "ShowCamPath"), 12, 227, 332, 155, 28, TextAnchor.MiddleLeft, CDim, false);
            Sw(c, P, Time.realtimeSinceStartup < ui.CamPathUntil, "vr.ui cam.showpath", 385, 335);

            float bottomY = 547;
            if (mode == 0)
            {
                Cap(c, P, T(id, "CamKeysCap", cam.Keys.Count), 17, 376);
                // фокус на цели — чип, клик листает ботов
                string fname = cam.Focus >= 0 && cam.Focus < scene.Takes.Count ? (cam.Focus + 1) + ". " + scene.Takes[cam.Focus].Name : T(id, "FocusNone");
                string ftxt = T(id, "CamFocus", Clip(fname, 14));
                float fw = TextW(ftxt, 11) + 34;
                bool fon = cam.Focus >= 0;
                BBox(c, P, fon ? "0.639 0.086 0.184 0.22" : CSurf, fon ? "0.941 0.651 0.694 0.45" : CLine, 352 - fw, 370, fw, 24, 12f);
                Ic(c, P, "crosshair", 360 - fw, 376, 12, fon ? CWhite : CDim);
                Tx(c, P, ftxt, 11, 378 - fw, 370, fw - 30, 24, TextAnchor.MiddleLeft, fon ? CWhite : CDim);
                Hit(c, P, "vr.ui cam.focus", 352 - fw, 370, fw, 24);
                Tx(c, P, T(id, "Clear"), 12, 300, 375, 123, 14, TextAnchor.MiddleRight, CRose);
                Hit(c, P, "vr.ui cam.clear", 360, 370, 63, 24);
                int first = Mathf.Max(0, Mathf.Min(cam.Keys.Count - 3, curKey - 1));
                for (int r = 0; r < 3 && first + r < cam.Keys.Count; r++)
                {
                    int i = first + r;
                    var k = cam.Keys[i];
                    float y = 399 + r * 48;
                    bool cur = i == curKey;
                    BBox(c, P, cur ? CHi : CSurf, cur ? CHiLine : CLine, 17, y, 406, 42, 12f);
                    Ic(c, P, "tridown", 28, y + 16, 10, CWhite);
                    Tx(c, P, $"CAM {i + 1}", 13, 46, y + 2, 46, 22);
                    Tx(c, P, Time3(k.t), 13, 92, y + 2, 54, 22, TextAnchor.MiddleLeft, CText, false);
                    // кривая скорости до следующей точки — клик меняет
                    bool last = i == cam.Keys.Count - 1;
                    Tx(c, P, last ? "—" : "› " + T(id, CamEases[Mathf.Clamp(k.e, 0, CamEases.Length - 1)]), 10, 46, y + 22, 100, 16, TextAnchor.MiddleLeft, last ? CDim : k.e > 0 ? CRose : CDim, false);
                    if (!last) Hit(c, P, $"vr.ui cam.ease {i}", 40, y + 20, 106, 20);
                    string[] lbl = { "−1", "−0.1", "+0.1", "+1" };
                    string[] dv = { "-1", "-0.1", "0.1", "1" };
                    float[] bx = { 147, 190, 243, 295 };
                    float[] bwid = { 41, 51, 51, 41 };
                    for (int b = 0; b < 4; b++)
                    {
                        Box(c, P, CSurf, bx[b], y + 8, bwid[b], 26, 13f);
                        Tx(c, P, lbl[b], 11, bx[b], y + 8, bwid[b], 26, TextAnchor.MiddleCenter, CText);
                        Hit(c, P, $"vr.ui cam.kt {i} {dv[b]}", bx[b], y + 8, bwid[b], 26);
                    }
                    IBtn(c, P, "pin", $"vr.ui cam.tp {i}", 350, y + 5, 32, null, CText, 9f);
                    IBtn(c, P, "x", $"vr.ui cam.del {i}", 386, y + 5, 32, null, CDim, 9f);
                }
                if (cam.Keys.Count == 0) Tx(c, P, T(id, "CamKeysEmpty"), 12, 17, 399, 406, 140, TextAnchor.MiddleCenter, CDim, false);
                PBtn(c, P, T(id, "CamAddHere", Time3(now)), "plus", "vr.ui cam.add", "soft", 17, bottomY, 406, 38, 13, 19f);
            }
            else
            {
                Cap(c, P, T(id, "CamFollowCap"), 17, 376);
                string target = scene.Takes.Count == 0 ? "—" : $"{Mathf.Clamp(cam.Target, 0, scene.Takes.Count - 1) + 1}. {scene.Takes[Mathf.Clamp(cam.Target, 0, scene.Takes.Count - 1)].Name}";
                BBox(c, P, CSurf, CLine, 17, 399, 406, 140, 14f);
                Tx(c, P, T(id, "CamTarget"), 13, 28, 407, 150, 32, TextAnchor.MiddleLeft, CText, false);
                Box(c, P, CSurf, 196, 407, 216, 32, 10f);
                IBtn(c, P, "left", "vr.ui cam.target -1", 198, 409, 28, null, CDim, 8f, 14f);
                Tx(c, P, Clip(target, 22), 13, 226, 407, 158, 32, TextAnchor.MiddleCenter, CText);
                IBtn(c, P, "right", "vr.ui cam.target 1", 382, 409, 28, null, CDim, 8f, 14f);
                Tx(c, P, T(id, "CamDist"), 13, 28, 445, 150, 32, TextAnchor.MiddleLeft, CText, false);
                Step(c, P, F1(cam.Dist) + " м", "vr.ui cam.dist -0.5", "vr.ui cam.dist 0.5", 284, 445);
                Tx(c, P, T(id, "CamHeight"), 13, 28, 483, 150, 32, TextAnchor.MiddleLeft, CText, false);
                Step(c, P, F1(cam.Height) + " м", "vr.ui cam.height -0.5", "vr.ui cam.height 0.5", 284, 483);
                if (mode == 3)
                {
                    Tx(c, P, T(id, "CamOrbit"), 13, 28, 507, 150, 28, TextAnchor.MiddleLeft, CText, false);
                    Tx(c, P, cam.OrbitSpeed.ToString("0") + "°/с", 12, 200, 507, 76, 28, TextAnchor.MiddleRight, CDim, false);
                    IBtn(c, P, "minus", "vr.ui cam.orbit -5", 300, 509, 24, null, CDim, 6f, 14f);
                    IBtn(c, P, "plus", "vr.ui cam.orbit 5", 388, 509, 24, null, CDim, 6f, 14f);
                }
                PBtn(c, P, T(id, "CamTargetNext"), "user", "vr.ui cam.target 1", "soft", 17, bottomY, 406, 38, 13, 19f);
            }

            PBtn(c, P, T(id, "CamGo"), "video", "vr.ui cam.go", "primary", 17, 621, 306, 42, 14, 14f);
            Kbd(c, P, "F5", 331, 631);
            Tx(c, P, T(id, "OnOff"), 11, 373, 621, 50, 42, TextAnchor.MiddleLeft, CDim, false);
        }

        // ---------------- вкладка: эффекты
        private static readonly string[] FxIcons = { "boom", "bolt", "tank", "gas", "signal", "flame", "smoke", "flare" };

        private void BuildFx(CuiElementContainer c, BasePlayer player, Scene scene, UiState ui)
        {
            var id = Id(player);
            var owner = OwnerOf(id);
            const string P = UiMain;
            ui.FxType = Mathf.Clamp(ui.FxType, 0, FxTypes.Length - 1);

            Cap(c, P, T(id, "FxTypeCap"), 17, 178);
            for (int i = 0; i < FxTypes.Length && i < 8; i++)
            {
                float x = 17 + (i % 4) * 103, y = 201 + (i / 4) * 68;
                bool a = ui.FxType == i;
                BBox(c, P, a ? "0.639 0.086 0.184 0.22" : CSurf, a ? "0.941 0.651 0.694 0.45" : CLine, x, y, 97, 62, 12f);
                Ic(c, P, FxIcons[i], x + 40, y + 12, 18, a ? CWhite : CDim);
                Tx(c, P, T(id, FxTypes[i].Key), 11, x, y + 36, 97, 13, TextAnchor.MiddleCenter, a ? CWhite : CDim);
                Hit(c, P, "vr.ui fx.typeset " + i, x, y, 97, 62);
            }
            PBtn(c, P, T(id, "FxPlaceHere", Time3(CurrentTime(owner))), "crosshair", "vr.ui fx.add", "primary", 17, 341, 406, 42, 13, 14f);
            Tx(c, P, T(id, "FxHint"), 12, 17, 393, 406, 50, TextAnchor.UpperLeft, CDim, false);

            Cap(c, P, T(id, "FxOnTimeline", scene.Cues.Count), 17, 453);
            Tx(c, P, T(id, "PlayAllShort"), 12, 300, 452, 123, 14, TextAnchor.MiddleRight, CRose);
            Hit(c, P, "vr.ui playall", 330, 447, 93, 24);

            if (scene.Cues.Count == 0) Tx(c, P, T(id, "FxEmpty"), 12, 17, 477, 406, 120, TextAnchor.MiddleCenter, CDim, false);
            int first = Mathf.Clamp(ui.FxPage * 3, 0, Mathf.Max(0, scene.Cues.Count - 1));
            for (int r = 0; r < 3 && first + r < scene.Cues.Count; r++)
            {
                int i = first + r;
                var cue = scene.Cues[i];
                int k = Mathf.Clamp(cue.k, 0, FxTypes.Length - 1);
                float y = 477 + r * 52;
                BBox(c, P, CSurf, CLine, 17, y, 406, 46, 12f);
                Box(c, P, "0.831 0.122 0.235 0.18", 26, y + 8, 30, 30, 9f);
                Ic(c, P, FxIcons[k], 34, y + 15, 15, CRose);
                Tx(c, P, T(id, FxTypes[k].Key), 13, 64, y + 7, 158, 17);
                float dist = player.transform != null ? Vector3.Distance(player.transform.position, new Vector3(cue.x, cue.y, cue.z)) : 0f;
                Tx(c, P, T(id, "FxDist", dist.ToString("0")), 11, 64, y + 23, 158, 15, TextAnchor.MiddleLeft, CDim, false);
                Box(c, P, "0 0 0 0.25", 230, y + 9, 110, 28, 9f);
                IBtn(c, P, "minus", $"vr.ui fx.t {i} -0.1", 232, y + 11, 24, null, CDim, 6f, 16f);
                Tx(c, P, Time3(cue.t), 12, 256, y + 9, 58, 28, TextAnchor.MiddleCenter, CText);
                IBtn(c, P, "plus", $"vr.ui fx.t {i} 0.1", 314, y + 11, 24, null, CDim, 6f, 16f);
                IBtn(c, P, "play", $"vr.ui fx.test {i}", 347, y + 7, 32, null, CRose, 9f);
                IBtn(c, P, "x", $"vr.ui fx.del {i}", 385, y + 7, 32, null, CDim, 9f);
            }
            int pages = Mathf.Max(1, Mathf.CeilToInt(scene.Cues.Count / 3f));
            if (pages > 1)
            {
                Tx(c, P, $"{ui.FxPage + 1}/{pages}", 11, 180, 633, 80, 26, TextAnchor.MiddleCenter, CDim);
                IBtn(c, P, "left", "vr.ui fx.page -1", 150, 633, 26, CSurf, CDim, 8f, 14f);
                IBtn(c, P, "right", "vr.ui fx.page 1", 264, 633, 26, CSurf, CDim, 8f, 14f);
            }
        }

        // ---------------- вкладка: сцена
        private static readonly int[] QuickPresets = { 0, 3, 2, 4, 5 };
        private static readonly string[] QuickIcons = { "sun", "cloud", "fog", "rain", "bolt" };

        private void BuildScene(CuiElementContainer c, BasePlayer player, PlayerData data, Scene scene, UiState ui)
        {
            var id = Id(player);
            var owner = OwnerOf(id);
            const string P = UiMain;

            Inp(c, P, UiMain + ".sname", scene.Name, T(id, "SceneNameHint"), "vr.ui scenename", 17, 179, 360, 40, "clap");
            IBtn(c, P, "copy", "vr.ui dupscene", 385, 179, 38, CSurf, CText, 11f);

            // маркеры
            Card(c, P, T(id, "Markers"), 17, 227, 406, 153);
            Inp(c, P, UiMain + ".mname", ui.MarkerName, T(id, "MarkerHint"), "vr.ui marker.name", 26, 255, 291, 40, "tag");
            PBtn(c, P, "+ " + T(id, "AtTime", Time3(CurrentTime(owner))), null, "vr.ui marker.add", "primary", 323, 255, 91, 38, 12, 19f);
            int mfirst = Mathf.Max(0, scene.Markers.Count - 2);
            for (int r = 0; r < 2 && mfirst + r < scene.Markers.Count; r++)
            {
                int i = mfirst + r;
                var m = scene.Markers[i];
                float y = 301 + r * 38;
                Box(c, P, "0 0 0 0.22", 26, y, 388, 32, 10f);
                Ic(c, P, "flag", 36, y + 9, 13, "0.953 0.914 0.906 1");
                Tx(c, P, Time3(m.t), 12, 57, y, 56, 32);
                Tx(c, P, m.n, 13, 117, y, 176, 32, TextAnchor.MiddleLeft, CText, false);
                Box(c, P, CSurf, 301, y + 3, 73, 26, 13f);
                Tx(c, P, T(id, "Go"), 11, 301, y + 3, 73, 26, TextAnchor.MiddleCenter, CText);
                Hit(c, P, $"vr.ui marker.go {i}", 301, y + 3, 73, 26);
                IBtn(c, P, "x", $"vr.ui marker.del {i}", 380, y + 1, 30, null, CDim, 8f);
            }
            if (scene.Markers.Count == 0) Tx(c, P, T(id, "NoMarkers"), 12, 26, 301, 388, 70, TextAnchor.MiddleCenter, CDim, false);

            // вся сцена
            Card(c, P, T(id, "WholeSceneCap"), 17, 388, 406, 79);
            string[] icons = { "move", "rotate", "rotate", "mirror" };
            string[] keys = { "MoveHere", "Rotate90", "Rotate180", "Mirror" };
            string[] cmds = { "vr.ui s.movehere", "vr.ui s.rot 90", "vr.ui s.rot 180", "vr.ui s.mirror" };
            for (int i = 0; i < 4; i++)
            {
                float x = 26 + i * 98.5f;
                Box(c, P, "0 0 0 0.25", x, 418, 93, 40, 10f);
                string t = T(id, keys[i]);
                float w = 20 + TextW(t, 11);
                Ic(c, P, icons[i], x + (93 - w) / 2f, 431, 14, CText);
                Tx(c, P, t, 11, x + (93 - w) / 2f + 18, 418, w, 40, TextAnchor.MiddleLeft, CText);
                Hit(c, P, cmds[i], x, 418, 93, 40);
            }

            // атмосфера
            Card(c, P, T(id, "AtmoCap"), 17, 475, 406, 107);
            Ic(c, P, "sun", 26, 511, 15, CRose);
            float tod = scene.TimeOfDay;
            Slider(c, P, tod < 0f ? 0.5f : tod / 24f, "vr.ui todset", 51, 511, 239);
            Tx(c, P, tod < 0f ? T(id, "Server") : Hm(tod), 12, 296, 505, 48, 28, TextAnchor.MiddleRight, CText);
            Box(c, P, CSurf, 354, 505, 60, 28, 14f);
            Tx(c, P, T(id, "Reset"), 11, 354, 505, 60, 28, TextAnchor.MiddleCenter, CText);
            Hit(c, P, "vr.ui tod.reset", 354, 505, 60, 28);
            for (int i = 0; i < 5; i++)
            {
                float x = 26 + i * 78.5f;
                var pr = WeatherPresets[QuickPresets[i]];
                bool a = scene.Weather.Enabled && scene.Weather.Preset == pr.Key;
                Box(c, P, a ? CAcc : "0 0 0 0.25", x, 541, 74, 32, 9f);
                string t = T(id, "Q" + i);
                float w = 18 + TextW(t, 11);
                Ic(c, P, QuickIcons[i], x + (74 - w) / 2f, 550, 13, a ? CWhite : CDim);
                Tx(c, P, t, 11, x + (74 - w) / 2f + 17, 541, w, 32, TextAnchor.MiddleLeft, a ? CWhite : CDim);
                Hit(c, P, "vr.ui w.preset " + QuickPresets[i], x, 541, 74, 32);
            }

            // поделиться
            Card(c, P, T(id, "ShareCap"), 17, 590, 406, 74);
            BBox(c, P, "0 0 0 0.28", "0.941 0.651 0.694 0.4", 26, 616, 92, 40, 11f);
            Tx(c, P, string.IsNullOrEmpty(ui.LastCode) ? T(id, "GetCode") : ui.LastCode, string.IsNullOrEmpty(ui.LastCode) ? 12 : 16, 26, 616, 92, 40, TextAnchor.MiddleCenter, CRose);
            Hit(c, P, "vr.ui export", 26, 616, 92, 40);
            Inp(c, P, UiMain + ".icode", ui.ImportCode, T(id, "EnterCode"), "vr.ui import.code", 124, 616, 240, 40, "copy");
            PBtn(c, P, "OK", null, "vr.ui import", "ghost", 370, 616, 44, 40, 12, 11f);
        }

        // ---------------- вкладка: погода и время суток
        private static string Hm(float h)
        {
            int hh = Mathf.FloorToInt(Mathf.Repeat(h, 24f));
            int mm = Mathf.RoundToInt((Mathf.Repeat(h, 24f) - hh) * 60f) % 60;
            return $"{hh:00}:{mm:00}";
        }

        private static readonly string[] PresetIcons = { "sun", "sun", "fog", "cloud", "rain", "bolt", "smoke", "fog", "sun", "flare" };

        private void WeatherRow(CuiElementContainer c, ulong id, string icon, string key, string param, int value, float x, float y)
        {
            const string P = UiMain;
            Ic(c, P, icon, x, y + 6, 13, CDim);
            Tx(c, P, T(id, key), 12, x + 19, y, 62, 26, TextAnchor.MiddleLeft, CText, false);
            int max = param == "bright" || param == "contrast" ? 200 : 100;
            bool auto = value < 0;
            if (auto) Tx(c, P, T(id, "AutoServer"), 10, x + 80, y, 66, 26, TextAnchor.MiddleLeft, CDim, false);
            else
            {
                Slider(c, P, value / (float)max, "vr.ui w.setp " + param, x + 82, y + 5, 26);
                Tx(c, P, value + "%", 11, x + 108, y, 38, 26, TextAnchor.MiddleRight, CText);
            }
            Box(c, P, auto ? CAcc : CSurf, x + 150, y + 2, 36, 22, 11f);
            Tx(c, P, T(id, "AutoShort"), 9, x + 150, y + 2, 36, 22, TextAnchor.MiddleCenter, auto ? CWhite : CDim);
            Hit(c, P, auto ? $"vr.ui w.set {param} 50" : $"vr.ui w.set {param} auto", x + 150, y + 2, 36, 22);
            if (auto) Hit(c, P, $"vr.ui w.set {param} 50", x + 80, y, 66, 26);
        }

        private void BuildWeather(CuiElementContainer c, BasePlayer player)
        {
            var id = Id(player);
            var owner = OwnerOf(id);
            var scene = GetData(owner).Scene;
            var wx = scene.Weather;
            const string P = UiMain;
            bool world = CanWorld(player);

            Sw(c, P, wx.Enabled, "vr.ui w.enable", 17, 184);
            Tx(c, P, T(id, "WEnabledShort"), 13, 63, 180, 66, 30, TextAnchor.MiddleLeft, CText);
            PBtn(c, P, T(id, "WApply"), "check", "vr.ui w.apply", "primary", 133, 179, 117, 32, 12, 16f);
            PBtn(c, P, T(id, "WReset"), "undo", "vr.ui w.reset", "ghost", 257, 179, 166, 32, 12, 16f);

            for (int i = 0; i < WeatherPresets.Length && i < 10; i++)
            {
                var pr = WeatherPresets[i];
                float x = 17 + (i % 5) * 82.25f, y = 219 + (i / 5) * 59;
                bool a = wx.Enabled && wx.Preset == pr.Key;
                BBox(c, P, a ? "0.639 0.086 0.184 0.25" : CSurf, a ? "0.941 0.651 0.694 0.5" : CLine, x, y, 77, 54, 12f);
                Ic(c, P, PresetIcons[i], x + 31, y + 6, 15, a ? CWhite : CText);
                Tx(c, P, T(id, pr.Key), 10, x + 2, y + 21, 73, 22, TextAnchor.MiddleCenter, a ? CWhite : CText);
                Tx(c, P, Hm(pr.Time), 9, x, y + 42, 77, 10, TextAnchor.MiddleCenter, CDim, false);
                Hit(c, P, "vr.ui w.preset " + i, x, y, 77, 54);
            }

            Card(c, P, T(id, "TimeOfDay"), 17, 340, 406, 98);
            Ic(c, P, "sun", 28, 372, 15, CRose);
            Slider(c, P, scene.TimeOfDay < 0f ? 0.5f : scene.TimeOfDay / 24f, "vr.ui todset", 53, 372, 305);
            Tx(c, P, scene.TimeOfDay < 0f ? T(id, "Server") : Hm(scene.TimeOfDay), 12, 362, 367, 50, 26, TextAnchor.MiddleRight, CText);
            Seg(c, P, new[] { T(id, "TimeTeam"), T(id, "TimeAll") }, wx.ServerTime ? 1 : 0, "vr.ui w.servertimeset", 28, 395, 93, 28);
            Seg(c, P, new[] { T(id, "Frozen"), T(id, "Flowing") }, wx.FreezeTime ? 0 : 1, "vr.ui w.freezeset", 226, 395, 81, 28);

            string preset = wx.Preset != null ? T(id, wx.Preset) : T(id, "WCustom");
            Card(c, P, T(id, "FineTune", preset), 17, 446, 406, 157);
            WeatherRow(c, id, "cloud", "WClouds", "clouds", wx.Clouds, 28, 476);
            WeatherRow(c, id, "fog", "WFog", "fog", wx.Fog, 227, 476);
            WeatherRow(c, id, "rain", "WRain", "rain", wx.Rain, 28, 506);
            WeatherRow(c, id, "smoke", "WWind", "wind", wx.Wind, 227, 506);
            WeatherRow(c, id, "bolt", "WThunder", "thunder", wx.Thunder, 28, 536);
            WeatherRow(c, id, "flare", "WRainbow", "rainbow", wx.Rainbow, 227, 536);
            WeatherRow(c, id, "sun", "WBright", "bright", wx.Brightness, 28, 566);
            WeatherRow(c, id, "sliders", "WContrast", "contrast", wx.Contrast, 227, 566);

            Tx(c, P, world ? T(id, "WGlobalNote") : T(id, "WNoPerm"), 11, 17, 611, 406, 50, TextAnchor.UpperLeft, world ? CDim : CRose, false);
        }

        // ---------------- вкладка: наборы инвентаря
        private const int KitsPerPage = 3;

        private static void ItemIcon(CuiElementContainer c, string shortname, ulong skin, float x, float y, float s)
        {
            var def = ItemManager.FindItemDefinition(shortname);
            if (def == null) return;
            c.Add(new CuiElement
            {
                Parent = UiMain,
                Components =
                {
                    new CuiImageComponent { ItemId = def.itemid, SkinId = skin },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = Mn(x, y, s), OffsetMax = Mx(x, y, s) }
                }
            });
        }

        private void BuildKits(CuiElementContainer c, BasePlayer player, UiState ui)
        {
            var id = Id(player);
            var my = GetData(id);
            const string P = UiMain;

            if (!CanKits(player))
            {
                Ic(c, P, "bag", 204, 360, 32, CFaint);
                Tx(c, P, T(id, "NoKitPerm"), 13, 17, 400, 406, 40, TextAnchor.MiddleCenter, CDim, false);
                return;
            }

            Cap(c, P, T(id, "KitSaveCap"), 17, 179);
            Inp(c, P, UiMain + ".kname", ui.KitName, T(id, "KitNameHint", my.Kits.Count + 1), "vr.ui kit.name", 17, 198, 280, 40, "tag");
            PBtn(c, P, T(id, "KitSave"), "check", "vr.ui kit.save", "primary", 305, 198, 118, 38, 13, 19f);
            Tx(c, P, T(id, "KitHint"), 11, 17, 244, 406, 26, TextAnchor.UpperLeft, CDim, false);

            int pages = Mathf.Max(1, Mathf.CeilToInt(my.Kits.Count / (float)KitsPerPage));
            ui.KitPage = Mathf.Clamp(ui.KitPage, 0, pages - 1);
            Cap(c, P, T(id, "KitsHeader", my.Kits.Count, _config.MaxKits), 17, 281);
            if (pages > 1)
            {
                IBtn(c, P, "left", "vr.ui kit.page -1", 337, 275, 24, null, CDim, 6f, 14f);
                Tx(c, P, $"{ui.KitPage + 1}/{pages}", 11, 361, 275, 38, 24, TextAnchor.MiddleCenter, CDim);
                IBtn(c, P, "right", "vr.ui kit.page 1", 399, 275, 24, null, CDim, 6f, 14f);
            }

            if (my.Kits.Count == 0)
            {
                BBox(c, P, CSurf, CLine, 17, 304, 406, 100, 14f);
                Ic(c, P, "bag", 206, 322, 28, CFaint);
                Tx(c, P, T(id, "KitEmpty"), 12, 27, 356, 386, 40, TextAnchor.MiddleCenter, CDim, false);
            }

            int from = ui.KitPage * KitsPerPage, to = Mathf.Min(my.Kits.Count, from + KitsPerPage);
            for (int i = from; i < to; i++)
            {
                var kit = my.Kits[i];
                float y = 304 + (i - from) * 108;
                bool first = i == from;
                BBox(c, P, first ? CHi : CSurf, first ? CHiLine : CLine, 17, y, 406, 100, 14f);
                Ic(c, P, "bag", 28, y + 19, 15, CRose);
                Tx(c, P, kit.Name, 14, 51, y + 12, 221, 16);
                int wear = kit.Items.Count(it => it.c == 2);
                Tx(c, P, T(id, "KitMeta", kit.Items.Count, wear), 11, 51, y + 28, 221, 13, TextAnchor.MiddleLeft, CDim, false);
                PBtn(c, P, T(id, "KitLoad"), "shirt", $"vr.ui kit.load {i}", "primary", 280, y + 11, 94, 30, 12, 15f);
                bool confirm = ui.ConfirmKit == i && Time.realtimeSinceStartup - ui.ConfirmKitAt < 15f;
                IBtn(c, P, confirm ? "x" : "more", $"vr.ui kit.more {i}", 381, y + 11, 30, null, CDim, 8f, 16f);

                if (confirm)
                {
                    // меню набора: перезаписать / удалить
                    PBtn(c, P, T(id, "KitOver"), "rerec", $"vr.ui kit.over {i}", "ghost", 28, y + 53, 186, 36, 12, 18f);
                    PBtn(c, P, T(id, "KitDelete"), "x", $"vr.ui kit.del {i}", "rec", 222, y + 53, 190, 36, 12, 18f);
                    continue;
                }

                // превью: пояс, затем одежда, затем рюкзак
                var items = kit.Items.OrderBy(it => it.c == 1 ? 0 : it.c == 2 ? 1 : 2).ThenBy(it => it.p).ToList();
                int shown = Mathf.Min(items.Count, items.Count > 6 ? 5 : 6);
                for (int j = 0; j < shown; j++)
                {
                    var ki = items[j];
                    float sx = 28 + j * 46;
                    BBox(c, P, "0 0 0 0.3", CLine, sx, y + 49, 40, 40, 9f);
                    ItemIcon(c, ki.s, ki.k, sx + 4, y + 53, 32);
                    if (ki.c == 2) Box(c, P, CRose, sx + 4, y + 53, 6, 6, 3f);
                    if (ki.a > 1) Tx(c, P, ki.a.ToString(), 9, sx + 2, y + 77, 36, 10, TextAnchor.MiddleRight, CText);
                }
                if (items.Count > shown)
                {
                    BBox(c, P, "0 0 0 0", CLine, 28 + shown * 46, y + 49, 40, 40, 9f);
                    Tx(c, P, "+" + (items.Count - shown), 11, 28 + shown * 46, y + 49, 40, 40, TextAnchor.MiddleCenter, CDim, false);
                }
                PBtn(c, P, T(id, "KitAdd"), "plus", $"vr.ui kit.add {i}", "ghost", 312, y + 55, 100, 28, 11, 14f);
            }
        }

        // ---------------- вкладка: команда и «Мотор для всех»
        private void BuildTeam(CuiElementContainer c, BasePlayer player, UiState ui)
        {
            var id = Id(player);
            var owner = OwnerOf(id);
            var scene = GetData(owner).Scene;
            var team = Team(owner);
            const string P = UiMain;
            int actors = GroupParticipants(owner).Count;
            bool counting = _groupCountdowns.ContainsKey(owner);
            var groupRec = _recorders.Values.FirstOrDefault(r => r.Owner == owner && r.Group != null);

            // мотор
            BBox(c, P, "0.831 0.122 0.235 0.14", "0.941 0.651 0.694 0.25", 17, 179, 406, 98, 16f);
            Tx(c, P, T(id, "MotorTitle"), 16, 32, 194, 203, 19);
            Tx(c, P, T(id, "MotorHint", _config.GroupCountdown), 11, 32, 217, 203, 45, TextAnchor.UpperLeft, CDim, false);
            string mlabel = groupRec != null ? T(id, "MotorStop") : counting ? T(id, "MotorCancel") : T(id, "MotorGo", actors);
            Box(c, P, CRec, 247, 204, 161, 48, 24f);
            float mw = 20 + TextW(mlabel, 15);
            float mx = 247 + (161 - mw) / 2f;
            Box(c, P, CWhite, mx, 222, 12, 12, groupRec != null ? 2f : 6f);
            Tx(c, P, mlabel, 15, mx + 20, 204, mw, 48, TextAnchor.MiddleLeft, CWhite);
            Hit(c, P, "vr.motor", 247, 204, 161, 48);

            // участники
            Cap(c, P, T(id, "TeamMembers", team.Count), 17, 292);
            if (owner != id) { Tx(c, P, T(id, "LeaveScene"), 12, 230, 287, 145, 23, TextAnchor.MiddleRight, CRose); Hit(c, P, "vr.team leave", 280, 287, 95, 23); }
            Kbd(c, P, "F11", 385, 287);
            for (int i = 0; i < team.Count && i < 3; i++)
            {
                var m = team[i];
                var p = BasePlayer.FindByID(m.Id);
                bool me = m.Id == id, isOwner = m.Id == owner;
                bool online = p != null && p.IsConnected;
                float y = 320 + i * 56;
                BBox(c, P, me ? "0.639 0.086 0.184 0.14" : CSurf, me ? "0.941 0.651 0.694 0.2" : CLine, 17, y, 406, 50, 14f);
                Box(c, P, "0 0 0 0.3", 28, y + 9, 32, 32, 16f);
                Ic(c, P, "user", 36, y + 17, 16, CText);
                Box(c, P, "0.102 0.086 0.086 1", 46, y + 27, 16, 16, 8f);
                Box(c, P, online ? CGreen : CFaint, 47, y + 28, 14, 14, 7f);

                string nm = Clip(p?.displayName ?? m.Id.ToString(), 18) + (isOwner ? T(id, "TagOwner") : "") + (me ? T(id, "TagMe") : "");
                Tx(c, P, nm, 14, 70, y + 8, me ? 130 : 220, 18, TextAnchor.MiddleLeft, CText);
                string st; string sc;
                if (_recorders.ContainsKey(m.Id)) { st = "● " + T(id, "StRec"); sc = CRec; }
                else if (!m.Actor) { st = T(id, "StCamLong"); sc = CDim; }
                else if (m.Ready) { st = T(id, "StReady"); sc = CGreen; }
                else { st = T(id, "StNotReady"); sc = CDim; }
                Tx(c, P, st, 11, 70, y + 26, 214, 15, TextAnchor.MiddleLeft, sc);

                bool canRole = me || owner == id;
                float rx = me ? 202 : 294;
                Box(c, P, m.Actor ? CAcc : CSurf, rx, y + 11, 82, 28, 14f);
                Tx(c, P, m.Actor ? T(id, "RoleActor") : T(id, "RoleCam"), 11, rx, y + 11, 82, 28, TextAnchor.MiddleCenter, CWhite);
                if (canRole) Hit(c, P, "vr.team actor " + m.Id, rx, y + 11, 82, 28);
                if (me)
                {
                    if (m.Actor)
                    {
                        Box(c, P, m.Ready ? "0.498 0.820 0.545 0.2" : CSurf, 294, y + 11, 82, 28, 14f);
                        string rt = m.Ready ? T(id, "ReadyOn") : T(id, "ReadyOff");
                        float rw = (m.Ready ? 16 : 0) + TextW(rt, 11);
                        if (m.Ready) Ic(c, P, "check", 294 + (82 - rw) / 2f, y + 19, 12, "0.651 0.890 0.690 1");
                        Tx(c, P, rt, 11, 294 + (82 - rw) / 2f + (m.Ready ? 16 : 0), y + 11, rw + 4, 28, TextAnchor.MiddleLeft, m.Ready ? "0.651 0.890 0.690 1" : CText);
                        Hit(c, P, "vr.team ready", 294, y + 11, 82, 28);
                    }
                }
                else if (owner == id)
                    IBtn(c, P, "x", "vr.team kick " + m.Id, 384, y + 9, 32, null, CDim, 9f);
            }
            if (team.Count > 3) Tx(c, P, T(id, "MoreMembers", team.Count - 3), 11, 17, 485, 406, 8, TextAnchor.MiddleRight, CDim, false);

            // пригласить
            if (owner == id)
            {
                Cap(c, P, T(id, "InviteTitle"), 17, 496);
                Tx(c, P, T(id, "InviteSub"), 11, 223, 496, 200, 13, TextAnchor.MiddleRight, CDim, false);
                var candidates = BasePlayer.activePlayerList
                    .Where(p => p != null && p != player && !_botInfo.ContainsKey(p) && OwnerOf(Id(p)) != id && HasAccess(p, true))
                    .OrderBy(p => p.displayName).Take(8).ToList();
                if (candidates.Count == 0)
                    Tx(c, P, T(id, "NoCandidates"), 12, 17, 519, 406, 32, TextAnchor.MiddleLeft, CDim, false);
                float px = 17, py = 519;
                foreach (var p in candidates)
                {
                    Invite inv;
                    bool pending = _invites.TryGetValue(Id(p), out inv) && inv.Owner == id;
                    string nm = Clip(p.displayName, 16);
                    string label = pending ? T(id, "InvitePending", nm) : nm;
                    float w = 13 + 13 + 8 + TextW(label, 12) + 14;
                    if (px + w > 423) { px = 17; py += 38; }
                    if (py > 557) break;
                    if (pending) Box(c, P, "0 0 0 0.25", px, py, w, 32, 16f);
                    else BBox(c, P, "0.071 0.055 0.055 1", "1 1 1 0.2", px, py, w, 32, 16f);
                    Ic(c, P, pending ? "clock" : "plus", px + 12, py + 9, 13, pending ? CDim : CText);
                    Tx(c, P, label, 12, px + 33, py, w - 35, 32, TextAnchor.MiddleLeft, pending ? CDim : CText);
                    if (!pending) Hit(c, P, "vr.team invite " + Id(p), px, py, w, 32);
                    px += w + 6;
                }
                Tx(c, P, T(id, "InviteFoot"), 11, 17, 632, 406, 31, TextAnchor.UpperLeft, CDim, false);
            }
            else
            {
                Cap(c, P, T(id, "GuestCap"), 17, 496);
                Tx(c, P, T(id, "GuestInfo", BasePlayer.FindByID(owner)?.displayName ?? owner.ToString(), scene.Name), 13, 17, 519, 406, 60, TextAnchor.UpperLeft, CText, false);
            }
        }

        // ---------------- вкладка: опции
        private void OptRow(CuiElementContainer c, ulong id, string key, bool on, string cmd, float y)
        {
            Tx(c, UiMain, T(id, key), 13, 28, y, 334, 16, TextAnchor.MiddleLeft, CText);
            Tx(c, UiMain, T(id, key + "Sub"), 11, 28, y + 15, 334, 13, TextAnchor.MiddleLeft, CDim, false);
            Box(c, UiMain, on ? CAcc : CTrack, 372, y + 3, 40, 24, 12f);
            Box(c, UiMain, CWhite, on ? 391 : 375, y + 6, 18, 18, 9f);
            Hit(c, UiMain, cmd, 17, y - 2, 406, 32);
        }

        private void BuildOptions(CuiElementContainer c, BasePlayer player, PlayerData data, UiState ui)
        {
            var id = Id(player);
            var my = GetData(id);
            const string P = UiMain;

            Card(c, P, T(id, "OptRecCap"), 17, 179, 406, 188);
            OptRow(c, id, "OptClean", my.CleanFrame, "vr.ui opt.clean", 209);
            OptRow(c, id, "OptGod", data.GodWhileRecording, "vr.ui opt.god", 239);
            OptRow(c, id, "OptAmmo", data.InfiniteAmmo, "vr.ui opt.ammo", 269);
            OptRow(c, id, "OptVoice", my.RecordVoice && _config.RecordVoice, "vr.ui opt.voice", 299);
            OptRow(c, id, "OptTags", my.Nametags, "vr.ui opt.tags", 329);

            Card(c, P, T(id, "OptPlayCap"), 17, 375, 406, 130);
            Tx(c, P, T(id, "StartFrom"), 13, 28, 398, 200, 32, TextAnchor.MiddleLeft, CText, false);
            Step(c, P, Time2(data.Seek), "vr.ui seek -1", "vr.ui seek 1", 284, 398);
            Tx(c, P, T(id, "SceneSpeed"), 13, 28, 434, 200, 32, TextAnchor.MiddleLeft, CText, false);
            Step(c, P, data.GlobalSpeed.ToString("0.00", CultureInfo.InvariantCulture) + "×", "vr.ui gspeed -0.25", "vr.ui gspeed 0.25", 284, 434);
            Tx(c, P, T(id, "SlowmoSpeed"), 13, 28, 470, 200, 32, TextAnchor.MiddleLeft, CText, false);
            Step(c, P, data.SlowmoSpeed.ToString("0.00", CultureInfo.InvariantCulture) + "×", "vr.ui opt.slowmo -0.05", "vr.ui opt.slowmo 0.05", 284, 470);

            Card(c, P, T(id, "HotkeysCap"), 17, 513, 406, 145);
            string[] keys = { "F5", "F6", "F7", "F8", "F9", "F10" };
            string[] names = { "HkCam", "HkPanel", "HkScene", "HkRec", "HkPause", "HkSlowmo" };
            for (int i = 0; i < 6; i++)
            {
                float x = 28 + (i % 3) * 130, y = 545 + (i / 3) * 36;
                Box(c, P, "0 0 0 0.22", x, y, 124, 30, 9f);
                Kbd(c, P, keys[i], x + 8, y + 4);
                Tx(c, P, T(id, names[i]), 12, x + 54, y, 68, 30, TextAnchor.MiddleLeft, CText, false);
            }
            Tx(c, P, T(id, "BindsHint"), 11, 28, 619, 216, 28, TextAnchor.MiddleLeft, CDim, false);
            PBtn(c, P, T(id, "CopyBinds"), "copy", "vr.ui binds", "ghost", 252, 619, 160, 28, 11, 14f);
        }

        // ---------------- окно приглашения
        private void ShowInvite(BasePlayer player)
        {
            var id = Id(player);
            Invite inv;
            if (!_invites.TryGetValue(id, out inv)) return;
            var host = BasePlayer.FindByID(inv.Owner);
            if (host == null) return;
            var scene = GetData(inv.Owner).Scene;
            var members = Team(inv.Owner).Select(m => BasePlayer.FindByID(m.Id)?.displayName).Where(n => n != null).ToList();
            const string R = UiInvite;
            const string M = UiInvite + ".m";

            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0.031 0.024 0.024 0.45" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", R);
            Glass(c, M, R, "0.5 0.5", "0.5 0.5", "-240 -205", "240 205", 24f, false);

            Box(c, M, CRec, 21, 21, 44, 44, 22f);
            Ic(c, M, "clap", 33, 33, 20, CText);
            Tx(c, M, T(id, "InvTitle"), 18, 77, 25, 333, 21);
            Tx(c, M, T(id, "InvFrom", host.displayName), 12, 77, 48, 333, 14, TextAnchor.MiddleLeft, CDim, false);
            float left = Mathf.Max(0f, _config.InviteSeconds - (Time.realtimeSinceStartup - inv.At));
            Ic(c, M, "clock", 404, 37, 12, CDim);
            Tx(c, M, Time2(left).Substring(1), 11, 420, 37, 40, 13, TextAnchor.MiddleLeft, CDim, false);

            // карточка сцены
            BBox(c, M, "0.25 0.06 0.1 0.55", "1 1 1 0.1", 21, 79, 438, 122, 16f);
            Box(c, M, "0 0 0 0.35", 22, 140, 436, 60, 15f);
            Ic(c, M, "clap", 222, 96, 34, "1 1 1 0.25");
            Tx(c, M, $"«{scene.Name}»", 17, 36, 151, 408, 20, TextAnchor.MiddleLeft, CWhite);
            Tx(c, M, T(id, "InvMeta", scene.Takes.Count, Time2(scene.Length), string.Join(", ", members.Take(4).ToArray())), 11, 36, 175, 408, 13, TextAnchor.MiddleLeft, "1 1 1 0.85", false);

            Cap(c, M, T(id, "InvRole"), 21, 215);
            for (int i = 0; i < 2; i++)
            {
                bool actor = i == 0, sel = inv.Actor == actor;
                float x = 21 + i * 223;
                BBox(c, M, sel ? "0.639 0.086 0.184 0.24" : CSurf, sel ? "0.941 0.651 0.694 0.5" : CLine, x, 236, 215, 66, 14f);
                Ic(c, M, actor ? "user" : "video", x + 13, 260, 18, sel ? CWhite : CText);
                Tx(c, M, actor ? T(id, "InvActor") : T(id, "InvCam"), 14, x + 41, 247, 161, 16, TextAnchor.MiddleLeft, sel ? CWhite : CText);
                Tx(c, M, actor ? T(id, "InvActorHint") : T(id, "InvCamHint"), 11, x + 41, 265, 166, 26, TextAnchor.UpperLeft, sel ? CRose : CDim, false);
                Hit(c, M, actor ? "vr.team role actor" : "vr.team role cam", x, 236, 215, 66);
            }

            Box(c, M, CRec, 21, 316, 201, 46, 23f);
            string acc = T(id, "InvAccept");
            float aw = 24 + TextW(acc, 15);
            Ic(c, M, "check", 21 + (201 - aw) / 2f, 331, 16, CWhite);
            Tx(c, M, acc, 15, 21 + (201 - aw) / 2f + 24, 316, aw, 46, TextAnchor.MiddleLeft, CWhite);
            Hit(c, M, "vr.team accept", 21, 316, 201, 46);
            Box(c, M, CSurf, 230, 316, 229, 46, 14f);
            Tx(c, M, T(id, "InvDecline"), 15, 230, 316, 229, 46, TextAnchor.MiddleCenter, CText);
            Hit(c, M, "vr.team decline", 230, 316, 229, 46);
            Tx(c, M, T(id, "InvFoot"), 11, 21, 376, 438, 13, TextAnchor.MiddleLeft, CDim, false);

            CuiHelper.DestroyUi(player, UiInvite);
            CuiHelper.AddUi(player, c);
        }

        // ---------------- отсчёт перед записью (одиночный и «Мотор для всех»)
        private void ShowCountdown(BasePlayer player, int n, string sub = null)
        {
            var id = Id(player);
            var owner = OwnerOf(id);
            var scene = GetData(owner).Scene;
            bool motor = sub != null;
            const string R = UiCountdown;
            var c = new CuiElementContainer();
            c.Add(new CuiElement { Name = R, Parent = "Overlay", Components = { new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-640 -360", OffsetMax = "640 360" } } });
            Box(c, R, CRec, 0, 0, 1280, 4);

            float d = motor ? 178 : 170, cx = 640 - d / 2f, cy = motor ? 229 : 227;
            if (motor) Tx(c, R, T(id, "MotorAll").ToUpper(), 13, 440, 198, 400, 15, TextAnchor.MiddleCenter, CRose);
            Box(c, R, CRec, cx, cy, d, d, d / 2f);
            Box(c, R, "0.071 0.055 0.055 0.94", cx + 5, cy + 5, d - 10, d - 10, d / 2f - 5);
            Tx(c, R, n.ToString(), motor ? 84 : 76, cx, cy + (motor ? 40 : 30), d, motor ? 92 : 84, TextAnchor.MiddleCenter, CWhite);

            if (!motor)
            {
                Tx(c, R, T(id, "Recording").ToUpper(), 11, cx, cy + 116, d, 13, TextAnchor.MiddleCenter, CRose);
                int playing = scene.Takes.Count(t => t.Enabled);
                string a = T(id, "TakeN", scene.Takes.Count + 1) + " · " + Clip(player.displayName, 14);
                string b = playing > 0 ? T(id, "PlayAlong", playing) : T(id, "FirstTake");
                float w = 47 + TextW(a, 14) + 10 + TextW(b, 13) + 17;
                float x = 640 - w / 2f;
                Glass(c, R + ".pill", R, "0 1", "0 1", Mn(x, 415, 42), Mx(x, 415, w), 20f, false);
                Box(c, R + ".pill", "1 0.353 0.431 1", 17, 16, 10, 10, 5f);
                Tx(c, R + ".pill", a, 14, 37, 0, TextW(a, 14) + 6, 42);
                Tx(c, R + ".pill", b, 13, 47 + TextW(a, 14) + 6, 0, TextW(b, 13) + 12, 42, TextAnchor.MiddleLeft, CDim, false);
            }
            else
            {
                Tx(c, R, sub, 18, 340, 423, 600, 21, TextAnchor.MiddleCenter, CText);
                var actors = GroupParticipants(owner);
                var cams = Team(owner).Where(m => !m.Actor).Select(m => BasePlayer.FindByID(m.Id)?.displayName).Where(s => s != null).ToList();
                float total = TextW(T(id, "Writing"), 12) + 6;
                foreach (var p in actors.Take(4)) total += 30 + TextW(Clip(p.displayName, 12) + (p == player ? T(id, "TagYou") : ""), 12) + 6;
                string camText = cams.Count > 0 ? "· " + T(id, "Filming", string.Join(", ", cams.Take(2).ToArray())) : "";
                total += TextW(camText, 12);
                float x = 640 - total / 2f;
                Tx(c, R, T(id, "Writing"), 12, x, 460, TextW(T(id, "Writing"), 12) + 6, 28, TextAnchor.MiddleLeft, CDim, false);
                x += TextW(T(id, "Writing"), 12) + 6;
                foreach (var p in actors.Take(4))
                {
                    bool me = p == player;
                    string nm = Clip(p.displayName, 12) + (me ? T(id, "TagYou") : "");
                    float w = 30 + TextW(nm, 12);
                    Box(c, R, me ? "0.831 0.122 0.235 0.3" : "1 1 1 0.08", x, 460, w, 28, 14f);
                    Box(c, R, me ? "0.949 0.627 0.678 1" : "1 0.353 0.431 1", x + 12, 470, 8, 8, 4f);
                    Tx(c, R, nm, 12, x + 26, 460, w - 26, 28, TextAnchor.MiddleLeft, CText);
                    x += w + 6;
                }
                if (camText != "") Tx(c, R, camText, 12, x, 460, TextW(camText, 12) + 10, 28, TextAnchor.MiddleLeft, CDim, false);

                // нижняя полоса
                string k1 = T(id, "MotorCancelKey"), k2 = T(id, "SameThing"), stop = T(id, "StopAll");
                float bw = 17 + 46 + TextW(k1, 12) + 14 + 46 + TextW(k2, 12) + 14 + 36 + TextW(stop, 12) + 17;
                float bx = 640 - bw / 2f;
                Glass(c, R + ".keys", R, "0 1", "0 1", Mn(bx, 654, 42), Mx(bx, 654, bw), 20f, false);
                KeyBar(c, R + ".keys", new[] { "F11", "F8" }, new[] { k1, k2 }, stop, "vr.motor");
            }

            CuiHelper.DestroyUi(player, UiCountdown);
            CuiHelper.AddUi(player, c);
        }

        // полоса подсказок клавиш с красной кнопкой «Стоп»
        private static void KeyBar(CuiElementContainer c, string P, string[] keys, string[] texts, string stop, string stopCmd)
        {
            float x = 17;
            for (int i = 0; i < keys.Length; i++)
            {
                Kbd(c, P, keys[i], x, 10);
                Tx(c, P, texts[i], 12, x + 46, 10, TextW(texts[i], 12) + 8, 23, TextAnchor.MiddleLeft, CDim, false);
                x += 46 + TextW(texts[i], 12) + 14;
            }
            float w = 36 + TextW(stop, 12);
            Box(c, P, CRec, x, 7, w, 28, 14f);
            Box(c, P, CWhite, x + 13, 15, 11, 11, 2f);
            Tx(c, P, stop, 12, x + 30, 7, w - 30, 28, TextAnchor.MiddleLeft, CWhite);
            Hit(c, P, stopCmd, x, 7, w, 28);
        }

        // ---------------- HUD записи: красная полоса, REC-таймер, подсказки
        private void ShowRecHud(BasePlayer player, int seconds)
        {
            var id = Id(player);
            Recorder rec;
            _recorders.TryGetValue(id, out rec);
            const string R = UiHud;
            var c = new CuiElementContainer();
            c.Add(new CuiElement { Name = R, Parent = "Hud", Components = { new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } } });
            c.Add(new CuiPanel { Image = { Color = CRec }, RectTransform = { AnchorMin = "0 1", AnchorMax = "1 1", OffsetMin = "0 -4", OffsetMax = "0 0" } }, R);

            Glass(c, R + ".rec", R, "1 1", "1 1", "-153 -62", "-20 -20", 20f, false);
            bool blink = seconds % 2 == 0;
            Box(c, R + ".rec", blink ? CRec : "0.831 0.122 0.235 0.35", 17, 16, 10, 10, 5f);
            Tx(c, R + ".rec", "REC", 15, 37, 12, 34, 18);
            Tx(c, R + ".rec", Time2(seconds), 15, 78, 12, 48, 18, TextAnchor.MiddleLeft, CDim);

            if (rec != null && rec.Group == null && seconds < 4)
            {
                // первые секунды — подсказка, кто играет вместе
                var scene = GetData(rec.Owner).Scene;
                int playing = scene.Takes.Count(t => t.Enabled);
                string a = T(id, "TakeN", rec.ReplaceIndex >= 0 ? rec.ReplaceIndex + 1 : scene.Takes.Count + 1) + " · " + Clip(player.displayName, 14);
                string b = playing > 0 ? T(id, "PlayAlong", playing) : T(id, "FirstTake");
                float w = 47 + TextW(a, 14) + 10 + TextW(b, 13) + 17;
                Glass(c, R + ".pill", R, "0.5 0.5", "0.5 0.5", $"{-w / 2f} {-76}", $"{w / 2f} {-34}", 20f, false);
                Box(c, R + ".pill", "1 0.353 0.431 1", 17, 16, 10, 10, 5f);
                Tx(c, R + ".pill", a, 14, 37, 0, TextW(a, 14) + 6, 42);
                Tx(c, R + ".pill", b, 13, 47 + TextW(a, 14) + 6, 0, TextW(b, 13) + 12, 42, TextAnchor.MiddleLeft, CDim, false);
            }

            string k1 = T(id, "KeyStopSave"), k2 = T(id, "KeyPause"), k3 = T(id, "KeyPanel"), stop = rec?.Group != null ? T(id, "StopAll") : T(id, "Stop");
            float bw = 17 + 46 + TextW(k1, 12) + 14 + 46 + TextW(k2, 12) + 14 + 46 + TextW(k3, 12) + 14 + 36 + TextW(stop, 12) + 17;
            Glass(c, R + ".keys", R, "0.5 0", "0.5 0", $"{-bw / 2f} 24", $"{bw / 2f} 66", 20f, false);
            KeyBar(c, R + ".keys", new[] { "F8", "F9", "F6" }, new[] { k1, k2, k3 }, stop, rec?.Group != null ? "vr.motor" : "vr.rec");

            CuiHelper.DestroyUi(player, UiHud);
            CuiHelper.AddUi(player, c);
        }

        // ---------------- HUD воспроизведения: карточка справа сверху, бейдж, док с таймлайном
        private void ShowPlayHud(BasePlayer player, Playback pb, bool full)
        {
            var id = Id(player);
            var owner = OwnerOf(id);
            var data = GetData(owner);
            var scene = data.Scene;
            float len = TimelineLength(scene, owner);
            float t = pb.Time;

            if (full)
            {
                var b = new CuiElementContainer();
                b.Add(new CuiElement { Name = UiPbHud, Parent = "Hud", Components = { new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } } });
                Glass(b, UiPbHud + ".badge", UiPbHud, "0 1", "0 1", "20 -62", "212 -20", 20f, false);
                Box(b, UiPbHud + ".badge", CAcc, 8, 8, 26, 26, 13f);
                Ic(b, UiPbHud + ".badge", "video", 14, 14, 14, CWhite);
                Tx(b, UiPbHud + ".badge", "VideoRecorder", 13, 42, 0, 100, 42);
                Kbd(b, UiPbHud + ".badge", "F6", 143, 10, 38, 22);
                BuildDock(b, player, GetData(id), scene, pb, "0.5 0", "0.5 0", "-340 20", "340 193", false, 680f);
                CuiHelper.DestroyUi(player, UiPbHud);
                CuiHelper.AddUi(player, b);
            }

            var c = new CuiElementContainer();
            const string R = UiHud;
            Glass(c, R, "Hud", "1 1", "1 1", "-260 -107", "-20 -20", 18f, false);
            string state = pb.Paused ? T(id, "Paused") : pb.Slowmo ? T(id, "SlowmoOn") : T(id, "SceneRunning");
            Box(c, R, pb.Paused ? CDim : CRose, 15, 17, 8, 8, 4f);
            Tx(c, R, state.ToUpper(), 12, 31, 13, 120, 16, TextAnchor.MiddleLeft, CRose);
            float end = pb.EndTime > 0f ? pb.EndTime : len;
            Tx(c, R, Time2(t) + " / " + Time2(end), 13, 120, 12, 105, 17, TextAnchor.MiddleRight, CText);
            Box(c, R, "1 1 1 0.1", 15, 36, 210, 4, 2f);
            float pr = end > 0f ? Mathf.Clamp01(t / end) : 0f;
            if (pr > 0.01f) Box(c, R, CAcc, 15, 36, Mathf.Max(4f, 210f * pr), 4, 2f);
            Tx(c, R, T(id, "PbMeta", Clip(scene.Name, 22), scene.Takes.Count(x => x.Enabled)), 11, 15, 48, 210, 26, TextAnchor.UpperLeft, CDim, false);

            // бегунок на доке
            c.Add(new CuiElement { Name = UiPbHud + ".ph", Parent = UiPbHud + ".dock", Components = { new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" } } });
            PlayheadInto(c, UiPbHud + ".ph", id, t, len, pb, 680f);

            CuiHelper.DestroyUi(player, UiPbHud + ".ph");
            CuiHelper.DestroyUi(player, UiHud);
            CuiHelper.AddUi(player, c);
        }

        private void HidePlayHud(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UiHud);
            CuiHelper.DestroyUi(player, UiPbHud);
        }

        private void TickHud()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                var id = Id(player);
                if (_recorders.ContainsKey(id)) continue;
                if (!HasAccess(player, true)) continue;
                var owner = OwnerOf(id);
                var pb = GetPlayback(owner);
                var my = GetData(id);

                if (_uiOpen.Contains(id)) UpdatePlayhead(player);
                if (my.Nametags && pb != null && CanDraw(player)) DrawNametags(player, owner);

                if (pb == null || my.CleanFrame || _uiOpen.Contains(id) || _cameras.ContainsKey(id) && my.CleanFrame)
                {
                    if (_hudShown.Remove(id)) HidePlayHud(player);
                    continue;
                }
                bool full = _hudShown.Add(id);
                ShowPlayHud(player, pb, full);
            }
        }

        #endregion

        #region Локализация

        private string T(ulong id, string key, params object[] args)
        {
            var msg = lang.GetMessage(key, this, id.ToString());
            if (args == null || args.Length == 0) return msg;
            try { return string.Format(msg, args); }
            catch { return msg; }
        }

        private void Reply(BasePlayer player, string key, params object[] args)
        {
            if (player == null || !player.IsConnected) return;
            SendReply(player, "<color=#e0384e>[VR]</color> " + T(Id(player), key, args));
        }

        private void RegisterLang()
        {
            var ru = new Dictionary<string, string>();
            var en = new Dictionary<string, string>();
            foreach (var row in Messages)
            {
                ru[row[0]] = row[1];
                en[row[0]] = row[2];
            }
            // 3.5: тексты интерфейса переписаны — старые файлы lang/ru|en/VideoRecorder.json
            // не обновились бы сами (Oxide только дописывает новые ключи), поэтому один раз сбрасываем их
            string have;
            var cur = lang.GetMessages("ru", this);
            if (cur == null || !cur.TryGetValue("LangVer", out have) || have != ru["LangVer"])
            {
                var wipe = new Dictionary<string, string> { { "_", "_" } };
                lang.RegisterMessages(wipe, this, "en");
                lang.RegisterMessages(wipe, this, "ru");
            }
            lang.RegisterMessages(en, this, "en");
            lang.RegisterMessages(ru, this, "ru");
        }

        private static readonly string[][] Messages =
        {

            // --- интерфейс 3.5
            new[] { "AtTime", "в {0}", "at {0}" },
            new[] { "AtmoCap", "Атмосфера · время видишь только ты", "Atmosphere · only you see the time" },
            new[] { "AutoName", "авто: Дубль {0}", "auto: Take {0}" },
            new[] { "AutoServer", "авто (сервер)", "auto (server)" },
            new[] { "AutoShort", "АВТО", "AUTO" },
            new[] { "Behaviour", "Поведение", "Behaviour" },
            new[] { "BindsCopied", "Бинды выведены в консоль F1 — скопируй строку оттуда.", "Binds printed to the F1 console — copy the line from there." },
            new[] { "CamAddHere", "Добавить точку отсюда · {0}", "Add point from here · {0}" },
            new[] { "CamFollowCap", "Цель камеры", "Camera target" },
            new[] { "CamKeysCap", "Точки пути · {0}", "Path points · {0}" },
            new[] { "Ease0", "авто", "auto" }, new[] { "Ease1", "линейно", "linear" }, new[] { "Ease2", "разгон", "ease in" },
            new[] { "Ease3", "торможение", "ease out" }, new[] { "Ease4", "разгон+торм.", "in-out" },
            new[] { "CamFocus", "Фокус: {0}", "Focus: {0}" }, new[] { "FocusNone", "нет", "none" },
            new[] { "CamKeysEmpty", "Точек пока нет. Встань, куда смотрит камера, и добавь первую.", "No points yet. Stand where the camera should look and add one." },
            new[] { "CamTargetNext", "Следующий дубль", "Next take" },
            new[] { "CopyBinds", "Скопировать бинды", "Copy binds" },
            new[] { "DeleteScene", "Удалить сцену", "Delete scene" },
            new[] { "DisableAll", "Выключить все", "Disable all" },
            new[] { "EnableAll", "Включить все", "Enable all" },
            new[] { "EnterCode", "Ввести код друга", "Enter a friend's code" },
            new[] { "Filming", "снимает: {0}", "filming: {0}" },
            new[] { "FineTune", "Тонкая настройка · пресет «{0}»", "Fine-tune · preset \"{0}\"" },
            new[] { "FirstTake", "первый дубль сцены", "first take of the scene" },
            new[] { "FxDist", "в {0} м от тебя", "{0} m from you" },
            new[] { "FxOnTimeline", "На таймлайне · {0}", "On timeline · {0}" },
            new[] { "FxPlaceHere", "Поставить туда, куда смотришь · {0}", "Place where you look · {0}" },
            new[] { "FxTypeCap", "Тип эффекта", "Effect type" },
            new[] { "GetCode", "Код", "Code" },
            new[] { "Grid169", "16:9 · сетка третей", "16:9 · rule of thirds" },
            new[] { "GuestCap", "Совместная съёмка", "Joint shoot" },
            new[] { "HkCam", "Кинокамера", "Cine camera" },
            new[] { "HkPanel", "Панель", "Panel" },
            new[] { "HkScene", "Сцена: пуск/стоп", "Scene: play/stop" },
            new[] { "HkRec", "Запись", "Record" },
            new[] { "HkPause", "Пауза", "Pause" },
            new[] { "HkSlowmo", "Слоу-мо", "Slow-mo" },
            new[] { "HotkeysCap", "Горячие клавиши", "Hotkeys" },
            new[] { "InvFoot", "Можно принять и командой /vr join", "You can also accept with /vr join" },
            new[] { "InviteFoot", "Друг увидит окно приглашения и выберет роль: актёр или оператор.", "Your friend sees an invite window and picks a role: actor or camera." },
            new[] { "InviteSub", "игроки онлайн с доступом", "online players with access" },
            new[] { "KeyPanel", "панель", "panel" },
            new[] { "KeyPause", "пауза сцены", "pause scene" },
            new[] { "KeyStopSave", "стоп и сохранить", "stop and save" },
            new[] { "KitDelete", "Удалить", "Delete" },
            new[] { "KitSaveCap", "Сохранить текущий инвентарь", "Save current inventory" },
            new[] { "Linear", "линейно", "linear" },
            new[] { "MoreMembers", "и ещё {0}", "and {0} more" },
            new[] { "MotorAll", "Мотор для всех", "Action for everyone" },
            new[] { "MotorCancelKey", "отменить отсчёт / стоп для всех", "cancel countdown / stop all" },
            new[] { "NoCamKeys", "нет точек", "no points" },
            new[] { "NoMarkers", "Меток нет — назови момент и нажми «+ в …»", "No markers — name a moment and press \"+ at …\"" },
            new[] { "OfTime", "из {0} · {1}", "of {0} · {1}" },
            new[] { "OnOff", "вкл/выкл", "on/off" },
            new[] { "OptCleanSub", "прячет весь HUD плагина", "hides all plugin HUD" },
            new[] { "OptGodSub", "боты не убьют тебя на дубле", "bots won't kill you during a take" },
            new[] { "OptAmmoSub", "магазин не кончается", "the magazine never runs out" },
            new[] { "OptVoiceSub", "боты повторят твои реплики", "bots repeat your lines" },
            new[] { "OptTagsSub", "видишь только ты", "only you see them" },
            new[] { "OptPlayCap", "Воспроизведение", "Playback" },
            new[] { "OptRecCap", "Запись", "Recording" },
            new[] { "OwnFaceCard", "Своё лицо · листай, манекен покажет", "Own face · flip, the mannequin shows it" },
            new[] { "PbMeta", "{0} · {1} актёров", "{0} · {1} actors" },
            new[] { "PlayAllShort", "Проиграть все", "Play all" },
            new[] { "PlayAlong", "одновременно играют {0} актёров", "{0} actors play along" },
            new[] { "Q0", "Ясно", "Clear" },
            new[] { "Q1", "Тучи", "Clouds" },
            new[] { "Q2", "Туман", "Fog" },
            new[] { "Q3", "Дождь", "Rain" },
            new[] { "Q4", "Гроза", "Storm" },
            new[] { "RecShort", "Запись", "Record" },
            new[] { "Rotate90", "90°", "90°" },
            new[] { "Rotate180", "180°", "180°" },
            new[] { "SameThing", "то же самое", "same thing" },
            new[] { "SceneMeta", "{0} дублей · {1}", "{0} takes · {1}" },
            new[] { "SceneNameHint", "Название сцены", "Scene name" },
            new[] { "SceneRunning", "Сцена идёт", "Scene running" },
            new[] { "ShareCap", "Поделиться сценой", "Share scene" },
            new[] { "Shots", "{0} выстр.", "{0} shots" },
            new[] { "ShowCamPath", "Показать путь в мире", "Show path in world" },
            new[] { "SlowmoBtn", "Слоу-мо {0}×", "Slow-mo {0}×" },
            new[] { "Smoothing", "Сглаживание", "Smoothing" },
            new[] { "Smoothness", "Плавность", "Smoothness" },
            new[] { "Spline", "сплайн", "spline" },
            new[] { "StCamLong", "оператор · снимает камерой", "camera · films the scene" },
            new[] { "StopAll", "Стоп для всех", "Stop for all" },
            new[] { "TagYou", " · ты", " · you" },
            new[] { "TakeNameHint", "Имя дубля", "Take name" },
            new[] { "TakesCount", "Дубли сцены · {0}", "Scene takes · {0}" },
            new[] { "Timing", "Время", "Timing" },
            new[] { "Trim", "Обрезка", "Trim" },
            new[] { "TrimEndBtn", "Конец −0.5 с", "End −0.5 s" },
            new[] { "TrimStartBtn", "Начало +0.5 с", "Start +0.5 s" },
            new[] { "TryOnFront", "Примерить перед собой", "Try on in front" },
            new[] { "WEnabledShort", "Погода сцены", "Scene weather" },
            new[] { "WholeSceneCap", "Вся сцена · вокруг тебя", "Whole scene · around you" },
            new[] { "WhoseLook", "Чья внешность", "Whose look" },
            new[] { "Writing", "пишут:", "recording:" },
            new[] { "LangVer", "3.6.0", "3.6.0" },
            // --- сообщения в чат
            new[] { "NoAccess", "Нет доступа (нужно право videorecorder.use).", "No access (videorecorder.use permission required)." },
            new[] { "HostLeft", "Владелец сцены вышел — ты снова в своей студии.", "The scene owner left — you are back in your own studio." },
            new[] { "TakeLimit", "В сцене уже {0} дублей. Удали лишние или создай новую сцену.", "The scene already has {0} takes. Delete some or create a new scene." },
            new[] { "TakeN", "Дубль {0}", "Take {0}" },
            new[] { "SceneN", "Сцена {0}", "Scene {0}" },
            new[] { "MarkerN", "Маркер {0}", "Marker {0}" },
            new[] { "RecStarted", "<color=#e0384e>REC</color> Запись пошла. Стоп: бинд vr.rec или /vr stop", "<color=#e0384e>REC</color> Recording. Stop: vr.rec bind or /vr stop" },
            new[] { "TooShort", "Дубль слишком короткий — не сохранён.", "Take is too short — not saved." },
            new[] { "TakeSaved", "Дубль «{0}» сохранён: {1} сек, кадров {2}.", "Take \"{0}\" saved: {1} s, {2} frames." },
            new[] { "GuestSaved", "{0} записал дубль «{1}» в твою сцену.", "{0} recorded take \"{1}\" into your scene." },
            new[] { "MaxLength", "Достигнута максимальная длина дубля.", "Maximum take length reached." },
            new[] { "NoTakes", "Нет отмеченных дублей.", "No takes enabled." },
            new[] { "ScenePlaying", "Сцена запущена, ботов: {0}", "Scene playing, bots: {0}" },
            new[] { "SceneStopped", "Сцена остановлена, боты убраны.", "Scene stopped, bots removed." },
            new[] { "NotPlaying", "Сцена не запущена.", "Scene is not playing." },
            new[] { "SeekSet", "Сцена будет стартовать с {0} сек.", "Scene will start from {0} s." },
            new[] { "Undone", "Отменено.", "Undone." },
            new[] { "NothingToUndo", "Нечего отменять.", "Nothing to undo." },
            new[] { "UsageRename", "Использование: /vr rename <номер> <имя>", "Usage: /vr rename <number> <name>" },
            new[] { "UsageScene", "Использование: /vr scene <имя>", "Usage: /vr scene <name>" },
            new[] { "UsageInvite", "Использование: /vr invite <ник>", "Usage: /vr invite <name>" },
            new[] { "Done", "Готово.", "Done." },
            new[] { "Exported", "Код сцены: <color=#d9465a>{0}</color> — другой игрок вводит его во вкладке «Сцена» или /vr import {0}", "Scene code: <color=#d9465a>{0}</color> — another player enters it on the Scene tab or /vr import {0}" },
            new[] { "Imported", "Сцена импортирована.", "Scene imported." },
            new[] { "ImportFail", "Не удалось импортировать: неверный код или лимит сцен.", "Import failed: wrong code or scene limit." },
            new[] { "PlayerNotFound", "Игрок не найден.", "Player not found." },
            new[] { "TargetNoAccess", "У игрока нет права videorecorder.use.", "The player lacks videorecorder.use." },
            new[] { "InviteSent", "Приглашение отправлено: {0}", "Invite sent: {0}" },
            new[] { "InviteGot", "{0} зовёт тебя сниматься. Открыто окно приглашения (или /vr join).", "{0} invites you to film. Invite window opened (or /vr join)." },
            new[] { "NoInvites", "Нет активных приглашений.", "No pending invites." },
            new[] { "Joined", "Ты в чужой сцене: дубли сохраняются туда. Выйти — /vr leave", "You joined another scene: your takes are saved there. Leave — /vr leave" },
            new[] { "GuestJoined", "{0} присоединился к твоей сцене.", "{0} joined your scene." },
            new[] { "Left", "Ты вернулся в свою студию.", "You are back in your own studio." },
            new[] { "SceneLimit", "Достигнут лимит сцен.", "Scene limit reached." },
            new[] { "NoWeather", "Погода отключена в конфиге или нет права videorecorder.world.", "Weather is disabled in config or you lack videorecorder.world." },
            new[] { "NoPathPerm", "Нет права videorecorder.paths.", "Missing videorecorder.paths permission." },
            new[] { "PathsShown", "Траектории показаны на {0} сек. Точки — выстрелы, белое — камера, оранжевое — эффекты.", "Paths shown for {0} s. Dots — shots, white — camera, orange — effects." },
            new[] { "CamNoKeys", "Добавь хотя бы одну точку камеры (вкладка «Камера»).", "Add at least one camera point (Camera tab)." },
            new[] { "CamNoBot", "Бот-цель не найден: запусти сцену и выбери дубль-цель.", "Target bot not found: play the scene and pick a target take." },
            new[] { "CamDismount", "Слезь с транспорта, чтобы включить камеру.", "Dismount first to use the camera." },
            new[] { "CamFail", "Не удалось включить камеру на этом сервере.", "Could not start the camera on this server." },
            new[] { "CamOn", "Камера включена. Выход: E (слезть) или бинд vr.cam", "Camera on. Exit: E (dismount) or vr.cam bind" },
            new[] { "CamOff", "Камера выключена.", "Camera off." },
            new[] { "Help",
                "<size=14><color=#d9465a>VideoRecorder</color></size>\n/vr — панель\n/vr rec | stop — запись\n/vr play | pause | end | slowmo | cam\n/vr seek <сек> · /vr undo\n/vr rename <№> <имя> · /vr scene <имя>\n/vr paths · /vr export · /vr import <код>\n/vr invite <ник> · /vr join · /vr leave · /vr team · /vr motor\n/vr kit save|load|add|del <имя> · /vr kit list",
                "<size=14><color=#d9465a>VideoRecorder</color></size>\n/vr — panel\n/vr rec | stop — record\n/vr play | pause | end | slowmo | cam\n/vr seek <sec> · /vr undo\n/vr rename <#> <name> · /vr scene <name>\n/vr paths · /vr export · /vr import <code>\n/vr invite <name> · /vr join · /vr leave" },

            new[] { "TabKits", "Наборы", "Kits" },
            new[] { "KitN", "Набор {0}", "Kit {0}" },
            new[] { "KitName", "НАЗВАНИЕ НАБОРА", "KIT NAME" },
            new[] { "KitNameHint", "например: Набор {0}, снайпер, МВК…", "e.g. Kit {0}, sniper, heavy…" },
            new[] { "KitSave", "Сохранить", "Save" },
            new[] { "KitHint", "Запоминаются одежда, пояс и рюкзак: скины, модули, патроны и прочность.", "Clothing, belt and backpack are saved: skins, mods, ammo and condition." },
            new[] { "KitsHeader", "Мои наборы · {0} из {1}", "My kits · {0} of {1}" },
            new[] { "KitEmpty", "Наборов пока нет. Собери инвентарь и нажми «Сохранить».", "No kits yet. Set up your inventory and press Save." },
            new[] { "KitMeta", "{0} предметов · {1} одежды", "{0} items · {1} worn" },
            new[] { "KitLoad", "Надеть", "Wear" },
            new[] { "KitAdd", "На дубль", "To take" },
            new[] { "KitOver", "Перезаписать", "Overwrite" },
            new[] { "NoKitPerm", "Наборы доступны с правом videorecorder.kits", "Kits require the videorecorder.kits permission" },
            new[] { "UsageKit", "Использование: /vr kit save|load|add|del <имя>, /vr kit wear <№ дубля> <имя>, /vr kit list", "Usage: /vr kit save|load|add|del <name>, /vr kit wear <take #> <name>, /vr kit list" },
            new[] { "UsageKitWear", "Использование: /vr kit wear <№ дубля> <имя набора>", "Usage: /vr kit wear <take #> <kit name>" },
            new[] { "KitLimit", "Достигнут лимит наборов ({0}).", "Kit limit reached ({0})." },
            new[] { "KitSaved", "Набор «{0}» сохранён: {1} предметов.", "Kit \"{0}\" saved: {1} items." },
            new[] { "KitLoaded", "Набор «{0}» выдан: {1} предметов.", "Kit \"{0}\" given: {1} items." },
            new[] { "KitNotFound", "Набор не найден. Список: /vr kit list", "Kit not found. List: /vr kit list" },
            new[] { "KitList", "Твои наборы: {0}", "Your kits: {0}" },
            new[] { "KitWearDone", "Одежда из «{0}» надета на дубль {1}.", "Clothes from \"{0}\" applied to take {1}." },

            new[] { "Look", "Внешность", "Look" },
            new[] { "TryOn", "ПРИМЕРКА", "TRY ON" },
            new[] { "LookTitle", "ВНЕШНОСТЬ · ДУБЛЬ {0}", "APPEARANCE · TAKE {0}" },
            new[] { "Naked", "Без одежды", "No clothes" },
            new[] { "Nick", "НИК", "NAME" },
            new[] { "NickFromLook", "Ник игрока", "Player nick" },
            new[] { "LookOwn", "Своё лицо", "Own face" },
            new[] { "LookMe", "Как у автора", "Like author" },
            new[] { "LookPlayer", "Как у игрока", "Like player" },
            new[] { "FaceN", "ЛИЦО", "FACE" },
            new[] { "FaceRandom", "Случайное лицо", "Random face" },
            new[] { "FaceHint", "Своё лицо: листай лица ниже — манекен перед тобой покажет результат.", "Own face: flip through faces below — a mannequin in front of you shows the result." },
            new[] { "LookMeInfo", "Бот выглядит как {0} — тот, кто записал этот дубль.", "The bot looks like {0}, who recorded this take." },
            new[] { "LookPick", "Выбери игрока онлайн или введи SteamID64:", "Pick an online player or enter a SteamID64:" },
            new[] { "LookPicked", "Внешность игрока {0} ({1}). Можно выбрать другого:", "Looks like {0} ({1}). Pick another:" },
            new[] { "SteamHint", "SteamID64 игрока", "Player SteamID64" },
            new[] { "SteamApply", "Применить", "Apply" },
            new[] { "RealId", "Точная копия по SteamID", "Exact copy by SteamID" },
            new[] { "RealIdHint", "если лицо не совпадает с игроком", "if the face doesn't match the player" },
            new[] { "LookAll", "Ко всем дублям", "Apply to all takes" },
            new[] { "LookAllDone", "Внешность применена ещё к {0} дублям.", "Appearance applied to {0} more takes." },
            new[] { "BadSteamId", "Это не похоже на SteamID64 (17 цифр, начинается с 7656119).", "That is not a SteamID64 (17 digits, starts with 7656119)." },
            new[] { "UsageMove", "Использование: /vr move <откуда №> <куда №>", "Usage: /vr move <from #> <to #>" },

            new[] { "TabTeam", "Команда", "Team" },
            new[] { "RecStartedGroup", "<color=#e0384e>REC</color> Мотор! Все пишут свои дубли. Стоп для всех: vr.rec или vr.motor", "<color=#e0384e>REC</color> Action! Everyone records. Stop for all: vr.rec or vr.motor" },
            new[] { "SceneBusy", "Сейчас в сцене пишет {0}. Дождись или используй «Мотор для всех».", "{0} is recording in this scene. Wait or use \"Action for all\"." },
            new[] { "AlreadyRecording", "В сцене уже идёт запись.", "A recording is already running." },
            new[] { "NoActors", "Нет готовых актёров: включи «Актёр» и «Готов» у участников.", "No ready actors: set Actor and Ready for members." },
            new[] { "MotorAnnounce", "{0} запустил «Мотор для всех». Пишут: {1}", "{0} started Action for all. Recording: {1}" },
            new[] { "MotorYou", "Ты в кадре — приготовься", "You're on — get ready" },
            new[] { "MotorWatch", "Ты оператор — снимай", "You're the camera — roll" },
            new[] { "MotorCancelled", "«Мотор» отменён.", "Action cancelled." },
            new[] { "MotorDone", "Снято! Сохранено дублей: {0}.", "Cut! Takes saved: {0}." },
            new[] { "MotorTitle", "Мотор для всех", "Action for everyone" },
            new[] { "MotorHint", "Один отсчёт {0} с на всю команду: каждый актёр пишет свой дубль одновременно.", "One {0}s countdown for the whole team: every actor records their take at once." },
            new[] { "MotorGo", "Мотор! · {0} акт.", "Action! · {0}" },
            new[] { "MotorCancel", "Отменить отсчёт", "Cancel countdown" },
            new[] { "MotorStop", "Стоп для всех", "Stop for all" },
            new[] { "TeamMembers", "В сцене · {0}", "In scene · {0}" },
            new[] { "LeaveScene", "Выйти из сцены", "Leave scene" },
            new[] { "TagOwner", " · владелец", " · owner" },
            new[] { "TagMe", " (ты)", " (you)" },
            new[] { "StRec", "пишет", "recording" },
            new[] { "StCam", "оператор", "camera" },
            new[] { "StReady", "готов", "ready" },
            new[] { "StNotReady", "не готов", "not ready" },
            new[] { "RoleActor", "Актёр", "Actor" },
            new[] { "RoleCam", "Оператор", "Camera" },
            new[] { "ReadyOn", "Готов", "Ready" },
            new[] { "ReadyOff", "Не готов", "Not ready" },
            new[] { "InviteTitle", "Пригласить в сцену", "Invite to scene" },
            new[] { "NoCandidates", "Нет игроков онлайн с доступом к студии.", "No online players with studio access." },
            new[] { "InvitePending", "ждём: {0}", "waiting: {0}" },
            new[] { "GuestInfo", "Ты в сцене игрока {0} — «{1}». Твои дубли сохраняются туда.", "You're in {0}'s scene \"{1}\". Your takes are saved there." },
            new[] { "GuestCantInvite", "Приглашать может только владелец сцены.", "Only the scene owner can invite." },
            new[] { "AlreadyInTeam", "Игрок уже в твоей сцене.", "Player is already in your scene." },
            new[] { "InviteDeclined", "{0} отклонил приглашение.", "{0} declined the invite." },
            new[] { "GuestLeft", "{0} вышел из сцены.", "{0} left the scene." },
            new[] { "Kicked", "Тебя убрали из сцены — ты снова в своей студии.", "You were removed from the scene." },
            new[] { "InvTitle", "Приглашение на съёмку", "Invitation to a shoot" },
            new[] { "InvFrom", "{0} зовёт тебя сниматься в своей сцене", "{0} invites you to film in their scene" },
            new[] { "InvMeta", "{0} дублей · длина {1} · в команде: {2}", "{0} takes · length {1} · team: {2}" },
            new[] { "InvRole", "Твоя роль", "Your role" },
            new[] { "InvActor", "Актёр", "Actor" },
            new[] { "InvCam", "Оператор", "Camera" },
            new[] { "InvActorHint", "играешь в кадре, бот повторит тебя", "you act on camera, a bot repeats you" },
            new[] { "InvCamHint", "не пишешься, снимаешь камерой", "not recorded, you film the scene" },
            new[] { "InvAccept", "Присоединиться", "Join" },
            new[] { "InvDecline", "Отклонить", "Decline" },
            new[] { "InvExpire", "Приглашение действует {0} с · можно принять и командой /vr join", "Invite expires in {0}s · or type /vr join" },

            new[] { "TabWeather", "Погода", "Weather" },
            new[] { "Atmosphere", "АТМОСФЕРА", "ATMOSPHERE" },
            new[] { "OpenWeather", "ПОГОДА И ВРЕМЯ  >", "WEATHER & TIME  >" },
            new[] { "WCustom", "своя погода", "custom weather" },
            new[] { "WServer", "погода сервера", "server weather" },
            new[] { "WEnabled", "Погода сцены", "Scene weather" },
            new[] { "WApply", "Применить", "Apply" },
            new[] { "WReset", "Вернуть серверную", "Restore server" },
            new[] { "WGlobalNote", "Погода в Rust общая на сервер, время суток можно показать только команде.", "Rust weather is server-wide; time of day can be shown to your team only." },
            new[] { "WNoPerm", "Погоду меняют админы или игроки с правом videorecorder.world (+ включить в конфиге). Время суток работает для твоей команды.", "Weather needs admin or videorecorder.world (+ config). Time of day works for your team." },
            new[] { "WeatherApplied", "Погода сцены применена.", "Scene weather applied." },
            new[] { "WeatherReset", "Вернул погоду и время сервера.", "Server weather and time restored." },
            new[] { "Auto", "авто", "auto" },
            new[] { "TimeAll", "Весь сервер", "Whole server" },
            new[] { "TimeTeam", "Только команда", "Team only" },
            new[] { "Frozen", "Время стоит", "Time frozen" },
            new[] { "Flowing", "Время идёт", "Time flows" },
            new[] { "WClouds", "Облака", "Clouds" },
            new[] { "WFog", "Туман", "Fog" },
            new[] { "WRain", "Дождь/снег", "Rain/snow" },
            new[] { "WWind", "Ветер", "Wind" },
            new[] { "WThunder", "Гроза", "Thunder" },
            new[] { "WRainbow", "Радуга", "Rainbow" },
            new[] { "WBright", "Яркость", "Brightness" },
            new[] { "WContrast", "Контраст", "Contrast" },
            new[] { "PrNoon", "Ясный полдень", "Clear noon" },
            new[] { "PrGolden", "Золотой час", "Golden hour" },
            new[] { "PrDawnFog", "Рассвет в тумане", "Foggy dawn" },
            new[] { "PrOvercast", "Пасмурно", "Overcast" },
            new[] { "PrRain", "Ливень", "Downpour" },
            new[] { "PrStorm", "Гроза ночью", "Night storm" },
            new[] { "PrBlizzard", "Метель", "Blizzard" },
            new[] { "PrHorror", "Туман-хоррор", "Horror fog" },
            new[] { "PrMoon", "Ночь при луне", "Moonlit night" },
            new[] { "PrRainbow", "Радуга", "Rainbow" },

            // --- интерфейс: общее
            new[] { "Subtitle", "студия постановок", "machinima studio" },
            new[] { "GuestOf", "совместная съёмка · сцена игрока {0}", "co-op · scene of {0}" },
            new[] { "Undo", "ОТМЕНИТЬ", "UNDO" },
            new[] { "TakesShort", "дублей", "takes" },
            new[] { "NewScene", "СЦЕНА", "SCENE" },
            new[] { "Delete", "УДАЛИТЬ", "DELETE" },
            new[] { "Sure", "Точно удалить?", "Delete for sure?" },
            new[] { "TabTakes", "Дубли", "Takes" },
            new[] { "TabCamera", "Камера", "Camera" },
            new[] { "TabFx", "Эффекты", "Effects" },
            new[] { "TabScene", "Сцена", "Scene" },
            new[] { "TabOptions", "Опции", "Options" },
            new[] { "On", "ВКЛ", "ON" },
            new[] { "Off", "ВЫКЛ", "OFF" },
            new[] { "Timeline", "ТАЙМЛАЙН · клик = перемотка", "TIMELINE · click = seek" },
            new[] { "ToStart", "В НАЧАЛО", "TO START" },
            new[] { "Pause", "ПАУЗА", "PAUSE" },
            new[] { "Resume", "ПРОДОЛЖИТЬ", "RESUME" },
            new[] { "PlayAll", "ЗАПУСТИТЬ", "PLAY" },
            new[] { "Stop", "Стоп", "Stop" },
            new[] { "Slowmo", "СЛОУ-МО", "SLOW-MO" },
            new[] { "Camera", "Камера", "Camera" },
            new[] { "RecordNew", "●  ЗАПИСАТЬ НОВЫЙ ДУБЛЬ", "●  RECORD NEW TAKE" },
            new[] { "BindsHint", "Вставь в консоль F1 одной строкой", "Paste into the F1 console as one line" },
            new[] { "Stopped", "Остановлена", "Stopped" },
            new[] { "Paused", "Пауза", "Paused" },
            new[] { "Playing", "Идёт", "Playing" },
            new[] { "SlowmoOn", "Слоу-мо", "Slow-mo" },
            new[] { "Recording", "Запись", "Recording" },

            // --- вкладка дубли
            new[] { "NextName", "Имя следующего дубля", "Next take name" },
            new[] { "NextNameHint", "Андрей, снайпер, водитель…", "Andrey, sniper, driver…" },
            new[] { "TakesHeader", "ДУБЛИ ({0})", "TAKES ({0})" },
            new[] { "EmptyTakes", "Пусто. Встань на позицию и нажми «Записать новый дубль».", "Empty. Take your position and press \"Record new take\"." },
            new[] { "TagLoop", "петля", "loop" },
            new[] { "TagBlank", "холостые", "blanks" },
            new[] { "TagGod", "бессмертный", "god" },
            new[] { "TagDie", "смерть", "dies" },
            new[] { "TagVehicle", "транспорт", "vehicle" },
            new[] { "TagVoice", "голос", "voice" },
            new[] { "Play", "ПУСК", "PLAY" },
            new[] { "Edit", "ИЗМЕНИТЬ", "EDIT" },
            new[] { "Rerec", "ПЕРЕЗАПИСЬ", "RE-RECORD" },
            new[] { "ShowPaths", "Показать траектории в мире", "Show paths in world" },

            // --- редактор дубля
            new[] { "TakeTitle", "ДУБЛЬ {0}", "TAKE {0}" },
            new[] { "Delay", "Задержка старта", "Start delay" },
            new[] { "Speed", "Скорость", "Speed" },
            new[] { "Health", "ЗДОРОВЬЕ", "HEALTH" },
            new[] { "DieAt", "Умирает на", "Dies at" },
            new[] { "DieOff", "ВЫКЛ", "OFF" },
            new[] { "TrimStart", "ОБРЕЗ. НАЧАЛО", "TRIM START" },
            new[] { "TrimEnd", "ОБРЕЗ. КОНЕЦ", "TRIM END" },
            new[] { "InScene", "В сцене", "In scene" },
            new[] { "Loop", "Повтор по кругу", "Loop" },
            new[] { "Damage", "Пули наносят урон", "Bullets deal damage" },
            new[] { "God", "Бот бессмертный", "Bot is immortal" },
            new[] { "Voice", "Проигрывать голос", "Play voice" },
            new[] { "TakeInfo", "Длина {0} · кадров {1}\nвыстрелов {2} · транспорт {3} · голос ~{4}с", "Length {0} · frames {1}\nshots {2} · vehicles {3} · voice ~{4}s" },
            new[] { "TpStart", "К старту", "To start" },
            new[] { "TpEnd", "К концу", "To end" },
            new[] { "Path", "ТРАЕКТОРИЯ", "PATH" },
            new[] { "Duplicate", "КОПИЯ", "DUPLICATE" },
            new[] { "MoveUp", "ВЫШЕ", "UP" },
            new[] { "MoveDown", "НИЖЕ", "DOWN" },
            new[] { "MoveHere", "Сюда", "Here" },
            new[] { "Rot90", "90°", "90°" },
            new[] { "Rot180", "180°", "180°" },
            new[] { "Mirror", "Зеркало", "Mirror" },
            new[] { "Back", "< НАЗАД", "< BACK" },
            new[] { "RerecThis", "Перезаписать", "Re-record" },
            new[] { "AppendThis", "Дописать в конец", "Append to end" },

            // --- камера
            new[] { "CamMode", "РЕЖИМ", "MODE" },
            new[] { "CamPath", "Полёт по точкам", "Fly-through" },
            new[] { "CamPathHint", "Камера плавно летит через точки в заданное время сцены (сплайн).", "Camera glides smoothly through the points at their scene times (spline)." },
            new[] { "CamPov", "Глазами бота", "Bot eyes" },
            new[] { "CamPovHint", "Вид от первого лица выбранного актёра.", "First-person view of the chosen actor." },
            new[] { "CamFollow", "Из-за плеча", "Over shoulder" }, new[] { "CamModeOrbit", "Облёт", "Orbit" },
            new[] { "CamFollowHint", "Камера едет за спиной актёра на заданной дистанции.", "Camera follows behind the actor at the set distance." },
            new[] { "CamOrbit", "Скорость облёта", "Orbit speed" },
            new[] { "CamOrbitHint", "Камера кружит вокруг актёра (скорость в °/сек).", "Camera circles the actor (speed in °/s)." },
            new[] { "CamWatch", "Штатив", "Tripod" },
            new[] { "CamWatchHint", "Камера стоит в первой точке пути и поворачивается за актёром.", "Camera stays at the first path point and turns to follow the actor." },
            new[] { "FreeLook", "Свободный обзор мышью", "Free mouse look" },
            new[] { "CamTarget", "Следить за", "Follow" },
            new[] { "CamDist", "Дистанция", "Distance" },
            new[] { "CamHeight", "Высота", "Height" },
            new[] { "CamOrbit", "Скорость облёта", "Orbit speed" },
            new[] { "CamKeys", "ТОЧКИ ПУТИ ({0})", "PATH POINTS ({0})" },
            new[] { "CamAdd", "+ ТОЧКА ОТСЮДА (текущее время)", "+ POINT FROM HERE (current time)" },
            new[] { "Clear", "Очистить", "Clear" },
            new[] { "Tp", "ТП", "TP" },
            new[] { "CamGo", "Смотреть через камеру", "Look through camera" },

            // --- эффекты
            new[] { "FxC4", "Взрыв C4", "C4 explosion" },
            new[] { "FxRocket", "Взрыв ракеты", "Rocket explosion" },
            new[] { "FxBradley", "Взрыв техники", "Vehicle explosion" },
            new[] { "FxGas", "Газовый взрыв", "Gas explosion" },
            new[] { "FxSignal", "Сигнальный дым", "Signal smoke" },
            new[] { "FxFire", "Огонь", "Fire" },
            new[] { "FxSmoke", "Дымовая завеса", "Smoke screen" },
            new[] { "FxFlare", "Фальшфейер", "Flare" },
            new[] { "FxAdd", "+ ПОСТАВИТЬ ТУДА, КУДА СМОТРЮ  в {0}", "+ PLACE WHERE I LOOK  at {0}" },
            new[] { "FxHint", "Эффект сработает в текущий момент сцены. Поставь сцену на паузу в нужном месте, наведи прицел и жми кнопку выше.", "The effect fires at the current scene time. Pause the scene at the right moment, aim and press the button above." },
            new[] { "FxEmpty", "Эффектов пока нет — выбери тип и поставь первый.", "No effects yet — pick a type and place one." },
            new[] { "Test", "ТЕСТ", "TEST" },

            // --- сцена
            new[] { "SceneName", "НАЗВАНИЕ", "NAME" },
            new[] { "DupScene", "Копия сцены", "Duplicate scene" },
            new[] { "Markers", "Маркеры", "Markers" },
            new[] { "MarkerHint", "название метки…", "marker name…" },
            new[] { "MarkerAdd", "+ МАРКЕР В {0}", "+ MARKER AT {0}" },
            new[] { "MoreMarkers", "…и ещё {0} (видны на таймлайне)", "…and {0} more (see the timeline)" },
            new[] { "Go", "Перейти", "Go" },
            new[] { "WholeScene", "ВСЯ СЦЕНА", "WHOLE SCENE" },
            new[] { "TimeOfDay", "Время суток", "Time of day" },
            new[] { "Server", "сервер", "server" },
            new[] { "Reset", "Сброс", "Reset" },
            new[] { "Share", "ПОДЕЛИТЬСЯ", "SHARE" },
            new[] { "Export", "Получить код", "Get code" },
            new[] { "CodeHint", "КОД", "CODE" },
            new[] { "Import", "ИМПОРТ", "IMPORT" },
            new[] { "Weather", "ПОГОДА", "WEATHER" },
            new[] { "WeatherOff", "Погода для всего сервера: включается в конфиге + право videorecorder.world", "Server-wide weather: enable in config + videorecorder.world permission" },
            new[] { "WClear", "Ясно", "Clear" },
            new[] { "WOvercast", "Облачно", "Cloudy" },
            new[] { "WFog", "Туман", "Fog" },
            new[] { "WRainMild", "Дождик", "Drizzle" },
            new[] { "WRainHeavy", "Ливень", "Rain" },
            new[] { "WStorm", "Гроза", "Storm" },

            // --- опции
            new[] { "OptClean", "Чистый кадр (без HUD плагина)", "Clean frame (no plugin HUD)" },
            new[] { "OptGod", "Бессмертие при записи", "God mode while recording" },
            new[] { "OptAmmo", "Бесконечные патроны при записи", "Infinite ammo while recording" },
            new[] { "OptTags", "Подписи над ботами (только тебе)", "Labels above bots (only you)" },
            new[] { "OptVoice", "Записывать мой голос", "Record my voice" },
            new[] { "StartFrom", "Старт сцены с", "Start scene at" },
            new[] { "SceneSpeed", "Скорость сцены", "Scene speed" },
            new[] { "SlowmoSpeed", "Скорость слоу-мо", "Slow-mo speed" },
            new[] { "BindsFull",
                "БИНДЫ (вставь в консоль F1):\nbind f5 vr.cam   — кинокамера\nbind f6 vr.ui    — панель\nbind f7 vr.play  — запустить / остановить сцену\nbind f8 vr.rec   — запись\nbind f9 vr.pause — пауза\nbind f10 vr.slowmo — слоу-мо\nbind f11 vr.motor — мотор для всех\n\nСовместно: /vr invite <ник>, друг пишет /vr join\nДля чистого видео: graphics.hud false в консоли",
                "BINDS (paste into the F1 console):\nbind f5 vr.cam   — cinematic camera\nbind f6 vr.ui    — panel\nbind f7 vr.play  — play / stop scene\nbind f8 vr.rec   — record\nbind f9 vr.pause — pause\nbind f10 vr.slowmo — slow-mo\n\nCo-op: /vr invite <name>, friend types /vr join\nFor clean footage: graphics.hud false in console" },
        };

        #endregion
    }
}
