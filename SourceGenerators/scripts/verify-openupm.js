#!/usr/bin/env node
'use strict';
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
const crypto = require('crypto');
const { GitHubClient } = require('./publish-release');
const { validateRelease } = require('./validate-release-artifacts');
function safeEvidenceEntries(entries, types) {
  if (types.some(type => !['-', 'd'].includes(type))) throw new Error('Evidence archive contains links or special files');
  for (const entry of entries) {
    if (!entry || entry.startsWith('/') || entry.split('/').includes('..') || !(['release-manifest.json', 'SHA256SUMS', 'unity-gate.json'].includes(entry) || entry === 'unity-gate/' || entry.startsWith('unity-gate/'))) throw new Error('Evidence archive contains an unsafe or unexpected path');
  }
}
async function prepare(client, output, tag, commit) {
  if (!/^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$/.test(tag || '') || await client.tagCommit(tag) !== commit) throw new Error('OpenUPM workflow must target the validated version tag and commit');
  const release = await client.release(tag);
  if (!release || release.draft || release.prerelease) throw new Error('A completed stable GitHub Release is required');
  const names = ['release-manifest.json', 'SHA256SUMS', `jp.rhycol.openapicodegen-${tag}.tgz`, `jp.rhycol.openapicodegen.source-generator-${tag}.tgz`, 'verification-evidence.tar.gz'];
  if (release.assets.length !== names.length || names.some(name => release.assets.filter(asset => asset.name === name).length !== 1)) throw new Error('Release assets are incomplete or unexpected');
  fs.mkdirSync(output, { recursive: true });
  for (const asset of release.assets) fs.writeFileSync(path.join(output, asset.name), await client.downloadAsset(asset));
  const originalManifest = fs.readFileSync(path.join(output, 'release-manifest.json'));
  const originalChecksums = fs.readFileSync(path.join(output, 'SHA256SUMS'));
  const archive = path.join(output, 'verification-evidence.tar.gz');
  const entries = execFileSync('tar', ['-tzf', archive], { encoding: 'utf8' }).trim().split('\n');
  const types = execFileSync('tar', ['-tvzf', archive], { encoding: 'utf8' }).trim().split('\n').map(line => line[0]);
  safeEvidenceEntries(entries, types);
  execFileSync('tar', ['-xzf', archive, '-C', output]);
  if (!originalManifest.equals(fs.readFileSync(path.join(output, 'release-manifest.json'))) || !originalChecksums.equals(fs.readFileSync(path.join(output, 'SHA256SUMS')))) throw new Error('Release metadata differs from verification evidence');
  validateRelease({ 'release-output': output, analyzer: 'Packages/OpenApiCodeGen.SourceGenerator/Runtime/Analyzers/Rhycol.OpenApiCodeGen.SourceGenerator.dll', 'expected-version': tag, 'expected-commit': commit, 'expected-node-version': `v${fs.readFileSync('.node-version', 'utf8').trim()}`, 'expected-npm-version': '11.19.0', gateMode: '--require-passed-unity-gate' });
}
async function compareRegistry(output, version, request = fetch) {
  const packageName = 'jp.rhycol.openapicodegen.source-generator';
  const response = await request(`https://package.openupm.com/${packageName}/${encodeURIComponent(version)}`);
  if (!response.ok) throw new Error(`OpenUPM registry metadata returned HTTP ${response.status}`);
  const metadata = await response.json();
  const url = new URL(metadata.dist?.tarball || '');
  if (metadata.name !== packageName || metadata.version !== version || url.protocol !== 'https:' || url.username || url.password) throw new Error('OpenUPM registry metadata does not match the release');
  const downloaded = await request(url.href);
  if (!downloaded.ok) throw new Error(`OpenUPM tarball download returned HTTP ${downloaded.status}`);
  const bytes = Buffer.from(await downloaded.arrayBuffer());
  const expected = fs.readFileSync(path.join(output, `${packageName}-${version}.tgz`));
  if (!bytes.equals(expected)) throw new Error('OpenUPM Source Generator tarball differs from the Unity-validated candidate');
  console.log(`OpenUPM ${packageName}@${version} matches candidate SHA-256 ${crypto.createHash('sha256').update(bytes).digest('hex')}.`);
}
module.exports = { prepare, safeEvidenceEntries, compareRegistry };
if (require.main === module) {
  const command = process.argv[2];
  const output = path.resolve(process.env.RELEASE_OUTPUT || 'artifacts/openupm');
  const tag = process.env.GITHUB_REF_NAME;
  Promise.resolve().then(async () => {
    if (process.env.GITHUB_REF_TYPE !== 'tag' || process.env.GITHUB_REF !== `refs/tags/${tag}`) throw new Error('OpenUPM publication requires a tag-ref workflow_dispatch');
    if (command === 'prepare') await prepare(new GitHubClient(process.env.GITHUB_REPOSITORY, process.env.GH_TOKEN), output, tag, process.env.GITHUB_SHA);
    else if (command === 'compare') await compareRegistry(output, tag);
    else throw new Error('Usage: verify-openupm.js prepare | compare');
  }).catch(error => { console.error(error.message); process.exitCode = 1; });
}
