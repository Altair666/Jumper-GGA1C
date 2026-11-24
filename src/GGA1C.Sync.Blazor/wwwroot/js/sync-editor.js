// Обвязка рабочей области редактора (П12.4): перетаскивание объектов мышью, маркер
// позиции на линейках, скачивание файла.
//
// Перетаскивание живёт в JS целиком: Blazor Server не должен получать событие на каждый
// шаг мыши (ленточка SignalR и отрисовка через канал дали бы дёрганье). JS двигает
// выделенную группу сам (transform), а .NET зовёт один раз — на отпускании кнопки, с
// итоговыми координатами в мм. Клик без движения — выделение. Маркер мыши на линейках
// тоже двигается здесь, без .NET.

export function initEditor(svgId, dotnetRef) {
    const svg = document.getElementById(svgId);
    if (!svg) {
        return false;
    }

    let drag = null; // { index, group, startXmm, startYmm, startMouseXmm, startMouseYmm, moved }

    const eventMm = e => {
        const rect = svg.getBoundingClientRect();
        const viewBox = svg.viewBox.baseVal;
        return {
            x: (e.clientX - rect.left) * viewBox.width / Math.max(1, rect.width),
            y: (e.clientY - rect.top) * viewBox.height / Math.max(1, rect.height),
        };
    };

    svg.addEventListener('pointerdown', e => {
        const group = e.target.closest('g[data-shape-index]');
        if (!group || e.button !== 0) {
            return;
        }

        const pos = eventMm(e);
        drag = {
            index: parseInt(group.dataset.shapeIndex, 10),
            group,
            startXmm: parseFloat(group.dataset.x),
            startYmm: parseFloat(group.dataset.y),
            startMouseXmm: pos.x,
            startMouseYmm: pos.y,
            moved: false,
        };
        svg.setPointerCapture(e.pointerId);
        e.preventDefault();
    });

    svg.addEventListener('pointermove', e => {
        const pos = eventMm(e);
        moveMarkers(pos.x, pos.y);

        if (!drag) {
            return;
        }

        const dx = pos.x - drag.startMouseXmm;
        const dy = pos.y - drag.startMouseYmm;
        if (Math.abs(dx) > 0.2 || Math.abs(dy) > 0.2) {
            drag.moved = true;
        }
        drag.group.setAttribute('transform', `translate(${dx},${dy})`);
        e.preventDefault();
    });

    svg.addEventListener('pointerup', e => {
        if (!drag) {
            return;
        }

        const pos = eventMm(e);
        const payload = {
            index: drag.index,
            x: drag.startXmm + (pos.x - drag.startMouseXmm),
            y: drag.startYmm + (pos.y - drag.startMouseYmm),
        };
        const moved = drag.moved;
        drag.group.removeAttribute('transform');
        drag = null;

        if (moved) {
            dotnetRef.invokeMethodAsync('ShapeDragged', payload.index, payload.x, payload.y);
        } else {
            dotnetRef.invokeMethodAsync('ShapeClicked', payload.index);
        }
    });

    return true;
}

export function hideMarkers() {
    for (const id of ['sync-marker-top', 'sync-marker-left', 'sync-cross-x', 'sync-cross-y']) {
        const marker = document.getElementById(id);
        if (marker) {
            marker.setAttribute('visibility', 'hidden');
        }
    }
}

/// Маркер позиции на линейках и перекрестие на канве. Позиции — в экранных пикселях
/// относительно канвы: линейки стоят вровень с ней, поэтому тем же числом.
function moveMarkers(xMm, yMm) {
    const svg = document.getElementById('sync-canvas-svg');
    if (!svg) {
        return;
    }
    const rect = svg.getBoundingClientRect();
    const viewBox = svg.viewBox.baseVal;
    const px = x => (x - viewBox.x) * Math.max(1, rect.width) / viewBox.width;
    const py = y => (y - viewBox.y) * Math.max(1, rect.height) / viewBox.height;

    const crossX = document.getElementById('sync-cross-x');
    const crossY = document.getElementById('sync-cross-y');
    const markerTop = document.getElementById('sync-marker-top');
    const markerLeft = document.getElementById('sync-marker-left');
    const readout = document.getElementById('sync-pos-readout');

    const pxX = px(xMm);
    const pxY = px(yMm);

    if (crossX) {
        crossX.setAttribute('x1', pxX);
        crossX.setAttribute('x2', pxX);
        crossX.removeAttribute('visibility');
    }
    if (crossY) {
        crossY.setAttribute('y1', pxY);
        crossY.setAttribute('y2', pxY);
        crossY.removeAttribute('visibility');
    }
    if (markerTop) {
        markerTop.setAttribute('x1', pxX);
        markerTop.setAttribute('x2', pxX);
        markerTop.removeAttribute('visibility');
    }
    if (markerLeft) {
        markerLeft.setAttribute('y1', pxY);
        markerLeft.setAttribute('y2', pxY);
        markerLeft.removeAttribute('visibility');
    }
    if (readout) {
        readout.textContent = `${xMm.toFixed(1)} / ${yMm.toFixed(1)} мм`;
    }
}

// Скачивание файла: байты приходят base64 по каналу SignalR, в браузере собираются
// в blob и отдаются ссылкой.
window.syncEditorDownloadFile = function (fileName, base64) {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const blob = new Blob([bytes], { type: 'application/octet-stream' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
};
