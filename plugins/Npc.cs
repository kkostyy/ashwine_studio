using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    // NPC «куда смотришь»: неподвижная фигура в одежде и с оружием — для лобби, скринов и декора.
    [Info("Npc", "Claude", "1.1.0")]
    [Description("Static NPC at your crosshair, dressed from your inventory or a kit")]
    public class Npc : RustPlugin
    {
        private const string PermUse = "npc.use";
        private const string PlayerPrefab = "assets/prefabs/player/player.prefab";
        private const string UiMain = "Npc.Main";

        #region Конфиг и данные

        private Configuration _config;

        private class Configuration
        {
            [JsonProperty("Наборы (название → вещи)")] public Dictionary<string, NpcKit> Kits = new Dictionary<string, NpcKit>
            {
                ["ak"] = new NpcKit { Hand = "rifle.ak", Wear = new List<string> { "metal.facemask", "metal.plate.torso", "roadsign.kilt", "hoodie", "pants", "shoes.boots", "tactical.gloves" } },
                ["tier2"] = new NpcKit { Hand = "smg.thompson", Wear = new List<string> { "coffeecan.helmet", "roadsign.jacket", "roadsign.kilt", "hoodie", "pants", "shoes.boots" } },
                ["prim"] = new NpcKit { Hand = "bow.hunting", Wear = new List<string> { "burlap.headwrap", "burlap.shirt", "burlap.trousers", "burlap.shoes" } },
                ["sci"] = new NpcKit { Hand = "smg.mp5", Wear = new List<string> { "hazmatsuit_scientist" } },
                ["guide"] = new NpcKit { Hand = "", Wear = new List<string> { "hat.beenie", "jacket.snow", "pants", "shoes.boots" } },
            };
            [JsonProperty("Предел NPC на игрока (админам без предела)")] public int Limit = 20;
        }

        private class NpcKit { public string Hand; public List<string> Wear = new List<string>(); }

        private class NpcItem { public string S; public ulong Skin; }
        private class NpcRec
        {
            public string Name; public ulong Owner;
            public float X, Y, Z, Yaw;
            public List<NpcItem> Wear = new List<NpcItem>();
            public NpcItem Hand;
            public bool Crouch;
        }
        private class StoredData { public List<NpcRec> Npcs = new List<NpcRec>(); }

        private StoredData _data;
        private readonly Dictionary<BasePlayer, NpcRec> _live = new Dictionary<BasePlayer, NpcRec>();

        protected override void LoadDefaultConfig() => _config = new Configuration();
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { _config = Config.ReadObject<Configuration>(); if (_config == null) throw new Exception(); }
            catch { PrintWarning("Конфиг повреждён — создаю новый"); LoadDefaultConfig(); }
            SaveConfig();
        }
        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private void LoadData()
        {
            try { _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("Npc"); } catch { }
            if (_data == null) _data = new StoredData();
        }
        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("Npc", _data);

        #endregion

        #region Жизненный цикл

        private void Init() { permission.RegisterPermission(PermUse, this); RegisterLang(); LoadData(); }

        private void OnServerInitialized() { foreach (var r in _data.Npcs) SpawnNpc(r); }

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList) CuiHelper.DestroyUi(p, UiMain);
            foreach (var b in _live.Keys.ToList()) if (b != null && !b.IsDestroyed) b.Kill();
            _live.Clear();
        }

        // NPC не получают урон, не умирают и не мешают ботам/турелям
        private object OnEntityTakeDamage(BaseCombatEntity e, HitInfo info) => e is BasePlayer && _live.ContainsKey((BasePlayer)e) ? (object)true : null;
        private object CanBeTargeted(BaseCombatEntity e, MonoBehaviour turret) => e is BasePlayer && _live.ContainsKey((BasePlayer)e) ? (object)false : null;
        private object OnNpcTarget(BaseEntity npc, BaseEntity target) => target is BasePlayer && _live.ContainsKey((BasePlayer)target) ? (object)true : null;
        private object CanLootPlayer(BasePlayer target, BasePlayer looter) => target != null && _live.ContainsKey(target) ? (object)false : null;

        #endregion

        #region Спавн

        private ulong FakeId(int i) => 71000000UL + (ulong)i;

        private BasePlayer SpawnNpc(NpcRec r)
        {
            var bot = GameManager.server.CreateEntity(PlayerPrefab, new Vector3(r.X, r.Y, r.Z), Quaternion.Euler(0f, r.Yaw, 0f)) as BasePlayer;
            if (bot == null) return null;
            bot.userID = FakeId(_data.Npcs.IndexOf(r) + 1);
            bot.displayName = string.IsNullOrEmpty(r.Name) ? "NPC" : r.Name;
            bot.enableSaving = false;
            bot.Spawn();
            if (bot.IsSleeping()) bot.EndSleeping();
            bot.inventory.Strip();
            foreach (var w in r.Wear)
            {
                var it = ItemManager.CreateByName(w.S, 1, w.Skin);
                if (it != null && !it.MoveToContainer(bot.inventory.containerWear)) it.Remove();
            }
            if (r.Hand != null && !string.IsNullOrEmpty(r.Hand.S))
            {
                var it = ItemManager.CreateByName(r.Hand.S, 1, r.Hand.Skin);
                if (it != null)
                {
                    if (!it.MoveToContainer(bot.inventory.containerBelt, 0)) it.Remove();
                    else bot.UpdateActiveItem(it.uid);
                }
            }
            bot.SendNetworkUpdateImmediate();
            _live[bot] = r;
            return bot;
        }

        private Vector3? LookPoint(BasePlayer player)
        {
            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 60f, LayerMask.GetMask("Terrain", "World", "Construction", "Default", "Deployed"), QueryTriggerInteraction.Ignore)) return null;
            return hit.point;
        }

        private BasePlayer LookNpc(BasePlayer player)
        {
            var at = LookPoint(player) ?? player.eyes.position + player.eyes.HeadForward() * 4f;
            return _live.Keys.Where(b => b != null && !b.IsDestroyed).OrderBy(b => Vector3.Distance(b.transform.position, at)).FirstOrDefault(b => Vector3.Distance(b.transform.position, at) < 3f);
        }

        private void Place(BasePlayer player, string kitName, string name)
        {
            var id = (ulong)player.userID;
            var at = LookPoint(player);
            if (at == null) { Reply(player, "NoLook"); return; }
            if (!player.IsAdmin && _data.Npcs.Count(n => n.Owner == id) >= _config.Limit) { Reply(player, "Limit", _config.Limit); return; }
            var r = new NpcRec { Owner = id, Name = name, X = at.Value.x, Y = at.Value.y, Z = at.Value.z, Yaw = player.eyes.rotation.eulerAngles.y + 180f };
            if (!string.IsNullOrEmpty(kitName))
            {
                NpcKit kit;
                if (!_config.Kits.TryGetValue(kitName, out kit)) { Reply(player, "NoKit", string.Join(", ", _config.Kits.Keys)); return; }
                r.Wear = kit.Wear.Select(w => new NpcItem { S = w }).ToList();
                r.Hand = string.IsNullOrEmpty(kit.Hand) ? null : new NpcItem { S = kit.Hand };
            }
            else
            {
                // одежда и оружие в руке — как у тебя сейчас
                r.Wear = player.inventory.containerWear.itemList.Select(i => new NpcItem { S = i.info.shortname, Skin = i.skin }).ToList();
                var held = player.GetActiveItem();
                r.Hand = held != null ? new NpcItem { S = held.info.shortname, Skin = held.skin } : null;
            }

            if (string.IsNullOrEmpty(r.Name)) r.Name = "NPC " + (_data.Npcs.Count + 1);
            _data.Npcs.Add(r);
            SaveData();
            SpawnNpc(r);
            Reply(player, "Placed", r.Name);
        }

        private void Remove(BasePlayer player)
        {
            var b = LookNpc(player);
            if (b == null) { Reply(player, "NoNpc"); return; }
            var r = _live[b];
            if (r.Owner != (ulong)player.userID && !player.IsAdmin) { Reply(player, "NotYours"); return; }
            _live.Remove(b);
            b.Kill();
            _data.Npcs.Remove(r);
            SaveData();
            Reply(player, "Removed", r.Name);
        }

        #endregion

        #region Команды и окно

        [ChatCommand("npc")]
        private void CmdNpc(BasePlayer player, string command, string[] a)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermUse) && !player.IsAdmin) { Reply(player, "NoPerm"); return; }
            string sub = a.Length > 0 ? a[0].ToLower() : "";
            switch (sub)
            {
                case "": Place(player, null, null); break;
                case "kit": Place(player, a.Length > 1 ? a[1] : "", a.Length > 2 ? string.Join(" ", a.Skip(2)) : null); break;
                case "name": Place(player, null, a.Length > 1 ? string.Join(" ", a.Skip(1)) : null); break;
                case "remove": case "del": Remove(player); break;
                case "list": OpenList(player); break;
                case "help": Reply(player, "Help"); break;
                default: Place(player, _config.Kits.ContainsKey(sub) ? sub : null, _config.Kits.ContainsKey(sub) ? null : string.Join(" ", a)); break;
            }
        }

        [ConsoleCommand("npc.ui")]
        private void CmdUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            switch (arg.GetString(0))
            {
                case "close": CuiHelper.DestroyUi(player, UiMain); break;
                case "tp": { int i = arg.GetInt(1); if (i >= 0 && i < _data.Npcs.Count && player.IsAdmin) { var r = _data.Npcs[i]; player.Teleport(new Vector3(r.X, r.Y, r.Z) + Vector3.up * 0.2f); } CuiHelper.DestroyUi(player, UiMain); break; }
                case "del":
                {
                    int i = arg.GetInt(1);
                    if (i < 0 || i >= _data.Npcs.Count) break;
                    var r = _data.Npcs[i];
                    if (r.Owner != (ulong)player.userID && !player.IsAdmin) break;
                    foreach (var kv in _live.Where(kv => kv.Value == r).ToList()) { kv.Key?.Kill(); _live.Remove(kv.Key); }
                    _data.Npcs.Remove(r); SaveData(); OpenList(player); break;
                }
            }
        }

        // окно списка в стиле ASH & WINE

        private void OpenList(BasePlayer player)
        {
            VInit();
            var id = (ulong)player.userID;
            var list = _data.Npcs.Select((r, i) => new { r, i }).Where(x => player.IsAdmin || x.r.Owner == id).Take(9).ToList();
            const float W = 560f, H = 540f;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0.03 0.02 0.02 0.5" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", UiMain);
            string win = UiMain + ".w";
            VRoot(c, win, UiMain, VGlass, "0.5 0.5", "0.5 0.5", $"{-W / 2} {-H / 2}", $"{W / 2} {H / 2}", 22f, true);
            VHead(c, win, "profile", "N", T(id, "Title"), T(id, "Sub"), W, "npc.ui close");
            float y = 73;
            VTx(c, win, T(id, "Count", list.Count).ToUpper(), 11, 17, y, 300, 20, TextAnchor.MiddleLeft, VDim, false);
            y += 26;
            if (list.Count == 0) VTx(c, win, T(id, "Empty"), 12, 17, y, W - 34, 40, TextAnchor.MiddleLeft, VDim, false);
            foreach (var x in list)
            {
                VBBox(c, win, VSurf, VLine, 17, y, W - 34, 40, 12f);
                VIc(c, win, "profile", 30, y + 12, 16, VRose);
                VTx(c, win, x.r.Name, 13, 56, y, 200, 40);
                VTx(c, win, (x.r.Hand?.S ?? "—") + " · " + x.r.Wear.Count + " " + T(id, "Clothes"), 11, 256, y, 140, 40, TextAnchor.MiddleLeft, VDim, false);
                if (player.IsAdmin) VPBtn(c, win, T(id, "Tp"), "npc.ui tp " + x.i, "ghost", W - 172, y + 6, 72, 28, 11);
                VPBtn(c, win, T(id, "Del"), "npc.ui del " + x.i, "soft", W - 94, y + 6, 66, 28, 11);
                y += 46;
            }
            VBox(c, win, VDark, 17, H - 74, W - 34, 56, 14f);
            VTx(c, win, T(id, "Help"), 11, 31, H - 74, W - 62, 56, TextAnchor.MiddleLeft, VDim, false);
            CuiHelper.DestroyUi(player, UiMain);
            CuiHelper.AddUi(player, c);
        }

        #endregion

        #region Язык

        private void RegisterLang()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPerm"] = "Нет права npc.use", ["NoLook"] = "Наведись на землю или постройку",
                ["Limit"] = "Предел: {0} NPC", ["NoKit"] = "Нет такого набора. Есть: {0}",
                ["Placed"] = "NPC «{0}» поставлен", ["NoNpc"] = "Наведись на NPC", ["NotYours"] = "Это не твой NPC", ["Removed"] = "NPC «{0}» убран",
                ["Count"] = "Фигур: {0}", ["Title"] = "Мои NPC", ["Sub"] = "Неподвижные фигуры для лобби и скринов", ["Empty"] = "Пока пусто. Наведись на землю и напиши /npc.",
                ["Clothes"] = "вещей", ["Tp"] = "К НЕМУ", ["Del"] = "УБРАТЬ",
                ["Help"] = "/npc — в твоей одежде и с оружием в руке · /npc kit <ak|tier2|prim|sci|guide> [имя] · /npc name <имя> · /npc remove — убрать того, на кого смотришь · /npc list",
            }, this, "ru");
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPerm"] = "No permission npc.use", ["NoLook"] = "Aim at the ground or a building",
                ["Limit"] = "Limit: {0} NPCs", ["NoKit"] = "No such kit. Available: {0}",
                ["Placed"] = "NPC \"{0}\" placed", ["NoNpc"] = "Aim at an NPC", ["NotYours"] = "Not your NPC", ["Removed"] = "NPC \"{0}\" removed",
                ["Count"] = "Figures: {0}", ["Title"] = "My NPCs", ["Sub"] = "Static figures for the lobby and screenshots", ["Empty"] = "Empty. Aim at the ground and type /npc.",
                ["Clothes"] = "items", ["Tp"] = "GO TO", ["Del"] = "REMOVE",
                ["Help"] = "/npc — your clothes and held weapon · /npc kit <ak|tier2|prim|sci|guide> [name] · /npc name <name> · /npc remove · /npc list",
            }, this);
        }

        private string T(ulong id, string key, params object[] a) { var s = lang.GetMessage(key, this, id.ToString()); return a.Length > 0 ? string.Format(s, a) : s; }
        private void Reply(BasePlayer p, string key, params object[] a) => SendReply(p, "<color=#f0a6b0>NPC</color> " + T((ulong)p.userID, key, a));

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
