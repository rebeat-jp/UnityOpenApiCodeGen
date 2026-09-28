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
function oneLine(text) {
  return redact(String(text)).replace(/[\u0000-\u001f\u007f]+/g, ' ').replace(/\s+/g, ' ').trim();
}
function escapeWorkflowCommand(text, property = false) {
  let value = oneLine(text).replace(/%/g, '%25').replace(/\r/g, '%0D').replace(/\n/g, '%0A');
  if (property) value = value.replace(/:/g, '%3A').replace(/,/g, '%2C');
  return value;
}
function escapeMarkdownCell(text) {
  return oneLine(text)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/\\/g, '\\\\')
    .replace(/\|/g, '\\|');
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
  if (!['preserve', 'preserve-failure', 'replace'].includes(policy)) throw new Error('Gate finalization policy must be preserve, preserve-failure, or replace');
  fs.mkdirSync(evidenceRoot, { recursive: true });
  const gatePath = path.join(evidenceRoot, 'gate.json');
  let status = fallbackStatus;
  let exitCode = Number(fallbackExitCode);
  let reason = fallbackReason;
  if (policy !== 'replace' && fs.existsSync(gatePath)) {
    try {
      const existing = JSON.parse(fs.readFileSync(gatePath, 'utf8'));
      if (!['passed', 'failed', 'not-run'].includes(existing.status) || !Number.isInteger(existing.exitCode) ||
        typeof existing.reason !== 'string' || existing.version !== version) throw new Error('invalid existing gate');
      // A non-zero container exit may still have written the precise gate
      // failure. Preserve it, but never allow a stale passed gate to survive.
      if (policy === 'preserve' || existing.status !== 'passed') ({ status, exitCode, reason } = existing);
    } catch {
      status = fallbackStatus; exitCode = Number(fallbackExitCode); reason = fallbackReason;
    }
  }
  if (fs.existsSync(hostLog)) sanitizeFile(hostLog, path.join(evidenceRoot, 'ci-host.log'));
  writeGate([gatePath, status, String(exitCode), version, reason, evidenceRoot, releaseOutput]);
}
function reportGate([gatePath, summaryPath = '']) {
  const gate = JSON.parse(fs.readFileSync(gatePath, 'utf8'));
  if (!['passed', 'failed', 'not-run'].includes(gate.status) ||
      !Number.isInteger(gate.exitCode) || typeof gate.reason !== 'string' ||
      typeof gate.version !== 'string') throw new Error('Cannot report an invalid gate summary');
  const reason = oneLine(gate.reason) || 'No failure reason was recorded.';
  const version = oneLine(gate.version) || 'unknown';
  const level = gate.status === 'passed' ? 'notice' : gate.status === 'failed' ? 'error' : 'warning';
  const title = `Unity ${version} gate ${gate.status}`;
  const detail = `Unity gate ${version}: ${gate.status} (exit ${gate.exitCode}): ${reason}`;
  // Emit only a recognized V2 workflow command. GitHub's legacy parser scans
  // for ##[command] anywhere in ordinary output, even after a safe prefix.
  console.log(`::${level} title=${escapeWorkflowCommand(title, true)}::${escapeWorkflowCommand(detail)}`);
  if (summaryPath) {
    const marker = '<!-- openapi-codegen-unity-gates -->';
    const existing = fs.existsSync(summaryPath) ? fs.readFileSync(summaryPath, 'utf8') : '';
    const heading = existing.includes(marker) ? '' : `\n${marker}\n## Unity gate results\n\n| Version | Status | Exit | Reason |\n| --- | --- | ---: | --- |\n`;
    fs.appendFileSync(summaryPath,
      `${heading}| ${escapeMarkdownCell(version)} | ${escapeMarkdownCell(gate.status)} | ${gate.exitCode} | ${escapeMarkdownCell(reason)} |\n`);
  }
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
module.exports = { ciContext, gateContext, redact, oneLine, escapeWorkflowCommand, writeGate, finalizeGate, reportGate, writeChecksums, stampCandidate, sanitizeFile, sanitizeEvidence };
if (require.main === module) {
  try {
    const [command, ...args] = process.argv.slice(2);
    if (command === 'stamp-candidate' && args.length === 1) stampCandidate(args[0]);
    else if (command === 'sanitize-file' && args.length === 2) sanitizeFile(args[0], args[1]);
    else if (command === 'write-gate' && args.length === 7) writeGate(args);
    else if (command === 'finalize-gate' && args.length === 8) finalizeGate(args);
    else if (command === 'report-gate' && (args.length === 1 || args.length === 2)) reportGate(args);
    else throw new Error('Usage: ci-evidence.js stamp-candidate <output> | write-gate <path> <status> <exit> <version> <reason> <evidence-root> <output> | finalize-gate <host-log> <evidence-root> <output> <version> <preserve|preserve-failure|replace> <status> <exit> <reason> | report-gate <gate> [step-summary]');
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
