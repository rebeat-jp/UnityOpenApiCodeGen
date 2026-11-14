#!/usr/bin/env node
'use strict';

// Player gate (Issue #57): builds the Source Generator verification fixture as an IL2CPP (or Mono) Standalone
// player and as a WebGL player, runs it against a local HTTP server, and stores one result per combination.
//
//   node verify-player.js [--unity 6000.3.2f1] [--cells all|standalone|webgl|<id>,<id>...] [--output <dir>] [--work-dir <dir>]
//                         [--fresh] [--include-mono] [--list] [--prepare-only] [--phase all|build|run]
//
// --phase build builds every cell and runs the Standalone players but leaves the WebGL cells pending;
// --phase run runs the pending WebGL builds in a browser (CI runs these two phases on different machines).
//
// A cell id is <target>-<backend>-<stripping>-<transport>-<serializer>, for example
// win64-il2cpp-high-uwr-stj or webgl-il2cpp-minimal-uwr-newtonsoft.
// Environment: UNITY_EXECUTABLE (editor path), PV_BROWSER (Chrome/Edge executable for WebGL),
// PV_BUILD_TIMEOUT_SECONDS, PV_RUN_TIMEOUT_SECONDS, PV_SOURCE_GENERATOR_PACKAGE (use another copy of the Source Generator
// package, for example one with a freshly built analyzer, instead of Packages/OpenApiCodeGen.SourceGenerator).
//
// The exit code is 0 when every selected cell has the expected verdict (pass, or unsupported for the cells that
// are documented as unsupported), 1 when a cell failed and 2 when the gate could not run.

const childProcess = require('child_process');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { startServer } = require('./player-gate-server');

const scriptsDirectory = __dirname;
const sourceGeneratorsRoot = path.resolve(scriptsDirectory, '..');
const repositoryRoot = path.resolve(sourceGeneratorsRoot, '..');
const fixtureRoot = path.join(sourceGeneratorsRoot, 'BuildTools', 'UnityPlayerVerificationFixture');

const editorRevisions = {
  '6000.0.23f1': '1c4764c07fb4',
  '6000.3.2f1': 'a9779f353c9b',
};

const comboNames = {
  'httpclient-newtonsoft': 'HttpNewtonsoft',
  'httpclient-stj': 'HttpStj',
  'uwr-newtonsoft': 'UwrNewtonsoft',
  'uwr-stj': 'UwrStj',
};

class GateNotRun extends Error {}

function parseArguments(argv) {
  const options = {
    unity: '6000.3.2f1',
    cells: 'all',
    output: null,
    workDir: process.env.PV_WORK_DIR || null,
    fresh: false,
    includeMono: false,
    list: false,
    prepareOnly: false,
    skipPrepare: false,
    phase: 'all',
  };
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    const value = () => {
      index += 1;
      if (index >= argv.length) throw new GateNotRun(`${argument} needs a value`);
      return argv[index];
    };
    switch (argument) {
      case '--unity': options.unity = value(); break;
      case '--cells': options.cells = value(); break;
      case '--output': options.output = path.resolve(value()); break;
      case '--work-dir': options.workDir = path.resolve(value()); break;
      case '--fresh': options.fresh = true; break;
      case '--include-mono': options.includeMono = true; break;
      case '--list': options.list = true; break;
      case '--prepare-only': options.prepareOnly = true; break;
      case '--skip-prepare': options.skipPrepare = true; break;
      case '--phase':
        options.phase = value();
        if (!['all', 'build', 'run'].includes(options.phase)) throw new GateNotRun('--phase must be all, build or run');
        break;
      default: throw new GateNotRun(`Unknown argument: ${argument}`);
    }
  }
  return options;
}

function hostStandaloneTarget() {
  if (process.platform === 'win32') return { id: 'win64', unityBuildTarget: 'Win64' };
  if (process.platform === 'linux') return { id: 'linux64', unityBuildTarget: 'Linux64' };
  if (process.platform === 'darwin') return { id: 'osx', unityBuildTarget: 'OSXUniversal' };
  throw new GateNotRun(`Unsupported host platform ${process.platform}`);
}

function makeCell(target, backend, stripping, transport, serializer, expectation) {
  return {
    id: `${target}-${backend}-${stripping}-${transport}-${serializer}`,
    target,
    backend,
    stripping,
    transport,
    serializer,
    combo: comboNames[`${transport}-${serializer}`],
    expectation,
  };
}

function buildMatrix(options) {
  const standalone = hostStandaloneTarget().id;
  const cells = [];
  for (const stripping of ['minimal', 'high']) {
    for (const transport of ['httpclient', 'uwr']) {
      for (const serializer of ['newtonsoft', 'stj']) {
        cells.push(makeCell(standalone, 'il2cpp', stripping, transport, serializer, 'pass'));
      }
    }
  }
  for (const stripping of ['minimal', 'high']) {
    for (const serializer of ['newtonsoft', 'stj']) {
      cells.push(makeCell('webgl', 'il2cpp', stripping, 'uwr', serializer, 'pass'));
    }
  }
  // WebGL has no socket-based HttpClient. The analyzer reports OACG014; the build and the run are recorded as evidence.
  cells.push(makeCell('webgl', 'il2cpp', 'minimal', 'httpclient', 'newtonsoft', 'unsupported'));
  if (options.includeMono) {
    cells.push(makeCell(standalone, 'mono', 'minimal', 'httpclient', 'newtonsoft', 'pass'));
    cells.push(makeCell(standalone, 'mono', 'minimal', 'uwr', 'newtonsoft', 'pass'));
  }
  return cells;
}

function selectCells(options) {
  const matrix = buildMatrix(options);
  if (options.cells === 'all') return matrix;
  if (options.cells === 'standalone') return matrix.filter(cell => cell.target !== 'webgl');
  if (options.cells === 'webgl') return matrix.filter(cell => cell.target === 'webgl');
  const wanted = options.cells.split(',').map(id => id.trim()).filter(Boolean);
  return wanted.map(id => {
    const cell = matrix.find(candidate => candidate.id === id);
    if (!cell) throw new GateNotRun(`Unknown cell "${id}". Use --list to see the cell ids.`);
    return cell;
  });
}

function defaultUnityExecutable(version) {
  if (process.env.UNITY_EXECUTABLE) return process.env.UNITY_EXECUTABLE;
  if (process.platform === 'win32') {
    return `C:\\Program Files\\Unity\\Hub\\Editor\\${version}\\Editor\\Unity.exe`;
  }
  if (process.platform === 'darwin') {
    return `/Applications/Unity/Hub/Editor/${version}/Unity.app/Contents/MacOS/Unity`;
  }
  return `/opt/unity/Editor/Unity`;
}

function killTree(child) {
  if (child.pid === undefined) return;
  if (process.platform === 'win32') {
    childProcess.spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore' });
  } else {
    try { process.kill(-child.pid, 'SIGKILL'); } catch (error) { try { child.kill('SIGKILL'); } catch (ignored) { /* gone */ } }
  }
}

// Runs a command with a wall-clock limit; a timed-out command and its descendants are killed.
function runProcess(command, args, { timeoutMs, stderrFile = null, env = process.env }) {
  return new Promise(resolve => {
    const stderrDescriptor = stderrFile ? fs.openSync(stderrFile, 'w') : 'ignore';
    const child = childProcess.spawn(command, args, {
      stdio: ['ignore', stderrFile ? stderrDescriptor : 'ignore', stderrFile ? stderrDescriptor : 'ignore'],
      detached: process.platform !== 'win32',
      windowsHide: true,
      env,
    });
    let timedOut = false;
    const timer = setTimeout(() => { timedOut = true; killTree(child); }, timeoutMs);
    child.on('error', error => {
      clearTimeout(timer);
      if (stderrFile) fs.closeSync(stderrDescriptor);
      resolve({ exitCode: null, timedOut: false, error: error.message });
    });
    child.on('exit', code => {
      clearTimeout(timer);
      // A command must not leave helpers behind (the Unity batch process starts several).
      killTree(child);
      if (stderrFile) fs.closeSync(stderrDescriptor);
      resolve({ exitCode: code, timedOut });
    });
  });
}

function writeJson(file, value) {
  fs.writeFileSync(file, `${JSON.stringify(value, null, 2)}\n`);
}

function readTextIfExists(file) {
  return fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : '';
}

class UnityDriver {
  constructor({ executable, projectPath, timeoutMs }) {
    this.executable = executable;
    this.projectPath = projectPath;
    this.timeoutMs = timeoutMs;
  }

  // Runs one Unity batch launch. Returns { exitCode, timedOut }.
  async run(logFile, extraArguments) {
    fs.rmSync(logFile, { force: true });
    fs.mkdirSync(path.dirname(logFile), { recursive: true });
    return runProcess(
      this.executable,
      ['-batchmode', '-nographics', '-projectPath', this.projectPath, ...extraArguments, '-logFile', logFile],
      { timeoutMs: this.timeoutMs });
  }
}

function writeProjectFiles(projectPath, unityVersion) {
  const revision = editorRevisions[unityVersion];
  if (!revision) throw new GateNotRun(`Add the editor revision for ${unityVersion} to editorRevisions.`);
  const packageUrl = name => `file:${path.join(repositoryRoot, 'Packages', name).replace(/\\/g, '/')}`;
  fs.mkdirSync(path.join(projectPath, 'Packages'), { recursive: true });
  fs.mkdirSync(path.join(projectPath, 'ProjectSettings'), { recursive: true });
  // The fixture needs Newtonsoft.Json and System.Text.Json (UnityNuGet) because the add-on no longer installs a serializer.
  writeJson(path.join(projectPath, 'Packages', 'manifest.json'), {
    dependencies: {
      'com.unity.nuget.newtonsoft-json': '3.2.2',
      'org.nuget.system.text.json': '8.0.5',
      'com.unity.modules.unitywebrequest': '1.0.0',
      'com.unity.modules.jsonserialize': '1.0.0',
      'com.unity.modules.imgui': '1.0.0',
      'com.unity.modules.ui': '1.0.0',
      'com.unity.modules.uielements': '1.0.0',
      'jp.rhycol.openapicodegen': packageUrl('OpenApiCodeGen'),
      'jp.rhycol.openapicodegen.source-generator': process.env.PV_SOURCE_GENERATOR_PACKAGE
        ? `file:${path.resolve(process.env.PV_SOURCE_GENERATOR_PACKAGE).replace(/\\/g, '/')}`
        : packageUrl('OpenApiCodeGen.SourceGenerator'),
    },
    scopedRegistries: [
      { name: 'Unity NuGet', url: 'https://unitynuget-registry.openupm.com', scopes: ['org.nuget'] },
    ],
  });
  fs.writeFileSync(
    path.join(projectPath, 'ProjectSettings', 'ProjectVersion.txt'),
    `m_EditorVersion: ${unityVersion}\nm_EditorVersionWithRevision: ${unityVersion} (${revision})\n`);
  fs.mkdirSync(path.join(projectPath, 'Assets', 'OpenApiCodeGen'), { recursive: true });
  fs.writeFileSync(
    path.join(projectPath, 'Assets', 'OpenApiCodeGen', 'projectSettings.json'),
    '{"GenerateProvider":1}\n');
}

async function prepareProject(options, driver, projectPath, logDirectory) {
  const marker = path.join(projectPath, '.pv-prepared');
  if (options.fresh) {
    fs.rmSync(projectPath, { recursive: true, force: true });
    fs.rmSync(path.join(path.dirname(projectPath), 'builds'), { recursive: true, force: true });
  }
  fs.mkdirSync(projectPath, { recursive: true });

  // Always refresh the fixture sources; the generated clients are rewritten only when the project is prepared.
  const assetsTarget = path.join(projectPath, 'Assets', 'PlayerVerification');
  const preparing = !fs.existsSync(marker);
  writeProjectFiles(projectPath, options.unity);
  fs.cpSync(fixtureRoot, assetsTarget, {
    recursive: true,
    filter: source => !/[\\/](Library|Temp|Logs|Generated|Probe\.cs)(\.meta)?$/.test(source) && !/README\.md$/.test(source),
  });
  if (!preparing) return;

  fs.mkdirSync(logDirectory, { recursive: true });
  const step = async (name, extra) => {
    const result = await driver.run(path.join(logDirectory, `prepare-${name}.log`), extra);
    if (result.timedOut || result.exitCode !== 0) {
      throw new GateNotRun(`Unity project preparation step "${name}" failed (exit ${result.exitCode}, timedOut ${result.timedOut}); see ${path.join(logDirectory, `prepare-${name}.log`)}`);
    }
  };
  await step('select-all', ['-executeMethod', 'PlayerVerification.Editor.PlayerVerificationBuild.SelectCombos', '-pvCombos', 'HttpNewtonsoft,HttpStj,UwrNewtonsoft,UwrStj', '-quit']);
  await step('define-sync', ['-executeMethod', 'Rhycol.OpenApiCodeGen.Core.SourceGeneratorDefineSynchronization.SynchronizeActiveBuildTargetForBatchMode']);
  await step('generate', ['-executeMethod', 'PlayerVerification.Editor.PlayerVerificationGenerate.Generate', '-quit']);
  const generateLog = readTextIfExists(path.join(logDirectory, 'prepare-generate.log'));
  if (!/PlayerVerification scene saved/.test(generateLog)) {
    throw new GateNotRun('The generation step did not report success; see prepare-generate.log');
  }
  fs.writeFileSync(marker, new Date().toISOString());
}

function findBrowser() {
  const candidates = [
    process.env.PV_BROWSER,
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
    '/usr/bin/google-chrome',
    '/usr/bin/google-chrome-stable',
    '/usr/bin/chromium',
    '/usr/bin/chromium-browser',
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  ].filter(Boolean);
  return candidates.find(candidate => fs.existsSync(candidate)) || null;
}

function extractDiagnostics(buildLog) {
  const found = new Map();
  for (const line of buildLog.split(/\r?\n/)) {
    const match = /(OACG\d{3})/.exec(line);
    if (match && /(warning|error)/i.test(line)) {
      found.set(`${match[1]}:${line.trim()}`, { id: match[1], line: line.trim().slice(0, 400) });
    }
  }
  return [...found.values()];
}

function extractLogExcerpt(logFile, patterns, limit = 40) {
  const lines = readTextIfExists(logFile).split(/\r?\n/);
  return lines.filter(line => patterns.some(pattern => pattern.test(line))).slice(0, limit);
}

async function runCell(options, cell, context) {
  const cellDirectory = path.join(context.outputRoot, cell.id);
  const buildDirectory = path.join(context.workDir, 'builds', cell.id);
  const isWebGl = cell.target === 'webgl';
  const runOnly = options.phase === 'run';
  if (!runOnly) {
    fs.rmSync(cellDirectory, { recursive: true, force: true });
    fs.mkdirSync(cellDirectory, { recursive: true });
    fs.rmSync(buildDirectory, { recursive: true, force: true });
  }

  const result = runOnly ? JSON.parse(fs.readFileSync(path.join(cellDirectory, 'result.json'), 'utf8')) : {
    schemaVersion: 1,
    cell: { id: cell.id, target: cell.target, backend: cell.backend, stripping: cell.stripping, transport: cell.transport, serializer: cell.serializer },
    expectation: cell.expectation,
    unityVersion: options.unity,
    host: { platform: process.platform, arch: process.arch },
    date: new Date().toISOString(),
    build: { status: 'not-run' },
    run: { status: 'not-run' },
    verdict: 'not-run',
    reason: '',
  };
  const finish = (verdict, reason) => {
    result.verdict = verdict;
    result.reason = reason;
    writeJson(path.join(cellDirectory, 'result.json'), result);
    console.log(`[player-gate] ${cell.id}: ${verdict}${reason ? ` (${reason})` : ''}`);
    return result;
  };

  const host = hostStandaloneTarget();
  const buildTarget = isWebGl ? 'WebGL' : host.unityBuildTarget;

  if (!runOnly) {
  // The generated definition files are guarded by OPENAPI_CODEGEN_SOURCE_GENERATOR, which the base package sets
  // per build target. Synchronize it once for each target that the gate switches to.
  if (!context.syncedTargets.has(buildTarget)) {
    const sync = await context.driver.run(path.join(cellDirectory, 'define-sync.log'), [
      '-buildTarget', buildTarget,
      '-executeMethod', 'Rhycol.OpenApiCodeGen.Core.SourceGeneratorDefineSynchronization.SynchronizeActiveBuildTargetForBatchMode']);
    if (sync.timedOut || sync.exitCode !== 0) {
      return finish('not-run', `the define synchronization for ${buildTarget} failed (exit ${sync.exitCode})`);
    }
    context.syncedTargets.add(buildTarget);
  }

  // Launch A selects the client through scripting defines (they take effect on the next compilation).
  const selectLog = path.join(cellDirectory, 'select.log');
  const select = await context.driver.run(selectLog, [
    '-buildTarget', buildTarget,
    '-executeMethod', 'PlayerVerification.Editor.PlayerVerificationBuild.SelectCombos',
    '-pvCombos', cell.combo, '-quit']);
  if (select.timedOut || select.exitCode !== 0) {
    result.build = { status: 'not-run', reason: 'selecting the client failed' };
    return finish('not-run', `selecting the client failed (exit ${select.exitCode})`);
  }

  // Launch B compiles with the selected client and builds the Player.
  const buildLog = path.join(cellDirectory, 'build.log');
  const startedAt = Date.now();
  const build = await context.driver.run(buildLog, [
    '-buildTarget', buildTarget,
    '-executeMethod', 'PlayerVerification.Editor.PlayerVerificationBuild.Build',
    '-pvTarget', isWebGl ? 'webgl' : 'standalone',
    '-pvBackend', cell.backend,
    '-pvStripping', cell.stripping,
    '-pvOutput', buildDirectory,
    '-quit']);
  const buildSeconds = Math.round((Date.now() - startedAt) / 1000);
  const buildText = readTextIfExists(buildLog);
  result.build = {
    status: build.exitCode === 0 && !build.timedOut ? 'succeeded' : (build.timedOut ? 'timed-out' : 'failed'),
    exitCode: build.exitCode,
    seconds: buildSeconds,
    diagnostics: extractDiagnostics(buildText),
    errors: extractLogExcerpt(buildLog, [/error CS\d{4}/, /error:/i, /Build completed with a result of 'Failed'/, /Exception/], 25),
  };
  if (result.build.status !== 'succeeded') {
    if (cell.expectation === 'unsupported') {
      // A build that failed for another reason must not be recorded as the documented unsupported combination.
      const [verdict, reason] = verdictFor(cell, result, ['the build ' + result.build.status]);
      return finish(verdict, `the build ${result.build.status}; ${reason}`);
    }
    return finish('fail', `the build ${result.build.status}; see build.log`);
  }

  }

  // Run the Player against the verification server.
  if (isWebGl && options.phase === 'build') {
    result.run = { status: 'pending' };
    return finish('pending', 'the WebGL build is waiting for the browser run');
  }
  if (isWebGl) {
    return finish(...(await runWebGl(cell, context, result, buildDirectory, cellDirectory)));
  }
  return finish(...(await runStandalone(cell, context, result, buildDirectory, cellDirectory)));
}

function evaluate(cell, result, server, report) {
  const problems = [];
  if (!report) {
    problems.push('the Player did not report a result');
  } else {
    if (!report.passed) {
      for (const check of report.checks.filter(entry => !entry.passed)) {
        problems.push(`${check.name}: ${check.detail}`.slice(0, 600));
      }
    }
    if (report.scriptingBackend !== cell.backend) {
      problems.push(`the Player ran with the ${report.scriptingBackend} backend instead of ${cell.backend}`);
    }
  }
  if (server.gets !== 1) problems.push(`the server saw ${server.gets} GET requests instead of 1`);
  if (server.posts !== 1) problems.push(`the server saw ${server.posts} POST requests instead of 1`);
  for (const post of server.postResults) {
    if (!post.valid) problems.push(`wire format: ${post.problems.join('; ')}`);
  }
  for (const contentType of server.contentTypes) {
    if (!/^application\/json/i.test(contentType)) problems.push(`the POST Content-Type was "${contentType}"`);
  }
  return problems;
}

// The Player and the browser run code that the gate did not write; they do not need the Unity credentials.
function sanitizedEnvironment() {
  const env = { ...process.env };
  for (const name of Object.keys(env)) {
    if (/^UNITY_(PASSWORD|LICENSE|SERIAL|EMAIL)$/i.test(name) || /(PASSWORD|SECRET|TOKEN)/i.test(name)) delete env[name];
  }
  return env;
}

function summarizeServer(state) {
  return {
    gets: state.gets,
    posts: state.posts,
    postResults: state.postResults,
    contentTypes: state.contentTypes,
    requests: state.requestLog,
  };
}

function verdictFor(cell, result, problems) {
  if (cell.expectation === 'unsupported') {
    const oacg014 = (result.build.diagnostics || []).some(entry => entry.id === 'OACG014');
    if (!oacg014) {
      return ['fail', 'OACG014 was not reported for the unsupported WebGL + HttpClient combination'];
    }
    return ['unsupported',
      `OACG014 reported by the analyzer; Player run ${problems.length === 0 ? 'unexpectedly passed' : 'failed as expected'}`];
  }
  return problems.length === 0 ? ['pass', ''] : ['fail', problems.join(' | ').slice(0, 1500)];
}

async function runStandalone(cell, context, result, buildDirectory, cellDirectory) {
  const executable = path.join(
    buildDirectory,
    process.platform === 'win32' ? 'Player.exe' : process.platform === 'linux' ? 'Player.x86_64' : 'Player.app/Contents/MacOS/PlayerVerification');
  if (!fs.existsSync(executable)) {
    result.run = { status: 'not-run', reason: `the player executable is missing: ${executable}` };
    return ['fail', result.run.reason];
  }
  const server = await startServer();
  const reportPath = path.join(cellDirectory, 'player-report.json');
  const playerLog = path.join(cellDirectory, 'player.log');
  const startedAt = Date.now();
  const run = await runProcess(
    executable,
    ['-batchmode', '-nographics', '-logFile', playerLog, '-pvServer', server.origin, '-pvReport', reportPath],
    { timeoutMs: context.runTimeoutMs, env: sanitizedEnvironment() });
  const seconds = Math.round((Date.now() - startedAt) / 1000);
  await server.close();
  let report = null;
  if (fs.existsSync(reportPath)) {
    try { report = JSON.parse(fs.readFileSync(reportPath, 'utf8')); } catch (error) { /* reported below */ }
  }
  result.run = { status: run.timedOut ? 'timed-out' : 'finished', exitCode: run.exitCode, seconds, report };
  result.server = summarizeServer(server.state);
  const problems = evaluate(cell, result, result.server, report);
  if (run.timedOut) problems.unshift('the Player timed out');
  else if (report && report.passed && run.exitCode !== 0) problems.push(`the Player exited with ${run.exitCode}`);
  return verdictFor(cell, result, problems);
}

async function runWebGl(cell, context, result, buildDirectory, cellDirectory) {
  const browser = findBrowser();
  if (!browser) {
    result.run = { status: 'not-run', reason: 'no Chrome/Edge found; set PV_BROWSER' };
    return ['not-run', result.run.reason];
  }
  if (!fs.existsSync(path.join(buildDirectory, 'index.html'))) {
    result.run = { status: 'not-run', reason: 'the WebGL build has no index.html' };
    return ['fail', result.run.reason];
  }
  const server = await startServer({ staticRoot: buildDirectory });
  const profile = path.join(context.workDir, 'browser-profile', cell.id);
  fs.rmSync(profile, { recursive: true, force: true });
  const browserLog = path.join(cellDirectory, 'browser.log');
  const arguments_ = [
    '--headless=new',
    '--disable-extensions',
    '--no-first-run',
    '--no-default-browser-check',
    '--use-angle=swiftshader',
    '--enable-unsafe-swiftshader',
    '--ignore-gpu-blocklist',
    '--autoplay-policy=no-user-gesture-required',
    '--enable-logging=stderr',
    '--v=0',
    `--user-data-dir=${profile}`,
    ...(process.platform === 'linux' ? ['--no-sandbox'] : []),
    `${server.origin}/index.html`,
  ];
  const startedAt = Date.now();
  const stderrDescriptor = fs.openSync(browserLog, 'w');
  const child = childProcess.spawn(browser, arguments_, {
    stdio: ['ignore', stderrDescriptor, stderrDescriptor],
    detached: process.platform !== 'win32',
    windowsHide: true,
    env: sanitizedEnvironment(),
  });
  let launchError = null;
  child.on('error', error => { launchError = error.message; });
  let reportBody = null;
  let timedOut = false;
  let timer;
  const timeout = new Promise(resolve => { timer = setTimeout(() => { timedOut = true; resolve(null); }, context.runTimeoutMs); });
  const failedToStart = new Promise(resolve => child.on('error', () => resolve(null)));
  reportBody = await Promise.race([server.reportPromise, timeout, failedToStart]);
  clearTimeout(timer);
  // Give the browser a moment to flush its console output, then stop it.
  await new Promise(resolve => setTimeout(resolve, 1500));
  killTree(child);
  fs.closeSync(stderrDescriptor);
  await server.close();
  const seconds = Math.round((Date.now() - startedAt) / 1000);

  let report = null;
  if (reportBody) {
    fs.writeFileSync(path.join(cellDirectory, 'player-report.json'), reportBody);
    try { report = JSON.parse(reportBody); } catch (error) { /* reported below */ }
  }
  result.run = { status: timedOut ? 'timed-out' : 'finished', seconds, browser: path.basename(browser), report };
  result.server = summarizeServer(server.state);
  const consoleLines = extractLogExcerpt(browserLog, [/CONSOLE/], 60);
  result.run.consoleExcerpt = consoleLines.map(line => line.slice(0, 500));
  const problems = evaluate(cell, result, result.server, report);
  if (launchError) problems.unshift(`the browser could not be started: ${launchError}`);
  else if (timedOut) problems.unshift('the WebGL Player did not report within the time limit');
  return verdictFor(cell, result, problems);
}

function writeSummary(outputRoot, unityVersion) {
  const results = fs.readdirSync(outputRoot, { withFileTypes: true })
    .filter(entry => entry.isDirectory() && fs.existsSync(path.join(outputRoot, entry.name, 'result.json')))
    .map(entry => JSON.parse(fs.readFileSync(path.join(outputRoot, entry.name, 'result.json'), 'utf8')))
    .sort((left, right) => left.cell.id.localeCompare(right.cell.id));
  writeJson(path.join(outputRoot, 'summary.json'), {
    schemaVersion: 1,
    unityVersion,
    date: new Date().toISOString(),
    cells: results.map(entry => ({
      id: entry.cell.id,
      verdict: entry.verdict,
      buildSeconds: entry.build.seconds ?? null,
      runSeconds: entry.run.seconds ?? null,
      reason: entry.reason,
    })),
  });
  const rows = results.map(entry =>
    `| ${entry.cell.id} | ${entry.verdict} | ${entry.build.seconds ?? '-'} | ${entry.run.seconds ?? '-'} | ${entry.reason.replace(/\|/g, '/').slice(0, 160)} |`);
  fs.writeFileSync(
    path.join(outputRoot, 'summary.md'),
    ['| cell | verdict | build s | run s | note |', '| --- | --- | ---: | ---: | --- |', ...rows, ''].join('\n'));
  return results;
}

async function main() {
  const options = parseArguments(process.argv.slice(2));
  const cells = selectCells(options);
  if (options.list) {
    for (const cell of cells) console.log(cell.id);
    return 0;
  }

  const executable = defaultUnityExecutable(options.unity);
  if (options.phase !== 'run' && !fs.existsSync(executable)) {
    throw new GateNotRun(`Unity editor is not installed at ${executable}`);
  }
  const workDir = options.workDir || path.join(os.tmpdir(), `pv-${options.unity}`);
  const projectPath = path.join(workDir, 'project');
  const outputRoot = path.join(options.output || path.join(repositoryRoot, 'artifacts', 'player-gate'), options.unity);
  fs.mkdirSync(outputRoot, { recursive: true });

  const driver = new UnityDriver({
    executable,
    projectPath,
    timeoutMs: Number(process.env.PV_BUILD_TIMEOUT_SECONDS || 3600) * 1000,
  });
  const context = {
    driver,
    workDir,
    outputRoot,
    syncedTargets: new Set(),
    runTimeoutMs: Number(process.env.PV_RUN_TIMEOUT_SECONDS || 180) * 1000,
  };
  if (options.phase !== 'run') {
    await prepareProject(options, driver, projectPath, path.join(outputRoot, '_prepare'));
    if (options.prepareOnly) return 0;
  }

  // Group by build target so that the editor switches platforms as rarely as possible.
  let ordered = [...cells].sort((left, right) => (left.target === 'webgl') - (right.target === 'webgl'));
  if (options.phase === 'run') {
    const pending = cell => {
      const file = path.join(outputRoot, cell.id, 'result.json');
      return fs.existsSync(file) && JSON.parse(fs.readFileSync(file, 'utf8')).verdict === 'pending';
    };
    ordered = ordered.filter(pending);
  }
  for (const cell of ordered) {
    await runCell(options, cell, context);
    writeSummary(outputRoot, options.unity);
  }
  const results = writeSummary(outputRoot, options.unity);
  console.log(fs.readFileSync(path.join(outputRoot, 'summary.md'), 'utf8'));
  const failed = results.filter(entry => entry.verdict === 'fail' || entry.verdict === 'not-run');
  return failed.length === 0 ? 0 : (failed.some(entry => entry.verdict === 'fail') ? 1 : 2);
}

main().then(code => process.exit(code), error => {
  console.error(error instanceof GateNotRun ? `not-run: ${error.message}` : error);
  process.exit(error instanceof GateNotRun ? 2 : 1);
});
