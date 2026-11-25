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
    [Info("Npc", "Claude", "1.0.0")]
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
        private const string ColGlass = "0.07 0.04 0.05 0.96", ColPanel = "1 1 1 0.04", ColWine = "0.639 0.086 0.184 1", ColText = "0.95 0.92 0.92 1", ColDim = "0.66 0.6 0.62 1";

        private void OpenList(BasePlayer player)
        {
            var id = (ulong)player.userID;
            var list = _data.Npcs.Select((r, i) => new { r, i }).Where(x => player.IsAdmin || x.r.Owner == id).Take(10).ToList();
            const float W = 560f, H = 520f;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0 0 0 0.55" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", UiMain);
            string win = UiMain + ".w";
            c.Add(new CuiPanel { Image = { Color = ColGlass }, RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = $"{-W / 2} {-H / 2}", OffsetMax = $"{W / 2} {H / 2}" } }, UiMain, win);
            Box(c, win, ColWine, 0, 0, W, 4);
            Txt(c, win, T(id, "Title"), 18, 20, 14, W - 80, 26, TextAnchor.MiddleLeft, ColText);
            Txt(c, win, T(id, "Sub"), 11, 20, 40, W - 40, 16, TextAnchor.MiddleLeft, ColDim);
            Btn(c, win, "×", "npc.ui close", "1 1 1 0.06", W - 50, 14, 32, 32, 16);
            float y = 70;
            if (list.Count == 0) Txt(c, win, T(id, "Empty"), 12, 20, y, W - 40, 40, TextAnchor.MiddleLeft, ColDim);
            foreach (var x in list)
            {
                Box(c, win, ColPanel, 20, y, W - 40, 38);
                Txt(c, win, x.r.Name, 13, 32, y, 220, 38, TextAnchor.MiddleLeft, ColText);
                Txt(c, win, (x.r.Hand?.S ?? "—") + " · " + x.r.Wear.Count + " " + T(id, "Clothes"), 10, 250, y, 150, 38, TextAnchor.MiddleLeft, ColDim);
                if (player.IsAdmin) Btn(c, win, T(id, "Tp"), "npc.ui tp " + x.i, "1 1 1 0.08", W - 170, y + 6, 70, 26, 11);
                Btn(c, win, T(id, "Del"), "npc.ui del " + x.i, ColWine, W - 92, y + 6, 64, 26, 11);
                y += 44;
            }
            Txt(c, win, T(id, "Help"), 10, 20, H - 50, W - 40, 40, TextAnchor.UpperLeft, ColDim);
            CuiHelper.DestroyUi(player, UiMain);
            CuiHelper.AddUi(player, c);
        }

        private static string Off(float x, float y, float w, float h) => $"{x} {-y - h}|{x + w} {-y}";
        private static void Box(CuiElementContainer c, string p, string col, float x, float y, float w, float h)
        {
            var o = Off(x, y, w, h).Split('|');
            c.Add(new CuiPanel { Image = { Color = col }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = o[0], OffsetMax = o[1] } }, p);
        }
        private static void Txt(CuiElementContainer c, string p, string t, int s, float x, float y, float w, float h, TextAnchor a, string col)
        {
            var o = Off(x, y, w, h).Split('|');
            c.Add(new CuiLabel { Text = { Text = t, FontSize = s, Align = a, Color = col, Font = "robotocondensed-regular.ttf" }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = o[0], OffsetMax = o[1] } }, p);
        }
        private static void Btn(CuiElementContainer c, string p, string t, string cmd, string col, float x, float y, float w, float h, int s)
        {
            var o = Off(x, y, w, h).Split('|');
            c.Add(new CuiButton { Button = { Command = cmd, Color = col }, Text = { Text = t, FontSize = s, Align = TextAnchor.MiddleCenter, Color = ColText }, RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = o[0], OffsetMax = o[1] } }, p);
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
                ["Title"] = "МОИ NPC", ["Sub"] = "Неподвижные фигуры для лобби и скринов", ["Empty"] = "Пока пусто. Наведись на землю и напиши /npc.",
                ["Clothes"] = "вещей", ["Tp"] = "К НЕМУ", ["Del"] = "УБРАТЬ",
                ["Help"] = "/npc — в твоей одежде и с оружием в руке · /npc kit <ak|tier2|prim|sci|guide> [имя] · /npc name <имя> · /npc remove — убрать того, на кого смотришь · /npc list",
            }, this, "ru");
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPerm"] = "No permission npc.use", ["NoLook"] = "Aim at the ground or a building",
                ["Limit"] = "Limit: {0} NPCs", ["NoKit"] = "No such kit. Available: {0}",
                ["Placed"] = "NPC \"{0}\" placed", ["NoNpc"] = "Aim at an NPC", ["NotYours"] = "Not your NPC", ["Removed"] = "NPC \"{0}\" removed",
                ["Title"] = "MY NPCS", ["Sub"] = "Static figures for the lobby and screenshots", ["Empty"] = "Empty. Aim at the ground and type /npc.",
                ["Clothes"] = "items", ["Tp"] = "GO TO", ["Del"] = "REMOVE",
                ["Help"] = "/npc — your clothes and held weapon · /npc kit <ak|tier2|prim|sci|guide> [name] · /npc name <name> · /npc remove · /npc list",
            }, this);
        }

        private string T(ulong id, string key, params object[] a) { var s = lang.GetMessage(key, this, id.ToString()); return a.Length > 0 ? string.Format(s, a) : s; }
        private void Reply(BasePlayer p, string key, params object[] a) => SendReply(p, "<color=#f0a6b0>NPC</color> " + T((ulong)p.userID, key, a));

        #endregion
    }
}
