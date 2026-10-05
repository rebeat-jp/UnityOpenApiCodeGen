'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('node:child_process');

const repository = path.resolve(__dirname, '../../..');
const scripts = path.join(repository, 'SourceGenerators/scripts');

function scriptFixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'verify-unity-args-'));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const stubScripts = path.join(root, 'scripts');
  const bin = path.join(root, 'bin');
  fs.mkdirSync(stubScripts);
  fs.mkdirSync(bin);
  for (const name of ['verify-base-unity.sh', 'verify-unity.sh', 'verify-unity-matrix.sh']) {
    fs.copyFileSync(path.join(scripts, name), path.join(stubScripts, name));
  }
  function write(name, content, directory = stubScripts) {
    fs.writeFileSync(path.join(directory, name), content, { mode: 0o755 });
  }
  write('common.sh', 'repository_root="$TEST_REPOSITORY_ROOT"\nsource_generators_root="$TEST_REPOSITORY_ROOT/SourceGenerators"\npackage_analyzer="$TEST_REPOSITORY_ROOT/Packages/OpenApiCodeGen.SourceGenerator/Runtime/Analyzers/Rhycol.OpenApiCodeGen.SourceGenerator.dll"\n');
  write('ci-time-budget.sh', 'CI_EDITOR_TIMEOUT_SECONDS=1\n');
  write('xmllint', '#!/bin/sh\nexit 0\n', bin);
  write('ci-evidence.js', `const fs = require('fs');
const [file, status, exitCode, version, reason] = process.argv.slice(3);
fs.writeFileSync(file, JSON.stringify({ status, exitCode: Number(exitCode), version, reason }));
`);
  const env = {
    ...process.env,
    PATH: `${bin}${path.delimiter}${process.env.PATH}`,
    TEST_REPOSITORY_ROOT: repository,
    SOURCE_GENERATOR_CI: '0',
    SOURCE_GENERATOR_RELEASE_OUTPUT: path.join(root, 'release'),
    SOURCE_GENERATOR_EVIDENCE_ROOT: path.join(root, 'evidence'),
    UNITY_EXECUTABLE: process.execPath,
    PACK_MARKER: path.join(root, 'pack-args'),
    GATE_MARKER: path.join(root, 'gate-args')
  };
  const run = (name, args = []) => spawnSync('/bin/bash', [path.join(stubScripts, name), ...args], {
    cwd: repository, env, encoding: 'utf8'
  });
  return { root, env, run, write };
}

test('direct Unity gates forward zero or one pack arguments on Bash 3.2 and retain packing failure', t => {
  const f = scriptFixture(t);
  f.write('pack-release.sh', '#!/bin/bash\nprintf "%s\\n" "$#" "$@" > "$PACK_MARKER"\nexit 9\n');
  for (const [script, version] of [['verify-base-unity.sh', '2021.3.19f1'], ['verify-unity.sh', '6000.0.23f1']]) {
    for (const [args, expected] of [[[version], ['0']], [[version, '--allow-dirty'], ['1', '--allow-dirty']]]) {
      fs.rmSync(f.env.PACK_MARKER, { force: true });
      const result = f.run(script, args);
      assert.equal(result.status, 2, result.stderr);
      assert.ok(fs.existsSync(f.env.PACK_MARKER), `${result.stderr}\n${fs.readFileSync(path.join(f.env.SOURCE_GENERATOR_EVIDENCE_ROOT, 'gate.json'), 'utf8')}`);
      assert.deepEqual(fs.readFileSync(f.env.PACK_MARKER, 'utf8').trim().split('\n'), expected);
      const gate = JSON.parse(fs.readFileSync(path.join(f.env.SOURCE_GENERATOR_EVIDENCE_ROOT, 'gate.json'), 'utf8'));
      assert.equal(gate.status, 'not-run');
      assert.equal(gate.exitCode, 2);
      assert.match(gate.reason, /packing failed/);
      assert.doesNotMatch(result.stderr, /unbound variable/);
    }
  }
});

test('manual Unity matrix forwards optional dirty flag to packer and every gate', t => {
  const f = scriptFixture(t);
  f.write('verify.sh', '#!/bin/bash\nexit 0\n');
  f.write('pack-release.sh', `#!/bin/bash
printf '%s\\n' "$#" "$@" > "$PACK_MARKER"
mkdir -p "$SOURCE_GENERATOR_RELEASE_OUTPUT"
printf '{"unityGate":{"status":"passed"}}\\n' > "$SOURCE_GENERATOR_RELEASE_OUTPUT/release-manifest.json"
`);
  f.write('verify-base-unity.sh', '#!/bin/bash\nprintf "%s\\n" "$#" "$@" >> "$GATE_MARKER"\n');
  f.write('verify-unity.sh', '#!/bin/bash\nprintf "%s\\n" "$#" "$@" >> "$GATE_MARKER"\n');
  f.write('aggregate-unity-evidence.js', '');
  for (const [args, expectedPack, expectedGateCount] of [[[], '0\n', 1], [['--allow-dirty'], '1\n--allow-dirty\n', 2]]) {
    fs.rmSync(f.env.GATE_MARKER, { force: true });
    const result = f.run('verify-unity-matrix.sh', args);
    assert.equal(result.status, 0, result.stderr);
    assert.equal(fs.readFileSync(f.env.PACK_MARKER, 'utf8'), expectedPack);
    const gateArgs = fs.readFileSync(f.env.GATE_MARKER, 'utf8').trim().split('\n');
    assert.deepEqual(gateArgs, ['2021.3.19f1', '6000.0.23f1', '6000.3.2f1'].flatMap(version => [String(expectedGateCount), version, ...args]));
    assert.doesNotMatch(result.stderr, /unbound variable/);
  }
});

test('manual Unity matrix reports packing failure without starting editor gates', t => {
  const f = scriptFixture(t);
  f.write('verify.sh', '#!/bin/bash\nexit 0\n');
  f.write('pack-release.sh', `#!/bin/bash
printf '%s\\n' "$#" "$@" > "$PACK_MARKER"
mkdir -p "$SOURCE_GENERATOR_RELEASE_OUTPUT"
printf '{"unityGate":{"status":"failed"}}\\n' > "$SOURCE_GENERATOR_RELEASE_OUTPUT/release-manifest.json"
exit 9
`);
  f.write('aggregate-unity-evidence.js', '');
  const result = f.run('verify-unity-matrix.sh');
  assert.equal(result.status, 1, result.stderr);
  assert.equal(fs.readFileSync(f.env.PACK_MARKER, 'utf8'), '0\n');
  assert.equal(fs.existsSync(f.env.GATE_MARKER), false);
  for (const version of ['2021.3.19f1', '6000.0.23f1', '6000.3.2f1']) {
    const gate = JSON.parse(fs.readFileSync(path.join(f.env.SOURCE_GENERATOR_RELEASE_OUTPUT, 'unity-gate', version, 'gate.json'), 'utf8'));
    assert.equal(gate.status, 'not-run');
    assert.equal(gate.exitCode, 2);
  }
  assert.doesNotMatch(result.stderr, /unbound variable/);
});
