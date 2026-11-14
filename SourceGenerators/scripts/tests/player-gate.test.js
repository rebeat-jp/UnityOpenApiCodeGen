'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');
const { startServer, validateGadgetWire } = require('../player-gate-server');

const verifyPlayer = path.join(__dirname, '..', 'verify-player.js');

const validBody = {
  id: 42,
  name: 'posted',
  quantity: 5,
  color: 'red',
  level: 3,
  createdAt: '2025-01-02T03:04:05+00:00',
  birthday: '2000-12-31',
  scores: { x: 10 },
  tags: ['one', 'two', 'three'],
  shape: { kind: 'square', side: 3 },
};

async function request(origin, method, pathname, body) {
  const response = await fetch(origin + pathname, {
    method,
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  return { status: response.status, text: await response.text(), type: response.headers.get('content-type') };
}

test('the wire validator accepts the probe body and names each deviation', () => {
  assert.deepEqual(validateGadgetWire(JSON.stringify(validBody)), { valid: true, problems: [] });
  const wrongEnum = validateGadgetWire(JSON.stringify({ ...validBody, color: 'Red' }));
  assert.equal(wrongEnum.valid, false);
  assert.match(wrongEnum.problems.join('\n'), /color/);
  const noDiscriminator = validateGadgetWire(JSON.stringify({ ...validBody, shape: { side: 3 } }));
  assert.equal(noDiscriminator.valid, false);
  assert.match(noDiscriminator.problems.join('\n'), /shape/);
  assert.equal(validateGadgetWire('not json').valid, false);
  assert.equal(validateGadgetWire(JSON.stringify({ ...validBody, extra: 1 })).valid, false);
});

test('the server serves the canned GET, records POST bodies and receives the report', async t => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'player-gate-server-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  fs.writeFileSync(path.join(root, 'index.html'), '<html></html>');
  fs.writeFileSync(path.join(root, 'game.wasm'), Buffer.from([0, 97, 115, 109]));
  const server = await startServer({ staticRoot: root });
  t.after(() => server.close());

  const got = await request(server.origin, 'GET', '/api/gadgets/7');
  assert.equal(got.status, 200);
  assert.equal(JSON.parse(got.text).shape.kind, 'circle');

  const posted = await request(server.origin, 'POST', '/api/gadgets', validBody);
  assert.equal(posted.status, 201);
  assert.equal(JSON.parse(posted.text).id, 42);
  assert.equal(server.state.gets, 1);
  assert.equal(server.state.posts, 1);
  assert.equal(server.state.postResults[0].valid, true);
  assert.match(server.state.contentTypes[0], /^application\/json/);

  const page = await request(server.origin, 'GET', '/');
  assert.equal(page.status, 200);
  assert.match(page.type, /^text\/html/);
  const wasm = await fetch(`${server.origin}/game.wasm`);
  assert.equal(wasm.headers.get('content-type'), 'application/wasm');
  assert.equal((await request(server.origin, 'GET', '/../secret')).status, 404);

  await request(server.origin, 'POST', '/report', { passed: true });
  assert.deepEqual(JSON.parse(await server.reportPromise), { passed: true });
});

test('the matrix lists the Standalone cells, the WebGL cells and the unsupported WebGL HttpClient cell', () => {
  const result = spawnSync(process.execPath, [verifyPlayer, '--list'], { encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  const cells = result.stdout.trim().split(/\r?\n/);
  const standalone = cells.filter(id => /^(win64|linux64|osx)-il2cpp-/.test(id));
  assert.equal(standalone.length, 8);
  for (const stripping of ['minimal', 'high']) {
    for (const transport of ['httpclient', 'uwr']) {
      for (const serializer of ['newtonsoft', 'stj']) {
        assert.ok(standalone.some(id => id.endsWith(`-il2cpp-${stripping}-${transport}-${serializer}`)));
      }
    }
  }
  for (const stripping of ['minimal', 'high']) {
    for (const serializer of ['newtonsoft', 'stj']) {
      assert.ok(cells.includes(`webgl-il2cpp-${stripping}-uwr-${serializer}`));
    }
  }
  assert.ok(cells.includes('webgl-il2cpp-minimal-httpclient-newtonsoft'));
  assert.equal(cells.length, 13);

  const mono = spawnSync(process.execPath, [verifyPlayer, '--list', '--include-mono'], { encoding: 'utf8' });
  assert.equal(mono.stdout.trim().split(/\r?\n/).length, 15);
});

test('an unknown cell or option is rejected as not-run (exit code 2)', () => {
  const unknownCell = spawnSync(process.execPath, [verifyPlayer, '--list', '--cells', 'no-such-cell'], { encoding: 'utf8' });
  assert.equal(unknownCell.status, 2);
  const unknownOption = spawnSync(process.execPath, [verifyPlayer, '--nope'], { encoding: 'utf8' });
  assert.equal(unknownOption.status, 2);
});
