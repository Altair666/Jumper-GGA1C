import { test } from 'node:test';
import assert from 'node:assert/strict';
import { collectLegacyLengths, markLegacyLengthsOffered, writeCanvasRadius } from
    '../../src/GGA1C.Sync.Blazor/wwwroot/js/printer-profile-storage.js';

test('offer all eligible template keys once, without changing the legacy record or other users', () => {
    const prefix = 'gga1c:label-canvas::demo-user:';
    const first = prefix + '1', second = prefix + '2';
    const raw = '{"NonPrintableMm":10,"RadiusMm":3.45}';
    const data = new Map([
        [first, raw], [second, '{"NonPrintableMm":0,"RadiusMm":7}'],
        [prefix + '3', '{"NonPrintableMm":-7}'], [prefix + '4', '{broken'],
        ['gga1c:label-canvas::other-user:1', '{"NonPrintableMm":25}'],
    ]);
    const previous = Object.getOwnPropertyDescriptor(globalThis, 'localStorage');
    Object.defineProperty(globalThis, 'localStorage', { configurable: true, value: {
        get length() { return data.size; },
        key: index => Array.from(data.keys())[index] ?? null,
        getItem: key => data.get(key) ?? null,
        setItem: (key, value) => data.set(key, value),
    } });
    try {
        const entries = collectLegacyLengths(prefix);
        assert.deepEqual(entries, [{ key: first, length: 10 }, { key: second, length: 0 }]);
        markLegacyLengthsOffered(entries.map(entry => entry.key));
        assert.deepEqual(collectLegacyLengths(prefix), []);
        assert.equal(data.get(first), raw);
        assert.equal(data.has('gga1c:label-canvas::other-user:1:printer-profile-migrated'), false);
        writeCanvasRadius(first, 5);
        assert.deepEqual(JSON.parse(data.get(first)), { NonPrintableMm: 10, RadiusMm: 5 });
        assert.deepEqual(collectLegacyLengths(prefix), []);
        writeCanvasRadius(prefix + '5', 8);
        assert.deepEqual(JSON.parse(data.get(prefix + '5')), { RadiusMm: 8 });
    } finally {
        if (previous) Object.defineProperty(globalThis, 'localStorage', previous);
        else delete globalThis.localStorage;
    }
});
