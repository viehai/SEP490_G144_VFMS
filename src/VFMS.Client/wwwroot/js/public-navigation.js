const navigationStates = new Map();

// Top of page: transparent, full-width
const restingClasses = ['top-0', 'w-full', 'h-20', 'px-6', 'md:px-10', 'bg-transparent', 'border-b', 'border-transparent', 'shadow-none'];

// Scrolled down: Warm Ivory Frosted Glass, clean & elegant
const floatingClasses = ['top-0', 'w-full', 'h-16', 'px-6', 'md:px-10', 'bg-[#F7F4EC]/95', 'backdrop-blur-md', 'border-b', 'border-[#D8DED2]', 'shadow-sm'];

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
            const scrolled = window.scrollY > 40;
            const mode = scrolled ? 'floating' : 'resting';
            if (mode === state.mode) return;
            state.mode = mode;

            if (mode === 'floating') {
                nav.classList.remove(...restingClasses);
                nav.classList.add(...floatingClasses);
            } else {
                nav.classList.remove(...floatingClasses);
                nav.classList.add(...restingClasses);
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
