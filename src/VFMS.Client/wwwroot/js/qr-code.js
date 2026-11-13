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
        },

        // Reads a QR code from a playing <video> (the kiosk camera). Returns its text, or null after timeoutMs.
        scan: async function (video, timeoutMs) {
            await loadScanner();
            const canvas = document.createElement("canvas");
            const ctx = canvas.getContext("2d", { willReadFrequently: true });
            const until = Date.now() + timeoutMs;
            while (Date.now() < until) {
                if (video.videoWidth) {
                    canvas.width = video.videoWidth;
                    canvas.height = video.videoHeight;
                    ctx.drawImage(video, 0, 0);
                    const code = window.jsQR(ctx.getImageData(0, 0, canvas.width, canvas.height).data, canvas.width, canvas.height);
                    if (code && code.data) return code.data;
                }
                await new Promise(r => setTimeout(r, 150));
            }
            return null;
        }
    };

    let scannerLoading = null;
    function loadScanner() {
        if (window.jsQR) return Promise.resolve();
        return scannerLoading ??= new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = "https://cdn.jsdelivr.net/npm/jsqr@1.4.0/dist/jsQR.js";
            script.async = true;
            script.onload = resolve;
            script.onerror = () => { scannerLoading = null; reject(new Error("scanner")); };
            document.head.appendChild(script);
        });
    }
})();
