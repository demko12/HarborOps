window.voyageRouteMap = {
    render(element, stops) {
        if (!element || !window.L || !stops || stops.length < 2) return;
        const signature = JSON.stringify(stops);
        if (element._harborOpsSignature === signature && element._harborOpsMap) return;
        const mappedStops = unwrapLongitudes(stops);
        const coordinates = curvedRoute(mappedStops);
        const map = element._harborOpsMap ?? L.map(element, { zoomControl: true, scrollWheelZoom: false });
        if (!element._harborOpsMap) {
            L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
                maxZoom: 18,
                attribution: "© OpenStreetMap contributors"
            }).addTo(map);
            element._harborOpsMap = map;
        }
        if (element._harborOpsLayer) map.removeLayer(element._harborOpsLayer);
        const layer = L.featureGroup().addTo(map);
        element._harborOpsLayer = layer;
        L.polyline(coordinates, { color: "#60a5fa", weight: 10, opacity: 0.12, interactive: false }).addTo(layer);
        L.polyline(coordinates, { color: "#4285e4", weight: 2.5, opacity: 0.8, interactive: false }).addTo(layer);
        L.polyline(coordinates, { color: "#bae6fd", weight: 3, opacity: 0.95, dashArray: "5 24", className: "voyage-map-flow", interactive: false }).addTo(layer);
        mappedStops.forEach(stop => {
            const status = ["planned", "arrived", "departed"].includes(String(stop.status).toLowerCase()) ? stop.status.toLowerCase() : "planned";
            L.marker([stop.latitude, stop.longitude], {
                title: `${stop.stopOrder}. ${stop.portName} · ${stop.status}`,
                alt: `${stop.portName}, ${stop.status}`,
                icon: L.divIcon({ className: `voyage-map-marker ${status}`, html: `<span>${escapeHtml(stop.stopOrder)}</span>`, iconSize: [32, 32], iconAnchor: [16, 16] })
            }).bindTooltip(escapeHtml(stop.portName), { direction: "top", offset: [0, -18], className: "voyage-map-tooltip" })
                .bindPopup(`<div class="voyage-map-popup"><small>PORT CALL ${escapeHtml(stop.stopOrder)}</small><strong>${escapeHtml(stop.portName)}</strong><span>${escapeHtml(stop.portCountry)} · ${escapeHtml(stop.status)}</span></div>`).addTo(layer);
        });
        element._harborOpsSignature = signature;
        window.voyageRouteMap.fit(element);
        cancelAnimationFrame(element._harborOpsFrame);
        element._harborOpsFrame = requestAnimationFrame(() => map.invalidateSize());
    },
    fit(element) {
        if (!element?._harborOpsMap || !element._harborOpsLayer) return;
        element._harborOpsMap.fitBounds(element._harborOpsLayer.getBounds(), { padding: [44, 44], maxZoom: 6, animate: false });
    },
    renderMiniMaps(container) {
        if (!container || !window.L) return;
        container.querySelectorAll("[data-route-stops]").forEach(element => {
            const signature = element.dataset.routeStops;
            if (element._harborOpsSignature === signature) return;
            element._harborOpsSignature = signature;
            miniMapObserver.observe(element);
        });
    },
    dispose(element) {
        if (!element?._harborOpsMap) return;
        cancelAnimationFrame(element._harborOpsFrame);
        element._harborOpsMap.remove();
        delete element._harborOpsMap;
        delete element._harborOpsLayer;
        delete element._harborOpsSignature;
        delete element._harborOpsFrame;
    }
};

const miniMapObserver = new IntersectionObserver(entries => entries.forEach(entry => {
    if (!entry.isIntersecting) return;
    miniMapObserver.unobserve(entry.target);
    drawMiniMap(entry.target);
}), { rootMargin: "200px" });

function drawMiniMap(element) {
    const stops = unwrapLongitudes(JSON.parse(element.dataset.routeStops));
    if (stops.length < 2) return;
    const map = element._harborOpsMap ?? L.map(element, {
        zoomControl: false,
        dragging: false,
        scrollWheelZoom: false,
        doubleClickZoom: false,
        boxZoom: false,
        keyboard: false,
        touchZoom: false
    });
    if (!element._harborOpsMap) {
        L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
            maxZoom: 18,
            attribution: "© OpenStreetMap"
        }).addTo(map);
        map.attributionControl.setPrefix(false);
        element._harborOpsMap = map;
    }
    if (element._harborOpsLayer) map.removeLayer(element._harborOpsLayer);
    const layer = L.featureGroup().addTo(map);
    element._harborOpsLayer = layer;
    const coordinates = curvedRoute(stops);
    L.polyline(coordinates, { color: "#60a5fa", weight: 6, opacity: 0.18, interactive: false }).addTo(layer);
    L.polyline(coordinates, { color: "#4285e4", weight: 2, opacity: 0.9, interactive: false }).addTo(layer);
    stops.forEach(stop => {
        const status = ["planned", "arrived", "departed"].includes(String(stop.status).toLowerCase()) ? stop.status.toLowerCase() : "planned";
        L.marker([stop.latitude, stop.longitude], {
            interactive: false,
            keyboard: false,
            icon: L.divIcon({ className: `voyage-map-marker voyage-map-marker-mini ${status}`, html: `<span>${escapeHtml(stop.stopOrder)}</span>`, iconSize: [18, 18], iconAnchor: [9, 9] })
        }).addTo(layer);
    });
    map.fitBounds(layer.getBounds(), { padding: [18, 18], maxZoom: 6, animate: false });
}

// Keep adjacent ports in the nearest world copy, including dateline crossings.
function unwrapLongitudes(stops) {
    const mappedStops = stops.map(stop => ({ ...stop }));
    for (let index = 1; index < mappedStops.length; index++) {
        const previous = mappedStops[index - 1].longitude;
        while (mappedStops[index].longitude - previous > 180) mappedStops[index].longitude -= 360;
        while (mappedStops[index].longitude - previous < -180) mappedStops[index].longitude += 360;
    }
    return mappedStops;
}

function curvedRoute(stops) {
    const route = [];
    for (let index = 1; index < stops.length; index++) {
        const start = stops[index - 1];
        const end = stops[index];
        const bend = Math.min(12, Math.hypot(end.latitude - start.latitude, end.longitude - start.longitude) * 0.18);
        const controlLatitude = Math.min(82, Math.max(-82, (start.latitude + end.latitude) / 2 + bend));
        for (let step = index === 1 ? 0 : 1; step <= 24; step++) {
            const progress = step / 24;
            const inverse = 1 - progress;
            route.push([
                inverse * inverse * start.latitude + 2 * inverse * progress * controlLatitude + progress * progress * end.latitude,
                inverse * start.longitude + progress * end.longitude
            ]);
        }
    }
    return route;
}

function escapeHtml(value) {
    return String(value).replace(/[&<>'"]/g, character => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" })[character]);
}

// Recorded positions use straight segments, ordered by report time.
window.vesselPositionMap = (() => {
    const maps = new Map();
    function dispose(id) {
        const state = maps.get(id);
        if (!state) return;
        cancelAnimationFrame(state.frame);
        state.observer?.disconnect();
        state.map.remove();
        maps.delete(id);
    }
    function fit(id) {
        const state = maps.get(id);
        if (state?.layer) {
            state.map.invalidateSize();
            state.map.fitBounds(state.layer.getBounds(), { padding: [44, 44], maxZoom: 12, animate: false });
        }
    }
    return {
        render(id, element, reports) {
            if (!element || !window.L || !Array.isArray(reports) || !reports.length) return false;
            if (reports.some(report => !Number.isFinite(report.latitude) || Math.abs(report.latitude) > 90
                || !Number.isFinite(report.longitude) || Math.abs(report.longitude) > 180
                || !Number.isFinite(Date.parse(report.reportedAtUtc)))) return false;
            const ordered = [...reports].sort((a, b) => Date.parse(a.reportedAtUtc) - Date.parse(b.reportedAtUtc)
                || String(a.id).localeCompare(String(b.id)));
            const signature = JSON.stringify(ordered);
            let state = maps.get(id);
            if (state && state.element !== element) { dispose(id); state = null; }
            if (state?.signature === signature) return true;
            try {
                if (!state) {
                    state = { element, map: L.map(element, { zoomControl: true, scrollWheelZoom: false }) };
                    maps.set(id, state);
                    L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
                        maxZoom: 18, attribution: "&copy; OpenStreetMap contributors"
                    }).addTo(state.map);
                    if (typeof ResizeObserver !== "undefined") {
                        state.observer = new ResizeObserver(() => state.map.invalidateSize());
                        state.observer.observe(element);
                    }
                }
                if (state.layer) state.map.removeLayer(state.layer);
                state.layer = L.featureGroup().addTo(state.map);
                const positions = unwrapLongitudes(ordered);
                if (positions.length > 1) {
                    L.polyline(positions.map(report => [report.latitude, report.longitude]), {
                        color: "#4285e4", weight: 2.5, opacity: 0.8, interactive: false
                    }).addTo(state.layer);
                }
                positions.forEach((report, index) => {
                    const original = ordered[index];
                    const timestamp = new Date(report.reportedAtUtc).toISOString().replace("T", " ").replace(/\.\d{3}Z$/, " UTC");
                    const latest = index === positions.length - 1;
                    L.marker([report.latitude, report.longitude], {
                        title: `Position ${index + 1}, ${timestamp}${latest ? ", latest report on this page" : ""}`,
                        alt: `Position report ${index + 1}`,
                        icon: L.divIcon({ className: `voyage-map-marker ${latest ? "arrived" : "planned"}`,
                            html: `<span>${index + 1}</span>`, iconSize: [32, 32], iconAnchor: [16, 16] })
                    }).bindTooltip(escapeHtml(timestamp), { direction: "top", offset: [0, -18], className: "voyage-map-tooltip" })
                        .bindPopup(`<div class="voyage-map-popup"><small>POSITION ${index + 1}${latest ? " · LATEST ON THIS PAGE" : ""}</small><strong>${escapeHtml(timestamp)}</strong><span>${original.latitude.toFixed(4)}°, ${original.longitude.toFixed(4)}°</span><span>${escapeHtml(report.reportedByDisplayName ?? report.reportedByUserId ?? "Unknown reporter")}</span></div>`)
                        .addTo(state.layer);
                });
                state.signature = signature;
                fit(id);
                cancelAnimationFrame(state.frame);
                state.frame = requestAnimationFrame(() => state.map.invalidateSize());
                return true;
            } catch {
                dispose(id);
                return false;
            }
        },
        fit,
        dispose
    };
})();
