import http from "k6/http";
import { check, sleep } from "k6";
import { Counter } from "k6/metrics";

const BASE_URL = __ENV.BASE_URL || "http://localhost:8080";
const VUS = Number(__ENV.VUS || 1000);
const STEADY = __ENV.DURATION || "5m";
const PASSWORD = __ENV.LOADTEST_PASSWORD || "LoadTest123!";
const SYMBOLS = ["APXL", "NOVA", "QNTM", "HELX", "GRDX", "BRKX", "AERO", "FINX", "SOLR", "MEDI", "LUXE", "CRYP"];

const rateLimited = new Counter("rate_limited");

export const options = {
  setupTimeout: "10m",
  scenarios: {
    traders: {
      executor: "ramping-vus",
      startVUs: 0,
      stages: [
        { duration: "1m", target: VUS },
        { duration: STEADY, target: VUS },
        { duration: "30s", target: 0 },
      ],
      gracefulRampDown: "10s",
    },
  },
  thresholds: {
    http_req_failed: ["rate<0.01"],
    "http_req_duration{kind:read}": ["p(95)<200"],
    "http_req_duration{kind:order}": ["p(95)<500"],
    checks: ["rate>0.99"],
  },
  summaryTrendStats: ["avg", "med", "p(90)", "p(95)", "p(99)", "max"],
};

export function setup() {
  const users = [];
  for (let first = 1; first <= VUS; first += 50) {
    const batch = [];
    for (let i = first; i < Math.min(first + 50, VUS + 1); i++) {
      batch.push(["POST", `${BASE_URL}/api/v1/auth/login`,
        JSON.stringify({ username: `loadtest${String(i).padStart(4, "0")}`, password: PASSWORD }),
        { headers: { "Content-Type": "application/json" }, tags: { kind: "login" } }]);
    }
    for (const response of http.batch(batch)) {
      if (response.status !== 200) {
        throw new Error(`Login failed (${response.status}): ${response.body}. Were the load-test users seeded and the login limit raised?`);
      }
      const data = response.json().data;
      users.push({ token: data.accessToken, userId: data.userId });
    }
  }
  return { users };
}

export default function (data) {
  const user = data.users[(__VU - 1) % data.users.length];
  const params = (kind, name) => ({
    headers: { Authorization: `Bearer ${user.token}`, "Content-Type": "application/json" },
    tags: { kind, name },
  });
  const symbol = SYMBOLS[Math.floor(Math.random() * SYMBOLS.length)];
  const roll = Math.random();
  const responses = [];

  if (roll < 0.6) {
    const pick = Math.random();
    if (pick < 0.5) responses.push(http.get(`${BASE_URL}/api/v1/market/snapshot`, params("read", "snapshot")));
    else if (pick < 0.7) responses.push(http.get(`${BASE_URL}/api/v1/market/instruments`, params("read", "instruments")));
    else if (pick < 0.85) responses.push(http.get(`${BASE_URL}/api/v1/market/instruments/${symbol}/price-history?interval=5m&limit=78`, params("read", "price-history")));
    else responses.push(http.get(`${BASE_URL}/api/v1/market/instruments/${symbol}/orderbook?depth=10`, params("read", "orderbook")));
  } else if (roll < 0.9) {
    const pick = Math.random();
    const base = `${BASE_URL}/api/v1/portfolios/${user.userId}`;
    if (pick < 0.4) responses.push(http.get(base, params("read", "portfolio")));
    else if (pick < 0.7) responses.push(http.get(`${base}/positions`, params("read", "positions")));
    else if (pick < 0.85) responses.push(http.get(`${base}/transactions?pageSize=20`, params("read", "transactions")));
    else responses.push(http.get(`${base}/performance`, params("read", "performance")));
  } else if (Math.random() < 0.5) {
    responses.push(http.post(`${BASE_URL}/api/v1/orders`, JSON.stringify({
      userId: user.userId, instrumentSymbol: symbol === "CRYP" ? "APXL" : symbol, orderType: "MARKET", side: "BUY", quantity: 1, timeInForce: "DAY",
    }), params("order", "market-buy")));
  } else {
    const placed = http.post(`${BASE_URL}/api/v1/orders`, JSON.stringify({
      userId: user.userId, instrumentSymbol: "NOVA", orderType: "LIMIT", side: "BUY", quantity: 1, limitPrice: 1.0, timeInForce: "GTC",
    }), params("order", "limit-buy"));
    responses.push(placed);
    if (placed.status === 201) {
      responses.push(http.put(`${BASE_URL}/api/v1/orders/${placed.json().data.orderId}/cancel`, null, params("order", "cancel")));
    }
  }

  for (const response of responses) {
    check(response, { "status is 2xx": (r) => r.status >= 200 && r.status < 300 });
    if (response.status === 429) rateLimited.add(1);
  }

  sleep(1);
}
