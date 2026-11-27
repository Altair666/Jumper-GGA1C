import { test } from 'node:test';
import assert from 'node:assert/strict';
import { rulerSteps, rulerTicks } from '../../src/GGA1C.Sync.Blazor/wwwroot/js/sync-editor-ruler.js';

const cssPxPerMm = 96 / 25.4;

test('a numbered mark stays at its physical coordinate in all four units', () => {
    for (const [unit, unitsPerMm] of [['mm', 1], ['cm', 0.1], ['inch', 1 / 25.4], ['dot', 12]]) {
        for (const zoom of [0.1, 0.75, 1, 2.25, 6]) {
            const pxPerMm = cssPxPerMm * zoom;
            const origin = 188.625;
            const ticks = rulerTicks(origin, pxPerMm / unitsPerMm, 912, unit);
            const numbered = ticks.filter(tick => tick.value !== null);
            assert.ok(numbered.length > 2, `${unit} at ${zoom}`);
            for (const tick of numbered) {
                assert.ok(Math.abs(tick.position - (origin + tick.value / unitsPerMm * pxPerMm)) < 0.000001);
            }
            assert.deepEqual(numbered.find(tick => tick.position === origin)?.value, 0);
        }
    }
});

test('decimal, inch and dot intervals remain readable at every supported zoom', () => {
    for (const [unit, unitsPerMm] of [['mm', 1], ['cm', 0.1], ['inch', 1 / 25.4], ['dot', 8], ['dot', 12], ['dot', 24]]) {
        for (const zoom of [0.1, 0.25, 0.5, 1, 2, 4, 6]) {
            const pxPerUnit = cssPxPerMm * zoom / unitsPerMm;
            const { major, divisions } = rulerSteps(pxPerUnit, unit);
            assert.ok(major * pxPerUnit >= 64);
            assert.ok(major * pxPerUnit / divisions >= 6);
            if (unit === 'dot') assert.ok(Number.isInteger(major / divisions));
            if (unit === 'inch') assert.ok(Number.isInteger(Math.log2(major)));
        }
    }
});

test('scroll positions on either side of the origin draw only visible ticks', () => {
    for (const origin of [-6_000_023.625, -122, 0, 1.25, 250.5]) {
        const ticks = rulerTicks(origin, 3.78, 533, 'mm');
        assert.ok(ticks.length > 5 && ticks.length <= 90);
        assert.ok(ticks.every(tick => tick.position >= -1e-8 && tick.position <= 533 + 1e-8));
        assert.ok(ticks.every((tick, index) => index === 0 || tick.position > ticks[index - 1].position));
        assert.ok(ticks.every(tick => tick.position >= origin));
        if (origin < 0) assert.ok(ticks.every(tick => tick.value === null || tick.value > 0));
    }
});

test('vertical and zero-length horizontal rulers have no ticks before zero', () => {
    assert.deepEqual(rulerTicks(8_000_000, 3.78, 533, 'mm'), []);
    assert.deepEqual(rulerTicks(110, 3.78, 100, 'mm', -0), []);
    const ticks = rulerTicks(40, 3.78, 250, 'mm');
    assert.equal(ticks[0].value, 0);
    assert.equal(ticks[0].position, 40);
});

test('a 10 mm horizontal band has the same physical endpoints in every unit and zoom', () => {
    for (const [unit, factor] of [['mm', 1], ['cm', 0.1], ['inch', 1 / 25.4], ['dot', 7.99]]) {
        for (const zoom of [0.1, 1, 2.5, 6]) {
            const pxPerMm = cssPxPerMm * zoom;
            const origin = 280;
            const ticks = rulerTicks(origin, pxPerMm / factor, 533, unit, -10 * factor);
            const [start] = ticks;
            assert.equal(start.value, -10 * factor);
            assert.ok(Math.abs(start.position - (origin - 10 * pxPerMm)) < 1e-8);
            assert.ok(start.start);
            assert.ok(ticks.some(tick => tick.position === origin && tick.value === 0));
            assert.ok(ticks.every((tick, index) => index === 0 || tick.position > ticks[index - 1].position));
        }
    }
});

test('horizontal ruler handles scrolling across the band and skips duplicate endpoint marks', () => {
    const before = rulerTicks(130, 10, 300, 'mm', -10);
    assert.equal(before[0].position, 30);
    assert.equal(before.filter(tick => tick.position === 30).length, 1);
    const inside = rulerTicks(50, 10, 300, 'mm', -10);
    assert.ok(inside.every(tick => tick.position >= 0 && !tick.start));
    assert.ok(inside.some(tick => tick.value === 0));
    const beyond = rulerTicks(-600, 10, 300, 'mm', -10);
    assert.ok(beyond.every(tick => tick.value === null || tick.value > 0));
    assert.deepEqual(rulerTicks(450, 10, 300, 'mm', -10), []);
});

test('a flush left edge keeps its endpoint label after subpixel layout rounding', () => {
    const start = rulerTicks(37.785, 3.78, 300, 'mm', -10)[0];
    assert.equal(start.value, -10);
    assert.equal(start.position, 0);
    const zero = rulerTicks(-0.015, 3.78, 300, 'mm')[0];
    assert.equal(zero.value, 0);
    assert.equal(zero.position, 0);
});

test('a hidden or unmeasured viewport cannot produce a tick loop', () => {
    assert.deepEqual(rulerTicks(0, 0, 500, 'mm'), []);
    assert.deepEqual(rulerTicks(0, NaN, 500, 'mm'), []);
    assert.deepEqual(rulerTicks(0, 10, 0, 'mm'), []);
});
