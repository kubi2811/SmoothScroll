/**
 * Browser Smooth Scroll - Content Script
 * Exact match of the C# EasingScrollMotion engine for 1:1 feel.
 */

const DEFAULT_SETTINGS = {
    enabled: true,
    animationTimeMs: 550,
    stepSize: 120,
    easing: true,
    shiftHorizontal: true,
};

let settings = { ...DEFAULT_SETTINGS };

chrome.storage.sync.get(DEFAULT_SETTINGS, (s) => { settings = s; });
chrome.storage.onChanged.addListener((changes) => {
    for (const [k, { newValue }] of Object.entries(changes)) settings[k] = newValue;
});

// --- Physics Engine (Matching C# EasingScrollMotion exactly) ---

const scrollState = new WeakMap();

// C# matches: EaseOutCirc / Cubic
function easeOutCubic(t) {
    return 1 - Math.pow(1 - t, 3);
}

// C# matches: EasingScrollMotion parameters
function smoothScrollElement(el, rawDeltaX, rawDeltaY) {
    if (!scrollState.has(el)) {
        scrollState.set(el, {
            startX: el.scrollLeft,
            startY: el.scrollTop,
            targetX: el.scrollLeft,
            targetY: el.scrollTop,
            startTime: 0,
            animId: null
        });
    }

    const state = scrollState.get(el);

    // If already scrolling, update target from current TARGET, not current position
    // This causes the "Coasting" momentum feel from the EXE
    state.targetX += rawDeltaX;
    state.targetY += rawDeltaY;

    // Clamp targets
    const maxX = el === document.documentElement ? document.documentElement.scrollWidth - window.innerWidth : el.scrollWidth - el.clientWidth;
    const maxY = el === document.documentElement ? document.documentElement.scrollHeight - window.innerHeight : el.scrollHeight - el.clientHeight;

    state.targetX = Math.max(0, Math.min(maxX, state.targetX));
    state.targetY = Math.max(0, Math.min(maxY, state.targetY));

    // Reset animation start
    state.startX = el.scrollLeft;
    state.startY = el.scrollTop;
    state.startTime = performance.now();

    function tick(now) {
        let elapsed = now - state.startTime;
        let progress = Math.min(elapsed / settings.animationTimeMs, 1);

        let eased = settings.easing ? easeOutCubic(progress) : progress;

        let nextX = state.startX + (state.targetX - state.startX) * eased;
        let nextY = state.startY + (state.targetY - state.startY) * eased;

        el.scrollLeft = nextX;
        el.scrollTop = nextY;

        if (progress < 1) {
            state.animId = requestAnimationFrame(tick);
        } else {
            state.animId = null;
            scrollState.delete(el); // Clean up when done
        }
    }

    if (state.animId) cancelAnimationFrame(state.animId);
    state.animId = requestAnimationFrame(tick);
}

// --- Scroll targeting ---

function getScrollTarget(node) {
    let el = node;
    while (el && el !== document.documentElement) {
        const style = window.getComputedStyle(el);
        const ov = style.overflow + style.overflowY + style.overflowX;
        if (/auto|scroll/.test(ov)) {
            if (el.scrollHeight > el.clientHeight || el.scrollWidth > el.clientWidth)
                return el;
        }
        el = el.parentElement;
    }
    return document.documentElement;
}

// --- Wheel interception ---

window.addEventListener('wheel', (e) => {
    if (!settings.enabled) return;

    const tag = document.activeElement?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;

    const target = getScrollTarget(e.target);
    if (!target) return;

    // Base 120px is standard Windows wheel notch behavior
    // Normalize delta Mode
    let multiplier = 1;
    if (e.deltaMode === 1) multiplier = 40; // LINE
    if (e.deltaMode === 2) multiplier = 800; // PAGE

    let dx = e.deltaX * multiplier;
    let dy = e.deltaY * multiplier;

    // Scale by user stepSize setting
    const scale = settings.stepSize / 120;
    dx *= scale;
    dy *= scale;

    if (e.shiftKey && settings.shiftHorizontal && dy !== 0) {
        dx = dy;
        dy = 0;
    }

    // Allow native trackpad horizontal to pass through
    if (dx !== 0 && dy === 0 && !e.shiftKey) return;

    e.preventDefault();
    smoothScrollElement(target, dx, dy);

}, { passive: false, capture: true });
