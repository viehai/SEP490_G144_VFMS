// Hỗ trợ đóng modal workspace bằng phím Esc.
window.vfmsModal = (function () {
    const handlers = new Map();

    function onKeyDown(event) {
        if (event.key !== 'Escape') return;
        const entries = Array.from(handlers.entries());
        if (entries.length === 0) return;
        // Chỉ đóng modal đang mở gần nhất.
        const [id, dotNetRef] = entries[entries.length - 1];
        handlers.delete(id);
        dotNetRef.invokeMethodAsync('CloseFromEscape').catch(() => { });
    }

    document.addEventListener('keydown', onKeyDown);

    return {
        registerEscape(id, dotNetRef) {
            handlers.set(id, dotNetRef);
        },
        unregisterEscape(id) {
            handlers.delete(id);
        }
    };
})();
