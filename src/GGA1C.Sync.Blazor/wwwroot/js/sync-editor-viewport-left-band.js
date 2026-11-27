import './page-setup.js';
import { rulerTicks } from './sync-editor-ruler.js';

const editors = new WeakMap();
const ns = 'http://www.w3.org/2000/svg';
const labels = new Intl.NumberFormat('ru-RU', { useGrouping: false, maximumFractionDigits: 8 });
const positions = new Intl.NumberFormat('ru-RU', { useGrouping: false, maximumFractionDigits: 3 });
const inchLimits = new Intl.NumberFormat('ru-RU', { useGrouping: false, maximumFractionDigits: 4 });
const percentages = new Intl.NumberFormat('ru-RU', { style: 'percent', maximumFractionDigits: 1 });

function line(element, x1, y1, x2, y2) {
    for (const [name, value] of Object.entries({ x1, y1, x2, y2 })) element.setAttribute(name, value);
    element.setAttribute('visibility', 'visible');
}

function drawRuler(svg, horizontal, origin, scale, length, unit, minimum = 0) {
    const thickness = horizontal ? svg.clientHeight : svg.clientWidth;
    const edge = thickness - 1;
    const fragment = document.createDocumentFragment();
    const ticks = rulerTicks(origin, scale, length, unit, minimum);
    let marks = '', zero = '';
    for (const { position, value, size, start } of ticks) {
        const mark = horizontal ? `M${position} ${edge} v-${size}` : `M${edge} ${position} h-${size}`;
        if (value === 0) zero = mark;
        else marks += mark;
        if (value === null) continue;
        const text = document.createElementNS(ns, 'text');
        text.setAttribute('class', 'sync-ruler-label');
        const format = start && unit === 'dot' ? positions : start && unit === 'inch' ? inchLimits : labels;
        text.textContent = format.format(value);
        if (horizontal) {
            // The endpoint may be closer than the regular label spacing. Move its
            // caption, not its tick or the zero, if the captions would touch.
            let labelBefore = false, lowerRow = false;
            if (start) {
                const next = ticks.find(tick => !tick.start && tick.value !== null);
                const labelExtent = text.textContent.length * 6 + 3;
                const crowded = position + labelExtent > (next?.position ?? length);
                labelBefore = crowded && position >= labelExtent;
                lowerRow = crowded && !labelBefore;
            }
            text.setAttribute('x', position + (labelBefore ? -3 : 3));
            text.setAttribute('y', lowerRow ? 25 : 11);
            if (labelBefore) text.setAttribute('text-anchor', 'end');
        } else {
            text.setAttribute('transform', `translate(11 ${position + 3}) rotate(-90)`);
            text.setAttribute('text-anchor', 'end');
        }
        fragment.append(text);
    }
    for (const [marksPath, className] of [[marks, 'sync-ruler-ticks'], [zero, 'sync-ruler-ticks is-zero']]) {
        if (!marksPath) continue;
        const path = document.createElementNS(ns, 'path');
        path.setAttribute('d', marksPath);
        path.setAttribute('class', className);
        path.setAttribute('fill', 'none');
        fragment.prepend(path);
    }
    svg.firstElementChild.replaceChildren(fragment);
}

export function initEditor(root, dotnetRef) {
    if (!root?.isConnected) return false;
    editors.get(root)?.();
    const find = id => root.querySelector(`#${id}`);
    const workspace = find('sync-workspace');
    const viewport = find('sync-viewport');
    const svg = find('sync-canvas-svg');
    const stage = svg.parentElement;
    const artwork = svg.querySelector('.sync-canvas-objects');
    const stageGutter = parseFloat(getComputedStyle(stage).getPropertyValue('--sync-stage-gutter')) || 32;
    const nonPrintable = () => Number(svg.dataset.nonPrintableMm) || 0;
    const top = find('sync-ruler-top');
    const left = find('sync-ruler-left');
    const slider = find('sync-zoom');
    const reset = find('sync-zoom-reset');
    const readout = find('sync-pos-readout');
    const [crossX, crossY] = find('sync-crosshair').children;
    const markerX = top.lastElementChild, markerY = left.lastElementChild;
    const events = new AbortController();
    const listen = (target, type, handler, options) => target.addEventListener(type, handler, { signal: events.signal, ...options });
    let zoom = 1, frame = 0, dirty = true, pointer = null, gesture = null, committing = false;

    // SVG user units are millimetres. The browser matrix accounts for fractional layout,
    // scrolling and viewBox, so there is no second calculation of the origin.
    const toDocument = (point, matrix = svg.getScreenCTM()) => point && matrix ? point.matrixTransform(matrix.inverse()) : null;
    const position = e => new DOMPoint(e.clientX, e.clientY);
    const paused = () => root.dataset.settingsOpen === 'true';
    const hide = (...elements) => elements.forEach(element => element.setAttribute('visibility', 'hidden'));
    const showPosition = text => { if (readout.textContent !== text) readout.textContent = text; };
    const contains = (rect, p, width = rect.width, height = rect.height) => p &&
        p.x >= rect.left && p.x < rect.left + width && p.y >= rect.top && p.y < rect.top + height;

    function drawPointer(matrix, view, width, height) {
        const topBox = top.getBoundingClientRect(), leftBox = left.getBoundingClientRect();
        const overViewport = contains(view, pointer, width, height);
        const showX = overViewport || contains(topBox, pointer, width);
        const showY = overViewport || contains(leftBox, pointer, leftBox.width, height);
        const mm = !paused() && (showX || showY) ? toDocument(pointer, matrix) : null;
        if (!mm || !Number.isFinite(mm.x) || !Number.isFinite(mm.y)) {
            hide(crossX, crossY, markerX, markerY);
            showPosition('—');
            return;
        }
        if (showX) {
            const x = pointer.x - view.left;
            line(crossX, x, 0, x, height);
            if (mm.x >= -nonPrintable()) line(markerX, x, 0, x, topBox.height);
            else hide(markerX);
        } else hide(crossX, markerX);
        if (showY) {
            const y = pointer.y - view.top;
            line(crossY, 0, y, width, y);
            if (mm.y >= 0) line(markerY, 0, y, leftBox.width, y);
            else hide(markerY);
        } else hide(crossY, markerY);
        const factor = Number(root.dataset.unitsPerMm);
        const display = value => positions.format(Math.abs(value * factor) < 0.0005 ? 0 : value * factor);
        showPosition(`${showX ? display(mm.x) : '—'} / ${showY ? display(mm.y) : '—'} ${root.dataset.unit}`);
    }

    function paint() {
        frame = 0;
        const matrix = svg.getScreenCTM();
        const view = viewport.getBoundingClientRect();
        const width = viewport.clientWidth, height = viewport.clientHeight;
        if (dirty && matrix) {
            const factor = Number(root.dataset.unitsPerMm);
            const unit = root.dataset.unit;
            drawRuler(top, true, matrix.e - view.left, matrix.a / factor, width, unit, -nonPrintable() * factor);
            drawRuler(left, false, matrix.f - view.top, matrix.d / factor, height, unit);
            dirty = false;
        }
        drawPointer(matrix, view, width, height);
    }

    function schedule(layout = false) {
        dirty ||= layout;
        if (!frame) frame = requestAnimationFrame(paint);
    }

    function scaleCanvas() {
        svg.style.width = `${svg.width.baseVal.value * zoom}px`;
        svg.style.height = `${svg.height.baseVal.value * zoom}px`;
    }

    function layoutStage() {
        if (!artwork) return;
        const { x, y, width, height } = svg.viewBox.baseVal;
        if (width <= 0 || height <= 0) return;
        const bounds = artwork.getBBox();
        const sheetWidth = svg.width.baseVal.value * zoom;
        const sheetHeight = svg.height.baseVal.value * zoom;
        const scaleX = sheetWidth / width, scaleY = sheetHeight / height;
        const pad = (mm, scale) => stageGutter + Math.ceil(Math.max(0, mm) * scale);
        const right = pad(bounds.x + bounds.width - (x + width), scaleX);
        const bottom = pad(bounds.y + bounds.height - (y + height), scaleY);
        // Scrollable content starts at the SVG's top-left corner. Extend only the
        // far edges for off-sheet artwork; negative coordinates do not shift either origin.
        stage.style.padding = `0 ${right}px ${bottom}px 0`;
    }

    function updateLayout() {
        scaleCanvas();
        layoutStage();
    }

    function changeZoom(value, anchor, resetView = false) {
        if (gesture || !Number.isFinite(value)) return;
        value = Math.max(Number(slider.min), Math.min(Number(slider.max), value));
        if (zoom === value) {
            if (resetView) {
                viewport.scrollTo(0, 0);
                schedule(true);
            }
            return;
        }
        // Zoom from the docked edges by default. After a deliberate pan on an axis,
        // keep the old cursor-centred zoom on that axis until the view is reset.
        const pinX = resetView || viewport.scrollLeft === 0;
        const pinY = resetView || viewport.scrollTop === 0;
        const view = viewport.getBoundingClientRect();
        const cursor = anchor ?? new DOMPoint(view.left + viewport.clientWidth / 2, view.top + viewport.clientHeight / 2);
        const mm = pinX && pinY ? null : toDocument(cursor);
        zoom = value;
        scaleCanvas();
        layoutStage();
        if (resetView) {
            viewport.scrollTo(0, 0);
        } else if (mm) {
            const matrix = svg.getScreenCTM();
            if (matrix) {
                const screen = mm.matrixTransform(matrix);
                viewport.scrollTo(pinX ? 0 : viewport.scrollLeft + screen.x - cursor.x,
                    pinY ? 0 : viewport.scrollTop + screen.y - cursor.y);
            }
        }
        slider.value = zoom;
        reset.textContent = percentages.format(zoom);
        slider.setAttribute('aria-valuetext', reset.textContent);
        schedule(true);
    }

    function fit() {
        if (gesture) return;
        const { width, height } = svg.viewBox.baseVal;
        if (width <= 0 || height <= 0) return;
        // The SVG includes the band. Leave the normal gutter only on the far edges.
        changeZoom(Math.min((viewport.clientWidth - stageGutter) / svg.width.baseVal.value,
            (viewport.clientHeight - stageGutter) / svg.height.baseVal.value, 1), undefined, true);
    }

    function invoke(method, ...args) {
        return dotnetRef.invokeMethodAsync(method, ...args).catch(error => {
            if (!events.signal.aborted) console.error('Label editor:', error);
        });
    }

    function restoreGroup(group) {
        group.removeAttribute('transform');
        group.classList.remove('is-dragging');
    }

    function clearGesture(keepPreview = false) {
        const current = gesture;
        if (!current) return;
        gesture = null;
        if (current.group && !keepPreview) restoreGroup(current.group);
        viewport.classList.remove('is-panning');
        if (viewport.hasPointerCapture(current.id)) viewport.releasePointerCapture(current.id);
    }

    function onDown(e) {
        if (!e.isPrimary || gesture || paused()) return;
        const point = position(e);
        if (e.button === 1) {
            gesture = { id: e.pointerId, point, scrollX: viewport.scrollLeft, scrollY: viewport.scrollTop };
            viewport.classList.add('is-panning');
        } else if (e.button === 0 && !committing) {
            const group = e.target.closest('g[data-shape-index]');
            if (!group) {
                if (e.target !== viewport) void invoke('ShapeClicked', null);
                return;
            }
            const start = toDocument(point);
            if (!start) return;
            gesture = { id: e.pointerId, point, group, start, moved: false };
        } else return;
        viewport.focus({ preventScroll: true });
        viewport.setPointerCapture(e.pointerId);
        e.preventDefault();
    }

    function move(e) {
        if (!e.isPrimary) return;
        pointer = position(e);
        schedule();
        if (gesture?.id !== e.pointerId) return;
        if (!gesture.group) {
            viewport.scrollLeft = gesture.scrollX + gesture.point.x - pointer.x;
            viewport.scrollTop = gesture.scrollY + gesture.point.y - pointer.y;
            return;
        }
        if (!gesture.group.isConnected) { clearGesture(); return; }
        gesture.moved ||= Math.hypot(pointer.x - gesture.point.x, pointer.y - gesture.point.y) >= 3;
        if (!gesture.moved) return;
        const mm = toDocument(pointer);
        if (mm) {
            gesture.group.classList.add('is-dragging');
            gesture.group.setAttribute('transform', `translate(${mm.x - gesture.start.x} ${mm.y - gesture.start.y})`);
        }
    }

    function onUp(e) {
        if (gesture?.id !== e.pointerId) return;
        move(e);
        if (!gesture) return;
        const { group, start, moved } = gesture;
        if (!group) { clearGesture(); return; }
        const index = Number(group.dataset.shapeIndex);
        const mm = moved ? toDocument(position(e)) : null;
        clearGesture(!!mm);
        if (!mm) { void invoke('ShapeClicked', index); return; }
        committing = true;
        void invoke('ShapeDragged', index, Number(group.dataset.x) + mm.x - start.x, Number(group.dataset.y) + mm.y - start.y)
            .finally(() => { restoreGroup(group); committing = false; });
    }

    function onLeave() { pointer = null; schedule(); }
    function cancel(e) {
        if (gesture?.id !== e.pointerId) return;
        clearGesture();
        onLeave();
    }

    listen(viewport, 'pointerdown', onDown);
    listen(workspace, 'pointermove', move);
    listen(workspace, 'pointerleave', onLeave);
    listen(viewport, 'pointerup', onUp);
    listen(viewport, 'pointercancel', cancel);
    listen(viewport, 'lostpointercapture', cancel);
    listen(viewport, 'auxclick', e => { if (e.button === 1) e.preventDefault(); });
    listen(viewport, 'keydown', e => { if (e.key === 'Escape') clearGesture(); });
    listen(window, 'blur', () => { clearGesture(); onLeave(); });
    listen(viewport, 'scroll', () => schedule(true));
    listen(window, 'scroll', () => schedule());
    listen(viewport, 'wheel', e => {
        if (!e.ctrlKey || paused()) return;
        e.preventDefault();
        const delta = e.deltaY * (e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? viewport.clientHeight : 1);
        changeZoom(zoom * Math.exp(-delta * 0.002), position(e));
    }, { passive: false });
    listen(slider, 'input', () => changeZoom(slider.valueAsNumber));
    listen(reset, 'click', () => changeZoom(1, undefined, true));
    listen(find('sync-zoom-fit'), 'click', fit);

    const dimensions = new ResizeObserver(() => {
        updateLayout();
        schedule(true);
    });
    dimensions.observe(viewport);
    const settings = new MutationObserver(records => {
        if (paused()) { clearGesture(); pointer = null; }
        if (records.some(record => record.target === svg || record.target === artwork)) updateLayout();
        schedule(true);
    });
    settings.observe(root, { attributeFilter: ['data-unit', 'data-units-per-mm', 'data-settings-open'] });
    settings.observe(svg, { attributeFilter: ['width', 'height', 'viewBox', 'data-non-printable-mm'] });
    if (artwork) settings.observe(artwork, { childList: true });
    const lifecycle = new MutationObserver(() => { if (!root.isConnected) cleanup(); });
    lifecycle.observe(document.body, { childList: true, subtree: true });
    function cleanup() {
        events.abort();
        clearGesture();
        dimensions.disconnect();
        settings.disconnect();
        lifecycle.disconnect();
        cancelAnimationFrame(frame);
        editors.delete(root);
    }
    editors.set(root, cleanup);
    updateLayout();
    schedule(true);
    return true;
}

export function downloadFile(fileName, base64) {
    const bytes = Uint8Array.from(atob(base64), character => character.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: 'application/octet-stream' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    link.click();
    URL.revokeObjectURL(url);
}
