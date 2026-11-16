window.vfmsFreezeDocuments = {
    async download(fileName, contentType, streamReference) {
        const content = await streamReference.arrayBuffer();
        const url = URL.createObjectURL(new Blob([content], { type: contentType }));
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 0);
    }
};
