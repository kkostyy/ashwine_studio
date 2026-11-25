# Первый запуск: права и разметка

## 1. Права (в консоли сервера или `node tools/rcon.mjs "..."`)

Админ-группа получает всё:
```
oxide.grant group admin hub.admin
oxide.grant group admin arenas.admin
oxide.grant group admin aimtrain.admin
oxide.grant group admin ranked.admin
oxide.grant group admin shop.admin
oxide.grant group admin social.admin
oxide.grant group admin minigames.admin
oxide.grant group admin buildserver.admin
oxide.grant group admin blueprints.all
oxide.grant group admin blueprints.bypass
oxide.grant group admin videorecorder.use
oxide.grant group admin videorecorder.paths
oxide.grant group admin videorecorder.world
oxide.grant group admin videorecorder.kits
oxide.grant group admin buildserver.weather
oxide.grant group admin adminesp.use
oxide.grant group admin skinner.import
oxide.grant group admin skinner.skinrequest
oxide.grant group admin glowingeyes.use
oxide.usergroup add <SteamID64> admin
```

Права стройки и съёмок **не выдаём группе default** — Hub сам выдаёт их через группы
`hub_build` и `hub_film`, пока игрок в своём режиме. Если где-то раньше выдавали всем:
```
oxide.revoke group default buildserver.use
oxide.revoke group default buildserver.fly
oxide.revoke group default buildserver.god
oxide.revoke group default buildserver.items
oxide.revoke group default videorecorder.use
```

Встроенный креатив Rust из BuildServer действует на весь сервер. Для хаба в
`server/oxide/config/BuildServer.json` поставь `"Встроенный креатив: для всех игроков (creative.allusers)": false`
и `oxide.reload BuildServer` — полёт и бесплатные предметы в «Стройке» и так дают права.

## 2. Разметка (стоишь на месте точки, пишешь в чат)

Порядок: лобби → режимы → арены → ранкед → аим → мини-игры. План — на странице «Карта» в артефакте.

```
/hubadmin lobby                      × 6 (центр лобби)
/hubadmin portal ffa 2               и так для каждого режима
/hubadmin spawn ffa                  зал каждого режима (куда приводит портал)
/hubadmin npc cmd:shop Торговец
/hubadmin show · /hubadmin list

/arenaadmin create ak1 ffa · spawn ak1 (×8) · loadout ak1 ak · radius ak1 50
/arenaadmin create sklad match · spawna sklad · spawnb sklad · barricades sklad on
/rankedadmin create r1 · a r1 (×5) · b r1 (×5) · radius r1 50
/aimadmin lane lane1 14 · room fc1 door · a fc1 · b fc1 · wall fc1
/mgadmin create roam1 roam 400 · spawn roam1 0 (×15)

# Зоны-биомы стройки (вкладка «Зоны» в /build). 9 зон уже есть в конфиге — нужны только точки участков:
/bsadmin grid snow 6 6        стоишь в углу снежной области — сетка 6×6 участков (шаг = размер + 10 м)
/bsadmin grid grass 6 6 · /bsadmin grid sand 6 6
/bsadmin grid vsnow 2 2 · vgrass · vsand · lake · cave · swamp   (VIP, право shop.vip)
/bsadmin slot <зона>          один участок точно здесь (для пещер/озёр)
/bsadmin zone <ключ> <snow|grass|sand|forest|swamp|cave|lake> <размер> [vip] [Название] — новая зона
/bsadmin zones                 список и сколько свободно
```

## 3. Скины (Skinner)
Скины выбирает Skinner: /skin с предметом в руке. Магазин (/shop → Скины) продаёт доступ:
/skin (skinner.default), авто-скины (skinner.craft + skinner.skinauto), весь инвентарь
(skinner.skininv + skinner.skincon), скины построек (skinner.items + skinner.skinbase).
Цены и набор прав — в oxide/config/Shop.json. Свои скины-коллекции: /colimport <ID коллекции>.
Встроенные скины магазина при загруженном Skinner выключаются сами.
Если хочешь скины всем бесплатно: oxide.grant group default skinner.default

## 4. Окно стройки (/build)
Вкладки: Инструменты · Постройки (Blueprints) · Зоны · Окружение · Предметы · Рейд-тест · Телепорт.
Погода во «Окружении» общая на весь сервер — меняет только право buildserver.weather.
Своё время суток каждый ставит себе сам.
Картинки зон: в `oxide/config/BuildServer.json` у зоны поле «Картинка (URL)» — ссылка на png/jpg.

## 5. Турниры, достижения, размер HUD
/tour create Кубок 8 ak 3 1000            — турнир 1 на 1 (как раньше)
/tour create Кубок_пар 8 ak 3 2000 2v2    — командный: капитан пати пишет /tour join, пати ровно 2 чел.
/tour create Клановый 8 ak 3 3000 5v5 clan — только пати из одного клана
Приз делится поровну между игроками команды-победителя.
Достижения (24 шт.) — в профиле /profile, за них монеты и титулы (титулы выбираются в /shop).
Размер HUD: игрок сам — в профиле (S / M / L) или /hud s|m|l. По умолчанию M (80%).

## 6. Подключение из игры
F1 → `client.connect localhost:28015` (с другого ПК — твой IP; порты 28015/28017 UDP открыть в роутере).

## 7. Готовые плагины с umod (план 1.4)
Запусти `get-umod.bat` — скачает CopyPaste, ZoneManager, Spawns + CustomSpawnPoints, ImageLibrary, SignArtist,
AdminRadar, Vanish, PlayerAdministration, Telekinesis (вместо Entity Editor — на umod такого нет).
Права админам:
```
oxide.grant group admin adminradar.allowed
oxide.grant group admin vanish.allow
oxide.grant group admin playeradministration.access.show
oxide.grant group admin telekinesis.use
oxide.grant group admin signartist.url
oxide.grant group admin zonemanager.zone
oxide.grant group admin permissionsmanager.allowed
oxide.grant group default buildinggrades.use
oxide.grant group default removertool.normal
oxide.grant group default betternostability.use
oxide.grant group default quicksort.use
oxide.grant group default furnacesplitter.use
```
BuildingGrades, RemoverTool и BetterNoStability нужны в первую очередь на стройке — если хочешь только там,
выдавай их не группе default, а группе стройки. BetterNoStability требует `server.stability true`.
InstantCraft делает крафт мгновенным на всём сервере (в рейд-тесте стройки он и так мгновенный и бесплатный).
NoDecay: постройки не гниют; то же можно сделать конваром `decay.scale 0`.
Скины построек: уже есть через Skinner (/skinbase, продаётся в магазине) — отдельный Building Skins не ставим.
Точки появления: `/spawns new` → `/spawns add` (в нужных местах) → `/spawns save lobby` → в консоли `spawns.config lobby`.

## 8. Новое 1.4 (своё)
/npc                — NPC там, куда смотришь (в твоей одежде и с оружием в руке); /npc kit <набор>, /npc remove, /npc list
/emote или кнопка в меню P → Профиль · Жесты — жесты и танцы (часть продаётся в /shop)
/replay             — записи последних матчей ранкеда и арен, просмотр «призраками»
/aim recoil         — тренажёр отдачи
Окно после смерти (Combat Log) включается само: урон по тебе за последние 10 с.
