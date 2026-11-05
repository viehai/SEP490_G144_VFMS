window.vfmsAuthStorage = {
    read() {
        try {
            return sessionStorage.getItem('vfms.auth') ?? localStorage.getItem('vfms.auth');
        } catch { return null; }
    },
    write(value, rememberMe) {
        // A browser that disallows storage can still use the in-memory session.
        try {
            sessionStorage.removeItem('vfms.auth');
            localStorage.removeItem('vfms.auth');
            (rememberMe ? localStorage : sessionStorage).setItem('vfms.auth', value);
        } catch { }
    },
    clear() {
        try {
            sessionStorage.removeItem('vfms.auth');
            localStorage.removeItem('vfms.auth');
        } catch { }
    }
};
