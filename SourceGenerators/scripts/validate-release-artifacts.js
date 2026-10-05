#!/usr/bin/env node

'use strict';

const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

function fail(message) {
  throw new Error(message);
}

function usage() {
  return [
    'Usage: validate-release-artifacts.js',
    '--release-output <path>',
    '--analyzer <path>',
    '--expected-version <version>',
    '--expected-node-version <vX.Y.Z>',
    '--expected-npm-version <X.Y.Z>',
    '--require-initial-gate | --require-unity-gate | --require-passed-unity-gate [--expected-commit <sha>] [--expected-run-id <id>] [--expected-run-attempt <n>] [--expected-execution-attempt <n>]'
  ].join(' ');
}

function parseArguments(argv) {
  const options = {};
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === '--require-initial-gate' || argument === '--require-unity-gate' || argument === '--require-passed-unity-gate') {
      if (options.gateMode) fail(`Only one gate mode is allowed. ${usage()}`);
      options.gateMode = argument;
      continue;
    }
    if (!['--release-output', '--analyzer', '--expected-version', '--expected-node-version', '--expected-npm-version', '--expected-commit', '--expected-run-id', '--expected-run-attempt', '--expected-execution-attempt'].includes(argument)) {
      fail(`Unknown argument: ${argument}. ${usage()}`);
    }
    const value = argv[index + 1];
    if (!value || value.startsWith('--')) fail(`Missing value for ${argument}. ${usage()}`);
    options[argument.slice(2)] = value;
    index += 1;
  }
  for (const required of ['release-output', 'analyzer', 'expected-version', 'expected-node-version', 'expected-npm-version']) {
    if (!options[required]) fail(`Missing --${required}. ${usage()}`);
  }
  if (!options.gateMode) fail(`Missing gate mode. ${usage()}`);
  return options;
}

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}

function sha256(filePath) {
  return crypto.createHash('sha256').update(fs.readFileSync(filePath)).digest('hex');
}

function assertExactKeys(value, keys, label) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) fail(`${label} must be an object`);
  const actual = Object.keys(value).sort();
  const expected = keys.slice().sort();
  if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index])) {
    fail(`${label} has an unexpected field set: ${actual.join(', ')}`);
  }
}

function assertHex(value, length, label) {
  if (typeof value !== 'string' || !new RegExp(`^[0-9a-f]{${length}}$`).test(value)) {
    fail(`${label} must be ${length} lowercase hexadecimal characters`);
  }
}

function assertFile(filePath, label) {
  if (!fs.existsSync(filePath) || !fs.statSync(filePath).isFile()) fail(`${label} is missing: ${filePath}`);
}

function resolveArtifactPath(releaseOutput, relativePath, label) {
  if (typeof relativePath !== 'string' || relativePath.length === 0 || path.isAbsolute(relativePath)) {
    fail(`${label} must be a non-empty artifact-relative path`);
  }
  const resolved = path.resolve(releaseOutput, relativePath);
  const relative = path.relative(releaseOutput, resolved);
  if (relative === '' || relative === '..' || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) {
    fail(`${label} escapes the release output: ${relativePath}`);
  }
  return resolved;
}

const expectedGateVersions = ['2021.3.19f1', '6000.0.23f1', '6000.3.2f1'];
const requiredEvidence = {
  '2021.3.19f1': [
    'base-results.xml',
    'base.log',
    'base-consumer.log',
    'base-test-framework-consumer.log'
  ],
  '6000.0.23f1': [
    'source-generator-results.xml',
    'source-generator.log',
    'source-generator-bootstrap.log',
    'source-generator-consumer.log',
    'source-generator-generation.log',
    'source-generator-unchanged-generation.log',
    'source-generator-no-change.log',
    'source-generator-regeneration-results.xml',
    'source-generator-regeneration.log',
    'source-generator-regeneration-tests.log',
    'without-addon-results.xml',
    'without-addon.log',
    'without-addon-bootstrap.log',
    'normalized-v2.json'
  ],
  '6000.3.2f1': [
    'source-generator-results.xml',
    'source-generator.log',
    'source-generator-bootstrap.log',
    'source-generator-consumer.log',
    'source-generator-generation.log',
    'source-generator-unchanged-generation.log',
    'source-generator-no-change.log',
    'source-generator-regeneration-results.xml',
    'source-generator-regeneration.log',
    'source-generator-regeneration-tests.log',
    'without-addon-results.xml',
    'without-addon.log',
    'without-addon-bootstrap.log',
    'normalized-v2.json'
  ]
};

function validateEvidenceFiles(releaseOutput, version, files, requireComplete, requireCiHost = false) {
  if (!Array.isArray(files)) fail(`Unity ${version} evidence files must be an array`);
  const expectedPaths = new Set();
  const normalized = files.map((entry, index) => {
    assertExactKeys(entry, ['path', 'sha256'], `Unity ${version} evidence file ${index}`);
    const evidencePath = resolveArtifactPath(releaseOutput, entry.path, `Unity ${version} evidence file ${index} path`);
    const expectedPrefix = `unity-gate/${version}/`;
    if (!entry.path.startsWith(expectedPrefix)) fail(`Unity ${version} evidence file is outside its gate directory: ${entry.path}`);
    assertHex(entry.sha256, 64, `Unity ${version} evidence file ${entry.path} SHA-256`);
    assertFile(evidencePath, `Unity ${version} evidence file`);
    if (sha256(evidencePath) !== entry.sha256) fail(`Unity ${version} evidence file hash mismatch: ${entry.path}`);
    if (expectedPaths.has(entry.path)) fail(`Unity ${version} evidence file is duplicated: ${entry.path}`);
    expectedPaths.add(entry.path);
    return { path: entry.path, sha256: entry.sha256 };
  });
  if (requireComplete) {
    const expectedNames = requiredEvidence[version].concat(requireCiHost ? ['ci-host.log'] : []);
    const expected = expectedNames.map(name => `unity-gate/${version}/${name}`).sort();
    const actual = normalized.map(entry => entry.path).sort();
    if (JSON.stringify(actual) !== JSON.stringify(expected)) {
      fail(`Unity ${version} passed gate evidence is incomplete or unexpected`);
    }
  }
  return normalized;
}

function validateGateSummary(releaseOutput, version, summaryPath, expectedHash, manifest, expectedExecutionAttempt) {
  assertFile(summaryPath, `Unity ${version} gate summary`);
  if (expectedHash !== undefined && sha256(summaryPath) !== expectedHash) {
    fail(`Unity ${version} gate summary hash mismatch`);
  }
  const summary = readJson(summaryPath);
  assertExactKeys(summary, ['schemaVersion', 'status', 'version', 'exitCode', 'reason', 'evidencePath', 'files', 'timestampUtc'].concat(summary.ci ? ['ci'] : []), `Unity ${version} gate summary`);
  if (summary.schemaVersion !== 2 || summary.version !== version || !['passed', 'failed', 'not-run'].includes(summary.status)) {
    fail(`Unity ${version} gate summary has invalid schema/status/version`);
  }
  if (!Number.isInteger(summary.exitCode) || typeof summary.reason !== 'string' || typeof summary.timestampUtc !== 'string') {
    fail(`Unity ${version} gate summary has invalid metadata`);
  }
  if (summary.status === 'passed' && summary.exitCode !== 0) fail(`Unity ${version} passed gate has nonzero exit code`);
  if (summary.status !== 'passed' && summary.exitCode === 0) fail(`Unity ${version} unsuccessful gate has zero exit code`);
  if (manifest) validateCiGate(summary, manifest, expectedExecutionAttempt);
  if (summary.evidencePath !== `unity-gate/${version}`) fail(`Unity ${version} gate summary has invalid evidencePath`);
  const files = validateEvidenceFiles(releaseOutput, version, summary.files, summary.status === 'passed', manifest?.unityGate.mode === 'ci');
  if (summary.status === 'passed') {
    for (const file of files.filter(entry => entry.path.endsWith('.xml'))) validateTestXml(resolveArtifactPath(releaseOutput, file.path, 'test XML'));
  }
  return { status: summary.status, files, executionAttempt: summary.ci?.executionAttempt };
}

function validateUnityGate(releaseOutput, unityGate, gateMode, manifest, expectedExecutionAttempt) {
  assertExactKeys(unityGate, ['mode', 'status', 'versions', 'evidence', 'aggregateFile', 'aggregateSha256'], 'unityGate');
  if (!['manual', 'ci'].includes(unityGate.mode) || !['passed', 'failed', 'not-run'].includes(unityGate.status)) {
    fail('unityGate mode/status is invalid');
  }
  if (JSON.stringify(unityGate.versions) !== JSON.stringify(expectedGateVersions)) fail('unityGate versions mismatch');
  if (!Array.isArray(unityGate.evidence)) fail('unityGate evidence must be an array');

  if (gateMode === '--require-initial-gate') {
    if (unityGate.status !== 'not-run' || unityGate.evidence.length !== 0 ||
      unityGate.aggregateFile !== null || unityGate.aggregateSha256 !== null) {
      fail('Initial release output must record an empty manual Unity gate');
    }
    return;
  }

  if (typeof unityGate.aggregateFile !== 'string' || unityGate.aggregateFile !== 'unity-gate.json') {
    fail('Unity gate aggregateFile must be unity-gate.json after a matrix run');
  }
  assertHex(unityGate.aggregateSha256, 64, 'Unity gate aggregate SHA-256');
  const aggregatePath = resolveArtifactPath(releaseOutput, unityGate.aggregateFile, 'Unity gate aggregate path');
  assertFile(aggregatePath, 'Unity gate aggregate');
  if (sha256(aggregatePath) !== unityGate.aggregateSha256) fail('Unity gate aggregate hash mismatch');

  const aggregate = readJson(aggregatePath);
  assertExactKeys(aggregate, ['schemaVersion', 'mode', 'status', 'versions', 'timestampUtc'], 'Unity gate aggregate');
  if (aggregate.schemaVersion !== 2 || aggregate.mode !== unityGate.mode || aggregate.status !== unityGate.status ||
    !Array.isArray(aggregate.versions) || aggregate.versions.length !== expectedGateVersions.length ||
    typeof aggregate.timestampUtc !== 'string') {
    fail('Unity gate aggregate is invalid');
  }

  const expectedSummaries = [];
  const executionAttempts = new Set();
  let executionAttemptCount = 0;
  aggregate.versions.forEach((entry, index) => {
    const version = expectedGateVersions[index];
    assertExactKeys(entry, ['version', 'status', 'gate', 'files'], `Unity gate aggregate version ${index}`);
    if (entry.version !== version || !['passed', 'failed', 'not-run'].includes(entry.status)) {
      fail(`Unity gate aggregate version ${index} is invalid`);
    }
    assertExactKeys(entry.gate, ['path', 'sha256'], `Unity ${version} aggregate gate`);
    const gatePath = resolveArtifactPath(releaseOutput, entry.gate.path, `Unity ${version} gate path`);
    const expectedGatePath = `unity-gate/${version}/gate.json`;
    if (entry.gate.path !== expectedGatePath) fail(`Unity ${version} aggregate gate path mismatch`);
    assertHex(entry.gate.sha256, 64, `Unity ${version} gate SHA-256`);
    const summary = validateGateSummary(releaseOutput, version, gatePath, entry.gate.sha256, manifest, expectedExecutionAttempt);
    if (summary.status !== entry.status || JSON.stringify(summary.files) !== JSON.stringify(entry.files)) {
      fail(`Unity ${version} aggregate does not match its gate summary`);
    }
    validateEvidenceFiles(releaseOutput, version, entry.files, entry.status === 'passed', manifest.unityGate.mode === 'ci');
    expectedSummaries.push(entry.gate.path);
    if (summary.executionAttempt !== undefined) {
      executionAttempts.add(summary.executionAttempt);
      executionAttemptCount += 1;
    }
  });

  if (manifest.unityGate.mode === 'ci' &&
    (executionAttempts.size > 1 || (executionAttemptCount !== 0 && executionAttemptCount !== expectedGateVersions.length))) {
    fail('Unity gates must use one consistent execution attempt schema and value');
  }

  if (JSON.stringify(unityGate.evidence) !== JSON.stringify(expectedSummaries)) {
    fail('unityGate evidence must list gate summaries in matrix order');
  }
  const aggregateStatus = aggregate.versions.every(entry => entry.status === 'passed')
    ? 'passed'
    : aggregate.versions.some(entry => entry.status === 'failed') ? 'failed' : 'not-run';
  if (aggregateStatus !== unityGate.status) fail('Unity gate aggregate status does not match version statuses');
  if (gateMode === '--require-passed-unity-gate' && aggregateStatus !== 'passed') fail('All Unity gates must pass for release');
}

function validateChecksums(releaseOutput, manifest) {
  const checksumsPath = path.join(releaseOutput, 'SHA256SUMS');
  assertFile(checksumsPath, 'SHA256SUMS');
  const expectedFiles = manifest.archives.map(archive => archive.file).concat('release-manifest.json').sort();
  const text = fs.readFileSync(checksumsPath, 'utf8');
  const lines = text.trim().length === 0 ? [] : text.trim().split(/\r?\n/);
  if (lines.length !== expectedFiles.length) fail('SHA256SUMS must contain exactly the two archives and release manifest');
  lines.forEach((line, index) => {
    const match = /^([0-9a-f]{64})  (\S+)$/.exec(line);
    if (!match || match[2] !== expectedFiles[index]) fail('SHA256SUMS filenames must be unique and filename-sorted');
    const filePath = resolveArtifactPath(releaseOutput, match[2], 'SHA256SUMS file');
    assertFile(filePath, `SHA256SUMS file ${match[2]}`);
    if (sha256(filePath) !== match[1]) fail(`SHA256SUMS checksum mismatch: ${match[2]}`);
  });
}

function validateRelease(options) {
  const releaseOutput = path.resolve(options['release-output']);
  const manifestPath = path.join(releaseOutput, 'release-manifest.json');
  assertFile(manifestPath, 'release manifest');
  const manifest = readJson(manifestPath);

  assertExactKeys(manifest, ['schemaVersion', 'version', 'tag', 'provenance', 'archives', 'analyzerSha256', 'unityGate', 'registryPublication'], 'release manifest');
  if (manifest.schemaVersion !== 2 || manifest.version !== options['expected-version'] || manifest.tag !== options['expected-version']) {
    fail('release manifest schema/version/tag mismatch');
  }
  assertExactKeys(manifest.provenance, ['commit', 'treeState', 'nodeVersion', 'npmVersion'].concat(manifest.provenance.ci ? ['ci'] : []), 'release provenance');
  if (!['clean', 'dirty'].includes(manifest.provenance.treeState)) fail('release provenance treeState is invalid');
  if (manifest.provenance.treeState === 'clean') assertHex(manifest.provenance.commit, 40, 'clean release commit');
  if (manifest.provenance.treeState === 'dirty' && manifest.provenance.commit !== null) fail('dirty release commit must be null');
  if (manifest.provenance.nodeVersion !== options['expected-node-version'] ||
    manifest.provenance.npmVersion !== options['expected-npm-version']) {
    fail('release provenance Node/npm version mismatch');
  }

  if (options['expected-commit']) {
    assertHex(options['expected-commit'], 40, 'expected commit');
    if (manifest.provenance.treeState !== 'clean' || manifest.provenance.commit !== options['expected-commit']) fail('Release commit or clean tree mismatch');
  }
  if (manifest.unityGate.mode === 'ci') {
    assertExactKeys(manifest.provenance.ci, ['repository', 'runId', 'runAttempt'], 'CI provenance');
    if (!/^[^/]+\/[^/]+$/.test(manifest.provenance.ci.repository) || !/^[1-9][0-9]*$/.test(manifest.provenance.ci.runId) || !/^[1-9][0-9]*$/.test(manifest.provenance.ci.runAttempt)) fail('Invalid CI provenance');
    if (manifest.provenance.treeState !== 'clean') fail('CI candidate must have a clean tree');
    if (options['expected-run-id'] && manifest.provenance.ci.runId !== options['expected-run-id']) fail('CI run ID mismatch');
    if (options['expected-run-attempt'] && manifest.provenance.ci.runAttempt !== options['expected-run-attempt']) fail('CI run attempt mismatch');
  } else if (manifest.provenance.ci || options['expected-run-id']) fail('Expected CI provenance');

  const expectedArchives = [
    ['jp.rhycol.openapicodegen', 'Packages/OpenApiCodeGen'],
    ['jp.rhycol.openapicodegen.source-generator', 'Packages/OpenApiCodeGen.SourceGenerator']
  ];
  if (!Array.isArray(manifest.archives) || manifest.archives.length !== expectedArchives.length) fail('release manifest archives mismatch');
  manifest.archives.forEach((archive, index) => {
    assertExactKeys(archive, ['package', 'version', 'path', 'file', 'root', 'size', 'sha256'], `archive ${index}`);
    const [expectedPackage, expectedPath] = expectedArchives[index];
    const expectedFile = `${expectedPackage}-${options['expected-version']}.tgz`;
    if (archive.package !== expectedPackage || archive.version !== options['expected-version'] ||
      archive.path !== expectedPath || archive.file !== expectedFile || archive.root !== 'package/' ||
      !Number.isSafeInteger(archive.size) || archive.size <= 0) {
      fail(`archive ${index} metadata mismatch`);
    }
    assertHex(archive.sha256, 64, `archive ${index} SHA-256`);
    const archivePath = resolveArtifactPath(releaseOutput, archive.file, `archive ${index} path`);
    assertFile(archivePath, `archive ${index}`);
    if (fs.statSync(archivePath).size !== archive.size || sha256(archivePath) !== archive.sha256) {
      fail(`archive ${index} size or SHA-256 mismatch`);
    }
  });

  assertHex(manifest.analyzerSha256, 64, 'analyzer SHA-256');
  assertFile(options.analyzer, 'packaged analyzer');
  if (sha256(options.analyzer) !== manifest.analyzerSha256) fail('packaged analyzer SHA-256 mismatch');
  if (manifest.registryPublication !== 'not-performed') fail('registryPublication must be not-performed');

  validateUnityGate(releaseOutput, manifest.unityGate, options.gateMode, manifest, options['expected-execution-attempt']);
  validateChecksums(releaseOutput, manifest);
  return manifest;
}

function candidateFingerprint(manifest) {
  return crypto.createHash('sha256').update(JSON.stringify(manifest.archives.map(({ file, sha256 }) => ({ file, sha256 })))).digest('hex');
}

function validateCiGate(summary, manifest, expectedExecutionAttempt) {
  if (manifest.unityGate.mode !== 'ci') {
    if (summary.ci) fail('Manual gate contains CI provenance');
    return;
  }
  const hasExecutionAttempt = Boolean(summary.ci && Object.prototype.hasOwnProperty.call(summary.ci, 'executionAttempt'));
  assertExactKeys(summary.ci, ['repository', 'runId', 'runAttempt', 'commit', 'candidateSha256'].concat(hasExecutionAttempt ? ['executionAttempt'] : []), 'CI gate provenance');
  const expected = { ...manifest.provenance.ci, commit: manifest.provenance.commit, candidateSha256: candidateFingerprint(manifest) };
  for (const key of Object.keys(expected)) if (summary.ci[key] !== expected[key]) fail(`CI gate ${key} mismatch`);
  if (hasExecutionAttempt && !/^[1-9][0-9]*$/.test(summary.ci.executionAttempt)) fail('CI gate executionAttempt is invalid');
  if (expectedExecutionAttempt && summary.ci.executionAttempt !== expectedExecutionAttempt) fail('CI gate execution attempt mismatch');
}

function validateTestXml(filePath) {
  // Use the system XML parser; do not accept a truncated file or infer success from a substring.
  const xpath = `concat(/test-run/@result, '|', /test-run/@total, '|', /test-run/@passed, '|', /test-run/@failed, '|', string(/test-run/@skipped), '|', string(/test-run/@inconclusive), '|', count(//test-case), '|', count(//test-case[not(@result='Passed')]), '|', count(//test-case[@result='Passed']))`;
  let output;
  try { output = execFileSync('xmllint', ['--nonet', '--xpath', xpath, filePath], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }); }
  catch { fail(`Unity test XML is malformed or unreadable: ${filePath}`); }
  const [result, total, passed, failed, skipped, inconclusive, count, bad, actualPassed] = output.trim().split('|');
  if (result !== 'Passed' || !/^[1-9][0-9]*$/.test(total) || !/^[1-9][0-9]*$/.test(passed) ||
    failed !== '0' || (skipped !== '' && skipped !== '0') || (inconclusive !== '' && inconclusive !== '0') ||
    Number(count) !== Number(total) || Number(passed) !== Number(total) || bad !== '0' || Number(actualPassed) !== Number(passed)) {
    fail(`Unity test XML does not prove complete passing tests: ${filePath}`);
  }
}

module.exports = { parseArguments, validateRelease, validateGateSummary, candidateFingerprint, sha256, expectedGateVersions, requiredEvidence, validateTestXml };
if (require.main === module) {
  try {
    const options = parseArguments(process.argv.slice(2));
    validateRelease(options);
    process.stdout.write(`Validated release artifacts in ${path.resolve(options['release-output'])}.\n`);
  } catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  }
}
