// =====================================================================================
//  RadminAuth — вход друзей через Radmin VPN (и похожие VPN) на тестовый сервер
// =====================================================================================
//  Steam выписывает билет входа на адрес, по которому клиент подключается. Адреса Radmin
//  (26.x.x.x) выглядят для Steam публичными и не совпадают с настоящим внешним IP сервера,
//  поэтому вход отклоняется: "Steam Auth Error: NetworkIdentityFailure".
//
//  Плагин пропускает ТОЛЬКО эту ошибку и ТОЛЬКО для адресов из списка сетей в конфиге
//  (по умолчанию 26. — Radmin). Баны, VAC, поддельные и повторные билеты проверяются как обычно.
//  Выключить: "Включено": false в oxide/config/RadminAuth.json и oxide.reload RadminAuth.
// =====================================================================================
using System.Collections.Generic;
using HarmonyLib;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("RadminAuth", "Claude", "1.0.0")]
    [Description("Пускает игроков из Radmin VPN, которых Steam отклоняет с NetworkIdentityFailure")]
    public class RadminAuth : RustPlugin
    {
        private class Configuration
        {
            [JsonProperty("Включено")] public bool Enabled = true;
            [JsonProperty("Сети VPN (начало адреса)")] public List<string> Prefixes = new List<string> { "26." };
        }

        private static Configuration _config;

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                Config.Settings.ObjectCreationHandling = ObjectCreationHandling.Replace;
                _config = Config.ReadObject<Configuration>();
                if (_config == null || _config.Prefixes == null) throw new System.Exception();
            }
            catch
            {
                PrintWarning("Конфиг повреждён — создаю новый");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private void Unload() => _config = null;

        private static bool AllowedIp(string ip)
        {
            if (_config == null || !_config.Enabled || string.IsNullOrEmpty(ip)) return false;
            foreach (var p in _config.Prefixes)
                if (!string.IsNullOrEmpty(p) && ip.StartsWith(p)) return true;
            return false;
        }

        [AutoPatch]
        [HarmonyPatch(typeof(Auth_Steam), "ValidateConnecting")]
        private static class ValidateConnectingPatch
        {
            [HarmonyPrefix]
            private static void Prefix(ulong steamid, ref AuthResponse response)
            {
                if (response != AuthResponse.NetworkIdentityFailure) return;
                var conn = Auth_Steam.waitingList.Find(x => x.userid == steamid);
                if (conn == null) return;
                var ip = conn.IPAddressWithoutPort();
                if (!AllowedIp(ip)) return;
                response = AuthResponse.OK;
                Interface.Oxide.LogInfo($"[RadminAuth] {ip}/{steamid}: NetworkIdentityFailure пропущен (VPN)");
            }
        }
    }
}
