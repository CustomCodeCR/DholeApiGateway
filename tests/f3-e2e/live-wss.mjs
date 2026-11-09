// F3: real TLS/WSS through Traefik in a disposable GitHub runner.
// No real user, external QA JWT, staging URL, server access, or business writes.
// This checks the REAL Gateway process and owner REST authorization boundary.
import http from 'node:http';
import { createHmac, timingSafeEqual } from 'node:crypto';
import { writeFile, mkdir } from 'node:fs/promises';
import WebSocket from 'ws';

const RS = '\u001e';
const base = 'https://localhost:8443';
const ownerPort = 5052;
const resource = 'dddd4444-4444-4444-8444-444444444444';
const ownerPath = '/api/crm/accounts/' + resource;
const users = {
  allowedA: 'aaaa1111-1111-4111-8111-111111111111',
  allowedB: 'bbbb2222-2222-4222-8222-222222222222',
  denied: 'cccc3333-3333-4333-8333-333333333333'
};
const signingKey = process.env.F3_CI_SIGNING_KEY;
if (!signingKey || signingKey.length < 32 || process.env.GITHUB_ACTIONS !== 'true') {
  throw Error('Refusing to run F3 fixture outside isolated GitHub Actions');
}
const b64 = x => Buffer.from(JSON.stringify(x)).toString('base64url');
function issue(sub) {
  const now = Math.floor(Date.now() / 1000);
  const data = b64({ typ: 'JWT', alg: 'HS256' }) + '.' +
    b64({ iss: 'DholeAuthService', aud: 'DholeEcosystem', sub, iat: now, nbf: now - 10, exp: now + 300 });
  return data + '.' + createHmac('sha256', signingKey).update(data).digest('base64url');
}
function verifyOwnerToken(bearer) {
  if (!bearer?.startsWith('Bearer ')) return false;
  const parts = bearer.slice(7).split('.');
  if (parts.length !== 3) return false;
  const actual = Buffer.from(parts[2], 'base64url');
  const expected = createHmac('sha256', signingKey).update(parts[0] + '.' + parts[1]).digest();
  if (actual.length !== expected.length || !timingSafeEqual(actual, expected)) return false;
  let claims;
  try { claims = JSON.parse(Buffer.from(parts[1], 'base64url').toString('utf8')); }
  catch { return false; }
  return claims.iss === 'DholeAuthService' && claims.aud === 'DholeEcosystem' &&
    claims.exp > Math.floor(Date.now() / 1000) &&
    [users.allowedA, users.allowedB].includes(claims.sub);
}
async function jsonPost(path, jwt) {
  const response = await fetch(new URL(path, base), {
    method: 'POST',
    headers: jwt ? { Authorization: 'Bearer ' + jwt } : {}
  });
  const body = response.ok ? await response.json() : null;
  return { status: response.status, body };
}
function socketSubscribe(token, connectionId, origin = base) {
  const url = new URL('/hubs/realtime', base);
  url.protocol = 'wss:';
  url.searchParams.set('id', connectionId);
  url.searchParams.set('ticket', token);
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(url, { origin, handshakeTimeout: 8000 });
    let handshake = false, buffer = '', complete = false;
    const deadline = setTimeout(() => fail(new Error('Timed out while waiting for SignalR Subscribe result')), 12000);
    function fail(err) {
      if (complete) return;
      complete = true;
      clearTimeout(deadline);
      socket.terminate();
      reject(err);
    }
    socket.on('open', () => socket.send(JSON.stringify({ protocol: 'json', version: 1 }) + RS));
    socket.on('error', fail);
    socket.on('unexpected-response', (_, response) => fail(new Error('TLS WSS upgrade refused: ' + response.statusCode)));
    socket.on('close', () => { if (!complete) fail(new Error('SignalR socket closed before Subscribe result')); });
    socket.on('message', data => {
      buffer += data.toString();
      if (buffer.length > 32768) return fail(new Error('Unexpected oversize SignalR response'));
      while (buffer.includes(RS)) {
        const at = buffer.indexOf(RS);
        const frame = buffer.slice(0, at);
        buffer = buffer.slice(at + 1);
        if (!frame) continue;
        let payload;
        try { payload = JSON.parse(frame); }
        catch { return fail(new Error('Malformed SignalR frame')); }
        if (!handshake) {
          if (payload.error) return fail(new Error('SignalR handshake failed'));
          handshake = true;
          socket.send(JSON.stringify({
            type: 1, invocationId: 'f3', target: 'Subscribe', arguments: ['crm.account', resource]
          }) + RS);
        } else if (payload.type === 3 && payload.invocationId === 'f3') {
          complete = true;
          clearTimeout(deadline);
          resolve({ allowed: !payload.error, socket });
        }
      }
    });
  });
}
async function subscriber(jwt) {
  const negotiated = await jsonPost('/hubs/realtime/negotiate?negotiateVersion=1', jwt);
  if (negotiated.status !== 200) throw Error('Authenticated negotiate not 200');
  const connectionId = negotiated.body?.connectionToken ?? negotiated.body?.connectionId;
  if (!connectionId) throw Error('Missing SignalR connection token');
  const ticket = await jsonPost('/hubs/realtime/ticket', jwt);
  if (ticket.status !== 200 || !/^[a-zA-Z0-9_-]{43}$/.test(ticket.body?.ticket ?? ''))
    throw Error('Could not mint valid one-use ticket');
  return socketSubscribe(ticket.body.ticket, connectionId);
}
async function main() {
  if (process.argv[2] === 'owner') {
    http.createServer((req, res) => {
      const allowed = req.method === 'GET' && req.url === ownerPath &&
        verifyOwnerToken(req.headers.authorization);
      res.writeHead(allowed ? 200 : 403, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
      res.end('{}');
    }).listen(ownerPort, '0.0.0.0');
    return;
  }
  if (process.argv[2] !== 'verify') throw Error('Usage: node live-wss.mjs owner|verify');
  const anonymous = await jsonPost('/hubs/realtime/negotiate?negotiateVersion=1');
  if (anonymous.status !== 401) throw Error('Anonymous negotiation was not denied');
  const [a, b, denied] = await Promise.all([
    subscriber(issue(users.allowedA)), subscriber(issue(users.allowedB)), subscriber(issue(users.denied))
  ]);
  try {
    if (!a.allowed || !b.allowed || denied.allowed)
      throw Error('WSS resource authorization leaked or rejected an authorized connection');
    await mkdir('TestResults', { recursive: true });
    await writeFile('TestResults/f3-live-wss-traefik.json', JSON.stringify({
      releaseId: 'rt-20261107-01',
      scope: 'isolated GitHub Actions, not staging or production',
      tlsWssTraefikUpgrade: true, authenticatedNegotiate: true,
      twoAuthorizedSyntheticUsers: true, oneDeniedSyntheticUser: true,
      ownerRestGetCheckedOnSubscription: true, noExternalQaInputs: true,
      noRealDataOrCredentials: true, stagingAttested: false,
      formalF3Approved: false
    }, null, 2) + '\n');
    console.log('F3_LIVE_TRAEFIK_WSS_ISOLATED_PASS: TLS, JWT, 2 allowed, 1 denied, REST owner ACL');
  } finally {
    for (const x of [a, b, denied]) x.socket.close();
  }
}
main().catch(err => {
  // No JWTs, query tickets, or HTTP request details in logs.
  console.error('F3_LIVE_TRAEFIK_WSS_ISOLATED_FAILED:', err.message.replace(/Bearer\s+[^\s]+/ig, 'Bearer [REDACTED]'));
  process.exitCode = 1;
});
