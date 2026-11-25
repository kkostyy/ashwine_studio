// =====================================================================================
//  Shop 1.0 — магазин за монеты Hub: скины, титулы, цвет ника, VIP (Oxide / Carbon)
// =====================================================================================
//  Купленный скин работает везде: на аренах, в ранкеде, в аим-трене — плагин сам надевает
//  выбранный скин на любой предмет этого типа, когда он попадает игроку в инвентарь.
//  Титулы и цвет ника видны в чате вместе с рангом (Ranked) и тегом клана (Social).
//  VIP (право shop.vip): свои цвета и титулы бесплатно, вход без очереди на сервер.
//
//  Права:  shop.admin — управление каталогом · shop.vip — VIP
//  Чат:    /shop — магазин
//  Админ:  /shopadmin addheld <цена> [название] — добавить скин предмета из руки
//          /shopadmin add <shortname> <skinId> <цена> <название> · remove <id> · list
//          /shopadmin coins <ник> <кол-во>
//  NPC:    /hubadmin npc cmd:shop Торговец — NPC в лобби, открывает магазин по E
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
    [Info("Shop", "Claude", "1.2.2")]
    [Description("Магазин за монеты: скины на всё, титулы, цвет ника, VIP и вход без очереди")]
    public class Shop : RustPlugin
    {

        // ---------- размер HUD: S / M / L выбирает игрок в профиле хаба (/hud) ----------
        private static readonly HashSet<string> HudRootLayers = new HashSet<string> { "Hud", "Hud.Menu", "Under", "Overlay", "Overall" };

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
        [PluginReference] private Plugin Hub, Ranked, Social, Skinner, GlowingEyes;

        #region Константы

        private const string PermAdmin = "shop.admin";
        private const string PermVip = "shop.vip";
        private const string UiMain = "Sh.Main";
        private const string UiToast = "Sh.Toast";

        private const string ColBg = "0.071 0.055 0.055 0.97";
        private const string ColPanel = "1 1 1 0.03";
        private const string ColRow = "1 1 1 0.045";
        private const string ColBtn = "1 1 1 0.07";
        private const string ColWine = "0.620 0.082 0.188 1";
        private const string ColWineSoft = "0.639 0.086 0.184 0.18";
        private const string ColTab = "0 0 0 0.3";
        private const string ColText = "0.937 0.914 0.906 1";
        private const string ColDim = "0.639 0.596 0.588 1";
        private const string ColRose = "0.94 0.65 0.69 1";
        private const string ColGold = "0.91 0.77 0.42 1";
        private const string ColGreen = "0.50 0.83 0.60 1";
        private const string Font = "robotocondensed-bold.ttf";
        private const string FontReg = "robotocondensed-regular.ttf";

        private static readonly string[] CatsOwn = { "weapon", "armor", "clothing", "title", "color", "vip" };
        private static readonly string[] CatsSkinner = { "skins", "title", "color", "vip" };
        // со Skinner скины выбирает он (/skin), магазин продаёт доступ к его командам
        private bool SkinnerOn => Skinner != null && Skinner.IsLoaded;
        private string[] Cats => SkinnerOn ? CatsSkinner : CatsOwn;

        #endregion

        #region Конфиг

        private class Offer
        {
            [JsonProperty("ID")] public string Id;
            [JsonProperty("Категория (skins/weapon/armor/clothing/title/color)")] public string Cat;
            [JsonProperty("Название")] public string Name;
            [JsonProperty("Предмет (shortname) — для скинов")] public string Item;
            [JsonProperty("ID скина в мастерской")] public ulong Skin;
            [JsonProperty("Титул (текст) — для титулов")] public string Title;
            [JsonProperty("Цвет (#rrggbb) — для титулов и цвета ника")] public string Color;
            [JsonProperty("Цена (монеты)")] public int Price;
            [JsonProperty("Только VIP")] public bool VipOnly;
            [JsonProperty("Включено")] public bool Enabled = true;
            [JsonProperty("Права, которые выдаёт покупка (через запятую) — для перков")] public string Perms;
            [JsonProperty("Описание")] public string Desc;
        }

        private class Configuration
        {
            [JsonProperty("Форматировать чат (титул, ранг, клан, цвет ника)")] public bool ChatFormat = true;
            [JsonProperty("Показывать ранг из Ranked в чате")] public bool ChatRank = true;
            [JsonProperty("Показывать тег клана в чате (Social)")] public bool ChatClan = true;
            [JsonProperty("VIP: вход без очереди")] public bool VipSkipQueue = true;
            [JsonProperty("VIP: титулы и цвета бесплатно")] public bool VipFreeCosmetics = true;
            [JsonProperty("VIP: титул")] public string VipTitle = "VIP";
            [JsonProperty("VIP: цвет титула")] public string VipColor = "#e7c46a";
            [JsonProperty("Скруглённые углы интерфейса")] public bool RoundedCorners = true;
            [JsonProperty("Каталог")] public List<Offer> Offers = DefaultOffers();
        }

        private static List<Offer> DefaultOffers()
        {
            var list = new List<Offer>
            {
                // скины: впиши ID скинов из мастерской Steam (число в адресе страницы скина) или добавь через /shopadmin addheld
                new Offer { Id = "ak_1", Cat = "weapon", Name = "AK · скин 1", Item = "rifle.ak", Skin = 0, Price = 1200, Enabled = false },
                new Offer { Id = "lr_1", Cat = "weapon", Name = "LR-300 · скин 1", Item = "rifle.lr300", Skin = 0, Price = 800, Enabled = false },
                new Offer { Id = "mp5_1", Cat = "weapon", Name = "MP5 · скин 1", Item = "smg.mp5", Skin = 0, Price = 450, Enabled = false },
                new Offer { Id = "mask_1", Cat = "armor", Name = "МВК-маска · скин 1", Item = "metal.facemask", Skin = 0, Price = 1500, Enabled = false },
                new Offer { Id = "plate_1", Cat = "armor", Name = "МВК-нагрудник · скин 1", Item = "metal.plate.torso", Skin = 0, Price = 1200, Enabled = false },
                new Offer { Id = "hoodie_1", Cat = "clothing", Name = "Худи · скин 1", Item = "hoodie", Skin = 0, Price = 300, Enabled = false },
            };
            // перки: доступ к командам Skinner и косметика других плагинов
            list.Add(new Offer { Id = "p_skin", Cat = "skins", Name = "Скинер /skin", Item = "spraycan", Perms = "skinner.default", Price = 400, Desc = "Любой скин на свои вещи" });
            list.Add(new Offer { Id = "p_auto", Cat = "skins", Name = "Авто-скины", Item = "rifle.ak", Perms = "skinner.craft,skinner.skinauto", Price = 700, Desc = "Скины сами надеваются на всё" });
            list.Add(new Offer { Id = "p_inv", Cat = "skins", Name = "Весь инвентарь", Item = "box.wooden.large", Perms = "skinner.skininv,skinner.skincon", Price = 500, Desc = "/skininv, /skincon — всё разом" });
            list.Add(new Offer { Id = "p_base", Cat = "skins", Name = "Скины построек", Item = "door.hinged.metal", Perms = "skinner.items,skinner.skinbase", Price = 500, Desc = "/skinitem, /skinbase — база" });
            list.Add(new Offer { Id = "p_em_dance", Cat = "skins", Name = "Танцы", Item = "fun.guitar", Perms = "emotes.dance", Price = 600, Desc = "/emote — танцы в меню жестов" });
            list.Add(new Offer { Id = "p_em_taunt", Cat = "skins", Name = "Насмешки", Item = "skull.human", Perms = "emotes.taunt", Price = 800, Desc = "/emote — дразнилки после победы" });
            list.Add(new Offer { Id = "p_eyes", Cat = "skins", Name = "Светящиеся глаза", Item = "gloweyes", Perms = "glowingeyes.use", Price = 0, VipOnly = true, Desc = "/eyes — включить и выключить" });
            Func<string, string, string, int, Offer> Ti = (id, t, c, p) => new Offer { Id = id, Cat = "title", Name = t, Title = t, Color = c, Price = p };
            list.Add(Ti("t_aim", "Аимер", "#f0a6b1", 500));
            list.Add(Ti("t_rat", "Крыса", "#a39896", 300));
            list.Add(Ti("t_builder", "Строитель", "#d9b48c", 400));
            list.Add(Ti("t_director", "Режиссёр", "#b27ae0", 600));
            list.Add(Ti("t_sniper", "Снайпер", "#7fd8d0", 800));
            list.Add(Ti("t_legend", "Легенда арен", "#ff6b7f", 2500));
            list.Add(Ti("t_wine", "Винный", "#c2203b", 1000));
            list.Add(Ti("t_ash", "Пепельный", "#8c817f", 1000));
            Func<string, string, string, int, bool, Offer> Co = (id, n, c, p, v) => new Offer { Id = id, Cat = "color", Name = n, Color = c, Price = p, VipOnly = v };
            list.Add(Co("c_rose", "Розовый", "#f0a6b1", 400, false));
            list.Add(Co("c_wine", "Винный", "#e0566b", 600, false));
            list.Add(Co("c_gold", "Золотой", "#e7c46a", 900, false));
            list.Add(Co("c_ice", "Ледяной", "#8fd0ff", 700, false));
            list.Add(Co("c_mint", "Мятный", "#7fd49a", 700, false));
            list.Add(Co("c_violet", "Фиолетовый", "#c58cff", 900, false));
            list.Add(Co("c_white", "Белый", "#ffffff", 300, false));
            list.Add(Co("c_vip", "VIP-золото", "#ffcf4a", 0, true));
            return list;
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
                if (_config.Offers == null) _config.Offers = DefaultOffers();
                // новые товары из обновлений дописываются в старый каталог
                foreach (var o in DefaultOffers()) if (!_config.Offers.Any(x => x.Id == o.Id)) _config.Offers.Add(o);
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

        private class PData
        {
            public HashSet<string> Owned = new HashSet<string>();
            public Dictionary<string, ulong> Skins = new Dictionary<string, ulong>();   // shortname → выбранный скин
            public string Title;      // id оффера или "vip" или "season:<текст>"
            public string Color;      // id оффера
            public bool HideRank;
        }

        private Dictionary<ulong, PData> _players;
        private bool _dirty;

        private void LoadData()
        {
            try { _players = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, PData>>("Shop"); } catch { _players = null; }
            if (_players == null) _players = new Dictionary<ulong, PData>();
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject("Shop", _players);
            _dirty = false;
        }

        private PData D(ulong id)
        {
            PData d;
            if (!_players.TryGetValue(id, out d)) _players[id] = d = new PData();
            if (d.Owned == null) d.Owned = new HashSet<string>();
            if (d.Skins == null) d.Skins = new Dictionary<string, ulong>();
            return d;
        }

        #endregion

        #region Общее

        private static ulong Id(BasePlayer p) => (ulong)p.userID;

        private bool IsAdmin(BasePlayer p) => p != null && (p.IsAdmin || permission.UserHasPermission(p.UserIDString, PermAdmin));

        private bool IsVip(string userId) => permission.UserHasPermission(userId, PermVip);

        private Offer OfferById(string id) => _config.Offers.FirstOrDefault(o => o.Id == id);

        private int Coins(ulong id) => Hub == null ? 0 : Convert.ToInt32(Hub.Call("GetCoins", id) ?? 0);

        private string[] PermsOf(Offer o) => string.IsNullOrEmpty(o.Perms) ? new string[0] : o.Perms.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

        private bool Owns(BasePlayer p, Offer o)
        {
            if (!string.IsNullOrEmpty(o.Perms)) return PermsOf(o).All(x => permission.UserHasPermission(p.UserIDString, x));
            if (o.Price <= 0 && !o.VipOnly) return true;
            if (_config.VipFreeCosmetics && IsVip(p.UserIDString) && (o.Cat == "title" || o.Cat == "color")) return true;
            return D(Id(p)).Owned.Contains(o.Id);
        }

        private bool CanBuy(BasePlayer p, Offer o) => !o.VipOnly || IsVip(p.UserIDString);

        private readonly Dictionary<ulong, string> _tab = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, int> _page = new Dictionary<ulong, int>();

        #endregion

        #region Хуки

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermVip, this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            RegisterLang();
            InitRoundedSprite();
            timer.Every(120f, () => { if (_dirty) SaveData(); });
        }

        private void Unload()
        {
            foreach (var p in BasePlayer.activePlayerList) { CuiHelper.DestroyUi(p, UiMain); CuiHelper.DestroyUi(p, UiMain + ".dim"); CuiHelper.DestroyUi(p, UiToast); }
            SaveData();
        }

        private void OnServerSave() { if (_dirty) SaveData(); }

        // выбранный скин надевается на любой предмет этого типа в инвентаре игрока
        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (item == null || item.info == null || item.skin != 0UL || SkinnerOn) return;
            var player = container?.playerOwner;
            if (player == null || (ulong)player.userID < 76561197960265728UL) return;
            PData d;
            ulong skin;
            if (!_players.TryGetValue(Id(player), out d) || !d.Skins.TryGetValue(item.info.shortname, out skin) || skin == 0UL) return;
            item.skin = skin;
            var held = item.GetHeldEntity();
            if (held != null) { held.skinID = skin; held.SendNetworkUpdate(); }
            item.MarkDirty();
        }

        // VIP: вход без очереди
        private object CanBypassQueue(Network.Connection connection)
        {
            if (!_config.VipSkipQueue || connection == null) return null;
            return IsVip(connection.userid.ToString()) ? (object)true : null;
        }

        // чат: [титул] [клан] [ранг] ник: сообщение
        private object OnPlayerChat(BasePlayer player, string message, ConVar.Chat.ChatChannel channel)
        {
            if (!_config.ChatFormat || player == null || channel != ConVar.Chat.ChatChannel.Global) return null;
            var id = Id(player);
            var d = D(id);
            var parts = new List<string>();

            // титул
            if (d.Title == "vip" && IsVip(player.UserIDString)) parts.Add($"<color={_config.VipColor}>[{_config.VipTitle}]</color>");
            else if (!string.IsNullOrEmpty(d.Title) && d.Title.StartsWith("season:")) parts.Add($"<color=#e7c46a>[{Esc(d.Title.Substring(7))}]</color>");
            else
            {
                var t = OfferById(d.Title ?? "");
                if (t != null && t.Cat == "title" && Owns(player, t)) parts.Add($"<color={t.Color ?? "#f0a6b1"}>[{Esc(t.Title)}]</color>");
            }
            // клан
            if (_config.ChatClan && Social != null)
            {
                var tag = Social.Call("GetClanTag", id) as string;
                if (!string.IsNullOrEmpty(tag)) parts.Add($"<color=#a39896>[{Esc(tag)}]</color>");
            }
            // ранг
            if (_config.ChatRank && !d.HideRank && Ranked != null)
            {
                var rank = Ranked.Call("GetBestRank", id) as string;
                if (!string.IsNullOrEmpty(rank)) parts.Add($"<color={RankColor(rank)}>{rank}</color>");
            }
            // цвет ника
            string nameColor = player.IsAdmin ? "#aaff55" : "#55aaff";
            var c = OfferById(d.Color ?? "");
            if (c != null && c.Cat == "color" && Owns(player, c)) nameColor = c.Color;

            string line = (parts.Count > 0 ? string.Join(" ", parts.ToArray()) + " " : "") + $"<color={nameColor}>{Esc(player.displayName)}</color>: {Esc(message)}";
            Server.Broadcast(line, Id(player));
            Puts($"[chat] {player.displayName}: {message}");
            return true;
        }

        private static string RankColor(string rank)
        {
            switch (rank)
            {
                case "Бронза": return "#b07a4f";
                case "Серебро": return "#c9cdd3";
                case "Золото": return "#e7c46a";
                case "Платина": return "#7fd8d0";
                case "Алмаз": return "#8fb8ff";
                case "Мастер": return "#c58cff";
                case "Легенда": return "#ff6b7f";
                default: return "#a39896";
            }
        }

        private static string Esc(string s) => (s ?? "").Replace("<", "‹").Replace(">", "›");

        #endregion

        #region Покупка и выбор

        private void Buy(BasePlayer player, string offerId)
        {
            var id = Id(player);
            var o = OfferById(offerId);
            if (o == null || !o.Enabled) return;
            if (Owns(player, o)) { Select(player, offerId); return; }
            if (!CanBuy(player, o)) { Toast(player, T(id, "VipOnly"), true); return; }
            if (Hub == null) { Toast(player, T(id, "NoHub"), true); return; }
            int before = Coins(id);
            if (before < o.Price) { Toast(player, T(id, "NoCoins", o.Price - before), true); return; }
            var ok = Hub.Call("TakeCoins", id, o.Price);
            if (!(ok is bool) || !(bool)ok) { Toast(player, T(id, "NoCoins", o.Price - before), true); return; }
            D(id).Owned.Add(o.Id);
            foreach (var perm in PermsOf(o)) permission.GrantUserPermission(player.UserIDString, perm, null);
            _dirty = true;
            SaveData();
            Toast(player, T(id, "Bought", o.Name, o.Price), false);
            Select(player, offerId);
        }

        private void Select(BasePlayer player, string offerId)
        {
            var id = Id(player);
            var d = D(id);
            if (offerId == "vip") { d.Title = IsVip(player.UserIDString) ? "vip" : null; _dirty = true; OpenShop(player); return; }
            if (offerId.StartsWith("season:")) { d.Title = offerId; _dirty = true; OpenShop(player); return; }
            var o = OfferById(offerId);
            if (o == null || !Owns(player, o)) return;
            if (!string.IsNullOrEmpty(o.Perms)) { Toast(player, T(id, "PerkHint", o.Desc ?? ""), false); return; }
            switch (o.Cat)
            {
                case "title": d.Title = d.Title == o.Id ? null : o.Id; break;
                case "color": d.Color = d.Color == o.Id ? null : o.Id; break;
                default:
                    ulong cur;
                    if (d.Skins.TryGetValue(o.Item, out cur) && cur == o.Skin) d.Skins.Remove(o.Item);   // повторный клик — снять
                    else
                    {
                        d.Skins[o.Item] = o.Skin;
                        ApplyNow(player, o.Item, o.Skin);
                    }
                    break;
            }
            _dirty = true;
            OpenShop(player);
        }

        // перекрасить то, что уже в инвентаре
        private void ApplyNow(BasePlayer player, string shortname, ulong skin)
        {
            foreach (var c in new[] { player.inventory.containerBelt, player.inventory.containerMain, player.inventory.containerWear })
            {
                if (c?.itemList == null) continue;
                foreach (var item in c.itemList)
                {
                    if (item?.info == null || item.info.shortname != shortname) continue;
                    item.skin = skin;
                    var held = item.GetHeldEntity();
                    if (held != null) { held.skinID = skin; held.SendNetworkUpdate(); }
                    item.MarkDirty();
                }
            }
        }

        private bool IsSelected(BasePlayer player, Offer o)
        {
            var d = D(Id(player));
            switch (o.Cat)
            {
                case "title": return d.Title == o.Id;
                case "color": return d.Color == o.Id;
                default: ulong s; return d.Skins.TryGetValue(o.Item ?? "", out s) && s == o.Skin;
            }
        }

        #endregion

        #region Команды

        [ChatCommand("shop")]
        private void CmdShop(BasePlayer player, string c, string[] a) => OpenShop(player);

        [ConsoleCommand("shop.ui")]
        private void CcUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs()) return;
            var id = Id(player);
            string a1 = arg.GetString(1);
            switch (arg.GetString(0))
            {
                case "close": DestroyWindow(player, UiMain); break;
                case "tab": _tab[id] = a1; _page[id] = 0; OpenShop(player); break;
                case "page": { int p; _page.TryGetValue(id, out p); _page[id] = Mathf.Max(0, p + int.Parse(a1)); OpenShop(player); break; }
                case "buy": Buy(player, a1); break;
                case "select": Select(player, string.Join(" ", arg.Args.Skip(1).ToArray())); break;
                case "hiderank": D(id).HideRank = !D(id).HideRank; _dirty = true; OpenShop(player); break;
            }
        }

        [ChatCommand("shopadmin")]
        private void CmdAdmin(BasePlayer player, string c, string[] a)
        {
            var id = Id(player);
            if (!IsAdmin(player)) { Reply(player, T(id, "NoPerm")); return; }
            if (a.Length == 0) { Reply(player, T(id, "AdminHelp")); return; }
            switch (a[0].ToLower())
            {
                case "addheld":
                {
                    var item = player.GetActiveItem();
                    if (item == null || a.Length < 2) { Reply(player, "/shopadmin addheld <цена> [название] — возьми предмет со скином в руку"); return; }
                    int price; if (!int.TryParse(a[1], out price)) return;
                    string cat = item.info.category == ItemCategory.Attire ? (item.info.shortname.Contains("facemask") || item.info.shortname.Contains("plate") || item.info.shortname.Contains("roadsign") || item.info.shortname.Contains("armor") ? "armor" : "clothing") : "weapon";
                    var name = a.Length > 2 ? string.Join(" ", a.Skip(2).ToArray()) : item.info.displayName.english + " · " + item.skin;
                    var o = new Offer { Id = item.info.shortname + "_" + item.skin, Cat = cat, Name = name, Item = item.info.shortname, Skin = item.skin, Price = price };
                    _config.Offers.RemoveAll(x => x.Id == o.Id);
                    _config.Offers.Add(o);
                    SaveConfig();
                    Reply(player, T(id, "AdmAdded", o.Name, o.Id));
                    return;
                }
                case "add":
                {
                    if (a.Length < 5) { Reply(player, "/shopadmin add <shortname> <skinId> <цена> <название>"); return; }
                    ulong skin; int price;
                    if (!ulong.TryParse(a[2], out skin) || !int.TryParse(a[3], out price)) return;
                    var def = ItemManager.FindItemDefinition(a[1]);
                    if (def == null) { Reply(player, T(id, "NoItem", a[1])); return; }
                    string cat = def.category == ItemCategory.Attire ? "clothing" : (def.category == ItemCategory.Weapon ? "weapon" : "armor");
                    var o = new Offer { Id = a[1] + "_" + skin, Cat = cat, Name = string.Join(" ", a.Skip(4).ToArray()), Item = a[1], Skin = skin, Price = price };
                    _config.Offers.RemoveAll(x => x.Id == o.Id);
                    _config.Offers.Add(o);
                    SaveConfig();
                    Reply(player, T(id, "AdmAdded", o.Name, o.Id));
                    return;
                }
                case "remove":
                    if (a.Length > 1 && _config.Offers.RemoveAll(x => x.Id == a[1]) > 0) { SaveConfig(); Reply(player, T(id, "AdmRemoved")); }
                    return;
                case "list":
                    Reply(player, string.Join("\n", _config.Offers.Select(o => $"{o.Id} [{o.Cat}] {o.Name} — {o.Price}{(o.Enabled ? "" : " (выкл)")}").ToArray()));
                    return;
                case "coins":
                {
                    if (a.Length < 3 || Hub == null) return;
                    var t = BasePlayer.activePlayerList.FirstOrDefault(p => p.displayName.IndexOf(a[1], StringComparison.OrdinalIgnoreCase) >= 0);
                    int n;
                    if (t == null || !int.TryParse(a[2], out n)) return;
                    Hub.Call("AddCoins", Id(t), n, "admin");
                    Reply(player, $"{t.displayName}: {Coins(Id(t))}");
                    return;
                }
                default: Reply(player, T(id, "AdminHelp")); return;
            }
        }

        private void Reply(BasePlayer player, string msg) => player.ChatMessage($"<color=#f0a6b1>[Shop]</color> {msg}");

        #endregion

        #region Интерфейс

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

        private static string Nice(string t)
        {
            if (string.IsNullOrEmpty(t) || t.Length < 5 || t != t.ToUpperInvariant() || t == t.ToLowerInvariant()) return t;
            return t.Substring(0, 1) + t.Substring(1).ToLowerInvariant();
        }

        private static void Btn(CuiElementContainer c, string parent, string text, string cmd, string color, int size, float x, float y, float w, float h,
            string textColor = ColText, float r = 12f)
        {
            if (r >= 10f) r = h <= 40f ? h / 2f : 14f;
            bool round = _roundPng != null && r > 0f;
            if (round) Box(c, parent, color, x, y, w, h, r);
            c.Add(new CuiButton
            {
                Button = { Command = cmd, Color = round ? "0 0 0 0" : color },
                Text = { Text = Nice(text), FontSize = size, Align = TextAnchor.MiddleCenter, Color = textColor, Font = Font },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - h}", OffsetMax = $"{x + w} {-y}" }
            }, parent);
        }

        private static void ItemIcon(CuiElementContainer c, string parent, int itemId, ulong skin, float x, float y, float s)
        {
            c.Add(new CuiElement
            {
                Parent = parent,
                Components =
                {
                    new CuiImageComponent { ItemId = itemId, SkinId = skin },
                    new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - s}", OffsetMax = $"{x + s} {-y}" }
                }
            });
        }

        // шапка в стиле VideoRecorder: винная плитка с иконкой, круглая кнопка закрытия
        private string IconPng(string n) { string v = null; if (string.IsNullOrEmpty(n)) return null; v = plugins.Find("Hub")?.Call("GetIconPng", n) as string; return v; }

        private void HeadIcon(CuiElementContainer c, string parent, string icon)
        {
            Box(c, parent, ColWine, M, M + 1, 40, 40, 12f);
            string png = (icon != null && icon.Length > 2 ? IconPng(icon) : null) ?? IconPng("shop");
            if (png == null) { Text(c, parent, icon, 14, M, M + 1, 40, 40, TextAnchor.MiddleCenter); return; }
            c.Add(new CuiElement { Parent = parent, Components = { new CuiRawImageComponent { Png = png, Color = "1 1 1 1" },
                new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{M + 10} {-M - 31}", OffsetMax = $"{M + 30} {-M - 11}" } } });
        }

        private void CloseBtn(CuiElementContainer c, string parent, string cmd, float x, float y)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            Box(c, parent, ColBtn, x, y, 34, 34, 10f);
            string png = IconPng("close");
            if (png != null) c.Add(new CuiElement { Parent = parent, Components = { new CuiRawImageComponent { Png = png, Color = "0.937 0.914 0.906 1" },
                new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x + 9} {-y - 25}", OffsetMax = $"{x + 25} {-y - 9}" } } });
            c.Add(new CuiButton { Button = { Command = cmd, Color = "0 0 0 0" }, Text = { Text = png == null ? "×" : "", FontSize = 18, Align = TextAnchor.MiddleCenter, Color = "0.937 0.914 0.906 1" },
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - 34}", OffsetMax = $"{x + 34} {-y}" } }, parent);
        }

        private void DestroyWindow(BasePlayer p, string name) { CuiHelper.DestroyUi(p, name + ".dim"); CuiHelper.DestroyUi(p, name); }

        private static string HexToRgba(string hex, float a = 1f)
        {
            try
            {
                hex = (hex ?? "#ffffff").TrimStart('#');
                int r = Convert.ToInt32(hex.Substring(0, 2), 16), g = Convert.ToInt32(hex.Substring(2, 2), 16), b = Convert.ToInt32(hex.Substring(4, 2), 16);
                return $"{r / 255f:0.###} {g / 255f:0.###} {b / 255f:0.###} {a}";
            }
            catch { return "1 1 1 " + a; }
        }

        private const float M = 18f, G = 10f;

        private void OpenShop(BasePlayer player)
        {
            var id = Id(player);
            string tab; if (!_tab.TryGetValue(id, out tab) || Array.IndexOf(Cats, tab) < 0) tab = Cats[0];
            int page; _page.TryGetValue(id, out page);
            const float W = 1000f, H = 660f;
            var c = new CuiElementContainer();
            c.Add(new CuiPanel { Image = { Color = "0.03 0.02 0.02 0.5" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", UiMain + ".dim");
            Root(c, UiMain, UiMain + ".dim", ColBg, "0.5 0.5", "0.5 0.5", $"{-W / 2f} {-H / 2f}", $"{W / 2f} {H / 2f}", 22f, true);
            var R = UiMain;
            HeadIcon(c, R, "$");
            Text(c, R, Nice(T(id, "Title")), 18, M + 52, M, W - 2 * (M + 52), 22, TextAnchor.MiddleLeft);
            Text(c, R, T(id, "Sub"), 12, M + 52, M + 22, W - 2 * (M + 52), 16, TextAnchor.MiddleLeft, ColDim, FontReg);
            CloseBtn(c, R, "shop.ui close", W - M - 36, M + 3);

            float top = M + 52 + 2 + M;
            // вкладки + монеты
            const float coinW = 150f;
            float tw = (W - 2 * M - coinW - G - (Cats.Length - 1) * 6f) / Cats.Length;
            for (int i = 0; i < Cats.Length; i++)
            {
                bool a = Cats[i] == tab;
                Btn(c, R, T(id, "Cat_" + Cats[i]), "shop.ui tab " + Cats[i], a ? ColWine : ColTab, 12, M + i * (tw + 6), top, tw, 36, a ? ColText : ColDim, 11f);
            }
            Box(c, R, "0.91 0.77 0.42 0.12", W - M - coinW, top, coinW, 36, 11f);
            Text(c, R, T(id, "Coins", Coins(id)), 15, W - M - coinW, top, coinW, 36, TextAnchor.MiddleCenter, ColGold);
            top += 36 + G;

            float gridH = H - M - 40 - G - top;
            if (tab == "vip") DrawVip(c, player, top, W, gridH);
            else
            {
                var offers = _config.Offers.Where(o => o.Enabled && o.Cat == tab).ToList();
                const int cols = 4, rows = 2;
                int per = cols * rows, pages = Mathf.Max(1, Mathf.CeilToInt(offers.Count / (float)per));
                page = Mathf.Clamp(page, 0, pages - 1); _page[id] = page;
                float cw = (W - 2 * M - (cols - 1) * G) / cols, ch = (gridH - (rows - 1) * G) / rows;
                var show = offers.Skip(page * per).Take(per).ToList();
                for (int i = 0; i < show.Count; i++)
                {
                    var o = show[i];
                    float x = M + (i % cols) * (cw + G), y = top + (i / cols) * (ch + G);
                    bool own = Owns(player, o), sel = own && string.IsNullOrEmpty(o.Perms) && IsSelected(player, o);
                    Box(c, R, sel ? ColWineSoft : ColPanel, x, y, cw, ch, 16f);
                    float art = ch - 98;
                    Box(c, R, "0.620 0.082 0.188 0.22", x + 10, y + 10, cw - 20, art, 12f);
                    if (o.Cat == "title")
                        Text(c, R, $"[{o.Title}]", 20, x + 10, y + 10, cw - 20, art, TextAnchor.MiddleCenter, HexToRgba(o.Color));
                    else if (o.Cat == "color")
                        Text(c, R, player.displayName, 20, x + 10, y + 10, cw - 20, art, TextAnchor.MiddleCenter, HexToRgba(o.Color));
                    else
                    {
                        var def = ItemManager.FindItemDefinition(o.Item);
                        float s = Mathf.Min(art - 16, cw - 40);
                        if (def != null) ItemIcon(c, R, def.itemid, o.Skin, x + cw / 2f - s / 2f, y + 10 + (art - s) / 2f, s);
                    }
                    Text(c, R, o.Name, 13, x + 12, y + 16 + art, cw - 24, 22);
                    Text(c, R, o.VipOnly ? T(id, "VipTag") : !string.IsNullOrEmpty(o.Desc) ? o.Desc : (o.Cat == "title" ? T(id, "TitleHint") : o.Cat == "color" ? T(id, "ColorHint") : T(id, "SkinHint")), 10, x + 12, y + 38 + art, cw - 24, 16, TextAnchor.MiddleLeft, o.VipOnly ? ColGold : ColDim, FontReg);
                    string label, cmd, col;
                    if (sel) { label = T(id, "Selected"); cmd = "shop.ui select " + o.Id; col = "1 1 1 0.10"; }
                    else if (own && !string.IsNullOrEmpty(o.Perms)) { label = T(id, "PerkOwned"); cmd = "shop.ui select " + o.Id; col = "1 1 1 0.10"; }
                    else if (own) { label = T(id, "Use"); cmd = "shop.ui select " + o.Id; col = ColBtn; }
                    else if (!CanBuy(player, o)) { label = T(id, "VipOnlyBtn"); cmd = ""; col = ColBtn; }
                    else { label = o.Price <= 0 ? T(id, "GetFree") : T(id, "BuyFor", o.Price); cmd = "shop.ui buy " + o.Id; col = ColWine; }
                    Btn(c, R, label, cmd, col, 12, x + 10, y + ch - 42, cw - 20, 32, ColText, 12f);
                }
                if (offers.Count == 0) Text(c, R, T(id, tab == "weapon" || tab == "armor" || tab == "clothing" ? "EmptySkins" : "Empty"), 13, M, top, W - 2 * M, gridH, TextAnchor.MiddleCenter, ColDim, FontReg);

                // страницы
                float py = H - M - 40;
                Box(c, R, ColPanel, M, py, W - 2 * M, 40, 12f);
                Text(c, R, T(id, "Footer_" + tab), 11, M + 14, py, W / 2f - 120, 40, TextAnchor.MiddleLeft, ColDim, FontReg);
                Btn(c, R, "<", "shop.ui page -1", pages > 1 ? ColBtn : ColRow, 13, W / 2f - 64, py + 5, 30, 30, ColText, 10f);
                Text(c, R, $"{page + 1} / {pages}", 12, W / 2f - 30, py, 60, 40, TextAnchor.MiddleCenter);
                Btn(c, R, ">", "shop.ui page 1", pages > 1 ? ColBtn : ColRow, 13, W / 2f + 34, py + 5, 30, 30, ColText, 10f);
                var d = D(id);
                Btn(c, R, d.HideRank ? T(id, "RankHidden") : T(id, "RankShown"), "shop.ui hiderank", ColBtn, 11, W - M - 210, py + 5, 196, 30, ColDim, 10f);
            }

            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        private void DrawVip(CuiElementContainer c, BasePlayer player, float top, float W, float h)
        {
            var id = Id(player);
            var R = UiMain;
            bool vip = IsVip(player.UserIDString);
            float cw = (W - 2 * M - G) / 2f;
            Box(c, R, vip ? "0.91 0.77 0.42 0.12" : ColPanel, M, top, cw, h, 16f);
            Text(c, R, vip ? T(id, "VipActive") : T(id, "VipTitle"), 22, M, top + 16, cw, 32, TextAnchor.MiddleCenter, ColGold);
            Text(c, R, T(id, "VipPerks"), 13, M + 24, top + 60, cw - 48, h - 140, TextAnchor.UpperLeft, ColText, FontReg);
            Text(c, R, T(id, "VipHow"), 11, M + 24, top + h - 74, cw - 48, 60, TextAnchor.LowerCenter, ColDim, FontReg);

            float rx = M + cw + G;
            Box(c, R, ColPanel, rx, top, cw, h, 16f);
            Text(c, R, T(id, "SeasonTitles"), 10, rx + 14, top + 10, cw - 28, 16, TextAnchor.MiddleLeft, ColDim);
            var titles = new List<string>();
            if (vip) titles.Add("vip");
            if (Ranked != null)
            {
                // титулы сезонов из Ranked
                var seasonTitles = Ranked.Call("GetTitles", id) as List<string>;
                if (seasonTitles != null) titles.AddRange(seasonTitles.Select(t => "season:" + t));
            }
            if (Hub != null)
            {
                // титулы за достижения из Hub (показываются так же, как сезонные)
                var achTitles = Hub.Call("GetAchTitles", id) as List<string>;
                if (achTitles != null) titles.AddRange(achTitles.Select(t => "season:" + t).Where(t => !titles.Contains(t)));
            }
            var d = D(id);
            for (int i = 0; i < titles.Count && i < 8; i++)
            {
                var t = titles[i];
                bool sel = d.Title == t;
                string label = t == "vip" ? $"[{_config.VipTitle}]" : $"[{t.Substring(7)}]";
                Box(c, R, sel ? ColWineSoft : ColRow, rx + 12, top + 34 + i * 44, cw - 24, 38, 12f);
                Text(c, R, label, 13, rx + 24, top + 34 + i * 44, cw - 150, 38, TextAnchor.MiddleLeft, t == "vip" ? HexToRgba(_config.VipColor) : ColGold);
                Btn(c, R, sel ? T(id, "Selected") : T(id, "Use"), "shop.ui select " + t, sel ? "1 1 1 0.10" : ColBtn, 11, rx + cw - 120, top + 40 + i * 44, 96, 26, ColText, 10f);
            }
            if (titles.Count == 0) Text(c, R, T(id, "NoSeasonTitles"), 12, rx, top, cw, h, TextAnchor.MiddleCenter, ColDim, FontReg);
        }

        private void Toast(BasePlayer player, string text, bool warn)
        {
            if (player == null || !player.IsConnected) return;
            var c = new CuiElementContainer();
            Root(c, UiToast, "Overlay", warn ? "0.40 0.06 0.11 0.95" : "0.07 0.065 0.065 0.95", "0.5 1", "0.5 1", "-240 -76", "240 -34", 20f, false);
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
            PData d;
            if (!_players.TryGetValue(userId, out d) || d.Owned.Count == 0 && string.IsNullOrEmpty(d.Title)) return null;
            bool en = lng == "en";
            var t = OfferById(d.Title ?? "");
            string title = d.Title == "vip" ? _config.VipTitle : d.Title != null && d.Title.StartsWith("season:") ? d.Title.Substring(7) : t?.Title;
            int skins = d.Owned.Count(x => { var o = OfferById(x); return o != null && o.Cat != "title" && o.Cat != "color"; });
            return en ? $"<color=#f0a6b1>SHOP</color>\nTitle: {title ?? "—"} · skins owned: {skins}" : $"<color=#f0a6b1>МАГАЗИН</color>\nТитул: {title ?? "—"} · скинов куплено: {skins}";
        }

        [HookMethod("GetSkin")]
        public ulong GetSkin(ulong userId, string shortname)
        {
            PData d; ulong s;
            return _players.TryGetValue(userId, out d) && d.Skins.TryGetValue(shortname, out s) ? s : 0UL;
        }

        [HookMethod("IsVip")] public bool IsVipApi(ulong userId) => IsVip(userId.ToString());

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
            new[] { "Title", "МАГАЗИН", "SHOP" }, new[] { "Sub", "скины работают на всех аренах · /shop", "skins work in every mode · /shop" },
            new[] { "Cat_skins", "Скины", "Skins" },
            new[] { "Footer_skins", "Скины выбираешь сам: /skin с предметом в руке. Покупка — навсегда.", "Pick skins yourself: /skin with the item in hand. Purchases are permanent." },
            new[] { "PerkOwned", "КУПЛЕНО", "OWNED" },
            new[] { "GetFree", "ПОЛУЧИТЬ", "GET" },
            new[] { "PerkHint", "Уже твоё. {0}", "Already yours. {0}" },
            new[] { "Cat_weapon", "Оружие", "Weapons" }, new[] { "Cat_armor", "Броня", "Armor" }, new[] { "Cat_clothing", "Одежда", "Clothing" },
            new[] { "Cat_title", "Титулы", "Titles" }, new[] { "Cat_color", "Цвет ника", "Name color" }, new[] { "Cat_vip", "VIP", "VIP" },
            new[] { "Coins", "{0} монет", "{0} coins" },
            new[] { "BuyFor", "КУПИТЬ · {0}", "BUY · {0}" }, new[] { "Use", "ВЫБРАТЬ", "USE" }, new[] { "Selected", "ВЫБРАН", "SELECTED" },
            new[] { "VipOnlyBtn", "ТОЛЬКО VIP", "VIP ONLY" }, new[] { "VipTag", "только для VIP", "VIP only" },
            new[] { "SkinHint", "надевается сам на всех аренах", "applied automatically everywhere" },
            new[] { "TitleHint", "перед ником в чате", "shown before your name" }, new[] { "ColorHint", "цвет ника в чате", "your name color in chat" },
            new[] { "Footer_weapon", "Скин надевается на это оружие везде, где ты его получаешь.", "The skin is applied wherever you get this weapon." },
            new[] { "Footer_armor", "Скин брони работает во всех наборах.", "Armor skins work in every kit." },
            new[] { "Footer_clothing", "Скин одежды работает во всех наборах.", "Clothing skins work in every kit." },
            new[] { "Footer_title", "Повторный клик по выбранному титулу — снять.", "Click a selected title again to remove it." },
            new[] { "Footer_color", "Повторный клик — вернуть обычный цвет.", "Click again to reset your color." },
            new[] { "RankShown", "Ранг в чате: показан", "Rank in chat: shown" }, new[] { "RankHidden", "Ранг в чате: скрыт", "Rank in chat: hidden" },
            new[] { "Empty", "Здесь пока пусто", "Nothing here yet" },
            new[] { "EmptySkins", "Скинов пока нет. Админ: возьми предмет со скином в руку и напиши /shopadmin addheld <цена>", "No skins yet. Admin: hold a skinned item and type /shopadmin addheld <price>" },
            new[] { "NoCoins", "Не хватает {0} монет", "You need {0} more coins" },
            new[] { "Bought", "Куплено: {0} (−{1})", "Bought: {0} (−{1})" },
            new[] { "VipOnly", "Это только для VIP", "VIP only" }, new[] { "NoHub", "Нужен плагин Hub (монеты)", "Hub plugin required" },
            new[] { "VipTitle", "VIP", "VIP" }, new[] { "VipActive", "У ТЕБЯ VIP", "YOU ARE VIP" },
            new[] { "VipPerks", "• Вход на сервер без очереди\n• Титул [VIP] в чате\n• Все титулы и цвета ника бесплатно\n• Эксклюзивный цвет «VIP-золото»\n• Светящиеся глаза (/eyes)\n\nVIP — только косметика и удобство, никаких преимуществ в бою.", "• Skip the server queue\n• [VIP] title in chat\n• All titles and name colors for free\n• Exclusive VIP gold color\n\nVIP is cosmetic only — no combat advantage." },
            new[] { "VipHow", "Как получить VIP — смотри в Discord сервера", "See the server Discord for how to get VIP" },
            new[] { "SeasonTitles", "ТВОИ ОСОБЫЕ ТИТУЛЫ", "YOUR SPECIAL TITLES" },
            new[] { "NoSeasonTitles", "Титулы за топ-3 сезона ранкеда появятся здесь", "Season top-3 titles will appear here" },
            new[] { "NoPerm", "Нет прав", "No permission" }, new[] { "NoItem", "Нет предмета {0}", "No item {0}" },
            new[] { "AdmAdded", "Добавлено: {0} ({1})", "Added: {0} ({1})" }, new[] { "AdmRemoved", "Удалено", "Removed" },
            new[] { "AdminHelp",
                "<color=#f0a6b1>/shopadmin</color>\n" +
                "addheld <цена> [название] — добавить скин предмета, который держишь в руке\n" +
                "add <shortname> <skinId> <цена> <название> — добавить скин по ID мастерской\n" +
                "remove <id> · list · coins <ник> <кол-во>\n" +
                "Титулы и цвета — в конфиге. NPC: /hubadmin npc cmd:shop Торговец",
                "<color=#f0a6b1>/shopadmin</color> addheld <price> [name] · add <shortname> <skinId> <price> <name> · remove <id> · list · coins <name> <n>" },
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
