/**
 * Browser Smooth Scroll - Content Script
 * Physics-based smooth scrolling to replace native browser scroll.
 */

const DEFAULT_SETTINGS = {
    enabled: true,
    animationTimeMs: 500,
    stepSize: 120,
    easing: true,
    shiftHorizontal: true,
};

let settings = { ...DEFAULT_SETTINGS };

// Load settings from storage
chrome.storage.sync.get(DEFAULT_SETTINGS, (stored) => {
    settings = stored;
});

// Listen for settings changes from popup
chrome.storage.onChanged.addListener((changes) => {
    for (const [key, { newValue }] of Object.entries(changes)) {
        settings[key] = newValue;
    }
});

// --- Smooth Scroll Engine ---

const scrollState = new Map(); // element -> { targetX, targetY, currentX, currentY, animId }

function getScrollParent(el) {
    while (el && el !== document.documentElement) {
        const style = getComputedStyle(el);
        const overflow = style.overflow + style.overflowY + style.overflowX;
        if (/auto|scroll/.test(overflow)) return el;
        el = el.parentElement;
    }
    return document.documentElement;
}

function easeOutCubic(t) {
    return 1 - Math.pow(1 - t, 3);
}

function smoothScrollElement(el, deltaX, deltaY) {
    if (!scrollState.has(el)) {
        scrollState.set(el, {
            targetX: el.scrollLeft,
            targetY: el.scrollTop,
            startX: el.scrollLeft,
            startY: el.scrollTop,
            startTime: null,
            animId: null,
        });
    }

    const state = scrollState.get(el);

    // Cancel previous animation
    if (state.animId !== null) {
        cancelAnimationFrame(state.animId);
    }

    // Accumulate scroll target
    state.targetX = Math.max(0, Math.min(el.scrollWidth - el.clientWidth, state.targetX + deltaX));
    state.targetY = Math.max(0, Math.min(el.scrollHeight - el.clientHeight, state.targetY + deltaY));
    state.startX = el.scrollLeft;
    state.startY = el.scrollTop;
    state.startTime = null;

    const duration = settings.animationTimeMs;

    function step(timestamp) {
        if (!state.startTime) state.startTime = timestamp;
        const elapsed = timestamp - state.startTime;
        const progress = Math.min(elapsed / duration, 1);
        const easedProgress = settings.easing ? easeOutCubic(progress) : progress;

        el.scrollLeft = state.startX + (state.targetX - state.startX) * easedProgress;
        el.scrollTop = state.startY + (state.targetY - state.startY) * easedProgress;

        if (progress < 1) {
            state.animId = requestAnimationFrame(step);
        } else {
            state.animId = null;
            scrollState.delete(el);
        }
    }

    state.animId = requestAnimationFrame(step);
}

// --- Wheel Event Interception ---

window.addEventListener('wheel', (e) => {
    if (!settings.enabled) return;

    // Don't intercept on input/textarea/select
    const tag = document.activeElement?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;

    const target = getScrollParent(e.target);
    if (!target) return;

    let deltaX = 0;
    let deltaY = e.deltaY * (settings.stepSize / 100);

    // Shift key = horizontal scroll
    if (e.shiftKey && settings.shiftHorizontal) {
        deltaX = deltaY;
        deltaY = 0;
    }

    // Allow horizontal-only scroll wheels to pass through
    if (e.deltaX !== 0 && e.deltaY === 0) return;

    // Check if the element actually scrolls
    const canScrollY = target.scrollHeight > target.clientHeight;
    const canScrollX = target.scrollWidth > target.clientWidth;
    if (!canScrollY && !canScrollX) return;

    e.preventDefault();
    smoothScrollElement(target, deltaX, deltaY);

}, { passive: false, capture: true });
