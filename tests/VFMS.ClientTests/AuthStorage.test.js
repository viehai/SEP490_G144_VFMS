const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");
const { webcrypto } = require("node:crypto");

const script = fs.readFileSync(path.join(__dirname, "../../src/VFMS.Client/wwwroot/js/auth.js"), "utf8");

function storage() {
    const values = new Map();
    return {
        getItem: key => values.get(key) ?? null,
        setItem: (key, value) => values.set(key, value),
        removeItem: key => values.delete(key)
    };
}

function browser(localStorage = storage()) {
    let cookie = "";
    const document = {
        get cookie() { return cookie; },
        set cookie(value) { cookie = value.includes("Max-Age=0") ? "" : value.split(";")[0]; }
    };
    return {
        localStorage,
        tab(sessionStorage = storage()) {
            const window = {};
            vm.runInNewContext(script, { window, document, localStorage, sessionStorage, crypto: webcrypto,
                location: new URL("https://vfms.test"), TextEncoder, TextDecoder, atob, btoa });
            return window.vfmsAuthStorage;
        }
    };
}

test("a temporary login is shared within the browser session and unreadable after it ends", async () => {
    const profile = browser();
    const first = profile.tab(), second = profile.tab();
    const value = '{"User":{"RefreshToken":"sample-token"}}';
    await first.write(value, false);
    assert.equal(await second.read(), value);
    assert.ok(!profile.localStorage.getItem("vfms.auth").includes("sample-token"));
    assert.equal(await browser().tab().read(), null);
    assert.equal(await browser(profile.localStorage).tab().read(), null);
    second.clear();
    assert.equal(await first.read(), null);
});

test("a legacy tab shares its login and a remembered login survives a browser restart", async () => {
    const profile = browser(), legacy = storage();
    legacy.setItem("vfms.auth", "old-session");
    assert.equal(await profile.tab(legacy).read(), "old-session");
    assert.equal(await profile.tab().read(), "old-session");
    assert.equal(legacy.getItem("vfms.auth"), null);
    await profile.tab().write("remembered-session", true);
    const reopened = browser(profile.localStorage).tab();
    assert.equal(await reopened.read(), "remembered-session");
    reopened.clear();
    legacy.setItem("vfms.auth", "stale-session");
    assert.equal(await profile.tab(legacy).read(), null);
});
