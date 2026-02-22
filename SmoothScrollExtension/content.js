/**
 * Browser Smooth Scroll - Content Script
 * Velocity-decay physics engine (matches the EXE feel).
 */

const DEFAULT_SETTINGS = {
    enabled: true,
    animationTimeMs: 350,  // Reduced for snappier stops (less trôi)
    stepSize: 100,         // Better default notch size for browser
    easing: true,
    shiftHorizontal: true,
};

let settings = { ...DEFAULT_SETTINGS };

chrome.storage.sync.get(DEFAULT_SETTINGS, (s) => { settings = s; });
chrome.storage.onChanged.addListener((changes) => {
    for (const [k, { newValue }] of Object.entries(changes)) settings[k] = newValue;
});

// ---------------------------------------------------------------------------
// Physics engine: velocity decay per requestAnimationFrame
// ---------------------------------------------------------------------------

// Per-element scroll state (WeakMap so GC can clean up detached elements)
const states = new WeakMap();

function getState(el) {
    if (!states.has(el)) states.set(el, { vx: 0, vy: 0, rafId: null });
    return states.get(el);
}

/**
 * friction coefficient per frame.
 * We want velocity to reach ~1% of original after `animationTimeMs` ms.
 *   friction ^ (ms / 16.67) = 0.01
 *   friction = 0.01 ^ (16.67 / ms)
 */
function friction() {
    return Math.pow(0.01, 16.67 / Math.max(settings.animationTimeMs, 50));
}

function tick(el, state) {
    const f = friction();
    state.vx *= f;
    state.vy *= f;

    // Clamp to element scroll boundary
    const maxX = el === document.documentElement
        ? document.documentElement.scrollWidth - window.innerWidth
        : el.scrollWidth - el.clientWidth;
    const maxY = el === document.documentElement
        ? document.documentElement.scrollHeight - window.innerHeight
        : el.scrollHeight - el.clientHeight;

    const newX = Math.max(0, Math.min(maxX, el.scrollLeft + state.vx));
    const newY = Math.max(0, Math.min(maxY, el.scrollTop + state.vy));

    // Stop if we hit boundary and velocity is pushing further
    if ((newX === 0 || newX === maxX)) state.vx = 0;
    if ((newY === 0 || newY === maxY)) state.vy = 0;

    el.scrollLeft = newX;
    el.scrollTop = newY;

    if (Math.abs(state.vx) > 0.3 || Math.abs(state.vy) > 0.3) {
        state.rafId = requestAnimationFrame(() => tick(el, state));
    } else {
        state.rafId = null;
    }
}

function addVelocity(el, dvx, dvy) {
    const state = getState(el);
    const maxV = settings.stepSize * 5; // cap velocity
    state.vx = Math.max(-maxV, Math.min(maxV, state.vx + dvx));
    state.vy = Math.max(-maxV, Math.min(maxV, state.vy + dvy));
    if (!state.rafId) state.rafId = requestAnimationFrame(() => tick(el, state));
}

// ---------------------------------------------------------------------------
// Scroll target detection
// ---------------------------------------------------------------------------

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

// ---------------------------------------------------------------------------
// Normalize wheel delta across different devices / delta modes
// ---------------------------------------------------------------------------
const PIXELS_PER_LINE = 40;
const PIXELS_PER_PAGE = 800;

function normalizeDelta(e) {
    let dx = e.deltaX;
    let dy = e.deltaY;
    if (e.deltaMode === 1) { dx *= PIXELS_PER_LINE; dy *= PIXELS_PER_LINE; }
    if (e.deltaMode === 2) { dx *= PIXELS_PER_PAGE; dy *= PIXELS_PER_PAGE; }
    return { dx, dy };
}

// ---------------------------------------------------------------------------
// Wheel event listener
// ---------------------------------------------------------------------------

window.addEventListener('wheel', (e) => {
    if (!settings.enabled) return;

    // Skip inside inputs
    const tag = document.activeElement?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;

    const target = getScrollTarget(e.target);
    if (!target) return;

    let { dx, dy } = normalizeDelta(e);

    // Shift key → horizontal scroll
    if (e.shiftKey && settings.shiftHorizontal && dy !== 0) {
        dx = dy;
        dy = 0;
    }

    // Scale raw pixel delta to stepSize
    // Typical mouse: 1 notch = 100px raw.  We want 1 notch = stepSize px velocity.
    // UPDATE: Reduced impulse multiplier to 0.4 to prevent overly light scrolling.
    const scale = (settings.stepSize / 100) * 0.4;
    dx *= scale;
    dy *= scale;

    e.preventDefault();
    addVelocity(target, dx, dy);

}, { passive: false, capture: true });
