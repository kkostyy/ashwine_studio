# VideoRecorder — макет интерфейса (14 досок)

Это выгрузка страницы «VideoRecorder» из артефакта-макета ASH & WINE (тип Design, claude.ai).
По этим доскам сделан интерфейс плагина `plugins/VideoRecorder.cs` 3.5.0.

## Что в папке

| Путь | Что это |
|---|---|
| `boards/*.dc.html` | исходники 14 досок как есть из артефакта (формат Design: `<x-dc>`, шаблоны `{{...}}`, `<sc-for>`) |
| `canvas_vr.json` | раскладка досок на холсте: страница `vr`, x/y/w/h и заголовок каждой доски |
| `assets/bg.jpg` | фон-скриншот Rust, который стоит под панелями (в досках он записан как `url(/_blob/af80c85f1515fda0a5deb874e280c472)`) |
| `png/*.png` | как каждая доска выглядит (эталон для сверки) |
| `gen.py` | генератор, которым доски были сделаны (Python, пишет `*.dc.html`) |
| `preview.html` | открыть в браузере — все 14 эталонов на одной странице |

Акцент: у каждой доски свойство `accent` (`data-props`), по умолчанию `#9e1530`
(варианты `#7a1426`, `#b3263e`, `#5c0f1d`). В разметке это `{{accent}}`.

## Задание для Claude: пересобрать артефакт

Скопируй это сообщение агенту целиком.

> Пересобери макет VideoRecorder как артефакт по файлам из `docs/design/videorecorder/`.
>
> 1. Посмотри, какие типы артефактов тебе доступны (list_types). Если есть тип **Design** — создай новый артефакт из него с названием «ASH & WINE — VideoRecorder» и прочитай его SKILL.md.
> 2. Загрузи `assets/bg.jpg` как ассет артефакта (upload_asset) и во всех `boards/*.dc.html` замени `/_blob/af80c85f1515fda0a5deb874e280c472` на URL нового ассета. Больше в досках ничего не меняй — это готовые исходники того же формата.
> 3. Опубликуй 14 досок в `project/` под теми же именами (`Main.dc.html`, `TakeEditor.dc.html`, …).
> 4. В `project/canvas.json` создай страницу `{"id":"vr","name":"VideoRecorder"}` и для каждой доски запись в `boards` с `page:"vr"`, `x`, `y`, `w`, `h`, `title`, `is_interactive:true` — значения возьми из `canvas_vr.json`. Сначала прочитай живой `canvas.json` нового артефакта и дополни его, не затирая.
> 5. Открой артефакт и сверь каждую доску с `png/<имя>.png`. Если что-то разъехалось — чини доску, а не PNG.
>
> Если типа Design нет — собери один самодостаточный HTML-артефакт: 14 досок по 1280×720 сеткой как в `canvas_vr.json`, разметку возьми из `boards/` (подставь `{{accent}}` → `#9e1530`, разверни `<sc-for>` в обычные элементы), фон — `assets/bg.jpg`.

## Как доски устроены (для правок)

- Панель студии слева 440×680 с отступом 20, док таймлайна справа снизу 780×170 (`Playback` — по центру 680×173).
- Цвета: фон стекла `rgba(18,14,14,.86)`, рамка `rgba(255,255,255,.08)`, текст `#efe9e7`, второстепенный `#a39896`, приглушённый `#8c817f`, акцент `#9e1530`, запись `#d41f3c`, розовый `#f0a6b1`.
- Шрифт Roboto Condensed 400–700.
- Соответствие досок и кода в `VideoRecorder.cs`: Main → `BuildTakes`, TakeEditor → `BuildTake`, Appearance → `BuildLook`, Camera → `BuildCamera`, Effects → `BuildFx`, Scene → `BuildScene`, Weather → `BuildWeather`, Options → `BuildOptions`, Kits → `BuildKits`, Team → `BuildTeam`, Invite → `ShowInvite`, Motor/Recording → `ShowCountdown` + `ShowRecHud`, Playback → `ShowPlayHud`.
