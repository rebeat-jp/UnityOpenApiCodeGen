'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const { sha256, candidateFingerprint, expectedGateVersions, requiredEvidence } = require('../validate-release-artifacts');
const { writeChecksums } = require('../ci-evidence');
const commit = 'a'.repeat(40);
function fixture(ci = true, executionAttempt = '1') {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'release-tests-'));
  const output = path.join(root, 'candidate'); fs.mkdirSync(output);
  const analyzer = path.join(root, 'analyzer.dll'); fs.writeFileSync(analyzer, 'deterministic analyzer');
  const version = '0.5.0';
  const archives = [['jp.rhycol.openapicodegen', 'Packages/OpenApiCodeGen'], ['jp.rhycol.openapicodegen.source-generator', 'Packages/OpenApiCodeGen.SourceGenerator']].map(([name, packagePath]) => {
    const file = `${name}-${version}.tgz`; fs.writeFileSync(path.join(output, file), name);
    return { package: name, version, path: packagePath, file, root: 'package/', size: fs.statSync(path.join(output, file)).size, sha256: sha256(path.join(output, file)) };
  });
  const manifest = { schemaVersion: 2, version, tag: version, provenance: { commit, treeState: 'clean', nodeVersion: 'v24.20.0', npmVersion: '11.19.0', ...(ci ? { ci: { repository: 'owner/repo', runId: '123', runAttempt: '1' } } : {}) }, archives, analyzerSha256: sha256(analyzer), unityGate: { mode: ci ? 'ci' : 'manual', status: 'not-run', versions: expectedGateVersions, evidence: [], aggregateFile: null, aggregateSha256: null }, registryPublication: 'not-performed' };
  const save = () => { fs.writeFileSync(path.join(output, 'release-manifest.json'), JSON.stringify(manifest, null, 2) + '\n'); writeChecksums(output, manifest); };
  save();
  const options = { 'release-output': output, analyzer, 'expected-version': version, 'expected-node-version': 'v24.20.0', 'expected-npm-version': '11.19.0', 'expected-commit': commit, ...(ci ? { 'expected-run-id': '123', 'expected-run-attempt': '1' } : {}), gateMode: '--require-initial-gate' };
  function gates(status = 'passed') {
    for (const unityVersion of expectedGateVersions) {
      const dir = path.join(output, 'unity-gate', unityVersion); fs.mkdirSync(dir, { recursive: true });
      const names = status === 'passed' ? requiredEvidence[unityVersion] : [];
      const files = names.map(name => {
        const file = path.join(dir, name);
        fs.writeFileSync(file, name.endsWith('.xml') ? '<test-run result="Passed" total="1" passed="1" failed="0"><test-suite><test-case fullname="Example.Tests.Passes" result="Passed" /></test-suite></test-run>' : 'verification evidence');
        return { path: `unity-gate/${unityVersion}/${name}`, sha256: sha256(file) };
      }).sort((a, b) => a.path.localeCompare(b.path));
      const summary = { schemaVersion: 2, status, version: unityVersion, exitCode: status === 'passed' ? 0 : 2, reason: 'fixture', evidencePath: `unity-gate/${unityVersion}`, files, timestampUtc: new Date(0).toISOString(), ...(ci ? { ci: { ...manifest.provenance.ci, executionAttempt, commit, candidateSha256: candidateFingerprint(manifest) } } : {}) };
      fs.writeFileSync(path.join(dir, 'gate.json'), JSON.stringify(summary, null, 2) + '\n');
    }
  }
  return { root, output, analyzer, manifest, options, save, gates, remove: () => fs.rmSync(root, { recursive: true, force: true }) };
}
module.exports = { fixture, commit };
