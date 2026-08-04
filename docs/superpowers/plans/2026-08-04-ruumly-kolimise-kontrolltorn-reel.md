# Ruumly Kolimise Kontrolltorn Reel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce one publishable Estonian 9:16 social reel for Ruumly around the "Kolimise kontrolltorn" concept.

**Architecture:** Create a fresh standalone production folder under `C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel`. Separate responsibilities into brief/script, ElevenLabs voiceover, Kling prompts/raw clips, real Ruumly screen recordings, brand/audio assets, edit scripts, exports, thumbnails, and QA notes. Build one final 22-25s video, plus clean and voice-only variants, with mechanical audio/video checks before delivery.

**Tech Stack:** Kling CLI for cinematic clips, ElevenLabs API for Estonian voiceover, Playwright/browser recording for real Ruumly UI, FFmpeg/FFprobe for assembly and QA, PowerShell/Node scripts for repeatable production.

## Global Constraints

- Final language: Estonian.
- Final output: one good video, not 15s/30s variants.
- Format: vertical 9:16, 1080x1920, 30 fps.
- Target duration: 22-25 seconds.
- Hook lands around second 3.
- Voiceover must never cut mid-word or mid-sentence.
- Use real Ruumly UI for product scenes; do not generate fake readable UI with AI.
- Do not imply guaranteed instant booking or guaranteed availability.
- Do not use competitor logos, fake testimonials, personal data, white flashes, glitch transitions, or the rejected V3 hook.
- Keep service montage readable: show 4-5 clear service moments, not a frantic seven-service sprint.
- Store all generated marketing files outside application repos, under the production folder.

---

### Task 1: Production Folder and Source Brief

**Files:**
- Create: `C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel\README.md`
- Create: `C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel\01-brief\creative-brief.md`
- Create folders: `01-brief`, `02-script`, `03-elevenlabs`, `04-kling-prompts`, `05-kling-raw`, `06-screen-recordings`, `07-brand-assets`, `08-audio`, `09-edit-project`, `10-exports`, `11-thumbnails`

**Interfaces:**
- Consumes: Design spec at `docs/superpowers/specs/2026-08-04-ruumly-kolimise-kontrolltorn-reel-design.md`.
- Produces: Canonical production folder path and brief used by all later tasks.

- [ ] **Step 1: Create the folder structure**

Run:

```powershell
$root = "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel"
$dirs = "01-brief","02-script","03-elevenlabs","04-kling-prompts","05-kling-raw","06-screen-recordings","07-brand-assets","08-audio","09-edit-project","10-exports","11-thumbnails"
New-Item -ItemType Directory -Path $root -Force | Out-Null
$dirs | ForEach-Object { New-Item -ItemType Directory -Path (Join-Path $root $_) -Force | Out-Null }
```

- [ ] **Step 2: Write `01-brief/creative-brief.md`**

Content:

```markdown
# Ruumly Kolimise Kontrolltorn - Creative Brief

## Core Idea

Kolimine ei ole üks asi. See on väike projekt.

Ruumly connects storage, movers, trailers, vans, packing, cleaning, and insurance through one request.

## Hook

At roughly second 3, a moving box reveals too many tasks. The viewer recognizes the problem instantly: one move secretly creates seven jobs.

## Truth Boundary

Ruumly helps users find suitable verified partners and compare 2-3 offers. The ad must not promise instant automatic booking, guaranteed availability, or guaranteed prices.

## Final CTA

Kümne kõne asemel üks päring.
ruumly.eu
```

- [ ] **Step 3: Write root `README.md`**

Content:

```markdown
# Ruumly Kolimise Kontrolltorn Reel

Fresh production folder for the August 2026 Estonian Ruumly social reel.

Canonical export:

- `10-exports/ruumly-kolimise-kontrolltorn-reel-1080x1920.mp4`

Supporting exports:

- `10-exports/ruumly-kolimise-kontrolltorn-clean.mp4`
- `10-exports/ruumly-kolimise-kontrolltorn-voice-only.mp4`

This project intentionally avoids the rejected V3 hook and previous cramped 15s/30s variants.
```

- [ ] **Step 4: Verify**

Run:

```powershell
Get-ChildItem -Force "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel" | Sort-Object Name
```

Expected: all 11 folders plus `README.md`.

### Task 2: Script, Timing, and Subtitle Skeleton

**Files:**
- Create: `02-script/voiceover-et.txt`
- Create: `02-script/timing.md`
- Create: `02-script/ruumly-kolimise-kontrolltorn-et.srt`

**Interfaces:**
- Consumes: Brief from Task 1.
- Produces: Voiceover source for ElevenLabs and timing targets for edit scripts.

- [ ] **Step 1: Write final voiceover**

Content:

```text
Kolimine ei ole üks asi.
See on väike projekt.

Ladu, kolija, haagis, kaubik,
pakkimine, koristus ja kindlustus.

Ruumlys kirjeldad kõik ühe päringuga.
Meie aitame leida sobivad kinnitatud partnerid
ja saad 2-3 pakkumist rahulikuks võrdlemiseks.

Ruumly.
Kümne kõne asemel üks päring.
```

- [ ] **Step 2: Write timing targets**

Content:

```markdown
# Timing

Target duration: 24.0s.

| Time | Beat | Voice |
| --- | --- | --- |
| 0.0-3.4 | Hook: moving box reveals project chaos | Kolimine ei ole üks asi. |
| 3.4-5.0 | Resolve hook | See on väike projekt. |
| 5.0-10.5 | Service moments | Ladu, kolija, haagis, kaubik, pakkimine, koristus ja kindlustus. |
| 10.5-17.0 | Real Ruumly UI | Ruumlys kirjeldad kõik ühe päringuga. |
| 17.0-21.5 | Offers from verified partners | Meie aitame leida sobivad kinnitatud partnerid ja saad 2-3 pakkumist rahulikuks võrdlemiseks. |
| 21.5-24.0 | Brand CTA | Ruumly. Kümne kõne asemel üks päring. |

Audio rule: each phrase must finish at least 0.20s before the next hard scene cut.
```

- [ ] **Step 3: Write SRT skeleton**

Content:

```srt
1
00:00:00,300 --> 00:00:03,400
Kolimine ei ole üks asi.

2
00:00:03,500 --> 00:00:05,000
See on väike projekt.

3
00:00:05,200 --> 00:00:10,500
Ladu, kolija, haagis, kaubik,
pakkimine, koristus ja kindlustus.

4
00:00:10,800 --> 00:00:14,900
Ruumlys kirjeldad kõik ühe päringuga.

5
00:00:15,100 --> 00:00:21,000
Aitame leida kinnitatud partnerid
ja 2-3 pakkumist võrdlemiseks.

6
00:00:21,500 --> 00:00:24,000
Ruumly. Kümne kõne asemel üks päring.
```

- [ ] **Step 4: Verify the script has complete sentence endings**

Run:

```powershell
Get-Content "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel\02-script\voiceover-et.txt"
```

Expected: the final line ends with a period after `päring.`

### Task 3: Brand Assets and Real UI Recording

**Files:**
- Copy/create: `07-brand-assets/ruumly-icon.png`
- Create: `06-screen-recordings/capture-request-flow.mjs`
- Create: `06-screen-recordings/request-flow.webm`

**Interfaces:**
- Consumes: Real Ruumly production UI.
- Produces: Real UI footage used by the edit.

- [ ] **Step 1: Copy the official Ruumly icon**

Preferred source:

```powershell
Copy-Item -LiteralPath "C:\Users\Dmitri.MARKIT\Desktop\Ruumly\ruumly-icon-src-removebg-preview.png" -Destination "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel\07-brand-assets\ruumly-icon.png" -Force
```

- [ ] **Step 2: Create the Playwright recording script**

The script must open `https://ruumly.eu/et/request`, select multiple services, proceed to the next step, and record only the page. Use demo data only.

- [ ] **Step 3: Record at mobile reel-friendly size**

Run:

```powershell
node "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel\06-screen-recordings\capture-request-flow.mjs"
```

Expected: `06-screen-recordings/request-flow.webm` exists and shows the real Ruumly flow without personal data.

- [ ] **Step 4: Inspect recording**

Run:

```powershell
ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1 "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel\06-screen-recordings\request-flow.webm"
```

Expected: duration at least 7 seconds.

### Task 4: Kling Prompt Package and Clip Generation

**Files:**
- Create: `04-kling-prompts/01-hook-box-project.md`
- Create: `04-kling-prompts/02-storage-service.md`
- Create: `04-kling-prompts/03-moving-service.md`
- Create: `04-kling-prompts/04-trailer-van-service.md`
- Create: `04-kling-prompts/05-packing-cleaning-insurance.md`
- Create raw clips under: `05-kling-raw/generated/`

**Interfaces:**
- Consumes: Creative brief and visual style.
- Produces: Cinematic clips for hook and service montage.

- [ ] **Step 1: Write the hook prompt**

Prompt must request a 5s vertical shot: modern Estonian apartment, moving boxes, person opens one box, abstract task cards/papers pop out, funny shock at second 3, no readable text, no logos.

- [ ] **Step 2: Write four service prompts**

Each prompt must target 3-4s vertical source footage, premium Northern European realism, clean negative space, no readable logos/text.

- [ ] **Step 3: Generate clips with Kling**

Run one Kling command per approved prompt and save outputs to `05-kling-raw/generated`.

- [ ] **Step 4: Verify clip dimensions/durations**

Run:

```powershell
Get-ChildItem "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel\05-kling-raw\generated" -Filter *.mp4 | ForEach-Object {
  ffprobe -v error -select_streams v:0 -show_entries stream=width,height,duration -of default=noprint_wrappers=1 $_.FullName
}
```

Expected: usable clips exist for hook and service montage. Non-vertical clips may be cropped in the edit, but must be visually usable.

### Task 5: ElevenLabs Voiceover

**Files:**
- Create: `03-elevenlabs/generate-voice.mjs`
- Create: `03-elevenlabs/selected-voiceover.wav`
- Create: `03-elevenlabs/selected-voiceover.mp3`

**Interfaces:**
- Consumes: `02-script/voiceover-et.txt`.
- Produces: final voiceover audio used by the edit.

- [ ] **Step 1: Generate a clear Estonian read**

Use a warm adult Estonian-compatible voice. Keep delivery medium pace. Do not compress pauses away aggressively.

- [ ] **Step 2: Normalize to WAV**

Run:

```powershell
ffmpeg -y -i "selected-voiceover.mp3" -af "loudnorm=I=-16:TP=-1.5:LRA=7" "selected-voiceover.wav"
```

- [ ] **Step 3: Verify duration**

Run:

```powershell
ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1 "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel\03-elevenlabs\selected-voiceover.wav"
```

Expected: duration less than or equal to 23.3 seconds so the 24-25s edit can leave tail silence.

- [ ] **Step 4: Listen for complete sentence endings**

Play or inspect the WAV. Reject and regenerate if any phrase sounds chopped, rushed, slurred, or unclear.

### Task 6: Edit Overlay and Build Scripts

**Files:**
- Create: `09-edit-project/overlay.html`
- Create: `09-edit-project/render-overlays.mjs`
- Create: `09-edit-project/build.ps1`
- Create: `08-audio/music-bed.wav`

**Interfaces:**
- Consumes: Kling clips, real UI recording, brand assets, voiceover, SRT.
- Produces: final MP4 exports.

- [ ] **Step 1: Build overlay template**

Overlay requirements:

- Large mobile-safe text.
- Brandbug in top-left on footage scenes.
- Hook text around second 3.
- Service chips during montage.
- Offer result card around `17.0-21.5`.
- Final CTA from `21.5-24.8`.

- [ ] **Step 2: Render transparent overlay frames**

Run:

```powershell
node "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel\09-edit-project\render-overlays.mjs"
```

Expected: `09-edit-project/frames/f0000.png` through final frame.

- [ ] **Step 3: Build final, clean, and voice-only exports**

Run:

```powershell
powershell -File "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel\09-edit-project\build.ps1"
```

Expected exports:

- `10-exports/ruumly-kolimise-kontrolltorn-reel-1080x1920.mp4`
- `10-exports/ruumly-kolimise-kontrolltorn-clean.mp4`
- `10-exports/ruumly-kolimise-kontrolltorn-voice-only.mp4`

### Task 7: Final QA, Thumbnail, and Delivery Notes

**Files:**
- Create: `09-edit-project/qa/contact-sheet.jpg`
- Create: `09-edit-project/FINAL-QA.md`
- Create: `11-thumbnails/ruumly-kolimise-kontrolltorn-thumbnail-1080x1920.png`
- Create: `social-copy-et.md`

**Interfaces:**
- Consumes: final exports from Task 6.
- Produces: delivery-ready proof and social publishing support.

- [ ] **Step 1: Verify video properties**

Run:

```powershell
ffprobe -v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate,duration -of default=noprint_wrappers=1 "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel\10-exports\ruumly-kolimise-kontrolltorn-reel-1080x1920.mp4"
```

Expected: `width=1080`, `height=1920`, `r_frame_rate=30/1`, duration between `22.000000` and `25.500000`.

- [ ] **Step 2: Run black-frame detection**

Run:

```powershell
ffmpeg -v info -i "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel\10-exports\ruumly-kolimise-kontrolltorn-reel-1080x1920.mp4" -vf blackdetect=d=0.15:pix_th=0.08 -an -f null NUL
```

Expected: no `black_start` intervals.

- [ ] **Step 3: Create contact sheet**

Run:

```powershell
ffmpeg -y -loglevel error -i "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel\10-exports\ruumly-kolimise-kontrolltorn-reel-1080x1920.mp4" -vf "fps=1/3,scale=270:-1,tile=4x3" "C:\Users\Dmitri.MARKIT\ruumly-ad\projects\2026-08-kolimise-kontrolltorn-reel\09-edit-project\qa\contact-sheet.jpg"
```

Expected: no text covers important UI or faces; hook, UI, offers, and CTA are visually clear.

- [ ] **Step 4: Write QA note**

Document duration, dimensions, voiceover check, black-frame check, and any known limitations in `09-edit-project/FINAL-QA.md`.

- [ ] **Step 5: Write social copy**

Content must include:

- Instagram caption.
- Facebook caption.
- LinkedIn caption.
- Paid-ad headline.
- Paid-ad description.
- CTA variants.

## Self-Review

Spec coverage:

- One Estonian video: Task 6.
- 3-second hook: Tasks 2, 4, 6.
- No cut voiceover: Tasks 2, 5, 7.
- Real Ruumly UI: Task 3.
- Kling cinematic clips: Task 4.
- Final QA: Task 7.

Placeholder scan:

- No TODO/TBD placeholders remain.

Scope check:

- This plan produces one complete video and supporting assets. It does not include extra 15s/30s variants, app changes, or live deployment.
