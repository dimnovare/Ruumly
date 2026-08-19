# Ruumly One Request Reel Recut Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce revised 15s, 23-25s, and 30s Ruumly reel exports with clearer voice-over, no duplicated service-selection screen, a better hook, and an offer/result payoff.

**Architecture:** Keep the ad project self-contained in `C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-07-one-request-reel`. Archive previous exports, generate only the missing creative assets, and add a new recut pipeline beside the existing playful pipeline so the old version remains reproducible.

**Tech Stack:** Kling CLI, ElevenLabs API, Playwright/Chromium capture, Node scripts, PowerShell build scripts, ffmpeg/ffprobe.

## Global Constraints

- Do not expose or store the ElevenLabs API key in files.
- Do not reuse old campaign footage as final source.
- No burned subtitle track.
- The service-selection UI may appear once per export.
- The hook must land at approximately second 3.
- Use the same voice family as the preferred 23.5-second opening.
- Keep all exports 1080x1920, 30 fps, H.264, AAC.

---

### Task 1: Preserve Current Exports

**Files:**
- Create: `C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-07-one-request-reel\10-exports\archive-previous\`
- Modify: none

**Interfaces:**
- Consumes: current files under `10-exports`.
- Produces: archived old files so revised exports can use the canonical filenames.

- [ ] **Step 1: Create archive folder**

Run:

```powershell
New-Item -ItemType Directory -Force "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-07-one-request-reel\10-exports\archive-previous"
```

- [ ] **Step 2: Copy existing exports**

Run:

```powershell
Copy-Item "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-07-one-request-reel\10-exports\*.mp4" "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-07-one-request-reel\10-exports\archive-previous\" -Force
```

- [ ] **Step 3: Verify archive count**

Run:

```powershell
Get-ChildItem "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-07-one-request-reel\10-exports\archive-previous" -Filter *.mp4
```

Expected: at least the previous 15s, 23.5s, and 30s exports are present.

### Task 2: Generate Revised Voice-Over

**Files:**
- Create: `02-script\voiceover-recut-et.txt`
- Create: `03-elevenlabs\generate-recut-voice.mjs`
- Create: `03-elevenlabs\selected-voiceover-recut.wav`
- Create: `03-elevenlabs\selected-voiceover-recut-15s.wav`
- Create: `03-elevenlabs\selected-voiceover-recut-30s.wav`

**Interfaces:**
- Produces: normalized WAV voice files consumed by the recut build scripts.

- [ ] **Step 1: Save the approved Estonian script**

Use:

```text
Kolimine tulemas?
Ära ava seitset eri lehte.

Kirjelda Ruumlys üks kord, mida vajad:
ladu, kolimine, haagis või muu abi.

Meie aitame leida sobivad pakkumised.
Sina valid rahulikult.

Ruumly. Üks päring, mitu võimalust.
```

- [ ] **Step 2: Generate voice with Sarah voice settings**

Use ElevenLabs `eleven_v3`, voice ID `EXAVITQu4vr4xnSDxMaL`, language `et`, stability around `0.55`, similarity `0.84`, style around `0.18`, speed around `0.94`.

- [ ] **Step 3: Normalize WAV files**

Run ffmpeg loudness normalization to roughly `-16 LUFS`, true peak below `-1.5 dB`.

- [ ] **Step 4: Inspect timing**

Run ffprobe and, if available, ElevenLabs transcription. Confirm the second-three hook is clear and no phrase is cut at 5s or 10s.

### Task 3: Generate Better Hook Clip

**Files:**
- Create: `04-kling-prompts\01-recut-hook-too-many-tabs.txt`
- Create: `05-kling-raw\generated\recut-hook-too-many-tabs.mp4`

**Interfaces:**
- Produces: 5-second or longer hook source consumed by all recut builds.

- [ ] **Step 1: Write Kling prompt**

Prompt must specify a playful but professional vertical ad, no readable real-world brand logos, no subtitles, escalating browser-tab chaos, moving boxes, and a clean visual beat at second 3.

- [ ] **Step 2: Generate with Kling**

Use `kling text_to_video` with 1080x1920 output and the strongest available model.

- [ ] **Step 3: Review contact sheet**

Extract 6-8 frames and verify the second-three hook reads visually.

### Task 4: Build Offer/Result Payoff Animation

**Files:**
- Create: `09-edit-project\recut-offers.html`
- Create: `09-edit-project\render-recut-overlays.mjs`
- Create: `09-edit-project\frames-recut\`

**Interfaces:**
- Produces: transparent overlay frames showing Ruumly brand copy, service labels, and offer cards arriving.

- [ ] **Step 1: Add offer-card scene**

Render cards such as:

```text
3 pakkumist saabus
Ladu Tallinnas
Kolimisabi
Haagis nädalavahetuseks
```

Use simple, non-specific provider cards so no unsupported claim is made.

- [ ] **Step 2: Add timeline-specific overlays**

Support `main`, `15`, and `30` query variants with different timing but the same visual language.

- [ ] **Step 3: Render PNG frame sequences**

Run Node renderer for each cut and verify `f0000.png` exists in each target frame folder.

### Task 5: Rebuild Exports

**Files:**
- Create: `09-edit-project\build-recut.ps1`
- Create: `09-edit-project\build-recut-15.ps1`
- Create: `09-edit-project\build-recut-30.ps1`
- Modify: `10-exports\ruumly-one-request-reel-final-1080x1920.mp4`
- Modify: `10-exports\ruumly-one-request-reel-15s-1080x1920.mp4`
- Modify: `10-exports\ruumly-one-request-reel-30s-1080x1920.mp4`

**Interfaces:**
- Consumes: revised hook MP4, existing service relay MP4, existing UI recordings, generated offer overlays, and revised voice files.
- Produces: final MP4 exports.

- [ ] **Step 1: Build main export**

Use a sequence: hook, single request UI, service relay, designed offer payoff, CTA.

- [ ] **Step 2: Build 15-second export**

Use the same sequence compressed, with no duplicated UI and no dense service speech.

- [ ] **Step 3: Build 30-second export**

Use extra duration for animated offer cards and choose-option clarity, not a static empty form.

### Task 6: QA And Documentation

**Files:**
- Create: `09-edit-project\FINAL-QA-RECUT.md`
- Create: `09-edit-project\qa\recut-contact-sheet-*.jpg`
- Modify: `README.md`

**Interfaces:**
- Consumes: final exports.
- Produces: final validation notes and clear handoff.

- [ ] **Step 1: Run ffprobe**

Verify each final export is 1080x1920, 30 fps, H.264, AAC.

- [ ] **Step 2: Run black-frame detection**

Verify no unintended black frames.

- [ ] **Step 3: Create contact sheets**

Review that each frame is unique and the service-selection screen appears once.

- [ ] **Step 4: Update README**

Add revised export names, concept, and notes about archived prior versions.
