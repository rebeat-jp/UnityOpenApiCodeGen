#!/usr/bin/env node
'use strict';
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
const analyzer = 'Packages/OpenApiCodeGen.SourceGenerator/Runtime/Analyzers/Rhycol.OpenApiCodeGen.SourceGenerator.dll';
const analyzerName = path.basename(analyzer);

function containedPath(root, relativePath, label) {
  const rootPath = path.resolve(root);
  const rootStat = fs.lstatSync(rootPath);
  if (rootStat.isSymbolicLink() || !rootStat.isDirectory()) throw new Error(`${label} root must be a real directory`);
  const resolvedRoot = fs.realpathSync(rootPath);
  const candidate = path.resolve(rootPath, relativePath);
  const relative = path.relative(rootPath, candidate);
  if (!relative || relative === '..' || relative.startsWith(`..${path.sep}`) || path.isAbsolute(relative)) throw new Error(`${label} path escapes its root`);
  let current = rootPath;
  for (const segment of relative.split(path.sep)) {
    current = path.join(current, segment);
    const stat = fs.lstatSync(current);
    if (stat.isSymbolicLink()) throw new Error(`${label} path contains a symbolic link`);
    if (current !== candidate && !stat.isDirectory()) throw new Error(`${label} parent is not a directory`);
  }
  const realCandidate = fs.realpathSync(candidate);
  const realRelative = path.relative(resolvedRoot, realCandidate);
  if (!realRelative || realRelative === '..' || realRelative.startsWith(`..${path.sep}`) || path.isAbsolute(realRelative)) throw new Error(`${label} path escapes its real root`);
  return candidate;
}

function copyAnalyzerArtifact(artifactRoot, targetRoot) {
  const source = containedPath(artifactRoot, analyzerName, 'Analyzer artifact');
  const entries = fs.readdirSync(artifactRoot, { withFileTypes: true });
  if (entries.length !== 1 || entries[0].name !== analyzerName || !entries[0].isFile() || entries[0].isSymbolicLink()) {
    throw new Error('Analyzer artifact must contain exactly one regular DLL');
  }
  const destination = containedPath(targetRoot, analyzer, 'Analyzer destination');
  const sourceStat = fs.lstatSync(source);
  const destinationStat = fs.lstatSync(destination);
  if (!sourceStat.isFile() || sourceStat.isSymbolicLink() || !destinationStat.isFile() || destinationStat.isSymbolicLink()) {
    throw new Error('Analyzer source and destination must be regular files');
  }
  const tracked = execFileSync('git', ['ls-files', '--stage', '--', analyzer], { cwd: targetRoot, encoding: 'utf8' }).trim();
  if (!/^100644 [0-9a-f]{40,64} 0\tPackages\/OpenApiCodeGen\.SourceGenerator\/Runtime\/Analyzers\/Rhycol\.OpenApiCodeGen\.SourceGenerator\.dll$/.test(tracked)) {
    throw new Error('Analyzer destination must be one tracked, non-executable regular file');
  }

  const noFollow = fs.constants.O_NOFOLLOW || 0;
  const temporary = `${destination}.automation-${process.pid}`;
  let sourceFd;
  let destinationFd;
  try {
    sourceFd = fs.openSync(source, fs.constants.O_RDONLY | noFollow);
    if (!fs.fstatSync(sourceFd).isFile()) throw new Error('Analyzer artifact changed before copying');
    destinationFd = fs.openSync(temporary, fs.constants.O_WRONLY | fs.constants.O_CREAT | fs.constants.O_EXCL | noFollow, destinationStat.mode & 0o777);
    const buffer = Buffer.allocUnsafe(64 * 1024);
    for (;;) {
      const read = fs.readSync(sourceFd, buffer, 0, buffer.length, null);
      if (read === 0) break;
      let written = 0;
      while (written < read) written += fs.writeSync(destinationFd, buffer, written, read - written);
    }
    fs.fsyncSync(destinationFd);
    fs.closeSync(destinationFd); destinationFd = undefined;
    fs.closeSync(sourceFd); sourceFd = undefined;
    fs.renameSync(temporary, destination);
    fs.chmodSync(destination, destinationStat.mode & 0o777);
  } finally {
    if (destinationFd !== undefined) fs.closeSync(destinationFd);
    if (sourceFd !== undefined) fs.closeSync(sourceFd);
    fs.rmSync(temporary, { force: true });
  }
}

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
module.exports = { validateUpdate, copyAnalyzerArtifact, analyzer };
if (require.main === module) {
  try {
    const [command, ...args] = process.argv.slice(2);
    if (!command) update();
    else if (command === 'copy-analyzer' && args.length === 2) copyAnalyzerArtifact(args[0], args[1]);
    else throw new Error('Usage: update-analyzer-pr.js [copy-analyzer <artifact-root> <target-root>]');
  }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
