#!/usr/bin/env node
'use strict';
const { execFileSync } = require('child_process');
const analyzer = 'Packages/OpenApiCodeGen.SourceGenerator/Runtime/Analyzers/Rhycol.OpenApiCodeGen.SourceGenerator.dll';
function validateUpdate(base, changed) {
  if (!['develop', 'main'].includes(base)) throw new Error('DLL update base must be develop or main');
  if (changed.some(file => file !== analyzer)) throw new Error('DLL automation may change only the analyzer DLL; metadata and sources must remain untouched');
}
function update() {
  const base = process.env.BASE_BRANCH;
  validateUpdate(base, []);
  const git = args => execFileSync('git', args, { encoding: 'utf8' }).trim();
  const changed = git(['diff', 'HEAD', '--name-only']).split('\n').filter(Boolean);
  validateUpdate(base, changed);
  if (!changed.length) { console.log('Analyzer is already synchronized; no update PR needed.'); return; }
  if (!/^[1-9][0-9]*$/.test(process.env.GITHUB_RUN_ID || '') || !/^[1-9][0-9]*$/.test(process.env.GITHUB_RUN_ATTEMPT || '')) throw new Error('Missing GitHub run identity');
  const branch = `automation/source-generator-dll-${base}-${process.env.GITHUB_RUN_ID}-${process.env.GITHUB_RUN_ATTEMPT}`;
  git(['switch', '--create', branch]);
  git(['add', '--', analyzer]);
  git(['-c', 'user.name=github-actions[bot]', '-c', 'user.email=41898282+github-actions[bot]@users.noreply.github.com', 'commit', '-m', 'chore: synchronize Source Generator analyzer DLL']);
  const commit = git(['rev-parse', 'HEAD']);
  // Checkout does not persist credentials. Supply authentication through the
  // push process environment, so neither target builds nor error command text
  // receive/print the repository write token.
  const authentication = Buffer.from(`x-access-token:${process.env.GH_TOKEN}`).toString('base64');
  execFileSync('git', ['push', 'origin', `HEAD:refs/heads/${branch}`], { encoding: 'utf8', env: { ...process.env, GIT_CONFIG_COUNT: '1', GIT_CONFIG_KEY_0: 'http.https://github.com/.extraheader', GIT_CONFIG_VALUE_0: `AUTHORIZATION: basic ${authentication}` } });
  const gh = args => execFileSync('gh', args, { encoding: 'utf8' }).trim();
  const pr = gh(['pr', 'create', '--repo', process.env.GITHUB_REPOSITORY, '--base', base, '--head', branch,
    '--title', 'chore: synchronize Source Generator analyzer DLL', '--body', 'Updates the deterministic analyzer DLL from the existing sources. Package versions and Unity metadata are preserved. Explicit Source Generator CI is dispatched for this commit.']);
  // GITHUB_TOKEN PR creation does not trigger pull_request workflows. A workflow_dispatch does.
  gh(['workflow', 'run', 'source-generator-ci.yml', '--repo', process.env.GITHUB_REPOSITORY, '--ref', branch, '-f', `commit=${commit}`]);
  console.log(`Created ${pr}; dispatched exact-commit verification for ${commit}.`);
}
module.exports = { validateUpdate, analyzer };
if (require.main === module) {
  try { update(); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
