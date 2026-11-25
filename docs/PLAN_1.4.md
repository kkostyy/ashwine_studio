# План 1.4 — плагины из списка (ответы владельца, 2026-11-25)

Подход: **готовые бесплатные с umod.org ставим как есть, остальное пишем сами** в стиле вино/пепел.

## Ставим готовые (umod.org, бесплатные)
| Плагин | Зачем | Связь с нашими |
|---|---|---|
| Copy Paste | копия/вставка построек | обязателен для Blueprints |
| Zone Manager | зоны с флагами (без урона, без стройки и т. п.) | можно привязать лобби/арены |
| Custom Spawn Points | свои точки появления | спавн в лобби хаба |
| Image Library | картинки для UI | нужна Sign Artist и др. |
| Sign Artist | картинки по ссылке на табличках | логотип и правила в лобби |
| Admin Radar | ESP/радар для админов | рядом с нашим AdminESP |
| Vanish | невидимость админа | — |
| Player Administration | окно управления игроком | — |
| Entity Editor | двигать/вращать объекты | разметка лобби |

Кладём в `third_party/` (в .gitignore не попадают только наши), ставим через `sync-plugins.bat`.
Перед установкой проверить на umod.org актуальность под текущий Rust.

## Пишем сами
1. **NPC куда смотришь** (вместо Monument Addons / BotSpawn / Human NPC / Npc Kits):
   `/npc` — ставит неподвижного NPC в точку прицела, в одежде и с оружием
   (твой инвентарь или набор), сохраняется, `/npc remove`, `/npc list`.
2. **Combat Log UI** — после смерти окно: кто, чем, куда, урон, дистанция, ХП убийцы.
3. **Recoil Training** — режим в аим-трене: стрельба в стену, эталон отдачи и твой рисунок.
4. **Custom Emotes** — жесты/танцы из меню, можно продавать в магазине.
5. **Match Replay** — запись матча ранкеда/арены и просмотр (на базе записи VideoRecorder).
6. **Event Statistics** — статистика по режимам и ивентам, сколько играли, победы, топы.

## Усиливаем своё (владелец отметил «заменить» — делаем на уровне готовых)
- Зритель на аренах (Arena Spectator): свободная камера, переключение игроков, HUD с ХП.
- Турнирные сетки (Tournament Brackets): сетка на 8/16/32, ставки монетами, автостарт.
- Статистика аима (Aim Statistics): по оружию, история сессий, график точности.
- Достижения (Achievements UI): отдельное окно с категориями и прогрессом.
- Кинокамера (Cinematic Camera): ключевые точки, кривые скорости, фокус на цель.

## Не берём
BotSpawn, Human NPC, Npc Kits, Monument Addons (заменяет свой /npc), Custom Map Markers, No Escape,
Hit Markers, Damage Numbers, GUI Announcements (+API), Lobby Minigames, Parkour Timer, Daily Challenges,
Build Protection, Raid Simulator, Build Sharing / Blueprint Gallery, Weather/Time Controller,
Scene Manager, NPC Animation Controller.
