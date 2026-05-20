/**
 * Ruumly Social Preview Worker
 *
 * Intercepts requests from social media crawlers and returns a minimal
 * server-rendered HTML page with correct Open Graph metadata.
 * All other traffic is passed through to the Vercel origin unchanged.
 *
 * Supported patterns:
 *   /{lang}/                       → homepage OG
 *   /{lang}/storage/{slug}         → city OG  (GET /api/locations?city=…)
 *   /{lang}/partner/{slug}         → partner OG  (GET /api/suppliers/by-slug/…)
 *   /{lang}/warehouse/{id}         → listing OG  (GET /api/listings/…)
 *   /{lang}/moving/{id}            → listing OG
 *   /{lang}/trailer/{id}           → listing OG
 *   /{lang}/blog/{slug}            → blog OG  (falls back to platform settings)
 *   everything else                → generic site OG
 */

export interface Env {
  /** Base URL for the backend API, e.g. "https://api.ruumly.eu" */
  API_BASE_URL: string;
  /** Canonical origin for the frontend, e.g. "https://ruumly.eu" */
  SITE_URL: string;
}

// ── Crawler detection ─────────────────────────────────────────────────────────

const CRAWLER_UA_FRAGMENTS = [
  "facebookexternalhit",
  "linkedinbot",
  "twitterbot",
  "telegrambot",
  "whatsapp",
  "slackbot",
  "discordbot",
  "skypeuripreview",
  "vkshare",
  "redditbot",
  "applebot",
] as const;

function isCrawler(userAgent: string): boolean {
  const ua = userAgent.toLowerCase();
  return CRAWLER_UA_FRAGMENTS.some((fragment) => ua.includes(fragment));
}

// ── Language helpers ──────────────────────────────────────────────────────────

type Lang = "et" | "en" | "ru" | "lv" | "lt";
const SUPPORTED_LANGS: readonly Lang[] = ["et", "en", "ru", "lv", "lt"];

const OG_LOCALE: Record<Lang, string> = {
  et: "et_EE",
  en: "en_US",
  ru: "ru_RU",
  lv: "lv_LV",
  lt: "lt_LT",
};

// ── City page copy per language ───────────────────────────────────────────────

const CITY_TITLE: Record<Lang, (city: string) => string> = {
  et: (c) => `${c} ladustamine`,
  en: (c) => `Storage in ${c}`,
  ru: (c) => `Хранение в ${c}`,
  lv: (c) => `Glabāšana ${c}`,
  lt: (c) => `Sandėliavimas ${c}`,
};

const CITY_DESC: Record<Lang, (city: string) => string> = {
  et: (c) => `Leia laopindu, kolimis- ja haagiseteenuseid ${c} piirkonnas Ruumly kaudu.`,
  en: (c) => `Find warehouse storage, moving services, and trailer rentals in ${c} on Ruumly.`,
  ru: (c) => `Найдите складские помещения, услуги переезда и аренду прицепов в ${c} на Ruumly.`,
  lv: (c) => `Atrodi noliktavu, pārvākšanās un piekabju nomas pakalpojumus ${c} ar Ruumly.`,
  lt: (c) => `Raskite sandėliavimą, perkraustymo paslaugas ir priekabų nuomą ${c} su Ruumly.`,
};

function parseLang(segment: string | undefined): Lang {
  return SUPPORTED_LANGS.includes(segment as Lang) ? (segment as Lang) : "et";
}

// ── Safe string helpers ───────────────────────────────────────────────────────

function escHtml(s: string): string {
  return s
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

function escAttr(s: string): string {
  return s
    .replace(/&/g, "&amp;")
    .replace(/"/g, "&quot;");
}

/** Strip HTML tags and collapse whitespace for safe use in meta attributes. */
function plainText(s: string): string {
  return s
    .replace(/<[^>]*>/g, " ")
    .replace(/\s+/g, " ")
    .trim();
}

function truncate(s: string, max = 160): string {
  const plain = plainText(s);
  if (plain.length <= max) return plain;
  return plain.slice(0, max - 1).trimEnd() + "…"; // …
}

// ── OG data & HTML template ───────────────────────────────────────────────────

interface OgData {
  title: string;
  description: string;
  image: string;
  lang: Lang;
  canonicalUrl: string;
}

const SITE_NAME    = "Ruumly";
const DEFAULT_IMAGE = "https://ruumly.eu/ruumly-logo.png";
const DEFAULT_TITLE = "Ruumly — Storage, Moving & Trailer Services";
const DEFAULT_DESC  = "Browse and book warehouse storage, moving services, and trailer rentals across Estonia and the Baltics.";

function withSiteName(title: string): string {
  return title.includes(SITE_NAME) ? title : `${title} — ${SITE_NAME}`;
}

function buildHtml(og: OgData): string {
  const title = withSiteName(og.title);
  const locale = OG_LOCALE[og.lang];

  return `<!DOCTYPE html>
<html lang="${og.lang}">
  <head>
    <meta charset="utf-8">
    <title>${escHtml(title)}</title>
    <meta name="description" content="${escAttr(og.description)}">

    <meta property="og:title"       content="${escAttr(title)}">
    <meta property="og:description" content="${escAttr(og.description)}">
    <meta property="og:image"       content="${escAttr(og.image)}">
    <meta property="og:url"         content="${escAttr(og.canonicalUrl)}">
    <meta property="og:type"        content="website">
    <meta property="og:locale"      content="${locale}">
    <meta property="og:site_name"   content="${SITE_NAME}">

    <meta name="twitter:card"        content="summary_large_image">
    <meta name="twitter:title"       content="${escAttr(title)}">
    <meta name="twitter:description" content="${escAttr(og.description)}">
    <meta name="twitter:image"       content="${escAttr(og.image)}">

    <link rel="canonical" href="${escAttr(og.canonicalUrl)}">
  </head>
  <body>
    <h1>${escHtml(title)}</h1>
    <p>${escHtml(og.description)}</p>
  </body>
</html>`;
}

// ── API response types (only the fields we use) ───────────────────────────────

interface ApiListing {
  title?:       string;
  description?: string;
  images?:      string[];
  city?:        string;
  type?:        string;
}

interface ApiPartner {
  name?:         string;
  tagline?:      string;
  heroImageUrl?: string;
  logoUrl?:      string;
}

interface ApiLocation {
  city?:   string;
  images?: string[];
}

interface ApiSettings {
  siteName?:     string;
  heroSubtitle?: string;
}

// ── Metadata resolution ───────────────────────────────────────────────────────

/**
 * Fetch from the backend API with a 3-second timeout and edge-level caching.
 * Returns null on any network or parse error so we can fall back gracefully.
 */
async function apiFetch<T>(url: string): Promise<T | null> {
  try {
    const res = await fetch(url, {
      headers: { Accept: "application/json" },
      signal: AbortSignal.timeout(3000),
      // Cloudflare-specific: cache the subrequest at the edge for 1 hour
      cf: { cacheTtl: 3600, cacheEverything: true },
    } as RequestInit);

    if (!res.ok) return null;
    return (await res.json()) as T;
  } catch {
    return null;
  }
}

function unwrapArray<T>(res: unknown): T[] {
  if (Array.isArray(res)) return res as T[];
  if (res && typeof res === "object" && "data" in res && Array.isArray((res as { data: unknown }).data)) {
    return (res as { data: T[] }).data;
  }
  return [];
}

async function resolveOgData(
  url: URL,
  env: Env,
): Promise<Omit<OgData, "canonicalUrl">> {
  const segments = url.pathname.split("/").filter(Boolean);
  // segments[0] may be a lang prefix; segments[1] is the section; segments[2] is the param
  const lang    = parseLang(segments[0]);
  const section = segments[1] ?? "";
  const param   = segments[2] ?? "";
  const api     = env.API_BASE_URL;

  // ── City / storage ──────────────────────────────────────────────────────────
  if (section === "storage" && param) {
    const data = await apiFetch<unknown>(`${api}/api/locations?city=${encodeURIComponent(param)}&limit=1`);
    const locs = unwrapArray<ApiLocation>(data);

    if (locs.length > 0) {
      const city  = locs[0].city ?? capitalize(param);
      const image = firstImage(locs[0].images);
      return {
        lang,
        title:       CITY_TITLE[lang](city),
        description: CITY_DESC[lang](city),
        image,
      };
    }
  }

  // ── Partner page ────────────────────────────────────────────────────────────
  if (section === "partner" && param) {
    const p = await apiFetch<ApiPartner>(`${api}/api/suppliers/by-slug/${encodeURIComponent(param)}`);

    if (p) {
      return {
        lang,
        title:       p.name ?? DEFAULT_TITLE,
        description: truncate(p.tagline ?? DEFAULT_DESC),
        image:       p.heroImageUrl ?? p.logoUrl ?? DEFAULT_IMAGE,
      };
    }
  }

  // ── Listing pages (warehouse / moving / trailer) ────────────────────────────
  if ((section === "warehouse" || section === "moving" || section === "trailer") && param) {
    const listing = await apiFetch<ApiListing>(`${api}/api/listings/${encodeURIComponent(param)}?lang=${lang}`);

    if (listing) {
      return {
        lang,
        title:       listing.title ?? DEFAULT_TITLE,
        description: truncate(listing.description ?? DEFAULT_DESC),
        image:       firstImage(listing.images),
      };
    }
  }

  // ── Blog post ───────────────────────────────────────────────────────────────
  if (section === "blog") {
    const settings = await apiFetch<ApiSettings>(`${api}/api/settings/public`);
    return {
      lang,
      title:       `Blog — ${settings?.siteName ?? SITE_NAME}`,
      description: truncate(settings?.heroSubtitle ?? DEFAULT_DESC),
      image:       DEFAULT_IMAGE,
    };
  }

  // ── Homepage ────────────────────────────────────────────────────────────────
  if (!section) {
    const settings = await apiFetch<ApiSettings>(`${api}/api/settings/public`);
    return {
      lang,
      title:       settings?.siteName ?? SITE_NAME,
      description: truncate(settings?.heroSubtitle ?? DEFAULT_DESC),
      image:       DEFAULT_IMAGE,
    };
  }

  // ── Generic fallback ────────────────────────────────────────────────────────
  return { lang, title: DEFAULT_TITLE, description: DEFAULT_DESC, image: DEFAULT_IMAGE };
}

// ── Utilities ─────────────────────────────────────────────────────────────────

function firstImage(images: string[] | undefined): string {
  return images?.[0] ?? DEFAULT_IMAGE;
}

function capitalize(s: string): string {
  return s.charAt(0).toUpperCase() + s.slice(1);
}

// ── Worker entry point ────────────────────────────────────────────────────────

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    const userAgent = request.headers.get("User-Agent") ?? "";

    // ── Non-crawlers: pass straight to Vercel origin ────────────────────────
    if (!isCrawler(userAgent)) {
      return fetch(request);
    }

    // ── Crawlers: serve server-rendered OG HTML ─────────────────────────────

    // 1. Check Workers edge cache first
    const cache    = caches.default;
    const cacheKey = new Request(request.url, { method: "GET" });
    const cached   = await cache.match(cacheKey);
    if (cached) return cached;

    // 2. Resolve metadata from API
    const url  = new URL(request.url);
    const ogBase = await resolveOgData(url, env);
    const og: OgData = {
      ...ogBase,
      canonicalUrl: `${env.SITE_URL}${url.pathname}`,
    };

    // 3. Render HTML
    const html = buildHtml(og);

    const response = new Response(html, {
      headers: {
        "Content-Type":  "text/html; charset=utf-8",
        "Cache-Control": "public, max-age=3600",
        "Vary":          "User-Agent",
      },
    });

    // 4. Store in edge cache (non-blocking)
    ctx.waitUntil(cache.put(cacheKey, response.clone()));

    return response;
  },
} satisfies ExportedHandler<Env>;
