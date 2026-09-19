#!/usr/bin/env node
'use strict';
const fs = require('fs');
const path = require('path');
const { candidateFingerprint, sha256 } = require('./validate-release-artifacts');

function ciContext() {
  if (process.env.SOURCE_GENERATOR_CI !== '1') return undefined;
  const ci = { repository: process.env.GITHUB_REPOSITORY, runId: process.env.GITHUB_RUN_ID, runAttempt: process.env.GITHUB_RUN_ATTEMPT };
  if (!/^[^/]+\/[^/]+$/.test(ci.repository || '') || !/^[1-9][0-9]*$/.test(ci.runId || '') || !/^[1-9][0-9]*$/.test(ci.runAttempt || '')) throw new Error('Missing GitHub CI provenance');
  return ci;
}
function gateContext(manifest) {
  if (process.env.SOURCE_GENERATOR_CI !== '1') return undefined;
  const candidateAttempt = process.env.SOURCE_GENERATOR_CANDIDATE_ATTEMPT || process.env.GITHUB_RUN_ATTEMPT;
  const executionAttempt = process.env.GITHUB_RUN_ATTEMPT;
  const candidate = { repository: process.env.GITHUB_REPOSITORY, runId: process.env.GITHUB_RUN_ID, runAttempt: candidateAttempt };
  if (!/^[^/]+\/[^/]+$/.test(candidate.repository || '') || !/^[1-9][0-9]*$/.test(candidate.runId || '') ||
    !/^[1-9][0-9]*$/.test(candidate.runAttempt || '') || !/^[1-9][0-9]*$/.test(executionAttempt || '')) {
    throw new Error('Missing GitHub CI gate provenance');
  }
  if (manifest.provenance.treeState !== 'clean' || manifest.provenance.commit !== process.env.SOURCE_GENERATOR_CI_COMMIT || JSON.stringify(manifest.provenance.ci) !== JSON.stringify(candidate)) throw new Error('Candidate provenance does not match this CI execution');
  return { ...candidate, executionAttempt, commit: manifest.provenance.commit, candidateSha256: candidateFingerprint(manifest) };
}
function redact(text) {
  const serial = process.env.UNITY_SERIAL;
  const values = [process.env.UNITY_LICENSE, process.env.UNITY_EMAIL, process.env.UNITY_PASSWORD, serial, serial && `${serial.slice(0, -4)}XXXX`].filter(Boolean).sort((a, b) => b.length - a.length);
  for (const value of values) text = text.split(value).join('[REDACTED]');
  return text;
}
function sanitizeFile(source, destination) {
  const temporary = `${destination}.redacted-${process.pid}`;
  try {
    fs.writeFileSync(temporary, redact(fs.readFileSync(source, 'utf8')), { mode: 0o600 });
    fs.renameSync(temporary, destination);
  } catch (error) {
    fs.rmSync(temporary, { force: true });
    fs.rmSync(destination, { force: true });
    throw new Error('Evidence sanitization failed; artifact was removed');
  }
}
function sanitizeEvidence(evidenceRoot) {
  const files = fs.readdirSync(evidenceRoot, { withFileTypes: true }).filter(entry => entry.isFile() && /\.(log|xml)$/.test(entry.name)).map(entry => path.join(evidenceRoot, entry.name));
  try { for (const file of files) sanitizeFile(file, file); }
  catch (error) { for (const file of files) fs.rmSync(file, { force: true }); throw error; }
}
function writeGate([outputPath, status, exitCode, version, reason, evidenceRoot, releaseOutput]) {
  // Sanitize before parsing any potentially missing/malformed provenance. Always-upload
  // failure paths must never leave raw logs behind.
  if (process.env.SOURCE_GENERATOR_CI === '1') sanitizeEvidence(evidenceRoot);
  const manifestPath = path.join(releaseOutput, 'release-manifest.json');
  const manifest = fs.existsSync(manifestPath) ? JSON.parse(fs.readFileSync(manifestPath)) : undefined;
  const ci = process.env.SOURCE_GENERATOR_CI === '1' ? gateContext(manifest) : undefined;
  const files = fs.readdirSync(evidenceRoot, { withFileTypes: true }).filter(entry => entry.isFile() && entry.name !== 'gate.json').map(entry => {
    const filePath = path.join(evidenceRoot, entry.name);
    // Artifact uploads do not apply GitHub's console masking. Redact before hashing.
    return { path: path.relative(releaseOutput, filePath).split(path.sep).join('/'), sha256: sha256(filePath) };
  }).sort((a, b) => a.path.localeCompare(b.path));
  const summary = { schemaVersion: 2, status, version, exitCode: Number(exitCode), reason: redact(reason), evidencePath: path.relative(releaseOutput, evidenceRoot).split(path.sep).join('/'), files, timestampUtc: new Date().toISOString(), ...(ci ? { ci } : {}) };
  fs.writeFileSync(outputPath, JSON.stringify(summary, null, 2) + '\n');
}
function finalizeGate([hostLog, evidenceRoot, releaseOutput, version, policy, fallbackStatus, fallbackExitCode, fallbackReason]) {
  if (!['preserve', 'replace'].includes(policy)) throw new Error('Gate finalization policy must be preserve or replace');
  fs.mkdirSync(evidenceRoot, { recursive: true });
  const gatePath = path.join(evidenceRoot, 'gate.json');
  let status = fallbackStatus;
  let exitCode = Number(fallbackExitCode);
  let reason = fallbackReason;
  if (policy === 'preserve' && fs.existsSync(gatePath)) {
    try {
      const existing = JSON.parse(fs.readFileSync(gatePath, 'utf8'));
      if (!['passed', 'failed', 'not-run'].includes(existing.status) || !Number.isInteger(existing.exitCode) ||
        typeof existing.reason !== 'string' || existing.version !== version) throw new Error('invalid existing gate');
      ({ status, exitCode, reason } = existing);
    } catch {
      status = fallbackStatus; exitCode = Number(fallbackExitCode); reason = fallbackReason;
    }
  }
  if (fs.existsSync(hostLog)) sanitizeFile(hostLog, path.join(evidenceRoot, 'ci-host.log'));
  writeGate([gatePath, status, String(exitCode), version, reason, evidenceRoot, releaseOutput]);
}
function writeChecksums(output, manifest) {
  const files = manifest.archives.map(archive => archive.file).concat('release-manifest.json').sort();
  fs.writeFileSync(path.join(output, 'SHA256SUMS'), files.map(file => `${sha256(path.join(output, file))}  ${file}`).join('\n') + '\n');
}
function stampCandidate(output) {
  const file = path.join(output, 'release-manifest.json');
  const manifest = JSON.parse(fs.readFileSync(file));
  const ci = ciContext();
  if (ci) {
    if (manifest.provenance.treeState !== 'clean' || manifest.provenance.commit !== process.env.SOURCE_GENERATOR_CI_COMMIT) throw new Error('CI packing requires exact clean commit');
    manifest.provenance.ci = ci;
    manifest.unityGate.mode = 'ci';
    fs.writeFileSync(file, JSON.stringify(manifest, null, 2) + '\n');
  }
}
module.exports = { ciContext, gateContext, redact, writeGate, finalizeGate, writeChecksums, stampCandidate, sanitizeFile, sanitizeEvidence };
if (require.main === module) {
  try {
    const [command, ...args] = process.argv.slice(2);
    if (command === 'stamp-candidate' && args.length === 1) stampCandidate(args[0]);
    else if (command === 'sanitize-file' && args.length === 2) sanitizeFile(args[0], args[1]);
    else if (command === 'write-gate' && args.length === 7) writeGate(args);
    else if (command === 'finalize-gate' && args.length === 8) finalizeGate(args);
    else throw new Error('Usage: ci-evidence.js stamp-candidate <output> | write-gate <path> <status> <exit> <version> <reason> <evidence-root> <output> | finalize-gate <host-log> <evidence-root> <output> <version> <preserve|replace> <status> <exit> <reason>');
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
