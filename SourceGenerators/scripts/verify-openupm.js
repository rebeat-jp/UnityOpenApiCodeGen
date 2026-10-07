#!/usr/bin/env node
'use strict';
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
const crypto = require('crypto');
const zlib = require('zlib');
const { isDeepStrictEqual } = require('node:util');
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
function archiveFiles(bytes) {
  const archive = zlib.gunzipSync(bytes, { maxOutputLength: 512 * 1024 * 1024 });
  const files = new Map();
  const paths = new Map();
  let rootSeen = false;
  const decode = field => new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(field.subarray(0, field.indexOf(0) < 0 ? field.length : field.indexOf(0)));
  const octal = field => {
    const value = decode(field).trim();
    if (!/^[0-7]+$/.test(value)) throw new Error('Package archive has an invalid tar header');
    const number = parseInt(value, 8);
    if (!Number.isSafeInteger(number)) throw new Error('Package archive has an invalid tar size');
    return number;
  };
  let offset = 0;
  let ended = false;
  while (offset + 512 <= archive.length) {
    const header = archive.subarray(offset, offset + 512);
    if (header.every(byte => byte === 0)) { ended = true; break; }
    const checksum = octal(header.subarray(148, 156));
    let calculated = 0;
    for (let index = 0; index < 512; index += 1) calculated += index >= 148 && index < 156 ? 32 : header[index];
    if (checksum !== calculated) throw new Error('Package archive has an invalid tar checksum');
    if (!header.subarray(257, 263).equals(Buffer.from('ustar\0')) || !header.subarray(263, 265).equals(Buffer.from('00'))) throw new Error('Package archive has an unsupported tar format');
    const name = decode(header.subarray(0, 100));
    const prefix = decode(header.subarray(345, 500));
    const entry = prefix ? `${prefix}/${name}` : name;
    const type = header[156];
    const directory = type === 53;
    if (type !== 0 && type !== 48 && !directory) throw new Error('Package archive contains a link, special file, or unsupported tar extension');
    const relative = entry.startsWith('package/') ? entry.slice(8).replace(/\/$/, '') : '';
    if (entry !== 'package/' && (!relative || relative.split('/').some(part => part === '.' || part === '..' || part === '')) || relative.includes('\\') || entry.includes('\uFEFF') || /[\x00-\x1f\x7f]/.test(entry) || entry.startsWith('/') || !directory && entry.endsWith('/')) throw new Error('Package archive contains an unsafe path');
    if (entry === 'package/') {
      if (!directory || rootSeen) throw new Error('Package archive contains an invalid or duplicate package root');
      rootSeen = true;
    } else {
      if (paths.has(relative)) throw new Error(`Package archive contains a duplicate path: ${relative}`);
      paths.set(relative, directory ? 'directory' : 'file');
    }
    const size = octal(header.subarray(124, 136));
    const start = offset + 512;
    const end = start + size;
    if (end > archive.length || directory && size !== 0) throw new Error('Package archive has a truncated or invalid entry');
    if (entry !== 'package/') files.set(relative, directory ? null : archive.subarray(start, end));
    offset = start + Math.ceil(size / 512) * 512;
  }
  if (!ended || archive.length - offset < 1024 || archive.subarray(offset).some(byte => byte !== 0)) throw new Error('Package archive is truncated or has trailing data');
  for (const relative of paths.keys()) {
    const parts = relative.split('/');
    for (let index = 1; index < parts.length; index += 1) {
      if (paths.get(parts.slice(0, index).join('/')) === 'file') throw new Error('Package archive has a file used as a directory');
    }
  }
  if (!Buffer.isBuffer(files.get('package.json'))) throw new Error('Package archive is missing package.json');
  return files;
}
function comparableManifest(bytes, published, commit) {
  const manifest = JSON.parse(bytes.toString('utf8'));
  if (!manifest || typeof manifest !== 'object' || Array.isArray(manifest)) throw new Error('Package manifest must be an object');
  if (Object.hasOwn(manifest, 'dependencies') && (!manifest.dependencies || typeof manifest.dependencies !== 'object' || Array.isArray(manifest.dependencies))) throw new Error('Package manifest dependencies must be an object');
  if (published) {
    if (!manifest.repository || typeof manifest.repository !== 'object' || Array.isArray(manifest.repository) || manifest.repository.type !== 'git' || manifest.repository.url !== 'https://github.com/rebeat-jp/UnityOpenApiCodeGen' || manifest.repository.revision !== commit) throw new Error('OpenUPM base repository metadata is invalid');
    if (!manifest.publishConfig || typeof manifest.publishConfig !== 'object' || Array.isArray(manifest.publishConfig) || manifest.publishConfig.registry !== 'https://package.openupm.com') throw new Error('OpenUPM base publishConfig.registry is invalid');
  }
  const result = structuredClone(manifest);
  if (result.repository && typeof result.repository === 'object') { delete result.repository.url; delete result.repository.revision; }
  if (result.publishConfig && typeof result.publishConfig === 'object') {
    delete result.publishConfig.registry;
    if (Object.keys(result.publishConfig).length === 0) delete result.publishConfig;
  }
  return result;
}
function compareBaseArchives(candidate, published, commit) {
  const expected = archiveFiles(candidate);
  const actual = archiveFiles(published);
  if (expected.size !== actual.size || [...expected.keys()].some(name => !actual.has(name))) throw new Error('OpenUPM base package file tree differs from the Unity-validated candidate');
  for (const [name, bytes] of expected) {
    if (name === 'package.json') {
      const candidateManifest = comparableManifest(bytes, false);
      const publishedManifest = comparableManifest(actual.get(name), true, commit);
      if (Object.hasOwn(candidateManifest, 'dependencies') && Object.keys(candidateManifest.dependencies).length === 0 && !Object.hasOwn(publishedManifest, 'dependencies')) delete candidateManifest.dependencies;
      if (!isDeepStrictEqual(candidateManifest, publishedManifest)) throw new Error('OpenUPM base package.json has unexpected differences');
    } else if (bytes === null ? actual.get(name) !== null : !Buffer.isBuffer(actual.get(name)) || !bytes.equals(actual.get(name))) throw new Error(`OpenUPM base package content differs from the Unity-validated candidate: ${name}`);
  }
}
async function compareRegistryPackage(output, version, packageName, commit, request = fetch) {
  const response = await request(`https://package.openupm.com/${packageName}/${encodeURIComponent(version)}`);
  if (!response.ok) throw new Error(`OpenUPM registry metadata returned HTTP ${response.status}`);
  const metadata = await response.json();
  let url;
  try { url = new URL(metadata?.dist?.tarball); } catch { throw new Error('OpenUPM registry metadata has an invalid tarball URL'); }
  if (metadata.name !== packageName || metadata.version !== version || url.protocol !== 'https:' || url.username || url.password) throw new Error('OpenUPM registry metadata does not match the release');
  const downloaded = await request(url.href);
  if (!downloaded.ok) throw new Error(`OpenUPM tarball download returned HTTP ${downloaded.status}`);
  const bytes = Buffer.from(await downloaded.arrayBuffer());
  const expected = fs.readFileSync(path.join(output, `${packageName}-${version}.tgz`));
  if (packageName === 'jp.rhycol.openapicodegen') compareBaseArchives(expected, bytes, commit);
  else if (!bytes.equals(expected)) throw new Error('OpenUPM Source Generator tarball differs from the Unity-validated candidate');
  console.log(`OpenUPM ${packageName}@${version} matches Unity-validated candidate; registry SHA-256 ${crypto.createHash('sha256').update(bytes).digest('hex')}.`);
}
async function compareRegistry(output, version, request = fetch) {
  const manifest = JSON.parse(fs.readFileSync(path.join(output, 'release-manifest.json'), 'utf8'));
  const commit = manifest.provenance?.commit;
  if (manifest.version !== version || !/^[0-9a-f]{40}$/.test(commit || '')) throw new Error('Validated Release manifest does not match the OpenUPM version or commit');
  await compareRegistryPackage(output, version, 'jp.rhycol.openapicodegen', commit, request);
  await compareRegistryPackage(output, version, 'jp.rhycol.openapicodegen.source-generator', commit, request);
}
module.exports = { prepare, safeEvidenceEntries, compareRegistry, compareBaseArchives, archiveFiles };
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
