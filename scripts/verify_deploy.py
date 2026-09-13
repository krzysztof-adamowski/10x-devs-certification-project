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
    """Return (status, body_bytes, landed_url, headers). Raises only for transport failures.

    `landed_url` is where the request actually ended up. urllib follows 301/302/303/307
    through the default opener, so without it a redirect is invisible: the caller would
    report success for the requested URL while having checked a different page.

    `headers` is read INSIDE the `with` block. Returning from outside it -- which this
    function used to do -- closes the response first, so Content-Type was destroyed
    before any caller could see it, leaving the status code as the only oracle.
    """
    req = request.Request(url, headers={"User-Agent": USER_AGENT})
    try:
        with request.urlopen(req, timeout=timeout) as response:
            return response.status, response.read(), response.url, response.headers
    except error.HTTPError as exc:
        # An HTTP error response is still a response; read it so the caller can judge.
        return exc.code, exc.read(), exc.url, exc.headers


def media_type(headers):
    """The media type alone, lowercased -- or None when no Content-Type was sent."""
    value = headers.get("Content-Type") if headers else None
    if not value:
        return None
    return value.split(";", 1)[0].strip().lower()


def redirect_rejection(requested, landed):
    """Why a root redirect is unacceptable, or None when it is acceptable.

    Three cases, and telling them apart is the whole point:

      - A different HOST: nothing about this deploy can be verified through it.
      - A different PATH on the same host: the app answered with some other page.
        A sign-in page is the case that matters, and it arrives as 200 text/html --
        which neither the status check nor the content-type check below can catch,
        because a root page legitimately IS html.
      - An http->https upgrade on the same host and path: httpsOnly doing its job.
        ACCEPTED, deliberately. The 2026-09-10 review narrowed the original fix to
        allow exactly this; failing every same-host redirect would report a working
        platform guarantee as a broken deploy.
    """
    if landed == requested:
        return None
    want, got = urlsplit(requested), urlsplit(landed)
    if want.netloc != got.netloc:
        return "redirected to a different host: %s" % landed
    if (want.path or "/") != (got.path or "/"):
        return (
            "redirected to a different path on the same host: %s\n"
            "       A gated site still answers 200 through its sign-in page, so\n"
            "       every asset checked below would be that page's, not this app's."
            % landed
        )
    return None


def asset_rejection(content_type):
    """Why an asset's media type is unacceptable, or None when it is acceptable.

    The oracle a status code cannot be. With .AllowAnonymous() removed from
    MapStaticAssets(), a stylesheet resolves 302 -> /Account/Login -> 200 and the
    sign-in page is served as the stylesheet. The status is 200 either way.
    """
    if content_type is None:
        return "no Content-Type header"
    if content_type == "text/html":
        return "served as text/html -- that is a page, not an asset"
    return None


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

    Returns (body_bytes, landed_url, headers).
    """
    deadline = time.time() + warmup_seconds
    delay = 2.0
    attempt = 0
    last = None

    while True:
        attempt += 1
        try:
            status, body, landed, headers = fetch(url, timeout)
            if status == 200:
                print("  [200] %s  (attempt %d)" % (url, attempt))
                if landed != url:
                    # Say so, and check the page we actually got rather than the one we
                    # asked for. When Identity lands and / redirects to a login page,
                    # silently verifying that page's assets would be a false green.
                    print("  [-->] redirected to %s" % landed)
                    rejection = redirect_rejection(url, landed)
                    if rejection:
                        fail("the root page %s" % rejection)
                return body, landed, headers
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
    body, landed, root_headers = fetch_root(base, args.warmup_seconds, args.timeout)

    # The only thing watching ASPNETCORE_ENVIRONMENT. It is absent on the live site, so
    # the container runs Production by framework default -- which is what makes UseHsts()
    # and the Key Vault key-ring encryption fire. Adding it as an app setting would turn
    # both off with every response still 200. Unassertable in-process: UseHsts() excludes
    # localhost by default, measured 2026-09-14.
    if not root_headers.get("Strict-Transport-Security"):
        fail(
            "the root page carries no Strict-Transport-Security header.\n"
            "       UseHsts() only runs outside Development, so the container is no\n"
            "       longer running as Production -- most likely because an\n"
            "       ASPNETCORE_ENVIRONMENT app setting was added. That also disables\n"
            "       key-ring encryption, which signs every auth cookie."
        )
    print("  [hsts] %s" % root_headers.get("Strict-Transport-Security"))

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
            status, _, _, headers = fetch(url, args.timeout)
        except Exception as exc:
            print("  [ERR ] %s" % url)
            fail("asset request failed: %s (%s: %s)" % (url, type(exc).__name__, exc))
        received = media_type(headers)
        print("  [%d] %-24s %s" % (status, received or "(no content-type)", url))
        if status != 200:
            fail(
                "asset returned %d: %s\n"
                "       The page deployed but its assets do not resolve. This is the\n"
                "       runtime signature of a wrong-shaped archive." % (status, url)
            )
        # Status is not the oracle here. A gated asset resolves 302 -> sign-in -> 200,
        # so the integer above says nothing; the media type does.
        rejection = asset_rejection(received)
        if rejection:
            fail(
                "asset %s: %s\n"
                "       A stylesheet or script that arrives as a page means the asset\n"
                "       endpoints lost .AllowAnonymous() and the sign-in page is being\n"
                "       served in its place -- at 200, which is why this check exists."
                % (url, rejection)
            )

    print("")
    print(
        "DEPLOY VERIFIED -- root page 200 on its own path with HSTS, and all %d"
        " same-origin asset(s) 200 and not html." % len(resolved)
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
