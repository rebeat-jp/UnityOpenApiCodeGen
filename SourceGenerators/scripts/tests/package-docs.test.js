'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const { createOutputs, synchronize, verifyPackage } = require('../generate-package-docs');

function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'OpenApi docs 日本語 '));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const write = (relative, contents) => {
    const file = path.join(root, relative);
    fs.mkdirSync(path.dirname(file), { recursive: true });
    fs.writeFileSync(file, contents);
  };
  write('SourceGenerators/OpenApiMvpSupportMatrix.ja.md', '# 日本語の対応表\n\n[English](OpenApiMvpSupportMatrix.md)\n');
  write('SourceGenerators/OpenApiMvpSupportMatrix.md', '# Source Generator (Beta)\n\n[日本語](OpenApiMvpSupportMatrix.ja.md)\n\n| Feature | Support |\n| --- | --- |\n| JSON | Yes |\n\n[Guide](../guide.md#日本語)\n');
  write('guide.md', '# 日本語\n\n[Return](SourceGenerators/OpenApiMvpSupportMatrix.md)\n\n[Data](data.json)\n');
  write('data.json', '{"version":1}\n');
  for (const name of ['OpenApiCodeGen', 'OpenApiCodeGen.SourceGenerator']) {
    write(`Packages/${name}/README.md`, '[Support Matrix](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.md)\n');
    write(`Packages/${name}/README.ja.md`, '[日本語対応表](Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.ja.md)\n');
  }
  return { root, write, packageRoot: path.join(root, 'Packages/OpenApiCodeGen') };
}

test('bundled documentation remains complete after moving a package outside the repository', t => {
  const f = fixture(t);
  synchronize(f.root);
  const moved = path.join(f.root, 'インストール先 with spaces');
  fs.cpSync(f.packageRoot, moved, { recursive: true });
  fs.rmSync(path.join(f.root, 'SourceGenerators'), { recursive: true });
  fs.rmSync(path.join(f.root, 'guide.md'));
  verifyPackage(moved);
  const html = fs.readFileSync(path.join(moved, 'Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.html'), 'utf8');
  assert.match(html, /<table>/);
  assert.match(fs.readFileSync(path.join(moved, 'Documentation~/SourceGenerators/OpenApiMvpSupportMatrix.ja.html'), 'utf8'), /日本語の対応表/);
  assert.match(html, /href="OpenApiMvpSupportMatrix\.ja\.html"/);
  assert.equal(decodeURI([...html.matchAll(/href="([^"]+)"/g)].map(match => match[1]).find(href => href.includes('guide.html'))), '../guide.html#日本語');
  assert.doesNotMatch(html, /<script|<link|https?:\/\/.*\.(?:js|css)/);
});

test('stale source and missing or altered bundled pages fail validation', t => {
  const f = fixture(t);
  synchronize(f.root);
  f.write('guide.md', '# Changed guide\n');
  assert.throws(() => synchronize(f.root, true), /Stale packaged documentation/);
  const page = path.join(f.packageRoot, 'Documentation~/guide.html');
  fs.writeFileSync(page, '<title>altered</title>');
  assert.throws(() => verifyPackage(f.packageRoot), /hash mismatch/);
  fs.rmSync(page);
  assert.throws(() => verifyPackage(f.packageRoot), /ENOENT/);
});

test('documentation rejects escaping links and active URL schemes and escapes raw HTML', t => {
  const f = fixture(t);
  for (const href of ['../../outside.md', 'javascript:alert%281%29', '%2e%2e/%2e%2e/outside.md']) {
    f.write('SourceGenerators/OpenApiMvpSupportMatrix.md', `[bad](${href})\n`);
    assert.throws(() => createOutputs(f.root), /Unsupported documentation link|leaves the documentation root/);
  }
  f.write('SourceGenerators/OpenApiMvpSupportMatrix.md', '# Matrix\n\n<script>alert(1)</script>\n');
  const html = createOutputs(f.root).get('SourceGenerators/OpenApiMvpSupportMatrix.html').toString();
  assert.match(html, /&lt;script&gt;/);
  assert.doesNotMatch(html, /<script>/);
});

test('both npm tarballs include independently usable current documentation', t => {
  const repository = path.resolve(__dirname, '../../..');
  synchronize(repository, true);
  const output = fs.mkdtempSync(path.join(os.tmpdir(), 'oacg-docs-pack-'));
  t.after(() => fs.rmSync(output, { recursive: true, force: true }));
  for (const name of ['OpenApiCodeGen', 'OpenApiCodeGen.SourceGenerator']) {
    const result = JSON.parse(execFileSync('npm', ['pack', '--json', '--ignore-scripts', '--pack-destination', output], {
      cwd: path.join(repository, 'Packages', name), encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']
    }));
    const extracted = path.join(output, name);
    fs.mkdirSync(extracted);
    execFileSync('tar', ['-xzf', path.join(output, result[0].filename), '-C', extracted]);
    verifyPackage(path.join(extracted, 'package'));
  }
});
