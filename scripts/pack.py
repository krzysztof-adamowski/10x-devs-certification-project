#!/usr/bin/env python3
"""Build the deployable archive, and refuse to produce one with a broken shape.

This script is the authority on how the 10xCards deploy archive is built. It replaces
the prose rules in TenExCards/AGENTS.md, whose failure mode is that a wrong archive
deploys *successfully* and then breaks at runtime with no signal:

  - a `publish/`-nested tree 503s
  - backslash entry names serve a page whose every asset 404s

Both were measured on 2026-08-31 / 2026-09-08; see context/deployment/deploy-plan.md.
A passing `az webapp deploy` is not evidence, so the assertions run here instead.

Python rather than shell: `zip` is not installed in Git Bash on the development
machine, so a shell packer would work in CI and fail locally. `zipfile` writes
`/`-separated names on every platform when arcnames are built as POSIX paths --
exactly the property Windows PowerShell 5.1's `Compress-Archive` lacks.

Usage (from the repo root, on Windows or Linux, identically):

    python scripts/pack.py
    python scripts/pack.py --publish-dir <dir> --out <zip>
"""

import argparse
import os
import sys
import zipfile

DEFAULT_PUBLISH_DIR = os.path.join("TenExCards", "bin", "Release", "net10.0", "publish")
DEFAULT_OUT = os.path.join("TenExCards", "bin", "publish.zip")


def build_archive(publish_dir, out_path):
    """Write every file under publish_dir into out_path at the archive root.

    Arcnames are built with os.path.relpath then normalised to `/`, so contents sit at
    the archive root with no wrapper directory and no platform separator leaks in.
    """
    out_dir = os.path.dirname(os.path.abspath(out_path))
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)

    written = 0
    with zipfile.ZipFile(out_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for root, _dirs, files in os.walk(publish_dir):
            for name in sorted(files):
                abs_path = os.path.join(root, name)
                rel = os.path.relpath(abs_path, publish_dir)
                arcname = rel.replace(os.sep, "/").replace("\\", "/")
                zf.write(abs_path, arcname)
                written += 1
    return written


def assert_shape(out_path):
    """Read the archive back and evaluate all four shape assertions.

    Reads rather than trusting the write: a report derived from what we intended to
    write cannot catch a writer that betrayed us, which is the whole reason
    Compress-Archive is banned.

    Every assertion is evaluated -- this deliberately does not stop at the first
    failure. A `publish/`-nested tree violates assertions 1, 2 and 4 at once (its
    static assets land at `publish/wwwroot/...`, not `wwwroot/...`), and a fail-fast
    script would name only assertion 1 and hide the two more diagnostic ones.

    Returns a list of failure descriptions; empty means the archive is good.
    """
    with zipfile.ZipFile(out_path) as zf:
        names = zf.namelist()

    failures = []

    # 1. TenExCards.dll at the archive root. If it is missing, the site 503s.
    ok1 = "TenExCards.dll" in names
    report(1, "TenExCards.dll present at the archive root", ok1)
    if not ok1:
        nested = [n for n in names if n.endswith("/TenExCards.dll")]
        detail = "TenExCards.dll not found at the archive root"
        if nested:
            detail += "; found nested at: " + ", ".join(sorted(nested)[:5])
        else:
            detail += "; it is not in the archive at all -- is --publish-dir right?"
        failures.append("assertion 1: " + detail)

    # 2. No `publish/` prefix. A nested tree deploys successfully and then 503s,
    #    because the runtime looks for the entrypoint at the site root.
    prefixed = [n for n in names if n.startswith("publish/")]
    ok2 = not prefixed
    report(2, "no entry prefixed 'publish/'", ok2)
    if not ok2:
        failures.append(
            "assertion 2: %d entr%s nested under 'publish/' (e.g. %s) -- point "
            "--publish-dir at the publish directory itself, not its parent"
            % (len(prefixed), "y" if len(prefixed) == 1 else "ies", prefixed[0])
        )

    # 3. No backslashes. These deploy successfully and then serve a page whose
    #    every asset 404s, because the separator is part of the entry name.
    backslashed = [n for n in names if "\\" in n]
    ok3 = not backslashed
    report(3, "no entry containing a backslash", ok3)
    if not ok3:
        failures.append(
            "assertion 3: %d entr%s contain a backslash (e.g. %s) -- the archive was "
            "built by a writer that leaks Windows separators; do not use Compress-Archive"
            % (len(backslashed), "y" if len(backslashed) == 1 else "ies", backslashed[0])
        )

    # 4. Static assets present. Same class of silent failure as the others: the app
    #    starts and every stylesheet and script 404s.
    wwwroot = [n for n in names if n.startswith("wwwroot/")]
    ok4 = bool(wwwroot)
    report(4, "at least one entry under 'wwwroot/'", ok4)
    if not ok4:
        failures.append(
            "assertion 4: no entry under 'wwwroot/' -- the publish output has no static "
            "assets, so the deployed page would load with no CSS and no JS"
        )

    print("")
    print("archive:  %s" % out_path)
    print("entries:  %d" % len(names))
    print("bytes:    %d" % os.path.getsize(out_path))
    if wwwroot:
        print("wwwroot:  %d entries" % len(wwwroot))

    return failures


def _discard(path):
    """Remove a staging archive, tolerating its absence."""
    try:
        os.remove(path)
    except OSError:
        pass


def report(number, description, ok):
    print("  [%s] assertion %d: %s" % ("PASS" if ok else "FAIL", number, description))


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Pack a dotnet publish directory into a shape-checked deploy archive."
    )
    parser.add_argument(
        "--publish-dir",
        default=DEFAULT_PUBLISH_DIR,
        help="publish directory to pack (default: %(default)s)",
    )
    parser.add_argument(
        "--out",
        default=DEFAULT_OUT,
        help="archive to write (default: %(default)s)",
    )
    args = parser.parse_args(argv)

    if not os.path.isdir(args.publish_dir):
        print(
            "error: publish directory not found: %s\n"
            "       run: dotnet publish TenExCards/TenExCards.csproj -c Release"
            % args.publish_dir,
            file=sys.stderr,
        )
        return 2

    # Build to a staging path and promote only on success. Writing --out first
    # and checking afterwards fails OPEN: a rejected archive would sit at exactly
    # the path `az webapp deploy --src-path` reads, and the truncating open would
    # already have destroyed the last good archive. Promotion is what makes "never
    # upload an archive that has not passed all four assertions" a guarantee
    # rather than a request.
    staging = args.out + ".tmp"

    print("packing %s -> %s" % (args.publish_dir, args.out))
    written = build_archive(args.publish_dir, staging)
    if written == 0:
        print("error: publish directory is empty: %s" % args.publish_dir, file=sys.stderr)
        _discard(staging)
        return 2
    print("wrote %d file(s) to %s; verifying before it becomes the deploy artifact"
          % (written, staging))
    print("")

    failures = assert_shape(staging)

    print("")
    if failures:
        # Keep the PASS/FAIL table and the verdict in order in a CI log, which
        # merges the two streams and does not otherwise flush stdout until exit.
        sys.stdout.flush()
        print("ARCHIVE REJECTED -- %d assertion(s) failed:" % len(failures), file=sys.stderr)
        for failure in failures:
            print("  - %s" % failure, file=sys.stderr)
        print(
            "\nNOTHING was written to %s -- it still holds whatever it held\n"
            "before. The rejected archive is at %s if you want to inspect it. Each\n"
            "of these failures deploys successfully and then breaks at runtime."
            % (args.out, staging),
            file=sys.stderr,
        )
        return 1

    os.replace(staging, args.out)
    print("ARCHIVE OK -- all four assertions passed; promoted to %s; safe to deploy."
          % args.out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
