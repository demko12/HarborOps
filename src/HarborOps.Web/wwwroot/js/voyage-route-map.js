window.voyageRouteMap = {
    render(element, stops) {
        if (!element || !window.L || !stops || stops.length < 2) return;
        const signature = JSON.stringify(stops);
        if (element._harborOpsSignature === signature && element._harborOpsMap) return;
        // Keep adjacent ports in the nearest world copy, including dateline crossings.
        const mappedStops = stops.map(stop => ({ ...stop }));
        for (let index = 1; index < mappedStops.length; index++) {
            const previous = mappedStops[index - 1].longitude;
            while (mappedStops[index].longitude - previous > 180) mappedStops[index].longitude -= 360;
            while (mappedStops[index].longitude - previous < -180) mappedStops[index].longitude += 360;
        }
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
