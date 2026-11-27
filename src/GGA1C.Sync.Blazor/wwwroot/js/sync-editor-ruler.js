const minLabelSpacing = 64;
const minTickSpacing = 6;
// An SVG origin snapped by CSS layout can miss a viewport edge by a subpixel.
const edgeTolerance = 1 / 32;

// A numbered interval uses round decimal values, or binary fractions in inches.
// Subdivisions are omitted when they would overlap or denote a fraction of a printer dot.
export function rulerSteps(pxPerUnit, unit) {
    const minimum = minLabelSpacing / pxPerUnit;
    const inch = unit === 'inch';
    const dot = unit === 'dot';
    let major;
    if (inch) {
        major = 2 ** Math.ceil(Math.log2(minimum));
    } else {
        const magnitude = 10 ** Math.floor(Math.log10(dot ? Math.max(1, minimum) : minimum));
        major = [1, 2, 5, 10].map(step => step * magnitude).find(step => step >= minimum);
    }
    const divisions = (inch ? [8, 4, 2, 1] : [10, 5, 2, 1]).find(count =>
        major * pxPerUnit / count >= minTickSpacing && (!dot || Number.isInteger(major / count)));
    return { major, divisions };
}

// Generate only the ticks visible in the viewport, starting at the allowed document coordinate.
export function rulerTicks(origin, pxPerUnit, length, unit, minimum = 0) {
    if (!Number.isFinite(origin) || !Number.isFinite(pxPerUnit) || pxPerUnit <= 0 || length <= 0) return [];
    const { major, divisions } = rulerSteps(pxPerUnit, unit);
    const minor = major / divisions;
    const pxPerTick = minor * pxPerUnit;
    const first = Math.max(Math.ceil((-origin - edgeTolerance) / pxPerTick), Math.ceil(minimum / minor - 1e-9));
    const last = Math.floor((length - origin + edgeTolerance) / pxPerTick);
    const ticks = [];
    const start = origin + minimum * pxPerUnit;
    const markStart = minimum < 0 && start >= -edgeTolerance && start <= length + edgeTolerance;
    const visible = position => Math.max(0, Math.min(length, position));
    for (let index = first; index <= last; index++) {
        const position = origin + index * pxPerTick;
        if (markStart && Math.abs(position - start) < 1e-6) continue;
        const numbered = index % divisions === 0;
        ticks.push({
            position: visible(position),
            value: numbered ? (index === 0 ? 0 : index / divisions * major) : null,
            size: numbered ? 14 : index % (divisions / 2) === 0 ? 9 : 5,
        });
    }
    // Draw the exact start even when the regular major interval is larger than the band.
    if (markStart) ticks.unshift({ position: visible(start), value: minimum, size: 14, start: true });
    return ticks;
}
