// Shared Gemini text-to-speech call and WAV writer for tts-gemini.mjs and tts-one-take.mjs.
// No npm packages needed: Node 20+ has fetch built in.

import { readFile } from "node:fs/promises";

const apiKey = process.env.GEMINI_API_KEY;
const model = process.env.GEMINI_TTS_MODEL ?? "gemini-2.5-flash-preview-tts";

export function requireApiKey() {
  if (!apiKey) {
    console.error("Missing GEMINI_API_KEY. Create one at https://aistudio.google.com/apikey and put it in video/.env");
    process.exit(1);
  }
}

// Calls the Gemini API and returns raw PCM audio (signed 16-bit little-endian, mono).
export async function synthesize(text, voiceName, attempt = 1) {
  const res = await fetch(`https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "x-goog-api-key": apiKey },
    body: JSON.stringify({
      contents: [{ parts: [{ text }] }],
      generationConfig: {
        responseModalities: ["AUDIO"],
        speechConfig: { voiceConfig: { prebuiltVoiceConfig: { voiceName } } },
      },
    }),
  });

  // Free-tier TTS allows only a few requests per minute: wait and retry on 429/5xx.
  // A retry delay of minutes or hours means the daily quota is spent, so stop instead of waiting.
  if (res.status === 429) {
    const body = await res.clone().text();
    const delay = Number(/"retryDelay":\s*"(\d+)s"/.exec(body)?.[1] ?? 0);
    if (delay > 120) {
      throw new Error(`Daily Gemini TTS quota used up; it resets in about ${Math.ceil(delay / 3600)} h. ` +
        "Rerun the same command later: scenes that already have audio are skipped.");
    }
  }
  if ((res.status === 429 || res.status >= 500) && attempt <= 4) {
    const wait = 15 * attempt;
    console.log(`      HTTP ${res.status}, retrying in ${wait}s...`);
    await new Promise((r) => setTimeout(r, wait * 1000));
    return synthesize(text, voiceName, attempt + 1);
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
export function toWav(pcm, sampleRate, channels = 1, bitsPerSample = 16) {
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

export async function readJson(url) {
  try {
    return JSON.parse(await readFile(url, "utf8"));
  } catch {
    return {};
  }
}
