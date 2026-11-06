const navigationStates = new Map();
const restingClasses = ['top-0', 'w-full', 'h-20', 'px-6', 'md:px-10', 'border-transparent', 'shadow-none', 'rounded-none'];
const floatingClasses = ['top-3', 'md:top-4', 'w-[95%]', 'max-w-7xl', 'h-16', 'px-5', 'md:px-7', 'bg-neutral-800/90', 'backdrop-blur-md', 'border-neutral-600/40', 'border', 'shadow-2xl', 'rounded-xl', 'md:rounded-2xl'];
const backgroundClasses = ['bg-transparent', 'bg-[#0A0A0A]'];

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
            const mode = window.scrollY > 50 ? 'floating' : state.isHome ? 'home' : 'solid';
            if (mode === state.mode) return;
            state.mode = mode;

            if (mode === 'floating') {
                nav.classList.remove(...restingClasses, ...backgroundClasses);
                nav.classList.add(...floatingClasses);
            } else {
                nav.classList.remove(...floatingClasses, ...backgroundClasses);
                nav.classList.add(...restingClasses, state.isHome ? 'bg-transparent' : 'bg-[#0A0A0A]');
            }
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
    // Blazor restores the header's initial classes when changing public pages.
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
