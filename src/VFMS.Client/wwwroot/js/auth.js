window.vfmsAuthStorage = (() => {
    const storageKey = 'vfms.auth';
    const cookieName = 'vfms.session.' + (location.port || 'default');
    const cookieFlags = '; Path=/; SameSite=Strict' + (location.protocol === 'https:' ? '; Secure' : '');
    let releaseLock;
    let fallback = null;

    function sessionKey() {
        const prefix = cookieName + '=';
        return document.cookie.split('; ').find(cookie => cookie.startsWith(prefix))?.slice(prefix.length);
    }

    const encode = bytes => btoa(String.fromCharCode(...new Uint8Array(bytes)));
    const decode = text => Uint8Array.from(atob(text), c => c.charCodeAt(0));
    const importKey = key => crypto.subtle.importKey('raw', decode(key), 'AES-GCM', false, ['encrypt', 'decrypt']);

    async function write(value, rememberMe) {
        try {
            if (rememberMe) {
                localStorage.setItem(storageKey, value);
                document.cookie = cookieName + '=; Max-Age=0' + cookieFlags;
            } else {
                // Tabs share the ciphertext. Only this browser session holds the key to open it.
                let key = sessionKey();
                if (!key) {
                    key = encode(crypto.getRandomValues(new Uint8Array(32)));
                    document.cookie = cookieName + '=' + key + cookieFlags;
                    if (sessionKey() !== key) throw new Error('Session cookies unavailable');
                }
                const iv = crypto.getRandomValues(new Uint8Array(12));
                const encrypted = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, await importKey(key),
                    new TextEncoder().encode(value));
                localStorage.setItem(storageKey, 'session.' + encode(iv) + '.' + encode(encrypted));
            }
            sessionStorage.removeItem(storageKey);
            fallback = null;
        } catch {
            // Storage-restricted browsers can still use the current tab.
            fallback = value;
            try { localStorage.removeItem(storageKey); } catch { }
            try { sessionStorage.setItem(storageKey, value); } catch { }
        }
    }

    async function read() {
        let shared;
        try { shared = localStorage.getItem(storageKey); } catch { return fallback; }
        if (shared === null) {
            let legacy;
            try { legacy = sessionStorage.getItem(storageKey); } catch { return fallback; }
            if (legacy) await write(legacy, false);
            return legacy ?? fallback;
        }
        if (shared === 'null') return null;
        if (!shared.startsWith('session.')) return shared; // Remembered login, including earlier versions.
        const key = sessionKey();
        if (!key) return null;
        try {
            const [, iv, ciphertext] = shared.split('.');
            const plaintext = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: decode(iv) },
                await importKey(key), decode(ciphertext));
            return new TextDecoder().decode(plaintext);
        } catch { return null; }
    }

    return {
        read, write,
        clear() {
            fallback = null;
            try { sessionStorage.removeItem(storageKey); } catch { }
            // A logout also invalidates legacy per-tab sessions that have not migrated yet.
            try { localStorage.setItem(storageKey, 'null'); } catch { }
            document.cookie = cookieName + '=; Max-Age=0' + cookieFlags;
        },
        watch(session) {
            window.addEventListener('storage', event => {
                if (event.storageArea === localStorage && (event.key === storageKey || event.key === null))
                    session.invokeMethodAsync('BrowserSessionChanged');
            });
        },
        lock() {
            if (!navigator.locks) return;
            return new Promise((acquired, reject) => {
                navigator.locks.request(storageKey, () => new Promise(release => {
                    releaseLock = release;
                    acquired();
                })).catch(reject);
            });
        },
        unlock() {
            releaseLock?.();
            releaseLock = undefined;
        }
    };
})();
