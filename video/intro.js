// Intro video drawn on a <canvas> with plain JavaScript.
//
// How it works:
//   1. scenes.js lists the scenes and the narration.
//   2. load() reads audio/manifest.json (written by tts-gemini.mjs), decodes each WAV and
//      builds a timeline: every scene lasts as long as its voice clip (or `seconds`).
//   3. play() schedules all clips on the Web Audio clock, then every animation frame asks
//      "how many seconds since start?" and redraws that instant. Because pictures and
//      sound read the same clock they cannot drift apart.
//   4. record() does the same while a MediaRecorder captures canvas + audio into a .webm.
//
// Preview a single frame without playing: index.html?scene=architecture&t=4

import { scenes } from "./scenes.js";

const W = 1920;
const H = 1080;
const FONT = '"Be Vietnam Pro", system-ui, sans-serif';
const LEAD = 0.3; // silence before the voice starts in each scene
const TAIL = 0.6; // silence after it ends
const FADE = 0.4; // cross-fade between scenes

const C = {
  bg1: "#0b1020",
  bg2: "#132a5e",
  text: "#eef3ff",
  muted: "#9fb0d0",
  card: "#18223d",
  line: "#2c3a60",
  blue: "#4c8dff",
  cyan: "#5ee0ff",
  green: "#3ddc97",
  red: "#ff5c6c",
  amber: "#ffb547",
};

const canvas = document.getElementById("stage");
const ctx = canvas.getContext("2d");
const ui = {
  play: document.getElementById("play"),
  record: document.getElementById("record"),
  subs: document.getElementById("subs"),
  status: document.getElementById("status"),
};

// ---------------------------------------------------------------- drawing helpers

const clamp = (x, a = 0, b = 1) => Math.min(b, Math.max(a, x));
const lerp = (a, b, t) => a + (b - a) * t;
const easeOut = (x) => 1 - Math.pow(1 - clamp(x), 3);
// 0 before `at`, eases to 1 over `dur` seconds: the building block of every animation.
const appear = (local, at, dur = 0.6) => easeOut((local - at) / dur);

function text(str, x, y, { size = 48, weight = 600, color = C.text, align = "center", alpha = 1 } = {}) {
  ctx.save();
  ctx.globalAlpha *= alpha;
  ctx.font = `${weight} ${size}px ${FONT}`;
  ctx.fillStyle = color;
  ctx.textAlign = align;
  ctx.textBaseline = "middle";
  ctx.fillText(str, x, y);
  ctx.restore();
}

function rrect(x, y, w, h, r, fill, stroke) {
  ctx.beginPath();
  ctx.roundRect(x, y, w, h, r);
  if (fill) { ctx.fillStyle = fill; ctx.fill(); }
  if (stroke) { ctx.strokeStyle = stroke; ctx.lineWidth = 3; ctx.stroke(); }
}

function withAlpha(alpha, draw) {
  ctx.save();
  ctx.globalAlpha *= clamp(alpha);
  draw();
  ctx.restore();
}

// Wall-mounted air conditioner; `on` adds the green LED and animated airflow.
function acUnit(cx, cy, w, on, time) {
  const h = w * 0.3;
  const x = cx - w / 2;
  const y = cy - h / 2;
  rrect(x, y, w, h, h * 0.25, "#f2f5fb");
  rrect(x + w * 0.06, y + h * 0.68, w * 0.88, h * 0.1, h * 0.05, "#c9d2e3");
  ctx.beginPath();
  ctx.arc(x + w * 0.9, y + h * 0.3, h * 0.07, 0, Math.PI * 2);
  ctx.fillStyle = on ? C.green : "#9aa4b8";
  ctx.fill();
  if (!on) return;
  ctx.save();
  ctx.strokeStyle = C.cyan;
  ctx.lineWidth = Math.max(2, w * 0.012);
  ctx.lineCap = "round";
  for (let i = 0; i < 4; i++) {
    const phase = (time * 0.8 + i * 0.25) % 1;
    ctx.globalAlpha = 0.8 * (1 - phase);
    const lx = x + w * (0.2 + i * 0.2);
    const ly = y + h + 10 + phase * h * 0.9;
    ctx.beginPath();
    ctx.moveTo(lx - w * 0.06, ly);
    ctx.quadraticCurveTo(lx, ly + h * 0.15, lx + w * 0.06, ly);
    ctx.stroke();
  }
  ctx.restore();
}

function phone(x, y, w, h, drawScreen) {
  rrect(x, y, w, h, w * 0.12, "#0e1426", "#3a4a78");
  const pad = w * 0.06;
  ctx.save();
  ctx.beginPath();
  ctx.roundRect(x + pad, y + pad * 1.6, w - pad * 2, h - pad * 3.2, w * 0.06);
  ctx.clip();
  ctx.fillStyle = "#16213f";
  ctx.fill();
  drawScreen(x + pad, y + pad * 1.6, w - pad * 2, h - pad * 3.2);
  ctx.restore();
}

function check(x, y, size, color, progress = 1) {
  ctx.save();
  ctx.strokeStyle = color;
  ctx.lineWidth = size * 0.18;
  ctx.lineCap = "round";
  ctx.lineJoin = "round";
  ctx.beginPath();
  ctx.moveTo(x - size * 0.45, y);
  const p = clamp(progress * 2);
  ctx.lineTo(lerp(x - size * 0.45, x - size * 0.1, p), lerp(y, y + size * 0.35, p));
  if (progress > 0.5) {
    const q = clamp((progress - 0.5) * 2);
    ctx.lineTo(lerp(x - size * 0.1, x + size * 0.5, q), lerp(y + size * 0.35, y - size * 0.4, q));
  }
  ctx.stroke();
  ctx.restore();
}

function chip(label, cx, cy, color, alpha) {
  withAlpha(alpha, () => {
    ctx.font = `600 36px ${FONT}`;
    const w = ctx.measureText(label).width + 64;
    rrect(cx - w / 2, cy - 34 + (1 - alpha) * 20, w, 68, 34, C.card, color);
    text(label, cx, cy + (1 - alpha) * 20, { size: 36, color });
  });
}

// Chips laid out side by side, centred, each fading in `step` seconds after the previous one.
function chipRow(items, cy, local, at, step = 0.3) {
  ctx.font = `600 36px ${FONT}`;
  const widths = items.map(([label]) => ctx.measureText(label).width + 64);
  const gap = 40;
  let x = W / 2 - (widths.reduce((a, b) => a + b, 0) + gap * (items.length - 1)) / 2;
  items.forEach(([label, color], i) => {
    chip(label, x + widths[i] / 2, cy, color, appear(local, at + i * step));
    x += widths[i] + gap;
  });
}

// A dot travelling along a straight line between `from` and `to` seconds.
function packet(x1, y1, x2, y2, local, from, to, color) {
  if (local < from || local > to) return;
  const t = easeOut((local - from) / (to - from));
  ctx.beginPath();
  ctx.arc(lerp(x1, x2, t), lerp(y1, y2, t), 16, 0, Math.PI * 2);
  ctx.fillStyle = color;
  ctx.shadowColor = color;
  ctx.shadowBlur = 30;
  ctx.fill();
  ctx.shadowBlur = 0;
}

function title(str, local, y = 110) {
  const a = appear(local, 0);
  text(str, W / 2, y + (1 - a) * 30, { size: 64, weight: 800, alpha: a });
}

// ---------------------------------------------------------------- scenes
// Each draw function gets `local` (seconds since the scene started), the scene
// duration `d`, and `time` (seconds since the video started, for looping motion).

const draw = {
  intro(local, d, time) {
    for (let i = 0; i < 70; i++) {
      const x = (i * 137.5) % W;
      const y = ((i * 97 + time * (40 + (i % 5) * 20)) % (H + 60)) - 30;
      ctx.beginPath();
      ctx.arc(x, y, 2 + (i % 3), 0, Math.PI * 2);
      ctx.fillStyle = `rgba(190, 230, 255, ${0.15 + (i % 4) * 0.08})`;
      ctx.fill();
    }
    const a = appear(local, 0, 0.8);
    withAlpha(a, () => acUnit(W / 2, 320 - (1 - a) * 40, 560, local > 0.6, time));
    const t = appear(local, 0.6);
    text("Nhat Vuong Controller", W / 2, 600 + (1 - t) * 40, { size: 120, weight: 800, alpha: t });
    const s = appear(local, 1.2);
    text("Điều khiển điều hòa cho toàn trường", W / 2, 720, { size: 52, color: C.muted, alpha: s });
    chipRow([["Server .NET 10", C.blue], ["Ứng dụng MAUI", C.cyan], ["MQTT", C.green]], 840, local, 2);
  },

  problem(local, d, time) {
    title("Hết giờ học, máy lạnh vẫn chạy", local);
    const minutes = 17 * 60 + Math.floor(clamp(local / d) * 285);
    const clock = `${String(Math.floor(minutes / 60)).padStart(2, "0")}:${String(minutes % 60).padStart(2, "0")}`;
    rrect(W - 300, 60, 220, 90, 45, C.card, C.amber);
    text(clock, W - 190, 106, { size: 52, weight: 800, color: C.amber });

    for (let i = 0; i < 8; i++) {
      const col = i % 4;
      const row = Math.floor(i / 4);
      const x = 180 + col * 400;
      const y = 200 + row * 310;
      const a = appear(local, 0.4 + i * 0.2);
      withAlpha(a, () => {
        rrect(x, y + (1 - a) * 30, 360, 280, 24, C.card, C.red);
        text(`A10${i + 1}`, x + 30, y + 44, { size: 36, weight: 800, align: "left" });
        text("Phòng trống", x + 330, y + 44, { size: 24, color: C.muted, align: "right" });
        acUnit(x + 180, y + 140, 240, true, time + i * 0.3);
        text("ĐANG CHẠY", x + 180, y + 240, { size: 30, weight: 800, color: C.red });
      });
    }

    const m = appear(local, 1.5);
    withAlpha(m, () => {
      const kwh = Math.round(clamp((local - 1.5) / (d - 2)) * 1840);
      text("Điện năng lãng phí", 180, 850, { size: 36, align: "left", color: C.muted });
      rrect(600, 830, 900, 40, 20, C.card);
      rrect(600, 830, Math.max(40, 900 * (kwh / 1840)), 40, 20, C.red);
      text(`${kwh} kWh`, 1740, 850, { size: 40, weight: 800, color: C.red, align: "right" });
    });
  },

  timetable(local, d, time) {
    const k = d / 9;
    title("Quyền điều khiển đến từ thời khóa biểu", local);

    // timetable grid
    const gx = 140, gy = 200, cw = 170, ch = 130;
    const days = ["T2", "T3", "T4", "T5", "T6"];
    const periods = ["07:30", "09:30", "13:00", "15:00"];
    const others = [[0, 1], [2, 0], [3, 2], [4, 3], [1, 3], [0, 2]];
    withAlpha(appear(local, 0.2), () => {
      days.forEach((day, c) => text(day, gx + 120 + c * cw + cw / 2, gy, { size: 34, color: C.muted }));
      periods.forEach((p, r) => {
        text(p, gx + 50, gy + 60 + r * ch + ch / 2, { size: 30, color: C.muted });
        days.forEach((_, c) => rrect(gx + 120 + c * cw + 6, gy + 60 + r * ch + 6, cw - 12, ch - 12, 14, C.card));
      });
      others.forEach(([c, r]) =>
        rrect(gx + 120 + c * cw + 6, gy + 60 + r * ch + 6, cw - 12, ch - 12, 14, "#24345e"));
    });
    const hl = appear(local, 1 * k);
    const hx = gx + 120 + cw + 6, hy = gy + 66;
    withAlpha(hl, () => {
      ctx.shadowColor = C.blue;
      ctx.shadowBlur = 20 + 15 * Math.sin(time * 4);
      rrect(hx, hy, cw - 12, ch - 12, 14, C.blue);
      ctx.shadowBlur = 0;
      text("A101", hx + (cw - 12) / 2, hy + 40, { size: 34, weight: 800 });
      text("CTDL", hx + (cw - 12) / 2, hy + 80, { size: 28 });
    });

    // dashed link from the class to the phone
    const link = appear(local, 1.8 * k);
    if (link > 0) {
      ctx.save();
      ctx.strokeStyle = C.blue;
      ctx.lineWidth = 5;
      ctx.setLineDash([18, 14]);
      ctx.lineDashOffset = -time * 60;
      ctx.beginPath();
      ctx.moveTo(hx + cw - 12, hy + 60);
      ctx.lineTo(lerp(hx + cw - 12, 1290, link), lerp(hy + 60, 500, link));
      ctx.stroke();
      ctx.restore();
    }

    // phone with the power button
    const on = local > 4 * k;
    const pa = appear(local, 0.6 * k);
    withAlpha(pa, () => phone(1290, 170 + (1 - pa) * 40, 420, 660, (x, y, w, h) => {
      text("Phòng A101", x + w / 2, y + 70, { size: 44, weight: 800 });
      text("Lớp CTDL · 07:30–09:30", x + w / 2, y + 125, { size: 28, color: C.green, alpha: appear(local, 2.2 * k) });
      const bx = x + w / 2, by = y + h * 0.52, r = 110;
      if (on) {
        for (let i = 0; i < 3; i++) {
          const ph = ((local - 4 * k) * 0.7 + i / 3) % 1;
          ctx.beginPath();
          ctx.arc(bx, by, r + ph * 120, 0, Math.PI * 2);
          ctx.strokeStyle = `rgba(61, 220, 151, ${0.6 * (1 - ph)})`;
          ctx.lineWidth = 4;
          ctx.stroke();
        }
      }
      ctx.beginPath();
      ctx.arc(bx, by, r, 0, Math.PI * 2);
      ctx.fillStyle = on ? C.green : "#2a3658";
      ctx.fill();
      ctx.strokeStyle = C.text;
      ctx.lineWidth = 12;
      ctx.lineCap = "round";
      ctx.beginPath();
      ctx.arc(bx, by + 6, 48, -Math.PI * 0.3, Math.PI * 1.3);
      ctx.stroke();
      ctx.beginPath();
      ctx.moveTo(bx, by - 60);
      ctx.lineTo(bx, by - 5);
      ctx.stroke();
      text(on ? "Đang bật · 24°C" : "Đang tắt", x + w / 2, y + h * 0.82, { size: 36, weight: 800, color: on ? C.green : C.muted });
    }));
  },

  architecture(local, d, time) {
    const k = d / 9;
    title("Một lệnh đi qua hệ thống như thế nào?", local);
    const y = 440;
    const nodes = [
      { x: 330, name: "Ứng dụng", sub: "Android · Windows" },
      { x: 960, name: "Máy chủ", sub: "ASP.NET Core + MQTT" },
      { x: 1590, name: "Mô-đun máy lạnh", sub: "trên từng máy" },
    ];
    const deviceOn = local > 5.2 * k;

    // links
    withAlpha(appear(local, 0.4), () => {
      ctx.strokeStyle = C.line;
      ctx.lineWidth = 6;
      ctx.beginPath();
      ctx.moveTo(520, y); ctx.lineTo(770, y);
      ctx.moveTo(1150, y); ctx.lineTo(1400, y);
      ctx.stroke();
      text("HTTPS", 645, y - 40, { size: 30, color: C.muted });
      text("MQTT", 1275, y - 40, { size: 30, color: C.muted });
    });

    nodes.forEach((n, i) => {
      const a = appear(local, 0.2 + i * 0.2);
      withAlpha(a, () => {
        rrect(n.x - 190, y - 150 + (1 - a) * 30, 380, 300, 28, C.card, i === 1 ? C.blue : C.line);
        if (i === 0) phone(n.x - 45, y - 115, 90, 150, () => {});
        if (i === 1) for (let s = 0; s < 3; s++) {
          rrect(n.x - 80, y - 110 + s * 50, 160, 38, 8, "#24345e");
          ctx.beginPath();
          ctx.arc(n.x + 55, y - 91 + s * 50, 7, 0, Math.PI * 2);
          ctx.fillStyle = C.green;
          ctx.fill();
        }
        if (i === 2) acUnit(n.x, y - 60, 250, deviceOn, time);
        text(n.name, n.x, y + 70, { size: 38, weight: 800 });
        text(n.sub, n.x, y + 115, { size: 26, color: C.muted });
      });
    });

    // command out (blue), then the device's true state back (green)
    packet(520, y, 770, y, local, 1 * k, 2.4 * k, C.blue);
    packet(1150, y, 1400, y, local, 4 * k, 5.2 * k, C.blue);
    packet(1400, y, 1150, y, local, 5.8 * k, 6.6 * k, C.green);
    packet(770, y, 520, y, local, 6.6 * k, 7.4 * k, C.green);
    if (local > 7.4 * k) chip("Trạng thái thật: đang bật", 330, 680, C.green, appear(local, 7.4 * k));

    ["Kiểm tra quyền", "Ghi nhật ký", "Gửi lệnh"].forEach((label, i) => {
      const at = (2.5 + i * 0.5) * k;
      const a = appear(local, at, 0.4);
      withAlpha(a, () => {
        check(870, 680 + i * 70, 36, C.green, clamp((local - at) / 0.4));
        text(label, 910, 680 + i * 70, { size: 34, align: "left" });
      });
    });
  },

  autooff(local, d, time) {
    const k = d / 8;
    title("Lớp cuối kết thúc → điều hòa tự tắt", local);

    // clock running from 16:52 to 17:00
    const cx = 480, cy = 500, r = 230;
    const minute = 52 + 8 * easeOut(local / (3 * k));
    ctx.beginPath();
    ctx.arc(cx, cy, r, 0, Math.PI * 2);
    ctx.fillStyle = C.card;
    ctx.fill();
    ctx.strokeStyle = C.line;
    ctx.lineWidth = 8;
    ctx.stroke();
    for (let i = 0; i < 12; i++) {
      const ang = (i / 12) * Math.PI * 2;
      ctx.beginPath();
      ctx.moveTo(cx + Math.sin(ang) * r * 0.82, cy - Math.cos(ang) * r * 0.82);
      ctx.lineTo(cx + Math.sin(ang) * r * 0.92, cy - Math.cos(ang) * r * 0.92);
      ctx.strokeStyle = C.muted;
      ctx.lineWidth = 6;
      ctx.stroke();
    }
    const hand = (angle, len, width, color) => {
      ctx.beginPath();
      ctx.moveTo(cx, cy);
      ctx.lineTo(cx + Math.sin(angle) * len, cy - Math.cos(angle) * len);
      ctx.strokeStyle = color;
      ctx.lineWidth = width;
      ctx.lineCap = "round";
      ctx.stroke();
    };
    hand(((4 + minute / 60) / 12) * Math.PI * 2, r * 0.5, 14, C.text);
    hand((minute / 60) * Math.PI * 2, r * 0.75, 8, C.amber);
    const ended = appear(local, 3 * k);
    text("17:00 — hết giờ học", cx, cy + r + 60, { size: 40, weight: 800, color: C.amber, alpha: ended });

    ["A101", "A102", "A103", "A104"].forEach((room, i) => {
      const x = 1030 + (i % 2) * 500;
      const y = 330 + Math.floor(i / 2) * 290;
      const offAt = 3.3 * k + i * 0.35 * k;
      const off = local > offAt;
      rrect(x - 220, y - 110, 440, 250, 24, C.card, off ? C.green : C.line);
      text(room, x - 190, y - 70, { size: 34, weight: 800, align: "left" });
      acUnit(x, y + 10, 280, !off, time + i * 0.2);
      text(off ? "ĐÃ TỰ TẮT" : "đang chạy", x, y + 105, { size: 30, weight: 800, color: off ? C.green : C.muted });
    });

    chipRow([["Làm mát trước giờ học", C.cyan], ["Báo lỗi cho bảo trì", C.amber]], 830, local, 5.3 * k, 0.6 * k);
  },

  offline(local, d, time) {
    const k = d / 8;
    title("Mất Internet vẫn điều khiển được", local);

    // cloud with a red cross
    const ca = appear(local, 0.3);
    withAlpha(ca, () => {
      ctx.fillStyle = "#2a3658";
      ctx.beginPath();
      ctx.arc(900, 270, 60, 0, Math.PI * 2);
      ctx.arc(980, 240, 80, 0, Math.PI * 2);
      ctx.arc(1060, 275, 55, 0, Math.PI * 2);
      ctx.fill();
      rrect(850, 260, 260, 75, 37, "#2a3658");
      text("Internet", 980, 375, { size: 30, color: C.muted });
      const x = clamp((local - 0.9 * k) / 0.4);
      ctx.strokeStyle = C.red;
      ctx.lineWidth = 14;
      ctx.lineCap = "round";
      ctx.beginPath();
      ctx.moveTo(920, 200); ctx.lineTo(lerp(920, 1040, x), lerp(200, 330, x));
      if (x >= 1) { ctx.moveTo(1040, 200); ctx.lineTo(920, 330); }
      ctx.stroke();
    });

    const on = local > 3.6 * k;
    phone(380, 400, 240, 420, (x, y, w) => {
      text("A101", x + w / 2, y + 50, { size: 32, weight: 800 });
      ctx.beginPath();
      ctx.arc(x + w / 2, y + 190, 55, 0, Math.PI * 2);
      ctx.fillStyle = on ? C.green : "#2a3658";
      ctx.fill();
      text(on ? "Bật" : "Tắt", x + w / 2, y + 190, { size: 30, weight: 800 });
      text("Chế độ LAN", x + w / 2, y + 300, { size: 22, color: C.amber });
    });
    acUnit(1450, 560, 440, on, time);

    const link = appear(local, 2 * k, 1);
    if (link > 0) {
      ctx.save();
      ctx.strokeStyle = C.amber;
      ctx.lineWidth = 6;
      ctx.setLineDash([20, 16]);
      ctx.lineDashOffset = -time * 80;
      ctx.beginPath();
      ctx.moveTo(640, 610);
      ctx.lineTo(lerp(640, 1210, link), 610);
      ctx.stroke();
      ctx.restore();
      text("Wi-Fi nội bộ", 925, 570, { size: 34, weight: 800, color: C.amber, alpha: link });
    }

    chipRow([["Quyền đã ký số (ES256)", C.blue], ["Lịch học lưu sẵn", C.cyan]], 830, local, 4.6 * k, 0.6 * k);
  },

  outro(local, d, time) {
    const a = appear(local, 0);
    withAlpha(a, () => acUnit(W / 2, 220, 360, true, time));
    text("Nhat Vuong Controller", W / 2, 440 + (1 - a) * 30, { size: 120, weight: 800, alpha: a });
    text("Mát đúng lúc, tắt đúng giờ.", W / 2, 560, { size: 56, color: C.cyan, alpha: appear(local, 0.8) });
    const stats = [
      { n: 27, label: "user story" },
      { n: 111, label: "bài kiểm thử" },
      { n: 2, label: "nền tảng: Android · Windows" },
    ];
    stats.forEach((s, i) => {
      const at = 1.5 + i * 0.3;
      const sa = appear(local, at);
      const x = W / 2 + (i - 1) * 520;
      withAlpha(sa, () => {
        rrect(x - 230, 660 + (1 - sa) * 30, 460, 200, 28, C.card, C.line);
        text(String(Math.round(s.n * clamp((local - at) / 1.2))), x, 735, { size: 80, weight: 800, color: C.blue });
        text(s.label, x, 815, { size: 30, color: C.muted });
      });
    });
  },
};

// ---------------------------------------------------------------- timeline

let timeline = [];
let total = 0;

function sceneAt(t) {
  return timeline.find((s) => t < s.start + s.duration) ?? timeline[timeline.length - 1];
}

function background(time) {
  const g = ctx.createLinearGradient(0, 0, W, H);
  g.addColorStop(0, C.bg1);
  g.addColorStop(1, C.bg2);
  ctx.fillStyle = g;
  ctx.fillRect(0, 0, W, H);
  // two slow-moving glows so the background never feels frozen
  [[0.2, C.blue], [0.8, C.cyan]].forEach(([fx, color], i) => {
    const x = W * fx + Math.sin(time * 0.3 + i * 2) * 200;
    const y = H * 0.4 + Math.cos(time * 0.25 + i) * 150;
    const rg = ctx.createRadialGradient(x, y, 0, x, y, 600);
    rg.addColorStop(0, color + "22");
    rg.addColorStop(1, color + "00");
    ctx.fillStyle = rg;
    ctx.fillRect(0, 0, W, H);
  });
}

function subtitle(str) {
  ctx.font = `600 38px ${FONT}`;
  const words = str.split(" ");
  const lines = [""];
  for (const word of words) {
    const next = lines[lines.length - 1] ? `${lines[lines.length - 1]} ${word}` : word;
    if (ctx.measureText(next).width > 1500) lines.push(word);
    else lines[lines.length - 1] = next;
  }
  const lh = 50;
  const boxH = lines.length * lh + 30;
  const y = H - boxH - 24;
  rrect(W / 2 - 800, y, 1600, boxH, 18, "rgba(0, 0, 0, 0.6)");
  lines.forEach((line, i) => text(line, W / 2, y + 15 + lh / 2 + i * lh, { size: 38 }));
}

function render(t) {
  const scene = sceneAt(t);
  const local = t - scene.start;
  background(t);
  ctx.save();
  ctx.globalAlpha = clamp(Math.min(local / FADE, (scene.duration - local) / FADE));
  draw[scene.id](local, scene.duration, t);
  if (ui.subs.checked) subtitle(scene.narration);
  ctx.restore();
}

// ---------------------------------------------------------------- audio + playback

const audioCtx = new AudioContext();
const master = audioCtx.createGain();
const recordDest = audioCtx.createMediaStreamDestination();
master.connect(audioCtx.destination); // speakers
master.connect(recordDest); // and the recorder

async function load() {
  let manifest = {};
  try {
    const res = await fetch("audio/manifest.json", { cache: "no-store" });
    if (res.ok) manifest = await res.json();
  } catch { /* no voice-over yet: fall back to scenes.js durations */ }

  timeline = [];
  let start = 0;
  let voiced = 0;
  for (const s of scenes) {
    let buffer = null;
    if (manifest[s.id]) {
      try {
        const res = await fetch(`audio/${manifest[s.id].file}`, { cache: "no-store" });
        buffer = await audioCtx.decodeAudioData(await res.arrayBuffer());
        voiced++;
      } catch (e) {
        console.warn(`Could not load audio for ${s.id}`, e);
      }
    }
    const duration = Math.max(s.seconds, buffer ? LEAD + buffer.duration + TAIL : 0);
    timeline.push({ ...s, start, duration, buffer });
    start += duration;
  }
  total = start;
  ui.status.textContent = voiced
    ? `${voiced}/${scenes.length} cảnh có lồng tiếng · ${total.toFixed(1)} giây`
    : `Chưa có lồng tiếng (chạy "npm run voice") · ${total.toFixed(1)} giây`;
}

let session = null; // the playback in progress, if any

async function play({ record = false } = {}) {
  if (session) return stop();
  await audioCtx.resume();
  const t0 = audioCtx.currentTime + 0.2;
  const sources = timeline.filter((s) => s.buffer).map((s) => {
    const src = audioCtx.createBufferSource();
    src.buffer = s.buffer;
    src.connect(master);
    src.start(t0 + s.start + LEAD);
    return src;
  });

  let recorder = null;
  if (record) {
    const stream = new MediaStream([
      ...canvas.captureStream(30).getVideoTracks(),
      ...recordDest.stream.getAudioTracks(),
    ]);
    const mimeType = ["video/webm;codecs=vp9,opus", "video/webm;codecs=vp8,opus", "video/webm"]
      .find((m) => MediaRecorder.isTypeSupported(m));
    recorder = new MediaRecorder(stream, { mimeType, videoBitsPerSecond: 8_000_000 });
    const chunks = [];
    recorder.ondataavailable = (e) => chunks.push(e.data);
    recorder.onstop = () => download(new Blob(chunks, { type: "video/webm" }), "nhat-vuong-intro.webm");
    recorder.start();
  }

  session = { sources, recorder, frame: 0 };
  setButtons(true, record);
  const tick = () => {
    if (!session) return;
    const t = Math.max(0, audioCtx.currentTime - t0);
    render(Math.min(t, total - 0.001));
    if (t < total) session.frame = requestAnimationFrame(tick);
    else stop();
  };
  tick();
}

function stop() {
  if (!session) return;
  cancelAnimationFrame(session.frame);
  session.sources.forEach((s) => { try { s.stop(); } catch { /* already ended */ } });
  if (session.recorder?.state === "recording") session.recorder.stop();
  session = null;
  setButtons(false);
}

function setButtons(playing, recording = false) {
  ui.play.textContent = playing ? "■ Dừng" : "▶ Phát";
  ui.record.disabled = playing;
  if (recording) ui.status.textContent = "Đang quay... giữ tab này mở cho đến khi xong.";
}

function download(blob, name) {
  const a = document.createElement("a");
  a.href = URL.createObjectURL(blob);
  a.download = name;
  a.click();
  ui.status.textContent = `Đã lưu ${name} (${(blob.size / 1e6).toFixed(1)} MB)`;
}

ui.play.onclick = () => play();
ui.record.onclick = () => play({ record: true });

await document.fonts.load(`800 48px ${FONT}`).catch(() => {});
await document.fonts.load(`600 48px ${FONT}`).catch(() => {});
await load();

// Still frame for previews: ?scene=<id>&t=<seconds into that scene>, or ?t=<seconds>
const params = new URLSearchParams(location.search);
const pick = timeline.find((s) => s.id === params.get("scene"));
render((pick?.start ?? 0) + Number(params.get("t") ?? 3));
ui.subs.onchange = () => { if (!session) render((pick?.start ?? 0) + Number(params.get("t") ?? 3)); };
