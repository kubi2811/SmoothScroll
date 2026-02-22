/**
 * Browser Smooth Scroll - Content Script
 * 1:1 Port of SmoothScrollService.cs Physics Engine
 */

const DEFAULT_SETTINGS = {
    enabled: true,
    animationTimeMs: 350,
    stepSize: 100,
    easing: true,
    shiftHorizontal: true,
};

let settings = { ...DEFAULT_SETTINGS };

chrome.storage.sync.get(DEFAULT_SETTINGS, (s) => { settings = s; });
chrome.storage.onChanged.addListener((changes) => {
    for (const [k, { newValue }] of Object.entries(changes)) settings[k] = newValue;
});

// --- Constants from SmoothScrollService.cs ---
const CoastKickGain = 0.22;
const CoastDragPerSecond = 15.0;
const CoastActiveDragPerSecond = 18.0;
const CoastMaxVelocity = 3600.0;
const CoastStopVelocity = 20.0;
const TailResidualBleedVelocity = 90.0;
const TailResidualBleedPerSecond = 5.4;
const ReleaseSeedGain = 0.92;

// --- State Variables ---
const stateMap = new WeakMap();

function getScrollState(el) {
    if (!stateMap.has(el)) {
        stateMap.set(el, {
            impulses: [],
            residual: 0,
            coastVelocity: 0,
            releaseSeedRate: 0,
            releaseSeedPending: false,
            lastInputMs: -99999,
            lastDirection: 0,
            animId: null,
            lastAnimTime: 0
        });
    }
    return stateMap.get(el);
}

// --- Easing Math ---
function easeOutCubic(t) {
    // settings.TailToHeadRatio in C# was default ~3
    return 1.0 - Math.pow(1.0 - t, 3);
}

class ScrollImpulse {
    constructor(totalDelta, durationMs, useEasing) {
        this.totalDelta = totalDelta;
        this.creationTime = performance.now();
        this.durationMs = durationMs;
        this.useEasing = useEasing;
        this.emittedDelta = 0;
    }

    get isCompleted() {
        return Math.abs(this.emittedDelta - this.totalDelta) < 0.001;
    }

    takeIncrement(now) {
        if (this.isCompleted) return 0;

        let elapsedMs = now - this.creationTime;
        let progress = Math.max(0, Math.min(elapsedMs / this.durationMs, 1.0));
        let eased = this.useEasing ? easeOutCubic(progress) : progress;

        let targetEmitted = this.totalDelta * eased;
        let increment = targetEmitted - this.emittedDelta;
        this.emittedDelta = targetEmitted;
        return increment;
    }
}

function processFrame(el, state, now) {
    if (state.lastAnimTime === 0) state.lastAnimTime = now;
    let dtSec = Math.max(0.001, (now - state.lastAnimTime) / 1000.0);
    state.lastAnimTime = now;

    // 1. Consume impulses
    let emittedThisTick = 0;
    for (let i = state.impulses.length - 1; i >= 0; i--) {
        let imp = state.impulses[i];
        emittedThisTick += imp.takeIncrement(now);
        if (imp.isCompleted) state.impulses.splice(i, 1);
    }
    state.residual += emittedThisTick;

    // 2. Coast Motion
    let hasActiveImpulses = state.impulses.length > 0;
    if (Math.abs(state.coastVelocity) >= CoastStopVelocity) {
        let coasting = (now - state.lastInputMs) >= 14 && !hasActiveImpulses;
        let drag = coasting ? CoastDragPerSecond : CoastActiveDragPerSecond;

        if (!hasActiveImpulses && state.releaseSeedPending) {
            let seed = Math.min(state.releaseSeedRate * ReleaseSeedGain, CoastMaxVelocity);
            if (seed > 0) {
                let dir = state.lastDirection !== 0 ? state.lastDirection : Math.sign(state.coastVelocity) || 1;
                let candidate = dir * seed;
                if (Math.abs(candidate) > Math.abs(state.coastVelocity)) {
                    state.coastVelocity = candidate;
                }
            }
            state.releaseSeedPending = false;
            state.releaseSeedRate = 0;
        }

        if (!hasActiveImpulses) {
            state.residual += state.coastVelocity * dtSec;
            if (Math.abs(state.coastVelocity) <= TailResidualBleedVelocity && Math.abs(state.residual) < 2.0) {
                state.residual *= Math.exp(-TailResidualBleedPerSecond * dtSec);
            }
        }

        state.coastVelocity *= Math.exp(-drag * dtSec);
        if (Math.abs(state.coastVelocity) < CoastStopVelocity) state.coastVelocity = 0;
    } else {
        state.coastVelocity = 0;
    }

    // 3. Take integral to send to browser scroll
    let deltaToSend = Math.trunc(state.residual);
    state.residual -= deltaToSend;

    // 4. Apply scroll
    if (deltaToSend !== 0) {
        const maxX = el === document.documentElement ? document.documentElement.scrollWidth - window.innerWidth : el.scrollWidth - el.clientWidth;
        const maxY = el === document.documentElement ? document.documentElement.scrollHeight - window.innerHeight : el.scrollHeight - el.clientHeight;

        // Convert to pixel scrolling
        el.scrollLeft = Math.max(0, Math.min(maxX, el.scrollLeft + (state.lastHorizontal ? deltaToSend : 0)));
        el.scrollTop = Math.max(0, Math.min(maxY, el.scrollTop + (!state.lastHorizontal ? deltaToSend : 0)));
    }

    // Check if done
    if (state.impulses.length > 0 || Math.abs(state.coastVelocity) > 0 || Math.abs(state.residual) >= 1) {
        state.animId = requestAnimationFrame((n) => processFrame(el, state, n));
    } else {
        state.animId = null;
        state.lastAnimTime = 0;
        state.residual = 0;
    }
}

// --- Acceleration Math (from SmoothScrollService) ---
const AccelerationStrength = 1.20;
const CadenceBoostStrength = 0.55;
const MaxAccelerationMultiplier = 3.50;
const AccelerationDeltaMs = 120; // Default from AppSettings
const AccelerationMax = 12;

function computeAcceleration(combo, elapsedMs) {
    let safeWindow = Math.max(1, AccelerationDeltaMs);
    let clampedElapsed = Math.max(0, Math.min(elapsedMs, safeWindow));
    let cadenceRatio = 1.0 - (clampedElapsed / safeWindow);
    let comboRatio = combo / Math.max(1, AccelerationMax);
    let comboBoost = Math.pow(Math.max(0.0, Math.min(comboRatio, 1.0)), 0.45) * AccelerationStrength;
    let cadenceBoost = cadenceRatio * CadenceBoostStrength;
    let factor = Math.max(1.0, Math.min(1.0 + comboBoost + cadenceBoost, MaxAccelerationMultiplier));
    return factor;
}

function computeDurationMs(baseDurationMs, accelFactor) {
    let normalizedBase = Math.max(40, Math.min(baseDurationMs, 2000));
    let accelerationImpact = Math.max(0.0, accelFactor - 1.0);
    let shortenRatio = Math.min(0.45, accelerationImpact * 0.11);
    let scaled = normalizedBase * (1.0 - shortenRatio);
    return Math.max(80, Math.round(scaled));
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

    let multiplier = 1;
    if (e.deltaMode === 1) multiplier = 40;
    if (e.deltaMode === 2) multiplier = 800;

    let dx = e.deltaX * multiplier;
    let dy = e.deltaY * multiplier;

    const notchCount = dy !== 0 ? dy / 120 : dx / 120;
    let targetDelta = notchCount * settings.stepSize;

    let horizontal = false;
    if (e.shiftKey && settings.shiftHorizontal && dy !== 0) {
        horizontal = true;
    } else if (dx !== 0 && dy === 0) {
        horizontal = true;
        targetDelta = (dx / 120) * settings.stepSize;
    }

    // Allow native trackpad horizontal without shift
    if (dx !== 0 && dy === 0 && !e.shiftKey) return;

    e.preventDefault();

    let state = getScrollState(target);
    let now = performance.now();
    let dir = Math.sign(targetDelta);

    // Apply Acceleration
    let isAccDirectionSame = state.lastAccDirection === dir;
    let elapsedSinceInput = state.lastInputMs === -99999 ? 999999 : now - state.lastInputMs;

    if (!isAccDirectionSame) state.combo = 0;

    if (elapsedSinceInput <= AccelerationDeltaMs) {
        state.combo = Math.min((state.combo || 0) + 1, AccelerationMax);
    } else {
        state.combo = 0;
    }

    state.lastAccDirection = dir;
    let accelFactor = computeAcceleration(state.combo, elapsedSinceInput);
    let acceleratedTargetDelta = targetDelta * accelFactor;
    let acceleratedDurationMs = computeDurationMs(settings.animationTimeMs, accelFactor);

    if (Math.abs(acceleratedTargetDelta) < 0.01) return;

    state.lastHorizontal = horizontal;
    state.lastDirection = dir;
    state.lastInputMs = now;

    let impulse = new ScrollImpulse(acceleratedTargetDelta, acceleratedDurationMs, settings.easing);

    let coastKick = (acceleratedTargetDelta / Math.max(1.0, acceleratedDurationMs)) * 1000.0 * CoastKickGain;
    let seedRate = (Math.abs(acceleratedTargetDelta) / Math.max(1.0, acceleratedDurationMs)) * 1000.0;

    state.impulses.push(impulse);

    let currentCoast = state.coastVelocity;
    currentCoast = Math.max(-CoastMaxVelocity, Math.min(CoastMaxVelocity, currentCoast + coastKick));
    state.coastVelocity = currentCoast;

    state.releaseSeedRate = Math.max(0, Math.min(CoastMaxVelocity, (state.releaseSeedRate * 0.45) + (seedRate * 0.55)));
    state.releaseSeedPending = true;

    if (!state.animId) {
        state.lastAnimTime = now;
        state.animId = requestAnimationFrame((n) => processFrame(target, state, n));
    }

}, { passive: false, capture: true });
