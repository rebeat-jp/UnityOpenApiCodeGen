#!/usr/bin/env node
'use strict';
const fs = require('fs');
const path = require('path');
const { validateRelease, validateGateSummary, sha256, expectedGateVersions } = require('./validate-release-artifacts');
const { writeChecksums } = require('./ci-evidence');

function aggregate(options) {
  const output = path.resolve(options['release-output']);
  // Validate candidate bytes/provenance before consuming independently uploaded evidence.
  validateRelease({ ...options, gateMode: '--require-initial-gate' });
  const manifestPath = path.join(output, 'release-manifest.json');
  const manifest = JSON.parse(fs.readFileSync(manifestPath));
  const executionAttempts = new Set();
  let executionAttemptCount = 0;
  const versions = expectedGateVersions.map(version => {
    const relative = `unity-gate/${version}/gate.json`;
    const filePath = path.join(output, relative);
    const summary = validateGateSummary(output, version, filePath, undefined, manifest, options['expected-execution-attempt']);
    if (summary.executionAttempt !== undefined) {
      executionAttempts.add(summary.executionAttempt);
      executionAttemptCount += 1;
    }
    return { version, status: summary.status, gate: { path: relative, sha256: sha256(filePath) }, files: summary.files };
  });
  if (manifest.unityGate.mode === 'ci' &&
    (executionAttempts.size > 1 || (executionAttemptCount !== 0 && executionAttemptCount !== expectedGateVersions.length))) {
    throw new Error('Unity gates must use one consistent execution attempt schema and value');
  }
  const status = versions.every(entry => entry.status === 'passed') ? 'passed' : versions.some(entry => entry.status === 'failed') ? 'failed' : 'not-run';
  const aggregate = { schemaVersion: 2, mode: manifest.unityGate.mode, status, versions, timestampUtc: new Date().toISOString() };
  const aggregatePath = path.join(output, 'unity-gate.json');
  fs.writeFileSync(aggregatePath, JSON.stringify(aggregate, null, 2) + '\n');
  manifest.unityGate = { mode: aggregate.mode, status, versions: expectedGateVersions, evidence: versions.map(entry => entry.gate.path), aggregateFile: 'unity-gate.json', aggregateSha256: sha256(aggregatePath) };
  fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2) + '\n');
  writeChecksums(output, manifest);
  const finalGateMode = options.gateMode === '--require-initial-gate' ? '--require-unity-gate' : (options.gateMode || '--require-unity-gate');
  validateRelease({ ...options, gateMode: finalGateMode });
  console.log(`Unity gate aggregate: ${status}`);
  return manifest;
}
module.exports = { aggregate };
if (require.main === module) {
  try { aggregate(require('./validate-release-artifacts').parseArguments(process.argv.slice(2))); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
