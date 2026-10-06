'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const { fixture, commit } = require('./release-fixture');
const { publish, publicationPlan } = require('../publish-release');
const { safeEvidenceEntries, compareRegistry } = require('../verify-openupm');
const { archive } = require('./tar-fixture');
function use(t) { const f = fixture(); t.after(f.remove); fs.writeFileSync(path.join(f.output, 'verification-evidence.tar.gz'), 'verified logs'); return f; }
class FakeGitHub {
  constructor() { this.tag = null; this.savedRelease = null; this.contents = new Map(); this.events = []; this.failOnUpload = undefined; this.uploadCount = 0; }
  async tagCommit() { return this.tag; }
  async release() { return this.savedRelease && structuredClone(this.savedRelease); }
  async releaseById() { return this.savedRelease && structuredClone(this.savedRelease); }
  async createTag(tag, sha) { this.events.push(['tag', tag, sha]); this.tag = sha; }
  async createRelease(tag, sha) { this.events.push(['release', tag, sha]); return (this.savedRelease = { id: 1, draft: true, assets: [] }); }
  async uploadAsset(release, name, bytes) {
    if (++this.uploadCount === this.failOnUpload) throw new Error('simulated upload interruption');
    this.events.push(['upload', name]); const asset = { id: this.contents.size + 1, name }; this.savedRelease.assets.push(asset); this.contents.set(asset.id, Buffer.from(bytes)); return asset;
  }
  async downloadAsset(asset) { return this.contents.get(asset.id); }
  async finishRelease() { this.events.push(['finish']); this.savedRelease.draft = false; }
  async dispatchOpenUpm(tag) { this.events.push(['dispatch-tag-ref', tag]); }
}
test('publish creates version tag, complete immutable assets, then tag-ref OpenUPM dispatch', async t => {
  const f = use(t); const client = new FakeGitHub(); await publish(client, f.output, f.manifest);
  assert.deepEqual(client.events[0], ['tag', '0.5.0', commit]); assert.equal(client.savedRelease.assets.length, 5); assert.equal(client.savedRelease.draft, false); assert.deepEqual(client.events.at(-1), ['dispatch-tag-ref', '0.5.0']);
  const uploads = client.uploadCount; await publish(client, f.output, f.manifest); assert.equal(client.uploadCount, uploads, 'same-artifact retry must not replace assets');
});
test('interrupted draft Release resumes only missing assets from the same artifact', async t => {
  const f = use(t); const client = new FakeGitHub(); client.failOnUpload = 3;
  await assert.rejects(publish(client, f.output, f.manifest), /interruption/); assert.equal(client.savedRelease.draft, true); assert.equal(client.savedRelease.assets.length, 2); assert.equal(client.events.some(entry => entry[0] === 'dispatch-tag-ref'), false);
  client.failOnUpload = undefined; await publish(client, f.output, f.manifest); assert.equal(client.savedRelease.assets.length, 5); assert.equal(client.events.filter(entry => entry[0] === 'tag').length, 1); assert.equal(client.events.filter(entry => entry[0] === 'upload' && entry[1] === 'release-manifest.json').length, 1);
});
test('different existing tag or asset fails before any remote mutation', async t => {
  const f = use(t); const client = new FakeGitHub(); client.tag = 'b'.repeat(40); await assert.rejects(publish(client, f.output, f.manifest), /different commit/); assert.equal(client.events.length, 0);
  client.tag = null; await publish(client, f.output, f.manifest); const count = client.events.length; fs.appendFileSync(path.join(f.output, 'release-manifest.json'), 'different run'); await assert.rejects(publish(client, f.output, f.manifest), /asset differs/); assert.equal(client.events.length, count);
});
test('published missing/unexpected assets are never modified; tag-only retry is supported', () => {
  assert.throws(() => publicationPlan(commit, commit, { draft: false, assets: [] }, ['one']), /incomplete/);
  assert.throws(() => publicationPlan(commit, commit, { draft: true, assets: [{ name: 'other' }] }, ['one']), /unexpected/);
  assert.throws(() => publicationPlan(commit, null, { draft: true, assets: [] }, ['one']), /no matching tag/);
  assert.deepEqual(publicationPlan(commit, commit, null, ['one']), { createTag: false, createRelease: true, missing: ['one'] });
});
test('reject unsafe archive paths, links and special files before extraction', () => {
  safeEvidenceEntries(['release-manifest.json', 'unity-gate/6000.3.2f1/gate.json'], ['-', '-']);
  for (const entry of ['/etc/passwd', '../leak', 'unity-gate/../leak', 'unrelated']) assert.throws(() => safeEvidenceEntries([entry], ['-']), /unsafe/);
  for (const type of ['l', 'h', 'b']) assert.throws(() => safeEvidenceEntries(['unity-gate/log'], [type]), /links or special/);
});
function registryFixture(t) {
  const f = use(t);
  const base = 'jp.rhycol.openapicodegen';
  const addon = `${base}.source-generator`;
  const manifest = { name: base, version: '0.5.0', displayName: 'Base', repository: { type: 'git', url: 'https://github.com/rebeat-jp/UnityOpenApiCodeGen/tree/0.5.0/Packages/OpenApiCodeGen' }, dependencies: {} };
  const published = { ...manifest, repository: { type: 'git', url: 'https://github.com/rebeat-jp/UnityOpenApiCodeGen', revision: commit }, publishConfig: { registry: 'https://package.openupm.com' } };
  const entries = (json = manifest, files = [{ name: 'package/Runtime/code.cs', content: 'validated code' }]) => [{ name: 'package/package.json', content: JSON.stringify(json) }, ...files];
  fs.writeFileSync(path.join(f.output, `${base}-0.5.0.tgz`), archive(entries()));
  const addonBytes = fs.readFileSync(path.join(f.output, `${addon}-0.5.0.tgz`));
  const request = (baseBytes = archive(entries(published)), options = {}) => async raw => {
    const url = new URL(raw);
    const name = url.pathname.includes(addon) ? addon : base;
    if (url.pathname.endsWith('/0.5.0')) return options.metadataResponse || { ok: true, json: async () => ({ name, version: options.version || '0.5.0', dist: { tarball: `https://package.openupm.com/${name}.tgz` } }) };
    if (options.tarballResponse) return options.tarballResponse;
    return { ok: true, arrayBuffer: async () => name === base ? baseBytes : options.addonBytes || addonBytes };
  };
  return { f, manifest, published, entries, request, addonBytes };
}
test('registry comparison accepts OpenUPM base manifest patch and exact Source Generator bytes', async t => {
  const { f, request } = registryFixture(t);
  await compareRegistry(f.output, '0.5.0', request());
});
test('registry comparison rejects changed base content, missing and extra files', async t => {
  const { f, published, entries, request } = registryFixture(t);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(archive(entries(published, [{ name: 'package/Runtime/code.cs', content: 'changed' }])))), /content differs/);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(archive(entries(published, [])))), /file tree differs/);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(archive(entries(published, [{ name: 'package/Runtime/code.cs', content: 'validated code' }, { name: 'package/extra', content: 'extra' }])))), /file tree differs/);
});
test('registry comparison rejects unsafe, duplicate and special tar entries', async t => {
  const { f, published, entries, request } = registryFixture(t);
  for (const item of [
    { name: 'package/../escape', content: 'bad' },
    { name: '/absolute', content: 'bad' },
    { name: 'package/link', type: '2' },
    { name: 'package/Runtime/code.cs', content: 'duplicate' }
  ]) await assert.rejects(compareRegistry(f.output, '0.5.0', request(archive([...entries(published), item]))), /unsafe path|link|duplicate path/);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(archive([...entries(published), { name: 'package/empty/', type: '5' }]))), /file tree differs/);
});
test('registry comparison rejects tar names whose prefix or BOM has a different extraction meaning', async t => {
  const { f, published, entries, request } = registryFixture(t);
  const [manifest, code] = entries(published);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(archive([
    { ...manifest, name: 'package.json', prefix: 'package', magic: '', version: '' }, code
  ]))), /unsupported tar format/);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(archive([
    { ...manifest, name: '\uFEFFpackage.json', prefix: 'package' }, code
  ]))), /unsafe path/);
});
test('registry comparison rejects unrelated manifest edits and invalid OpenUPM metadata', async t => {
  const { f, published, entries, request } = registryFixture(t);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(archive(entries({ ...published, displayName: 'Tampered' })))), /package.json has unexpected differences/);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(archive(entries({ ...published, publishConfig: { registry: 'https://elsewhere.invalid' } })))), /publishConfig/);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(archive(entries({ ...published, repository: { type: 'git', url: 'https://github.com/other/repo', revision: '0.5.0' } })))), /repository metadata/);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(archive(entries({ ...published, repository: { ...published.repository, revision: 'b'.repeat(40) } })))), /repository metadata/);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(archive(entries({ ...published, repository: { ...published.repository, revision: undefined } })))), /repository metadata/);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(undefined, { version: '0.4.0' })), /metadata/);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(undefined, { metadataResponse: { ok: false, status: 503 } })), /HTTP 503/);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(undefined, { metadataResponse: { ok: true, json: async () => ({ name: 'jp.rhycol.openapicodegen', version: '0.5.0', dist: { tarball: 'http://example.invalid/a.tgz' } }) } })), /metadata/);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(undefined, { tarballResponse: { ok: false, status: 502 } })), /HTTP 502/);
});
test('registry comparison still requires exact Source Generator tarball bytes', async t => {
  const { f, request } = registryFixture(t);
  await assert.rejects(compareRegistry(f.output, '0.5.0', request(undefined, { addonBytes: Buffer.from('different') })), /Source Generator tarball differs/);
});
test('trusted release workflow rejects non-main code even when target contract is a no-op', t => {
  const os = require('os'); const { execFileSync, spawnSync } = require('child_process');
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'release-boundary-')); t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const git = args => execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
  git(['init', '--initial-branch=main']); fs.writeFileSync(path.join(root, 'contract.js'), 'process.exit(0);'); git(['add', '.']); git(['-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-m', 'main']); const main = git(['rev-parse', 'HEAD']); git(['update-ref', 'refs/remotes/origin/main', main]);
  git(['switch', '--orphan', 'untrusted']); fs.writeFileSync(path.join(root, 'contract.js'), 'process.exit(0);'); git(['add', '.']); git(['-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-m', 'untrusted no-op contract']); const untrusted = git(['rev-parse', 'HEAD']);
  const workflow = fs.readFileSync(path.join(__dirname, '../../../.github/workflows/source-generator-release.yml'), 'utf8');
  function step(name) {
    const start = workflow.indexOf(`      - name: ${name}\n`); assert.ok(start >= 0);
    const section = workflow.slice(start).split(/\n      - /)[0]; const run = section.split('        run: |\n')[1]; assert.ok(run);
    return run.split('\n').filter(line => line.startsWith('          ')).map(line => line.slice(10)).join('\n');
  }
  const checkoutBoundary = step('Enforce trusted checkout and main ancestry before target scripts');
  const publishBoundary = step('Enforce trusted publication boundary before target scripts');
  const run = (script, target, publish = 'true', ref = 'refs/heads/main') => spawnSync('bash', ['-e', '-o', 'pipefail', '-c', script], { cwd: root, encoding: 'utf8', env: { ...process.env, TARGET_COMMIT: target, PUBLISH: publish, GITHUB_REF: ref } }).status;
  assert.equal(spawnForNoop(root), 0, 'attacker contract itself succeeds');
  assert.notEqual(run(checkoutBoundary, untrusted), 0); assert.notEqual(run(publishBoundary, untrusted), 0); assert.notEqual(run(checkoutBoundary, untrusted, 'false'), 0, 'dry-run rejects a non-main commit');
  git(['switch', 'main']); assert.equal(run(checkoutBoundary, main), 0); assert.equal(run(checkoutBoundary, main, 'false'), 0); assert.equal(run(publishBoundary, main), 0); assert.notEqual(run(checkoutBoundary, untrusted), 0, 'exact checkout mismatch rejected'); assert.notEqual(run(checkoutBoundary, main, 'true', 'refs/heads/develop'), 0);
  assert.notEqual(run(step('Validate literal publication inputs before checkout'), 'main'), 0, 'symbolic refs are rejected before checkout');
  assert.notEqual(run(step('Validate literal publication inputs before checkout'), main, 'false', 'refs/heads/feature'), 0, 'dry-run rejects a non-main dispatch ref before checkout');
  assert.notEqual(run(step('Validate literal publication inputs before checkout'), main, 'true', 'refs/heads/feature'), 0, 'publish rejects a non-main dispatch ref before checkout');
  function spawnForNoop(cwd) { return spawnSync(process.execPath, ['contract.js'], { cwd }).status; }
});
test('real GitHub client honors draft HTTP API semantics and refreshes by Release ID', async t => {
  const { GitHubClient } = require('../publish-release'); const f = use(t); const originalFetch = global.fetch; t.after(() => { global.fetch = originalFetch; });
  let tag = null; let release = null; const contents = new Map(); const routes = [];
  global.fetch = async (rawUrl, init) => {
    const url = new URL(rawUrl); const route = url.pathname.replace('/repos/owner/repo/', '') + url.search; const method = init.method; routes.push(`${method} ${route}`);
    const jsonBody = init.body && typeof init.body === 'string' ? JSON.parse(init.body) : undefined;
    let status = 200; let data; let binary;
    if (method === 'GET' && route === 'git/ref/tags/0.5.0') { if (tag) data = { object: { type: 'commit', sha: tag } }; else status = 404; }
    else if (method === 'GET' && route.startsWith('releases/tags/')) { if (!release || release.draft) status = 404; else data = release; }
    else if (method === 'GET' && route === 'releases?per_page=100&page=1') data = release ? [release] : [];
    else if (method === 'POST' && route === 'git/refs') { tag = jsonBody.sha; status = 201; data = {}; }
    else if (method === 'POST' && route === 'releases') { release = { id: 1, tag_name: jsonBody.tag_name, draft: true, assets: [] }; status = 201; data = release; }
    else if (method === 'POST' && route.startsWith('releases/1/assets?name=')) { const name = url.searchParams.get('name'); const asset = { id: contents.size + 1, name }; release.assets.push(asset); contents.set(asset.id, Buffer.from(init.body)); status = 201; data = asset; }
    else if (method === 'GET' && route === 'releases/1') data = release;
    else if (method === 'GET' && /^releases\/assets\/[0-9]+$/.test(route)) binary = contents.get(Number(route.split('/').at(-1)));
    else if (method === 'PATCH' && route === 'releases/1') { release.draft = jsonBody.draft; data = release; }
    else if (method === 'POST' && route.endsWith('/dispatches')) status = 204;
    else throw new Error(`Unexpected test API route: ${method} ${route}`);
    return { status, ok: status < 400, json: async () => structuredClone(data), arrayBuffer: async () => binary };
  };
  const client = new GitHubClient('owner/repo', 'fake-token'); await publish(client, f.output, f.manifest);
  assert.equal(release.draft, false); assert.equal(release.assets.length, 5); assert.ok(routes.includes('GET releases/1')); assert.equal(routes.some(route => route.includes('releases/tags/')), false, 'draft lifecycle must not use published-only lookup');
  const saved = structuredClone(release); release.draft = true; assert.equal((await client.release('0.5.0')).draft, true, 'retry finds draft through authenticated list');
  release = saved; const uploadCount = routes.filter(route => route.startsWith('POST releases/1/assets')).length; await publish(client, f.output, f.manifest); assert.equal(routes.filter(route => route.startsWith('POST releases/1/assets')).length, uploadCount);
});
test('draft lookup paginates and rejects multiple Releases matching one tag', async t => {
  const { GitHubClient } = require('../publish-release'); const originalFetch = global.fetch; t.after(() => { global.fetch = originalFetch; });
  const pages = [Array.from({ length: 100 }, (_, i) => ({ tag_name: `other-${i}` })), [{ id: 9, tag_name: '0.5.0', draft: true }]];
  global.fetch = async url => { const page = Number(new URL(url).searchParams.get('page')); return { ok: true, status: 200, json: async () => structuredClone(pages[page - 1]) }; };
  const client = new GitHubClient('owner/repo', 'fake-token'); assert.equal((await client.release('0.5.0')).id, 9);
  pages[1].push({ id: 10, tag_name: '0.5.0', draft: true }); await assert.rejects(client.release('0.5.0'), /Multiple Releases/);
});
