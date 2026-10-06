// Browsers started by the test fixtures have already "seen" the home popup (as a returning visitor has), so it does
// not cover what the older scenarios click. The popup itself is checked by its own test with a fresh context.
const fs = require('fs');
const path = require('path');

const BASE = process.env.SH_E2E_BASE_URL || 'http://localhost:5173';
const STATE = path.join(__dirname, '.state', 'popup-seen.json');

module.exports = async () => {
  fs.mkdirSync(path.dirname(STATE), { recursive: true });
  fs.writeFileSync(STATE, JSON.stringify({
    cookies: [],
    origins: [{ origin: new URL(BASE).origin, localStorage: [{ name: 'sh_popup_seen', value: JSON.stringify({ at: Date.now() }) }] }],
  }));
};

module.exports.STATE = STATE;
