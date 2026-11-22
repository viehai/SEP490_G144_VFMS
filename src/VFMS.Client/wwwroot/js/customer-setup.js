(() => {
    const setupPath = new URL("auth/setup-password", document.baseURI).pathname;

    function getSetupUrl() {
        const url = new URL(window.location.href);
        return url.pathname.replace(/\/$/, "").toLowerCase() === setupPath.toLowerCase() ? url : null;
    }

    function captureFragment() {
        const url = getSetupUrl();
        if (url === null) {
            return null;
        }

        if (url.hash || url.search) {
            window.history.replaceState(window.history.state, "", setupPath);
        }

        return url.hash || null;
    }

    let initialFragment = captureFragment();
    window.vfmsCustomerSetup = {
        takeFragment() {
            const fragment = captureFragment() ?? initialFragment;
            initialFragment = null;
            return fragment;
        }
    };

    window.addEventListener("hashchange", () => {
        // Pasting a fresh setup link into this page can be a same-document navigation.
        if (getSetupUrl()?.hash) {
            window.location.reload();
        }
    });
})();
