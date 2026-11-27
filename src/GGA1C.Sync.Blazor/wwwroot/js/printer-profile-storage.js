const marker = ':printer-profile-migrated';

// Only the signed-in user's template keys are passed in. Other users' and browsers'
// values are collected when those users open the page, not read across accounts.
export function collectLegacyLengths(prefix) {
    const entries = [];
    try {
        for (let index = 0; index < localStorage.length; index++) {
            const key = localStorage.key(index);
            if (!key?.startsWith(prefix) || !/^\d+$/.test(key.slice(prefix.length)) || localStorage.getItem(key + marker)) continue;
            try {
                const length = JSON.parse(localStorage.getItem(key))?.NonPrintableMm;
                if (typeof length === 'number' && Number.isFinite(length) && length >= 0) entries.push({ key, length });
            } catch { /* Leave a damaged record untouched. */ }
        }
    } catch { /* Browser storage can be unavailable. */ }
    return entries;
}

export function markLegacyLengthsOffered(keys) {
    if (!Array.isArray(keys)) return;
    try {
        for (const key of keys) localStorage.setItem(key + marker, '1');
    } catch { /* A repeat offer is idempotent in the core. */ }
}

export function writeCanvasRadius(key, radius) {
    let options;
    try { options = JSON.parse(localStorage.getItem(key)); } catch { /* No readable options. */ }
    if (!options || typeof options !== 'object' || Array.isArray(options)) options = {};
    // Preserve any legacy length until the core has accepted it, and do not write
    // any current printer setting to browser storage.
    options.RadiusMm = radius;
    localStorage.setItem(key, JSON.stringify(options));
}
