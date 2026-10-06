// Generates the voice-over with Gemini text-to-speech: one WAV per scene in audio/,
// plus audio/manifest.json with each clip's length so the video can time its scenes.
//
//   GEMINI_API_KEY=...  node tts-gemini.mjs            # only scenes without audio yet
//   node tts-gemini.mjs --force                         # regenerate everything
//   node tts-gemini.mjs outro                           # regenerate chosen scenes
//   node tts-gemini.mjs --scenes pitch/scenes.js        # another storyboard -> pitch/audio/

import { mkdir, writeFile, access } from "node:fs/promises";
import { pathToFileURL } from "node:url";
import { readJson, requireApiKey, synthesize, toWav } from "./gemini-tts.mjs";

requireApiKey();

const args = process.argv.slice(2);
const force = args.includes("--force");
const scenesIndex = args.indexOf("--scenes");
const scenesFile = scenesIndex >= 0 ? args[scenesIndex + 1] : "scenes.js";
const only = args.filter((a, i) => !a.startsWith("--") && i !== scenesIndex + 1);

// Audio lands next to the storyboard it was made from: scenes.js -> audio/, pitch/scenes.js -> pitch/audio/
const scenesUrl = pathToFileURL(new URL(scenesFile, import.meta.url).pathname);
const { scenes, voice } = await import(scenesUrl.href);
const outDir = new URL("./audio/", scenesUrl);

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

  const { pcm, sampleRate } = await synthesize(`${voice.style}\n${scene.narration}`, voice.name);
  await writeFile(fileUrl, toWav(pcm, sampleRate));
  const seconds = pcm.length / (sampleRate * 2); // 16-bit mono = 2 bytes per sample
  manifest[scene.id] = { file, seconds: Number(seconds.toFixed(2)) };
  await writeFile(manifestUrl, JSON.stringify(manifest, null, 2));
  console.log(`done  ${scene.id}  ${seconds.toFixed(1)}s`);
}
