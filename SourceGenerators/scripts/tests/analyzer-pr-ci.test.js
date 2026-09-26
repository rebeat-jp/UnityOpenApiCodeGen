'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const os = require('os');
const { spawnSync } = require('child_process');
const { validate } = require('../validate-analyzer-pr-ci');
const { analyzer } = require('../update-analyzer-pr');
const root = path.resolve(__dirname, '../../..');
const sha = c => c.repeat(40);
function fixture() {
  const repository = 'owner/repo', target = sha('a'), parent = sha('b'), runHead = sha('c');
  const branch = 'automation/source-generator-dll-develop-123-2';
  return {
    repository, number: '42', target,
    pr: { number: 42, state: 'open', draft: false, merged_at: null,
      user: { login: 'github-actions[bot]' }, created_at: '2026-01-02T00:00:00Z',
      head: { repo: { full_name: repository }, ref: branch, sha: target },
      base: { repo: { full_name: repository }, ref: 'develop' } },
    run: { id: 123, run_attempt: 2, event: 'workflow_dispatch', head_branch: 'main',
      path: '.github/workflows/source-generator-update-dll.yml',
      repository: { full_name: repository }, head_repository: { full_name: repository },
      head_sha: runHead, created_at: '2026-01-01T00:00:00Z' },
    provenance: { repository, pr: 42, head: target, parent, base: 'develop', branch, runId: 123, attempt: 2 },
    commit: { sha: target, parents: [{ sha: parent }], author: { login: 'github-actions[bot]' },
      committer: { login: 'github-actions[bot]' }, files: [{ filename: analyzer, status: 'modified', sha: sha('d') }] },
    content: { type: 'file', path: analyzer, sha: sha('d') },
    baseComparison: { status: 'ahead', merge_base_commit: { sha: parent } },
    mainComparison: { status: 'ahead', merge_base_commit: { sha: runHead } }
  };
}
test('trusted source run artifact, bot PR and sole DLL change permit automation CI', () => {
  assert.deepEqual(validate(fixture()), { runId: 123, attempt: 2 });
});
test('automation preflight rejects mismatched PR, run, artifact and non-DLL target', () => {
  const changes = [
    f => { f.pr.user.login = 'maintainer'; },
    f => { f.pr.head.repo.full_name = 'fork/repo'; },
    f => { f.pr.head.sha = sha('e'); },
    f => { f.pr.base.ref = 'feature'; },
    f => { f.run.path = '.github/workflows/other.yml'; },
    f => { f.run.head_branch = 'feature'; },
    f => { f.run.run_attempt = 3; },
    f => { f.provenance.head = sha('e'); },
    f => { f.provenance.parent = sha('e'); },
    f => { f.commit.parents.push({ sha: sha('e') }); },
    f => { f.commit.files.push({ filename: 'untrusted.js', status: 'added' }); },
    f => { f.content.type = 'symlink'; },
    f => { f.baseComparison.status = 'diverged'; },
    f => { f.mainComparison.status = 'diverged'; }
  ];
  for (const change of changes) { const value = fixture(); change(value); assert.throws(() => validate(value)); }
});
test('CLI obtains GitHub metadata and source run artifact before accepting target', t => {
  const value = fixture();
  const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'analyzer-pr-ci-test-'));
  t.after(() => fs.rmSync(temporary, { recursive: true, force: true }));
  const payloads = {
    pull: value.pr, run: value.run, provenance: value.provenance, commit: value.commit,
    content: value.content, base: value.baseComparison, main: value.mainComparison
  };
  for (const [name, payload] of Object.entries(payloads)) fs.writeFileSync(path.join(temporary, `${name}.json`), JSON.stringify(payload));
  const bin = path.join(temporary, 'bin'); fs.mkdirSync(bin);
  fs.writeFileSync(path.join(bin, 'gh'), `#!/bin/sh
printf '%s\\n' "$1 $2" >> "$FAKE_GH_CALLS"
if [ "$1" = run ]; then
  while [ "$1" != -D ]; do shift; done
  cp "$FAKE_PAYLOAD_ROOT/provenance.json" "$2/analyzer-pr-provenance.json"
  exit 0
fi
case "$2" in
  */pulls/*) name=pull ;;
  */actions/runs/*) name=run ;;
  */commits/*) name=commit ;;
  */contents/*) name=content ;;
  */compare/*...develop) name=base ;;
  */compare/*...main) name=main ;;
  *) exit 2 ;;
esac
cat "$FAKE_PAYLOAD_ROOT/$name.json"
`, { mode: 0o755 });
  const marker = path.join(temporary, 'calls');
  const result = spawnSync(process.execPath, [path.join(root, 'SourceGenerators/scripts/validate-analyzer-pr-ci.js')], {
    cwd: temporary, encoding: 'utf8', env: { ...process.env, PATH: `${bin}${path.delimiter}${process.env.PATH}`,
      FAKE_PAYLOAD_ROOT: temporary, FAKE_GH_CALLS: marker, GITHUB_REPOSITORY: value.repository,
      AUTOMATION_PR: value.number, TARGET_COMMIT: value.target, GH_TOKEN: 'never-print-this-token' }
  });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /Validated bot analyzer PR #42/);
  assert.doesNotMatch(result.stdout + result.stderr, /never-print-this-token/);
  const calls = fs.readFileSync(marker, 'utf8');
  assert.match(calls, /api repos\/owner\/repo\/pulls\/42/);
  assert.match(calls, /run download/);
  assert.match(calls, /api repos\/owner\/repo\/commits\//);
});
test('dispatch uses trusted main and preflight does not execute the target checkout', () => {
  const ci = fs.readFileSync(path.join(root, '.github/workflows/source-generator-ci.yml'), 'utf8');
  const update = fs.readFileSync(path.join(root, '.github/workflows/source-generator-update-dll.yml'), 'utf8');
  const source = fs.readFileSync(path.join(root, 'SourceGenerators/scripts/update-analyzer-pr.js'), 'utf8');
  assert.match(update, /ANALYZER_PR_PROVENANCE/);
  assert.doesNotMatch(source, /gh\(\['workflow', 'run'/);
  assert.match(update, /name: Upload provenance from the trusted controller[\s\S]*name: analyzer-pr-provenance-\$\{\{ github\.run_id \}\}-\$\{\{ github\.run_attempt \}\}/);
  assert.match(update, /gh workflow run source-generator-ci\.yml --repo "\$GITHUB_REPOSITORY" --ref main/);
  const preflight = ci.split('\n  preflight:\n')[1].split('\n  verify:\n')[0];
  assert.match(preflight, /ref: \$\{\{ github\.workflow_sha \}\}/);
  assert.match(preflight, /persist-credentials: false/);
  assert.ok(preflight.indexOf('Validate bot DLL PR before target code runs') < preflight.length);
  assert.doesNotMatch(preflight.slice(preflight.indexOf('Check out trusted main controller')), /ref: \$\{\{ inputs\.commit/);
  assert.match(ci, /commit: \$\{\{ github\.event\.pull_request\.head\.sha \|\| inputs\.commit \|\| github\.sha \}\}/);
  assert.doesNotMatch(fs.readFileSync(path.join(root, 'SourceGenerators/scripts/validate-analyzer-pr-ci.js'), 'utf8'), /console\.log\([^\n]*(GH_TOKEN|github\.token)/);
});
