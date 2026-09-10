#!/usr/bin/env python3
"""Decide whether a deploy actually worked -- not whether the deploy command succeeded.

A green `az webapp deploy` is not evidence. The 2026-09-08 deploy reported
`RuntimeSuccessful` and was only trusted after every stylesheet and script URL the
rendered root references was confirmed to return 200 on the live host. That check is
what this script encodes, so it runs on every deploy instead of when someone remembers.

It fetches the root page, extracts the stylesheet `href`s and script `src`s the page
actually references, discards off-origin URLs, and asserts 200 for each of the rest.

Two behaviours are deliberate:

  - Warm-up loop. The container restarts after a deploy, so a cold first request is
    expected on a *correct* deploy. Without the retry the pipeline produces false reds,
    which trains everyone to ignore it.
  - Same-origin filtering. A CDN outage is not this deploy's fault and must not fail
    the run.

Standard library only, so CI needs no dependency install step.

Usage (from the repo root, on Windows or Linux, identically):

    python scripts/verify_deploy.py
    python scripts/verify_deploy.py --base-url https://example.azurewebsites.net
    python scripts/verify_deploy.py --warmup-seconds 180
"""

import argparse
import sys
import time
from html.parser import HTMLParser
from urllib import error, request
from urllib.parse import urljoin, urlsplit

DEFAULT_BASE_URL = "https://tenexcards-ka.azurewebsites.net"
DEFAULT_WARMUP_SECONDS = 120
DEFAULT_TIMEOUT = 30
USER_AGENT = "tenexcards-verify-deploy/1"

# The only 4xx statuses App Service emits transiently, so the only ones worth waiting
# out: 429 is front-end throttling, and 403 is the "this web app is stopped" page during
# a platform stop or tier operation. Every other 4xx means the app answered definitively.
TRANSIENT_4XX = frozenset((403, 429))


class AssetCollector(HTMLParser):
    """Collect stylesheet hrefs and script srcs in document order, without duplicates."""

    def __init__(self):
        HTMLParser.__init__(self)
        self.assets = []

    def handle_starttag(self, tag, attrs):
        attributes = dict(attrs)
        url = None
        if tag == "link":
            # rel is a space-separated token list; "stylesheet" may not be alone.
            rels = (attributes.get("rel") or "").lower().split()
            if "stylesheet" in rels:
                url = attributes.get("href")
        elif tag == "script":
            url = attributes.get("src")
        if url:
            url = url.strip()
        if url and url not in self.assets:
            self.assets.append(url)


def fetch(url, timeout):
    """Return (status, body_bytes, landed_url). Raises only for transport failures.

    `landed_url` is where the request actually ended up. urllib follows 301/302/303/307
    through the default opener, so without it a redirect is invisible: the caller would
    report success for the requested URL while having checked a different page.
    """
    req = request.Request(url, headers={"User-Agent": USER_AGENT})
    try:
        with request.urlopen(req, timeout=timeout) as response:
            return response.status, response.read(), response.url
    except error.HTTPError as exc:
        # An HTTP error response is still a response; read it so the caller can judge.
        return exc.code, exc.read(), exc.url


def fail(message):
    # Keep the per-URL log and the verdict in order in a CI log, which merges the
    # two streams and does not otherwise flush stdout until exit.
    sys.stdout.flush()
    print("\nDEPLOY VERIFICATION FAILED\n  error: %s" % message, file=sys.stderr)
    sys.exit(1)


def fetch_root(url, warmup_seconds, timeout):
    """Fetch the root page, retrying while the app is plausibly still warming up.

    Retries transport failures and 5xx -- what a restarting container actually produces
    -- plus the two 4xx statuses App Service emits transiently: 429 (front-end
    throttling) and 403 (served as "this web app is stopped" during a platform stop or
    tier operation). For those, waiting genuinely does change the outcome.

    Every other 4xx is definitive: the app answered and said this URL is not there, and
    no amount of waiting changes that. Retrying a 404 would burn the whole warm-up
    budget to reach the same verdict, on every genuinely broken deploy.
    """
    deadline = time.time() + warmup_seconds
    delay = 2.0
    attempt = 0
    last = None

    while True:
        attempt += 1
        try:
            status, body, landed = fetch(url, timeout)
            if status == 200:
                print("  [200] %s  (attempt %d)" % (url, attempt))
                if landed != url:
                    # Say so, and check the page we actually got rather than the one we
                    # asked for. When Identity lands and / redirects to a login page,
                    # silently verifying that page's assets would be a false green.
                    print("  [-->] redirected to %s" % landed)
                    # A scheme upgrade to https on the same host is httpsOnly doing its
                    # job -- log it and carry on. A different HOST is another matter:
                    # nothing about this deploy can be verified through it.
                    if urlsplit(landed).netloc != urlsplit(url).netloc:
                        fail(
                            "the root page redirected to a different host: %s\n"
                            "       Nothing about this deploy can be verified through a\n"
                            "       redirect off the host under test." % landed
                        )
                return body, landed
            if 400 <= status < 500 and status not in TRANSIENT_4XX:
                print("  [%d] %s" % (status, url))
                fail(
                    "the root page returned %d. The app answered, so this is not a\n"
                    "       warm-up problem -- the URL is wrong, or the app is not\n"
                    "       serving it." % status
                )
            last = "HTTP %d" % status
        except Exception as exc:  # transport failure: reset, DNS, timeout
            last = "%s: %s" % (type(exc).__name__, exc)

        remaining = deadline - time.time()
        if remaining <= 0:
            fail(
                "the root page never returned 200 within the %ds warm-up budget.\n"
                "       Last attempt (%d): %s\n"
                "       URL: %s" % (warmup_seconds, attempt, last, url)
            )
        wait = min(delay, remaining)
        print("  [....] not ready yet (%s); retrying in %.0fs" % (last, wait))
        time.sleep(wait)
        delay = min(delay * 1.5, 15.0)


def same_origin(a, b):
    sa, sb = urlsplit(a), urlsplit(b)
    return (sa.scheme, sa.netloc) == (sb.scheme, sb.netloc)


def normalise_base(base_url):
    """Keep any explicit path -- pointing at a sub-path is a legitimate check target."""
    split = urlsplit(base_url)
    if not split.scheme or not split.netloc:
        fail("--base-url must be absolute, e.g. https://host.example (got: %s)" % base_url)
    return base_url if split.path else base_url + "/"


def main(argv=None):
    parser = argparse.ArgumentParser(
        description=(
            "Verify a deploy by fetching the root page and every same-origin asset "
            "it references."
        )
    )
    parser.add_argument(
        "--base-url",
        default=DEFAULT_BASE_URL,
        help="page to fetch and resolve assets against (default: %(default)s)",
    )
    parser.add_argument(
        "--warmup-seconds",
        type=int,
        default=DEFAULT_WARMUP_SECONDS,
        help="how long to keep retrying a not-yet-ready app (default: %(default)s)",
    )
    parser.add_argument(
        "--timeout",
        type=int,
        default=DEFAULT_TIMEOUT,
        help="per-request timeout in seconds (default: %(default)s)",
    )
    args = parser.parse_args(argv)

    base = normalise_base(args.base_url)

    print("verifying deploy at %s" % base)
    print("")
    print("root page (warm-up budget %ds):" % args.warmup_seconds)
    body, landed = fetch_root(base, args.warmup_seconds, args.timeout)

    collector = AssetCollector()
    collector.feed(body.decode("utf-8", errors="replace"))

    # Resolve against where we landed, not where we asked. A relative href in a page
    # served from /Account/Login resolves differently than the same href at /.
    resolved = []
    skipped = []
    for raw in collector.assets:
        absolute = urljoin(landed, raw)
        if same_origin(absolute, landed):
            if absolute not in resolved:
                resolved.append(absolute)
        else:
            skipped.append(absolute)

    print("")
    print(
        "referenced assets: %d same-origin, %d off-origin (skipped)"
        % (len(resolved), len(skipped))
    )
    for url in skipped:
        print("  [skip] %s  (off-origin; not this deploy's responsibility)" % url)

    if not resolved:
        fail(
            "the root page returned 200 but references no same-origin stylesheet or\n"
            "       script. A correct deploy of this app always serves at least one.\n"
            "       This is the signature of an archive that lost its wwwroot/, or of\n"
            "       a page that rendered an error instead of the app."
        )

    print("")
    for url in resolved:
        try:
            status, _, _ = fetch(url, args.timeout)
        except Exception as exc:
            print("  [ERR ] %s" % url)
            fail("asset request failed: %s (%s: %s)" % (url, type(exc).__name__, exc))
        print("  [%d] %s" % (status, url))
        if status != 200:
            fail(
                "asset returned %d: %s\n"
                "       The page deployed but its assets do not resolve. This is the\n"
                "       runtime signature of a wrong-shaped archive." % (status, url)
            )

    print("")
    print(
        "DEPLOY VERIFIED -- root page 200 and all %d same-origin asset(s) 200."
        % len(resolved)
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
