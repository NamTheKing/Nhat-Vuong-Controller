// Builds an .srt subtitle file from a storyboard's `subtitle` fields, timed to the generated voice.
//
//   node make-srt.mjs pitch/scenes.js        -> pitch/subtitles.en.srt
//
// Timing model: scene i occupies [i * seconds, (i + 1) * seconds) in the final edit (one Flow clip
// each), its voice clip starts at the beginning of that slot, and its subtitle stays up while the
// voice plays. Long subtitles are split at punctuation into cues of at most two lines.

import { readFile, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

const MAX_LINE = 42; // characters per subtitle line, the usual broadcast limit
const scenesUrl = pathToFileURL(new URL(process.argv[2] ?? "pitch/scenes.js", import.meta.url).pathname);
const { scenes } = await import(scenesUrl.href);

let manifest = {};
try {
  manifest = JSON.parse(await readFile(new URL("audio/manifest.json", scenesUrl), "utf8"));
} catch {
  console.warn("No audio/manifest.json yet: subtitles span each whole scene. Run the voice step first for exact timing.");
}

const cues = [];
let slotStart = 0;
for (const scene of scenes) {
  if (scene.subtitle) {
    const spoken = Math.min(manifest[scene.id]?.seconds ?? scene.seconds - 0.5, scene.seconds - 0.1);
    const parts = splitIntoCues(scene.subtitle);
    const totalChars = parts.reduce((n, p) => n + p.length, 0);
    let t = slotStart;
    for (const part of parts) {
      const length = spoken * (part.length / totalChars); // each part gets screen time in proportion to its length
      cues.push({ start: t, end: t + length, text: wrap(part) });
      t += length;
    }
  }
  slotStart += scene.seconds;
}

const srt = cues
  .map((c, i) => `${i + 1}\n${stamp(c.start)} --> ${stamp(c.end)}\n${c.text}\n`)
  .join("\n");
const outUrl = new URL("subtitles.en.srt", scenesUrl);
await writeFile(outUrl, srt);
console.log(`${cues.length} cues -> ${outUrl.pathname}`);

// Cut at punctuation into clauses, then join neighbouring clauses while the cue still fits in two lines.
function splitIntoCues(text) {
  const clauses = text.match(/[^,.!?:]+[,.!?:]?/g).map((s) => s.trim()).filter(Boolean);
  const cuesOut = [];
  for (const clause of clauses) {
    const last = cuesOut[cuesOut.length - 1];
    if (last && wrap(`${last} ${clause}`).split("\n").length <= 2) cuesOut[cuesOut.length - 1] = `${last} ${clause}`;
    else cuesOut.push(clause);
  }
  return cuesOut;
}

// Greedy word wrap into lines of at most MAX_LINE characters, balanced for two-line cues.
function wrap(text) {
  if (text.length <= MAX_LINE) return text;
  const words = text.split(" ");
  let best = null;
  for (let i = 1; i < words.length; i++) {
    const a = words.slice(0, i).join(" ");
    const b = words.slice(i).join(" ");
    const worst = Math.max(a.length, b.length);
    if (!best || worst < best.worst) best = { worst, text: `${a}\n${b}` };
  }
  return best.worst <= MAX_LINE ? best.text : greedy(words);
}

function greedy(words) {
  const lines = [""];
  for (const w of words) {
    const cur = lines[lines.length - 1];
    if (cur && `${cur} ${w}`.length > MAX_LINE) lines.push(w);
    else lines[lines.length - 1] = cur ? `${cur} ${w}` : w;
  }
  return lines.join("\n");
}

function stamp(seconds) {
  const ms = Math.round(seconds * 1000);
  const pad = (n, w = 2) => String(n).padStart(w, "0");
  return `${pad(Math.floor(ms / 3600000))}:${pad(Math.floor(ms / 60000) % 60)}:${pad(Math.floor(ms / 1000) % 60)},${pad(ms % 1000, 3)}`;
}
