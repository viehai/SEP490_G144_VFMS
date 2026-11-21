const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const test = require("node:test");
const vm = require("node:vm");

const clientScript = fs.readFileSync(
    path.resolve(__dirname, "../../src/VFMS.Client/wwwroot/js/customer-setup.js"),
    "utf8"
);
const origin = "https://client.example.test";
const setupRoute = "/auth/setup-password";

function loadPage(relativeUrl) {
    const state = { _index: 2 };
    const replacements = [];
    const listeners = new Map();
    let reloads = 0;
    const window = {
        location: {
            href: origin + relativeUrl,
            reload() { reloads++; }
        },
        history: {
            state,
            replaceState(replacementState, title, url) {
                replacements.push({ replacementState, title, url });
                window.location.href = new URL(url, window.location.href).href;
            }
        },
        addEventListener(event, handler) {
            listeners.set(event, handler);
        }
    };

    vm.runInNewContext(clientScript, { URL, window, document: { baseURI: origin + "/" } });
    return {
        state,
        replacements,
        window,
        setFragment(fragment) {
            window.location.href = new URL(fragment, window.location.href).href;
            listeners.get("hashchange")();
        },
        get reloads() { return reloads; }
    };
}

test("replaces the token URL before the page takes the fragment and clears the in-memory handoff", () => {
    const fragment = "#token=" + "A".repeat(64);
    const page = loadPage(setupRoute + fragment);

    assert.equal(page.window.location.href, origin + setupRoute);
    assert.deepEqual(page.replacements, [{ replacementState: page.state, title: "", url: setupRoute }]);
    assert.equal(page.window.vfmsCustomerSetup.takeFragment(), fragment);
    assert.equal(page.window.vfmsCustomerSetup.takeFragment(), null);
    assert.equal(page.replacements.length, 1);
});

test("refresh of the cleaned URL has no token", () => {
    const page = loadPage(setupRoute + "#token=" + "A".repeat(64));
    const refreshed = loadPage(new URL(page.window.location.href).pathname);

    assert.equal(refreshed.window.vfmsCustomerSetup.takeFragment(), null);
    assert.equal(refreshed.replacements.length, 0);
});

test("cleans a query-only URL without accepting its token", () => {
    const page = loadPage(setupRoute + "?token=" + "A".repeat(64));

    assert.equal(page.window.location.href, origin + setupRoute);
    assert.equal(page.window.vfmsCustomerSetup.takeFragment(), null);
});

test("leaves other pages' query and fragment untouched", () => {
    const otherRoute = "/auth/login?returnUrl=%2F#help";
    const page = loadPage(otherRoute);

    assert.equal(page.window.location.href, origin + otherRoute);
    assert.equal(page.window.vfmsCustomerSetup.takeFragment(), null);
    assert.equal(page.replacements.length, 0);
    page.setFragment("#other-help");
    assert.equal(page.reloads, 0);
});

test("opens a newly pasted setup link when the clean setup page is already open", () => {
    const page = loadPage(setupRoute);
    page.setFragment("#token=" + "B".repeat(64));

    assert.equal(page.reloads, 1);
    assert.equal(page.replacements.length, 0);
});
