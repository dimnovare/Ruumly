# ruumly-social-preview

Cloudflare Worker that intercepts social media crawler requests to `ruumly.eu`
and returns server-rendered HTML with correct Open Graph metadata. Real users
(non-crawlers) are passed through to the Vercel origin unchanged.

## How it works

```
Request to ruumly.eu/*
       │
       ▼
  Is User-Agent a known social crawler?
       │
   No  │  Yes
       │    │
       │    ▼
       │  Check Workers edge cache (1-hour TTL)
       │    │ HIT → return cached HTML
       │    │ MISS → fetch metadata from api.ruumly.eu
       │    │          └─ render OG HTML
       │    │             └─ store in edge cache
       │    │                └─ return HTML
       ▼    │
  fetch(request) — Vercel SPA, unchanged
```

**Detected crawlers:** facebookexternalhit, LinkedInBot, Twitterbot, TelegramBot,
WhatsApp, Slackbot, Discordbot, SkypeUriPreview, vkShare, redditbot, applebot.

## URL patterns

| Pattern | API call |
|---|---|
| `/{lang}/` | `GET /api/settings` |
| `/{lang}/storage/{slug}` | `GET /api/locations?city={slug}&limit=1` |
| `/{lang}/partner/{slug}` | `GET /api/suppliers/by-slug/{slug}` |
| `/{lang}/warehouse/{id}` | `GET /api/listings/{id}?lang={lang}` |
| `/{lang}/moving/{id}` | `GET /api/listings/{id}?lang={lang}` |
| `/{lang}/trailer/{id}` | `GET /api/listings/{id}?lang={lang}` |
| `/{lang}/blog/{slug}` | `GET /api/settings` (no blog-specific API yet) |
| everything else | generic site OG (no API call) |

Supported `{lang}` values: `et`, `en`, `ru`, `lv`, `lt`.

## Development

```bash
cd workers/social-preview
npm install
npm run dev          # starts wrangler dev at http://localhost:8787
npm run typecheck    # tsc --noEmit
```

During `wrangler dev`, requests are proxied through your local machine. The
Vercel origin (`ruumly.eu`) is not reachable from the worker in dev mode, so
non-crawler requests will be passed through but may fail — this is expected.
Crawler requests hit the real `api.ruumly.eu` backend.

## Deployment

```bash
# First time: authenticate
npx wrangler login

# Deploy to Cloudflare
npm run deploy

# Then attach the route in the Cloudflare dashboard (or uncomment [[routes]] in wrangler.toml):
#   Workers & Pages → ruumly-social-preview → Triggers → Routes
#   Add route: ruumly.eu/*   (zone: ruumly.eu)
```

## Environment variables

| Variable | Default (in wrangler.toml) | Description |
|---|---|---|
| `API_BASE_URL` | `https://api.ruumly.eu` | Backend API base URL |
| `SITE_URL` | `https://ruumly.eu` | Canonical frontend origin (used to build og:url) |

To override for a staging environment, add a `[env.staging]` block in `wrangler.toml`.

## Testing with curl

Replace `http://localhost:8787` with `https://ruumly.eu` to test production
(note: production only intercepts crawler UAs — non-crawler requests are passed
to Vercel, so use the real crawler UA strings below).

### Homepage

```bash
# Facebook
curl -s -A "facebookexternalhit/1.1" "http://localhost:8787/et/" | grep -E "<title>|og:"

# Twitter/X
curl -s -A "Twitterbot/1.0" "http://localhost:8787/en/" | grep -E "<title>|og:|twitter:"

# LinkedIn
curl -s -A "LinkedInBot/1.0" "http://localhost:8787/ru/" | grep "og:"
```

### City / storage page

```bash
# Facebook — Tallinn storage
curl -s -A "facebookexternalhit/1.1" "http://localhost:8787/et/storage/tallinn" | grep "og:"

# WhatsApp — Riga storage
curl -s -A "WhatsApp/2.0" "http://localhost:8787/lv/storage/riga" | grep "og:"

# Telegram — Vilnius storage
curl -s -A "TelegramBot (like TwitterBot)" "http://localhost:8787/lt/storage/vilnius" | grep "og:"
```

### Partner / supplier page

```bash
# Slack
curl -s -A "Slackbot-LinkExpanding 1.0" "http://localhost:8787/et/partner/some-company" | grep "og:"

# Discord
curl -s -A "Discordbot/2.0" "http://localhost:8787/en/partner/example-partner" | grep "og:"

# VK
curl -s -A "vkShare; vk.com/dev/Share" "http://localhost:8787/ru/partner/partner-slug" | grep "og:"
```

### Listing pages

```bash
# Facebook — warehouse listing (replace UUID with a real listing ID)
LISTING_ID="00000000-0000-0000-0000-000000000001"
curl -s -A "facebookexternalhit/1.1" "http://localhost:8787/et/warehouse/${LISTING_ID}" | grep "og:"

# Twitter — moving listing
curl -s -A "Twitterbot/1.0" "http://localhost:8787/en/moving/${LISTING_ID}" | grep -E "og:|twitter:"

# LinkedIn — trailer listing
curl -s -A "LinkedInBot/1.0 +" "http://localhost:8787/ru/trailer/${LISTING_ID}" | grep "og:"
```

### Blog post

```bash
# Apple
curl -s -A "Applebot/0.1" "http://localhost:8787/et/blog/how-to-store-furniture" | grep "og:"

# Reddit
curl -s -A "redditbot/v0.1" "http://localhost:8787/en/blog/storage-tips" | grep "og:"
```

### Generic / unknown page

```bash
# Any crawler on an unrecognised path
curl -s -A "facebookexternalhit/1.1" "http://localhost:8787/et/about" | grep "og:"
curl -s -A "Discordbot/2.0" "http://localhost:8787/et/search?q=tallinn" | grep "og:"
```

### Edge cache behaviour

```bash
# First request (MISS) — check Cache-Control header
curl -sI -A "Twitterbot/1.0" "http://localhost:8787/et/" | grep -i "cache"

# Second request should be served from edge cache (HIT in production)
curl -sI -A "Twitterbot/1.0" "http://localhost:8787/et/"
```

### Verify non-crawlers are passed through

```bash
# Regular browser UA — should NOT receive OG-only HTML (passes to Vercel)
curl -sI -A "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" "http://localhost:8787/et/" | head -5
```

## Cache purging

Cloudflare caches crawler responses for 1 hour. To purge immediately:

```bash
# Via Cloudflare API
curl -X POST "https://api.cloudflare.com/client/v4/zones/{ZONE_ID}/purge_cache" \
  -H "Authorization: Bearer {CF_API_TOKEN}" \
  -H "Content-Type: application/json" \
  --data '{"files":["https://ruumly.eu/et/partner/some-company"]}'
```

Or use **Cloudflare dashboard → Caching → Cache Rules → Purge Everything** for a
full cache flush after a major content update.
