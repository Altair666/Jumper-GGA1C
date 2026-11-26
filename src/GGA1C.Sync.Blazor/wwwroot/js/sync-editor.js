import './page-setup.js';

// P12.4 editor interaction. SVG coordinates are millimetres; ruler coordinates are local CSS pixels.
export function initEditor(svgId, dotnetRef) {
    const svg = document.getElementById(svgId);
    if (!svg) return false;
    if (typeof svg.__syncEditorCleanup === 'function') svg.__syncEditorCleanup();
    const topRuler = document.getElementById('sync-ruler-top');
    const leftRuler = document.getElementById('sync-ruler-left');
    const last = { x: 0, y: 0 };
    let drag = null;
    const canvasMm = e => {
        const rect = svg.getBoundingClientRect(), vb = svg.viewBox.baseVal;
        return { x: vb.x + (e.clientX - rect.left) * vb.width / Math.max(1, rect.width), y: vb.y + (e.clientY - rect.top) * vb.height / Math.max(1, rect.height) };
    };
    const rulerMm = (e, axis) => {
        const vb = svg.viewBox.baseVal;
        if (axis === 'x' && topRuler) { const r = topRuler.getBoundingClientRect(); return { x: vb.x + (e.clientX - r.left) * vb.width / Math.max(1, r.width), y: last.y }; }
        if (leftRuler) { const r = leftRuler.getBoundingClientRect(); return { x: last.x, y: vb.y + (e.clientY - r.top) * vb.height / Math.max(1, r.height) }; }
        return last;
    };
    const update = pos => { last.x = pos.x; last.y = pos.y; moveMarkers(pos.x, pos.y); };
    const onPointerDown = e => {
        const target = e.target instanceof Element ? e.target : null, group = target?.closest('g[data-shape-index]');
        if (!group || e.button !== 0) return;
        const pos = canvasMm(e);
        drag = { index: Number.parseInt(group.dataset.shapeIndex, 10), group, startXmm: Number.parseFloat(group.dataset.x), startYmm: Number.parseFloat(group.dataset.y), anchorXmm: pos.x - Number.parseFloat(group.dataset.x), anchorYmm: pos.y - Number.parseFloat(group.dataset.y), moved: false, pointerId: e.pointerId };
        update(pos); svg.setPointerCapture?.(e.pointerId); e.preventDefault();
    };
    const onPointerMove = e => {
        const pos = canvasMm(e); update(pos);
        if (!drag || drag.pointerId !== e.pointerId) return;
        const nextX = pos.x - drag.anchorXmm, nextY = pos.y - drag.anchorYmm;
        const dx = nextX - drag.startXmm, dy = nextY - drag.startYmm;
        if (Math.abs(dx) > .2 || Math.abs(dy) > .2) drag.moved = true;
        drag.group.setAttribute('transform', `translate(${dx},${dy})`); e.preventDefault();
    };
    const finishPointer = e => {
        if (!drag || drag.pointerId !== e.pointerId) return;
        const pos = canvasMm(e), payload = { index: drag.index, x: pos.x - drag.anchorXmm, y: pos.y - drag.anchorYmm }, moved = drag.moved, index = drag.index;
        drag.group.removeAttribute('transform'); svg.releasePointerCapture?.(e.pointerId); drag = null;
        if (moved) dotnetRef.invokeMethodAsync('ShapeDragged', index, payload.x, payload.y); else dotnetRef.invokeMethodAsync('ShapeClicked', index);
        e.preventDefault();
    };
    const cancelPointer = e => { if (drag?.pointerId === e.pointerId) { drag.group.removeAttribute('transform'); drag = null; } };
    const onCanvasMove = e => update(canvasMm(e)), onTopMove = e => update(rulerMm(e, 'x')), onLeftMove = e => update(rulerMm(e, 'y'));
    const inside = (r, x, y) => r && x >= r.left && x <= r.right && y >= r.top && y <= r.bottom;
    // GoLabel tracks the mouse globally, not only while the pointer is over a shape.
    // This keeps the orange-red crosshair and both ruler marks alive over every object.
    const onDocumentMove = e => {
        const x = e.clientX, y = e.clientY;
        const canvasRect = svg.getBoundingClientRect();
        if (inside(canvasRect, x, y)) { update(canvasMm(e)); return; }
        if (topRuler && inside(topRuler.getBoundingClientRect(), x, y)) { update(rulerMm(e, 'x')); return; }
        if (leftRuler && inside(leftRuler.getBoundingClientRect(), x, y)) { update(rulerMm(e, 'y')); }
    };
    svg.addEventListener('pointerdown', onPointerDown); svg.addEventListener('pointermove', onPointerMove); svg.addEventListener('pointerup', finishPointer); svg.addEventListener('pointercancel', cancelPointer); svg.addEventListener('lostpointercapture', cancelPointer); svg.addEventListener('pointermove', onCanvasMove);
    topRuler?.addEventListener('pointermove', onTopMove); leftRuler?.addEventListener('pointermove', onLeftMove); document.addEventListener('pointermove', onDocumentMove, true);
    svg.__syncEditorCleanup = () => { svg.removeEventListener('pointerdown', onPointerDown); svg.removeEventListener('pointermove', onPointerMove); svg.removeEventListener('pointerup', finishPointer); svg.removeEventListener('pointercancel', cancelPointer); svg.removeEventListener('lostpointercapture', cancelPointer); svg.removeEventListener('pointermove', onCanvasMove); topRuler?.removeEventListener('pointermove', onTopMove); leftRuler?.removeEventListener('pointermove', onLeftMove); document.removeEventListener('pointermove', onDocumentMove, true); delete svg.__syncEditorCleanup; };
    return true;
}
export function hideMarkers() { for (const id of ['sync-marker-top', 'sync-marker-left', 'sync-cross-x', 'sync-cross-y', 'sync-guide-vertical', 'sync-guide-horizontal']) document.getElementById(id)?.setAttribute('visibility', 'hidden'); }
function moveMarkers(xMm, yMm) {
    const svg = document.getElementById('sync-canvas-svg'); if (!svg) return;
    const vb = svg.viewBox.baseVal, canvas = svg.getBoundingClientRect(), top = document.getElementById('sync-ruler-top'), left = document.getElementById('sync-ruler-left'), topRect = top?.getBoundingClientRect(), leftRect = left?.getBoundingClientRect();
    const xCanvas = (xMm - vb.x) * Math.max(1, canvas.width) / vb.width, yCanvas = (yMm - vb.y) * Math.max(1, canvas.height) / vb.height;
    const crossX = document.getElementById('sync-cross-x'), crossY = document.getElementById('sync-cross-y');
    crossX?.setAttribute('x1', xMm); crossX?.setAttribute('x2', xMm); crossX?.setAttribute('visibility', 'visible'); crossY?.setAttribute('y1', yMm); crossY?.setAttribute('y2', yMm); crossY?.setAttribute('visibility', 'visible');
    const markerTop = document.getElementById('sync-marker-top'); if (markerTop && topRect) { const x = Math.max(0, Math.min(topRect.width, xCanvas)); markerTop.setAttribute('x1', x); markerTop.setAttribute('x2', x); markerTop.setAttribute('visibility', 'visible'); }
    const markerLeft = document.getElementById('sync-marker-left'); if (markerLeft && leftRect) { const y = Math.max(0, Math.min(leftRect.height, yCanvas)); markerLeft.setAttribute('y1', y); markerLeft.setAttribute('y2', y); markerLeft.setAttribute('visibility', 'visible'); }
    const guideV = document.getElementById('sync-guide-vertical'), guideH = document.getElementById('sync-guide-horizontal');
    if (guideV) { guideV.style.left = `${22 + xCanvas}px`; guideV.style.height = `${canvas.height}px`; guideV.style.visibility = 'visible'; }
    if (guideH) { guideH.style.top = `${22 + yCanvas}px`; guideH.style.width = `${canvas.width}px`; guideH.style.visibility = 'visible'; }
    const readout = document.getElementById('sync-pos-readout'); if (readout) readout.textContent = `${xMm.toFixed(1)} / ${yMm.toFixed(1)} mm`;
}
window.syncEditorDownloadFile = function (fileName, base64) { const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0)); const url = URL.createObjectURL(new Blob([bytes], { type: "application/octet-stream" })); const link = document.createElement('a'); link.href = url; link.download = fileName; document.body.appendChild(link); link.click(); link.remove(); URL.revokeObjectURL(url); };
