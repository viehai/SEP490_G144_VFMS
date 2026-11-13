// Draws a member's check-in QR token as an SVG (UC-44). Only the token text is encoded.
(function () {
    const LIB_URL = "https://cdn.jsdelivr.net/npm/qrcode-generator@1.4.4/qrcode.js";
    let loading = null;

    function loadLibrary() {
        if (window.qrcode) return Promise.resolve();
        return loading ??= new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = LIB_URL;
            script.async = true;
            script.onload = resolve;
            script.onerror = () => { loading = null; reject(new Error("library")); };
            document.head.appendChild(script);
        });
    }

    window.vfmsQr = {
        svg: async function (text) {
            await loadLibrary();
            const qr = window.qrcode(0, "M");
            qr.addData(text);
            qr.make();
            return qr.createSvgTag({ cellSize: 6, margin: 2, scalable: true });
        }
    };
})();
