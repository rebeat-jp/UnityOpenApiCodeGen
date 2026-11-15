#!/usr/bin/env node
'use strict';
const fs = require('fs');
const { execFileSync } = require('child_process');
// X.Y.Z or X.Y.Z-(preview|alpha|beta|rc).N (no leading zeros, N positive, no +build metadata).
const RELEASE_VERSION_PATTERN = /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-(preview|alpha|beta|rc)\.[1-9][0-9]*)?$/;
function isPrerelease(version) { return typeof version === 'string' && version.includes('-'); }
function assertVersionContract(base, addon, expected) {
  if (!RELEASE_VERSION_PATTERN.test(base.version || '')) throw new Error('Release version must be X.Y.Z or X.Y.Z-(preview|alpha|beta|rc).N');
  if (base.name !== 'jp.rhycol.openapicodegen' || addon.name !== 'jp.rhycol.openapicodegen.source-generator' || addon.version !== base.version || addon.dependencies?.[base.name] !== base.version || (expected && base.version !== expected)) throw new Error('Package versions, dependency version, or requested version mismatch');
  return base.version;
}
function validateContract() {
  const commit = process.env.SOURCE_GENERATOR_CI_COMMIT || process.env.TARGET_COMMIT;
  if (!/^[0-9a-f]{40}$/.test(commit || '')) throw new Error('Target must be an exact 40-character lowercase commit SHA');
  const git = args => execFileSync('git', args, { encoding: 'utf8' }).trim();
  if (git(['rev-parse', 'HEAD']) !== commit || git(['status', '--porcelain'])) throw new Error('Release checkout must be clean and match the target commit');
  assertVersionContract(JSON.parse(fs.readFileSync('Packages/OpenApiCodeGen/package.json')), JSON.parse(fs.readFileSync('Packages/OpenApiCodeGen.SourceGenerator/package.json')), process.env.EXPECTED_VERSION);
  if (process.env.PUBLISH === 'true') {
    try { execFileSync('git', ['merge-base', '--is-ancestor', commit, 'origin/main'], { stdio: 'ignore' }); }
    catch { throw new Error('Publication target must be contained in origin/main'); }
  }
}
module.exports = { assertVersionContract, validateContract, RELEASE_VERSION_PATTERN, isPrerelease };
if (require.main === module) {
  try { validateContract(); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
