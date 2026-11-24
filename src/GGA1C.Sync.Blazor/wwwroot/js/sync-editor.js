// Скачивание файла из Blazor Server: байты приходят base64 по каналу SignalR, в браузере
// собираются в blob и отдаются ссылкой. Задача «ФАЙЛ» потом заменит это отдачей с NAS.
window.syncEditor = {
    downloadFile: function (fileName, base64) {
        const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
        const blob = new Blob([bytes], { type: 'application/octet-stream' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
    }
};
