#!/usr/bin/env node
'use strict';
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
const { sha256, validateRelease } = require('./validate-release-artifacts');

class GitHubClient {
  constructor(repository, token) {
    if (!/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repository || '') || !token) throw new Error('GitHub repository/token required');
    this.repository = repository; this.token = token;
  }
  async request(method, route, body, binary = false) {
    const url = `https://api.github.com/repos/${this.repository}/${route}`;
    const response = await fetch(url, { method, headers: { Authorization: `Bearer ${this.token}`, Accept: binary ? 'application/octet-stream' : 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28', ...(body ? { 'Content-Type': 'application/json' } : {}) }, ...(body ? { body: JSON.stringify(body) } : {}) });
    if (response.status === 404 && method === 'GET') return null;
    if (!response.ok) throw new Error(`GitHub ${method} ${route} returned HTTP ${response.status}`);
    if (binary) return Buffer.from(await response.arrayBuffer());
    if (response.status === 204) return undefined;
    return response.json();
  }
  async tagCommit(tag) {
    let ref = await this.request('GET', `git/ref/tags/${encodeURIComponent(tag)}`);
    if (!ref) return null;
    let object = ref.object;
    for (let depth = 0; object.type === 'tag' && depth < 8; depth++) object = (await this.request('GET', `git/tags/${object.sha}`)).object;
    if (object.type !== 'commit') throw new Error('Release tag does not point to a commit');
    return object.sha;
  }
  async release(tag) {
    // GET releases/tags/{tag} omits drafts. Authenticated list includes drafts,
    // which are required to resume interrupted uploads without duplicate Releases.
    const matches = [];
    for (let page = 1; page <= 100; page++) {
      const releases = await this.request('GET', `releases?per_page=100&page=${page}`);
      if (!Array.isArray(releases)) throw new Error('GitHub Release list is invalid');
      matches.push(...releases.filter(release => release.tag_name === tag));
      if (releases.length < 100) {
        if (matches.length > 1) throw new Error('Multiple Releases match the tag; refusing ambiguous retry');
        return matches[0] || null;
      }
    }
    throw new Error('GitHub Release lookup exceeded pagination limit');
  }
  releaseById(id) { return this.request('GET', `releases/${id}`); }
  createTag(tag, commit) { return this.request('POST', 'git/refs', { ref: `refs/tags/${tag}`, sha: commit }); }
  createRelease(tag, commit) { return this.request('POST', 'releases', { tag_name: tag, target_commitish: commit, name: tag, body: `Validated UPM packages from ${commit}. Verification evidence and checksums are attached.`, draft: true, prerelease: false }); }
  downloadAsset(asset) { return this.request('GET', `releases/assets/${asset.id}`, undefined, true); }
  async uploadAsset(release, name, content) {
    const url = new URL(`https://uploads.github.com/repos/${this.repository}/releases/${release.id}/assets`); url.searchParams.set('name', name);
    const response = await fetch(url, { method: 'POST', headers: { Authorization: `Bearer ${this.token}`, 'Content-Type': 'application/octet-stream' }, body: content });
    if (!response.ok) throw new Error(`GitHub asset upload ${name} returned HTTP ${response.status}`);
    return response.json();
  }
  finishRelease(release) { return this.request('PATCH', `releases/${release.id}`, { draft: false }); }
  dispatchOpenUpm(tag) { return this.request('POST', 'actions/workflows/source-generator-openupm.yml/dispatches', { ref: tag }); }
}
function publicationPlan(commit, tagCommit, release, names) {
  if (tagCommit && tagCommit !== commit) throw new Error('Existing tag points to a different commit; refusing overwrite');
  if (release && !tagCommit) throw new Error('Existing Release has no matching tag');
  const existing = release?.assets || [];
  if (existing.some(asset => !names.includes(asset.name)) || new Set(existing.map(asset => asset.name)).size !== existing.length) throw new Error('Existing Release has unexpected or duplicate assets');
  const missing = names.filter(name => !existing.some(asset => asset.name === name));
  if (release && !release.draft && missing.length) throw new Error('Published Release is incomplete; refusing to modify published assets');
  return { createTag: !tagCommit, createRelease: !release, missing };
}
async function publish(client, output, manifest) {
  const names = ['release-manifest.json', 'SHA256SUMS', ...manifest.archives.map(archive => archive.file), 'verification-evidence.tar.gz'];
  let release = await client.release(manifest.tag);
  const plan = publicationPlan(manifest.provenance.commit, await client.tagCommit(manifest.tag), release, names);
  // Verify every existing asset byte-for-byte before any tag/release mutation.
  for (const asset of release?.assets || []) {
    const content = await client.downloadAsset(asset);
    if (!content || !content.equals(fs.readFileSync(path.join(output, asset.name)))) throw new Error(`Existing Release asset differs: ${asset.name}; refusing overwrite`);
  }
  if (plan.createTag) await client.createTag(manifest.tag, manifest.provenance.commit);
  if (plan.createRelease) release = await client.createRelease(manifest.tag, manifest.provenance.commit);
  for (const name of plan.missing) await client.uploadAsset(release, name, fs.readFileSync(path.join(output, name)));
  // Re-read uploaded assets. A successful HTTP upload alone is not release evidence.
  release = await client.releaseById(release.id);
  if (!release) throw new Error('Created Release disappeared before upload verification');
  publicationPlan(manifest.provenance.commit, await client.tagCommit(manifest.tag), release, names);
  if (names.some(name => !release.assets.some(asset => asset.name === name))) throw new Error('Release upload is incomplete');
  for (const asset of release.assets) {
    const content = await client.downloadAsset(asset);
    if (!content || !content.equals(fs.readFileSync(path.join(output, asset.name)))) throw new Error(`Uploaded asset differs: ${asset.name}`);
  }
  if (release.draft) await client.finishRelease(release);
  await client.dispatchOpenUpm(manifest.tag);
  console.log(`Published immutable Release ${manifest.tag}; dispatched OpenUPM verification at tag ref.`);
}
function createEvidenceArchive(output) {
  const file = path.join(output, 'verification-evidence.tar.gz');
  const tar = execFileSync('tar', ['--sort=name', '--mtime=@0', '--owner=0', '--group=0', '--numeric-owner', '-cf', '-', '-C', output, 'release-manifest.json', 'SHA256SUMS', 'unity-gate.json', 'unity-gate'], { maxBuffer: 128 * 1024 * 1024 });
  fs.writeFileSync(file, execFileSync('gzip', ['-n'], { input: tar, maxBuffer: 128 * 1024 * 1024 }));
}
async function main() {
  const output = path.resolve(process.env.RELEASE_OUTPUT);
  const manifest = validateRelease({ 'release-output': output, analyzer: 'Packages/OpenApiCodeGen.SourceGenerator/Runtime/Analyzers/Rhycol.OpenApiCodeGen.SourceGenerator.dll', 'expected-version': process.env.EXPECTED_VERSION, 'expected-node-version': `v${fs.readFileSync('.node-version', 'utf8').trim()}`, 'expected-npm-version': '11.19.0', 'expected-commit': process.env.TARGET_COMMIT, 'expected-run-id': process.env.GITHUB_RUN_ID, 'expected-run-attempt': process.env.VERIFICATION_ATTEMPT, gateMode: '--require-passed-unity-gate' });
  createEvidenceArchive(output);
  if (process.env.PUBLISH !== 'true') { console.log(`Release dry-run passed for ${manifest.tag} (${manifest.provenance.commit}); no publication performed.`); return; }
  require('./release-contract').validateContract();
  if (process.env.GITHUB_REF !== 'refs/heads/main') throw new Error('Publication workflow must run from main');
  await publish(new GitHubClient(process.env.GITHUB_REPOSITORY, process.env.GH_TOKEN), output, manifest);
}
module.exports = { GitHubClient, publicationPlan, publish, createEvidenceArchive };
if (require.main === module) main().catch(error => { console.error(error.message); process.exitCode = 1; });
