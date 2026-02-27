const DEFAULTS = {
    enabled: true,
    animationTimeMs: 550,
    stepSize: 120,
    easing: true,
    shiftHorizontal: true,
};

const $ = (id) => document.getElementById(id);

function displayVal(id, value) {
    const el = document.getElementById(id + 'Val');
    if (el) el.textContent = value;
}

// Load settings and populate UI
chrome.storage.sync.get(DEFAULTS, (settings) => {
    $('enabled').checked = settings.enabled;
    $('animationTimeMs').value = settings.animationTimeMs;
    $('stepSize').value = settings.stepSize;
    $('easing').checked = settings.easing;
    $('shiftHorizontal').checked = settings.shiftHorizontal;

    displayVal('animationTimeMs', settings.animationTimeMs);
    displayVal('stepSize', settings.stepSize);
});

// Save on any change
function save() {
    const animMs = parseInt($('animationTimeMs').value);
    const step = parseInt($('stepSize').value);

    chrome.storage.sync.set({
        enabled: $('enabled').checked,
        animationTimeMs: animMs,
        stepSize: step,
        easing: $('easing').checked,
        shiftHorizontal: $('shiftHorizontal').checked,
    });

    displayVal('animationTimeMs', animMs);
    displayVal('stepSize', step);
}

document.querySelectorAll('input').forEach((el) => {
    el.addEventListener('input', save);
    el.addEventListener('change', save);
});
