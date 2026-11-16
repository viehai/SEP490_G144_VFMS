window.vfmsInvoices = {
    async download(fileName, contentType, streamReference) {
        const content = await streamReference.arrayBuffer();
        const url = URL.createObjectURL(new Blob([content], { type: contentType }));
        const link = document.createElement("a");
        try {
            link.href = url;
            link.download = fileName;
            document.body.appendChild(link);
            link.click();
        } finally {
            link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 0);
        }
    }
};
