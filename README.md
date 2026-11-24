# ASH & WINE | Studio

Rust-хаб на Oxide: стройка, чертежи баз, арены, ранкед, аим-трен, мини-игры, магазин и VideoRecorder — студия съёмок с ботами-дублями.
Общий стиль интерфейса — тёмное стекло и винный акцент `#9e1530`.

## Что здесь

| Путь | Что это |
|---|---|
| `plugins/` | наши Oxide-плагины (C#) — копируются в `server/oxide/plugins/` |
| `docs/SERVER_SPEC.md` | описание всех режимов и плагинов |
| `docs/FIRST_RUN.md` | первый запуск: права, разметка зон |
| `docs/design/videorecorder/` | макет интерфейса VideoRecorder: 14 досок, эталонные PNG, задание на пересборку артефакта |
| `install.bat` / `install.ps1` | ставят SteamCMD, Rust Dedicated и Oxide в `server/` |
| `start.bat` | запуск сервера (порт 28015, RCON 28016) |
| `sync-plugins.bat` | копирует `plugins/` в сервер |
| `update.bat` | обновляет Rust и Oxide |
| `tools/rcon.mjs` | RCON-клиент для команд из терминала |
| `third_party/` | список сторонних плагинов, которые ставятся отдельно |
| `CLAUDE.md`, `AGENTS.md` | инструкции для агента (Claude Code и др.) |

## Быстрый старт (Windows)

1. `install.bat` — скачает сервер и Oxide (~10 ГБ).
2. Поставь сторонние плагины из `third_party/README.md`.
3. Задай пароль RCON: `setx RCON_PASS "твой_пароль"` (иначе будет `CHANGE_ME`).
4. `sync-plugins.bat`, затем `start.bat`.
5. В игре: F1 → `client.connect localhost:28015`.

С Claude Code: открой папку и напиши «Подними сервер по CLAUDE.md».
