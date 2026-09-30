// Generates the voice-over with Gemini text-to-speech: one WAV per scene in audio/,
// plus audio/manifest.json with each clip's length so the video can time its scenes.
//
//   GEMINI_API_KEY=...  node tts-gemini.mjs            # only scenes without audio yet
//   node tts-gemini.mjs --force                         # regenerate everything
//   node tts-gemini.mjs outro                           # regenerate chosen scenes
//
// No npm packages needed: Node 20+ has fetch built in.

import { mkdir, readFile, writeFile, access } from "node:fs/promises";
import { scenes, voice } from "./scenes.js";

const apiKey = process.env.GEMINI_API_KEY;
const model = process.env.GEMINI_TTS_MODEL ?? "gemini-2.5-flash-preview-tts";
const outDir = new URL("./audio/", import.meta.url);

if (!apiKey) {
  console.error("Missing GEMINI_API_KEY. Create one at https://aistudio.google.com/apikey and put it in video/.env");
  process.exit(1);
}

const args = process.argv.slice(2);
const force = args.includes("--force");
const only = args.filter((a) => !a.startsWith("--"));

await mkdir(outDir, { recursive: true });
const manifestUrl = new URL("manifest.json", outDir);
const manifest = await readJson(manifestUrl);

for (const scene of scenes) {
  const file = `${scene.id}.wav`;
  const fileUrl = new URL(file, outDir);
  const chosen = only.length === 0 || only.includes(scene.id);
  const exists = await access(fileUrl).then(() => true, () => false);
  if (!chosen || (exists && manifest[scene.id] && !force && only.length === 0)) {
    console.log(`skip  ${scene.id}`);
    continue;
  }

  const { pcm, sampleRate } = await synthesize(`${voice.style}\n${scene.narration}`);
  await writeFile(fileUrl, toWav(pcm, sampleRate));
  const seconds = pcm.length / (sampleRate * 2); // 16-bit mono = 2 bytes per sample
  manifest[scene.id] = { file, seconds: Number(seconds.toFixed(2)) };
  await writeFile(manifestUrl, JSON.stringify(manifest, null, 2));
  console.log(`done  ${scene.id}  ${seconds.toFixed(1)}s`);
}

// Calls the Gemini API and returns raw PCM audio (signed 16-bit little-endian, mono).
async function synthesize(text, attempt = 1) {
  const res = await fetch(`https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "x-goog-api-key": apiKey },
    body: JSON.stringify({
      contents: [{ parts: [{ text }] }],
      generationConfig: {
        responseModalities: ["AUDIO"],
        speechConfig: { voiceConfig: { prebuiltVoiceConfig: { voiceName: voice.name } } },
      },
    }),
  });

  // Free-tier TTS allows only a few requests per minute: wait and retry on 429/5xx.
  if ((res.status === 429 || res.status >= 500) && attempt <= 4) {
    const wait = 15 * attempt;
    console.log(`      HTTP ${res.status}, retrying in ${wait}s...`);
    await new Promise((r) => setTimeout(r, wait * 1000));
    return synthesize(text, attempt + 1);
  }
  if (!res.ok) throw new Error(`Gemini API ${res.status}: ${await res.text()}`);

  const body = await res.json();
  const audio = body.candidates?.[0]?.content?.parts?.find((p) => p.inlineData)?.inlineData;
  if (!audio) throw new Error(`No audio in response: ${JSON.stringify(body).slice(0, 500)}`);

  // mimeType looks like "audio/L16;codec=pcm;rate=24000"
  const sampleRate = Number(/rate=(\d+)/.exec(audio.mimeType)?.[1] ?? 24000);
  return { pcm: Buffer.from(audio.data, "base64"), sampleRate };
}

// Browsers cannot play headerless PCM, so prepend the 44-byte RIFF/WAVE header.
function toWav(pcm, sampleRate, channels = 1, bitsPerSample = 16) {
  const blockAlign = (channels * bitsPerSample) / 8;
  const header = Buffer.alloc(44);
  header.write("RIFF", 0);
  header.writeUInt32LE(36 + pcm.length, 4);
  header.write("WAVE", 8);
  header.write("fmt ", 12);
  header.writeUInt32LE(16, 16); // fmt chunk size
  header.writeUInt16LE(1, 20); // PCM
  header.writeUInt16LE(channels, 22);
  header.writeUInt32LE(sampleRate, 24);
  header.writeUInt32LE(sampleRate * blockAlign, 28); // byte rate
  header.writeUInt16LE(blockAlign, 32);
  header.writeUInt16LE(bitsPerSample, 34);
  header.write("data", 36);
  header.writeUInt32LE(pcm.length, 40);
  return Buffer.concat([header, pcm]);
}

async function readJson(url) {
  try {
    return JSON.parse(await readFile(url, "utf8"));
  } catch {
    return {};
  }
}
