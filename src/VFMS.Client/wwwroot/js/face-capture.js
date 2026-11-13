// Face capture for FaceID enrolment (UC-43/UC-52).
// Runs face detection in the browser; only the 128-number descriptor is returned to Blazor.
// The camera frame is never uploaded or stored.
(function () {
    const LIB_URL = "https://cdn.jsdelivr.net/npm/@vladmandic/face-api@1.7.13/dist/face-api.js";
    const MODEL_URL = "https://cdn.jsdelivr.net/npm/@vladmandic/face-api@1.7.13/model/";
    let stream = null;
    let loading = null;

    function loadLibrary() {
        if (window.faceapi) return Promise.resolve();
        if (loading) return loading;
        loading = new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = LIB_URL;
            script.async = true;
            script.onload = resolve;
            script.onerror = () => { loading = null; reject(new Error("library")); };
            document.head.appendChild(script);
        }).then(() => Promise.all([
            faceapi.nets.tinyFaceDetector.loadFromUri(MODEL_URL),
            faceapi.nets.faceLandmark68Net.loadFromUri(MODEL_URL),
            faceapi.nets.faceRecognitionNet.loadFromUri(MODEL_URL)
        ]));
        return loading;
    }

    window.vfmsFace = {
        async start(video) {
            await loadLibrary();
            stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "user", width: 640, height: 480 }, audio: false });
            video.srcObject = stream;
            await video.play();
            return true;
        },
        async capture(video) {
            if (!window.faceapi || !stream) return null;
            const result = await faceapi
                .detectSingleFace(video, new faceapi.TinyFaceDetectorOptions({ inputSize: 416, scoreThreshold: 0.6 }))
                .withFaceLandmarks()
                .withFaceDescriptor();
            return result ? Array.from(result.descriptor) : null;
        },
        stop() {
            if (stream) stream.getTracks().forEach(t => t.stop());
            stream = null;
        }
    };
})();
