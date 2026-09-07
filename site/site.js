const config = window.VALTRANS_CONFIG || {};
const fairy = document.querySelector('#fairy-link');
if (fairy && config.fairyProjectUrl) fairy.href = config.fairyProjectUrl;
const download = document.querySelector('#download-link');
if (download && config.downloadUrl) download.href = config.downloadUrl;
