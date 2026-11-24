// =====================================================================================
//  ItemWear — износ вещей для съёмок: «калаш уже стреляли, потом чинили»
// =====================================================================================
//  Любая вещь с прочностью (оружие, инструменты, одежда, броня, модули) получает
//  два значения в процентах от полной полоски:
//    состояние — сколько прочности осталось сейчас;
//    макс      — до куда полоска чинится (после ремонтов в Rust конец полоски красный).
//
//  Чат:  /wear 60 85        — вещь в руках: состояние 60%, после ремонтов максимум 85%
//        /wear 60           — только состояние (максимум не меняется)
//        /wear random       — вещь в руках: случайный «живой» износ
//        /wear new          — вещь в руках как новая
//        /wear all 60 85 · /wear all random · /wear all new — все вещи: пояс, инвентарь, одежда
//        /износ — то же самое
//  Бинд: bind k "wear all random"   (консольная команда wear — те же аргументы)
//  Кому: админ, или есть videorecorder.use (Съёмки), или buildserver.use (Стройка)
// =====================================================================================
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ItemWear", "Claude", "1.0.0")]
    [Description("Износ и ремонт вещей для съёмок: состояние и максимум после ремонтов")]
    public class ItemWear : RustPlugin
    {
        private static readonly string[] AllowedPerms = { "videorecorder.use", "buildserver.use" };

        [ChatCommand("wear")] private void CmdWear(BasePlayer player, string c, string[] a) => Run(player, a);
        [ChatCommand("износ")] private void CmdWearRu(BasePlayer player, string c, string[] a) => Run(player, a);

        [ConsoleCommand("wear")]
        private void CcWear(ConsoleSystem.Arg arg)
        {
            var p = arg.Player();
            if (p == null) return;
            var args = new List<string>();
            for (int i = 0; arg.HasArgs(i + 1); i++) args.Add(arg.GetString(i));
            Run(p, args.ToArray());
        }

        private bool Allowed(BasePlayer p)
        {
            if (p.IsAdmin) return true;
            foreach (var perm in AllowedPerms) if (permission.UserHasPermission(p.UserIDString, perm)) return true;
            return false;
        }

        private void Run(BasePlayer player, string[] a)
        {
            if (player == null) return;
            if (!Allowed(player)) { Reply(player, "Износ доступен в «Съёмках» и «Стройке». / Wear works in Filming and Build."); return; }

            bool all = a.Length > 0 && a[0].ToLower() == "all";
            int i = all ? 1 : 0;
            string mode = a.Length > i ? a[i].ToLower() : null;

            var items = all ? AllItems(player) : Held(player);
            if (mode == null)
            {
                Reply(player, "/wear 60 85 — в руках: 60% состояния, после ремонтов максимум 85%\n/wear random · /wear new · /wear all … — все вещи");
                if (items.Count > 0) Reply(player, Describe(items[0]));
                return;
            }
            if (items.Count == 0) { Reply(player, all ? "Нет вещей с прочностью. / No items with condition." : "Возьми вещь в руки (с прочностью). / Hold an item with condition."); return; }

            int changed = 0;
            if (mode == "new")
                foreach (var it in items) { Apply(it, 1f, 1f); changed++; }
            else if (mode == "random" || mode == "rnd" || mode == "случайно")
                foreach (var it in items)
                {
                    // 40% — ни разу не чинили; остальные — после 1–3 ремонтов
                    float max = Random.value < 0.4f ? 1f : Random.Range(0.65f, 0.95f);
                    Apply(it, Random.Range(0.25f, 1f) * max, max);
                    changed++;
                }
            else
            {
                float cond, max = -1f;
                if (!TryPct(mode, out cond)) { Reply(player, "Проценты числом: /wear 60 85"); return; }
                if (a.Length > i + 1 && !TryPct(a[i + 1], out max)) { Reply(player, "Проценты числом: /wear 60 85"); return; }
                foreach (var it in items)
                {
                    float full = it.info.condition.max;
                    float m = max >= 0f ? max : it.maxCondition / full;
                    Apply(it, cond, Mathf.Max(m, cond));
                    changed++;
                }
            }

            if (all) Reply(player, $"Износ выставлен: {changed} вещей. / Wear set on {changed} items.");
            else Reply(player, Describe(items[0]));
        }

        private static bool TryPct(string s, out float v)
        {
            v = 0f;
            if (!float.TryParse(s.Replace(',', '.').TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
            v = Mathf.Clamp(v, 0f, 100f) / 100f;
            return true;
        }

        // cond и max — доли полной полоски предмета (0..1)
        private static void Apply(Item item, float cond, float max)
        {
            float full = item.info.condition.max;
            item.maxCondition = Mathf.Clamp(full * max, 1f, full);
            item.condition = Mathf.Clamp(full * cond, 0f, item.maxCondition);
            item.MarkDirty();
        }

        private static List<Item> Held(BasePlayer player)
        {
            var list = new List<Item>();
            var it = player.GetActiveItem();
            if (it != null && it.hasCondition) list.Add(it);
            return list;
        }

        private static List<Item> AllItems(BasePlayer player)
        {
            var list = new List<Item>();
            foreach (var c in new[] { player.inventory.containerBelt, player.inventory.containerMain, player.inventory.containerWear })
            {
                if (c == null) continue;
                foreach (var it in c.itemList)
                {
                    if (it.hasCondition) list.Add(it);
                    if (it.contents == null) continue;
                    foreach (var mod in it.contents.itemList) if (mod.hasCondition) list.Add(mod);   // прицелы, глушители
                }
            }
            return list;
        }

        private static string Describe(Item it)
        {
            float full = it.info.condition.max;
            int cond = Mathf.RoundToInt(it.condition / full * 100f), max = Mathf.RoundToInt(it.maxCondition / full * 100f);
            return $"{it.info.displayName.english}: состояние {cond}%, после ремонтов {max}%" + (max < 100 ? $" (красное {100 - max}%)" : " (не чинили)");
        }

        private void Reply(BasePlayer player, string msg) => player.ChatMessage($"<color=#f0a6b1>[Износ]</color> {msg}");
    }
}
