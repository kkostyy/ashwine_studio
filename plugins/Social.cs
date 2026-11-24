// =====================================================================================
//  Social 1.0 — пати, кланы, турниры и режим зрителя для хаб-сервера Rust (Oxide / Carbon)
// =====================================================================================
//  Пати — это обычная команда Rust (её же использует Ranked для очереди командой):
//  приглашения из меню, «идти за лидером» при смене режима.
//  Кланы: тег 2–5 символов, офицеры, приглашения, клановый чат /c, клановый рейтинг
//  (средний лучший MMR пяти сильнейших в Ranked), топ кланов. Тег виден в чате (Shop).
//  Турниры 1v1: регистрация, сетка на 4–32, матчи на аренах Arenas, техническое поражение
//  за неявку, призы монетами. Режим зрителя /spec — смотреть любой идущий матч.
//
//  Права:  social.admin — турниры, кланы
//  Чат:    /party · /clan · /c <сообщение> · /tour · /spec [ник]
//  Админ:  /tour create <название> <4|8|16|32> [набор] [раунды] [приз] · /tour start · /tour cancel
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
    [Info("Social", "Claude", "1.1.0")]
    [Description("Пати, кланы с рейтингом, турниры с сеткой и режим зрителя")]
    public class Social : RustPlugin
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
        [PluginReference] private Plugin Hub, Arenas, Ranked;

        #region Константы

        private const string PermAdmin = "social.admin";
        private const string UiMain = "So.Main";
        private const string UiPopup = "So.Popup";
        private const string UiToast = "So.Toast";
        private const string UiSpec = "So.Spec";

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

        #endregion

        #region Конфиг

        private class Configuration
        {
            [JsonProperty("Пати: максимум игроков")] public int PartyMax = 5;
            [JsonProperty("Пати: предлагать идти за лидером")] public bool FollowLeader = true;
            [JsonProperty("Клан: максимум участников")] public int ClanMax = 20;
            [JsonProperty("Клан: длина тега (от-до)")] public int[] TagLen = { 2, 5 };
            [JsonProperty("Клан: цена создания (монеты, 0 = бесплатно)")] public int ClanPrice = 500;
            [JsonProperty("Турнир: ждать игрока на матч (сек), потом техпоражение")] public int NoShowSeconds = 90;
            [JsonProperty("Турнир: набор по умолчанию (ID набора Arenas)")] public string TourLoadout = "ak";
            [JsonProperty("Турнир: побед в матче по умолчанию")] public int TourRounds = 3;
            [JsonProperty("Турнир: доля приза за 2-е место")] public float SecondShare = 0.3f;
            [JsonProperty("Зритель: только матчи (иначе любого игрока)")] public bool SpecOnlyMatches = true;
            [JsonProperty("Скруглённые углы интерфейса")] public bool RoundedCorners = true;
        }

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

        #region Данные

        private class Clan
        {
            public string Tag, Name;
            public ulong Owner;
            public HashSet<ulong> Officers = new HashSet<ulong>();
            public List<ulong> Members = new List<ulong>();
            public string Created;
            public int TourWins;
        }

        private class TMatch
        {
            public ulong A, B, Winner;
            public string State = "pending";   // pending | live | done
            public string Score;
            public float WaitSince;
        }

        private class Tournament
        {
            public string Name;
            public int Size = 8;
            public string Loadout = "ak";
            public int Rounds = 3;
            public int Prize;
            public string State = "registration";   // registration | running | finished
            public List<ulong> Players = new List<ulong>();   // в командном турнире — капитаны
            public List<List<TMatch>> Bracket = new List<List<TMatch>>();
            public ulong Champion, Second;
            public int TeamSize = 1;                          // 1 · 2 · 3 · 5
            public bool ClanOnly;                             // команда — только из одного клана
            public Dictionary<ulong, List<ulong>> Teams = new Dictionary<ulong, List<ulong>>();
        }

        private static List<ulong> Members(Tournament t, ulong captain)
        {
            List<ulong> l;
            return t.TeamSize > 1 && t.Teams != null && t.Teams.TryGetValue(captain, out l) ? l : new List<ulong> { captain };
        }

        private ulong CaptainOf(Tournament t, ulong player)
        {
            if (t.TeamSize <= 1 || t.Teams == null) return player;
            foreach (var kv in t.Teams) if (kv.Value.Contains(player)) return kv.Key;
            return 0UL;
        }

        private string EntryName(Tournament t, ulong captain)
        {
            if (captain == 0UL) return "?";
            if (t.TeamSize <= 1) return NameOf(captain);
            var clan = ClanOf(captain);
            if (t.ClanOnly && clan != null) return "[" + clan.Tag + "] " + clan.Name;
            return NameOf(captain) + " +" + (Members(t, captain).Count - 1);
        }

        private class StoredData
        {
            public Dictionary<string, Clan> Clans = new Dictionary<string, Clan>();
            public Dictionary<ulong, string> Names = new Dictionary<ulong, string>();
            public Tournament Tour;
            public List<string> History = new List<string>();
        }

        private StoredData _data;
        private readonly Dictionary<ulong, string> _clanOf = new Dictionary<ulong, string>();

        private void LoadData()
        {
            try { _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("Social"); } catch { _data = null; }
            if (_data == null) _data = new StoredData();
            if (_data.Clans == null) _data.Clans = new Dictionary<string, Clan>();
            if (_data.Names == null) _data.Names = new Dictionary<ulong, string>();
            if (_data.History == null) _data.History = new List<string>();
            _clanOf.Clear();
            foreach (var c in _data.Clans.Values) foreach (var m in c.Members) _clanOf[m] = c.Tag;
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("Social", _data);

        private string NameOf(ulong id)
        {
            var p = BasePlayer.FindByID(id);
            if (p != null) return p.displayName;
            string n;
            return _data.Names.TryGetValue(id, out n) ? n : id.ToString();
        }

        #endregion

        #region Общее

        private static ulong Id(BasePlayer p) => (ulong)p.userID;

        private bool IsAdmin(BasePlayer p) => p != null && (p.IsAdmin || permission.UserHasPermission(p.UserIDString, PermAdmin));

        private string ModeOf(BasePlayer p) => Hub != null ? (Hub.Call("GetMode", p) as string ?? "lobby") : "";

        private string ModeName(string mode, ulong viewer) => string.IsNullOrEmpty(mode) ? "" : T(viewer, "M_" + mode) == "M_" + mode ? mode : T(viewer, "M_" + mode);

        private readonly Dictionary<ulong, string> _tab = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> _input = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> _input2 = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> _clanInvites = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, ulong> _partyInvites = new Dictionary<ulong, ulong>();
        private readonly HashSet<ulong> _noFollow = new HashSet<ulong>();

        #endregion

        #region Хуки

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            RegisterLang();
            InitRoundedSprite();
            timer.Every(3f, TourTick);
            timer.Every(1f, SpecTick);
            timer.Every(300f, SaveData);
            foreach (var p in BasePlayer.activePlayerList) _data.Names[Id(p)] = p.displayName;
        }

        private void Unload()
        {
            foreach (var kv in _spec.ToList()) { var p = BasePlayer.FindByID(kv.Key); if (p != null) StopSpec(p); }
            foreach (var p in BasePlayer.activePlayerList)
                foreach (var ui in new[] { UiMain, UiPopup, UiToast, UiSpec }) { CuiHelper.DestroyUi(p, ui); CuiHelper.DestroyUi(p, ui + ".dim"); }
            SaveData();
        }

        private void OnPlayerConnected(BasePlayer player) { if (player != null) _data.Names[Id(player)] = player.displayName; }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            if (_spec.ContainsKey(Id(player))) StopSpec(player);
        }

        #endregion

        #region Пати (команда Rust)

        private RelationshipManager.PlayerTeam TeamOf(BasePlayer p)
        {
            try { return p.currentTeam == 0UL ? null : RelationshipManager.ServerInstance?.FindTeam(p.currentTeam); } catch { return null; }
        }

        private void PartyInvite(BasePlayer from, BasePlayer to)
        {
            var id = Id(from);
            if (to == null || to == from) return;
            var team = TeamOf(from);
            if (team != null && team.teamLeader != id) { Toast(from, T(id, "OnlyLeader"), true); return; }
            if (team != null && team.members.Count >= _config.PartyMax) { Toast(from, T(id, "PartyFull"), true); return; }
            if (TeamOf(to) != null) { Toast(from, T(id, "AlreadyInTeam", to.displayName), true); return; }
            _partyInvites[Id(to)] = id;
            Toast(from, T(id, "InviteSent", to.displayName), false);
            ShowInvite(to, T(Id(to), "PartyInviteTitle", from.displayName), T(Id(to), "PartyInviteSub"), "social.ui party.accept", "social.ui party.decline");
        }

        private void PartyAccept(BasePlayer player)
        {
            var id = Id(player);
            ulong fromId;
            if (!_partyInvites.TryGetValue(id, out fromId)) return;
            _partyInvites.Remove(id);
            DestroyWindow(player, UiPopup);
            var from = BasePlayer.FindByID(fromId);
            if (from == null) { Toast(player, T(id, "InviteGone"), true); return; }
            try
            {
                var team = TeamOf(from);
                if (team == null)
                {
                    team = RelationshipManager.ServerInstance.CreateTeam();
                    team.teamLeader = fromId;
                    team.AddPlayer(from);
                }
                if (team.members.Count >= _config.PartyMax) { Toast(player, T(id, "PartyFull"), true); return; }
                var mine = TeamOf(player);
                if (mine != null) mine.RemovePlayer(id);
                team.AddPlayer(player);
                foreach (var m in team.members) { var p = BasePlayer.FindByID(m); if (p != null) Toast(p, T(m, "Joined", player.displayName), false); }
            }
            catch (Exception ex) { PrintWarning("Пати: " + ex.Message); Toast(player, T(id, "TeamError"), true); }
        }

        private void PartyLeave(BasePlayer player)
        {
            var team = TeamOf(player);
            if (team == null) return;
            try { team.RemovePlayer(Id(player)); } catch { }
            Toast(player, T(Id(player), "PartyLeft"), false);
        }

        private void PartyKick(BasePlayer leader, ulong target)
        {
            var team = TeamOf(leader);
            if (team == null || team.teamLeader != Id(leader) || target == Id(leader)) return;
            try { team.RemovePlayer(target); } catch { }
            var p = BasePlayer.FindByID(target);
            if (p != null) Toast(p, T(target, "Kicked"), true);
        }

        // лидер сменил режим — предлагаем пойти за ним
        private void OnHubModeChanged(BasePlayer player, string from, string to)
        {
            if (!_config.FollowLeader || player == null || to == "lobby") return;
            var team = TeamOf(player);
            if (team == null || team.teamLeader != Id(player)) return;
            foreach (var m in team.members)
            {
                if (m == Id(player) || _noFollow.Contains(m)) continue;
                var p = BasePlayer.FindByID(m);
                if (p == null || !p.IsConnected || ModeOf(p) == to) continue;
                ShowInvite(p, T(m, "FollowTitle", player.displayName, ModeName(to, m)), T(m, "FollowSub"), "social.ui follow " + to, "social.ui popup.close");
            }
        }

        #endregion

        #region Кланы

        private Clan ClanOf(ulong id) { string tag; Clan c; return _clanOf.TryGetValue(id, out tag) && _data.Clans.TryGetValue(tag, out c) ? c : null; }

        private int ClanRating(Clan c)
        {
            if (Ranked == null) return 0;
            var mmrs = c.Members.Select(m => Convert.ToInt32(Ranked.Call("GetBestMmr", m) ?? 0)).Where(x => x > 0).OrderByDescending(x => x).Take(5).ToList();
            return mmrs.Count == 0 ? 0 : Mathf.RoundToInt((float)mmrs.Average());
        }

        private void CreateClan(BasePlayer player, string tag, string name)
        {
            var id = Id(player);
            if (ClanOf(id) != null) { Toast(player, T(id, "AlreadyClan"), true); return; }
            tag = (tag ?? "").Trim().ToUpper();
            name = (name ?? "").Trim();
            if (tag.Length < _config.TagLen[0] || tag.Length > _config.TagLen[1] || !tag.All(ch => char.IsLetterOrDigit(ch)))
            { Toast(player, T(id, "BadTag", _config.TagLen[0], _config.TagLen[1]), true); return; }
            if (_data.Clans.ContainsKey(tag)) { Toast(player, T(id, "TagTaken", tag), true); return; }
            if (string.IsNullOrEmpty(name)) name = tag;
            if (_config.ClanPrice > 0 && Hub != null)
            {
                var ok = Hub.Call("TakeCoins", id, _config.ClanPrice);
                if (!(ok is bool) || !(bool)ok) { Toast(player, T(id, "NoCoins", _config.ClanPrice), true); return; }
            }
            var c = new Clan { Tag = tag, Name = name.Length > 24 ? name.Substring(0, 24) : name, Owner = id, Created = DateTime.UtcNow.ToString("yyyy-MM-dd") };
            c.Members.Add(id);
            _data.Clans[tag] = c;
            _clanOf[id] = tag;
            SaveData();
            Toast(player, T(id, "ClanCreated", tag), false);
            OpenClan(player);
        }

        private void ClanInvite(BasePlayer from, BasePlayer to)
        {
            var id = Id(from);
            var c = ClanOf(id);
            if (c == null || to == null) return;
            if (c.Owner != id && !c.Officers.Contains(id)) { Toast(from, T(id, "OfficersOnly"), true); return; }
            if (c.Members.Count >= _config.ClanMax) { Toast(from, T(id, "ClanFull"), true); return; }
            if (ClanOf(Id(to)) != null) { Toast(from, T(id, "TargetHasClan", to.displayName), true); return; }
            _clanInvites[Id(to)] = c.Tag;
            Toast(from, T(id, "InviteSent", to.displayName), false);
            ShowInvite(to, T(Id(to), "ClanInviteTitle", c.Tag, c.Name), T(Id(to), "ClanInviteSub", from.displayName), "social.ui clan.accept", "social.ui clan.decline");
        }

        private void ClanAccept(BasePlayer player)
        {
            var id = Id(player);
            string tag;
            if (!_clanInvites.TryGetValue(id, out tag)) return;
            _clanInvites.Remove(id);
            DestroyWindow(player, UiPopup);
            Clan c;
            if (!_data.Clans.TryGetValue(tag, out c) || c.Members.Count >= _config.ClanMax || ClanOf(id) != null) return;
            c.Members.Add(id);
            _clanOf[id] = tag;
            SaveData();
            ClanBroadcast(c, T(0, "ClanJoined", player.displayName));
        }

        private void ClanLeave(BasePlayer player)
        {
            var id = Id(player);
            var c = ClanOf(id);
            if (c == null) return;
            if (c.Owner == id)
            {
                if (c.Members.Count > 1)
                {
                    var heir = c.Officers.FirstOrDefault(o => o != id);
                    c.Owner = heir != 0UL ? heir : c.Members.First(m => m != id);
                }
                else { _data.Clans.Remove(c.Tag); _clanOf.Remove(id); SaveData(); Toast(player, T(id, "ClanDisbanded"), false); return; }
            }
            c.Members.Remove(id);
            c.Officers.Remove(id);
            _clanOf.Remove(id);
            SaveData();
            ClanBroadcast(c, T(0, "ClanLeft", player.displayName));
            Toast(player, T(id, "YouLeftClan"), false);
        }

        private void ClanKick(BasePlayer by, ulong target)
        {
            var id = Id(by);
            var c = ClanOf(id);
            if (c == null || target == c.Owner || !c.Members.Contains(target)) return;
            if (c.Owner != id && !(c.Officers.Contains(id) && !c.Officers.Contains(target))) return;
            c.Members.Remove(target);
            c.Officers.Remove(target);
            _clanOf.Remove(target);
            SaveData();
            ClanBroadcast(c, T(0, "ClanKicked", NameOf(target)));
        }

        private void ClanPromote(BasePlayer by, ulong target)
        {
            var c = ClanOf(Id(by));
            if (c == null || c.Owner != Id(by) || !c.Members.Contains(target) || target == c.Owner) return;
            if (!c.Officers.Remove(target)) c.Officers.Add(target);
            SaveData();
        }

        private void ClanBroadcast(Clan c, string text)
        {
            foreach (var m in c.Members) { var p = BasePlayer.FindByID(m); if (p != null) p.ChatMessage($"<color=#f0a6b1>[{c.Tag}]</color> {text}"); }
        }

        // зрители не получают и не наносят урон
        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var victim = entity as BasePlayer;
            var attacker = info?.InitiatorPlayer;
            if (victim == null || attacker == null || victim == attacker) return null;
            if (_spec.ContainsKey(Id(victim)) || _spec.ContainsKey(Id(attacker))) return true;
            return null;
        }

        #endregion

        #region Турниры

        private void TourCreate(BasePlayer admin, string[] a)
        {
            var id = Id(admin);
            if (_data.Tour != null && _data.Tour.State != "finished") { Reply(admin, T(id, "TourExists")); return; }
            if (a.Length < 3) { Reply(admin, "/tour create <название> <4|8|16|32> [набор] [раунды] [приз] [1v1|2v2|3v3|5v5] [clan]"); return; }
            int size;
            if (!int.TryParse(a[2], out size) || !new[] { 4, 8, 16, 32 }.Contains(size)) { Reply(admin, "4 | 8 | 16 | 32"); return; }
            var t = new Tournament
            {
                Name = a[1].Replace("_", " "), Size = size,
                Loadout = a.Length > 3 ? a[3] : _config.TourLoadout,
                Rounds = a.Length > 4 ? int.Parse(a[4]) : _config.TourRounds,
                Prize = a.Length > 5 ? int.Parse(a[5]) : 0
            };
            if (a.Length > 6)
            {
                int ts;
                if (int.TryParse(a[6].Substring(0, 1), out ts) && new[] { 1, 2, 3, 5 }.Contains(ts)) t.TeamSize = ts;
            }
            t.ClanOnly = a.Length > 7 && a[7].ToLower() == "clan";
            _data.Tour = t;
            SaveData();
            foreach (var p in BasePlayer.activePlayerList) Toast(p, T(Id(p), t.TeamSize > 1 ? "TeamTourAnnounce" : "TourAnnounce", t.Name, t.Size, t.Prize, t.TeamSize), false);
        }

        private void TourJoin(BasePlayer player)
        {
            var id = Id(player);
            var t = _data.Tour;
            if (t == null || t.State != "registration") { Toast(player, T(id, "NoRegistration"), true); return; }
            if (t.TeamSize > 1) { TeamTourJoin(player, t); return; }
            if (t.Players.Contains(id)) { t.Players.Remove(id); Toast(player, T(id, "TourUnregistered"), false); }
            else if (t.Players.Count >= t.Size) { Toast(player, T(id, "TourFull"), true); return; }
            else { t.Players.Add(id); _data.Names[id] = player.displayName; Toast(player, T(id, "TourRegistered", t.Players.Count, t.Size), false); }
            SaveData();
            OpenTour(player);
        }

        // командная регистрация: капитан записывает всю пати (ровно TeamSize человек)
        private void TeamTourJoin(BasePlayer player, Tournament t)
        {
            var id = Id(player);
            if (t.Teams == null) t.Teams = new Dictionary<ulong, List<ulong>>();
            ulong cap = CaptainOf(t, id);
            if (cap != 0UL)
            {
                if (cap != id) { Toast(player, T(id, "OnlyCaptain"), true); return; }
                foreach (var m in t.Teams[cap]) { var p = BasePlayer.FindByID(m); if (p != null) Toast(p, T(m, "TourUnregistered"), false); }
                t.Teams.Remove(cap); t.Players.Remove(cap);
                SaveData(); OpenTour(player); return;
            }
            var team = TeamOf(player);
            if (team == null || team.teamLeader != id) { Toast(player, T(id, "NeedPartyLead", t.TeamSize), true); return; }
            var members = team.members.ToList();
            if (members.Count != t.TeamSize) { Toast(player, T(id, "NeedPartySize", t.TeamSize, members.Count), true); return; }
            if (members.Any(m => { var p = BasePlayer.FindByID(m); return p == null || !p.IsConnected; })) { Toast(player, T(id, "PartyOffline"), true); return; }
            if (members.Any(m => CaptainOf(t, m) != 0UL)) { Toast(player, T(id, "AlreadyInTeam"), true); return; }
            if (t.ClanOnly && members.Any(m => ClanOf(m) == null || ClanOf(m) != ClanOf(id))) { Toast(player, T(id, "NeedSameClan"), true); return; }
            if (t.Players.Count >= t.Size) { Toast(player, T(id, "TourFull"), true); return; }
            t.Teams[id] = members;
            t.Players.Add(id);
            foreach (var m in members)
            {
                var p = BasePlayer.FindByID(m);
                if (p != null) { _data.Names[m] = p.displayName; Toast(p, T(m, "TourRegistered", t.Players.Count, t.Size), false); }
            }
            SaveData();
            OpenTour(player);
        }

        private void TourStart(BasePlayer admin)
        {
            var t = _data.Tour;
            if (t == null || t.State != "registration") { Reply(admin, T(Id(admin), "NoRegistration")); return; }
            if (t.Players.Count < 2) { Reply(admin, T(Id(admin), "TourNeed2")); return; }
            if (Arenas == null) { Reply(admin, T(Id(admin), "NeedArenas")); return; }
            // размер сетки — ближайшая степень двойки, пустые места — проход дальше
            int size = 2;
            while (size < t.Players.Count) size *= 2;
            t.Size = size;
            var seed = t.Players.OrderBy(x => UnityEngine.Random.Range(0, 100000)).ToList();
            while (seed.Count < size) seed.Add(0UL);
            var first = new List<TMatch>();
            for (int i = 0; i < size / 2; i++) first.Add(new TMatch { A = seed[i], B = seed[size - 1 - i] });
            t.Bracket = new List<List<TMatch>> { first };
            for (int r = 1, n = size / 4; n >= 1; r++, n /= 2) t.Bracket.Add(Enumerable.Range(0, n).Select(_ => new TMatch()).ToList());
            t.State = "running";
            foreach (var m in first) if (m.A == 0UL || m.B == 0UL) FinishTMatch(t, 0, first.IndexOf(m), m.A == 0UL ? m.B : m.A, "—");
            SaveData();
            foreach (var p in BasePlayer.activePlayerList) Toast(p, T(Id(p), "TourStarted", t.Name), false);
        }

        private void TourTick()
        {
            var t = _data.Tour;
            if (t == null || t.State != "running" || Arenas == null) return;
            float now = Time.realtimeSinceStartup;
            for (int r = 0; r < t.Bracket.Count; r++)
                for (int i = 0; i < t.Bracket[r].Count; i++)
                {
                    var m = t.Bracket[r][i];
                    if (m.State != "pending" || m.A == 0UL || m.B == 0UL) continue;
                    var teamA = Members(t, m.A);
                    var teamB = Members(t, m.B);
                    System.Func<ulong, bool> ready = pid => { var p = BasePlayer.FindByID(pid); return p != null && p.IsConnected && !p.IsDead(); };
                    bool aOk = teamA.All(ready), bOk = teamB.All(ready);
                    if (m.WaitSince <= 0f) m.WaitSince = now;
                    if (!aOk || !bOk)
                    {
                        if (now - m.WaitSince > _config.NoShowSeconds)
                        {
                            if (aOk && !bOk) FinishTMatch(t, r, i, m.A, T(0, "NoShow"));
                            else if (bOk && !aOk) FinishTMatch(t, r, i, m.B, T(0, "NoShow"));
                            else FinishTMatch(t, r, i, UnityEngine.Random.Range(0, 2) == 0 ? m.A : m.B, T(0, "NoShow"));
                        }
                        continue;
                    }
                    var res = Arenas.Call("StartTournamentMatch", teamA.ToList(), teamB.ToList(), t.Loadout, t.Rounds, $"tour:{r}:{i}") as string;
                    if (res == null) continue;   // нет свободной арены — подождём
                    m.State = "live";
                    foreach (var pid in teamA.Concat(teamB)) { var p = BasePlayer.FindByID(pid); if (p != null) Toast(p, T(pid, "TourMatchStart", EntryName(t, teamA.Contains(pid) ? m.B : m.A)), false); }
                }
        }

        private void OnArenasMatchEnd(string tag, List<ulong> winners, List<ulong> losers, int scoreA, int scoreB)
        {
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith("tour:")) return;
            var t = _data.Tour;
            if (t == null || t.State != "running") return;
            var parts = tag.Split(':');
            int r = int.Parse(parts[1]), i = int.Parse(parts[2]);
            if (r >= t.Bracket.Count || i >= t.Bracket[r].Count) return;
            var w = winners != null && winners.Count > 0 ? CaptainOf(t, winners[0]) : 0UL;
            if (w == 0UL) { t.Bracket[r][i].State = "pending"; t.Bracket[r][i].WaitSince = 0f; return; }
            FinishTMatch(t, r, i, w, $"{Mathf.Max(scoreA, scoreB)}:{Mathf.Min(scoreA, scoreB)}");
        }

        private void Prize(ulong id, int amount)
        {
            if (amount <= 0 || Hub == null) return;
            if (!(Hub.Call("AddPrize", id, amount, T(id, "TourPrize")) is bool)) Hub.Call("AddCoins", id, amount, T(id, "TourPrize"));
        }

        private void FinishTMatch(Tournament t, int r, int i, ulong winner, string score)
        {
            var m = t.Bracket[r][i];
            m.Winner = winner;
            m.State = "done";
            m.Score = score;
            if (r + 1 < t.Bracket.Count)
            {
                var next = t.Bracket[r + 1][i / 2];
                if (i % 2 == 0) next.A = winner; else next.B = winner;
            }
            else
            {
                t.State = "finished";
                t.Champion = winner;
                t.Second = m.A == winner ? m.B : m.A;
                foreach (var pid in Members(t, winner)) Ach(pid, t.TeamSize > 1 ? "team_tour_wins" : "tour_wins");
                int second = Mathf.RoundToInt(t.Prize * _config.SecondShare);
                if (Hub != null && t.Prize > 0)
                {
                    // приз делится поровну между игроками команды
                    var champs = Members(t, winner);
                    foreach (var pid in champs) Prize(pid, (t.Prize - second) / champs.Count);
                    if (t.Second != 0UL && second > 0) { var sec = Members(t, t.Second); foreach (var pid in sec) Prize(pid, second / sec.Count); }
                }
                var clan = ClanOf(winner);
                if (clan != null) clan.TourWins++;
                _data.History.Insert(0, $"{t.Name}: {EntryName(t, winner)} ({DateTime.UtcNow:yyyy-MM-dd})");
                foreach (var p in BasePlayer.activePlayerList) Toast(p, T(Id(p), "TourChampion", EntryName(t, winner), t.Name), false);
            }
            SaveData();
        }

        #endregion

        #region Режим зрителя

        private class SpecState { public ulong Target; public Vector3 Back; }
        private readonly Dictionary<ulong, SpecState> _spec = new Dictionary<ulong, SpecState>();

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

        private List<ulong> Watchable(BasePlayer viewer)
        {
            var list = new List<ulong>();
            if (Arenas != null)
            {
                var live = Arenas.Call("GetLiveMatches") as List<Dictionary<string, object>>;
                if (live != null) foreach (var m in live) { var ps = m["players"] as List<ulong>; if (ps != null) list.AddRange(ps); }
            }
            if (!_config.SpecOnlyMatches || IsAdmin(viewer))
                list.AddRange(BasePlayer.activePlayerList.Where(p => p != viewer).Select(Id));
            if (Ranked != null)
                list.AddRange(BasePlayer.activePlayerList.Where(p => p != viewer && Ranked.Call("IsInRanked", p) is bool && (bool)Ranked.Call("IsInRanked", p)).Select(Id));
            return list.Distinct().Where(x => x != Id(viewer) && BasePlayer.FindByID(x) != null).ToList();
        }

        private void StartSpec(BasePlayer player, ulong target)
        {
            var id = Id(player);
            var t = BasePlayer.FindByID(target);
            if (t == null || t == player) { Toast(player, T(id, "NothingToWatch"), true); return; }
            if (player.IsDead()) return;
            if (Hub != null && Hub.Call("IsInCombat", player) is bool && (bool)Hub.Call("IsInCombat", player)) { Toast(player, T(id, "InCombat"), true); return; }
            if (Arenas != null && Arenas.Call("IsInArena", player) is bool && (bool)Arenas.Call("IsInArena", player)) { Toast(player, T(id, "LeaveArenaFirst"), true); return; }
            SpecState s;
            if (!_spec.TryGetValue(id, out s))
            {
                s = new SpecState { Back = player.transform.position };
                _spec[id] = s;
                player.inventory.Strip();
                Call(player, "StartSpectating");
            }
            s.Target = target;
            Call(player, "UpdateSpectateTarget", t.UserIDString);
            CloseAll(player);
            DrawSpec(player);
        }

        private void StopSpec(BasePlayer player)
        {
            SpecState s;
            if (!_spec.TryGetValue(Id(player), out s)) return;
            _spec.Remove(Id(player));
            Call(player, "StopSpectating");
            CuiHelper.DestroyUi(player, UiSpec);
            CuiHelper.DestroyUi(player, UiSpec + ".hp");
            if (player.IsConnected)
            {
                player.Teleport(s.Back);
                if (Hub != null) { var mode = ModeOf(player); if (!string.IsNullOrEmpty(mode)) Hub.Call("SendToMode", player, mode, true); }
            }
        }

        private void SpecStep(BasePlayer player, int dir)
        {
            SpecState s;
            if (!_spec.TryGetValue(Id(player), out s)) return;
            var list = Watchable(player);
            if (list.Count == 0) { StopSpec(player); return; }
            int idx = list.IndexOf(s.Target);
            idx = ((idx < 0 ? 0 : idx + dir) % list.Count + list.Count) % list.Count;
            StartSpec(player, list[idx]);
        }

        private void SpecTick()
        {
            foreach (var kv in _spec.ToList())
            {
                var p = BasePlayer.FindByID(kv.Key);
                if (p == null || !p.IsConnected) { _spec.Remove(kv.Key); continue; }
                if (BasePlayer.FindByID(kv.Value.Target) == null) { SpecStep(p, 1); continue; }
                DrawSpec(p);
            }
        }

        #endregion

        #region Команды

        [ChatCommand("party")]
        private void CmdParty(BasePlayer player, string c, string[] a)
        {
            if (a.Length >= 2 && a[0] == "invite") { var t = FindOnline(string.Join(" ", a.Skip(1).ToArray())); if (t != null) PartyInvite(player, t); return; }
            if (a.Length >= 1 && a[0] == "accept") { PartyAccept(player); return; }
            if (a.Length >= 1 && a[0] == "leave") { PartyLeave(player); return; }
            OpenParty(player);
        }

        [ChatCommand("clan")]
        private void CmdClan(BasePlayer player, string c, string[] a)
        {
            if (a.Length >= 2 && a[0] == "create") { CreateClan(player, a[1], a.Length > 2 ? string.Join(" ", a.Skip(2).ToArray()) : null); return; }
            if (a.Length >= 2 && a[0] == "invite") { var t = FindOnline(string.Join(" ", a.Skip(1).ToArray())); if (t != null) ClanInvite(player, t); return; }
            if (a.Length >= 1 && a[0] == "accept") { ClanAccept(player); return; }
            if (a.Length >= 1 && a[0] == "leave") { ClanLeave(player); return; }
            OpenClan(player);
        }

        [ChatCommand("c")]
        private void CmdClanChat(BasePlayer player, string c, string[] a)
        {
            var clan = ClanOf(Id(player));
            if (clan == null) { Toast(player, T(Id(player), "NoClan"), true); return; }
            if (a.Length == 0) return;
            ClanBroadcast(clan, $"<color=#55aaff>{player.displayName}</color>: {string.Join(" ", a).Replace("<", "‹")}");
        }

        [ChatCommand("tour")]
        private void CmdTour(BasePlayer player, string c, string[] a)
        {
            if (a.Length > 0 && IsAdmin(player))
            {
                switch (a[0])
                {
                    case "create": TourCreate(player, a); return;
                    case "start": TourStart(player); return;
                    case "cancel": _data.Tour = null; SaveData(); Reply(player, T(Id(player), "TourCancelled")); return;
                }
            }
            if (a.Length > 0 && a[0] == "join") { TourJoin(player); return; }
            OpenTour(player);
        }

        [ChatCommand("spec")]
        private void CmdSpec(BasePlayer player, string c, string[] a)
        {
            if (_spec.ContainsKey(Id(player)) && a.Length == 0) { StopSpec(player); return; }
            if (a.Length > 0) { var t = FindOnline(string.Join(" ", a)); if (t != null && (IsAdmin(player) || Watchable(player).Contains(Id(t)))) StartSpec(player, Id(t)); else Toast(player, T(Id(player), "CantWatch"), true); return; }
            OpenSpecList(player);
        }

        private BasePlayer FindOnline(string q) =>
            BasePlayer.activePlayerList.FirstOrDefault(p => p.displayName.Equals(q, StringComparison.OrdinalIgnoreCase))
            ?? BasePlayer.activePlayerList.FirstOrDefault(p => p.displayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);

        [ConsoleCommand("social.ui")]
        private void CcUi(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !arg.HasArgs()) return;
            var id = Id(player);
            string a1 = arg.GetString(1);
            ulong target; ulong.TryParse(a1, out target);
            switch (arg.GetString(0))
            {
                case "close": CloseAll(player); break;
                case "popup.close": DestroyWindow(player, UiPopup); break;
                case "party": OpenParty(player); break;
                case "party.invite": PartyInvite(player, BasePlayer.FindByID(target)); OpenParty(player); break;
                case "party.accept": PartyAccept(player); break;
                case "party.decline": _partyInvites.Remove(id); DestroyWindow(player, UiPopup); break;
                case "party.leave": PartyLeave(player); OpenParty(player); break;
                case "party.kick": PartyKick(player, target); OpenParty(player); break;
                case "party.follow": if (!_noFollow.Remove(id)) _noFollow.Add(id); OpenParty(player); break;
                case "follow": DestroyWindow(player, UiPopup); if (Hub != null) Hub.Call("SendToMode", player, a1, false); break;

                case "clan": _tab[id] = a1 ?? "my"; OpenClan(player); break;
                case "clan.tag": _input[id] = string.Join(" ", arg.Args.Skip(1).ToArray()); break;
                case "clan.name": _input2[id] = string.Join(" ", arg.Args.Skip(1).ToArray()); break;
                case "clan.create": { string t, n; _input.TryGetValue(id, out t); _input2.TryGetValue(id, out n); CreateClan(player, t, n); break; }
                case "clan.invite": ClanInvite(player, BasePlayer.FindByID(target)); OpenClan(player); break;
                case "clan.accept": ClanAccept(player); break;
                case "clan.decline": _clanInvites.Remove(id); DestroyWindow(player, UiPopup); break;
                case "clan.leave": ClanLeave(player); OpenClan(player); break;
                case "clan.kick": ClanKick(player, target); OpenClan(player); break;
                case "clan.promote": ClanPromote(player, target); OpenClan(player); break;

                case "tour": OpenTour(player); break;
                case "tour.join": TourJoin(player); break;

                case "spec.list": OpenSpecList(player); break;
                case "spec": StartSpec(player, target); break;
                case "spec.next": SpecStep(player, 1); break;
                case "spec.prev": SpecStep(player, -1); break;
                case "spec.stop": StopSpec(player); break;
            }
        }

        private void Reply(BasePlayer player, string msg) => player.ChatMessage($"<color=#f0a6b1>[Social]</color> {msg}");

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

        private static void Input(CuiElementContainer c, string parent, string name, string value, string hint, string cmd, float x, float y, float w, float h, int limit)
        {
            c.Add(new CuiElement { Name = name, Parent = parent, Components = { new CuiImageComponent { Color = "0 0 0 0.35" }, new CuiRectTransformComponent { AnchorMin = "0 1", AnchorMax = "0 1", OffsetMin = $"{x} {-y - h}", OffsetMax = $"{x + w} {-y}" } } });
            if (string.IsNullOrEmpty(value)) Text(c, name, hint, 12, 12, 0, w - 24, h, TextAnchor.MiddleLeft, "0.45 0.41 0.40 1", FontReg);
            c.Add(new CuiElement { Parent = name, Components = { new CuiInputFieldComponent { Text = value ?? "", Command = cmd, FontSize = 13, Align = TextAnchor.MiddleLeft, CharsLimit = limit, Color = ColText }, new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1", OffsetMin = "12 0", OffsetMax = "-12 0" } } });
        }

        private const float M = 18f, G = 10f;

        private void Window(CuiElementContainer c, string name, string icon, string title, string sub, float w, float h, string closeCmd, bool dim = true)
        {
            if (dim) c.Add(new CuiPanel { Image = { Color = "0.03 0.02 0.02 0.5" }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }, CursorEnabled = true }, "Overlay", name + ".dim");
            Root(c, name, dim ? name + ".dim" : "Overlay", ColBg, "0.5 0.5", "0.5 0.5", $"{-w / 2f} {-h / 2f}", $"{w / 2f} {h / 2f}", 18f, true);
            Box(c, name, ColWine, M, M, 40, 40, 12f);
            Text(c, name, icon, 13, M, M, 40, 40, TextAnchor.MiddleCenter);
            Text(c, name, title, 18, M + 50, M - 2, w - 2 * (M + 50), 26, TextAnchor.MiddleCenter);
            Text(c, name, sub, 11, M + 50, M + 22, w - 2 * (M + 50), 18, TextAnchor.MiddleCenter, ColDim, FontReg);
            if (!string.IsNullOrEmpty(closeCmd)) Btn(c, name, "X", closeCmd, ColBtn, 14, w - M - 40, M, 40, 40);
            Box(c, name, ColWine, 0, M + 52, w, 2, 0f);
        }

        private void DestroyWindow(BasePlayer p, string name) { CuiHelper.DestroyUi(p, name + ".dim"); CuiHelper.DestroyUi(p, name); }

        private void CloseAll(BasePlayer p) { DestroyWindow(p, UiMain); DestroyWindow(p, UiPopup); }

        // вкладки «Пати · Клан · Турнир · Зритель» — одно окно, одинаковая сетка
        private const float W = 940f, H = 620f;

        private float Frame(CuiElementContainer c, ulong id, string active, string title, string sub)
        {
            Window(c, UiMain, active.Substring(0, 1).ToUpper(), title, sub, W, H, "social.ui close");
            float top = M + 52 + 2 + M;
            var tabs = new[] { "party", "clan", "tour", "spec.list" };
            float tw = (W - 2 * M - 3 * 6f) / 4f;
            for (int i = 0; i < tabs.Length; i++)
            {
                bool a = tabs[i] == active;
                Btn(c, UiMain, T(id, "Tab_" + tabs[i]), "social.ui " + tabs[i], a ? ColWine : ColBtn, 13, M + i * (tw + 6), top, tw, 36, a ? ColText : ColDim, 11f);
            }
            return top + 36 + G;
        }

        private void Row(CuiElementContainer c, float x, float y, float w, string left, string sub, string right, string rightColor = ColDim, bool hl = false)
        {
            Box(c, UiMain, hl ? ColWineSoft : ColRow, x, y, w, 40, 10f);
            Text(c, UiMain, left, 13, x + 12, y + (sub != null ? 2 : 0), w - 220, sub != null ? 22 : 40);
            if (sub != null) Text(c, UiMain, sub, 10, x + 12, y + 22, w - 220, 16, TextAnchor.MiddleLeft, ColDim, FontReg);
            if (right != null) Text(c, UiMain, right, 11, x + w - 210, y, 120, 40, TextAnchor.MiddleRight, rightColor);
        }

        #endregion

        #region Интерфейс: пати

        private void OpenParty(BasePlayer player)
        {
            var id = Id(player);
            var c = new CuiElementContainer();
            var team = TeamOf(player);
            float top = Frame(c, id, "party", T(id, "PartyTitle"), T(id, "PartySub"));
            float cw = (W - 2 * M - G) / 2f, ph = H - M - top;

            Box(c, UiMain, ColPanel, M, top, cw, ph, 16f);
            int n = team?.members.Count ?? 1;
            Text(c, UiMain, T(id, "PartyMembers", n, _config.PartyMax), 10, M + 14, top + 10, cw - 28, 16, TextAnchor.MiddleLeft, ColDim);
            var members = team != null ? team.members.ToList() : new List<ulong> { id };
            bool leader = team == null || team.teamLeader == id;
            for (int i = 0; i < members.Count && i < 8; i++)
            {
                var m = members[i];
                var p = BasePlayer.FindByID(m);
                string mode = p != null ? ModeName(ModeOf(p), id) : T(id, "Offline");
                bool isLeader = team != null && team.teamLeader == m;
                Row(c, M + 12, top + 34 + i * 46, cw - 24, NameOf(m) + (m == id ? T(id, "You") : ""), isLeader ? T(id, "Leader") : null, mode, p != null ? ColGreen : ColDim, m == id);
                if (leader && team != null && m != id) Btn(c, UiMain, "X", "social.ui party.kick " + m, ColBtn, 11, M + cw - 12 - 40, top + 34 + i * 46 + 7, 30, 26, "0.92 0.42 0.48 1", 9f);
            }
            float by = top + ph - 54;
            float bw = (cw - 24 - G) / 2f;
            Btn(c, UiMain, _noFollow.Contains(id) ? T(id, "FollowOff") : T(id, "FollowOn"), "social.ui party.follow", ColBtn, 11, M + 12, by, bw, 40, ColText, 12f);
            Btn(c, UiMain, T(id, "LeaveParty"), team != null ? "social.ui party.leave" : "", ColBtn, 12, M + 12 + bw + G, by, bw, 40, team != null ? ColText : ColDim, 12f);

            float rx = M + cw + G;
            Box(c, UiMain, ColPanel, rx, top, cw, ph, 16f);
            Text(c, UiMain, T(id, "InviteOnline"), 10, rx + 14, top + 10, cw - 28, 16, TextAnchor.MiddleLeft, ColDim);
            var online = BasePlayer.activePlayerList.Where(p => p != player && TeamOf(p) == null).OrderBy(p => p.displayName).Take(16).ToList();
            float ow = (cw - 24 - G) / 2f;
            for (int i = 0; i < online.Count; i++)
                Btn(c, UiMain, "+ " + online[i].displayName, "social.ui party.invite " + Id(online[i]), ColBtn, 11, rx + 12 + (i % 2) * (ow + G), top + 34 + (i / 2) * 42, ow, 36, ColText, 10f);
            if (online.Count == 0) Text(c, UiMain, T(id, "NobodyFree"), 12, rx, top, cw, ph, TextAnchor.MiddleCenter, ColDim, FontReg);
            Text(c, UiMain, T(id, "PartyHint"), 10, rx + 14, top + ph - 44, cw - 28, 34, TextAnchor.LowerLeft, ColDim, FontReg);

            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        private void ShowInvite(BasePlayer to, string title, string sub, string yes, string no)
        {
            var id = Id(to);
            const float w = 460f, h = 200f;
            var c = new CuiElementContainer();
            Root(c, UiPopup, "Overlay", ColBg, "0.5 0", "0.5 0", $"{-w / 2f} 150", $"{w / 2f} {150 + h}", 18f, true);
            Text(c, UiPopup, title, 16, M, 24, w - 2 * M, 30, TextAnchor.MiddleCenter);
            Text(c, UiPopup, sub, 11, M, 56, w - 2 * M, 40, TextAnchor.UpperCenter, ColDim, FontReg);
            float bw = (w - 2 * M - G) / 2f;
            Btn(c, UiPopup, T(id, "Yes"), yes, ColWine, 14, M, h - M - 46, bw, 46, ColText, 14f);
            Btn(c, UiPopup, T(id, "No"), no, ColBtn, 13, M + bw + G, h - M - 46, bw, 46, ColText, 14f);
            DestroyWindow(to, UiPopup);
            AddUiScaled(to, c);
            var p = to;
            timer.Once(30f, () => { if (p != null && p.IsConnected) DestroyWindow(p, UiPopup); });
        }

        #endregion

        #region Интерфейс: клан

        private void OpenClan(BasePlayer player)
        {
            var id = Id(player);
            string tab; if (!_tab.TryGetValue(id, out tab)) tab = "my";
            var clan = ClanOf(id);
            var c = new CuiElementContainer();
            float top = Frame(c, id, "clan", T(id, "ClanTitle"), T(id, "ClanSub"));
            float cw = (W - 2 * M - G) / 2f, ph = H - M - top;

            // слева: мой клан или создание
            Box(c, UiMain, ColPanel, M, top, cw, ph, 16f);
            if (clan == null)
            {
                Text(c, UiMain, T(id, "CreateClan"), 10, M + 14, top + 10, cw - 28, 16, TextAnchor.MiddleLeft, ColDim);
                string tagV, nameV; _input.TryGetValue(id, out tagV); _input2.TryGetValue(id, out nameV);
                Input(c, UiMain, UiMain + ".tag", tagV, T(id, "TagHint", _config.TagLen[0], _config.TagLen[1]), "social.ui clan.tag", M + 12, top + 36, cw - 24, 40, _config.TagLen[1]);
                Input(c, UiMain, UiMain + ".name", nameV, T(id, "NameHint"), "social.ui clan.name", M + 12, top + 84, cw - 24, 40, 24);
                Btn(c, UiMain, _config.ClanPrice > 0 ? T(id, "CreateFor", _config.ClanPrice) : T(id, "CreateFree"), "social.ui clan.create", ColWine, 14, M + 12, top + 134, cw - 24, 44, ColText, 14f);
                Text(c, UiMain, T(id, "ClanWhy"), 11, M + 14, top + 196, cw - 28, ph - 210, TextAnchor.UpperLeft, ColDim, FontReg);
            }
            else
            {
                int rating = ClanRating(clan);
                Box(c, UiMain, ColWine, M + 14, top + 14, 62, 62, 16f);
                Text(c, UiMain, clan.Tag, clan.Tag.Length > 3 ? 13 : 17, M + 14, top + 14, 62, 62, TextAnchor.MiddleCenter);
                Text(c, UiMain, clan.Name, 17, M + 88, top + 16, cw - 100, 26);
                Text(c, UiMain, T(id, "ClanInfo", clan.Members.Count, _config.ClanMax, rating, clan.TourWins), 11, M + 88, top + 44, cw - 100, 30, TextAnchor.UpperLeft, ColDim, FontReg);
                float y = top + 90;
                var ordered = clan.Members.OrderByDescending(m => m == clan.Owner).ThenByDescending(m => clan.Officers.Contains(m)).ThenByDescending(m => BasePlayer.FindByID(m) != null).Take(8).ToList();
                bool canManage = clan.Owner == id || clan.Officers.Contains(id);
                foreach (var m in ordered)
                {
                    var p = BasePlayer.FindByID(m);
                    string role = m == clan.Owner ? T(id, "Owner") : clan.Officers.Contains(m) ? T(id, "Officer") : null;
                    Row(c, M + 12, y, cw - 24, NameOf(m), role, p != null ? ModeName(ModeOf(p), id) : T(id, "Offline"), p != null ? ColGreen : ColDim, m == id);
                    if (canManage && m != id && m != clan.Owner)
                    {
                        if (clan.Owner == id) Btn(c, UiMain, clan.Officers.Contains(m) ? "−" : "+", "social.ui clan.promote " + m, ColBtn, 13, M + cw - 12 - 76, y + 7, 30, 26, ColText, 9f);
                        Btn(c, UiMain, "X", "social.ui clan.kick " + m, ColBtn, 11, M + cw - 12 - 40, y + 7, 30, 26, "0.92 0.42 0.48 1", 9f);
                    }
                    y += 46;
                }
                Btn(c, UiMain, clan.Owner == id && clan.Members.Count == 1 ? T(id, "Disband") : T(id, "LeaveClan"), "social.ui clan.leave", ColBtn, 12, M + 12, top + ph - 54, cw - 24, 40, ColText, 12f);
            }

            // справа: пригласить (если в клане) или топ кланов
            float rx = M + cw + G;
            Box(c, UiMain, ColPanel, rx, top, cw, ph, 16f);
            bool showInvite = clan != null && tab == "my";
            Btn(c, UiMain, T(id, "ClanInviteTab"), clan != null ? "social.ui clan my" : "", showInvite ? ColWine : ColBtn, 11, rx + 12, top + 12, (cw - 30) / 2f, 30, clan != null ? ColText : ColDim, 10f);
            Btn(c, UiMain, T(id, "ClanTopTab"), "social.ui clan top", !showInvite ? ColWine : ColBtn, 11, rx + 18 + (cw - 30) / 2f, top + 12, (cw - 30) / 2f, 30, ColText, 10f);
            if (showInvite)
            {
                var online = BasePlayer.activePlayerList.Where(p => p != player && ClanOf(Id(p)) == null).OrderBy(p => p.displayName).Take(16).ToList();
                float ow = (cw - 24 - G) / 2f;
                for (int i = 0; i < online.Count; i++)
                    Btn(c, UiMain, "+ " + online[i].displayName, "social.ui clan.invite " + Id(online[i]), ColBtn, 11, rx + 12 + (i % 2) * (ow + G), top + 54 + (i / 2) * 42, ow, 36, ColText, 10f);
                if (online.Count == 0) Text(c, UiMain, T(id, "NobodyFree"), 12, rx, top + 40, cw, ph - 40, TextAnchor.MiddleCenter, ColDim, FontReg);
            }
            else
            {
                var top10 = _data.Clans.Values.Select(x => new { C = x, R = ClanRating(x) }).OrderByDescending(x => x.R).ThenByDescending(x => x.C.Members.Count).Take(10).ToList();
                for (int i = 0; i < top10.Count; i++)
                {
                    var x = top10[i];
                    Row(c, rx + 12, top + 54 + i * 46, cw - 24, $"{i + 1}. [{x.C.Tag}] {x.C.Name}", T(id, "ClanRowSub", x.C.Members.Count, x.C.TourWins), x.R > 0 ? x.R + " MMR" : "—", i == 0 ? ColGold : ColDim, clan != null && x.C.Tag == clan.Tag);
                }
                if (top10.Count == 0) Text(c, UiMain, T(id, "NoClans"), 12, rx, top + 40, cw, ph - 40, TextAnchor.MiddleCenter, ColDim, FontReg);
            }

            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        #endregion

        #region Интерфейс: турнир

        private void OpenTour(BasePlayer player)
        {
            var id = Id(player);
            var t = _data.Tour;
            var c = new CuiElementContainer();
            string sub = t == null ? T(id, "NoTourSub") : T(id, "TourSub", t.Name, t.Size, t.Rounds, t.Prize) + (t.TeamSize > 1 ? $" · {t.TeamSize}v{t.TeamSize}" + (t.ClanOnly ? " · " + T(id, "ClansOnly") : "") : "");
            float top = Frame(c, id, "tour", T(id, "TourTitle"), sub);
            float ph = H - M - 46 - G - top;
            Box(c, UiMain, ColPanel, M, top, W - 2 * M, ph, 16f);

            if (t == null)
            {
                Text(c, UiMain, T(id, "NoTour") + (_data.History.Count > 0 ? "\n\n" + T(id, "LastWinners") + "\n" + string.Join("\n", _data.History.Take(5).ToArray()) : ""), 13, M, top, W - 2 * M, ph, TextAnchor.MiddleCenter, ColDim, FontReg);
            }
            else if (t.State == "registration")
            {
                Text(c, UiMain, T(id, "Registered", t.Players.Count, t.Size), 10, M + 14, top + 10, 400, 16, TextAnchor.MiddleLeft, ColDim);
                float ow = (W - 2 * M - 24 - 3 * G) / 4f;
                for (int i = 0; i < t.Players.Count && i < 32; i++)
                {
                    bool myTeam = Members(t, t.Players[i]).Contains(id);
                    Box(c, UiMain, myTeam ? ColWineSoft : ColRow, M + 12 + (i % 4) * (ow + G), top + 34 + (i / 4) * 40, ow, 34, 10f);
                    Text(c, UiMain, EntryName(t, t.Players[i]), 12, M + 24 + (i % 4) * (ow + G), top + 34 + (i / 4) * 40, ow - 24, 34);
                }
            }
            else
            {
                // сетка: колонки раундов, слоты по центру своих пар
                int rounds = t.Bracket.Count;
                float colW = (W - 2 * M - 24 - (rounds - 1) * 16f) / rounds;
                float areaH = ph - 40;
                for (int r = 0; r < rounds; r++)
                {
                    var col = t.Bracket[r];
                    float x = M + 12 + r * (colW + 16f);
                    Text(c, UiMain, r == rounds - 1 ? T(id, "Final") : r == rounds - 2 && rounds > 1 ? T(id, "Semi") : T(id, "RoundN", r + 1), 10, x, top + 10, colW, 16, TextAnchor.MiddleCenter, ColDim);
                    float slot = areaH / col.Count;
                    float mh = Mathf.Min(58f, slot - 6f);
                    for (int i = 0; i < col.Count; i++)
                    {
                        var m = col[i];
                        float y = top + 34 + i * slot + (slot - mh) / 2f;
                        bool live = m.State == "live";
                        Box(c, UiMain, live ? "0.48 0.07 0.14 0.25" : ColRow, x, y, colW, mh, 10f);
                        float lh = (mh - 6f) / 2f;
                        for (int k = 0; k < 2; k++)
                        {
                            ulong pid = k == 0 ? m.A : m.B;
                            bool won = m.State == "done" && m.Winner == pid && pid != 0UL;
                            bool lost = m.State == "done" && m.Winner != pid;
                            string nm = pid == 0UL ? (r == 0 ? T(id, "Bye") : "?") : EntryName(t, pid);
                            if (won) Box(c, UiMain, "0.50 0.83 0.60 0.15", x + 3, y + 3 + k * lh, colW - 6, lh, 7f);
                            Text(c, UiMain, nm, mh < 40 ? 10 : 12, x + 10, y + 3 + k * lh, colW - 20, lh, TextAnchor.MiddleLeft, lost ? ColDim : won ? ColGreen : Members(t, pid).Contains(id) ? ColRose : ColText, won ? Font : FontReg);
                        }
                        if (m.State == "done" && !string.IsNullOrEmpty(m.Score)) Text(c, UiMain, m.Score, 10, x, y, colW - 8, mh, TextAnchor.MiddleRight, ColDim);
                        if (live) Text(c, UiMain, T(id, "Live"), 9, x, y + mh - 2, colW, 12, TextAnchor.MiddleCenter, ColRose, FontReg);
                    }
                }
                if (t.State == "finished") Text(c, UiMain, T(id, "Champion", EntryName(t, t.Champion)), 14, M, top + ph - 34, W - 2 * M, 24, TextAnchor.MiddleCenter, ColGold);
            }

            float by = H - M - 46, bw = (W - 2 * M - G) / 2f;
            bool reg = t != null && t.State == "registration";
            bool mine = t != null && (t.TeamSize > 1 ? CaptainOf(t, id) != 0UL : t.Players.Contains(id));
            Btn(c, UiMain, reg ? (mine ? T(id, "Unregister") : t.TeamSize > 1 ? T(id, "RegisterTeam", t.TeamSize) : T(id, "Register")) : T(id, "RegClosed"), reg ? "social.ui tour.join" : "", reg ? (mine ? ColBtn : ColWine) : ColBtn, 14, M, by, bw, 46, reg ? ColText : ColDim, 14f);
            Btn(c, UiMain, T(id, "WatchMatches"), "social.ui spec.list", ColBtn, 13, M + bw + G, by, bw, 46, ColText, 14f);
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        #endregion

        #region Интерфейс: зритель

        private void OpenSpecList(BasePlayer player)
        {
            var id = Id(player);
            var c = new CuiElementContainer();
            float top = Frame(c, id, "spec.list", T(id, "SpecTitle"), T(id, "SpecSub"));
            float ph = H - M - top;
            Box(c, UiMain, ColPanel, M, top, W - 2 * M, ph, 16f);
            var rows = new List<string[]>();
            if (Arenas != null)
            {
                var live = Arenas.Call("GetLiveMatches") as List<Dictionary<string, object>>;
                if (live != null)
                    foreach (var m in live)
                    {
                        var ps = m["players"] as List<ulong>;
                        if (ps == null || ps.Count == 0) continue;
                        string tag = m["tag"] as string;
                        rows.Add(new[] { $"{m["a"]}  vs  {m["b"]}", $"{m["arena"]} · {m["score"]}{(!string.IsNullOrEmpty(tag) && tag.StartsWith("tour:") ? " · " + T(id, "TourTag") : "")}", ps[0].ToString() });
                    }
            }
            if (Ranked != null)
                foreach (var p in BasePlayer.activePlayerList.Where(p => Ranked.Call("IsInRanked", p) is bool && (bool)Ranked.Call("IsInRanked", p)).Take(10))
                    rows.Add(new[] { p.displayName, T(id, "RankedTag"), Id(p).ToString() });
            float rw = W - 2 * M - 24;
            for (int i = 0; i < rows.Count && i < 9; i++)
            {
                float y = top + 12 + i * 50;
                Box(c, UiMain, ColRow, M + 12, y, rw, 44, 12f);
                Text(c, UiMain, rows[i][0], 14, M + 26, y + 2, rw - 200, 22);
                Text(c, UiMain, rows[i][1], 10, M + 26, y + 24, rw - 200, 16, TextAnchor.MiddleLeft, ColDim, FontReg);
                Btn(c, UiMain, T(id, "Watch"), "social.ui spec " + rows[i][2], ColWine, 12, M + 12 + rw - 130, y + 7, 118, 30, ColText, 10f);
            }
            if (rows.Count == 0) Text(c, UiMain, T(id, "NothingLive"), 13, M, top, W - 2 * M, ph, TextAnchor.MiddleCenter, ColDim, FontReg);
            DestroyWindow(player, UiMain);
            AddUiScaled(player, c);
        }

        private void DrawSpec(BasePlayer player)
        {
            var id = Id(player);
            SpecState s;
            if (!_spec.TryGetValue(id, out s)) return;
            var t = BasePlayer.FindByID(s.Target);
            var c = new CuiElementContainer();
            const float bw = 560f;
            Root(c, UiSpec, "Overlay", ColHud, "0.5 0", "0.5 0", $"{-bw / 2f} 24", $"{bw / 2f} 78", 18f, true);
            Btn(c, UiSpec, "<", "social.ui spec.prev", ColBtn, 15, 10, 10, 40, 34, ColText, 11f);
            Box(c, UiSpec, ColWineSoft, 56, 10, 220, 34, 11f);
            Text(c, UiSpec, t != null ? t.displayName : "—", 13, 56, 10, 220, 34, TextAnchor.MiddleCenter);
            Btn(c, UiSpec, ">", "social.ui spec.next", ColBtn, 15, 282, 10, 40, 34, ColText, 11f);
            Btn(c, UiSpec, T(id, "Matches"), "social.ui spec.list", ColBtn, 11, 332, 10, 106, 34, ColText, 11f);
            Btn(c, UiSpec, T(id, "Exit"), "social.ui spec.stop", ColBtn, 11, 444, 10, 106, 34, ColText, 11f);
            if (t != null)
            {
                Root(c, UiSpec + ".hp", "Hud", ColHud, "1 1", "1 1", "-216 -72", "-16 -16", 16f, false);
                Text(c, UiSpec + ".hp", t.displayName, 12, 12, 6, 120, 20, TextAnchor.MiddleLeft);
                Text(c, UiSpec + ".hp", Mathf.RoundToInt(t.health) + " HP", 12, 120, 6, 68, 20, TextAnchor.MiddleRight, ColGreen);
                Box(c, UiSpec + ".hp", "1 1 1 0.08", 12, 32, 176, 6, 3f);
                Box(c, UiSpec + ".hp", ColRed, 12, 32, Mathf.Max(6f, 176f * Mathf.Clamp01(t.health / 100f)), 6, 3f);
            }
            CuiHelper.DestroyUi(player, UiSpec);
            CuiHelper.DestroyUi(player, UiSpec + ".hp");
            AddUiScaled(player, c);
        }

        private void Toast(BasePlayer player, string text, bool warn)
        {
            if (player == null || !player.IsConnected) return;
            var c = new CuiElementContainer();
            Root(c, UiToast, "Overlay", warn ? "0.40 0.06 0.11 0.95" : "0.07 0.065 0.065 0.95", "0.5 1", "0.5 1", "-260 -76", "260 -34", 20f, false);
            c.Add(new CuiLabel { Text = { Text = text, FontSize = 14, Align = TextAnchor.MiddleCenter, Color = ColText, Font = Font }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, UiToast);
            CuiHelper.DestroyUi(player, UiToast);
            AddUiScaled(player, c);
            var p = player;
            timer.Once(3.5f, () => { if (p != null && p.IsConnected) CuiHelper.DestroyUi(p, UiToast); });
        }

        #endregion

        #region Профиль и API

        private string OnHubProfile(ulong userId, string lng)
        {
            var clan = ClanOf(userId);
            if (clan == null) return null;
            return lng == "en" ? $"<color=#f0a6b1>CLAN</color>\n[{clan.Tag}] {clan.Name} · {clan.Members.Count} members" : $"<color=#f0a6b1>КЛАН</color>\n[{clan.Tag}] {clan.Name} · участников {clan.Members.Count}";
        }

        [HookMethod("GetClanTag")] public string GetClanTag(ulong id) => ClanOf(id)?.Tag;

        [HookMethod("SameClan")] public bool SameClan(ulong a, ulong b) { var ca = ClanOf(a); return ca != null && ca == ClanOf(b); }

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
            new[] { "Tab_party", "Пати", "Party" }, new[] { "Tab_clan", "Клан", "Clan" }, new[] { "Tab_tour", "Турнир", "Tournament" }, new[] { "Tab_spec.list", "Смотреть", "Spectate" },
            new[] { "M_lobby", "лобби", "lobby" }, new[] { "M_build", "стройка", "build" }, new[] { "M_film", "съёмки", "filming" },
            new[] { "M_ranked_ak", "ранкед · калаши", "ranked AK" }, new[] { "M_ranked_mix", "ранкед · ганы", "ranked mix" },
            new[] { "M_ffa", "FFA", "FFA" }, new[] { "M_duels", "дуэли", "duels" }, new[] { "M_facecheck", "фейсчеки", "facechecks" },
            new[] { "M_aim", "аим-трен", "aim train" }, new[] { "M_primitive", "примитив", "primitive" },
            new[] { "Offline", "не в сети", "offline" }, new[] { "You", " (ты)", " (you)" }, new[] { "Yes", "ДА", "YES" }, new[] { "No", "Нет", "No" },

            new[] { "PartyTitle", "ПАТИ", "PARTY" }, new[] { "PartySub", "команда Rust · вместе в очередь и режимы · /party", "Rust team · queue and play together · /party" },
            new[] { "PartyMembers", "СОСТАВ · {0} / {1}", "MEMBERS · {0} / {1}" }, new[] { "Leader", "лидер", "leader" },
            new[] { "InviteOnline", "ПРИГЛАСИТЬ ИЗ ОНЛАЙНА", "INVITE ONLINE PLAYERS" }, new[] { "NobodyFree", "Все уже заняты", "Nobody available" },
            new[] { "PartyHint", "Лидер ставит всю пати в очередь ранкеда. Когда лидер меняет режим, остальным приходит «пойти за ним?».", "The leader queues the whole party for ranked. When the leader switches mode, others are offered to follow." },
            new[] { "FollowOn", "Идти за лидером: да", "Follow leader: on" }, new[] { "FollowOff", "Идти за лидером: нет", "Follow leader: off" },
            new[] { "LeaveParty", "ВЫЙТИ ИЗ ПАТИ", "LEAVE PARTY" },
            new[] { "PartyInviteTitle", "{0} зовёт в пати", "{0} invites you to a party" }, new[] { "PartyInviteSub", "пати = команда Rust, вместе в очередь и режимы", "party = Rust team" },
            new[] { "OnlyLeader", "Приглашает только лидер", "Only the leader can invite" }, new[] { "PartyFull", "Пати заполнена", "Party is full" },
            new[] { "AlreadyInTeam", "{0} уже в другой команде", "{0} is already in a team" }, new[] { "InviteSent", "Приглашение отправлено: {0}", "Invite sent to {0}" },
            new[] { "InviteGone", "Приглашение устарело", "Invite expired" }, new[] { "Joined", "{0} в пати", "{0} joined the party" },
            new[] { "PartyLeft", "Ты вышел из пати", "You left the party" }, new[] { "Kicked", "Тебя убрали из пати", "You were removed from the party" },
            new[] { "TeamError", "Не получилось — попробуй через Tab → команда", "Failed — try via the Tab team menu" },
            new[] { "FollowTitle", "{0} ушёл в «{1}»", "{0} went to '{1}'" }, new[] { "FollowSub", "Пойти за лидером?", "Follow your leader?" },

            new[] { "ClanTitle", "КЛАН", "CLAN" }, new[] { "ClanSub", "тег в чате · клановый рейтинг · /clan · /c — клановый чат", "tag in chat · clan rating · /clan · /c — clan chat" },
            new[] { "CreateClan", "СОЗДАТЬ КЛАН", "CREATE A CLAN" }, new[] { "TagHint", "тег: {0}–{1} букв или цифр", "tag: {0}–{1} letters or digits" }, new[] { "NameHint", "название клана", "clan name" },
            new[] { "CreateFor", "СОЗДАТЬ · {0} монет", "CREATE · {0} coins" }, new[] { "CreateFree", "СОЗДАТЬ", "CREATE" },
            new[] { "ClanWhy", "Тег клана виден в чате перед ником. Рейтинг клана — средний лучший MMR пяти сильнейших участников в ранкеде. Победы в турнирах тоже записываются клану.", "Your tag shows in chat. Clan rating is the average best ranked MMR of the top five members." },
            new[] { "ClanInfo", "участников {0}/{1} · рейтинг {2} · побед в турнирах {3}", "{0}/{1} members · rating {2} · {3} tournament wins" },
            new[] { "Owner", "глава", "leader" }, new[] { "Officer", "офицер", "officer" },
            new[] { "LeaveClan", "ВЫЙТИ ИЗ КЛАНА", "LEAVE CLAN" }, new[] { "Disband", "РАСПУСТИТЬ КЛАН", "DISBAND CLAN" },
            new[] { "ClanInviteTab", "Пригласить", "Invite" }, new[] { "ClanTopTab", "Топ кланов", "Top clans" },
            new[] { "ClanRowSub", "участников {0} · турниров {1}", "{0} members · {1} tournaments" }, new[] { "NoClans", "Кланов пока нет — создай первый", "No clans yet" },
            new[] { "AlreadyClan", "Ты уже в клане", "You are already in a clan" }, new[] { "BadTag", "Тег: {0}–{1} букв или цифр", "Tag: {0}–{1} letters or digits" },
            new[] { "TagTaken", "Тег {0} уже занят", "Tag {0} is taken" }, new[] { "NoCoins", "Нужно {0} монет", "You need {0} coins" },
            new[] { "ClanCreated", "Клан [{0}] создан!", "Clan [{0}] created!" }, new[] { "OfficersOnly", "Приглашают глава и офицеры", "Only leader and officers can invite" },
            new[] { "ClanFull", "В клане нет мест", "The clan is full" }, new[] { "TargetHasClan", "{0} уже в клане", "{0} already has a clan" },
            new[] { "ClanInviteTitle", "Приглашение в клан [{0}] {1}", "Invite to clan [{0}] {1}" }, new[] { "ClanInviteSub", "от {0}", "from {0}" },
            new[] { "ClanJoined", "{0} вступил в клан", "{0} joined the clan" }, new[] { "ClanLeft", "{0} вышел из клана", "{0} left the clan" },
            new[] { "ClanKicked", "{0} исключён из клана", "{0} was kicked" }, new[] { "YouLeftClan", "Ты вышел из клана", "You left the clan" },
            new[] { "ClanDisbanded", "Клан распущен", "Clan disbanded" }, new[] { "NoClan", "Ты не в клане", "You are not in a clan" },

            new[] { "TourTitle", "ТУРНИР", "TOURNAMENT" }, new[] { "NoTourSub", "сейчас турниров нет · /tour", "no tournament right now · /tour" },
            new[] { "TeamTourAnnounce", "Командный турнир «{0}» {3}v{3}: {1} команд, приз {2} — /tour", "Team tournament \"{0}\" {3}v{3}: {1} teams, prize {2} — /tour" },
            new[] { "RegisterTeam", "ЗАПИСАТЬ ПАТИ ({0} ЧЕЛ.)", "REGISTER PARTY ({0})" },
            new[] { "OnlyCaptain", "Снять команду может только капитан", "Only the captain can unregister" },
            new[] { "NeedPartyLead", "Собери пати из {0} человек (/party) — записывает лидер", "Make a party of {0} (/party) — the leader registers" },
            new[] { "NeedPartySize", "Нужно ровно {0} в пати, сейчас {1}", "Need exactly {0} in the party, now {1}" },
            new[] { "PartyOffline", "Кто-то из пати не в сети", "Someone in the party is offline" },
            new[] { "AlreadyInTeam", "Кто-то из пати уже записан в другой команде", "Someone is already in another team" },
            new[] { "NeedSameClan", "Турнир только для кланов: все в пати должны быть из твоего клана", "Clan tournament: everyone must be in your clan" },
            new[] { "ClansOnly", "только кланы", "clans only" },
            new[] { "TourSub", "{0} · сетка на {1} · до {2} побед · приз {3} монет", "{0} · {1}-player bracket · first to {2} · prize {3} coins" },
            new[] { "NoTour", "Турнир ещё не объявлен — следи за Discord", "No tournament yet — watch Discord" }, new[] { "LastWinners", "Последние чемпионы:", "Last champions:" },
            new[] { "Registered", "ЗАРЕГИСТРИРОВАНО · {0} / {1}", "REGISTERED · {0} / {1}" },
            new[] { "Register", "ЗАРЕГИСТРИРОВАТЬСЯ", "REGISTER" }, new[] { "Unregister", "ОТМЕНИТЬ РЕГИСТРАЦИЮ", "UNREGISTER" }, new[] { "RegClosed", "РЕГИСТРАЦИЯ ЗАКРЫТА", "REGISTRATION CLOSED" },
            new[] { "WatchMatches", "СМОТРЕТЬ МАТЧИ", "WATCH MATCHES" },
            new[] { "RoundN", "раунд {0}", "round {0}" }, new[] { "Semi", "полуфинал", "semi-final" }, new[] { "Final", "финал", "final" },
            new[] { "Bye", "проход", "bye" }, new[] { "Live", "● идёт", "● live" }, new[] { "Champion", "Чемпион: {0}", "Champion: {0}" },
            new[] { "TourExists", "Турнир уже идёт (/tour cancel)", "A tournament is already running" },
            new[] { "TourAnnounce", "Турнир «{0}» на {1} игроков · приз {2} · /tour", "Tournament '{0}' for {1} · prize {2} · /tour" },
            new[] { "NoRegistration", "Регистрации сейчас нет", "Registration is closed" }, new[] { "TourFull", "Мест нет", "Full" },
            new[] { "TourRegistered", "Ты в турнире ({0}/{1})", "Registered ({0}/{1})" }, new[] { "TourUnregistered", "Регистрация отменена", "Unregistered" },
            new[] { "TourNeed2", "Нужно хотя бы 2 игрока", "Need at least 2 players" }, new[] { "NeedArenas", "Нужен плагин Arenas и арены типа match", "Arenas plugin with match arenas required" },
            new[] { "TourStarted", "Турнир «{0}» начался! /tour — сетка", "Tournament '{0}' has started! /tour" },
            new[] { "TourMatchStart", "Твой турнирный матч против {0}!", "Your tournament match vs {0}!" },
            new[] { "NoShow", "неявка", "no-show" }, new[] { "TourPrize", "приз турнира", "tournament prize" },
            new[] { "TourChampion", "{0} — чемпион турнира «{1}»!", "{0} won '{1}'!" }, new[] { "TourCancelled", "Турнир отменён", "Tournament cancelled" },
            new[] { "TourTag", "турнир", "tournament" }, new[] { "RankedTag", "ранкед", "ranked" },

            new[] { "SpecTitle", "СМОТРЕТЬ", "SPECTATE" }, new[] { "SpecSub", "идущие матчи · /spec · стрелки переключают игрока", "live matches · /spec · arrows switch player" },
            new[] { "Watch", "СМОТРЕТЬ", "WATCH" }, new[] { "NothingLive", "Сейчас никто не играет матч", "No live matches" },
            new[] { "Matches", "Матчи", "Matches" }, new[] { "Exit", "Выйти", "Exit" },
            new[] { "NothingToWatch", "Некого смотреть", "Nothing to watch" }, new[] { "CantWatch", "Этого игрока сейчас нельзя смотреть", "Can't spectate this player now" },
            new[] { "InCombat", "Нельзя: ты в бою", "Can't: you're in combat" }, new[] { "LeaveArenaFirst", "Сначала выйди с арены (/arena leave)", "Leave the arena first" },
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
