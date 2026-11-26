// Hands-free kiosk scanning (UC-44/45). Needs face-capture.js (camera + face models) and qr-code.js (jsQR) loaded.
// Each call looks at one camera frame; only a QR token or a 128-number face descriptor goes back to Blazor.
(function () {
    const canvas = document.createElement("canvas");
    const ctx = canvas.getContext("2d", { willReadFrequently: true });
    const options = () => new faceapi.TinyFaceDetectorOptions({ inputSize: 416, scoreThreshold: 0.5 });
    // Same person when the descriptors are this close (face-api's usual cut-off).
    const SAME_PERSON = 0.6;

    function readQr(video) {
        if (!video.videoWidth || !window.jsQR) return null;
        canvas.width = video.videoWidth;
        canvas.height = video.videoHeight;
        ctx.drawImage(video, 0, 0);
        const code = window.jsQR(ctx.getImageData(0, 0, canvas.width, canvas.height).data, canvas.width, canvas.height);
        return code && code.data ? code.data.trim() : null;
    }

    // Beep with WebAudio, so no sound file is needed. The kiosk starts by itself, so the browser may keep audio
    // suspended until someone touches the screen or a key; resume on the first touch and stay silent until then.
    let audio = null;
    function audioContext() {
        const Ctx = window.AudioContext || window.webkitAudioContext;
        if (!audio && Ctx) audio = new Ctx();
        return audio;
    }
    ["pointerdown", "keydown"].forEach(type =>
        window.addEventListener(type, () => { const a = audioContext(); if (a && a.state === "suspended") a.resume(); }, { once: true }));

    window.vfmsKiosk = {
        // Short 880 Hz beeps: 1 = check-in, 2 = check-out. Never throws.
        beep(times) {
            try {
                const a = audioContext();
                if (!a) return false;
                if (a.state === "suspended") a.resume();
                for (let i = 0; i < times; i++) {
                    const start = a.currentTime + i * 0.25;
                    const tone = a.createOscillator();
                    const gain = a.createGain();
                    tone.frequency.value = 880;
                    gain.gain.setValueAtTime(0.0001, start);
                    gain.gain.exponentialRampToValueAtTime(0.3, start + 0.01);
                    gain.gain.exponentialRampToValueAtTime(0.0001, start + 0.15);
                    tone.connect(gain).connect(a.destination);
                    tone.start(start);
                    tone.stop(start + 0.16);
                }
                return true;
            } catch (e) {
                return false;
            }
        },

        // Loads the QR reader so look() can use it.
        async ready() {
            await window.vfmsQr.loadScanner();
            return true;
        },

        // { qr } when a QR code is in view, else { face } when a face is, else null.
        async look(video) {
            const qr = readQr(video);
            if (qr) return { qr };
            if (!window.faceapi || !video.videoWidth) return null;
            const found = await faceapi.detectSingleFace(video, options()).withFaceLandmarks().withFaceDescriptor();
            return found ? { face: Array.from(found.descriptor) } : null;
        },

        // True while whoever was just scanned is still in front of the camera.
        // After a QR scan any face counts as them (they are holding the phone); after a face scan only the same face does.
        async stillThere(video, qr, face) {
            const code = readQr(video);
            if (code) return code === qr;
            if (!window.faceapi || !video.videoWidth) return false;
            if (!face) return !!(await faceapi.detectSingleFace(video, options()));
            const found = await faceapi.detectSingleFace(video, options()).withFaceLandmarks().withFaceDescriptor();
            return !!found && faceapi.euclideanDistance(found.descriptor, face) < SAME_PERSON;
        }
    };
})();
