const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { resolve } = require('node:path');
const vm = require('node:vm');

const source = readFileSync(resolve(__dirname, '../../../src/HarborOps.Web/wwwroot/js/file-download.js'), 'utf8');

function response({ ok = true, status = 200, redirected = false, headers = {}, json, blob = 'csv' }) {
    const lower = Object.fromEntries(Object.entries(headers).map(([key, value]) => [key.toLowerCase(), value]));
    return {
        ok,
        status,
        redirected,
        headers: { get: name => lower[name.toLowerCase()] ?? null },
        json: async () => { if (json === undefined) { throw new Error('not json'); } return json; },
        blob: async () => blob
    };
}

function setup(fetchResult) {
    const clicks = [], revoked = [], requests = [];
    const document = {
        body: { appendChild() {} },
        createElement: () => ({ click() { clicks.push({ href: this.href, download: this.download }); }, remove() {} })
    };
    const context = {
        window: {},
        document,
        URL: { createObjectURL: () => 'blob:manifest', revokeObjectURL: url => revoked.push(url) },
        setTimeout: callback => callback(),
        fetch: async (url, options) => {
            requests.push({ url, options });
            if (fetchResult instanceof Error) { throw fetchResult; }
            return fetchResult;
        }
    };
    vm.runInNewContext(source, context);
    return { api: context.window.harborOpsFiles, clicks, revoked, requests };
}

test('a csv response is saved under the server file name without caching', async () => {
    const state = setup(response({
        headers: {
            'Content-Type': 'text/csv; charset=utf-8',
            'Content-Disposition': "attachment; filename=manifest.csv; filename*=UTF-8''manifest-BK-1-20260928T1432Z.csv",
            'X-Matching-Count': '20'
        }
    }));

    const result = await state.api.download('/cargo/1/manifest/export?search=MSKU', 'text/csv');

    assert.deepEqual({ ...result }, { ok: true, status: 200, message: null, count: 20 });
    assert.equal(state.requests.length, 1);
    assert.equal(state.requests[0].url, '/cargo/1/manifest/export?search=MSKU');
    assert.equal(state.requests[0].options.credentials, 'same-origin');
    assert.equal(state.requests[0].options.cache, 'no-store');
    assert.deepEqual(state.clicks, [{ href: 'blob:manifest', download: 'manifest-BK-1-20260928T1432Z.csv' }]);
    assert.deepEqual(state.revoked, ['blob:manifest']);
});

test('a validation problem returns its messages and saves nothing', async () => {
    const state = setup(response({
        ok: false,
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
        json: { title: 'One or more validation errors occurred.', errors: { limit: ['More than 10,000 containers match.'] } }
    }));

    const result = await state.api.download('/cargo/1/manifest/export', 'text/csv');

    assert.deepEqual({ ...result }, { ok: false, status: 400, message: 'More than 10,000 containers match.', count: null });
    assert.equal(state.clicks.length, 0);
});

test('a problem without field errors falls back to its detail or title', async () => {
    const state = setup(response({ ok: false, status: 503, json: { title: 'HarborOps API is unavailable', detail: 'Please try again.' } }));

    const result = await state.api.download('/cargo/1/manifest/export', 'text/csv');

    assert.equal(result.message, 'Please try again.');
});

test('a redirect to sign in is reported instead of saving the login page', async () => {
    const state = setup(response({ redirected: true, headers: { 'Content-Type': 'text/html' } }));

    const result = await state.api.download('/cargo/1/manifest/export', 'text/csv');

    assert.equal(result.ok, false);
    assert.equal(result.status, 401);
    assert.equal(state.clicks.length, 0);
});

test('a network failure reports status zero so the page can offer a retry', async () => {
    const state = setup(new Error('offline'));

    const result = await state.api.download('/cargo/1/manifest/export', 'text/csv');

    assert.deepEqual({ ...result }, { ok: false, status: 0, message: null, count: null });
});

test('a missing or malformed matching count is reported as unknown', async () => {
    for (const header of [undefined, '', '12a', '-1']) {
        const headers = { 'Content-Type': 'text/csv; charset=utf-8' };
        if (header !== undefined) { headers['X-Matching-Count'] = header; }
        const state = setup(response({ headers }));

        const result = await state.api.download('/cargo/1/manifest/export', 'text/csv');

        assert.equal(result.ok, true);
        assert.equal(result.count, null);
    }
});
