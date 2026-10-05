// Joins a storyboard's per-scene voice clips into one WAV the length of the whole edit, each clip
// placed at the start of its scene slot with silence in between. Drop it at 0:00 in the editor.
//
//   node make-voice-track.mjs pitch-40s/scenes.js     -> pitch-40s/voice-track.wav

import { readFile, writeFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

const scenesUrl = pathToFileURL(new URL(process.argv[2] ?? "pitch-40s/scenes.js", import.meta.url).pathname);
const { scenes } = await import(scenesUrl.href);
const audioDir = new URL("audio/", scenesUrl);
const manifest = JSON.parse(await readFile(new URL("manifest.json", audioDir), "utf8"));

const clips = [];
let sampleRate = 0;
let slotStart = 0;
for (const scene of scenes) {
  const entry = manifest[scene.id];
  if (!entry) throw new Error(`No voice for ${scene.id}: run the voice step first`);
  const wav = await readFile(new URL(entry.file, audioDir));
  const rate = wav.readUInt32LE(24);
  if (sampleRate && rate !== sampleRate) throw new Error(`${scene.id} is ${rate} Hz, expected ${sampleRate} Hz`);
  sampleRate = rate;
  const pcm = wav.subarray(44); // tts-gemini.mjs writes a plain 44-byte header, 16-bit mono
  if (pcm.length / (rate * 2) > scene.seconds) console.warn(`warn  ${scene.id} voice is longer than its ${scene.seconds}s slot`);
  clips.push({ start: slotStart, pcm });
  slotStart += scene.seconds;
}

const total = Buffer.alloc(Math.round(slotStart * sampleRate) * 2); // zero bytes = silence
for (const { start, pcm } of clips) {
  const offset = Math.round(start * sampleRate) * 2;
  pcm.copy(total, offset, 0, Math.min(pcm.length, total.length - offset));
}

const header = Buffer.alloc(44);
header.write("RIFF", 0);
header.writeUInt32LE(36 + total.length, 4);
header.write("WAVEfmt ", 8);
header.writeUInt32LE(16, 16);
header.writeUInt16LE(1, 20); // PCM
header.writeUInt16LE(1, 22); // mono
header.writeUInt32LE(sampleRate, 24);
header.writeUInt32LE(sampleRate * 2, 28);
header.writeUInt16LE(2, 32);
header.writeUInt16LE(16, 34);
header.write("data", 36);
header.writeUInt32LE(total.length, 40);

const outUrl = new URL("voice-track.wav", scenesUrl);
await writeFile(outUrl, Buffer.concat([header, total]));
console.log(`${slotStart}s voice track -> ${outUrl.pathname}`);
