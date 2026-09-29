// k6 load profile for NFR-02 (API P95 <= 500 ms at 100 concurrent requests) and US-26 (2,000 users).
//
//   k6 run -e BASE_URL=https://localhost:7180 -e VUS=100 tests/load/api-load.js
//   k6 run -e BASE_URL=https://localhost:7180 -e VUS=2000 -e DURATION=10m tests/load/api-load.js   # US-26 campaign
//
// Run it while the simulator fleet is connected (see tests/load/README.md) so commands exercise real MQTT
// round trips. Users sign in once per VU; the demo seed accounts are used unless USERS is given.
import http from 'k6/http';
import { check, sleep } from 'k6';

const BASE = __ENV.BASE_URL || 'https://localhost:7180';
const PASSWORD = __ENV.PASSWORD || 'Demo@12345';
const USERS = (__ENV.USERS || 'giangvien1@nhatvuong.edu.vn,admin@nhatvuong.edu.vn,baotri@nhatvuong.edu.vn').split(',');

export const options = {
  insecureSkipTLSVerify: true,
  scenarios: {
    campus: {
      executor: 'constant-vus',
      vus: Number(__ENV.VUS || 100),
      duration: __ENV.DURATION || '2m',
    },
  },
  thresholds: {
    // NFR-02
    'http_req_duration{kind:read}': ['p(95)<500'],
    // NFR-01 budget for the whole tap-to-device round trip, measured at the API
    'http_req_duration{kind:command}': ['p(95)<3000'],
    http_req_failed: ['rate<0.01'],
  },
};

const tokens = {};

function token() {
  const email = USERS[(__VU - 1) % USERS.length];
  if (!tokens[email]) {
    const res = http.post(`${BASE}/api/v1/auth/login`, JSON.stringify({ email, password: PASSWORD }),
      { headers: { 'Content-Type': 'application/json' }, tags: { kind: 'login' } });
    check(res, { 'signed in': (r) => r.status === 200 });
    tokens[email] = res.json('accessToken');
  }
  return tokens[email];
}

export default function () {
  const headers = { Authorization: `Bearer ${token()}`, 'Content-Type': 'application/json' };

  const devices = http.get(`${BASE}/api/v1/devices`, { headers, tags: { kind: 'read' } });
  check(devices, { 'devices listed': (r) => r.status === 200 });

  const controllable = (devices.json() || []).filter((d) => d.canControlNow && d.connectivity === 'Online');
  if (controllable.length > 0 && Math.random() < 0.2) {
    const target = controllable[Math.floor(Math.random() * controllable.length)];
    const setpoint = (22 + Math.floor(Math.random() * 6)).toString();
    const res = http.post(`${BASE}/api/v1/devices/${target.id}/commands`,
      JSON.stringify({ action: 'SetTemperature', value: setpoint }), { headers, tags: { kind: 'command' } });
    check(res, { 'command answered': (r) => r.status === 200 });
  }

  http.get(`${BASE}/api/v1/notifications/unread-count`, { headers, tags: { kind: 'read' } });
  sleep(1 + Math.random() * 2);
}
