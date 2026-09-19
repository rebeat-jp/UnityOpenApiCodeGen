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
const { validateUpdate, copyAnalyzerArtifact, analyzer } = require('../update-analyzer-pr');
const { serialFromLicense } = require('../unity-license');
const { redact, writeGate } = require('../ci-evidence');
function use(t, ci = true) { const f = fixture(ci); t.after(f.remove); return f; }
function mutateGate(f, version, change) {
  const file = path.join(f.output, 'unity-gate', version, 'gate.json'); const data = JSON.parse(fs.readFileSync(file)); change(data); fs.writeFileSync(file, JSON.stringify(data));
}
test('CI candidate and complete evidence validate and finalize checksums', t => {
  const f = use(t); validateRelease(f.options); f.gates(); aggregate({ ...f.options, gateMode: '--require-passed-unity-gate' });
  const result = validateRelease({ ...f.options, gateMode: '--require-passed-unity-gate' }); assert.equal(result.unityGate.status, 'passed');
});
test('Unity rerun evidence retains candidate attempt and records execution attempt', t => {
  const f = fixture(true, '2'); t.after(f.remove); f.gates();
  const options = { ...f.options, 'expected-execution-attempt': '2', gateMode: '--require-passed-unity-gate' };
  aggregate(options);
  const result = validateRelease(options);
  assert.equal(result.provenance.ci.runAttempt, '1');
  assert.throws(() => validateRelease({ ...options, 'expected-execution-attempt': '1' }), /execution attempt/);
});
test('gate writer separates a reused candidate from the current execution attempt', t => {
  const f = use(t); const saved = { ...process.env }; t.after(() => { process.env = saved; });
  const dir = path.join(f.output, 'unity-gate/2021.3.19f1'); fs.mkdirSync(dir, { recursive: true });
  Object.assign(process.env, { SOURCE_GENERATOR_CI: '1', SOURCE_GENERATOR_CI_COMMIT: commit, SOURCE_GENERATOR_CANDIDATE_ATTEMPT: '1', GITHUB_REPOSITORY: 'owner/repo', GITHUB_RUN_ID: '123', GITHUB_RUN_ATTEMPT: '2' });
  const output = path.join(dir, 'gate.json');
  writeGate([output, 'not-run', '2', '2021.3.19f1', 'fixture', dir, f.output]);
  const ci = JSON.parse(fs.readFileSync(output)).ci;
  assert.equal(ci.runAttempt, '1');
  assert.equal(ci.executionAttempt, '2');
});
test('completed schema v2 gates without executionAttempt remain readable', t => {
  const f = use(t); f.gates();
  for (const version of require('../validate-release-artifacts').expectedGateVersions) mutateGate(f, version, gate => { delete gate.ci.executionAttempt; });
  aggregate({ ...f.options, gateMode: '--require-passed-unity-gate' });
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
  for (const mutation of [g => { g.exitCode = 1; }, g => { g.ci.commit = 'b'.repeat(40); }, g => { g.ci.runId = '124'; }, g => { g.ci.runAttempt = '2'; }, g => { g.ci.executionAttempt = '2'; }, g => { g.ci.candidateSha256 = '0'.repeat(64); }, g => { delete g.ci; }]) {
    const f = use(t); f.gates(); mutateGate(f, '2021.3.19f1', mutation); assert.throws(() => aggregate(f.options), /exit code|CI gate|execution attempt/);
  }
});
test('failed and not-run gates can be recorded but never pass release gate', t => {
  for (const status of ['failed', 'not-run']) { const f = use(t); f.gates(status); aggregate({ ...f.options, gateMode: '--require-unity-gate' }); assert.throws(() => validateRelease({ ...f.options, gateMode: '--require-passed-unity-gate' }), /must pass/); }
});
test('strict XML rejects truncated, zero-test, failed and incomplete runs', t => {
  const f = use(t); const file = path.join(f.root, 'result.xml');
  for (const text of ['<test-run result="Passed">', '<test-run result="Passed" total="0" passed="0" failed="0" />', '<test-run result="Passed" total="1" passed="1" failed="0" />', '<test-run result="Failed" total="1" passed="0" failed="1"><test-case result="Failed" /></test-run>', '<test-run result="Passed" total="1" passed="1" failed="0"><test-case result="Inconclusive" /></test-run>', '<test-run result="Passed" total="2" passed="1" failed="0" skipped="1"><test-case result="Passed"/><test-case result="Skipped"/></test-run>', '<test-run result="Passed" total="2" passed="1" failed="0"><test-case result="Passed"/></test-run>']) { fs.writeFileSync(file, text); assert.throws(() => validateTestXml(file), /XML/); }
  fs.writeFileSync(file, '<test-run result="Passed" total="1" passed="1" failed="0" skipped="0" inconclusive="0"><test-case result="Passed"/></test-run>');
  validateTestXml(file);
});
test('version and DLL update allowlists preserve public package metadata', () => {
  const base = { name: 'jp.rhycol.openapicodegen', version: '0.5.0' }; const addon = { name: 'jp.rhycol.openapicodegen.source-generator', version: '0.5.0', dependencies: { [base.name]: '0.5.0' } };
  assert.equal(assertVersionContract(base, addon, '0.5.0'), '0.5.0'); assert.throws(() => assertVersionContract(base, { ...addon, version: '0.4.0' }), /mismatch/); assert.throws(() => assertVersionContract({ ...base, version: '0.5.0-rc1' }, addon), /stable/);
  validateUpdate('develop', [analyzer]); validateUpdate('main', []); assert.throws(() => validateUpdate('feature/anything', []), /base/); assert.throws(() => validateUpdate('develop', [`${analyzer}.meta`]), /only/);
});
test('trusted analyzer copy preserves metadata and rejects linked paths', t => {
  function targetFixture() {
    const root = fs.mkdtempSync(path.join(require('os').tmpdir(), 'analyzer-copy-'));
    const target = path.join(root, 'target');
    const destination = path.join(target, analyzer);
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    fs.writeFileSync(destination, 'old analyzer');
    fs.writeFileSync(`${destination}.meta`, 'stable-guid');
    execFileSync('git', ['init', '--initial-branch=main'], { cwd: target, stdio: 'ignore' });
    execFileSync('git', ['add', '.'], { cwd: target });
    execFileSync('git', ['-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-m', 'fixture'], { cwd: target, stdio: 'ignore' });
    const artifact = path.join(root, 'artifact'); fs.mkdirSync(artifact);
    fs.writeFileSync(path.join(artifact, path.basename(analyzer)), 'new analyzer');
    return { root, target, destination, artifact };
  }
  const valid = targetFixture(); t.after(() => fs.rmSync(valid.root, { recursive: true, force: true }));
  copyAnalyzerArtifact(valid.artifact, valid.target);
  assert.equal(fs.readFileSync(valid.destination, 'utf8'), 'new analyzer');
  assert.equal(fs.readFileSync(`${valid.destination}.meta`, 'utf8'), 'stable-guid');
  assert.deepEqual(execFileSync('git', ['diff', '--name-only'], { cwd: valid.target, encoding: 'utf8' }).trim().split('\n'), [analyzer]);

  const linked = targetFixture(); t.after(() => fs.rmSync(linked.root, { recursive: true, force: true }));
  fs.rmSync(linked.destination);
  fs.symlinkSync(path.join(linked.root, 'outside.dll'), linked.destination);
  assert.throws(() => copyAnalyzerArtifact(linked.artifact, linked.target), /symbolic link/);

  const linkedParent = targetFixture(); t.after(() => fs.rmSync(linkedParent.root, { recursive: true, force: true }));
  const analyzerDirectory = path.dirname(linkedParent.destination);
  const realAnalyzerDirectory = path.join(linkedParent.root, 'outside-analyzers');
  fs.renameSync(analyzerDirectory, realAnalyzerDirectory);
  fs.symlinkSync(realAnalyzerDirectory, analyzerDirectory);
  assert.throws(() => copyAnalyzerArtifact(linkedParent.artifact, linkedParent.target), /symbolic link/);

  const extra = targetFixture(); t.after(() => fs.rmSync(extra.root, { recursive: true, force: true }));
  fs.writeFileSync(path.join(extra.artifact, 'unexpected.txt'), 'unexpected');
  assert.throws(() => copyAnalyzerArtifact(extra.artifact, extra.target), /exactly one/);
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
