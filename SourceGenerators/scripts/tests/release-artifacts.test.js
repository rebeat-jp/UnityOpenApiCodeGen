'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const { execFileSync, spawnSync } = require('child_process');
const { fixture, commit } = require('./release-fixture');
const { validateRelease, validateTestXml } = require('../validate-release-artifacts');
const { aggregate } = require('../aggregate-unity-evidence');
const { assertVersionContract } = require('../release-contract');
const { validateUpdate, analyzer } = require('../update-analyzer-pr');
const { serialFromLicense } = require('../unity-license');
const { redact } = require('../ci-evidence');
function use(t, ci = true) { const f = fixture(ci); t.after(f.remove); return f; }
function mutateGate(f, version, change) {
  const file = path.join(f.output, 'unity-gate', version, 'gate.json'); const data = JSON.parse(fs.readFileSync(file)); change(data); fs.writeFileSync(file, JSON.stringify(data));
}
test('CI candidate and complete evidence validate and finalize checksums', t => {
  const f = use(t); validateRelease(f.options); f.gates(); aggregate({ ...f.options, gateMode: '--require-passed-unity-gate' });
  const result = validateRelease({ ...f.options, gateMode: '--require-passed-unity-gate' }); assert.equal(result.unityGate.status, 'passed');
});
test('manual Mac evidence remains supported', t => { const f = use(t, false); f.gates(); aggregate({ ...f.options, gateMode: '--require-passed-unity-gate' }); });
test('reject wrong target version, commit, CI run and attempt', t => {
  const f = use(t);
  for (const [key, value, message] of [['expected-version', '0.6.0', /version/], ['expected-commit', 'b'.repeat(40), /commit/], ['expected-run-id', '124', /run ID/], ['expected-run-attempt', '2', /attempt/]]) assert.throws(() => validateRelease({ ...f.options, [key]: value }), message);
  f.manifest.provenance.treeState = 'dirty'; f.manifest.provenance.commit = null; f.save(); assert.throws(() => validateRelease(f.options), /clean|commit/);
});
test('reject tarball tampering and stale DLL', t => {
  const f = use(t); fs.writeFileSync(f.analyzer, 'stale analyzer'); assert.throws(() => validateRelease(f.options), /analyzer SHA/);
  fs.writeFileSync(f.analyzer, 'deterministic analyzer'); fs.appendFileSync(path.join(f.output, f.manifest.archives[0].file), 'tamper'); assert.throws(() => validateRelease(f.options), /size or SHA/);
});
test('reject missing and wrong checksums', t => {
  const f = use(t); fs.writeFileSync(path.join(f.output, 'SHA256SUMS'), 'bad'); assert.throws(() => validateRelease(f.options), /SHA256SUMS/);
  f.save(); const sums = path.join(f.output, 'SHA256SUMS'); fs.writeFileSync(sums, fs.readFileSync(sums, 'utf8').replace(/^[0-9a-f]{64}/, '0'.repeat(64))); assert.throws(() => validateRelease(f.options), /checksum mismatch/);
});
test('reject missing, escaped or tampered gate files', t => {
  for (const mode of ['missing', 'escaped', 'tampered']) {
    const f = use(t); f.gates();
    if (mode === 'missing') fs.rmSync(path.join(f.output, 'unity-gate/2021.3.19f1/base.log'));
    if (mode === 'escaped') mutateGate(f, '2021.3.19f1', g => { g.files[0].path = '../secret'; });
    if (mode === 'tampered') fs.appendFileSync(path.join(f.output, 'unity-gate/2021.3.19f1/base.log'), 'bad');
    assert.throws(() => aggregate(f.options), /missing|escapes|hash mismatch/);
  }
});
test('reject passed status with failed exit code or evidence from another candidate/run', t => {
  for (const mutation of [g => { g.exitCode = 1; }, g => { g.ci.commit = 'b'.repeat(40); }, g => { g.ci.runId = '124'; }, g => { g.ci.runAttempt = '2'; }, g => { g.ci.candidateSha256 = '0'.repeat(64); }, g => { delete g.ci; }]) {
    const f = use(t); f.gates(); mutateGate(f, '2021.3.19f1', mutation); assert.throws(() => aggregate(f.options), /exit code|CI gate/);
  }
});
test('failed and not-run gates can be recorded but never pass release gate', t => {
  for (const status of ['failed', 'not-run']) { const f = use(t); f.gates(status); aggregate({ ...f.options, gateMode: '--require-unity-gate' }); assert.throws(() => validateRelease({ ...f.options, gateMode: '--require-passed-unity-gate' }), /must pass/); }
});
test('strict XML rejects truncated, zero-test, failed and incomplete runs', t => {
  const f = use(t); const file = path.join(f.root, 'result.xml');
  for (const text of ['<test-run result="Passed">', '<test-run result="Passed" total="0" passed="0" failed="0" />', '<test-run result="Passed" total="1" passed="1" failed="0" />', '<test-run result="Failed" total="1" passed="0" failed="1"><test-case result="Failed" /></test-run>', '<test-run result="Passed" total="1" passed="1" failed="0"><test-case result="Inconclusive" /></test-run>']) { fs.writeFileSync(file, text); assert.throws(() => validateTestXml(file), /XML/); }
});
test('version and DLL update allowlists preserve public package metadata', () => {
  const base = { name: 'jp.rhycol.openapicodegen', version: '0.5.0' }; const addon = { name: 'jp.rhycol.openapicodegen.source-generator', version: '0.5.0', dependencies: { [base.name]: '0.5.0' } };
  assert.equal(assertVersionContract(base, addon, '0.5.0'), '0.5.0'); assert.throws(() => assertVersionContract(base, { ...addon, version: '0.4.0' }), /mismatch/); assert.throws(() => assertVersionContract({ ...base, version: '0.5.0-rc1' }, addon), /stable/);
  validateUpdate('develop', [analyzer]); validateUpdate('main', []); assert.throws(() => validateUpdate('feature/anything', []), /base/); assert.throws(() => validateUpdate('develop', [`${analyzer}.meta`]), /only/);
});
test('Personal parser matches GameCI serial decoding and rejects corrupt license', () => {
  const serial = 'F4-ABCD-EFGH-IJKL-MNOP-QRST'; const data = Buffer.concat([Buffer.from([1, 0, 0, 0]), Buffer.from(serial)]).toString('base64');
  assert.equal(serialFromLicense(`<root><DeveloperData Value="${data}"/></root>`), serial); assert.throws(() => serialFromLicense('<root/>'), /DeveloperData/); assert.throws(() => serialFromLicense('<DeveloperData Value="YmFk"/>'), /Personal/);
});
test('artifact redaction covers credentials and decoded/masked serial', t => {
  const saved = { ...process.env }; t.after(() => { process.env = saved; });
  Object.assign(process.env, { UNITY_LICENSE: 'ulf fixture', UNITY_EMAIL: 'sso@example.com', UNITY_PASSWORD: 'fixture-password', UNITY_SERIAL: 'F4-ABCD-EFGH-IJKL-MNOP-QRST' });
  const output = redact('ulf fixture sso@example.com fixture-password F4-ABCD-EFGH-IJKL-MNOP-QRST F4-ABCD-EFGH-IJKL-MNOP-XXXX'); assert.equal(output, Array(5).fill('[REDACTED]').join(' '));
});
test('missing and malformed Secrets fail explicitly with non-success CI evidence', t => {
  for (const license of ['', '<corrupt/>']) {
    const f = use(t);
    const env = { ...process.env, SOURCE_GENERATOR_CI: '1', SOURCE_GENERATOR_CI_COMMIT: commit, SOURCE_GENERATOR_RELEASE_OUTPUT: f.output, GITHUB_REPOSITORY: 'owner/repo', GITHUB_RUN_ID: '123', GITHUB_RUN_ATTEMPT: '1', UNITY_LICENSE: license, UNITY_EMAIL: 'sso@example.com', UNITY_PASSWORD: 'fixture-password' };
    const result = spawnSync('bash', [path.join(__dirname, '../run-ci-unity.sh'), '2021.3.19f1'], { env, encoding: 'utf8' });
    assert.equal(result.status, 2, result.stderr); assert.doesNotMatch(result.stdout + result.stderr, /sso@example.com|fixture-password/);
    const gate = JSON.parse(fs.readFileSync(path.join(f.output, 'unity-gate/2021.3.19f1/gate.json'))); assert.equal(gate.status, 'not-run'); assert.equal(gate.exitCode, 2);
  }
});
test('provenance failure sanitizes mounted evidence before refusing gate creation', t => {
  const f = use(t); const saved = { ...process.env }; t.after(() => { process.env = saved; });
  const dir = path.join(f.output, 'unity-gate/2021.3.19f1'); fs.mkdirSync(dir, { recursive: true });
  const log = path.join(dir, 'base.log'); fs.writeFileSync(log, 'fake-password');
  Object.assign(process.env, { SOURCE_GENERATOR_CI: '1', SOURCE_GENERATOR_CI_COMMIT: 'b'.repeat(40), GITHUB_REPOSITORY: 'owner/repo', GITHUB_RUN_ID: '123', GITHUB_RUN_ATTEMPT: '1', UNITY_PASSWORD: 'fake-password' });
  assert.throws(() => require('../ci-evidence').writeGate([path.join(dir, 'gate.json'), 'passed', '0', '2021.3.19f1', 'fixture', dir, f.output]), /provenance/);
  assert.equal(fs.readFileSync(log, 'utf8'), '[REDACTED]');
  fs.writeFileSync(path.join(f.output, 'release-manifest.json'), '{invalid'); fs.writeFileSync(log, 'fake-password');
  assert.throws(() => require('../ci-evidence').writeGate([path.join(dir, 'gate.json'), 'passed', '0', '2021.3.19f1', 'fixture', dir, f.output])); assert.equal(fs.readFileSync(log, 'utf8'), '[REDACTED]');
});
