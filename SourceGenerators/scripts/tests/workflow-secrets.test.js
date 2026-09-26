'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');
const { fixture, commit } = require('./release-fixture');
const { expectedGateVersions, validateGateSummary } = require('../validate-release-artifacts');
const repository = path.resolve(__dirname, '../../..');
const shortTerminationBudget = {
  SOURCE_GENERATOR_CI_GATE_STOP_TIMEOUT_SECONDS: '1',
  SOURCE_GENERATOR_CI_EVIDENCE_TIMEOUT_SECONDS: '1',
  SOURCE_GENERATOR_CI_LICENSE_RETURN_TIMEOUT_SECONDS: '1',
  SOURCE_GENERATOR_CI_HOST_TERMINATION_MARGIN_SECONDS: '2'
};
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
  const command = section.match(/\n        run: ([^\n]+)$/m); assert.ok(command);
  return command[1];
}
function runHost(t, missing, options = {}) {
  const f = fixture(); t.after(f.remove);
  const bin = path.join(f.root, 'bin'); fs.mkdirSync(bin);
  fs.writeFileSync(path.join(bin, 'docker'), options.docker || '#!/usr/bin/env bash\nprintf "%s\\n" "$1" >> "$DOCKER_MARKER"\nif [[ "$1" == build ]]; then exit 1; fi\n', { mode: 0o755 });
  fs.writeFileSync(path.join(bin, 'sudo'), '#!/usr/bin/env bash\nexec "$@"\n', { mode: 0o755 });
  const secrets = { UNITY_LICENSE: 'fixture-license', UNITY_EMAIL: 'fixture-email@example.invalid', UNITY_PASSWORD: 'fixture-password' };
  const env = { ...process.env, ...secrets, PATH: `${bin}${path.delimiter}${process.env.PATH}`, SOURCE_GENERATOR_CI: '1', SOURCE_GENERATOR_CI_COMMIT: commit, SOURCE_GENERATOR_RELEASE_OUTPUT: f.output, SOURCE_GENERATOR_RELEASE_VERSION: '0.5.0', SOURCE_GENERATOR_CI_STARTED_AT_EPOCH: String(Math.floor(Date.now() / 1000)), GITHUB_REPOSITORY: 'owner/repo', GITHUB_RUN_ID: '123', GITHUB_RUN_ATTEMPT: '1', GITHUB_WORKSPACE: repository, DOCKER_MARKER: path.join(f.root, 'docker-called'), ...options.env };
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
test('manual CI gates both explicit SHA and selected ref on main ancestry before reusable verification', t => {
  const root = fs.mkdtempSync(path.join(require('os').tmpdir(), 'manual-ci-boundary-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const git = args => spawnSync('git', args, { cwd: root, encoding: 'utf8' });
  function checkedGit(args) {
    const result = git(args);
    assert.equal(result.status, 0, result.stderr);
    return result.stdout.trim();
  }
  checkedGit(['init', '--initial-branch=main']);
  fs.writeFileSync(path.join(root, 'file'), 'first');
  checkedGit(['add', '.']);
  checkedGit(['-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-m', 'first']);
  const ancestor = checkedGit(['rev-parse', 'HEAD']);
  fs.writeFileSync(path.join(root, 'file'), 'second');
  checkedGit(['add', '.']);
  checkedGit(['-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-m', 'second']);
  const main = checkedGit(['rev-parse', 'HEAD']);
  checkedGit(['update-ref', 'refs/remotes/origin/main', main]);
  checkedGit(['switch', '--orphan', 'untrusted']);
  fs.writeFileSync(path.join(root, 'file'), 'untrusted');
  checkedGit(['add', '.']);
  checkedGit(['-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-m', 'untrusted']);
  const untrusted = checkedGit(['rev-parse', 'HEAD']);

  const ci = workflow('source-generator-ci.yml');
  const preflight = ci.split('\n  preflight:\n')[1].split('\n  verify:\n')[0];
  const verify = ci.split('\n  verify:\n')[1];
  assert.match(preflight, /Validate manual target SHA before checkout/);
  assert.match(preflight, /ref: \$\{\{ inputs\.commit \|\| github\.sha \}\}/);
  assert.equal((preflight.match(/if: github\.event_name == 'workflow_dispatch'/g) || []).length, 3);
  assert.match(verify, /^    needs: preflight\n/);
  assert.match(verify, /commit: \$\{\{ github\.event\.pull_request\.head\.sha \|\| inputs\.commit \|\| github\.sha \}\}/);

  const validate = workflowStep('source-generator-ci.yml', 'Validate manual target SHA before checkout');
  const boundary = workflowStep('source-generator-ci.yml', 'Enforce main ancestry before manual Unity verification');
  const run = (script, target, ref = 'refs/heads/main') => spawnSync('bash', ['-e', '-o', 'pipefail', '-c', script], {
    cwd: root, encoding: 'utf8', env: { ...process.env, TARGET_COMMIT: target, GITHUB_REF: ref }
  }).status;
  assert.equal(run(validate, main), 0);
  assert.notEqual(run(validate, 'main'), 0, 'symbolic refs must fail before checkout');
  assert.notEqual(run(validate, main, 'refs/heads/feature'), 0, 'non-main dispatch refs must fail before checkout');
  assert.notEqual(run(boundary, untrusted), 0, 'untrusted checkout is rejected');
  checkedGit(['switch', 'main']);
  assert.equal(run(boundary, main), 0, 'selected main ref is accepted');
  checkedGit(['switch', '--detach', ancestor]);
  assert.equal(run(boundary, ancestor), 0, 'an older main ancestor is accepted');
  assert.notEqual(run(boundary, main), 0, 'selected SHA must match the checkout');
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
test('Unity workflow and scripts consume one bounded time-budget contract', () => {
  const workflowText = workflow('source-generator-verify.yml');
  const budget = fs.readFileSync(path.join(repository, 'SourceGenerators/scripts/ci-time-budget.sh'), 'utf8');
  const host = fs.readFileSync(path.join(repository, 'SourceGenerators/scripts/run-ci-unity-matrix.sh'), 'utf8');
  const entrypoint = fs.readFileSync(path.join(repository, 'SourceGenerators/scripts/run-ci-unity.sh'), 'utf8');
  assert.match(workflowText, /timeout-minutes: 240/);
  assert.match(workflowText, /Start the bounded Unity work deadline/);
  assert.match(workflowText, /run-with-timeout\.pl "\$CI_CLEANUP_TIMEOUT_SECONDS"/);
  for (const [name, value] of [['CI_TOTAL_BUDGET_SECONDS', '12600'], ['CI_EDITOR_TIMEOUT_SECONDS', '300'], ['CI_DOCKER_BUILD_TIMEOUT_SECONDS', '600'], ['CI_ACTIVATION_TIMEOUT_SECONDS', '300'], ['CI_LICENSE_RETURN_TIMEOUT_SECONDS', '120'], ['CI_CLEANUP_TIMEOUT_SECONDS', '120'], ['CI_GATE_STOP_TIMEOUT_SECONDS', '30'], ['CI_EVIDENCE_TIMEOUT_SECONDS', '30'], ['CI_HOST_TERMINATION_MARGIN_SECONDS', '30'], ['CI_MAX_CONTAINER_STARTS', '3']]) assert.match(budget, new RegExp(`${name}=.*${value}`));
  assert.match(budget, /CI_CONTAINER_TERMINATION_GRACE_SECONDS=\$\(\(/);
  assert.match(host, /source .*ci-time-budget\.sh/); assert.match(entrypoint, /source .*ci-time-budget\.sh/);
  assert.match(entrypoint, /CI_ACTIVATION_TIMEOUT_SECONDS/); assert.match(entrypoint, /CI_LICENSE_RETURN_TIMEOUT_SECONDS/); assert.match(entrypoint, /CI_EDITOR_TIMEOUT_SECONDS/);
  assert.doesNotMatch(`${workflowText}\n${host}\n${entrypoint}`, /alarm shift; exec @ARGV|\btimeout "?\$\{?CI_/);
  for (const script of [entrypoint, fs.readFileSync(path.join(repository, 'SourceGenerators/scripts/verify-base-unity.sh'), 'utf8'), fs.readFileSync(path.join(repository, 'SourceGenerators/scripts/verify-unity.sh'), 'utf8')]) {
    assert.match(script, /run-with-timeout\.pl/);
  }
});
test('process-group timeout kills signal-ignoring commands and their children', t => {
  const root = fs.mkdtempSync(path.join(require('os').tmpdir(), 'process-group-timeout-')); t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const marker = path.join(root, 'pids'); const command = path.join(root, 'ignore-signals.sh');
  fs.writeFileSync(command, `#!/usr/bin/env bash
trap '' TERM ALRM
printf '%s\\n' "$$" > "$PID_MARKER"
sleep 300 &
printf '%s\\n' "$!" >> "$PID_MARKER"
wait
`, { mode: 0o755 });
  const started = Date.now();
  const result = spawnSync('perl', [path.join(repository, 'SourceGenerators/scripts/run-with-timeout.pl'), '1', command], { env: { ...process.env, PID_MARKER: marker }, encoding: 'utf8' });
  assert.equal(result.status, 124, result.stderr); assert.ok(Date.now() - started < 3000, 'individual command timeout must remain bounded');
  const pids = fs.readFileSync(marker, 'utf8').trim().split('\n').map(Number); assert.equal(pids.length, 2);
  for (const pid of pids) assert.throws(() => process.kill(pid, 0), error => error.code === 'ESRCH');
});
test('Unity consumer phases install Test Framework before testables and reject package test DLLs', () => {
  const base = fs.readFileSync(path.join(repository, 'SourceGenerators/scripts/verify-base-unity.sh'), 'utf8');
  const baseWithoutFramework = base.indexOf('without the Unity Test Framework');
  const baseDependency = base.indexOf('com.unity.test-framework');
  const baseFrameworkConsumer = base.indexOf('Also compile with Test Framework installed but without testables');
  const baseTestables = base.indexOf('"testables"');
  assert.ok(baseWithoutFramework >= 0 && baseDependency > baseWithoutFramework && baseFrameworkConsumer > baseDependency && baseTestables > baseFrameworkConsumer,
    'base gate must preserve no-Framework compilation before the Framework-without-testables phase');
  assert.match(base, /base-test-framework-consumer\.log/); assert.match(base, /unexpectedly compiled .* without testables/);

  const addon = fs.readFileSync(path.join(repository, 'SourceGenerators/scripts/verify-unity.sh'), 'utf8');
  const addonDependency = addon.indexOf('com.unity.test-framework');
  const addonConsumer = addon.indexOf('consumer compilation');
  const addonTestables = addon.indexOf('"testables"');
  assert.ok(addonDependency >= 0 && addonConsumer > addonDependency && addonTestables > addonConsumer,
    'Unity 6 gates must compile with Test Framework before adding testables');
  assert.match(addon, /unexpectedly compiled .* without testables/);
});
test('Unity gates launch outside projectPath and require the cwd persistence regression test', () => {
  for (const name of ['verify-base-unity.sh', 'verify-unity.sh']) {
    const script = fs.readFileSync(path.join(repository, 'SourceGenerators/scripts', name), 'utf8');
    assert.match(script, /launch_directory=.*Unity Launch 日本語/);
    assert.match(script, /cd "\$\{launch_directory\}"/);
    assert.match(script, /export SOURCE_GENERATOR_VERIFY_LAUNCH_DIRECTORY="\$\{launch_directory\}"/);
    assert.match(script, /-projectPath "\$\{project_path\}"/);
    assert.match(script, /ProjectSettingsPersistenceUsesUnityAssetsDirectoryFromExternalWorkingDirectory/);
  }
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
      assert.match(result.stdout, new RegExp(`Unity gate ${version.replaceAll('.', '\\.')}\\: not-run \\(exit 2\\)`));
    }
  }
});
test('nonempty Secrets proceed to the image build path without revealing values', t => {
  const { f, result, marker } = runHost(t, []);
  assert.equal(result.status, 1, result.stderr); // The fake Docker build deliberately fails.
  assert.equal(fs.readFileSync(marker, 'utf8').split('\n').filter(line => line === 'build').length, 3);
  for (const version of expectedGateVersions) {
    const summary = validateGateSummary(f.output, version, path.join(f.output, 'unity-gate', version, 'gate.json'), undefined, f.manifest);
    assert.equal(summary.status, 'failed'); assert.ok(summary.files.some(file => file.path.endsWith('/ci-host.log')));
  }
});
test('a nonzero container exit cannot preserve a stale passed gate', t => {
  const docker = `#!/usr/bin/env bash
printf '%s\\n' "$*" >> "$DOCKER_MARKER"
case "$1" in
  build) exit 0 ;;
  run)
    version="\${!#}"
    evidence="$SOURCE_GENERATOR_RELEASE_OUTPUT/unity-gate/$version"
    mkdir -p "$evidence"
    node SourceGenerators/scripts/ci-evidence.js write-gate \
      "$evidence/gate.json" passed 0 "$version" stale-pass "$evidence" "$SOURCE_GENERATOR_RELEASE_OUTPUT"
    exit 9
    ;;
  kill|rm|image|builder) exit 0 ;;
esac
`;
  const { f, result } = runHost(t, [], { docker });
  assert.equal(result.status, 1, result.stderr);
  for (const version of expectedGateVersions) {
    const gate = JSON.parse(fs.readFileSync(path.join(f.output, 'unity-gate', version, 'gate.json')));
    assert.equal(gate.status, 'failed'); assert.match(gate.reason, /container exited 9/);
  }
});
test('container failure reason is preserved, redacted, and safely reported to console and summary', t => {
  const summary = path.join(require('os').tmpdir(), `unity-gate-summary-${process.pid}-${Date.now()}.md`);
  t.after(() => fs.rmSync(summary, { force: true }));
  const docker = `#!/usr/bin/env bash
printf '%s\\n' "$*" >> "$DOCKER_MARKER"
case "$1" in
  build) exit 0 ;;
  run)
    version="\${!#}"
    evidence="$SOURCE_GENERATOR_RELEASE_OUTPUT/unity-gate/$version"
    mkdir -p "$evidence"
    reason="container reason $UNITY_PASSWORD%25
::error::forged command|table"
    node SourceGenerators/scripts/ci-evidence.js write-gate \
      "$evidence/gate.json" failed 7 "$version" "$reason" "$evidence" "$SOURCE_GENERATOR_RELEASE_OUTPUT"
    exit 7
    ;;
  rm|image|builder) exit 0 ;;
esac
`;
  const { f, result } = runHost(t, [], { docker, env: { GITHUB_STEP_SUMMARY: summary } });
  assert.equal(result.status, 1, result.stderr);
  assert.doesNotMatch(result.stdout + result.stderr, /fixture-password/);
  assert.doesNotMatch(result.stdout + result.stderr, /\n::error::forged command/);
  assert.match(result.stdout, /Unity gate 2021\.3\.19f1: failed \(exit 7\): container reason \[REDACTED\]/);
  assert.match(result.stdout, /%2525/);
  const first = JSON.parse(fs.readFileSync(path.join(f.output, 'unity-gate/2021.3.19f1/gate.json')));
  assert.equal(first.exitCode, 7); assert.match(first.reason, /container reason \[REDACTED\]/);
  const summaryText = fs.readFileSync(summary, 'utf8');
  assert.match(summaryText, /## Unity gate results/);
  assert.match(summaryText, /container reason \[REDACTED\]/);
  assert.match(summaryText, /\\\|table/);
  assert.doesNotMatch(summaryText, /fixture-password/);
});
test('gate reporter neutralizes version commands and summary HTML markup', t => {
  const root = fs.mkdtempSync(path.join(require('os').tmpdir(), 'gate-report-injection-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const gate = path.join(root, 'gate.json');
  const summary = path.join(root, 'summary.md');
  fs.writeFileSync(gate, JSON.stringify({
    status: 'failed',
    exitCode: 1,
    version: '6000.0\n::error::forged-version ##[add-mask]FORGED_VERSION',
    reason: 'unsafe <img src=x onerror=alert(1)> | row\n::warning::forged-reason ##[add-mask]FORGED_REASON %'
  }));

  const result = spawnSync(process.execPath, [
    path.join(repository, 'SourceGenerators/scripts/ci-evidence.js'),
    'report-gate', gate, summary
  ], { encoding: 'utf8' });
  assert.equal(result.status, 0, result.stderr);
  assert.equal(result.stdout.trim().split('\n').length, 1);
  assert.match(result.stdout, /^::error title=/);
  assert.doesNotMatch(result.stdout, /\n::error::forged-version/);
  assert.doesNotMatch(result.stdout, /\n::warning::forged-reason/);
  assert.match(result.stdout, /Unity gate 6000\.0 ::error::forged-version ##\[add-mask\]FORGED_VERSION: failed/);
  assert.match(result.stdout, /##\[add-mask\]FORGED_VERSION/);
  assert.match(result.stdout, /##\[add-mask\]FORGED_REASON/);
  assert.match(result.stdout, /%25$/m);
  const summaryText = fs.readFileSync(summary, 'utf8');
  assert.doesNotMatch(summaryText, /<img/);
  assert.match(summaryText, /&lt;img src=x onerror=alert\(1\)&gt;/);
  assert.match(summaryText, /\\\| row/);
});
test('expired work deadline starts no containers and records all gates as not-run', t => {
  const started = Math.floor(Date.now() / 1000) - 5;
  const { f, result, marker } = runHost(t, [], { env: { SOURCE_GENERATOR_CI_TOTAL_BUDGET_SECONDS: '1', SOURCE_GENERATOR_CI_STARTED_AT_EPOCH: String(started) } });
  assert.equal(result.status, 2, result.stderr); assert.equal(fs.existsSync(marker), false);
  for (const version of expectedGateVersions) {
    const gate = JSON.parse(fs.readFileSync(path.join(f.output, 'unity-gate', version, 'gate.json')));
    assert.equal(gate.status, 'not-run'); assert.match(gate.reason, /deadline.*not started/);
  }
});
test('hanging container is terminated TERM then KILL with redacted partial evidence', t => {
  const docker = `#!/usr/bin/env bash
printf '%s\\n' "$*" >> "$DOCKER_MARKER"
case "$1" in
  build) exit 0 ;;
  run) printf '%s\\n' "$UNITY_PASSWORD"; trap '' TERM; while true; do sleep 1; done ;;
  kill|rm|image|builder) exit 0 ;;
esac
`;
  const started = Date.now();
  const { f, result, marker } = runHost(t, [], { docker, env: { SOURCE_GENERATOR_CI_TOTAL_BUDGET_SECONDS: '2', SOURCE_GENERATOR_CI_CLEANUP_TIMEOUT_SECONDS: '3', ...shortTerminationBudget } });
  assert.equal(result.status, 1, result.stderr); assert.ok(Date.now() - started < 15000, 'deadline test must remain bounded');
  const commands = fs.readFileSync(marker, 'utf8'); assert.match(commands, /kill --signal TERM/); assert.match(commands, /kill --signal KILL/);
  const current = JSON.parse(fs.readFileSync(path.join(f.output, 'unity-gate/2021.3.19f1/gate.json')));
  assert.equal(current.status, 'failed'); assert.match(current.reason, /deadline.*terminated/);
  const hostLog = fs.readFileSync(path.join(f.output, 'unity-gate/2021.3.19f1/ci-host.log'), 'utf8');
  assert.doesNotMatch(hostLog, /fixture-password/); assert.match(hostLog, /\[REDACTED\]/); assert.match(hostLog, /sending TERM/); assert.match(hostLog, /sending KILL/);
  for (const version of expectedGateVersions.slice(1)) assert.equal(JSON.parse(fs.readFileSync(path.join(f.output, 'unity-gate', version, 'gate.json'))).status, 'not-run');
});
test('hanging Docker kill commands stay within cleanup budget and leave no helper processes', t => {
  const processRoot = fs.mkdtempSync(path.join(require('os').tmpdir(), 'docker-kill-timeout-')); t.after(() => fs.rmSync(processRoot, { recursive: true, force: true }));
  const childMarker = path.join(processRoot, 'children');
  const docker = `#!/usr/bin/env bash
printf '%s\\n' "$*" >> "$DOCKER_MARKER"
case "$1" in
  build) exit 0 ;;
  run) trap '' TERM; while true; do sleep 1; done ;;
  kill)
    sleep 300 &
    printf '%s\\n' "$!" >> "$DOCKER_CHILD_MARKER"
    wait
    ;;
  rm|image|builder) exit 0 ;;
esac
`;
  const started = Date.now();
  const { f, result, marker } = runHost(t, [], { docker, env: { SOURCE_GENERATOR_CI_TOTAL_BUDGET_SECONDS: '2', SOURCE_GENERATOR_CI_CLEANUP_TIMEOUT_SECONDS: '3', DOCKER_CHILD_MARKER: childMarker, ...shortTerminationBudget } });
  assert.equal(result.status, 1, result.stderr); assert.ok(Date.now() - started < 15000, 'Docker control timeout must remain bounded');
  const commands = fs.readFileSync(marker, 'utf8'); assert.match(commands, /kill --signal TERM/); assert.match(commands, /kill --signal KILL/);
  const children = fs.readFileSync(childMarker, 'utf8').trim().split('\n').map(Number); assert.equal(children.length, 2);
  for (const pid of children) {
    let alive = true;
    for (let attempt = 0; attempt < 20 && alive; attempt += 1) {
      try { process.kill(pid, 0); spawnSync('sleep', ['0.05']); } catch (error) { if (error.code === 'ESRCH') alive = false; else throw error; }
    }
    assert.equal(alive, false, `timed-out Docker helper ${pid} must be reaped`);
  }
  const current = JSON.parse(fs.readFileSync(path.join(f.output, 'unity-gate/2021.3.19f1/gate.json')));
  assert.equal(current.status, 'failed'); assert.match(current.reason, /deadline.*terminated/);
});
test('nonzero Docker TERM acknowledgment still allows near-bound license return to finish', t => {
  const processRoot = fs.mkdtempSync(path.join(require('os').tmpdir(), 'docker-return-grace-'));
  t.after(() => fs.rmSync(processRoot, { recursive: true, force: true }));
  const runPid = path.join(processRoot, 'run-pid');
  const returnMarker = path.join(processRoot, 'return-marker');
  const docker = `#!/usr/bin/env bash
printf '%s\\n' "$*" >> "$DOCKER_MARKER"
case "$1" in
  build) exit 0 ;;
  run)
    printf '%s\\n' "$$" > "$DOCKER_RUN_PID"
    trap 'printf "return-started\\n" >> "$DOCKER_RETURN_MARKER"; sleep 2; printf "return-complete\\n" >> "$DOCKER_RETURN_MARKER"; exit 143' TERM
    while true; do sleep 1; done
    ;;
  kill)
    if [[ "$3" == TERM ]]; then kill -TERM "$(cat "$DOCKER_RUN_PID")"; exit 9; fi
    exit 0
    ;;
  rm|image|builder) exit 0 ;;
esac
`;
  const started = Date.now();
  const { f, result, marker } = runHost(t, [], { docker, env: {
    SOURCE_GENERATOR_CI_TOTAL_BUDGET_SECONDS: '2',
    SOURCE_GENERATOR_CI_CLEANUP_TIMEOUT_SECONDS: '1',
    SOURCE_GENERATOR_CI_GATE_STOP_TIMEOUT_SECONDS: '1',
    SOURCE_GENERATOR_CI_EVIDENCE_TIMEOUT_SECONDS: '1',
    SOURCE_GENERATOR_CI_LICENSE_RETURN_TIMEOUT_SECONDS: '3',
    SOURCE_GENERATOR_CI_HOST_TERMINATION_MARGIN_SECONDS: '2',
    DOCKER_RUN_PID: runPid,
    DOCKER_RETURN_MARKER: returnMarker
  } });
  assert.equal(result.status, 1, result.stderr);
  assert.ok(Date.now() - started < 15000, 'near-bound license return must remain bounded');
  assert.equal(fs.readFileSync(returnMarker, 'utf8'), 'return-started\nreturn-complete\n');
  const commands = fs.readFileSync(marker, 'utf8');
  assert.match(commands, /kill --signal TERM/);
  assert.doesNotMatch(commands, /kill --signal KILL/);
  const hostLog = fs.readFileSync(path.join(
    f.output, 'unity-gate/2021.3.19f1/ci-host.log'), 'utf8');
  assert.match(hostLog, /Docker TERM request exited 9; waiting for the container/);
});
test('hanging Docker build stops at its short production timeout and preserves evidence', t => {
  const docker = `#!/usr/bin/env bash
printf '%s\\n' "$*" >> "$DOCKER_MARKER"
if [[ "$1" == build ]]; then while true; do sleep 1; done; fi
exit 0
`;
  const started = Date.now();
  const { f, result } = runHost(t, [], { docker, env: { SOURCE_GENERATOR_CI_TOTAL_BUDGET_SECONDS: '4', SOURCE_GENERATOR_CI_DOCKER_BUILD_TIMEOUT_SECONDS: '1', SOURCE_GENERATOR_CI_CLEANUP_TIMEOUT_SECONDS: '1' } });
  assert.equal(result.status, 1, result.stderr); assert.ok(Date.now() - started < 10000, 'build timeout test must remain bounded');
  const first = JSON.parse(fs.readFileSync(path.join(f.output, 'unity-gate/2021.3.19f1/gate.json')));
  assert.equal(first.status, 'failed'); assert.match(first.reason, /image build failed or timed out after 1 seconds/);
  assert.ok(first.files.some(file => file.path.endsWith('/ci-host.log')));
});
