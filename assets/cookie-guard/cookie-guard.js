(() => {
  "use strict";

  const bannerWords = /cookie|consent|privacy|gdpr|ccpa|onetrust|trustarc|didomi|quantcast|cookiebot|usercentrics|iubenda|klaro/i;
  const rejectWords = /^(reject(?:\s+all)?|decline(?:\s+all)?|deny(?:\s+all)?|only\s+(?:necessary|essential)\s+cookies?|essential\s+only|continue\s+without\s+accepting|do\s+not\s+sell|opt\s+out|manage\s+preferences)$/i;
  const selectors = [
    "[id*='cookie' i]",
    "[class*='cookie' i]",
    "[id*='consent' i]",
    "[class*='consent' i]",
    "[id*='privacy' i]",
    "[class*='privacy' i]",
    "[id*='onetrust' i]",
    "[class*='onetrust' i]",
    "[id*='trustarc' i]",
    "[class*='trustarc' i]",
    "[data-testid*='cookie' i]",
    "[role='dialog']"
  ];

  const hiddenAttribute = "data-tugle-cookie-guard-hidden";

  function isVisible(element) {
    const style = getComputedStyle(element);
    const rect = element.getBoundingClientRect();
    return style.display !== "none" &&
      style.visibility !== "hidden" &&
      rect.width >= 240 &&
      rect.height >= 40;
  }

  function looksLikeCookieBanner(element) {
    if (!(element instanceof HTMLElement) || !isVisible(element)) return false;

    const text = (element.innerText || "").slice(0, 3000);
    const signature = `${element.id} ${element.className || ""}`;
    const style = getComputedStyle(element);
    const rect = element.getBoundingClientRect();
    const isOverlay = style.position === "fixed" || style.position === "sticky" || Number(style.zIndex) > 10;
    const isNearEdge = rect.bottom >= window.innerHeight - 180 || rect.top <= 80;

    return bannerWords.test(text) && (bannerWords.test(signature) || isOverlay || isNearEdge);
  }

  function findRejectButton(element) {
    const candidates = element.querySelectorAll("button, [role='button'], input[type='button'], input[type='submit'], a");
    for (const candidate of candidates) {
      const label = (candidate.innerText || candidate.value || candidate.getAttribute("aria-label") || "")
        .replace(/\s+/g, " ")
        .trim();
      if (rejectWords.test(label)) return candidate;
    }
    return null;
  }

  function handle(element) {
    if (!looksLikeCookieBanner(element) || element.hasAttribute(hiddenAttribute)) return;

    const rejectButton = findRejectButton(element);
    if (rejectButton) {
      rejectButton.click();
      return;
    }

    element.setAttribute(hiddenAttribute, "true");
  }

  function scan(root = document) {
    if (!root.querySelectorAll) return;
    for (const selector of selectors) {
      for (const element of root.querySelectorAll(selector)) handle(element);
    }
  }

  const style = document.createElement("style");
  style.textContent = `[${hiddenAttribute}] { display: none !important; visibility: hidden !important; }`;
  (document.head || document.documentElement).appendChild(style);
  scan();

  new MutationObserver(records => {
    for (const record of records) {
      for (const node of record.addedNodes) {
        if (node instanceof HTMLElement) handle(node);
      }
    }
  }).observe(document.documentElement, { childList: true, subtree: true });
})();
