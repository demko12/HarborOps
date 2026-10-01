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
    const context = { window: { L }, L, requestAnimationFrame: () => 1, cancelAnimationFrame() {},
        IntersectionObserver: class { observe() {} unobserve() {} } };
    vm.runInNewContext(source, context);
    return { api: context.window.voyageRouteMap, positions: context.window.vesselPositionMap, fleet: context.window.fleetPositionsMap, context, lines, markers, removed, fits: () => fits, disposed: () => disposed };
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

const reports = [
    { id: 'new', latitude: 38, longitude: -170, reportedAtUtc: '2026-09-02T10:00:00Z', reportedByDisplayName: '<img src=x onerror=alert(1)>' },
    { id: 'old', latitude: 35, longitude: 170, reportedAtUtc: '2026-09-01T10:00:00Z', reportedByDisplayName: 'Operator' }
];

test('position history connects chronological reports across the dateline and highlights latest', () => {
    const state = setup();
    assert.equal(state.positions.render('vessel', {}, reports), true);
    assert.equal(state.markers[0].point[1], 170);
    assert.equal(state.markers[1].point[1], 190);
    assert.equal(state.lines[0].points.length, 2);
    assert.equal(state.markers[1].options.icon.className, 'voyage-map-marker arrived');
    assert.ok(state.markers[1].popup.includes('&lt;img'));
    assert.ok(!state.markers[1].popup.includes('<img'));
    assert.equal(reports[0].longitude, -170);
    assert.equal(reports[0].id, 'new');
});

test('a single position has a marker, preserves viewport on rerender, and cleans up on disposal', () => {
    const state = setup(), element = {};
    state.positions.render('vessel', element, [reports[0]]);
    state.positions.render('vessel', element, [reports[0]]);
    assert.equal(state.lines.length, 0);
    assert.equal(state.markers.length, 1);
    assert.equal(state.fits(), 1);
    state.positions.fit('vessel');
    assert.equal(state.fits(), 2);
    state.positions.render('vessel', element, reports);
    assert.equal(state.removed.length, 1);
    state.positions.dispose('vessel');
    assert.equal(state.disposed(), true);
    state.positions.render('vessel', {}, reports);
    assert.equal(state.fits(), 4);
});

test('position map fails gracefully for missing Leaflet and invalid coordinates', () => {
    const state = setup();
    assert.equal(state.positions.render('vessel', {}, [{ ...reports[0], latitude: 91 }]), false);
    assert.equal(state.positions.render('vessel', {}, [{ ...reports[0], reportedAtUtc: 'invalid' }]), false);
    assert.equal(state.positions.render('vessel', {}, []), false);
    delete state.context.window.L;
    assert.equal(state.positions.render('vessel', {}, reports), false);
    assert.equal(state.markers.length, 0);
});

const vessels = [
    { vesselId: '40000000-0000-0000-0000-000000000001', name: 'Northern Star', imoNumber: '5100001',
        latitude: 35, longitude: -5, currentStatus: 'Active', fleetName: 'Adriatic Fleet',
        lastPositionReportedAtUtc: '2026-09-30T10:00:00Z', isPositionStale: false },
    { vesselId: '40000000-0000-0000-0000-000000000002', name: 'Southern Cross', imoNumber: '5100002',
        latitude: -1, longitude: 36, currentStatus: 'At Port', fleetName: null,
        lastPositionReportedAtUtc: null, isPositionStale: true }
];

test('fleet map shows independent ship positions, identity and report details', () => {
    const state = setup();
    assert.equal(state.fleet.render('fleet', {}, vessels), true);
    assert.equal(state.lines.length, 0);
    assert.equal(state.markers.length, 2);
    assert.equal(state.markers[0].options.icon.className, 'fleet-map-ship current');
    assert.equal(state.markers[1].options.icon.className, 'fleet-map-ship stale');
    assert.ok(state.markers[0].options.icon.html.includes('<svg'));
    assert.ok(state.markers[0].tooltip.includes('Northern Star'));
    assert.ok(state.markers[0].tooltip.includes('IMO 5100001'));
    assert.ok(state.markers[0].popup.includes('Adriatic Fleet'));
    assert.ok(state.markers[0].popup.includes('2026-09-30 10:00:00 UTC'));
    assert.ok(state.markers[0].popup.includes('/fleet/' + vessels[0].vesselId));
    assert.ok(state.markers[1].popup.includes('Report time unknown'));
});

test('fleet map escapes vessel data and excludes invalid detail links', () => {
    const state = setup();
    state.fleet.render('fleet', {}, [{ ...vessels[0], name: '<img src=x onerror=alert(1)>',
        imoNumber: '<script>', currentStatus: '<svg>', fleetName: '<iframe>', vesselId: 'bad" onclick="alert(1)' }]);
    assert.ok(state.markers[0].tooltip.includes('&lt;img'));
    assert.ok(!state.markers[0].popup.includes('<img'));
    assert.ok(!state.markers[0].popup.includes('<a '));
});

test('fleet map preserves the viewport on rerender, replaces markers and cleans up', () => {
    const state = setup(), element = {};
    state.fleet.render('fleet', element, vessels);
    state.fleet.render('fleet', element, vessels.map(vessel => ({ ...vessel })));
    assert.equal(state.fits(), 1);
    assert.equal(state.markers.length, 2);
    state.fleet.render('fleet', element, [{ ...vessels[0], latitude: 40 }]);
    assert.equal(state.removed.length, 1);
    assert.equal(state.fits(), 1);
    state.fleet.fit('fleet');
    assert.equal(state.fits(), 2);
    state.fleet.dispose('fleet');
    assert.equal(state.disposed(), true);
    state.fleet.render('fleet', {}, [vessels[0]]);
    assert.equal(state.fits(), 3);
});

test('fleet map identifies AIS observations in the popup', () => {
    const state = setup();
    state.fleet.render('fleet', {}, [{ ...vessels[0], positionSource: 'AISStream', mmsi: '227441980' }]);
    assert.ok(state.markers[0].popup.includes('Source: AISStream'));
    assert.ok(state.markers[0].popup.includes('MMSI 227441980'));
});

test('fleet map rejects invalid positions and handles missing Leaflet', () => {
    const state = setup();
    assert.equal(state.fleet.render('fleet', {}, [{ ...vessels[0], latitude: null }]), false);
    assert.equal(state.fleet.render('fleet', {}, [{ ...vessels[0], longitude: 181 }]), false);
    assert.equal(state.fleet.render('fleet', {}, []), false);
    delete state.context.window.L;
    assert.equal(state.fleet.render('fleet', {}, vessels), false);
    assert.equal(state.markers.length, 0);
});
