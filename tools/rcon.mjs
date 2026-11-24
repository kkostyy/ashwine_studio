// Отправка команды в консоль работающего сервера через WebRCON.
// node tools/rcon.mjs "oxide.grant group admin hub.admin"
// Нужен Node 22+ (встроенный WebSocket). Пароль и порт — как в start.bat.
const cmd = process.argv.slice(2).join(" ");
if (!cmd) { console.error('Использование: node tools/rcon.mjs "команда"'); process.exit(1); }
const host = process.env.RCON_HOST || "127.0.0.1", port = process.env.RCON_PORT || "28016", pass = process.env.RCON_PASS || "CHANGE_ME";
const ws = new WebSocket(`ws://${host}:${port}/${pass}`);
const id = Math.floor(Math.random() * 100000) + 1;
const timer = setTimeout(() => { console.error("Нет ответа (сервер запущен? rcon.web 1?)"); process.exit(2); }, 8000);
ws.onopen = () => ws.send(JSON.stringify({ Identifier: id, Message: cmd, Name: "ClaudeCode" }));
ws.onmessage = (e) => {
  const m = JSON.parse(e.data);
  if (m.Identifier !== id) return;
  console.log(m.Message);
  clearTimeout(timer); ws.close(); process.exit(0);
};
ws.onerror = (e) => { console.error("Ошибка подключения к RCON:", e.message || e); process.exit(3); };
