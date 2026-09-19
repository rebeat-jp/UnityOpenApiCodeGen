'use strict';
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

const repositoryRoot = path.resolve(__dirname, '../..');
const entry = 'SourceGenerators/OpenApiMvpSupportMatrix.md';
const packageDirectories = ['Packages/OpenApiCodeGen', 'Packages/OpenApiCodeGen.SourceGenerator'];
const manifestName = 'documentation-manifest.json';
const hash = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
const escapeHtml = text => text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');

function localTarget(source, href) {
  if (/^(?:https?:|mailto:)/i.test(href) || href.startsWith('#')) return null;
  if (/^[a-z][a-z0-9+.-]*:/i.test(href) || href.startsWith('/') || href.startsWith('\\')) {
    throw new Error(`Unsupported documentation link in ${source}: ${href}`);
  }
  const pathname = decodeURIComponent(href.split(/[?#]/, 1)[0]);
  const target = path.posix.normalize(path.posix.join(path.posix.dirname(source), pathname));
  if (!pathname || target === '..' || target.startsWith('../') || target.includes('\\')) {
    throw new Error(`Documentation link leaves the documentation root: ${source}: ${href}`);
  }
  return target;
}

function createOutputs(root = repositoryRoot) {
  const { marked, Renderer } = require('../BuildTools/Documentation/node_modules/marked');
  const outputs = new Map();
  const pending = [entry];
  const sources = new Set();
  while (pending.length) {
    const source = pending.shift();
    if (sources.has(source)) continue;
    sources.add(source);
    const sourcePath = path.join(root, source);
    const realPath = fs.realpathSync(sourcePath);
    if (!realPath.startsWith(fs.realpathSync(root) + path.sep)) throw new Error(`Documentation source escapes repository: ${source}`);
    const bytes = fs.readFileSync(sourcePath);
    outputs.set(source, bytes);
    if (!source.endsWith('.md')) continue;
    const tokens = marked.lexer(bytes.toString('utf8'), { gfm: true });
    marked.walkTokens(tokens, token => {
      if (token.type !== 'link' && token.type !== 'image') return;
      const target = localTarget(source, token.href);
      if (target) {
        pending.push(target);
        if (target.endsWith('.md')) token.href = token.href.replace(/\.md(?=[?#]|$)/, '.html');
      } else if (token.type === 'image') {
        throw new Error(`Documentation images must be bundled: ${source}: ${token.href}`);
      }
    });
    const renderer = new Renderer();
    const slugs = new Map();
    renderer.html = token => escapeHtml(token.text);
    renderer.heading = function (token) {
      const slug = token.text.toLowerCase().replace(/<[^>]*>/g, '').replace(/[^\p{L}\p{N}_\-\s]/gu, '').replace(/\s/g, '-');
      const count = slugs.get(slug) || 0;
      slugs.set(slug, count + 1);
      return `<h${token.depth} id="${escapeHtml(slug + (count ? '-' + count : ''))}">${this.parser.parseInline(token.tokens)}</h${token.depth}>\n`;
    };
    const title = tokens.find(token => token.type === 'heading')?.text || path.posix.basename(source);
    const body = marked.parser(tokens, { renderer, gfm: true });
    const html = `<!doctype html>
<html lang="ja"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src 'self' data:; base-uri 'none'; form-action 'none'">
<title>${escapeHtml(title)}</title>
<style>body{font:16px/1.7 system-ui,sans-serif;color:#20242c;background:#fff;max-width:1080px;margin:0 auto;padding:32px 24px;overflow-wrap:anywhere}a{color:#0756a5}h1,h2,h3{line-height:1.3}h2{margin-top:2em}table{display:block;overflow:auto;border-collapse:collapse;margin:20px 0}th,td{border:1px solid #ccd2da;padding:9px 12px;text-align:left;vertical-align:top}th{background:#f1f4f8}pre{padding:16px;background:#f1f4f8;overflow:auto}code{font-family:ui-monospace,monospace}blockquote{border-left:4px solid #ccd2da;margin-left:0;padding-left:16px}img{max-width:100%}</style>
</head><body><main>
${body}</main></body></html>
`;
    outputs.set(source.replace(/\.md$/, '.html'), Buffer.from(html));
  }
  const manifest = {
    formatVersion: 1,
    entry: entry.replace(/\.md$/, '.html'),
    files: [...outputs].sort(([a], [b]) => a.localeCompare(b, 'en')).map(([name, bytes]) => ({ path: name, sha256: hash(bytes) }))
  };
  outputs.set(manifestName, Buffer.from(JSON.stringify(manifest, null, 2) + '\n'));
  return outputs;
}

function synchronize(root = repositoryRoot, check = false) {
  const outputs = createOutputs(root);
  for (const packageDirectory of packageDirectories) {
    const destination = path.join(root, packageDirectory, 'Documentation~');
    for (const [relative, bytes] of outputs) {
      const file = path.join(destination, relative);
      if (check) {
        if (!fs.existsSync(file) || !fs.readFileSync(file).equals(bytes)) throw new Error(`Stale packaged documentation: ${file}`);
      } else {
        fs.mkdirSync(path.dirname(file), { recursive: true });
        fs.writeFileSync(file, bytes);
      }
    }
    verifyPackage(path.join(root, packageDirectory));
  }
}

function verifyPackage(packageRoot) {
  const root = path.join(packageRoot, 'Documentation~');
  const manifest = JSON.parse(fs.readFileSync(path.join(root, manifestName), 'utf8'));
  if (manifest.formatVersion !== 1 || manifest.entry !== entry.replace(/\.md$/, '.html') || !Array.isArray(manifest.files)) {
    throw new Error('Invalid packaged documentation manifest');
  }
  const names = new Set();
  for (const file of manifest.files) {
    if (!file || typeof file.path !== 'string' || localTarget('index.md', file.path) !== file.path || names.has(file.path)) {
      throw new Error('Unsafe or duplicate packaged documentation path');
    }
    names.add(file.path);
    if (hash(fs.readFileSync(path.join(root, file.path))) !== file.sha256) throw new Error(`Packaged documentation hash mismatch: ${file.path}`);
  }
  if (!names.has(entry) || !names.has(manifest.entry)) throw new Error('Support Matrix is missing from package');
  for (const name of names) {
    if (!name.endsWith('.html')) continue;
    const html = fs.readFileSync(path.join(root, name), 'utf8');
    for (const match of html.matchAll(/(?:href|src)="([^"]*)"/g)) {
      const target = localTarget(name, match[1].replaceAll('&amp;', '&'));
      if (target && !names.has(target)) throw new Error(`Broken packaged documentation link: ${name}: ${target}`);
    }
  }
  const readme = fs.readFileSync(path.join(packageRoot, 'README.md'), 'utf8');
  if (!readme.includes(`Documentation~/${entry}`)) throw new Error('Package README must link to the bundled Support Matrix');
}

if (require.main === module) {
  const args = process.argv.slice(2);
  if (args.length === 2 && args[0] === '--verify-package') verifyPackage(path.resolve(args[1]));
  else if (args.length === 0 || (args.length === 1 && args[0] === '--check')) synchronize(repositoryRoot, args[0] === '--check');
  else throw new Error('Usage: generate-package-docs.js [--check | --verify-package <directory>]');
  console.log('Package documentation verified.');
}

module.exports = { createOutputs, localTarget, synchronize, verifyPackage };
