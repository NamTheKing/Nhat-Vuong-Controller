// Lays a storyboard's voice clips over a screen recording. Each scene's segment of the recording
// ([start, start + seconds)) is kept as is; when its line is longer than the segment, the segment's
// last frame is held until the line ends. The recording's own audio is dropped.
//
//   FFMPEG=/path/to/ffmpeg node make-demo-video.mjs app-demo/scenes.js recording.mp4 out.mp4

import { readFile } from "node:fs/promises";
import { spawnSync } from "node:child_process";
import { pathToFileURL } from "node:url";

const LEAD = 0.15; // pause before each line
const TAIL = 0.35; // pause after each line before the next screen

const [scenesArg, input, output] = process.argv.slice(2);
if (!scenesArg || !input || !output) {
  console.error("usage: node make-demo-video.mjs <scenes.js> <recording.mp4> <output.mp4>");
  process.exit(1);
}

const ffmpeg = process.env.FFMPEG ?? "ffmpeg";
const scenesUrl = pathToFileURL(new URL(scenesArg, pathToFileURL(`${process.cwd()}/`)).pathname);
const { scenes, voice } = await import(scenesUrl.href);
const audioDir = new URL(`${voice?.audioDir ?? "audio"}/`, scenesUrl);
const manifest = JSON.parse(await readFile(new URL("manifest.json", audioDir), "utf8"));

const inputs = ["-i", input];
const filters = [];
const pairs = [];
let total = 0;
scenes.forEach((scene, i) => {
  const entry = manifest[scene.id];
  if (!entry) throw new Error(`No voice for ${scene.id}: run the voice step first`);
  inputs.push("-i", new URL(entry.file, audioDir).pathname);

  const length = Math.max(scene.seconds, LEAD + entry.seconds + TAIL);
  const hold = length - scene.seconds;
  filters.push(
    `[0:v]trim=start=${scene.start}:duration=${scene.seconds},setpts=PTS-STARTPTS` +
      (hold > 0 ? `,tpad=stop_mode=clone:stop_duration=${hold.toFixed(3)}` : "") + `[v${i}]`,
    `[${i + 1}:a]aresample=48000,pan=stereo|c0=c0|c1=c0,adelay=${Math.round(LEAD * 1000)}:all=1,` +
      `apad=whole_dur=${length.toFixed(3)},atrim=duration=${length.toFixed(3)}[a${i}]`,
  );
  pairs.push(`[v${i}][a${i}]`);
  console.log(`${scene.id.padEnd(18)} ${String(scene.seconds).padStart(4)}s screen  ${entry.seconds.toFixed(1)}s voice  -> ${length.toFixed(1)}s`);
  total += length;
});
// Light mastering: cut rumble, even out the level, and normalise to -16 LUFS (YouTube/podcast loudness).
filters.push(
  `${pairs.join("")}concat=n=${scenes.length}:v=1:a=1[v][raw]`,
  "[raw]highpass=f=70,acompressor=threshold=-20dB:ratio=2.5:attack=15:release=200,loudnorm=I=-16:TP=-1.5:LRA=9[a]",
);

const result = spawnSync(ffmpeg, [
  "-hide_banner", "-loglevel", "error", "-y", ...inputs,
  "-filter_complex", filters.join(";"), "-map", "[v]", "-map", "[a]",
  "-r", "30", "-c:v", "libx264", "-preset", "medium", "-crf", "18", "-pix_fmt", "yuv420p",
  "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", output,
], { stdio: "inherit" });
if (result.status !== 0) process.exit(result.status ?? 1);
console.log(`${total.toFixed(1)}s video -> ${output}`);
