// English voice-over for the same recording: timings come from scenes.js, only the words change.
// Voiced in one take so it sounds like one person reading a script, not seven stitched clips.
//
//   node tts-one-take.mjs app-demo/scenes.en.js
//   node make-demo-video.mjs app-demo/scenes.en.js <recording.mp4> <output.mp4>

import { scenes as timings } from "./scenes.js";

export const voice = {
  // Gemini voices that suit a relaxed walkthrough: Achird (friendly), Sulafat (warm), Zubenelgenubi (casual).
  name: process.env.VOICE ?? "Achird",
  audioDir: `audio-en-${(process.env.VOICE ?? "Achird").toLowerCase()}`,
  direction:
    "Read the following aloud in natural, conversational American English, like a friendly product designer " +
    "casually walking a colleague through an app they're proud of. Warm and relaxed, a hint of a smile, natural " +
    "rhythm with small breaths; not an announcer, not salesy. Pronounce \"Nhat Vuong\" as \"nyat voong\". " +
    "Leave a clear one-second pause between paragraphs:",
};

const lines = {
  "1-devices": "So this is Nhat Vuong Controller. Every air conditioner on campus, on one screen.",
  "2-detail": "Tap any unit to see what it's actually doing, then set the temperature, the mode, the fan.",
  "3-incidents": "If something goes wrong, it shows up right here. A unit running too long, a bad sensor, a module that's dropped offline.",
  "4-admin": "Admins get the whole toolbox in one place: rooms, users, access, the audit log, and reports.",
  "5-rooms": "Buildings, rooms, and how many units sit in each one.",
  "6-notifications": "Alerts come straight to you, so nothing slips through.",
  "7-language": "And it speaks your language. Vietnamese or English, one tap.",
};

export const scenes = timings.map((s) => ({ ...s, narration: lines[s.id] }));
