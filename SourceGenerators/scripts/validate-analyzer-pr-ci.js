#!/usr/bin/env node
'use strict';
const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFileSync } = require('child_process');
const { analyzer } = require('./update-analyzer-pr');

const shaPattern = /^[0-9a-f]{40}$/;
function insist(condition, message) { if (!condition) throw new Error(message); }
function validate({ repository, number, target, pr, run, provenance, commit, content, baseComparison, mainComparison }) {
  insist(/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repository), 'Invalid repository');
  insist(/^[1-9][0-9]*$/.test(String(number)), 'Invalid PR number');
  insist(shaPattern.test(target), 'Invalid target SHA');
  insist(pr.number === Number(number) && pr.state === 'open' && pr.draft === false && pr.merged_at === null,
    'Automation PR must be open and unmerged');
  insist(pr.user?.login === 'github-actions[bot]', 'Automation PR must be created by GitHub Actions bot');
  insist(pr.head?.repo?.full_name === repository && pr.base?.repo?.full_name === repository,
    'Automation PR must use this repository for head and base');
  insist(['develop', 'main'].includes(pr.base.ref), 'Unsupported automation PR base');
  insist(pr.head.sha === target, 'Automation PR head differs from selected target');
  const branch = pr.head.ref;
  const branchMatch = branch.match(/^automation\/source-generator-dll-(develop|main)-([1-9][0-9]*)-([1-9][0-9]*)$/);
  insist(branchMatch && branchMatch[1] === pr.base.ref, 'Invalid automation branch');
  const runId = Number(branchMatch[2]); const attempt = Number(branchMatch[3]);
  insist(run.id === runId && run.run_attempt === attempt && run.event === 'workflow_dispatch' &&
    run.head_branch === 'main' && run.path === '.github/workflows/source-generator-update-dll.yml' &&
    run.repository?.full_name === repository && run.head_repository?.full_name === repository &&
    shaPattern.test(run.head_sha), 'Automation branch does not identify a trusted DLL update run');
  insist(Date.parse(run.created_at) <= Date.parse(pr.created_at), 'Automation PR predates its source run');
  insist(provenance.repository === repository && provenance.pr === Number(number) && provenance.head === target &&
    provenance.base === pr.base.ref && provenance.branch === branch && provenance.runId === runId &&
    provenance.attempt === attempt && shaPattern.test(provenance.parent), 'Source run artifact does not match PR');
  insist(commit.sha === target && commit.parents?.length === 1 && commit.parents[0].sha === provenance.parent,
    'Automation commit must have the recorded sole parent');
  insist(commit.author?.login === 'github-actions[bot]' && commit.committer?.login === 'github-actions[bot]',
    'Automation commit must map to GitHub Actions bot');
  insist(commit.files?.length === 1 && commit.files[0].filename === analyzer && commit.files[0].status === 'modified',
    'Automation commit must modify only the analyzer DLL');
  insist(content.type === 'file' && content.path === analyzer && content.sha === commit.files[0].sha,
    'Analyzer DLL must remain a regular file at the target commit');
  insist(['ahead', 'identical'].includes(baseComparison.status) && baseComparison.merge_base_commit?.sha === provenance.parent,
    'Automation parent must be an ancestor of the allowed base');
  insist(['ahead', 'identical'].includes(mainComparison.status) && mainComparison.merge_base_commit?.sha === run.head_sha,
    'DLL update workflow commit must be on main ancestry');
  return { runId, attempt };
}

function main() {
  const repository = process.env.GITHUB_REPOSITORY;
  const number = process.env.AUTOMATION_PR;
  const target = process.env.TARGET_COMMIT;
  insist(/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repository || '') && /^[1-9][0-9]*$/.test(number || '') && shaPattern.test(target || ''),
    'Invalid automation CI inputs');
  const json = endpoint => JSON.parse(execFileSync('gh', ['api', `repos/${repository}/${endpoint}`], { encoding: 'utf8' }));
  const pr = json(`pulls/${number}`);
  const match = String(pr.head?.ref || '').match(/^automation\/source-generator-dll-(develop|main)-([1-9][0-9]*)-([1-9][0-9]*)$/);
  insist(match, 'Invalid automation branch');
  const runId = Number(match[2]); const attempt = Number(match[3]);
  const run = json(`actions/runs/${runId}`);
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'analyzer-pr-provenance-'));
  try {
    execFileSync('gh', ['run', 'download', String(runId), '-n', `analyzer-pr-provenance-${runId}-${attempt}`, '-D', directory, '-R', repository], { stdio: 'pipe' });
    const file = path.join(directory, 'analyzer-pr-provenance.json');
    insist(fs.readdirSync(directory).length === 1 && fs.lstatSync(file).isFile() && !fs.lstatSync(file).isSymbolicLink(),
      'Source run provenance artifact must contain one regular JSON file');
    const provenance = JSON.parse(fs.readFileSync(file, 'utf8'));
    insist(shaPattern.test(provenance.parent || '') && shaPattern.test(run.head_sha || ''), 'Invalid source run SHA');
    validate({ repository, number, target, pr, run, provenance,
      commit: json(`commits/${target}`),
      content: json(`contents/${analyzer}?ref=${target}`),
      baseComparison: json(`compare/${provenance.parent}...${pr.base.ref}`),
      mainComparison: json(`compare/${run.head_sha}...main`)
    });
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
  console.log(`Validated bot analyzer PR #${number} at ${target} before target code execution.`);
}
module.exports = { validate };
if (require.main === module) {
  try { main(); } catch (error) { console.error(error.message); process.exitCode = 1; }
}
