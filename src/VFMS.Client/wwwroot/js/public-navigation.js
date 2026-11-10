const navigationStates = new Map();

export function sync(ownerId, nav, isHome) {
    if (!nav) return;
    let state = navigationStates.get(ownerId);
    if (state && state.nav !== nav) {
        dispose(ownerId);
        state = null;
    }
    if (!state) {
        state = { nav, isHome, mode: null, frame: null, update: null, onScroll: null };
        state.update = () => {
            const scrolled = window.scrollY > 24;
            const mode = scrolled ? 'floating' : 'resting';
            if (mode === state.mode) return;
            state.mode = mode;

            nav.classList.toggle('is-scrolled', scrolled);
        };
        state.onScroll = () => {
            if (state.frame !== null) return;
            state.frame = window.requestAnimationFrame(() => {
                state.frame = null;
                state.update();
            });
        };
        navigationStates.set(ownerId, state);
        window.addEventListener('scroll', state.onScroll, { passive: true });
    }
    if (state.isHome !== isHome) state.mode = null;
    state.isHome = isHome;
    state.update();
}

export function dispose(ownerId) {
    const state = navigationStates.get(ownerId);
    if (!state) return;
    window.removeEventListener('scroll', state.onScroll);
    if (state.frame !== null) window.cancelAnimationFrame(state.frame);
    navigationStates.delete(ownerId);
}
