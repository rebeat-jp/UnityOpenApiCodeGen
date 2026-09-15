'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const { fixture, commit } = require('./release-fixture');
const { expectedGateVersions, validateGateSummary } = require('../validate-release-artifacts');
const repository = path.resolve(__dirname, '../../..');
function workflow(name) { return fs.readFileSync(path.join(repository, '.github/workflows', name), 'utf8'); }
function unityRun() {
  const text = workflow('source-generator-verify.yml');
  const start = text.indexOf('      - name: Run all three editors serially, retaining failures\n');
  assert.ok(start >= 0);
  const section = text.slice(start).split(/\n      - /)[0];
  return section.split('        run: |\n')[1].split('\n').filter(line => line.startsWith('          ')).map(line => line.slice(10)).join('\n').replaceAll('${{ needs.candidate.outputs.version }}', '0.5.0');
}
function runHost(t, missing) {
  const f = fixture(); t.after(f.remove);
  const bin = path.join(f.root, 'bin'); fs.mkdirSync(bin);
  fs.writeFileSync(path.join(bin, 'docker'), '#!/usr/bin/env bash\nprintf "%s\\n" "$1" >> "$DOCKER_MARKER"\nif [[ "$1" == build ]]; then exit 1; fi\n', { mode: 0o755 });
  fs.writeFileSync(path.join(bin, 'sudo'), '#!/usr/bin/env bash\nexec "$@"\n', { mode: 0o755 });
  const secrets = { UNITY_LICENSE: 'fixture-license', UNITY_EMAIL: 'fixture-email@example.invalid', UNITY_PASSWORD: 'fixture-password' };
  const env = { ...process.env, ...secrets, PATH: `${bin}${path.delimiter}${process.env.PATH}`, SOURCE_GENERATOR_CI: '1', SOURCE_GENERATOR_CI_COMMIT: commit, SOURCE_GENERATOR_RELEASE_OUTPUT: f.output, GITHUB_REPOSITORY: 'owner/repo', GITHUB_RUN_ID: '123', GITHUB_RUN_ATTEMPT: '1', GITHUB_WORKSPACE: repository, DOCKER_MARKER: path.join(f.root, 'docker-called') };
  for (const name of missing) env[name] = '';
  const result = spawnSync('bash', ['-e', '-o', 'pipefail', '-c', unityRun()], { cwd: repository, env, encoding: 'utf8' });
  for (const value of Object.values(secrets)) assert.ok(!(result.stdout + result.stderr).includes(value), 'host diagnostics must not expose Secret values');
  return { f, result, marker: env.DOCKER_MARKER };
}
test('CI and release callers inherit Secrets while fork Unity gates remain disabled', () => {
  for (const name of ['source-generator-ci.yml', 'source-generator-release.yml']) {
    const text = workflow(name);
    const verify = text.split('\n  verify:\n')[1].split(/\n  [a-z-]+:\n/)[0];
    assert.match(verify, /uses: \.\/\.github\/workflows\/source-generator-verify\.yml/);
    assert.match(verify, /\n    secrets: inherit\n/);
  }
  assert.match(workflow('source-generator-ci.yml'), /unity: .*github\.event\.pull_request\.head\.repo\.full_name == github\.repository/);
  assert.match(workflow('source-generator-verify.yml'), /if: inputs\.unity && .*github\.event\.pull_request\.head\.repo\.full_name == github\.repository/);
  assert.match(workflow('source-generator-verify.yml'), /environment: UNITY_LICENSE/);
});
test('host fails before any Docker call for missing Secrets, preserving all three not-run summaries', t => {
  for (const missing of [['UNITY_LICENSE', 'UNITY_EMAIL', 'UNITY_PASSWORD'], ['UNITY_LICENSE'], ['UNITY_EMAIL'], ['UNITY_PASSWORD']]) {
    const { f, result, marker } = runHost(t, missing);
    assert.equal(result.status, 2, result.stderr);
    assert.equal(fs.existsSync(marker), false, 'no Unity image should be pulled or built');
    for (const version of expectedGateVersions) {
      const file = path.join(f.output, 'unity-gate', version, 'gate.json');
      const summary = validateGateSummary(f.output, version, file, undefined, f.manifest);
      assert.equal(summary.status, 'not-run');
      const json = JSON.parse(fs.readFileSync(file)); assert.equal(json.exitCode, 2); assert.match(json.reason, /Secrets are unavailable/);
    }
  }
});
test('nonempty Secrets proceed to the image build path without revealing values', t => {
  const { f, result, marker } = runHost(t, []);
  assert.equal(result.status, 1, result.stderr); // The fake Docker build deliberately fails.
  assert.equal(fs.readFileSync(marker, 'utf8').split('\n').filter(line => line === 'build').length, 3);
  for (const version of expectedGateVersions) assert.equal(validateGateSummary(f.output, version, path.join(f.output, 'unity-gate', version, 'gate.json'), undefined, f.manifest).status, 'not-run');
});
