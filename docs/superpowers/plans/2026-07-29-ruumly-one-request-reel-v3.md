# Ruumly One Request Reel V3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebuild the Ruumly reel V3 with a stronger pop-out hook, slower and clearer voice-over, slower service visuals, and cleaner overlay placement.

**Architecture:** Work inside `C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-07-one-request-reel`. Preserve the current V2 exports in `10-exports/archive-v2-recut`, add V3 scripts/assets beside existing files, and overwrite canonical export filenames only after rendering.

**Tech Stack:** Kling CLI, ElevenLabs API, Node/Playwright overlay renderer, PowerShell, ffmpeg/ffprobe.

## Global Constraints

- Do not store API keys in files.
- No burned subtitles.
- Keep exports 1080x1920, 30fps, H.264, AAC.
- The phrase `Üks päring` must be spoken near the beginning.
- `Ruumly aitab leida sobivad pakkumised` must be fully audible and not cut by a visual chapter change.
- Service visuals must remain visible long enough to be understood.
- Overlay text must not cover important video subject areas.

---

### Task 1: Generate V3 Hook

- [ ] Write Kling prompt for hidden-behind-box pop-out hook.
- [ ] Generate one 5s vertical hook.
- [ ] Download unwatermarked MP4 as `recut-v3-hook-popout-box.mp4`.
- [ ] Create contact sheet and verify the pop-out reads around second 3.

### Task 2: Generate V3 Voice

- [ ] Save V3 scripts.
- [ ] Generate 15s/main/30s voice files with slower settings.
- [ ] Normalize to WAV.
- [ ] Transcribe and verify key phrases are recognized.

### Task 3: Rebuild V3 Overlay

- [ ] Create `overlay-v3.html`.
- [ ] Move hook/footage text to safe top or bottom bands.
- [ ] Use minimal service chips only.
- [ ] Replace result screen with `Pakkumised ühes kohas` and clean cards.
- [ ] Render frame sequences for all cuts.

### Task 4: Rebuild V3 Exports

- [ ] Create V3 build scripts.
- [ ] Use slower service pacing, especially in 24s and 30s.
- [ ] Render 15s, 24s, 30s, and clean variants.

### Task 5: QA

- [ ] Verify file specs via ffprobe.
- [ ] Run black-frame detection.
- [ ] Create contact sheets.
- [ ] Confirm no major visual cuts mid-sentence.
- [ ] Update V3 QA notes and README.
