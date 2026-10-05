# Intro video

A ~50-second animated introduction to Nhat Vuong Controller, drawn on an HTML canvas with plain JavaScript
and narrated in Vietnamese by Gemini text-to-speech. No npm dependencies; needs Node 22+ and a Chromium-based
browser (Chrome, Edge) or Firefox.

| File | Role |
|---|---|
| `scenes.js` | Storyboard: scene order, narration text, voice name and speaking style |
| `tts-gemini.mjs` | Calls the Gemini API, writes `audio/<scene>.wav` and `audio/manifest.json` |
| `intro.js` | Draws every scene, syncs it to the audio clock, records canvas + audio to `.webm` |
| `index.html` | Player page |
| `serve.mjs` | Static server (the page uses `fetch`, which does not work from `file://`) |

## Make the video

```sh
cd video
cp .env.example .env            # paste your key from https://aistudio.google.com/apikey
npm run voice                   # generate the narration (only scenes without audio)
npm run serve                   # open http://localhost:5173
```

On the page: **▶ Phát** previews with sound, **● Quay thành video** plays it once and downloads
`nhat-vuong-intro.webm`. Keep the tab visible while recording — browsers pause animation in hidden tabs.

Convert to MP4 if needed: `ffmpeg -i nhat-vuong-intro.webm -c:v libx264 -crf 18 -c:a aac nhat-vuong-intro.mp4`

## Editing

- Change a line of narration in `scenes.js`, then `npm run voice -- <scene-id>` to regenerate just that clip.
  Scene length follows the clip automatically.
- Change the voice or style in `scenes.js` (`voice.name`, `voice.style`), then `npm run voice -- --force`.
- Preview one frame without playing: `http://localhost:5173/?scene=architecture&t=4`.
- Another TTS model: set `GEMINI_TTS_MODEL` in `.env`.

`.env`, `audio/` and recorded videos are git-ignored.

## Pitch video (Google Flow + Gemini voice)

`pitch/` holds a second storyboard for a pitch-deck video whose pictures are generated in Google Flow:
`pitch/scenes.js` is the narration, `pitch/STORYBOARD.md` has the Flow prompt and overlay text for each
8-second clip plus the assembly steps. `npm run voice:pitch` generates the narration into `pitch/audio/`.
`npm run srt:pitch` then writes English subtitles timed to that narration (`pitch/subtitles.en.srt`).
