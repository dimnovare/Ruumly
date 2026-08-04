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

/**
 * Static asset paths (og:image, favicon, PWA icons, manifest, fonts, sitemap, …)
 * must always be served from origin — never rewritten to OG HTML. Otherwise a
 * social crawler that fetches the og:image gets HTML back and the link preview
 * renders no image at all.
 */
const STATIC_ASSET_EXT =
  /\.(png|jpe?g|gif|webp|avif|svg|ico|json|webmanifest|xml|txt|css|js|mjs|map|woff2?|ttf|otf|eot|mp4|webm|pdf)$/i;

function isStaticAsset(pathname: string): boolean {
  return STATIC_ASSET_EXT.test(pathname);
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

// ── Service copy per language ─────────────────────────────────────────────────
//
// Ruumly is a concierge for the whole "I'm moving" event, not a storage site.
// Every service the directory covers gets its own noun so a city hub's <title>
// matches what people actually search — Search Console shows the demand is
// per-service and per-city ("kaubiku rent tartu", "kolimisteenus rakveres",
// "laoruumide rent tartu"), and a page titled "Ruumly — Laopinnad Eestis"
// answers none of them.

/** Route section → service. `warehouse` is the listing detail route for storage. */
type Service = "storage" | "moving" | "trailer" | "vanrental" | "cleaning" | "packing" | "insurance";

const SERVICE_NOUN: Record<Lang, Record<Service, string>> = {
  et: {
    storage:   "Laopinna ja miniladu rent",
    moving:    "Kolimisteenus",
    trailer:   "Haagise rent",
    vanrental: "Kaubiku rent",
    cleaning:  "Koristusteenus",
    packing:   "Pakkimine ja pakkematerjal",
    insurance: "Kolimiskindlustus",
  },
  en: {
    storage:   "Self storage",
    moving:    "Moving services",
    trailer:   "Trailer rental",
    vanrental: "Van rental",
    cleaning:  "Cleaning services",
    packing:   "Packing and boxes",
    insurance: "Moving insurance",
  },
  ru: {
    storage:   "Аренда склада",
    moving:    "Услуги переезда",
    trailer:   "Аренда прицепа",
    vanrental: "Аренда фургона",
    cleaning:  "Уборка",
    packing:   "Упаковка и коробки",
    insurance: "Страхование переезда",
  },
  lv: {
    storage:   "Noliktavas noma",
    moving:    "Pārvākšanās pakalpojumi",
    trailer:   "Piekabju noma",
    vanrental: "Furgonu noma",
    cleaning:  "Uzkopšanas pakalpojumi",
    packing:   "Iepakošana un kastes",
    insurance: "Pārvākšanās apdrošināšana",
  },
  lt: {
    storage:   "Sandėlio nuoma",
    moving:    "Perkraustymo paslaugos",
    trailer:   "Priekabų nuoma",
    vanrental: "Mikroautobusų nuoma",
    cleaning:  "Valymo paslaugos",
    packing:   "Pakavimas ir dėžės",
    insurance: "Perkraustymo draudimas",
  },
};

/** "{noun} {city}" — deliberately nominative; Estonian/Latvian case endings on a
 *  city name are irregular and a wrong inflection reads worse than none. */
const HUB_TITLE: Record<Lang, (noun: string, city: string) => string> = {
  et: (n, c) => `${n} ${c}`,
  en: (n, c) => `${n} in ${c}`,
  ru: (n, c) => `${n}: ${c}`,
  lv: (n, c) => `${n} ${c}`,
  lt: (n, c) => `${n} ${c}`,
};

const HUB_DESC: Record<Lang, (noun: string, city: string) => string> = {
  et: (n, c) => `${n} ${c} piirkonnas — võrdle kontrollitud pakkujaid Ruumlys. Või saada üks päring ja saa 2–3 pakkumist, tavaliselt 24 tunni jooksul.`,
  en: (n, c) => `${n} in ${c} — compare verified providers on Ruumly, or send one request and get 2–3 offers, usually within 24 hours.`,
  ru: (n, c) => `${n} — ${c}. Сравните проверенных поставщиков на Ruumly или отправьте один запрос и получите 2–3 предложения обычно за 24 часа.`,
  lv: (n, c) => `${n} ${c} — salīdzini pārbaudītus pakalpojumu sniedzējus Ruumly. Vai sūti vienu pieprasījumu un saņem 2–3 piedāvājumus 24 stundās.`,
  lt: (n, c) => `${n} ${c} — palyginkite patikrintus tiekėjus Ruumly. Arba atsiųskite vieną užklausą ir gaukite 2–3 pasiūlymus per 24 val.`,
};

// ── Homepage copy per language ─────────────────────────────────────────────────
//
// Geography honesty (see estonia-space-hub/CLAUDE.md): the directory covers all
// of Estonia and the concierge runs Tallinn/Harjumaa first. It previously
// claimed "across the Baltics" / "visā Latvijā" / "visoje Lietuvoje" — coverage
// the business does not have.

// No em dash here: withSiteName() appends " — Ruumly", and two of them in one
// title reads badly in a search result.
const HOME_TITLE: Record<Lang, string> = {
  et: "Kolimisabi Eestis: üks päring, 2–3 pakkumist",
  en: "Moving in Estonia: one request, 2–3 offers",
  ru: "Переезд в Эстонии: один запрос, 2–3 предложения",
  lv: "Pārvākšanās Igaunijā: viens pieprasījums, 2–3 piedāvājumi",
  lt: "Perkraustymas Estijoje: viena užklausa, 2–3 pasiūlymai",
};

const HOME_DESC: Record<Lang, string> = {
  et: "Kolimine, laopind, haagis, kaubik, koristus, pakkimine ja kindlustus — kõik ühest kohast. Saada üks päring, toome 2–3 pakkumist, tavaliselt 24 tunniga.",
  en: "Movers, storage, trailers, vans, cleaning, packing and insurance across Estonia. Send one request and get 2–3 offers, usually within 24 hours.",
  ru: "Переезд, склад, прицепы, фургоны, уборка, упаковка и страхование по всей Эстонии. Один запрос — 2–3 предложения, обычно за 24 часа.",
  lv: "Pārvākšanās, noliktavas, piekabes, furgoni, uzkopšana, iepakošana un apdrošināšana Igaunijā. Viens pieprasījums — 2–3 piedāvājumi 24 stundās.",
  lt: "Perkraustymas, sandėliai, priekabos, mikroautobusai, valymas, pakavimas ir draudimas Estijoje. Viena užklausa — 2–3 pasiūlymai per 24 val.",
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
  /** false when the route fell through to the generic fallback — the visitor
   *  head-rewrite skips those so an unrecognised page keeps the origin's own
   *  tags instead of being relabelled with the homepage title. */
  specific?: boolean;
  title: string;
  description: string;
  image: string;
  lang: Lang;
  canonicalUrl: string;
}

const SITE_NAME    = "Ruumly";
const DEFAULT_IMAGE = "https://ruumly.eu/ruumly-og.png?v=3";
// Bump to invalidate cached OG HTML at the edge (caches.default persists across
// deploys, so changing og:image/title/desc otherwise won't reach crawlers until
// the entry's TTL expires).
const OG_CACHE_VERSION = "4";
const DEFAULT_TITLE = HOME_TITLE.en;
const DEFAULT_DESC  = HOME_DESC.en;

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
  siteName?:            string;
  heroSubtitle?:        string;
  showMovingService?:   boolean;
  showTrailerService?:  boolean;
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

/** Listing detail routes carry a GUID; the same section with a city slug is a hub. */
function isUuid(s: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(s);
}

/**
 * City slugs in URLs are ASCII-folded ("parnu", "johvi", "kohtla-jarve"), so
 * capitalising the slug produces "Parnu" — visibly wrong to an Estonian reader
 * in a page title. Ask the API which cities exist and match on the folded form
 * to recover the real spelling.
 */
function foldCity(city: string): string {
  return city
    .toLowerCase()
    .replace(/[õöô]/g, "o").replace(/[äàáâ]/g, "a").replace(/[üùúû]/g, "u")
    .replace(/[šś]/g, "s").replace(/[žź]/g, "z").replace(/[čć]/g, "c")
    .replace(/[ēėę]/g, "e").replace(/[īį]/g, "i").replace(/[ā]/g, "a")
    .replace(/[ķ]/g, "k").replace(/[ļ]/g, "l").replace(/[ņ]/g, "n").replace(/[ū]/g, "u")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "");
}

async function resolveCityName(slug: string, section: string, api: string): Promise<string> {
  // The storage hub lives at /storage/{city} but its ListingType is `warehouse`.
  const type   = section === "storage" ? "warehouse" : section;
  const cities = await apiFetch<{ city: string }[]>(
    `${api}/api/locations/cities?type=${encodeURIComponent(type)}`,
  );
  const match = Array.isArray(cities)
    ? cities.find((c) => c?.city && foldCity(c.city) === slug)
    : undefined;
  return match?.city ?? capitalize(slug);
}

/** Route section → the service its city hub represents. */
const HUB_SECTIONS: Record<string, Service> = {
  storage:   "storage",
  moving:    "moving",
  trailer:   "trailer",
  vanrental: "vanrental",
  cleaning:  "cleaning",
  packing:   "packing",
  insurance: "insurance",
};

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

  // ── Service × city hubs (all seven services, not just storage) ──────────────
  // `moving`/`trailer` double as listing-detail routes; a GUID param means the
  // detail page, anything else is the city hub.
  const hubService = HUB_SECTIONS[section];
  if (hubService && param && !isUuid(param)) {
    const city = await resolveCityName(param, section, api);
    const noun = SERVICE_NOUN[lang][hubService];

    // Emit the hub head even when the city has no supply yet: the page still
    // renders (with nearest-city fallbacks), and a correct title beats the
    // default storage one either way.
    return {
      lang,
      title:       HUB_TITLE[lang](noun, city),
      description: HUB_DESC[lang](noun, city),
      image:       DEFAULT_IMAGE,
    };
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
    // Belt-and-suspenders for hidden verticals: even though the backend 404s
    // disabled-type listings (so apiFetch returns null below), explicitly skip
    // building a listing preview for moving/trailer when the corresponding
    // service flag is off — otherwise a shared link to a hidden vertical could
    // leak a rich OG card. On settings-fetch failure we leave it to the listing
    // fetch result (stay resilient rather than over-suppress).
    if (section === "moving" || section === "trailer") {
      const settings = await apiFetch<ApiSettings>(`${api}/api/settings/public`);
      const enabled =
        section === "moving" ? settings?.showMovingService : settings?.showTrailerService;
      if (settings && enabled === false) {
        return {
          lang, specific: false,
          title: HOME_TITLE[lang], description: HOME_DESC[lang], image: DEFAULT_IMAGE,
        };
      }
    }

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
      description: truncate(settings?.heroSubtitle || DEFAULT_DESC),
      image:       DEFAULT_IMAGE,
    };
  }

  // ── Homepage ────────────────────────────────────────────────────────────────
  if (!section) {
    return {
      lang,
      title:       HOME_TITLE[lang],
      description: HOME_DESC[lang],
      image:       DEFAULT_IMAGE,
    };
  }

  // ── Generic fallback ────────────────────────────────────────────────────────
  // Language-correct rather than always English, but flagged non-specific: a
  // route we don't model (about, faq, search, provider …) must not be given the
  // homepage's title.
  return {
    lang, specific: false,
    title: HOME_TITLE[lang], description: HOME_DESC[lang], image: DEFAULT_IMAGE,
  };
}

// ── Utilities ─────────────────────────────────────────────────────────────────

function firstImage(images: string[] | undefined): string {
  return images?.[0] ?? DEFAULT_IMAGE;
}

function capitalize(s: string): string {
  return s.charAt(0).toUpperCase() + s.slice(1);
}

// ── Dev-mode debug page ───────────────────────────────────────────────────────

function renderDevDebugPage(url: URL): string {
  const path = url.pathname + url.search;
  return `<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8">
    <title>Ruumly Social Preview Worker — Dev Mode</title>
    <style>
      body { font-family: -apple-system, system-ui, sans-serif; max-width: 720px; margin: 2rem auto; padding: 0 1rem; line-height: 1.5; }
      code { background: #f4f4f4; padding: 2px 6px; border-radius: 3px; font-size: 0.92em; }
      pre { background: #1e1e1e; color: #d4d4d4; padding: 12px; border-radius: 6px; overflow-x: auto; }
      .pill { display: inline-block; background: #e8f4ff; color: #0066cc; padding: 2px 8px; border-radius: 12px; font-size: 0.85em; }
    </style>
  </head>
  <body>
    <h1>🤖 Ruumly Social Preview Worker</h1>
    <p class="pill">Running in dev mode at ${escHtml(url.host)}</p>
    <p>This worker only serves HTML to <strong>social crawlers</strong>. Regular browser
    requests are forwarded to the Vercel origin in production — but in <code>wrangler dev</code>
    there is no real origin to forward to, so you're seeing this debug page instead.</p>

    <h2>Test the worker with crawler user-agents</h2>
    <p>Open a new terminal and run any of these — you'll get the rendered OG HTML:</p>

    <pre><code>curl -A "facebookexternalhit/1.1" "http://${escHtml(url.host)}${escHtml(path)}"</code></pre>
    <pre><code>curl -A "Twitterbot/1.0" "http://${escHtml(url.host)}/et/"</code></pre>
    <pre><code>curl -A "TelegramBot (like TwitterBot)" "http://${escHtml(url.host)}/en/storage/tallinn"</code></pre>
    <pre><code>curl -A "LinkedInBot/1.0" "http://${escHtml(url.host)}/ru/partner/laobox"</code></pre>

    <h2>What you should see</h2>
    <p>Every command above should return a small HTML document with proper
    <code>&lt;meta property="og:title"&gt;</code>, <code>og:description</code>, and
    <code>og:image</code> tags — possibly fetched from the live api.ruumly.eu backend.</p>

    <h2>Detected crawlers</h2>
    <p>facebookexternalhit, LinkedInBot, Twitterbot, TelegramBot, WhatsApp, Slackbot,
    Discordbot, SkypeUriPreview, vkShare, redditbot, applebot.</p>

    <h2>Going to production</h2>
    <p>Run <code>npm run deploy</code>, then attach the route
    <code>ruumly.eu/*</code> in the Cloudflare dashboard. Once attached,
    non-crawlers will be forwarded to Vercel automatically (Cloudflare prevents loops).</p>
  </body>
</html>`;
}

// ── Head injection into the real origin HTML ──────────────────────────────────
//
// The SPA sets its head client-side (@unhead/react), so the HTML Vercel serves
// carries the SAME default title and description on every route. Google renders
// JS and still ranks the pages, but the SNIPPET it shows is frequently the stale
// default: /et/partner/alexela-haagiserent sat at position 6.98 with 582
// impressions and ZERO clicks, because a user searching "alexela haagise rent"
// was shown "Ruumly — Laopinnad Eestis / Leia ja rendi laopind Eestis".
//
// So instead of answering crawlers with a synthetic page, pass the real origin
// response through HTMLRewriter and correct only the head tags. Same content for
// everyone (this is not cloaking), the SPA overwrites them on hydration for real
// visitors, and no route loses its rendered body or internal links.

/** Replace an element's text content wholesale. */
class SetText {
  private written = false;
  constructor(private readonly value: string) {}
  text(chunk: Text) {
    // The original text arrives in chunks; emit ours once, drop the rest.
    if (!this.written) {
      chunk.replace(this.value);
      this.written = true;
    } else {
      chunk.remove();
    }
  }
}

/** Replace one attribute on a matched element. */
class SetAttr {
  constructor(private readonly name: string, private readonly value: string) {}
  element(el: Element) {
    el.setAttribute(this.name, this.value);
  }
}

/** Append tags that may be missing from the shell (canonical, robots). */
class AppendToHead {
  constructor(private readonly html: string) {}
  element(el: Element) {
    el.append(this.html, { html: true });
  }
}

function rewriteHead(response: Response, og: OgData): Response {
  const title = withSiteName(og.title);
  return new HTMLRewriter()
    .on("title", new SetText(title))
    .on('meta[name="description"]',        new SetAttr("content", og.description))
    .on('meta[property="og:title"]',       new SetAttr("content", title))
    .on('meta[property="og:description"]', new SetAttr("content", og.description))
    .on('meta[property="og:image"]',       new SetAttr("content", og.image))
    .on('meta[property="og:url"]',         new SetAttr("content", og.canonicalUrl))
    .on('meta[name="twitter:title"]',       new SetAttr("content", title))
    .on('meta[name="twitter:description"]', new SetAttr("content", og.description))
    .on('meta[name="twitter:image"]',       new SetAttr("content", og.image))
    // The Vite shell has no canonical of its own — the SPA injects one at
    // runtime, which is exactly what Google may never see.
    .on("head", new AppendToHead(
      `<link rel="canonical" href="${escAttr(og.canonicalUrl)}">`,
    ))
    .transform(response);
}

/** Only HTML documents get rewritten — never assets, API calls or redirects. */
function isHtmlDocument(response: Response): boolean {
  const type = response.headers.get("Content-Type") ?? "";
  return response.ok && type.toLowerCase().includes("text/html");
}

// ── Worker entry point ────────────────────────────────────────────────────────

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    const userAgent = request.headers.get("User-Agent") ?? "";
    const url       = new URL(request.url);
    const isDev     = url.hostname === "localhost" || url.hostname === "127.0.0.1";

    // ── Non-crawlers in DEV: return a debug page (origin not reachable from wrangler dev) ──
    if (!isCrawler(userAgent) && isDev) {
      return new Response(renderDevDebugPage(url), {
        headers: { "Content-Type": "text/html; charset=utf-8" },
      });
    }

    // ── Everyone else (real visitors, Googlebot, Bingbot) in PRODUCTION ──────
    // Serve the REAL origin page, with only its head tags corrected. Assets and
    // non-GET requests pass through untouched.
    if (!isCrawler(userAgent)) {
      if (request.method !== "GET" || isStaticAsset(url.pathname)) {
        return fetch(request);
      }

      // Resolve metadata and fetch origin concurrently — the API subrequest is
      // edge-cached for an hour, so this costs one round trip on a cold key and
      // nothing afterwards.
      const [originResponse, ogBase] = await Promise.all([
        fetch(request),
        resolveOgData(url, env).catch(() => null),
      ]);

      if (!ogBase || ogBase.specific === false || !isHtmlDocument(originResponse)) {
        return originResponse;
      }

      // The canonical deliberately drops the query string: /search?type=…&city=…
      // and /request?category=…&city=… are filtered views of a page that already
      // exists on its own URL, and Google has crawled ~300 of those parameter
      // permutations. Pointing them at the clean path consolidates the signal
      // instead of splitting it.
      return rewriteHead(originResponse, {
        ...ogBase,
        canonicalUrl: `${env.SITE_URL}${url.pathname}`,
      });
    }

    // ── Crawlers requesting a static asset (og:image, icons, manifest, …): ──
    // serve the real file from origin, never the OG HTML, or the card is blank.
    if (isStaticAsset(url.pathname)) {
      return fetch(request);
    }

    // ── Crawlers: serve server-rendered OG HTML ─────────────────────────────

    // 1. Check Workers edge cache first
    const cache    = caches.default;
    // Versioned cache key — bumping OG_CACHE_VERSION invalidates stale OG HTML.
    const cacheUrlObj = new URL(request.url);
    cacheUrlObj.searchParams.set("_ogv", OG_CACHE_VERSION);
    const cacheKey = new Request(cacheUrlObj.toString(), { method: "GET" });
    const cached   = await cache.match(cacheKey);
    if (cached) return cached;

    // 2. Resolve metadata from API
    // (url already declared above)
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
