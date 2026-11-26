// Keep keyboard navigation in the topmost workspace dialog.
window.vfmsModal = (() => {
    const dialogs = new Map();
    const controls = 'button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), a[href], [tabindex="0"]';

    document.addEventListener('keydown', event => {
        const current = Array.from(dialogs.values()).at(-1);
        if (!current) return;
        if (event.key === 'Escape') {
            event.preventDefault();
            current.reference.invokeMethodAsync('CloseFromEscape').catch(() => {});
            return;
        }
        if (event.key !== 'Tab') return;
        const items = Array.from(current.panel.querySelectorAll(controls)).filter(item => item.getClientRects().length);
        const first = items[0], last = items.at(-1);
        if (!first) return;
        if (!current.panel.contains(document.activeElement) ||
            (event.shiftKey ? document.activeElement === first : document.activeElement === last)) {
            event.preventDefault();
            (event.shiftKey ? last : first).focus();
        }
    });

    return {
        registerEscape(id, reference) {
            const panel = document.getElementById('workspace-modal-title-' + id)?.closest('[role="dialog"]');
            if (!panel) return;
            const opener = document.activeElement;
            dialogs.set(id, { panel, reference, opener });
            (panel.querySelector('[autofocus]') || panel.querySelector(controls))?.focus();
        },
        unregisterEscape(id) {
            const dialog = dialogs.get(id);
            dialogs.delete(id);
            if (dialog?.opener?.isConnected) dialog.opener.focus();
        }
    };
})();
