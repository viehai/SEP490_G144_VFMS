window.vfmsMembershipUpdates = (() => {
    const topic = 'vfms.memberships.paid';
    return {
        publish() {
            if (typeof BroadcastChannel !== 'undefined') {
                const channel = new BroadcastChannel(topic);
                channel.postMessage('paid');
                channel.close();
            } else {
                localStorage.setItem(topic, 'paid');
                localStorage.removeItem(topic);
            }
        },
        subscribe(page) {
            const refresh = () => page.invokeMethodAsync('ReloadAfterPurchase');
            if (typeof BroadcastChannel !== 'undefined') {
                const channel = new BroadcastChannel(topic);
                channel.onmessage = refresh;
                return { dispose: () => channel.close() };
            }
            const onStorage = event => {
                if (event.key === topic && event.newValue === 'paid') refresh();
            };
            window.addEventListener('storage', onStorage);
            return { dispose: () => window.removeEventListener('storage', onStorage) };
        }
    };
})();
