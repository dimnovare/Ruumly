"""Website probe: DNS, HTTP status, redirect target, contacts printed on the site.

Polite by construction: one homepage GET plus at most two contact-page GETs per
host, a real User-Agent, robots.txt honoured for the contact pages, 15 s
timeouts, and no request at all to a host that resolves to a private, loopback
or link-local address (a standalone-script stand-in for the backend's
OutboundEndpointValidator — this tool never runs inside the API).
"""
from __future__ import annotations

import html
import ipaddress
import json
import re
import socket
import time
import urllib.parse
import urllib.request
import urllib.robotparser
from functools import lru_cache

import requests

UA = "Mozilla/5.0 (compatible; RuumlyDirectoryScan/1.0; +https://ruumly.eu)"
TIMEOUT = 15
MAX_BYTES = 1_500_000

EMAIL_RE = re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,24}")
# Baltic numbers: +372 / +371 / +370 (or 00…) followed by 7-8 digits, with spacing.
PHONE_RE = re.compile(r"(?:\+|00)\s?37[012][\s\-().]*\d(?:[\s\-().]*\d){5,9}")
CONTACT_HINTS = ("kontakt", "contact", "kontakti", "kontaktai", "kontaktid", "контакт", "yhendust", "ühendust",
                 "about", "meist")
PARKED_HINTS = (
    "domain is for sale", "domain may be for sale", "this domain is parked", "buy this domain",
    "domeen on müügis", "domeen müüa", "domēns ir pārdošanā", "domenas parduodamas", "parked free",
    "sedo", "dan.com", "afternic", "hugedomains", "veebileht on ehitamisel", "coming soon",
    "default web site page", "index of /", "welcome to nginx", "apache2 default page",
    "website is under construction", "account suspended", "this account has been suspended",
)
IGNORE_EMAIL_DOMAINS = ("example.com", "sentry.io", "wixpress.com", "sentry-next.wixpress.com",
                        "domain.com", "email.com", "yourdomain", "godaddy.com")
IGNORE_EMAIL_SUFFIX = (".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".css", ".js")
# Form placeholders ("sinu@email.ee" = "your@email") — never a real contact.
PLACEHOLDER_LOCALS = {"sinu", "your", "you", "name", "nimi", "example", "test", "jusu", "tavo", "vas",
                      "email", "e-mail", "mail", "user", "username", "john", "jane", "vards", "vardas",
                      "epasts", "pasts", "jusu.epasts", "el.pastas", "pastas", "post", "kiri"}


def host_of(url: str | None) -> str:
    if not url:
        return ""
    u = url.strip()
    if "://" not in u:
        u = "http://" + u
    h = (urllib.parse.urlsplit(u).hostname or "").lower()
    return h[4:] if h.startswith("www.") else h


def reg_domain(host: str) -> str:
    """Registrable domain — good enough for .ee/.lv/.lt/.com/.eu (no 2-level public suffixes there)."""
    parts = (host or "").lower().split(".")
    return ".".join(parts[-2:]) if len(parts) >= 2 else host


def digits(phone: str | None) -> str:
    d = re.sub(r"\D", "", phone or "")
    if d.startswith("00"):
        d = d[2:]
    # Compare on the national part: drop a leading 37x country code.
    if len(d) > 8 and d[:3] in ("372", "371", "370"):
        d = d[3:]
    if d.startswith("8") and len(d) == 9:  # LT trunk prefix 8xxxxxxxx
        d = d[1:]
    return d


def _public_ip(host: str) -> tuple[bool, str]:
    try:
        infos = socket.getaddrinfo(host, None)
    except socket.gaierror as e:
        return False, f"dns_fail:{e.errno}"
    for info in infos:
        ip = ipaddress.ip_address(info[4][0])
        if ip.is_private or ip.is_loopback or ip.is_link_local or ip.is_reserved or ip.is_multicast:
            return False, f"non_public_ip:{ip}"
    return True, infos[0][4][0]


@lru_cache(maxsize=4096)
def has_mx(domain: str) -> str:
    """'yes' / 'no' / 'nxdomain' / 'error' via DNS-over-HTTPS (public domain names only)."""
    if not domain:
        return "error"
    try:
        r = requests.get("https://cloudflare-dns.com/dns-query", params={"name": domain, "type": "MX"},
                         headers={"Accept": "application/dns-json"}, timeout=10)
        j = r.json()
        if j.get("Status") == 3:
            return "nxdomain"
        if any(a.get("type") == 15 for a in j.get("Answer", [])):
            return "yes"
        # RFC 5321 implicit MX: an A record also accepts mail.
        r = requests.get("https://cloudflare-dns.com/dns-query", params={"name": domain, "type": "A"},
                         headers={"Accept": "application/dns-json"}, timeout=10)
        return "a_only" if any(a.get("type") == 1 for a in r.json().get("Answer", [])) else "no"
    except Exception:
        return "error"


class BlockedHost(Exception):
    """A hop (first URL or any redirect) resolved to a non-public address."""


MAX_HOPS = 8


def _guarded_request(session, url, timeout=TIMEOUT, stream=True):
    """GET with redirects followed BY HAND so every hop — not just the first
    URL — is checked: http(s) only, and the host must resolve to public IPs.
    A provider's site redirecting to http://192.168.1.1/ is refused.

    Residual risk, accepted for a local one-shot report script: DNS can change
    between our check and requests' own resolution (rebinding). The response is
    only regex-scanned for e-mails/phones, never executed or forwarded.
    """
    for _ in range(MAX_HOPS + 1):
        parts = urllib.parse.urlsplit(url)
        if parts.scheme not in ("http", "https") or not parts.hostname:
            raise BlockedHost(f"scheme/host not allowed: {url[:80]}")
        ok, info = _public_ip(parts.hostname)
        if not ok:
            raise BlockedHost(info)
        r = session.get(url, timeout=timeout, allow_redirects=False, stream=stream,
                        headers={"User-Agent": UA, "Accept-Language": "et,en;q=0.8,ru;q=0.5,lv;q=0.5,lt;q=0.5"})
        if r.is_redirect and r.headers.get("location"):
            url = urllib.parse.urljoin(url, r.headers["location"])
            r.close()
            continue
        return r
    raise requests.exceptions.TooManyRedirects(url)


def _get(session, url):
    r = _guarded_request(session, url)
    body = b""
    started = time.monotonic()
    for chunk in r.iter_content(65536):
        body += chunk
        # `timeout` is per socket read; a server that trickles bytes would
        # otherwise hold a worker forever.
        if len(body) > MAX_BYTES or time.monotonic() - started > 30:
            break
    r.close()
    enc = r.encoding or "utf-8"
    if enc.lower() in ("iso-8859-1",):  # requests' default when no charset; most of these sites are utf-8
        enc = "utf-8"
    return r, body.decode(enc, errors="replace")


def _cf_decode(hexstr: str) -> str:
    try:
        b = bytes.fromhex(hexstr)
        return "".join(chr(c ^ b[0]) for c in b[1:])
    except ValueError:
        return ""


def extract_contacts(text: str) -> tuple[set[str], set[str]]:
    t = html.unescape(text)
    emails = set()
    for m in re.findall(r'data-cfemail="([0-9a-fA-F]+)"', t):
        emails.add(_cf_decode(m))
    t2 = re.sub(r"\s*(\[at\]|\(at\)|\[ät\])\s*", "@", t, flags=re.I)
    for m in EMAIL_RE.findall(urllib.parse.unquote(t2)):
        emails.add(m)
    emails = {e.strip(".").lower() for e in emails
              if e and not e.lower().endswith(IGNORE_EMAIL_SUFFIX)
              and not any(d in e.lower() for d in IGNORE_EMAIL_DOMAINS)
              and e.split("@")[0].lower() not in PLACEHOLDER_LOCALS}
    phones = set()
    for m in re.findall(r'href="tel:([^"]+)"', t):
        phones.add(re.sub(r"[^\d+]", "", m))
    for m in PHONE_RE.findall(re.sub(r"<[^>]+>", " ", t)):
        phones.add(re.sub(r"[^\d+]", "", m))
    phones = {p for p in phones if 7 <= len(digits(p)) <= 9}
    return emails, phones


def _contact_links(base: str, text: str) -> list[str]:
    links = []
    for href, label in re.findall(r'<a[^>]+href="([^"#]+)"[^>]*>(.*?)</a>', text, flags=re.I | re.S):
        h = href.lower()
        lab = re.sub(r"<[^>]+>", "", label).lower()
        if h.startswith(("mailto:", "tel:", "javascript:")):
            continue
        if any(k in h or k in lab for k in CONTACT_HINTS):
            u = urllib.parse.urljoin(base, href)
            if host_of(u) == host_of(base) and u not in links:
                links.append(u)
    # Prefer a URL that says contact over one that says about.
    links.sort(key=lambda u: 0 if re.search(r"kontakt|contact|контакт", u.lower()) else 1)
    return links[:2]


def _robots_ok(session, url) -> bool:
    try:
        parts = urllib.parse.urlsplit(url)
        rp = urllib.robotparser.RobotFileParser()
        r = _guarded_request(session, f"{parts.scheme}://{parts.netloc}/robots.txt", timeout=8, stream=False)
        if r.status_code >= 400:
            return True
        rp.parse(r.text.splitlines())
        return rp.can_fetch(UA, url)
    except Exception:
        return True


def probe(url: str | None) -> dict:
    res = {"site_input": url or "", "site_host": host_of(url), "site_dns": "", "site_status": "",
           "site_final_url": "", "site_redirect_domain": "", "site_parked": "", "site_error": "",
           "site_emails": [], "site_phones": [], "site_pages": 0,
           "site_root_status": "", "site_root_url": ""}
    if not url:
        res["site_status"] = "no_website"
        return res
    host = res["site_host"]
    start = url.strip() if "://" in url else "http://" + url.strip()
    ok, info = _public_ip(urllib.parse.urlsplit(start).hostname or host)
    if not ok:
        # Retry with www. — some sites only answer there. Fetch the host that
        # was actually validated, not the original one.
        ok2, info2 = _public_ip("www." + host)
        if not ok2:
            res["site_dns"] = info
            res["site_status"] = "dns_fail" if info.startswith("dns_fail") else "blocked_ip"
            return res
        p = urllib.parse.urlsplit(start)
        start = urllib.parse.urlunsplit((p.scheme, "www." + host, p.path, p.query, p.fragment))
    res["site_dns"] = "ok"
    s = requests.Session()
    try:
        r, text = _get(s, start)
    except BlockedHost as e:
        res["site_status"], res["site_error"] = "blocked_ip", str(e)[:120]
        return res
    except requests.exceptions.SSLError:
        try:
            r, text = _get(s, start.replace("https://", "http://"))
            res["site_error"] = "ssl_error_http_ok"
        except Exception as e:
            res["site_status"], res["site_error"] = "conn_error", type(e).__name__
            return res
    except Exception as e:
        res["site_status"], res["site_error"] = "conn_error", type(e).__name__
        return res
    if r.status_code >= 500:  # one retry: 5xx is often a transient hiccup
        try:
            r, text = _get(s, start)
        except Exception:
            pass
    res["site_status"] = str(r.status_code)
    res["site_final_url"] = r.url
    # A dead DEEP link (the trailer page moved) is not a dead business: check the root.
    parts = urllib.parse.urlsplit(r.url)
    if r.status_code in (404, 410) and parts.path not in ("", "/"):
        root = f"{parts.scheme}://{parts.netloc}/"
        try:
            rr, rtext = _get(s, root)
            res["site_root_status"], res["site_root_url"] = str(rr.status_code), rr.url
            if rr.status_code < 400:
                r, text = rr, rtext
        except Exception:
            res["site_root_status"] = "conn_error"
    if reg_domain(host_of(r.url)) != reg_domain(host):
        res["site_redirect_domain"] = reg_domain(host_of(r.url))
    low = text.lower()
    hits = [h for h in PARKED_HINTS if h in low]
    # A tiny page that mentions a hint is parked; a real site that says "coming soon" in a footer is not.
    if hits and len(re.sub(r"<[^>]+>", "", text)) < 6000:
        res["site_parked"] = hits[0]
    emails, phones = extract_contacts(text)
    pages = 1
    if r.status_code < 400:
        for link in _contact_links(r.url, text):
            if not _robots_ok(s, link):
                continue
            try:
                r2, t2 = _get(s, link)
                if r2.status_code < 400:
                    e2, p2 = extract_contacts(t2)
                    emails |= e2
                    phones |= p2
                    pages += 1
            except Exception:
                pass
    res["site_emails"] = sorted(emails)
    res["site_phones"] = sorted(phones)
    res["site_pages"] = pages
    return res


def to_jsonable(d):
    return json.loads(json.dumps(d, default=list))
