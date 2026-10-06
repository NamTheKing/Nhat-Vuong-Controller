// Voices a whole storyboard in ONE Gemini call, so intonation flows from line to line like a single
// speaker reading a script (separate calls reset the delivery on every line and sound stitched).
// The take is then cut at the pauses between paragraphs into one WAV per scene, written to the same
// audio/ + manifest.json layout tts-gemini.mjs produces.
//
//   node tts-one-take.mjs app-demo/scenes.en.js          # -> app-demo/audio-en/
//
// The storyboard exports `voice = { name, direction, audioDir }` and `scenes` with `narration`.

import { mkdir, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";
import { requireApiKey, synthesize, toWav } from "./gemini-tts.mjs";

const WINDOW = 0.01; // seconds per loudness window
const SILENCE_DB = -42; // a window quieter than this counts as silence
const MIN_PAUSE = 0.3; // shortest pause that can separate two paragraphs
const PAD = 0.06; // silence kept at each end of a cut clip

requireApiKey();
const scenesUrl = pathToFileURL(new URL(process.argv[2] ?? "app-demo/scenes.en.js", pathToFileURL(`${process.cwd()}/`)).pathname);
const { scenes, voice } = await import(scenesUrl.href);
const outDir = new URL(`${voice.audioDir ?? "audio"}/`, scenesUrl);
await mkdir(outDir, { recursive: true });

const script = `${voice.direction}\n\n${scenes.map((s) => s.narration).join("\n\n")}`;
const { pcm, sampleRate } = await synthesize(script, voice.name);
await writeFile(new URL("take.wav", outDir), toWav(pcm, sampleRate));
const samples = new Int16Array(pcm.buffer, pcm.byteOffset, pcm.length / 2);
console.log(`take  ${(samples.length / sampleRate).toFixed(1)}s`);

// Loudness per window, then runs of silent windows.
const win = Math.round(WINDOW * sampleRate);
const silent = [];
for (let i = 0; i + win <= samples.length; i += win) {
  let sum = 0;
  for (let j = i; j < i + win; j++) sum += samples[j] * samples[j];
  const db = 20 * Math.log10(Math.sqrt(sum / win) / 32768 + 1e-9);
  silent.push(db < SILENCE_DB);
}
const firstSound = silent.indexOf(false);
const lastSound = silent.lastIndexOf(false);
const pauses = [];
for (let i = firstSound; i <= lastSound; i++) {
  if (!silent[i]) continue;
  let j = i;
  while (j <= lastSound && silent[j]) j++;
  if ((j - i) * WINDOW >= MIN_PAUSE) pauses.push({ from: i, to: j });
  i = j;
}

// The paragraph breaks are the longest pauses; keep as many as there are gaps between scenes.
const breaks = pauses
  .sort((a, b) => (b.to - b.from) - (a.to - a.from))
  .slice(0, scenes.length - 1)
  .sort((a, b) => a.from - b.from);
if (breaks.length < scenes.length - 1) {
  throw new Error(`Found ${breaks.length} pauses for ${scenes.length - 1} paragraph breaks; run again for a new take.`);
}

const bounds = [firstSound, ...breaks.flatMap((b) => [b.from, b.to]), lastSound + 1];
const words = scenes.map((s) => s.narration.split(/\s+/).length);
const totalWords = words.reduce((a, b) => a + b, 0);
const spoken = scenes.reduce((n, _, i) => n + (bounds[2 * i + 1] - bounds[2 * i]), 0);
const manifest = {};
for (const [i, scene] of scenes.entries()) {
  const start = Math.max(0, Math.round((bounds[2 * i] * WINDOW - PAD) * sampleRate));
  const end = Math.min(samples.length, Math.round((bounds[2 * i + 1] * WINDOW + PAD) * sampleRate));
  const clip = pcm.subarray(start * 2, end * 2);
  const file = `${scene.id}.wav`;
  await writeFile(new URL(file, outDir), toWav(clip, sampleRate));
  const seconds = (end - start) / sampleRate;
  manifest[scene.id] = { file, seconds: Number(seconds.toFixed(2)) };

  // A cut in the wrong place shows up as a clip far longer or shorter than its share of the words.
  const share = (bounds[2 * i + 1] - bounds[2 * i]) / spoken / (words[i] / totalWords);
  console.log(`cut   ${scene.id.padEnd(18)} ${seconds.toFixed(1)}s${share < 0.6 || share > 1.6 ? "   <- check this cut" : ""}`);
}
await writeFile(new URL("manifest.json", outDir), JSON.stringify(manifest, null, 2));
