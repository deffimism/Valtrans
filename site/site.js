const config = window.VALTRANS_CONFIG || {};
const fairyLinks = document.querySelectorAll('[data-fairy-link]');
if (config.fairySupportUrl) fairyLinks.forEach((link) => { link.href = config.fairySupportUrl; });
const download = document.querySelector('#download-link');
if (download && config.downloadUrl) download.href = config.downloadUrl;
const downloadPanel = document.querySelector('#download-panel-link');
if (downloadPanel && config.downloadUrl) downloadPanel.href = config.downloadUrl;
