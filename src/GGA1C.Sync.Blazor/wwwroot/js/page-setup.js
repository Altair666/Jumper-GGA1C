// The modal uses decimal text inputs so the displayed precision and comma are
// independent of the browser locale. Commit via the normal Blazor change event.
const measureSelector = 'input.sync-setup-decimal';
const measure = el => el instanceof HTMLInputElement && el.matches(measureSelector) && !el.disabled;
const printerMeasure = el => el instanceof HTMLInputElement && el.matches('input.sync-printer-number') && !el.disabled;
function stepPrinter(input, direction) {
    const current = Number(input.value.replace(',', '.'));
    if (!Number.isFinite(current)) return;
    const step = Number(input.dataset.step);
    const next = Math.round((current + Math.sign(direction) * step) * 100) / 100;
    if (!Number.isFinite(next)) return;
    input.value = String(next).replace('.', ',');
    input.dispatchEvent(new Event('change', { bubbles: true }));
}
const partial = /^\d*(?:[,.]\d{0,2})?$/;

function roundText(text) {
    const match = /^(\d*)(?:[,.](\d*))?$/.exec(text.trim());
    if (!match || (!match[1] && !match[2])) return null;
    const fraction = match[2] ?? '';
    const hundredths = BigInt(match[1] || '0') * 100n + BigInt(fraction.slice(0, 2).padEnd(2, '0'))
        + (fraction[2] >= '5' ? 1n : 0n);
    return formatHundredths(hundredths);
}

function formatHundredths(value) {
    const fraction = String(value % 100n).padStart(2, '0').replace(/0+$/, '');
    return String(value / 100n) + (fraction ? ',' + fraction : '');
}

document.addEventListener('beforeinput', event => {
    const input = event.target;
    if (!measure(input) || event.inputType !== 'insertText' || event.isComposing || event.data == null) return;
    const next = input.value.slice(0, input.selectionStart) + event.data + input.value.slice(input.selectionEnd);
    if (!partial.test(next)) event.preventDefault();
});

document.addEventListener('input', event => {
    const input = event.target;
    if (!measure(input) || event.isComposing || partial.test(input.value)) return;
    input.value = roundText(input.value) ?? input.value.replace(/[^\d,.]/g, '')
        .replace(/([,.])(\d{0,2}).*$/, '$1$2');
});

function step(input, direction) {
    const current = BigInt((roundText(input.value) ?? '0').replace(/^(\d+)(?:,(\d+))?$/,
        (_, integer, fraction = '') => integer + fraction.padEnd(2, '0')));
    const next = current + (direction > 0 ? 10n : -10n);
    input.value = formatHundredths(next < 0n ? 0n : next);
    input.dispatchEvent(new Event('change', { bubbles: true }));
}

document.addEventListener('wheel', event => {
    const input = event.target;
    if ((!measure(input) && !printerMeasure(input)) || document.activeElement !== input || !event.deltaY || event.ctrlKey) return;
    event.preventDefault();
    (printerMeasure(input) ? stepPrinter : step)(input, -event.deltaY);
}, { passive: false });

document.addEventListener('keydown', event => {
    const printer = printerMeasure(event.target);
    if (printer && event.key === 'Enter') {
        event.preventDefault();
        event.target.blur();
        return;
    }
    if ((!measure(event.target) && !printer) || !['ArrowUp', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault();
    (printer ? stepPrinter : step)(event.target, event.key === 'ArrowUp' ? 1 : -1);
});

function stepButton(event) {
    return event.target instanceof Element ? event.target.closest('button[data-setup-step]:enabled') : null;
}

document.addEventListener('mousedown', event => {
    if (stepButton(event)) event.preventDefault();
});

document.addEventListener('click', event => {
    const button = stepButton(event);
    const input = button?.closest('.sync-native-input-unit')?.querySelector('input');
    if (!(input instanceof HTMLInputElement) || input.disabled) return;
    const direction = Number(button.dataset.setupStep);
    input.focus();
    if (measure(input)) {
        step(input, direction);
    } else if (printerMeasure(input)) {
        stepPrinter(input, direction);
    } else if (input.matches('input.sync-setup-count[type="number"]')) {
        const previous = input.value;
        if (direction > 0) input.stepUp(); else input.stepDown();
        if (input.value !== previous) input.dispatchEvent(new Event('change', { bubbles: true }));
    }
});
