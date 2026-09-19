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
function workflowStep(name, stepName) {
  const text = workflow(name);
  const start = text.indexOf(`      - name: ${stepName}\n`); assert.ok(start >= 0, `${stepName} is missing`);
  const section = text.slice(start).split(/\n      - /)[0];
  const run = section.split('        run: |\n')[1]; assert.ok(run, `${stepName} has no shell body`);
  return run.split('\n').filter(line => line.startsWith('          ')).map(line => line.slice(10)).join('\n');
}
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
  assert.match(workflow('source-generator-verify.yml'), /environment:\n      name: UNITY_LICENSE/);
});
test('Unity approval links the exact commit and reruns keep immutable artifact identities', () => {
  const text = workflow('source-generator-verify.yml');
  assert.match(text, /environment:\n      name: UNITY_LICENSE\n      url: https:\/\/github\.com\/\$\{\{ github\.repository \}\}\/commit\/\$\{\{ needs\.candidate\.outputs\.commit \}\}/);
  assert.match(text, /SOURCE_GENERATOR_CANDIDATE_ATTEMPT: \$\{\{ needs\.candidate\.outputs\.attempt \}\}/);
  assert.match(text, /name: \$\{\{ needs\.unity\.outputs\.artifact \}\}/);
  assert.match(text, /verified-artifact=verified-release-%s-%s/);
  assert.doesNotMatch(text, /name: unity-evidence-\$\{\{ github\.run_id \}\}-\$\{\{ github\.run_attempt \}\}/);
  assert.doesNotMatch(text, /if: always\(\)\n        uses: actions\/upload-artifact@v4\n        with:\n          name: \$\{\{ steps\.metadata\.outputs\.verified-artifact \}\}/);
});
test('write-capable manual workflows fail unsupported refs in read-only preflight jobs', () => {
  const dll = workflow('source-generator-update-dll.yml');
  const openupm = workflow('source-generator-openupm.yml');
  assert.match(dll, /build:\n    name:[\s\S]*?needs: preflight/);
  assert.match(openupm, /publish:\n    needs: preflight/);
  const run = (script, env) => spawnSync('bash', ['-e', '-o', 'pipefail', '-c', script], { cwd: repository, env: { ...process.env, ...env }, encoding: 'utf8' }).status;
  const dllPreflight = workflowStep('source-generator-update-dll.yml', 'Reject unsupported workflow ref before write-capable jobs');
  assert.equal(run(dllPreflight, { GITHUB_REF: 'refs/heads/main', BASE_BRANCH: 'develop' }), 0);
  assert.notEqual(run(dllPreflight, { GITHUB_REF: 'refs/heads/feature', BASE_BRANCH: 'develop' }), 0);
  const openupmPreflight = workflowStep('source-generator-openupm.yml', 'Reject unsupported workflow ref before OIDC is available');
  assert.equal(run(openupmPreflight, { GITHUB_REF_TYPE: 'tag', GITHUB_REF: 'refs/tags/0.5.0' }), 0);
  assert.notEqual(run(openupmPreflight, { GITHUB_REF_TYPE: 'branch', GITHUB_REF: 'refs/heads/main' }), 0);
});
test('published spec validation runs in the parent shell so gate failures are retained', () => {
  const script = fs.readFileSync(path.join(repository, 'SourceGenerators/scripts/verify-unity.sh'), 'utf8');
  assert.doesNotMatch(script, /_spec_id="\$\(get_published_spec_id\)"/);
  for (const name of ['initial', 'unchanged', 'regenerated']) assert.match(script, new RegExp(`get_published_spec_id\\n${name}_spec_id="\\$\\{published_spec_id\\}"`));
});
test('missing published mirror or cache records the production failure reason in gate evidence', t => {
  const production = fs.readFileSync(path.join(repository, 'SourceGenerators/scripts/verify-unity.sh'), 'utf8');
  function between(start, end) {
    const first = production.indexOf(start); const last = production.indexOf(end, first);
    assert.ok(first >= 0 && last > first, `cannot extract ${start}`);
    return production.slice(first, last);
  }
  const failGate = between('fail_gate() {\n', 'copy_evidence_file() {\n');
  const publishedSpec = between('get_published_spec_id() {\n', 'assert_test_passed() {\n');
  const call = production.match(/get_published_spec_id\ninitial_spec_id="\$\{published_spec_id\}"/);
  assert.ok(call, 'production parent-shell invocation is missing');

  for (const scenario of ['missing-mirror', 'missing-cache']) {
    const f = fixture(); t.after(f.remove);
    const project = path.join(f.root, scenario); const mirror = path.join(project, 'mirror'); const cache = path.join(project, 'cache');
    const generated = path.join(project, 'Api.OpenApiDefinition.cs'); const raw = path.join(project, 'openapi.json');
    const evidence = path.join(f.output, 'unity-gate/6000.3.2f1');
    fs.mkdirSync(mirror, { recursive: true }); fs.mkdirSync(cache, { recursive: true }); fs.mkdirSync(evidence, { recursive: true });
    fs.writeFileSync(generated, 'partial class Api {}'); fs.writeFileSync(raw, '{}');
    if (scenario === 'missing-cache') fs.writeFileSync(path.join(mirror, `${'a'.repeat(32)}.Rhycol.OpenApiCodeGen.SourceGenerator.additionalfile`), '{}');
    const harness = [
      'set -euo pipefail',
      'gate_status="not-run"',
      'gate_reason="Source Generator Unity gate did not start"',
      'published_authoritative_cache=""',
      'generated_definition="$GENERATED_DEFINITION"',
      'compiler_mirror_directory="$COMPILER_MIRROR_DIRECTORY"',
      'analyzer_assembly_name="Rhycol.OpenApiCodeGen.SourceGenerator"',
      'authoritative_cache_root="$AUTHORITATIVE_CACHE_ROOT"',
      'raw_spec="$RAW_SPEC"',
      failGate,
      publishedSpec,
      'write_gate_evidence() {',
      '  local exit_code="$1"',
      '  node "$CI_EVIDENCE_SCRIPT" write-gate "$EVIDENCE_ROOT/gate.json" "$gate_status" "$exit_code" "6000.3.2f1" "$gate_reason" "$EVIDENCE_ROOT" "$RELEASE_OUTPUT"',
      '}',
      'cleanup() {',
      '  local exit_code=$?',
      '  trap - EXIT',
      '  write_gate_evidence "$exit_code"',
      '  exit "$exit_code"',
      '}',
      'trap cleanup EXIT',
      call[0]
    ].join('\n');
    const result = spawnSync('bash', ['-c', harness], { cwd: repository, encoding: 'utf8', env: {
      ...process.env,
      GENERATED_DEFINITION: generated,
      COMPILER_MIRROR_DIRECTORY: mirror,
      AUTHORITATIVE_CACHE_ROOT: cache,
      RAW_SPEC: raw,
      CI_EVIDENCE_SCRIPT: path.join(repository, 'SourceGenerators/scripts/ci-evidence.js'),
      EVIDENCE_ROOT: evidence,
      RELEASE_OUTPUT: f.output,
      SOURCE_GENERATOR_CI: '1',
      SOURCE_GENERATOR_CI_COMMIT: commit,
      SOURCE_GENERATOR_CANDIDATE_ATTEMPT: '1',
      GITHUB_REPOSITORY: 'owner/repo',
      GITHUB_RUN_ID: '123',
      GITHUB_RUN_ATTEMPT: '1'
    } });
    assert.equal(result.status, 1, result.stderr);
    const gate = JSON.parse(fs.readFileSync(path.join(evidence, 'gate.json')));
    assert.equal(gate.status, 'failed'); assert.equal(gate.exitCode, 1);
    assert.match(gate.reason, scenario === 'missing-mirror' ? /expected exactly one compiler mirror, found 0/ : /authoritative normalized-v2 cache is missing/);
  }
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
