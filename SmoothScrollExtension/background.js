// Background service worker - ensures content script runs on install
chrome.runtime.onInstalled.addListener(() => {
    // Inject content script into all existing tabs immediately
    chrome.tabs.query({}, (tabs) => {
        for (const tab of tabs) {
            if (tab.url && (tab.url.startsWith('http://') || tab.url.startsWith('https://'))) {
                chrome.scripting.executeScript({
                    target: { tabId: tab.id },
                    files: ['content.js']
                }).catch(() => { }); // Silently skip restricted tabs
            }
        }
    });
});
