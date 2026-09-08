const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const vm = require('node:vm');

const source = readFileSync(resolve(__dirname, '../../../src/HarborOps.Web/wwwroot/js/voyage-route-map.js'), 'utf8');

function setup() {
    const lines = [], markers = [], removed = [];
    let fits = 0, disposed = false;
    const map = {
        fitBounds() { fits++; }, invalidateSize() {},
        removeLayer(layer) { removed.push(layer); }, remove() { disposed = true; }
    };
    const L = {
        map: () => map,
        tileLayer: () => ({ addTo() {} }),
        featureGroup: () => ({ addTo() { return this; }, getBounds() { return {}; } }),
        polyline(points, options) { lines.push({ points, options }); return { addTo() {} }; },
        divIcon: options => options,
        marker(point, options) {
            const marker = { point, options, bindTooltip(html) { this.tooltip = html; return this; }, bindPopup(html) { this.popup = html; return this; }, addTo() {} };
            markers.push(marker);
            return marker;
        }
    };
    const context = { window: { L }, L, requestAnimationFrame: () => 1, cancelAnimationFrame() {} };
    vm.runInNewContext(source, context);
    return { api: context.window.voyageRouteMap, lines, markers, removed, fits: () => fits, disposed: () => disposed };
}

const stops = [
    { latitude: 35, longitude: 170, stopOrder: 1, portName: 'Origin', portCountry: 'A', status: 'Departed' },
    { latitude: 38, longitude: -170, stopOrder: 2, portName: 'Destination', portCountry: 'B', status: 'Planned' }
];

test('dateline routes use adjacent world copies without mutating the itinerary', () => {
    const { api, lines, markers } = setup();
    api.render({}, stops);
    assert.equal(markers[1].point[1], 190);
    assert.equal(stops[1].longitude, -170);
    const longitudes = lines[0].points.map(point => point[1]);
    assert.equal(Math.max(...longitudes) - Math.min(...longitudes), 20);
});

test('ordinary rerenders preserve the map view and updates replace the old layer', () => {
    const state = setup(), element = {};
    state.api.render(element, stops);
    state.api.render(element, stops.map(stop => ({ ...stop })));
    assert.equal(state.fits(), 1);
    assert.equal(state.lines.length, 3);
    state.api.fit(element);
    assert.equal(state.fits(), 2);
    state.api.render(element, stops.map(stop => ({ ...stop, status: 'Arrived' })));
    assert.equal(state.removed.length, 1);
});

test('port content is escaped and unknown statuses cannot inject marker classes', () => {
    const { api, markers } = setup();
    api.render({}, stops.map(stop => ({ ...stop, portName: '<img src=x onerror=alert(1)>', status: 'bad" class="injected' })));
    assert.ok(markers[0].tooltip.startsWith('&lt;img'));
    assert.ok(!markers[0].popup.includes('<img'));
    assert.equal(markers[0].options.icon.className, 'voyage-map-marker planned');
});

test('dispose clears map state and permits rendering again', () => {
    const state = setup(), element = {};
    state.api.render(element, stops);
    state.api.dispose(element);
    assert.equal(state.disposed(), true);
    assert.equal(element._harborOpsMap, undefined);
    assert.equal(element._harborOpsSignature, undefined);
    state.api.render(element, stops);
    assert.equal(state.fits(), 2);
});
