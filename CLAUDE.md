# CLAUDE.md — тестовый Rust-сервер «хаб» (Oxide)

Ты — Claude Code на Windows-ПК владельца. Задача: поднять локальный тестовый сервер Rust
с Oxide и плагинами из `plugins/`, добиться, чтобы **все плагины загрузились без ошибок**,
выдать права и помочь с разметкой. Отвечай владельцу по-русски, коротко.

## Что где
```
plugins/            ← ИСХОДНИКИ плагинов (правишь только здесь, потом sync-plugins.bat)
  Hub.cs            ядро: табличка, лобби, режимы, порталы, NPC, инвентарь на режим, монеты, профиль
  Arenas.cs         FFA, TDM, ган-гейм, дуэли, свои арены (+ турнирные матчи для Social)
  AimTrain.cs       аим-трен с ботами, фейсчеки
  Ranked.cs         рейтинговые матчи, MMR, сезоны
  Shop.cs           скины, титулы, цвет ника, VIP, формат чата
  Social.cs         пати (команда Rust), кланы, турниры, зритель
  Minigames.cs      роум, хели-бои, Bedwars, рейд против рейда
  BuildServer.cs    креатив-стройка: окно с вкладками, симметрия, стаки, авто-двери, зоны-биомы, погода, HUD цены/рейда и панель материала
  Blueprints.cs     библиотека построек (вкладка «Постройки»), нужен CopyPaste.cs с umod.org
  VideoRecorder.cs  съёмки с ботами
  ── сторонние (не наши, правь только если не компилируются) ──
  Skinner.cs        скины (/skin, /skinauto, /skinbase) — магазин продаёт доступ к его командам
  AdminESP.cs       ESP для админов (/ae), право adminesp.use
  GlowingEyes.cs    светящиеся глаза (/eyes), право glowingeyes.use — даёт магазин VIP-игрокам
install.ps1 / install.bat   SteamCMD + сервер (app 258550) + Oxide + плагины + владелец
start.bat           запуск (identity "hub", порт 28015, RCON 28016 пароль из переменной RCON_PASS (по умолчанию CHANGE_ME), лог logs/server.log)
update.bat          обновить сервер и Oxide
sync-plugins.bat    скопировать plugins/*.cs → server/oxide/plugins (Oxide перезагрузит сам)
tools/rcon.mjs      команда в консоль работающего сервера: node tools/rcon.mjs "oxide.reload Hub"
docs/FIRST_RUN.md   права и команды разметки
docs/SERVER_SPEC.md полное ТЗ сервера
```

## Порядок работы

1. **Проверка.** Windows, ≥ 25 ГБ свободно, есть PowerShell. Узнай у владельца SteamID64
   (если не знает — steamid.io по ссылке на профиль).
2. **Установка.** `powershell -ExecutionPolicy Bypass -File install.ps1` (скрипт спросит SteamID64 —
   можно передать заранее, дописав в начало скрипта `$steamId=...` вместо Read-Host). Первая закачка ~8–10 ГБ.
3. **Запуск.** `start.bat` — в отдельном окне/фоне. Сервер грузится 2–5 минут (генерация карты).
   Готов, когда в `logs/server.log` есть строка `Server startup complete`.
4. **Проверка плагинов.** Смотри `logs/server.log` и `server/oxide/logs/oxide_*.txt`:
   - успех: `Loaded plugin Hub v1.0.4 by Claude` (и так по каждому плагину из `plugins/` + CopyPaste);
   - ошибка компиляции: `Error while compiling <Plugin>.cs(<строка>,<колонка>): error CSxxxx: ...`;
   - ошибка в рантайме: `Failed to call hook '<Hook>' on plugin '<Plugin>'` + стек.
5. **Исправление.** Правь файл в `plugins/`, запускай `sync-plugins.bat`, Oxide перекомпилирует сам
   (или `node tools/rcon.mjs "oxide.reload <Plugin>"`). Повторяй, пока все не загрузятся.
6. **Права.** Выполни команды из `docs/FIRST_RUN.md` через `tools/rcon.mjs`.
7. **Отчёт владельцу:** что загрузилось, что исправил (файл, строка, причина), что проверить в игре.

## Правила правок
- Плагины писались без доступа к сборкам Rust — проверены только на заглушках. Самые вероятные ошибки:
  изменённые сигнатуры хуков, переименованные поля/методы Rust (`userID` как EncryptedValue,
  `GetFuelSystem`, `StartSpectating`, `RelationshipManager.PlayerTeam`, `HitInfo.damageTypes`),
  префабы транспорта и атак-хели (`Minigames.cs`, константы Prefab*).
- BuildServer 1.1 (инструменты): хуки `OnPayForPlacement`/`OnPayForUpgrade`, `Vis.Entities`,
  `PrefabAttribute.server.Find<Construction>`, `AttachToBuilding`, `BuildingBlock.grounded`,
  `ItemModDeployable.entityPrefab`; конвары погоды `weather.*`; префабы транспорта в конфиге.
  Авто-двери/окна/люки ставятся в позицию проёма — если в игре висят криво, нужен сдвиг/поворот в `ApplyAuto`.
- Чини **минимально**: ту строку, что сломана. Не переписывай архитектуру, не удаляй функции,
  не меняй стиль интерфейса (вино/пепел, скругления), тексты держи на русском + английском.
- Если не уверен в API текущей версии Rust/Oxide — найди в `server/RustDedicated_Data/Managed/`
  нужную сборку (Assembly-CSharp.dll) и проверь через ildasm/ILSpy или поиск по строкам,
  либо посмотри актуальные плагины на umod.org с тем же хуком.
- После каждого исправления подними версию в `[Info(..., "x.y.z")]` (патч +1).
- Плагины связаны через API: Hub ← все; Arenas ↔ Social (StartTournamentMatch, OnArenasMatchEnd);
  Ranked → Shop/Social (GetBestRank, GetBestMmr, GetTitles); Shop ← Social (GetClanTag).
  Меняешь имя API-метода — меняй во всех вызовах.
- Конфиги появятся в `server/oxide/config/*.json` после первой загрузки. Если меняешь значения
  по умолчанию в коде — старый конфиг их не подхватит: удали json и `oxide.reload <Plugin>`.
- Ничего не удаляй в `server/` кроме того, что просит владелец. Пароль RCON — только для локального теста.

## Полезные команды консоли
```
oxide.plugins                 список и статус плагинов
oxide.reload <Plugin>         перезагрузить
oxide.grant group admin <perm>
ownerid <SteamID64> ; server.writecfg
status                        кто онлайн
```

## Уже сделано на этом ПК (не откатывать)
- VideoRecorder 3.5.0: интерфейс переписан один в один по доскам артефакта (панель 440×680 слева, док таймлайна справа снизу, HUD записи/мотора/воспроизведения, окно приглашения). Бэкап 3.4.14: plugins_backup_2026-11-24/vr_3.4.14/. При первой загрузке плагин сам пересоздаёт oxide/lang/*/VideoRecorder.json (ключ LangVer) — это нормально.
Локальные правки под текущий Rust уже влиты в плагины: `StoreByCrc`, `SendChanges()`, `IsReceivingSnapshot` (свойство),
`SetHealth(MaxHealth())`, `StartSetFlags`, `ObjectCreationHandling.Replace` при чтении конфигов, креатив по праву в BuildServer,
`/god ник`, `/tp ник`, вход сразу в режим (Hub `JoinMode`), `bind p hub.menu`. Свои плагины ItemWear и RadminAuth не трогаются.
Резервная копия прошлых версий — в `plugins_backup_*`.
