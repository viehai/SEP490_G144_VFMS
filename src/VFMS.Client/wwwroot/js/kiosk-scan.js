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

    window.vfmsKiosk = {
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
