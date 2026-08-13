chrome.runtime.onInstalled.addListener(() => {
  chrome.storage.local.set({ mv2BackgroundStartedAt: Date.now() });
});

chrome.browserAction.setBadgeText({ text: "MV2" });
chrome.browserAction.setBadgeBackgroundColor({ color: "#16803a" });

chrome.webRequest.onBeforeRequest.addListener(
  () => ({}),
  { urls: ["<all_urls>"] },
  ["blocking"]
);
