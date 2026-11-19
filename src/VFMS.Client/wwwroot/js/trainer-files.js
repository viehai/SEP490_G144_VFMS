window.vfmsDownloadFile = async (name, reference) => {
    const url = URL.createObjectURL(new Blob([await reference.arrayBuffer()]));
    const link = document.createElement("a");
    link.href = url;
    link.download = name;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
};
