'use strict';

// Local HTTP server of the Player gate (verify-player.js). It serves the verification API that the generated
// clients call, receives the Player report at POST /report, and can serve a WebGL build directory.
// It uses only Node core modules.

const fs = require('fs');
const http = require('http');
const path = require('path');

const mimeTypes = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.wasm': 'application/wasm',
  '.data': 'application/octet-stream',
  '.symbols': 'application/octet-stream',
  '.css': 'text/css; charset=utf-8',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
};

const cannedGadget = {
  id: 7,
  name: 'gadget-seven',
  quantity: 12,
  color: 'dark-blue',
  level: 2,
  createdAt: '2024-05-06T07:08:09Z',
  birthday: '1990-01-02',
  scores: { a: 1, b: 2 },
  tags: ['x', 'y'],
  shape: { kind: 'circle', radius: 2.5 },
};

const allowedGadgetKeys = new Set([
  'id', 'name', 'quantity', 'color', 'level', 'createdAt', 'birthday', 'scores', 'tags', 'shape',
]);

// Checks the wire format of the POST body that a generated client wrote.
function validateGadgetWire(body) {
  const problems = [];
  let gadget;
  try {
    gadget = JSON.parse(body);
  } catch (error) {
    return { valid: false, problems: [`the body is not JSON: ${error.message}`] };
  }
  const expect = (condition, message) => { if (!condition) problems.push(message); };
  expect(gadget !== null && typeof gadget === 'object' && !Array.isArray(gadget), 'the body is not an object');
  if (problems.length > 0) return { valid: false, problems };
  for (const key of Object.keys(gadget)) {
    expect(allowedGadgetKeys.has(key), `unexpected property "${key}"`);
  }
  expect(gadget.id === 42, `id is ${JSON.stringify(gadget.id)}`);
  expect(gadget.name === 'posted', `name is ${JSON.stringify(gadget.name)}`);
  expect(gadget.quantity === 5, `quantity is ${JSON.stringify(gadget.quantity)}`);
  expect(gadget.color === 'red', `color is ${JSON.stringify(gadget.color)} (the enum must use its wire value)`);
  expect(gadget.level === 3, `level is ${JSON.stringify(gadget.level)}`);
  expect(
    typeof gadget.createdAt === 'string' && Date.parse(gadget.createdAt) === Date.UTC(2025, 0, 2, 3, 4, 5),
    `createdAt is ${JSON.stringify(gadget.createdAt)}`);
  expect(gadget.birthday === '2000-12-31', `birthday is ${JSON.stringify(gadget.birthday)}`);
  expect(
    gadget.scores && typeof gadget.scores === 'object' && gadget.scores.x === 10
      && Object.keys(gadget.scores).length === 1,
    `scores is ${JSON.stringify(gadget.scores)}`);
  expect(
    Array.isArray(gadget.tags) && gadget.tags.join(',') === 'one,two,three',
    `tags is ${JSON.stringify(gadget.tags)}`);
  expect(
    gadget.shape && gadget.shape.kind === 'square' && gadget.shape.side === 3,
    `shape is ${JSON.stringify(gadget.shape)} (the discriminator and the member must be written)`);
  return { valid: problems.length === 0, problems };
}

function readBody(request) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    request.on('data', chunk => chunks.push(chunk));
    request.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    request.on('error', reject);
  });
}

function sendJson(response, status, value) {
  const body = JSON.stringify(value);
  response.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store',
    'Content-Length': Buffer.byteLength(body),
  });
  response.end(body);
}

// Starts the server on 127.0.0.1. staticRoot (optional) is served for every other GET path.
// Resolves to { origin, state, reportPromise, close } where reportPromise settles with the first POST /report body.
async function startServer({ staticRoot = null, port = 0 } = {}) {
  const state = {
    gets: 0,
    posts: 0,
    postResults: [],
    contentTypes: [],
    reportBodies: [],
    requestLog: [],
  };
  let resolveReport;
  const reportPromise = new Promise(resolve => { resolveReport = resolve; });

  const server = http.createServer(async (request, response) => {
    const url = new URL(request.url, 'http://127.0.0.1');
    state.requestLog.push(`${request.method} ${url.pathname}`);
    try {
      if (request.method === 'GET' && /^\/api\/gadgets\/7$/.test(url.pathname)) {
        state.gets += 1;
        sendJson(response, 200, cannedGadget);
        return;
      }
      if (request.method === 'POST' && url.pathname === '/api/gadgets') {
        const body = await readBody(request);
        state.posts += 1;
        state.contentTypes.push(request.headers['content-type'] || '');
        state.postResults.push(validateGadgetWire(body));
        let echoed;
        try { echoed = JSON.parse(body); } catch (error) { echoed = {}; }
        sendJson(response, 201, echoed);
        return;
      }
      if (request.method === 'POST' && url.pathname === '/report') {
        const body = await readBody(request);
        state.reportBodies.push(body);
        response.writeHead(204, { 'Cache-Control': 'no-store' });
        response.end();
        resolveReport(body);
        return;
      }
      if (request.method === 'GET' && staticRoot) {
        let relative = decodeURIComponent(url.pathname);
        if (relative.endsWith('/')) relative += 'index.html';
        const file = path.resolve(staticRoot, '.' + relative);
        if (path.relative(path.resolve(staticRoot), file).startsWith('..') || path.isAbsolute(path.relative(path.resolve(staticRoot), file)) || !fs.existsSync(file) || !fs.statSync(file).isFile()) {
          response.writeHead(404);
          response.end('not found');
          return;
        }
        response.writeHead(200, {
          'Content-Type': mimeTypes[path.extname(file).toLowerCase()] || 'application/octet-stream',
          'Content-Length': fs.statSync(file).size,
          'Cache-Control': 'no-store',
        });
        fs.createReadStream(file).pipe(response);
        return;
      }
      response.writeHead(404);
      response.end('not found');
    } catch (error) {
      response.writeHead(500);
      response.end(String(error && error.stack ? error.stack : error));
    }
  });

  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, '127.0.0.1', resolve);
  });
  const origin = `http://127.0.0.1:${server.address().port}`;
  return {
    origin,
    state,
    reportPromise,
    close: () => new Promise(resolve => {
      server.closeAllConnections?.();
      server.close(() => resolve());
    }),
  };
}

module.exports = { startServer, validateGadgetWire };

if (require.main === module) {
  const root = process.argv[2] || null;
  startServer({ staticRoot: root, port: Number(process.argv[3] || 0) }).then(server => {
    console.log(server.origin);
  });
}
