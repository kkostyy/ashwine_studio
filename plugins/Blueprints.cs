// =====================================================================================
//  Blueprints 1.1 — «Чертежи баз» для Rust (Oxide / Carbon)
// =====================================================================================
//  Построил базу один раз -> сохранил чертёж -> ставишь её сколько угодно раз, выбирая
//  место по голограмме (синяя = можно, красная = нельзя).
//
//  Новое в 1.1:
//   • Материал при постановке: как в чертеже / солома / дерево / камень / металл / МВК
//   • Массив: поставить сразу 2–10 копий рядом (улица, деревня, линия стен)
//   • Шаг поворота 15° / 45° / 90° и рамка-габарит базы на голограмме
//   • Мини-план каждой базы в библиотеке, поиск, избранное, публичные чертежи, переименование
//   • «На исходное место» — поставить базу туда, где её сохраняли
//   • Для обычных игроков: чужие шкафы, монументы, кулдаун, лимит баз, оплата ресурсами
//   • Отмена последней постановки и «убрать все мои постановки»
//
//  1.2: окно-библиотека в стиле хаба (мои / опубликованные / избранное / сообщество,
//  схема сверху по материалам, фильтр размера, сортировка, панель выбранной постройки).
//  Если загружен BuildServer — вверху его вкладки, это вкладка «Постройки».
//
//  ТРЕБУЕТСЯ плагин CopyPaste (umod.org/plugins/copy-paste).
//
//  Права:   blueprints.use     — сохранять и ставить чертежи
//           blueprints.all     — видеть все чертежи (не только публичные)
//           blueprints.bypass  — без ограничений (шкафы, монументы, кулдаун, цена)
//  Чат:     /bp, /bp save <имя>, /bp place <имя>, /bp undo, /bp undoall, /bp rename <старое> | <новое>
//  Постановка: [E] поставить  [R]/[Shift+R] повернуть  [Shift+E] всё равно  [ПКМ] выйти
//  Бинды:   bp.height ±0.5 · bp.count ±1 · bp.step · bp.grade · bp.rotate <град>
// =====================================================================================

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
    [Info("Blueprints", "Claude", "1.2.1")]
    [Description("Библиотека чертежей баз с голограммой, массивами, материалами и ограничениями (на базе CopyPaste)")]
    public class Blueprints : RustPlugin
    {
        [PluginReference] private Plugin CopyPaste, BuildServer, Hub;

        #region Конфиг

        private const string PermUse = "blueprints.use";
        private const string PermAll = "blueprints.all";
        private const string PermBypass = "blueprints.bypass";
        private const string UiMain = "BP.Main";
        private const string UiHud = "BP.Hud";
        private const string FilePrefix = "bp_";

        private Configuration _config;

        private class GradeCost
        {
            [JsonProperty("Предмет")] public string Item;
            [JsonProperty("За блок")] public int PerBlock;
        }

        private class Configuration
        {
            [JsonProperty("Максимум чертежей у игрока")] public int MaxBlueprints = 50;
            [JsonProperty("Дальность выбора места (м)")] public float PlaceDistance = 150f;
            [JsonProperty("Частота обновления голограммы (сек)")] public float PreviewTick = 0.2f;
            [JsonProperty("Максимум линий голограммы за кадр")] public int MaxLines = 300;
            [JsonProperty("Оставаться в режиме постановки после установки")] public bool StayInPlaceMode = true;
            [JsonProperty("Сколько последних постановок можно отменить")] public int UndoDepth = 20;
            [JsonProperty("Максимум копий в массиве")] public int MaxArray = 10;
            [JsonProperty("Зазор между копиями в массиве (м)")] public float ArrayGap = 3f;
            [JsonProperty("Скруглённые углы интерфейса")] public bool RoundedCorners = true;

            [JsonProperty("Ограничения: не ставить в зоне чужого шкафа")] public bool RespectPrivilege = true;
            [JsonProperty("Ограничения: не ставить на монументах")] public bool BlockMonuments = true;
            [JsonProperty("Ограничения: кулдаун между постановками (сек)")] public float Cooldown = 0f;
            [JsonProperty("Ограничения: максимум живых баз игрока (0 = без лимита)")] public int MaxActiveBases = 0;
            [JsonProperty("Оплата: брать ресурсы за постановку")] public bool CostEnabled = false;
            [JsonProperty("Оплата: цена за блок по материалу (0 солома … 4 МВК)")]
            public Dictionary<int, GradeCost> Costs = new Dictionary<int, GradeCost>
            {
                [0] = new GradeCost { Item = "wood", PerBlock = 50 },
                [1] = new GradeCost { Item = "wood", PerBlock = 200 },
                [2] = new GradeCost { Item = "stones", PerBlock = 300 },
                [3] = new GradeCost { Item = "metal.fragments", PerBlock = 200 },
                [4] = new GradeCost { Item = "metal.refined", PerBlock = 25 },
            };
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null) throw new Exception();
                if (_config.Costs == null) _config.Costs = new Configuration().Costs;
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

        private class BlueprintInfo
        {
            public string Name;
            public string File;
            public ulong Author;
            public string AuthorName;
            public DateTime Created;
            public int Blocks;
            public int Deployables;
            public float SizeX, SizeZ, SizeY;
            public int Placed;
            public bool Public;
            public Dictionary<int, int> Grades = new Dictionary<int, int>();
            public List<int> Footprint = new List<int>();     // клетки 8x8 (индекс = z*8+x)
        }

        private class PlayerOptions
        {
            public bool Deployables = true;
            public bool Inventories;
            public bool Stability;
            public bool Auth = true;
            public bool AutoHeight = true;
            public int Filter;                  // 0 мои, 1 опубликованные мной, 2 избранное, 3 сообщество
            public int Sort;                    // 0 новые, 1 популярные, 2 по имени
            public int SizeF;                   // 0 любой, 1 до 100 блоков, 2 до 400, 3 больше
            public int Grade = -1;              // -1 как в чертеже, 0..4
            public int Count = 1;
            public float Step = 15f;
            public HashSet<string> Favorites = new HashSet<string>();
        }

        private class StoredData
        {
            public List<BlueprintInfo> Blueprints = new List<BlueprintInfo>();
            public Dictionary<ulong, PlayerOptions> Options = new Dictionary<ulong, PlayerOptions>();
        }

        private StoredData _data;

        private void LoadData()
        {
            try { _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("Blueprints"); }
            catch { _data = null; }
            if (_data == null) _data = new StoredData();
            if (_data.Blueprints == null) _data.Blueprints = new List<BlueprintInfo>();
            if (_data.Options == null) _data.Options = new Dictionary<ulong, PlayerOptions>();
            foreach (var b in _data.Blueprints)
            {
                if (b.Grades == null) b.Grades = new Dictionary<int, int>();
                if (b.Footprint == null) b.Footprint = new List<int>();
            }
            foreach (var o in _data.Options.Values)
                if (o.Favorites == null) o.Favorites = new HashSet<string>();
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("Blueprints", _data);

        private PlayerOptions Opt(ulong id)
        {
            PlayerOptions o;
            if (!_data.Options.TryGetValue(id, out o)) _data.Options[id] = o = new PlayerOptions();
            if (o.Favorites == null) o.Favorites = new HashSet<string>();
            if (o.Step <= 0f) o.Step = 15f;
            o.Count = Mathf.Clamp(o.Count, 1, Mathf.Max(1, _config.MaxArray));
            return o;
        }

        #endregion

        #region Форма чертежа

        private const int KFoundation = 0, KFoundationTri = 1, KWall = 2, KFloor = 3, KFloorTri = 4, KOther = 5;

        private struct Piece
        {
            public Vector3 Pos;
            public float Yaw;
            public int Kind;
            public int Grade;
        }

        private class Shape
        {
            public List<Piece> Pieces = new List<Piece>();
            public int Blocks, Deployables;
            public Vector3 Min, Max;
            public Dictionary<int, int> Grades = new Dictionary<int, int>();
            public bool HasOrigin;
            public Vector3 Origin;
            public float OriginRot;
        }

        private readonly Dictionary<string, Shape> _shapes = new Dictionary<string, Shape>();

        private static float F(object o)
        {
            if (o == null) return 0f;
            float v;
            return float.TryParse(o.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : Convert.ToSingle(o);
        }

        private Shape LoadShape(string file, bool force = false)
        {
            Shape s;
            if (!force && _shapes.TryGetValue(file, out s)) return s;
            s = null;
            try
            {
                var path = "copypaste/" + file;
                if (!Interface.Oxide.DataFileSystem.ExistsDatafile(path)) return null;
                var df = Interface.Oxide.DataFileSystem.GetDatafile(path);
                var ents = df["entities"] as List<object>;
                if (ents == null) return null;
                s = new Shape { Min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), Max = new Vector3(float.MinValue, float.MinValue, float.MinValue) };

                var def = df["default"] as Dictionary<string, object>;
                if (def != null && def.ContainsKey("position"))
                {
                    var dp = def["position"] as Dictionary<string, object>;
                    if (dp != null)
                    {
                        s.Origin = new Vector3(F(dp["x"]), F(dp["y"]), F(dp["z"]));
                        s.OriginRot = def.ContainsKey("rotationdiff") ? F(def["rotationdiff"]) : 0f;
                        s.HasOrigin = true;
                    }
                }

                foreach (var eo in ents)
                {
                    var e = eo as Dictionary<string, object>;
                    if (e == null) continue;
                    var prefab = (e.ContainsKey("prefabname") ? e["prefabname"] as string : null) ?? "";
                    var pos = e.ContainsKey("pos") ? e["pos"] as Dictionary<string, object> : null;
                    var rot = e.ContainsKey("rot") ? e["rot"] as Dictionary<string, object> : null;
                    if (pos == null) continue;
                    if (!e.ContainsKey("grade")) { s.Deployables++; continue; }
                    var p = new Vector3(F(pos["x"]), F(pos["y"]), F(pos["z"]));
                    float yaw = rot != null && rot.ContainsKey("y") ? F(rot["y"]) * Mathf.Rad2Deg : 0f;
                    int grade = Mathf.Clamp(Mathf.RoundToInt(F(e["grade"])), 0, 4);
                    s.Blocks++;
                    int g;
                    s.Grades[grade] = s.Grades.TryGetValue(grade, out g) ? g + 1 : 1;
                    int kind = KOther;
                    if (prefab.Contains("foundation.triangle")) kind = KFoundationTri;
                    else if (prefab.Contains("foundation")) kind = KFoundation;
                    else if (prefab.Contains("floor.triangle")) kind = KFloorTri;
                    else if (prefab.Contains("floor")) kind = KFloor;
                    else if (prefab.Contains("wall")) kind = KWall;
                    s.Pieces.Add(new Piece { Pos = p, Yaw = yaw, Kind = kind, Grade = grade });
                    s.Min = Vector3.Min(s.Min, p);
                    s.Max = Vector3.Max(s.Max, p);
                }
                if (s.Pieces.Count == 0) { s.Min = Vector3.zero; s.Max = Vector3.zero; }
                s.Pieces = s.Pieces.OrderBy(x => x.Kind == KFoundation || x.Kind == KFoundationTri ? 0 : x.Kind == KFloor || x.Kind == KFloorTri ? 1 : x.Kind == KWall ? 2 : 3).ToList();
                _shapes[file] = s;
            }
            catch (Exception ex)
            {
                PrintWarning($"Не удалось прочитать чертёж {file}: {ex.Message}");
            }
            return s;
        }

        // Мини-план 8x8 из фундаментов и полов
        private static List<int> BuildFootprint(Shape s)
        {
            var cells = new HashSet<int>();
            var ground = s.Pieces.Where(p => p.Kind <= KFoundationTri || p.Kind == KFloor || p.Kind == KFloorTri).ToList();
            if (ground.Count == 0) return cells.ToList();
            float minX = ground.Min(p => p.Pos.x), maxX = ground.Max(p => p.Pos.x);
            float minZ = ground.Min(p => p.Pos.z), maxZ = ground.Max(p => p.Pos.z);
            float span = Mathf.Max(maxX - minX, maxZ - minZ, 3f);
            foreach (var p in ground)
            {
                int cx = Mathf.Clamp(Mathf.RoundToInt((p.Pos.x - minX) / span * 7f), 0, 7);
                int cz = Mathf.Clamp(Mathf.RoundToInt((maxZ - p.Pos.z) / span * 7f), 0, 7);
                cells.Add(cz * 8 + cx);
            }
            return cells.ToList();
        }

        private static float BaseWidth(Shape s) => Mathf.Max(3f, s.Max.x - s.Min.x + 3f);

        #endregion

        #region Режим постановки

        private class Placer
        {
            public BasePlayer Player;
            public BlueprintInfo Bp;
            public Shape Shape;
            public float Yaw;
            public float Height;
            public Vector3 Anchor;
            public bool HasAnchor;
            public bool Valid;
            public string Reason;
            public float[] AutoOffsets = new float[0];
            public string LastHud;
            public bool Busy;
        }

        private class Placement
        {
            public string Name;
            public List<BaseEntity> Entities = new List<BaseEntity>();
            public int Alive => Entities.Count(e => e != null && !e.IsDestroyed);
        }

        private readonly Dictionary<ulong, Placer> _placers = new Dictionary<ulong, Placer>();
        private readonly Dictionary<ulong, List<Placement>> _undo = new Dictionary<ulong, List<Placement>>();
        private readonly Dictionary<ulong, float> _lastPlace = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, UiState> _ui = new Dictionary<ulong, UiState>();
        private Timer _tick;
        private int _groundMask, _blockMask;

        private class UiState
        {
            public int Page;
            public string NewName = "";
            public string Search = "";
            public string ConfirmDelete;
            public float ConfirmAt;
            public string Selected;
        }

        private UiState Ui(ulong id)
        {
            UiState s;
            if (!_ui.TryGetValue(id, out s)) _ui[id] = s = new UiState();
            return s;
        }

        private static ulong Id(BasePlayer p) => (ulong)p.userID;

        private bool Bypass(BasePlayer p) => p.IsAdmin || permission.UserHasPermission(p.UserIDString, PermBypass);

        private void StartPlacing(BasePlayer player, BlueprintInfo bp)
        {
            var shape = LoadShape(bp.File, true);
            if (shape == null) { Reply(player, "FileMissing", bp.Name); return; }
            CloseUi(player);
            var opt = Opt(Id(player));
            _placers[Id(player)] = new Placer
            {
                Player = player,
                Bp = bp,
                Shape = shape,
                Yaw = Mathf.Round(Mathf.Repeat(player.eyes.rotation.eulerAngles.y, 360f) / opt.Step) * opt.Step
            };
            Reply(player, "PlaceStart", bp.Name);
        }

        private void StopPlacing(BasePlayer player, bool reply)
        {
            if (player == null) return;
            if (_placers.Remove(Id(player)))
            {
                CuiHelper.DestroyUi(player, UiHud);
                if (reply) Reply(player, "PlaceStop");
            }
        }

        // Смещение i-й копии массива вдоль «правой» стороны базы
        private Vector3 CopyOffset(Placer pl, int i)
        {
            var opt = Opt(Id(pl.Player));
            int n = Mathf.Max(1, opt.Count);
            float step = BaseWidth(pl.Shape) + _config.ArrayGap;
            float shift = (i - (n - 1) / 2f) * step;
            return Quaternion.Euler(0f, pl.Yaw, 0f) * new Vector3(shift, 0f, 0f);
        }

        private Vector3 CopyAnchor(Placer pl, int i)
        {
            var a = pl.Anchor + CopyOffset(pl, i);
            float gy;
            if (GroundAt(a, out gy)) a.y = gy;
            return a;
        }

        private Vector3 Local2World(Placer pl, int copy, Vector3 local)
        {
            float auto = copy < pl.AutoOffsets.Length ? pl.AutoOffsets[copy] : 0f;
            return CopyAnchor(pl, copy) + Vector3.up * (pl.Height + auto) + Quaternion.Euler(0f, pl.Yaw, 0f) * local;
        }

        private bool GroundAt(Vector3 p, out float y)
        {
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out hit, 80f, _groundMask, QueryTriggerInteraction.Ignore))
            {
                y = hit.point.y;
                return true;
            }
            y = p.y;
            return false;
        }

        private void TickPlacers()
        {
            if (_placers.Count == 0) return;
            foreach (var pair in _placers.ToList())
            {
                var pl = pair.Value;
                var player = pl.Player;
                if (player == null || !player.IsConnected || player.IsDead()) { _placers.Remove(pair.Key); continue; }

                RaycastHit hit;
                pl.HasAnchor = Physics.Raycast(player.eyes.HeadRay(), out hit, _config.PlaceDistance, _groundMask, QueryTriggerInteraction.Ignore);
                if (pl.HasAnchor)
                {
                    pl.Anchor = hit.point;
                    Evaluate(pl);
                    DrawPreview(player, pl);
                }
                ShowHud(player, pl);
            }
        }

        private void Evaluate(Placer pl)
        {
            var player = pl.Player;
            var opt = Opt(Id(player));
            int n = opt.Count;
            var rot = Quaternion.Euler(0f, pl.Yaw, 0f);
            var foundations = pl.Shape.Pieces.Where(p => p.Kind == KFoundation || p.Kind == KFoundationTri).Take(60).ToList();
            pl.AutoOffsets = new float[n];
            pl.Valid = true;
            pl.Reason = null;
            bool bypass = Bypass(player);

            for (int c = 0; c < n; c++)
            {
                var anchor = CopyAnchor(pl, c);
                if (opt.AutoHeight && foundations.Count > 0)
                {
                    float need = float.MinValue;
                    foreach (var f in foundations)
                    {
                        var w = anchor + rot * f.Pos;
                        float gy;
                        if (GroundAt(w, out gy)) need = Mathf.Max(need, gy - w.y);
                    }
                    if (need > float.MinValue) pl.AutoOffsets[c] = need;
                }
                if (!pl.Valid) continue;

                float lowest = float.MaxValue, highest = float.MinValue;
                foreach (var f in foundations)
                {
                    var w = Local2World(pl, c, f.Pos);
                    if (Physics.CheckSphere(w + Vector3.up * 0.6f, 1.1f, _blockMask, QueryTriggerInteraction.Ignore)) { Fail(pl, "ReasonBlocked"); break; }
                    if (TerrainMeta.WaterMap != null && TerrainMeta.WaterMap.GetHeight(w) > w.y + 0.5f) { Fail(pl, "ReasonWater"); break; }
                    if (!bypass && _config.BlockMonuments && TerrainMeta.TopologyMap != null &&
                        (TerrainMeta.TopologyMap.GetTopology(w) & (int)TerrainTopology.Enum.Monument) != 0) { Fail(pl, "ReasonMonument"); break; }
                    if (!bypass && _config.RespectPrivilege)
                    {
                        var priv = player.GetBuildingPrivilege(new OBB(w, Quaternion.identity, new Bounds(Vector3.zero, new Vector3(3f, 3f, 3f))));
                        if (priv != null && !priv.IsAuthed(player)) { Fail(pl, "ReasonPrivilege"); break; }
                    }
                    float gy;
                    if (GroundAt(w, out gy))
                    {
                        lowest = Mathf.Min(lowest, w.y - gy);
                        highest = Mathf.Max(highest, w.y - gy);
                    }
                }
                if (pl.Valid && foundations.Count > 0)
                {
                    if (highest > 3.5f) Fail(pl, "ReasonFloating");
                    else if (lowest < -2.5f) Fail(pl, "ReasonBuried");
                }
            }
        }

        private static void Fail(Placer pl, string reason)
        {
            pl.Valid = false;
            if (pl.Reason == null) pl.Reason = reason;
        }

        private static readonly Vector3[] SquareCorners =
        {
            new Vector3(-1.5f, 0f, -1.5f), new Vector3(1.5f, 0f, -1.5f), new Vector3(1.5f, 0f, 1.5f), new Vector3(-1.5f, 0f, 1.5f)
        };

        private static readonly Vector3[] TriCorners =
        {
            new Vector3(0f, 0f, 1.732f), new Vector3(1.5f, 0f, -0.866f), new Vector3(-1.5f, 0f, -0.866f)
        };

        private void DrawPreview(BasePlayer player, Placer pl)
        {
            var color = pl.Valid ? new Color(0.35f, 0.7f, 1f) : new Color(1f, 0.25f, 0.3f);
            float dur = _config.PreviewTick + 0.06f;
            int n = Opt(Id(player)).Count;
            int budgetPer = Mathf.Max(24, _config.MaxLines / n);
            var baseRot = Quaternion.Euler(0f, pl.Yaw, 0f);
            var s = pl.Shape;

            WithAdmin(player, () =>
            {
                for (int c = 0; c < n; c++)
                {
                    int budget = budgetPer;

                    // габаритная рамка по низу
                    var min = new Vector3(s.Min.x - 1.5f, 0f, s.Min.z - 1.5f);
                    var max = new Vector3(s.Max.x + 1.5f, 0f, s.Max.z + 1.5f);
                    var b0 = Local2World(pl, c, new Vector3(min.x, s.Min.y, min.z));
                    var b1 = Local2World(pl, c, new Vector3(max.x, s.Min.y, min.z));
                    var b2 = Local2World(pl, c, new Vector3(max.x, s.Min.y, max.z));
                    var b3 = Local2World(pl, c, new Vector3(min.x, s.Min.y, max.z));
                    var frame = new Color(color.r, color.g, color.b, 0.5f);
                    player.SendConsoleCommand("ddraw.line", dur, frame, b0, b1);
                    player.SendConsoleCommand("ddraw.line", dur, frame, b1, b2);
                    player.SendConsoleCommand("ddraw.line", dur, frame, b2, b3);
                    player.SendConsoleCommand("ddraw.line", dur, frame, b3, b0);
                    budget -= 4;

                    foreach (var p in s.Pieces)
                    {
                        if (budget <= 0) break;
                        var center = Local2World(pl, c, p.Pos);
                        var r = baseRot * Quaternion.Euler(0f, p.Yaw, 0f);
                        switch (p.Kind)
                        {
                            case KFoundation:
                            case KFloor:
                            case KFoundationTri:
                            case KFloorTri:
                            {
                                var corners = p.Kind == KFoundationTri || p.Kind == KFloorTri ? TriCorners : SquareCorners;
                                for (int i = 0; i < corners.Length && budget > 0; i++, budget--)
                                    player.SendConsoleCommand("ddraw.line", dur, color, center + r * corners[i], center + r * corners[(i + 1) % corners.Length]);
                                if ((p.Kind == KFoundation || p.Kind == KFoundationTri) && budget > 0)
                                {
                                    float gy;
                                    if (GroundAt(center, out gy) && center.y - gy > 0.3f)
                                    {
                                        player.SendConsoleCommand("ddraw.line", dur, color, center, new Vector3(center.x, gy, center.z));
                                        budget--;
                                    }
                                }
                                break;
                            }
                            case KWall:
                            {
                                if (budget < 2) { budget = 0; break; }
                                var a = center + r * new Vector3(-1.5f, 0f, 0f);
                                var b = center + r * new Vector3(1.5f, 0f, 0f);
                                player.SendConsoleCommand("ddraw.line", dur, color, a + Vector3.up * 3f, b + Vector3.up * 3f);
                                player.SendConsoleCommand("ddraw.line", dur, color, a, a + Vector3.up * 3f);
                                budget -= 2;
                                break;
                            }
                            default:
                                player.SendConsoleCommand("ddraw.line", dur, color, center, center + Vector3.up * 1.5f);
                                budget--;
                                break;
                        }
                    }

                    var anchor = CopyAnchor(pl, c) + Vector3.up * (pl.Height + (c < pl.AutoOffsets.Length ? pl.AutoOffsets[c] : 0f) + 0.2f);
                    player.SendConsoleCommand("ddraw.arrow", dur, Color.white, anchor, anchor + baseRot * Vector3.forward * 4f, 0.6f);
                    if (n > 1) player.SendConsoleCommand("ddraw.text", dur, Color.white, anchor + Vector3.up * 2f, $"<size=16>{c + 1}</size>");
                }
            });
        }

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

        // ---------- стоимость
        private Dictionary<string, int> CostFor(BlueprintInfo bp, int grade, int copies)
        {
            var result = new Dictionary<string, int>();
            if (!_config.CostEnabled) return result;
            foreach (var kv in bp.Grades)
            {
                int g = grade >= 0 ? grade : kv.Key;
                GradeCost gc;
                if (!_config.Costs.TryGetValue(g, out gc) || gc == null || string.IsNullOrEmpty(gc.Item)) continue;
                int have;
                result[gc.Item] = (result.TryGetValue(gc.Item, out have) ? have : 0) + gc.PerBlock * kv.Value * copies;
            }
            return result;
        }

        private string CostText(ulong id, Dictionary<string, int> cost)
        {
            if (cost.Count == 0) return T(id, "Free");
            return string.Join(", ", cost.Select(kv =>
            {
                var def = ItemManager.FindItemDefinition(kv.Key);
                return $"{kv.Value} {(def != null ? def.displayName.english : kv.Key)}";
            }).ToArray());
        }

        private bool CanPay(BasePlayer player, Dictionary<string, int> cost)
        {
            foreach (var kv in cost)
            {
                var def = ItemManager.FindItemDefinition(kv.Key);
                if (def == null) continue;
                if (player.inventory.GetAmount(def.itemid) < kv.Value) return false;
            }
            return true;
        }

        private void Pay(BasePlayer player, Dictionary<string, int> cost)
        {
            foreach (var kv in cost)
            {
                var def = ItemManager.FindItemDefinition(kv.Key);
                if (def != null) player.inventory.Take(null, def.itemid, kv.Value);
            }
        }

        private int ActiveBases(ulong id)
        {
            List<Placement> list;
            return _undo.TryGetValue(id, out list) ? list.Count(p => p.Alive > 0) : 0;
        }

        // ---------- постановка
        private void Place(BasePlayer player, bool force)
        {
            var id = Id(player);
            Placer pl;
            if (!_placers.TryGetValue(id, out pl)) return;
            if (pl.Busy) return;
            if (!pl.HasAnchor) { Reply(player, "NoAnchor"); return; }
            if (!pl.Valid && !force) { Reply(player, "CantPlace", T(id, pl.Reason ?? "ReasonBlocked")); return; }
            if (!pl.Valid && force && !Bypass(player) && (pl.Reason == "ReasonPrivilege" || pl.Reason == "ReasonMonument"))
            {
                Reply(player, "CantForce");
                return;
            }

            var opt = Opt(id);
            if (!CheckLimits(player, pl.Bp, opt.Count)) return;

            var positions = new List<Vector3>();
            for (int c = 0; c < opt.Count; c++)
                positions.Add(CopyAnchor(pl, c) + Vector3.up * (pl.Height + (c < pl.AutoOffsets.Length ? pl.AutoOffsets[c] : 0f)));
            Paste(player, pl.Bp, positions, pl.Yaw * Mathf.Deg2Rad, pl);
            if (!_config.StayInPlaceMode) StopPlacing(player, false);
        }

        private bool CheckLimits(BasePlayer player, BlueprintInfo bp, int copies)
        {
            var id = Id(player);
            if (CopyPaste == null || !CopyPaste.IsLoaded) { Reply(player, "NoCopyPaste"); return false; }
            if (Bypass(player)) return true;

            float last;
            if (_config.Cooldown > 0f && _lastPlace.TryGetValue(id, out last))
            {
                float left = _config.Cooldown - (Time.realtimeSinceStartup - last);
                if (left > 0f) { Reply(player, "Cooldown", Mathf.CeilToInt(left)); return false; }
            }
            if (_config.MaxActiveBases > 0 && ActiveBases(id) + copies > _config.MaxActiveBases)
            {
                Reply(player, "MaxBases", _config.MaxActiveBases);
                return false;
            }
            var cost = CostFor(bp, Opt(id).Grade, copies);
            if (cost.Count > 0)
            {
                if (!CanPay(player, cost)) { Reply(player, "NoResources", CostText(id, cost)); return false; }
                Pay(player, cost);
            }
            return true;
        }

        private void Paste(BasePlayer player, BlueprintInfo bp, List<Vector3> positions, float rotationRad, Placer pl)
        {
            var id = Id(player);
            var opt = Opt(id);
            var args = new[]
            {
                "deployables", opt.Deployables ? "true" : "false",
                "inventories", opt.Inventories ? "true" : "false",
                "stability", opt.Stability ? "true" : "false",
                "auth", opt.Auth ? "true" : "false",
                "entityowner", "true",
                "autoheight", "false"
            };
            int grade = opt.Grade;
            int pending = positions.Count;
            if (pl != null) pl.Busy = true;

            foreach (var pos in positions)
            {
                var placement = new Placement { Name = bp.Name };
                Action finished = () =>
                {
                    pending--;
                    List<Placement> stack;
                    if (!_undo.TryGetValue(id, out stack)) _undo[id] = stack = new List<Placement>();
                    stack.Add(placement);
                    while (stack.Count > Mathf.Max(1, _config.UndoDepth)) stack.RemoveAt(0);
                    foreach (var e in placement.Entities) if (e != null && e.OwnerID == 0) e.OwnerID = id;
                    if (pending <= 0)
                    {
                        if (pl != null) pl.Busy = false;
                        if (player != null && player.IsConnected) Reply(player, "Placed", bp.Name, positions.Count, placement.Entities.Count);
                    }
                };
                Action<BaseEntity> onSpawn = e =>
                {
                    if (e == null) return;
                    placement.Entities.Add(e);
                    if (grade < 0) return;
                    var block = e as BuildingBlock;
                    if (block == null) return;
                    block.SetGrade((BuildingGrade.Enum)grade);
                    block.SetHealthToMax();
                    block.SendNetworkUpdate();
                };

                object result = CopyPaste.Call("TryPasteFromVector3", pos, rotationRad, bp.File, args, finished, onSpawn, 0);
                if (result is string)
                {
                    pending--;
                    Reply(player, "PasteError", (string)result);
                }
            }

            if (pending <= 0 && pl != null) pl.Busy = false;
            bp.Placed += positions.Count;
            _lastPlace[id] = Time.realtimeSinceStartup;
            SaveData();
            Reply(player, "Placing", bp.Name, positions.Count);
            if (pl != null) timer.Once(20f, () => { if (pl.Busy) pl.Busy = false; });
        }

        private void PasteBack(BasePlayer player, BlueprintInfo bp)
        {
            var shape = LoadShape(bp.File, true);
            if (shape == null || !shape.HasOrigin) { Reply(player, "NoOrigin"); return; }
            if (!Bypass(player) && _config.RespectPrivilege)
            {
                var priv = player.GetBuildingPrivilege(new OBB(shape.Origin, Quaternion.identity, new Bounds(Vector3.zero, new Vector3(6f, 6f, 6f))));
                if (priv != null && !priv.IsAuthed(player)) { Reply(player, "CantPlace", T(Id(player), "ReasonPrivilege")); return; }
            }
            if (!CheckLimits(player, bp, 1)) return;
            CloseUi(player);
            Paste(player, bp, new List<Vector3> { shape.Origin }, shape.OriginRot, null);
        }

        private void Undo(BasePlayer player, bool all)
        {
            List<Placement> stack;
            if (!_undo.TryGetValue(Id(player), out stack) || stack.Count == 0) { Reply(player, "NothingToUndo"); return; }
            var targets = all ? stack.ToList() : new List<Placement> { stack[stack.Count - 1] };
            int n = 0;
            foreach (var p in targets)
            {
                foreach (var e in p.Entities)
                {
                    if (e == null || e.IsDestroyed) continue;
                    e.Kill();
                    n++;
                }
                stack.Remove(p);
            }
            Reply(player, "Undone", n);
        }

        #endregion

        #region Сохранение чертежа

        private static string SafeFile(string name)
        {
            var chars = name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
            var s = new string(chars).Trim('_');
            if (s.Length > 40) s = s.Substring(0, 40);
            return s.Length == 0 ? "base" : s;
        }

        private void SaveBlueprint(BasePlayer player, string name)
        {
            var id = Id(player);
            name = (name ?? "").Trim();
            if (name.Length == 0) name = T(id, "BpN", _data.Blueprints.Count(b => b.Author == id) + 1);
            if (name.Length > 32) name = name.Substring(0, 32);
            if (CopyPaste == null || !CopyPaste.IsLoaded) { Reply(player, "NoCopyPaste"); return; }

            var existing = _data.Blueprints.FirstOrDefault(b => b.Author == id && string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing == null && _data.Blueprints.Count(b => b.Author == id) >= _config.MaxBlueprints)
            {
                Reply(player, "Limit", _config.MaxBlueprints);
                return;
            }

            var file = existing?.File ?? FilePrefix + id + "_" + SafeFile(name) + "_" + DateTime.UtcNow.ToString("yyMMddHHmmss");
            var args = new[] { "method", "building", "deployables", "true", "inventories", "true" };
            Action done = () => FinishSave(player, name, file, existing);

            object result = CopyPaste.Call("TryCopyFromSteamId", id, file, args, done);
            if (result is string) { Reply(player, "CopyError", (string)result); return; }
            Reply(player, "Saving", name);
            timer.Once(5f, () =>
            {
                if (!_data.Blueprints.Any(b => b.File == file) && Interface.Oxide.DataFileSystem.ExistsDatafile("copypaste/" + file))
                    FinishSave(player, name, file, existing);
            });
        }

        private void FinishSave(BasePlayer player, string name, string file, BlueprintInfo existing)
        {
            if (existing == null && _data.Blueprints.Any(b => b.File == file)) return;
            var shape = LoadShape(file, true);
            if (shape == null) { if (player != null) Reply(player, "CopyError", "file"); return; }

            var bp = existing ?? new BlueprintInfo { File = file, Author = player != null ? Id(player) : 0UL };
            bp.Name = name;
            bp.AuthorName = player?.displayName ?? bp.AuthorName;
            bp.Created = DateTime.UtcNow;
            FillStats(bp, shape);
            if (existing == null) _data.Blueprints.Insert(0, bp);
            SaveData();
            if (player != null && player.IsConnected)
            {
                Reply(player, "Saved", name, bp.Blocks, bp.Deployables);
                if (Hub != null && Hub.IsLoaded) Hub.Call("AchAdd", Id(player), "bp_saved", 1);
                OpenUi(player);
            }
        }

        private static void FillStats(BlueprintInfo bp, Shape shape)
        {
            bp.Blocks = shape.Blocks;
            bp.Deployables = shape.Deployables;
            bp.SizeX = Mathf.Max(3f, shape.Max.x - shape.Min.x + 3f);
            bp.SizeZ = Mathf.Max(3f, shape.Max.z - shape.Min.z + 3f);
            bp.SizeY = Mathf.Max(3f, shape.Max.y - shape.Min.y + 3f);
            bp.Grades = new Dictionary<int, int>(shape.Grades);
            bp.Footprint = BuildFootprint(shape);
        }

        #endregion

        #region Хуки

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
            permission.RegisterPermission(PermAll, this);
            permission.RegisterPermission(PermBypass, this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            RegisterLang();
            InitRoundedSprite();
            _groundMask = LayerMask.GetMask("Terrain", "World", "Construction", "Water");
            _blockMask = LayerMask.GetMask("Construction", "Deployed", "Prevent Building");
            _tick = timer.Every(Mathf.Clamp(_config.PreviewTick, 0.1f, 1f), TickPlacers);
            if (CopyPaste == null) PrintWarning("Не найден плагин CopyPaste — установи его: https://umod.org/plugins/copy-paste");

            // старые чертежи без мини-плана — досчитать
            bool changed = false;
            foreach (var bp in _data.Blueprints.Where(b => b.Footprint.Count == 0))
            {
                var s = LoadShape(bp.File, true);
                if (s == null) continue;
                FillStats(bp, s);
                changed = true;
            }
            if (changed) SaveData();
        }

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(p, UiMain);
                CuiHelper.DestroyUi(p, UiHud);
            }
            SaveData();
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason) => StopPlacing(player, false);

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null || _placers.Count == 0) return;
            Placer pl;
            if (!_placers.TryGetValue(Id(player), out pl)) return;
            var opt = Opt(Id(player));
            bool shift = input.IsDown(BUTTON.SPRINT);
            if (input.WasJustPressed(BUTTON.RELOAD))
                pl.Yaw = Mathf.Repeat(pl.Yaw + (shift ? -opt.Step : opt.Step), 360f);
            else if (input.WasJustPressed(BUTTON.USE))
                Place(player, shift);
            else if (input.WasJustPressed(BUTTON.FIRE_SECONDARY))
                StopPlacing(player, true);
        }

        #endregion

        #region Команды

        private bool CanUse(BasePlayer player, bool silent = false)
        {
            if (player == null) return false;
            if (player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermUse)) return true;
            if (!silent) Reply(player, "NoPerm");
            return false;
        }

        private bool CanSeeAll(BasePlayer player) => player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermAll);

        private bool CanManage(BasePlayer player, BlueprintInfo bp) => bp.Author == Id(player) || player.IsAdmin;

        private List<BlueprintInfo> Visible(BasePlayer player)
        {
            var id = Id(player);
            var opt = Opt(id);
            var ui = Ui(id);
            bool all = CanSeeAll(player);
            IEnumerable<BlueprintInfo> q = _data.Blueprints.Where(b => b.Author == id || b.Public || all);
            if (opt.Filter == 0) q = q.Where(b => b.Author == id);
            else if (opt.Filter == 1) q = q.Where(b => b.Author == id && b.Public);
            else if (opt.Filter == 2) q = q.Where(b => opt.Favorites.Contains(b.File));
            else q = q.Where(b => b.Author != id);
            if (opt.SizeF == 1) q = q.Where(b => b.Blocks < 100);
            else if (opt.SizeF == 2) q = q.Where(b => b.Blocks >= 100 && b.Blocks < 400);
            else if (opt.SizeF == 3) q = q.Where(b => b.Blocks >= 400);
            if (!string.IsNullOrEmpty(ui.Search))
                q = q.Where(b => b.Name.IndexOf(ui.Search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 (b.AuthorName ?? "").IndexOf(ui.Search, StringComparison.OrdinalIgnoreCase) >= 0);
            if (opt.Sort == 1) return q.OrderByDescending(b => b.Placed).ThenByDescending(b => b.Created).ToList();
            if (opt.Sort == 2) return q.OrderBy(b => b.Name).ToList();
            return q.OrderByDescending(b => b.Created).ToList();
        }

        private BlueprintInfo FindBp(BasePlayer player, string name)
        {
            var id = Id(player);
            bool all = CanSeeAll(player);
            var list = _data.Blueprints.Where(b => b.Author == id || b.Public || all).ToList();
            return list.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))
                   ?? list.FirstOrDefault(b => b.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));
        }

        private static float ParseFloat(string s, float def)
        {
            float v;
            return float.TryParse(s?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        private static readonly float[] Steps = { 15f, 45f, 90f };

        [ChatCommand("bp")]
        private void CmdChat(BasePlayer player, string command, string[] args)
        {
            if (!CanUse(player)) return;
            var sub = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            var rest = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()) : "";
            switch (sub)
            {
                case "": OpenUi(player); break;
                case "save": SaveBlueprint(player, rest); break;
                case "place":
                {
                    var bp = FindBp(player, rest);
                    if (bp == null) { Reply(player, "NotFound"); return; }
                    StartPlacing(player, bp);
                    break;
                }
                case "back":
                {
                    var bp = FindBp(player, rest);
                    if (bp == null) { Reply(player, "NotFound"); return; }
                    PasteBack(player, bp);
                    break;
                }
                case "rename":
                {
                    var parts = rest.Split('|');
                    var bp = parts.Length == 2 ? FindBp(player, parts[0].Trim()) : null;
                    if (bp == null || !CanManage(player, bp) || parts[1].Trim().Length == 0) { Reply(player, "UsageRename"); return; }
                    bp.Name = parts[1].Trim().Length > 32 ? parts[1].Trim().Substring(0, 32) : parts[1].Trim();
                    SaveData();
                    Reply(player, "Renamed", bp.Name);
                    break;
                }
                case "undo": Undo(player, false); break;
                case "undoall": Undo(player, true); break;
                case "cancel": StopPlacing(player, true); break;
                case "list": Reply(player, "List", string.Join(", ", Visible(player).Select(b => b.Name).ToArray())); break;
                default: Reply(player, "Help"); break;
            }
        }

        private Placer GetPlacer(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            Placer pl;
            return player != null && _placers.TryGetValue(Id(player), out pl) ? pl : null;
        }

        [ConsoleCommand("bp.rotate")]
        private void CmdRotate(ConsoleSystem.Arg arg)
        {
            var pl = GetPlacer(arg);
            if (pl != null) pl.Yaw = Mathf.Repeat(pl.Yaw + ParseFloat(arg.GetString(0), Opt(Id(pl.Player)).Step), 360f);
        }

        [ConsoleCommand("bp.height")]
        private void CmdHeight(ConsoleSystem.Arg arg)
        {
            var pl = GetPlacer(arg);
            if (pl != null) pl.Height = Mathf.Clamp(pl.Height + ParseFloat(arg.GetString(0), 0.5f), -10f, 30f);
        }

        [ConsoleCommand("bp.count")]
        private void CmdCount(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!CanUse(player, true)) return;
            var opt = Opt(Id(player));
            opt.Count = Mathf.Clamp(opt.Count + (int)ParseFloat(arg.GetString(0), 1f), 1, Mathf.Max(1, _config.MaxArray));
        }

        [ConsoleCommand("bp.step")]
        private void CmdStep(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!CanUse(player, true)) return;
            var opt = Opt(Id(player));
            int i = Array.IndexOf(Steps, opt.Step);
            opt.Step = Steps[(i + 1) % Steps.Length];
        }

        [ConsoleCommand("bp.grade")]
        private void CmdGrade(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!CanUse(player, true)) return;
            var opt = Opt(Id(player));
            opt.Grade = opt.Grade >= 4 ? -1 : opt.Grade + 1;
        }

        [ConsoleCommand("bp.place")]
        private void CmdPlace(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player != null) Place(player, arg.GetString(0) == "force");
        }

        [ConsoleCommand("bp.cancel")]
        private void CmdCancel(ConsoleSystem.Arg arg) => StopPlacing(arg.Player(), true);

        [ConsoleCommand("bp.undo")]
        private void CmdUndo(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (CanUse(player)) Undo(player, arg.GetString(0) == "all");
        }

        [ConsoleCommand("bp.ui")]
        private void CmdUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!CanUse(player)) return;
            var id = Id(player);
            var ui = Ui(id);
            var opt = Opt(id);
            var action = arg.GetString(0);
            string rest = arg.Args != null && arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1).ToArray()).Trim() : "";
            var list = Visible(player);
            int idx = arg.GetInt(1, -1);
            BlueprintInfo bp = idx >= 0 && idx < list.Count ? list[idx] : null;

            switch (action)
            {
                case "close": CloseUi(player); return;
                case "name": ui.NewName = rest.Length > 32 ? rest.Substring(0, 32) : rest; break;
                case "search": ui.Search = rest.Length > 24 ? rest.Substring(0, 24) : rest; ui.Page = 0; break;
                case "save": SaveBlueprint(player, ui.NewName); ui.NewName = ""; break;
                case "place": if (bp != null) { StartPlacing(player, bp); return; } break;
                case "back": if (bp != null) { PasteBack(player, bp); return; } break;
                case "undo": Undo(player, false); break;
                case "undoall": Undo(player, true); break;
                case "page": ui.Page = Mathf.Max(0, ui.Page + arg.GetInt(1, 0)); break;
                case "fav":
                    if (bp != null) { if (!opt.Favorites.Remove(bp.File)) opt.Favorites.Add(bp.File); }
                    break;
                case "public":
                    if (bp != null && CanManage(player, bp)) bp.Public = !bp.Public;
                    break;
                case "rename":
                    if (bp != null && CanManage(player, bp))
                    {
                        if (string.IsNullOrEmpty(ui.NewName)) Reply(player, "RenameHint");
                        else { bp.Name = ui.NewName; ui.NewName = ""; Reply(player, "Renamed", bp.Name); }
                    }
                    break;
                case "delete":
                    if (bp != null && CanManage(player, bp))
                    {
                        if (ui.ConfirmDelete == bp.File && Time.realtimeSinceStartup - ui.ConfirmAt < 5f)
                        {
                            _data.Blueprints.Remove(bp);
                            _shapes.Remove(bp.File);
                            ui.ConfirmDelete = null;
                        }
                        else { ui.ConfirmDelete = bp.File; ui.ConfirmAt = Time.realtimeSinceStartup; }
                    }
                    break;
                case "filter": opt.Filter = Mathf.Clamp(arg.GetInt(1, 0), 0, 3); ui.Page = 0; ui.Selected = null; break;
                case "sort": opt.Sort = Mathf.Clamp(arg.GetInt(1, 0), 0, 2); ui.Page = 0; break;
                case "size": opt.SizeF = Mathf.Clamp(arg.GetInt(1, 0), 0, 3); ui.Page = 0; break;
                case "reset": opt.Sort = 0; opt.SizeF = 0; ui.Search = ""; ui.Page = 0; break;
                case "sel": if (bp != null) ui.Selected = bp.File; break;
                case "goto": ui.Page = Mathf.Max(0, arg.GetInt(1, 0)); break;
                case "noop": break;
                case "grade": opt.Grade = Mathf.Clamp(arg.GetInt(1, -1), -1, 4); break;
                case "count": opt.Count = Mathf.Clamp(opt.Count + arg.GetInt(1, 0), 1, Mathf.Max(1, _config.MaxArray)); break;
                case "step": { int i = Array.IndexOf(Steps, opt.Step); opt.Step = Steps[(i + 1) % Steps.Length]; break; }
                case "opt.deploy": opt.Deployables = !opt.Deployables; break;
                case "opt.inv": opt.Inventories = !opt.Inventories; break;
                case "opt.stab": opt.Stability = !opt.Stability; break;
                case "opt.auth": opt.Auth = !opt.Auth; break;
                case "opt.height": opt.AutoHeight = !opt.AutoHeight; break;
            }
            SaveData();
            OpenUi(player);
        }

        #endregion

        #region Интерфейс (вино + пепел, скруглённый)

        private const string ColBg = "0.07 0.065 0.065 0.97";
        private const string ColPanel = "0.105 0.097 0.097 1";
        private const string ColRow = "0.155 0.145 0.145 1";
        private const string ColRowAlt = "0.13 0.12 0.12 1";
        private const string ColBtn = "0.215 0.20 0.20 1";
        private const string ColWine = "0.48 0.07 0.14 1";
        private const string ColRed = "0.66 0.09 0.16 1";
        private const string ColText = "0.93 0.90 0.89 1";
        private const string ColDim = "0.58 0.53 0.52 1";
        private const string ColRose = "0.94 0.65 0.69 1";
        private const string Font = "robotocondensed-bold.ttf";
        private const string FontReg = "robotocondensed-regular.ttf";
        private const float W = 1240f, H = 690f;
        private const int PerPage = 6;

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
            try
            {
                _roundPng = FileStorage.server.StoreByCrc(CommunityEntity.ServerInstance.net.ID, FileStorage.Type.png, Convert.FromBase64String(RoundPngB64)).ToString();
            }
            catch (Exception ex)
            {
                PrintWarning("Rounded UI disabled: " + ex.Message);
            }
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

        private static void Btn(CuiElementContainer c, string text, string cmd, string color, int size, float x, float y, float w, float h, string textColor = ColText, float r = 10f)
        {
            bool round = _roundPng != null && r > 0f;
            if (round) Box(c, color, x, y, w, h, null, UiMain, r);
            c.Add(new CuiButton
            {
                Button = { Command = cmd, Color = round ? "0 0 0 0" : color },
                Text = { Text = text, FontSize = size, Align = TextAnchor.MiddleCenter, Color = textColor, Font = Font },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - h}", OffsetMax = $"{x + w} {-y}" }
            }, UiMain);
        }

        private static void Input(CuiElementContainer c, string name, string value, string hint, string cmd, float x, float y, float w, float h)
        {
            Box(c, ColRow, x, y, w, h, name, UiMain, 9f);
            if (string.IsNullOrEmpty(value))
                Text(c, hint, 11, 10, 0, w - 20, h, TextAnchor.MiddleLeft, "0.45 0.41 0.40 1", FontReg, name);
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

        private void Toggle(CuiElementContainer c, ulong id, string key, bool on, string cmd, float x, float y, float w)
        {
            Box(c, ColRow, x, y, w, 28);
            Text(c, T(id, key), 11, x + 10, y, w - 64, 28);
            Btn(c, on ? T(id, "On") : T(id, "Off"), cmd, on ? ColWine : ColBtn, 10, x + w - 52, y + 4, 46, 20);
        }

        private static void Footprint(CuiElementContainer c, List<int> cells, float x, float y, float size)
        {
            Box(c, "0 0 0 0.3", x, y, size, size, null, UiMain, 8f);
            float pad = 4f, cell = (size - pad * 2) / 8f;
            var set = new HashSet<int>(cells ?? new List<int>());
            for (int i = 0; i < 64; i++)
            {
                if (!set.Contains(i)) continue;
                int cx = i % 8, cz = i / 8;
                c.Add(new CuiPanel
                {
                    Image = { Color = ColRose },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x + pad + cx * cell + 0.5f} {-(y + pad + (cz + 1) * cell) + 0.5f}", OffsetMax = $"{x + pad + (cx + 1) * cell - 0.5f} {-(y + pad + cz * cell) - 0.5f}" }
                }, UiMain);
            }
        }

        private static readonly string[] GradeKeys = { "GAsIs", "G0", "G1", "G2", "G3", "G4" };

        private void CloseUi(BasePlayer player) => CuiHelper.DestroyUi(player, UiMain);

        // ---------- библиотека построек (окно в стиле хаба) ----------

        private static readonly string[] GradeColors =
        {
            "0.60 0.50 0.34 1", "0.52 0.34 0.20 1", "0.56 0.54 0.51 1", "0.50 0.25 0.20 1", "0.32 0.37 0.44 1",
        };

        private readonly Dictionary<string, int[]> _plans = new Dictionary<string, int[]>();

        // схема сверху: n×n клеток, значение = материал + 1 (0 — пусто), +10 — только стены
        private int[] Plan(BlueprintInfo bp, int n)
        {
            int[] grid;
            var key = bp.File + "#" + n;
            if (_plans.TryGetValue(key, out grid)) return grid;
            grid = new int[n * n];
            var s = LoadShape(bp.File);
            if (s != null && s.Pieces.Count > 0)
            {
                float minX = s.Pieces.Min(p => p.Pos.x) - 1.5f, maxX = s.Pieces.Max(p => p.Pos.x) + 1.5f;
                float minZ = s.Pieces.Min(p => p.Pos.z) - 1.5f, maxZ = s.Pieces.Max(p => p.Pos.z) + 1.5f;
                float span = Mathf.Max(maxX - minX, maxZ - minZ, 3f);
                float ox = minX - (span - (maxX - minX)) / 2f, oz = minZ - (span - (maxZ - minZ)) / 2f;
                foreach (var p in s.Pieces)
                {
                    bool ground = p.Kind <= KFloorTri && p.Kind != KWall;
                    float r = ground ? 1.45f : 0f;
                    int x0 = Mathf.Clamp((int)((p.Pos.x - r - ox) / span * n), 0, n - 1), x1 = Mathf.Clamp((int)((p.Pos.x + r - ox) / span * n), 0, n - 1);
                    int z0 = Mathf.Clamp((int)((p.Pos.z - r - oz) / span * n), 0, n - 1), z1 = Mathf.Clamp((int)((p.Pos.z + r - oz) / span * n), 0, n - 1);
                    int v = p.Grade + 1 + (ground ? 0 : 10);
                    for (int cx = x0; cx <= x1; cx++)
                        for (int cz = z0; cz <= z1; cz++)
                        {
                            int i = (n - 1 - cz) * n + cx;
                            int cur = grid[i];
                            if (cur == 0 || (cur > 10 && ground) || (cur % 10 < v % 10 && (cur > 10) == !ground)) grid[i] = v;
                        }
                }
            }
            else if (bp.Footprint != null)
                foreach (var cell in bp.Footprint)
                {
                    int cx = cell % 8 * n / 8, cz = cell / 8 * n / 8;
                    for (int a = 0; a < n / 8; a++)
                        for (int b = 0; b < n / 8; b++)
                            grid[Mathf.Min(n * n - 1, (cz + b) * n + cx + a)] = 2;
                }
            _plans[key] = grid;
            return grid;
        }

        private void DrawPlan(CuiElementContainer c, BlueprintInfo bp, float x, float y, float size, int n)
        {
            Box(c, "0.09 0.085 0.085 1", x, y, size, size, null, UiMain, 8f);
            var grid = Plan(bp, n);
            float pad = 6f, cell = (size - 2 * pad) / n;
            for (int i = 0; i < grid.Length; i++)
            {
                int v = grid[i];
                if (v == 0) continue;
                bool wall = v > 10;
                int g = Mathf.Clamp(v % 10 - 1, 0, 4);
                int cx = i % n, cz = i / n;
                float inset = wall ? cell * 0.3f : 0.5f;
                c.Add(new CuiPanel
                {
                    Image = { Color = wall ? ColRose : GradeColors[g] },
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x + pad + cx * cell + inset} {-(y + pad + (cz + 1) * cell) + inset}", OffsetMax = $"{x + pad + (cx + 1) * cell - inset} {-(y + pad + cz * cell) - inset}" }
                }, UiMain);
            }
        }

        private const float LM = 16f, LSide = 290f, LTop = 74f, LBottom = H - 16f;

        private static string Ago(DateTime t)
        {
            var d = DateTime.UtcNow - t;
            if (d.TotalDays >= 1) return Mathf.FloorToInt((float)d.TotalDays) + "д";
            if (d.TotalHours >= 1) return Mathf.FloorToInt((float)d.TotalHours) + "ч";
            return Mathf.Max(1, Mathf.FloorToInt((float)d.TotalMinutes)) + "м";
        }

        private void LibToggle(CuiElementContainer c, ulong id, string key, bool on, string cmd, float x, float y, float w)
        {
            Box(c, ColRow, x, y, w, 26, null, UiMain, 8f);
            Text(c, T(id, key), 10, x + 9, y, w - 66, 26);
            Box(c, ColBtn, x + w - 60, y + 4, 54, 18, null, UiMain, 6f);
            Btn(c, T(id, "Off"), cmd, on ? "0 0 0 0" : ColRed, 8, x + w - 60, y + 4, 27, 18, on ? ColDim : ColText, 6f);
            Btn(c, T(id, "On"), cmd, on ? ColWine : "0 0 0 0", 8, x + w - 33, y + 4, 27, 18, on ? ColText : ColDim, 6f);
        }

        private void Chips(CuiElementContainer c, string[] labels, int active, string cmd, float x, float y, float w, float h, int offset = 0)
        {
            float cw = (w - (labels.Length - 1) * 4) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                bool a = i + offset == active;
                Btn(c, labels[i], cmd + " " + (i + offset), a ? ColWine : ColBtn, 10, x + i * (cw + 4), y, cw, h, a ? ColText : ColDim, 7f);
            }
        }

        private void OpenUi(BasePlayer player)
        {
            var id = Id(player);
            var ui = Ui(id);
            var opt = Opt(id);
            var list = Visible(player);
            var c = new CuiElementContainer();

            if (_roundPng != null)
                c.Add(new CuiElement
                {
                    Name = UiMain,
                    Parent = "Overlay",
                    Components =
                    {
                        new RoundedImage { Png = _roundPng, Color = ColBg, Ppu = 16f / 18f },
                        new CuiRectTransformComponent { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-W / 2f} {-H / 2f}", OffsetMax = $"{W / 2f} {H / 2f}" },
                        new CuiNeedsCursorComponent()
                    }
                });
            else
                c.Add(new CuiPanel { Image = { Color = ColBg }, RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-W / 2f} {-H / 2f}", OffsetMax = $"{W / 2f} {H / 2f}" }, CursorEnabled = true }, "Overlay", UiMain);

            // ---- шапка как у меню стройки: вкладки ведут в BuildServer
            Text(c, "ASH <color=#a8172a>&</color> WINE", 22, LM, 8, LSide, 28);
            Text(c, T(id, "LibSub", ActiveBases(id)), 10, LM, 34, LSide, 16, TextAnchor.MiddleLeft, ColDim, FontReg);
            float cx = LM + LSide + 14, cw = W - cx - LM;
            bool shell = BuildServer != null && BuildServer.IsLoaded;
            if (shell)
            {
                string[] tabs = { "tools", "library", "zones", "env", "items", "raid", "tp" };
                string[] keys = { "TTools", "TLibrary", "TZones", "TEnv", "TItems", "TRaid", "TTp" };
                float tw = (cw - 40 - 8 - (tabs.Length - 1) * 6) / tabs.Length;
                for (int i = 0; i < tabs.Length; i++)
                {
                    bool a = tabs[i] == "library";
                    Btn(c, T(id, keys[i]), a ? "bp.ui noop" : "bs.ui tab " + tabs[i], a ? ColWine : ColPanel, 12, cx + i * (tw + 6), 12, tw, 36, a ? ColText : ColDim);
                }
            }
            else Text(c, T(id, "Title"), 18, cx, 12, cw - 60, 36);
            Btn(c, "X", "bp.ui close", ColBtn, 13, W - LM - 36, 12, 36, 36);
            Box(c, ColWine, 0, 58, W, 2, null, UiMain, 0f);

            // ---- левая колонка: фильтры, постановка, сохранение
            float x = LM, y = LTop, w = LSide;
            Text(c, T(id, "Filters"), 13, x, y, w, 20);
            Btn(c, T(id, "ResetFilters"), "bp.ui reset", "0 0 0 0", 10, x + w - 110, y, 110, 20, ColRose, 0f);
            Box(c, ColWine, x, y + 21, w, 2, null, UiMain, 0f);
            y += 30;
            Text(c, T(id, "BySize"), 10, x, y, w, 16, TextAnchor.MiddleLeft, ColDim); y += 18;
            Chips(c, new[] { T(id, "SzAny"), T(id, "SzS"), T(id, "SzM"), T(id, "SzL") }, opt.SizeF, "bp.ui size", x, y, w, 24); y += 30;
            Text(c, T(id, "SortBy"), 10, x, y, w, 16, TextAnchor.MiddleLeft, ColDim); y += 18;
            Chips(c, new[] { T(id, "SortNew"), T(id, "SortPop"), T(id, "SortName") }, opt.Sort, "bp.ui sort", x, y, w, 24); y += 34;

            Text(c, T(id, "PasteOpts"), 13, x, y, w, 20);
            Box(c, ColWine, x, y + 21, w, 2, null, UiMain, 0f);
            y += 30;
            Text(c, T(id, "Material"), 10, x, y, w, 16, TextAnchor.MiddleLeft, ColDim); y += 18;
            Chips(c, new[] { T(id, "GAsIs"), T(id, "G0"), T(id, "G1") }, opt.Grade, "bp.ui grade", x, y, w, 24, -1); y += 28;
            Chips(c, new[] { T(id, "G2"), T(id, "G3"), T(id, "G4") }, opt.Grade, "bp.ui grade", x, y, w, 24, 2); y += 30;
            float hw = (w - 6) / 2f;
            Text(c, T(id, "Array"), 10, x, y, hw, 16, TextAnchor.MiddleLeft, ColDim);
            Text(c, T(id, "Step"), 10, x + hw + 6, y, hw, 16, TextAnchor.MiddleLeft, ColDim); y += 18;
            Btn(c, "−", "bp.ui count -1", ColBtn, 13, x, y, 30, 26, ColRose, 7f);
            Box(c, ColRow, x + 34, y, hw - 68, 26, null, UiMain, 7f);
            Text(c, "×" + opt.Count, 12, x + 34, y, hw - 68, 26, TextAnchor.MiddleCenter);
            Btn(c, "+", "bp.ui count 1", ColBtn, 13, x + hw - 30, y, 30, 26, ColRose, 7f);
            Btn(c, opt.Step.ToString("0") + "°", "bp.ui step", ColBtn, 12, x + hw + 6, y, hw, 26, ColText, 7f); y += 32;
            LibToggle(c, id, "OptDeploy", opt.Deployables, "bp.ui opt.deploy", x, y, hw);
            LibToggle(c, id, "OptInv", opt.Inventories, "bp.ui opt.inv", x + hw + 6, y, hw); y += 30;
            LibToggle(c, id, "OptAuth", opt.Auth, "bp.ui opt.auth", x, y, hw);
            LibToggle(c, id, "OptStab", opt.Stability, "bp.ui opt.stab", x + hw + 6, y, hw); y += 30;
            LibToggle(c, id, "OptHeight", opt.AutoHeight, "bp.ui opt.height", x, y, w); y += 36;

            Btn(c, T(id, "UndoBtn"), "bp.ui undo", ColBtn, 10, x, y, hw, 26, ColText, 7f);
            Btn(c, T(id, "UndoAllBtn"), "bp.ui undoall", ColBtn, 10, x + hw + 6, y, hw, 26, "0.92 0.42 0.48 1", 7f);

            float sy = LBottom - 48 - 6 - 30;
            Input(c, UiMain + ".name", ui.NewName, T(id, "NameHint"), "bp.ui name", x, sy, w, 30);
            Btn(c, T(id, "SaveBtn"), "bp.ui save", ColWine, 14, x, sy + 36, w, 48, ColText, 12f);

            // ---- вкладки списка
            y = LTop;
            string[] fk = { "FMine", "FPublished", "FFav", "FCommunity" };
            float fw = (cw - 3 * 8) / 4f;
            for (int i = 0; i < 4; i++)
                Btn(c, T(id, fk[i]), "bp.ui filter " + i, opt.Filter == i ? ColWine : ColPanel, 14, cx + i * (fw + 8), y, fw, 40, opt.Filter == i ? ColText : ColDim, 10f);
            y += 48;

            // ---- детали справа
            const float dw = 290f;
            float gx = cx, gw = cw - dw - 14, dx = cx + cw - dw;
            var sel = list.FirstOrDefault(b => b.File == ui.Selected) ?? list.FirstOrDefault();
            int selIdx = sel != null ? list.IndexOf(sel) : -1;

            Input(c, UiMain + ".search", ui.Search, T(id, "SearchHint"), "bp.ui search", gx, y, gw - 130, 30);
            Text(c, T(id, "Count", list.Count), 10, gx + gw - 124, y, 124, 30, TextAnchor.MiddleRight, ColDim, FontReg);
            y += 38;

            // ---- сетка карточек 3×2
            const int cols = 3, rows = 2;
            float pagerH = 34f, gy = y, gh = LBottom - pagerH - 8 - gy;
            float cardW = (gw - (cols - 1) * 10) / cols, cardH = (gh - (rows - 1) * 10) / rows;
            int pages = Mathf.Max(1, Mathf.CeilToInt(list.Count / (float)PerPage));
            ui.Page = Mathf.Clamp(ui.Page, 0, pages - 1);
            if (list.Count == 0)
            {
                Box(c, ColPanel, gx, gy, gw, gh, null, UiMain, 12f);
                Text(c, T(id, opt.Filter == 0 ? "Empty" : "EmptyFilter"), 12, gx + 30, gy, gw - 60, gh, TextAnchor.MiddleCenter, ColDim, FontReg);
            }
            for (int k = 0; k < PerPage; k++)
            {
                int i = ui.Page * PerPage + k;
                if (i >= list.Count) break;
                var bp = list[i];
                float px = gx + (k % cols) * (cardW + 10), py = gy + (k / cols) * (cardH + 10);
                bool isSel = bp == sel;
                if (isSel) Box(c, ColRed, px - 2, py - 2, cardW + 4, cardH + 4, null, UiMain, 13f);
                Box(c, ColPanel, px, py, cardW, cardH, null, UiMain, 12f);
                Text(c, bp.Name, 12, px, py + 2, cardW, 26, TextAnchor.MiddleCenter);
                float ps = Mathf.Min(cardW - 16, cardH - 28 - 46);
                float planX = px + (cardW - ps) / 2f, planY = py + 28;
                DrawPlan(c, bp, planX, planY, ps, 14);
                bool fav = opt.Favorites.Contains(bp.File);
                if (fav)
                {
                    Box(c, ColWine, planX + 4, planY + 4, 44, 18, null, UiMain, 5f);
                    Text(c, T(id, "FavTag"), 8, planX + 4, planY + 4, 44, 18, TextAnchor.MiddleCenter);
                }
                Box(c, "0.07 0.065 0.065 0.85", planX + ps - 44, planY + 4, 40, 18, null, UiMain, 5f);
                Text(c, Ago(bp.Created), 9, planX + ps - 44, planY + 4, 40, 18, TextAnchor.MiddleCenter, ColDim);
                if (bp.Public)
                {
                    Box(c, ColWine, planX + 4, planY + ps - 22, 70, 18, null, UiMain, 5f);
                    Text(c, T(id, "PublicTag"), 8, planX + 4, planY + ps - 22, 70, 18, TextAnchor.MiddleCenter);
                }
                float my = py + cardH - 44;
                Text(c, bp.AuthorName ?? bp.Author.ToString(), 10, px + 10, my, cardW / 2f, 18, TextAnchor.MiddleLeft, ColRose);
                Text(c, T(id, "Ents", bp.Blocks + bp.Deployables), 10, px + cardW / 2f, my, cardW / 2f - 10, 18, TextAnchor.MiddleRight, ColDim);
                Text(c, T(id, "MetaShort", $"{bp.SizeX:0}×{bp.SizeZ:0}", bp.Placed), 9, px + 10, my + 18, cardW - 20, 18, TextAnchor.MiddleLeft, ColDim, FontReg);
                Btn(c, "", "bp.ui sel " + i, "0 0 0 0", 9, px, py, cardW, cardH, ColText, 0f);
            }

            // ---- пагинация
            float pyb = LBottom - pagerH;
            Btn(c, "<", "bp.ui page -1", ColBtn, 13, gx, pyb, 40, pagerH, ColRose, 8f);
            Btn(c, ">", "bp.ui page 1", ColBtn, 13, gx + gw - 40, pyb, 40, pagerH, ColRose, 8f);
            int shown = Mathf.Min(pages, 9), first = Mathf.Clamp(ui.Page - 4, 0, Mathf.Max(0, pages - shown));
            float nx = gx + gw / 2f - shown * 20f;
            for (int p = 0; p < shown; p++)
            {
                int pg = first + p;
                Btn(c, (pg + 1).ToString(), "bp.ui goto " + pg, pg == ui.Page ? ColWine : "0 0 0 0", 12, nx + p * 40, pyb, 36, pagerH, pg == ui.Page ? ColText : ColDim, 8f);
            }

            // ---- панель выбранной постройки
            Box(c, ColPanel, dx, LTop + 48, dw, LBottom - LTop - 48, null, UiMain, 12f);
            if (sel == null)
                Text(c, T(id, "NoSelection"), 11, dx + 20, LTop + 48, dw - 40, LBottom - LTop - 48, TextAnchor.MiddleCenter, ColDim, FontReg);
            else
            {
                float ddy = LTop + 48 + 10;
                float ps = dw - 60;
                DrawPlan(c, sel, dx + 30, ddy, ps, 24);
                ddy += ps + 8;
                Text(c, sel.Name, 16, dx + 12, ddy, dw - 24, 24); ddy += 24;
                Text(c, (sel.AuthorName ?? "?") + "  ·  " + T(id, "Ents", sel.Blocks + sel.Deployables), 10, dx + 12, ddy, dw - 24, 16, TextAnchor.MiddleLeft, ColRose); ddy += 18;
                var grades = string.Join("  ", Enumerable.Range(0, 5).Where(g => sel.Grades != null && sel.Grades.ContainsKey(g)).Select(g => $"<color=#{GradeHex(g)}>{T(id, "G" + g)}</color> {sel.Grades[g]}").ToArray());
                Text(c, T(id, "Meta", sel.Blocks, sel.Deployables, $"{sel.SizeX:0}×{sel.SizeZ:0}", sel.Placed), 9, dx + 12, ddy, dw - 24, 16, TextAnchor.MiddleLeft, ColDim, FontReg); ddy += 16;
                if (grades.Length > 0) { Text(c, grades, 10, dx + 12, ddy, dw - 24, 16, TextAnchor.MiddleLeft, ColText, FontReg); ddy += 18; }
                var cost = CostFor(sel, opt.Grade, opt.Count);
                if (cost.Count > 0 && !Bypass(player)) { Text(c, T(id, "CostLine", CostText(id, cost)), 9, dx + 12, ddy, dw - 24, 16, TextAnchor.MiddleLeft, ColRose, FontReg); }

                bool mine = CanManage(player, sel), fav = opt.Favorites.Contains(sel.File);
                bool confirm = ui.ConfirmDelete == sel.File && Time.realtimeSinceStartup - ui.ConfirmAt < 5f;
                float bh = 30f, bw2 = (dw - 20 - 6) / 2f, by = LBottom - 10 - bh;
                if (mine)
                {
                    Btn(c, confirm ? T(id, "DelSure") : T(id, "Delete"), "bp.ui delete " + selIdx, confirm ? ColRed : ColBtn, 11, dx + 10, by, bw2, bh, confirm ? ColText : "0.92 0.42 0.48 1", 8f);
                    Btn(c, T(id, "Rename"), "bp.ui rename " + selIdx, ColBtn, 11, dx + 16 + bw2, by, bw2, bh, ColText, 8f);
                    by -= bh + 6;
                    Btn(c, sel.Public ? T(id, "Unpublish") : T(id, "Publish"), "bp.ui public " + selIdx, sel.Public ? ColWine : ColBtn, 11, dx + 10, by, bw2, bh, ColText, 8f);
                }
                Btn(c, fav ? T(id, "FavOn") : T(id, "FavOff"), "bp.ui fav " + selIdx, fav ? ColWine : ColBtn, 11, mine ? dx + 16 + bw2 : dx + 10, by, mine ? bw2 : dw - 20, bh, ColText, 8f);
                by -= bh + 6;
                Btn(c, T(id, "BackBtn"), "bp.ui back " + selIdx, ColBtn, 11, dx + 10, by, dw - 20, bh, ColText, 8f);
                by -= 44 + 6;
                Btn(c, T(id, "PlaceBtn"), "bp.ui place " + selIdx, ColWine, 15, dx + 10, by, dw - 20, 44, ColText, 10f);
            }

            CuiHelper.DestroyUi(player, UiMain);
            CuiHelper.AddUi(player, c);
        }

        private static string GradeHex(int g)
        {
            var p = GradeColors[Mathf.Clamp(g, 0, 4)].Split(' ');
            return string.Concat(p.Take(3).Select(v => ((int)(float.Parse(v, CultureInfo.InvariantCulture) * 255)).ToString("x2")).ToArray());
        }

        private void ShowHud(BasePlayer player, Placer pl)
        {
            var id = Id(player);
            var opt = Opt(id);
            string status = !pl.HasAnchor ? T(id, "HudNoAnchor")
                : pl.Valid ? T(id, "HudOk")
                : T(id, "HudBad", T(id, pl.Reason ?? "ReasonBlocked"));
            string line = T(id, "HudLine", pl.Bp.Name, Mathf.RoundToInt(pl.Yaw), (pl.Height >= 0 ? "+" : "") + pl.Height.ToString("0.0"),
                T(id, GradeKeys[opt.Grade + 1]), opt.Count, opt.Step.ToString("0"));
            string extra = "";
            if (!Bypass(player))
            {
                var cost = CostFor(pl.Bp, opt.Grade, opt.Count);
                if (cost.Count > 0) extra = T(id, "CostLine", CostText(id, cost));
                float last;
                if (_config.Cooldown > 0f && _lastPlace.TryGetValue(id, out last))
                {
                    float left = _config.Cooldown - (Time.realtimeSinceStartup - last);
                    if (left > 0f) extra += (extra.Length > 0 ? "  ·  " : "") + T(id, "CooldownShort", Mathf.CeilToInt(left));
                }
            }
            string key = status + "|" + line + "|" + extra + "|" + pl.Busy;
            if (key == pl.LastHud) return;
            pl.LastHud = key;

            var c = new CuiElementContainer();
            const string root = UiHud;
            if (_roundPng != null)
                c.Add(new CuiElement
                {
                    Name = root,
                    Parent = "Hud",
                    Components =
                    {
                        new RoundedImage { Png = _roundPng, Color = "0.07 0.065 0.065 0.85", Ppu = 1f },
                        new CuiRectTransformComponent { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-300 -120", OffsetMax = "300 -20" }
                    }
                });
            else
                c.Add(new CuiPanel { Image = { Color = "0.07 0.065 0.065 0.85" }, RectTransform = { AnchorMin = "0.5 1", AnchorMax = "0.5 1", OffsetMin = "-300 -120", OffsetMax = "300 -20" } }, "Hud", root);

            string barColor = !pl.HasAnchor ? "0.4 0.38 0.37 1" : pl.Valid ? "0.35 0.7 1 1" : "1 0.25 0.3 1";
            c.Add(new CuiPanel { Image = { Color = barColor }, RectTransform = { AnchorMin = "0.03 0.88", AnchorMax = "0.97 0.92" } }, root);
            c.Add(new CuiLabel { Text = { Text = line, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = ColText, Font = Font }, RectTransform = { AnchorMin = "0 0.6", AnchorMax = "1 0.86" } }, root);
            c.Add(new CuiLabel { Text = { Text = pl.Busy ? T(id, "HudBusy") : status, FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColText, Font = Font }, RectTransform = { AnchorMin = "0 0.4", AnchorMax = "1 0.6" } }, root);
            if (extra.Length > 0)
                c.Add(new CuiLabel { Text = { Text = extra, FontSize = 10, Align = TextAnchor.MiddleCenter, Color = ColRose, Font = FontReg }, RectTransform = { AnchorMin = "0 0.24", AnchorMax = "1 0.4" } }, root);
            c.Add(new CuiLabel { Text = { Text = T(id, "HudKeys"), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = ColDim, Font = FontReg }, RectTransform = { AnchorMin = "0 0.04", AnchorMax = "1 0.24" } }, root);

            CuiHelper.DestroyUi(player, UiHud);
            CuiHelper.AddUi(player, c);
        }

        #endregion

        #region Локализация

        private string T(ulong id, string key, params object[] args)
        {
            var msg = lang.GetMessage(key, this, id.ToString());
            if (args == null || args.Length == 0) return msg;
            try { return string.Format(msg, args); } catch { return msg; }
        }

        private void Reply(BasePlayer player, string key, params object[] args)
        {
            if (player == null || !player.IsConnected) return;
            SendReply(player, "<color=#e0384e>[Чертежи]</color> " + T(Id(player), key, args));
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
            new[] { "NoPerm", "Нет права blueprints.use.", "Missing blueprints.use permission." },
            new[] { "NoCopyPaste", "Нужен плагин CopyPaste (umod.org/plugins/copy-paste).", "CopyPaste plugin is required (umod.org/plugins/copy-paste)." },
            new[] { "BpN", "База {0}", "Base {0}" },
            new[] { "Limit", "Достигнут лимит чертежей ({0}).", "Blueprint limit reached ({0})." },
            new[] { "Saving", "Сохраняю «{0}»… не отводи прицел.", "Saving \"{0}\"… keep aiming at the base." },
            new[] { "Saved", "Чертёж «{0}» сохранён: {1} блоков, {2} предметов.", "Blueprint \"{0}\" saved: {1} blocks, {2} deployables." },
            new[] { "CopyError", "Не получилось скопировать: {0}. Наведись на блок своей базы.", "Copy failed: {0}. Aim at a block of your base." },
            new[] { "FileMissing", "Файл чертежа «{0}» не найден в oxide/data/copypaste.", "Blueprint file \"{0}\" not found in oxide/data/copypaste." },
            new[] { "NotFound", "Чертёж не найден. Список: /bp list", "Blueprint not found. List: /bp list" },
            new[] { "List", "Чертежи: {0}", "Blueprints: {0}" },
            new[] { "PlaceStart", "Ставим «{0}». [E] поставить, [R] повернуть, [ПКМ] выйти.", "Placing \"{0}\". [E] place, [R] rotate, [RMB] exit." },
            new[] { "PlaceStop", "Режим постановки выключен.", "Placement mode off." },
            new[] { "NoAnchor", "Посмотри на землю, куда ставить.", "Look at the ground where to place." },
            new[] { "CantPlace", "Здесь нельзя: {0}. Shift+E — поставить всё равно.", "Can't place here: {0}. Shift+E to force." },
            new[] { "CantForce", "Это ограничение нельзя обойти.", "This restriction can't be bypassed." },
            new[] { "Placing", "Ставлю «{0}» ×{1}…", "Placing \"{0}\" ×{1}…" },
            new[] { "Placed", "Готово: «{0}» ×{1}. Отменить — /bp undo", "Done: \"{0}\" ×{1}. Undo — /bp undo" },
            new[] { "PasteError", "CopyPaste не смог вставить: {0}", "CopyPaste failed: {0}" },
            new[] { "NothingToUndo", "Нечего отменять.", "Nothing to undo." },
            new[] { "Undone", "Убрано объектов: {0}.", "Removed {0} entities." },
            new[] { "NoOrigin", "У этого чертежа нет сохранённого места.", "This blueprint has no saved location." },
            new[] { "Cooldown", "Подожди ещё {0} с.", "Wait {0}s more." },
            new[] { "MaxBases", "Лимит живых баз: {0}. Убери старые — /bp undo.", "Active base limit: {0}. Remove old ones — /bp undo." },
            new[] { "NoResources", "Не хватает ресурсов: {0}.", "Not enough resources: {0}." },
            new[] { "Renamed", "Переименовано: «{0}».", "Renamed: \"{0}\"." },
            new[] { "RenameHint", "Впиши новое имя в поле сверху и снова нажми «Имя».", "Type the new name into the field above and press Name again." },
            new[] { "UsageRename", "Использование: /bp rename <старое имя> | <новое имя>", "Usage: /bp rename <old> | <new>" },
            new[] { "Help", "/bp · /bp save <имя> · /bp place <имя> · /bp back <имя> · /bp undo · /bp undoall · /bp rename <старое> | <новое> · /bp list", "/bp · /bp save <name> · /bp place <name> · /bp back <name> · /bp undo · /bp undoall · /bp rename <old> | <new> · /bp list" },
            new[] { "ReasonBlocked", "мешает другая постройка", "another building is in the way" },
            new[] { "ReasonWater", "под водой", "underwater" },
            new[] { "ReasonFloating", "база висит в воздухе", "base would float" },
            new[] { "ReasonBuried", "база уходит под землю", "base would be buried" },
            new[] { "ReasonMonument", "монумент", "monument" },
            new[] { "ReasonPrivilege", "зона чужого шкафа", "someone else's cupboard zone" },
            new[] { "Free", "бесплатно", "free" },
            new[] { "CostLine", "Цена: {0}", "Cost: {0}" },
            new[] { "CooldownShort", "кулдаун {0} с", "cooldown {0}s" },

            new[] { "Title", "ЧЕРТЕЖИ БАЗ", "BASE BLUEPRINTS" },
            new[] { "Subtitle", "построил один раз — ставь где угодно · твоих баз на карте: {0}", "build once — place anywhere · your bases on map: {0}" },
            new[] { "UndoBtn", "ОТМЕНИТЬ", "UNDO" },
            new[] { "UndoAllBtn", "УБРАТЬ ВСЕ МОИ", "REMOVE ALL MINE" },
            new[] { "SaveTitle", "НОВЫЙ ЧЕРТЁЖ / НОВОЕ ИМЯ", "NEW BLUEPRINT / NEW NAME" },
            new[] { "NameHint", "название: замок, бункер, клан-база…", "name: castle, bunker, clan base…" },
            new[] { "SaveBtn", "СОХРАНИТЬ БАЗУ ПОД ПРИЦЕЛОМ", "SAVE BASE I'M AIMING AT" },
            new[] { "SearchHint", "поиск по названию или автору…", "search by name or author…" },
            new[] { "FAll", "ВСЕ", "ALL" },
            new[] { "FMine", "МОИ", "MINE" },
            new[] { "FFav", "ИЗБРАННОЕ", "FAVORITES" },
            new[] { "LibSub", "постройки · твоих баз на карте: {0}", "builds · your bases on map: {0}" },
            new[] { "TTools", "ИНСТРУМЕНТЫ", "BUILD TOOLS" },
            new[] { "TLibrary", "ПОСТРОЙКИ", "BUILDS" },
            new[] { "TZones", "ЗОНЫ", "ZONES" },
            new[] { "TEnv", "ОКРУЖЕНИЕ", "ENVIRONMENT" },
            new[] { "TItems", "ПРЕДМЕТЫ", "ITEMS" },
            new[] { "TRaid", "РЕЙД-ТЕСТ", "RAID TEST" },
            new[] { "TTp", "ТЕЛЕПОРТ", "TELEPORT" },
            new[] { "Filters", "ФИЛЬТРЫ", "FILTERS" },
            new[] { "ResetFilters", "Сбросить", "Reset" },
            new[] { "BySize", "РАЗМЕР", "SIZE" },
            new[] { "SzAny", "ЛЮБОЙ", "ANY" },
            new[] { "SzS", "МАЛЫЕ", "SMALL" },
            new[] { "SzM", "СРЕДНИЕ", "MEDIUM" },
            new[] { "SzL", "БОЛЬШИЕ", "LARGE" },
            new[] { "SortBy", "СОРТИРОВКА", "SORT BY" },
            new[] { "SortNew", "НОВЫЕ", "LATEST" },
            new[] { "SortPop", "ПОПУЛЯРНЫЕ", "POPULAR" },
            new[] { "SortName", "ПО ИМЕНИ", "NAME" },
            new[] { "FPublished", "ОПУБЛИКОВАННЫЕ", "PUBLISHED" },
            new[] { "FCommunity", "СООБЩЕСТВО", "COMMUNITY" },
            new[] { "EmptyFilter", "Здесь пока пусто", "Nothing here yet" },
            new[] { "Ents", "{0} объектов", "{0} entities" },
            new[] { "MetaShort", "{0} м · ставили ×{1}", "{0} m · placed ×{1}" },
            new[] { "FavTag", "ИЗБР.", "FAV" },
            new[] { "NoSelection", "Выбери постройку слева", "Select a build" },
            new[] { "Delete", "Удалить", "Delete" },
            new[] { "DelSure", "Точно удалить?", "Really delete?" },
            new[] { "Rename", "Переименовать", "Rename" },
            new[] { "Publish", "Опубликовать", "Publish" },
            new[] { "Unpublish", "Скрыть", "Unpublish" },
            new[] { "FavOn", "В избранном", "Favorited" },
            new[] { "FavOff", "В избранное", "Favorite" },
            new[] { "Count", "чертежей: {0}", "blueprints: {0}" },
            new[] { "Empty", "Пусто. Построй базу, наведись на неё и нажми «Сохранить».", "Empty. Build a base, aim at it and press Save." },
            new[] { "Meta", "{0} блоков · {1} предметов · {2} м · ставили ×{3}", "{0} blocks · {1} deployables · {2} m · placed ×{3}" },
            new[] { "PublicTag", "ПУБЛИЧНЫЙ", "PUBLIC" },
            new[] { "PubOn", "ДЛЯ ВСЕХ", "PUBLIC" },
            new[] { "PubOff", "ЛИЧНЫЙ", "PRIVATE" },
            new[] { "RenameShort", "ИМЯ", "NAME" },
            new[] { "FavShort", "ИЗБР.", "FAV" },
            new[] { "BackBtn", "НА ИСХОДНОЕ МЕСТО", "TO ORIGINAL SPOT" },
            new[] { "PlaceBtn", "ПОСТАВИТЬ", "PLACE" },
            new[] { "PasteOpts", "ПРИ ПОСТАНОВКЕ", "WHEN PLACING" },
            new[] { "Material", "МАТЕРИАЛ", "MATERIAL" },
            new[] { "GAsIs", "КАК ЕСТЬ", "AS SAVED" },
            new[] { "G0", "СОЛОМА", "TWIG" },
            new[] { "G1", "ДЕРЕВО", "WOOD" },
            new[] { "G2", "КАМЕНЬ", "STONE" },
            new[] { "G3", "МЕТАЛЛ", "METAL" },
            new[] { "G4", "МВК", "HQM" },
            new[] { "Array", "КОПИЙ РЯДОМ", "COPIES" },
            new[] { "Step", "ШАГ ПОВОРОТА", "ROTATE STEP" },
            new[] { "Keys", "[E] ставить · [R] крутить · [Shift+E] всё равно · [ПКМ] выход", "[E] place · [R] rotate · [Shift+E] force · [RMB] exit" },
            new[] { "OptDeploy", "Мебель", "Deployables" },
            new[] { "OptInv", "Лут", "Loot" },
            new[] { "OptAuth", "Шкаф", "Cupboard" },
            new[] { "OptStab", "Стабильн.", "Stability" },
            new[] { "OptHeight", "Автовыс.", "Auto Y" },
            new[] { "On", "ВКЛ", "ON" },
            new[] { "Off", "ВЫКЛ", "OFF" },
            new[] { "HudLine", "{0}  ·  {1}° (шаг {5}°)  ·  высота {2} м  ·  {3}  ·  ×{4}", "{0}  ·  {1}° (step {5}°)  ·  height {2} m  ·  {3}  ·  ×{4}" },
            new[] { "HudOk", "<color=#79b8ff>МОЖНО СТАВИТЬ</color>", "<color=#79b8ff>OK TO PLACE</color>" },
            new[] { "HudBad", "<color=#ff5a6e>НЕЛЬЗЯ: {0}</color>", "<color=#ff5a6e>BLOCKED: {0}</color>" },
            new[] { "HudNoAnchor", "<color=#a39896>посмотри на землю</color>", "<color=#a39896>look at the ground</color>" },
            new[] { "HudBusy", "<color=#f0a6b1>ставлю…</color>", "<color=#f0a6b1>placing…</color>" },
            new[] { "HudKeys", "[E] поставить  [R]/[Shift+R] повернуть  [Shift+E] всё равно  [ПКМ] выйти  ·  bp.height ±0.5  bp.count ±1  bp.step  bp.grade", "[E] place  [R]/[Shift+R] rotate  [Shift+E] force  [RMB] exit  ·  bp.height ±0.5  bp.count ±1  bp.step  bp.grade" },
        };

        #endregion
    }
}
