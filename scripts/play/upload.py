#!/usr/bin/env python3
"""Upload an AAB to Google Play and optionally roll it out.

⛔ PRODUCTION IS OPT-IN, ALWAYS. The default target is the `internal` track as a DRAFT, which
   reaches nobody and is undoable. Reaching real users takes two explicit arguments. Uploading a
   draft is free; starting a production rollout is outward-facing and effectively is not, and it
   must never be something a mistyped make target can do.

⚑ Everything happens inside ONE edit. Play applies an edit atomically on commit, so a run that
  dies halfway leaves the listing exactly as it was rather than half-updated.

Usage:
    upload.py --aab PATH [--track internal|alpha|beta|production]
                         [--rollout FRACTION | --draft]
                         [--notes TEXT] [--dry-run]

Credentials come from GOOGLE_PLAY_SERVICE_ACCOUNT_JSON (a path, kept out of the repo).
"""

import argparse
import json
import os
import sys
import urllib.request
import urllib.error

VENV_PY = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".venv", "bin", "python")
""" ⚠️ Same reason as scripts/asc/asc.py: the shebang is the SYSTEM python, so a dependency error
    must tell the reader to re-run through this path rather than to pip-install into a venv the
    running interpreter will never consult. """

PACKAGE = os.environ.get("APP_ID", "ai.proseforge.game.onemorestory")  # One More Story's, unless you set your own
API = "https://androidpublisher.googleapis.com/androidpublisher/v3"
UPLOAD_API = "https://androidpublisher.googleapis.com/upload/androidpublisher/v3"
SCOPE = "https://www.googleapis.com/auth/androidpublisher"

# ⛔ Anything not in here cannot be passed as a track. A typo must fail loudly rather than
#    silently create a track nobody is watching.
TRACKS = ("internal", "alpha", "beta", "production")


def die(msg):
    print(f"error: {msg}", file=sys.stderr)
    sys.exit(1)


def access_token(key_path):
    """Mint an OAuth token from the service-account key.

    ⚠️ google-auth is imported HERE, not at module scope, so --help and argument validation work
    without the dependency installed. A missing library should not look like a broken script.
    """
    try:
        from google.oauth2 import service_account
        from google.auth.transport.requests import Request
    except ImportError as e:
        # ⚠️ Name the module that is ACTUALLY missing. The first version of this hardcoded
        # "google-auth is not installed" and printed it when `requests` was the missing one —
        # google-auth was installed fine, and the message sent me to re-check the wrong thing.
        # ⛔ This used to point at `pip install` into the venv, which does NOT help when the
        #    script is invoked by path: the shebang is `#!/usr/bin/env python3`, so it runs
        #    under the SYSTEM python and never reads that venv (measured 2026-09-21, same trap
        #    as scripts/asc/asc.py). `make` gets it right via PLAY_PY; a direct call does not.
        die(f"missing Python dependency: {e.name} — you are running under the SYSTEM python.\n"
            f"  re-run through this script's own venv:\n"
            f"    {VENV_PY} {os.path.relpath(__file__)} <args>\n"
            f"  (or `make upload-android`, which already uses PLAY_PY)")

    creds = service_account.Credentials.from_service_account_file(key_path, scopes=[SCOPE])
    creds.refresh(Request())
    return creds.token


def call(method, url, bearer, body=None, data=None, content_type=None):
    headers = {"Authorization": f"Bearer {bearer}"}
    if body is not None:
        data = json.dumps(body).encode()
        headers["Content-Type"] = "application/json"
    if content_type:
        headers["Content-Type"] = content_type

    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req) as r:
            raw = r.read()
            return json.loads(raw) if raw else {}
    except urllib.error.HTTPError as e:
        detail = e.read().decode(errors="replace")
        # ⚑ A fresh service account 401/403s for a few minutes while the Play Console grant
        #   propagates. That is the single most likely first-run failure and it is not a
        #   configuration error — say so rather than sending someone to re-check their setup.
        if e.code in (401, 403):
            detail += (
                "\n\nhint: a newly invited service account can take several minutes to "
                "propagate. If the grant was just made, wait and retry before changing anything."
            )
        die(f"HTTP {e.code} on {method} {url}\n{detail}")


def main():
    p = argparse.ArgumentParser(description="Upload an AAB to Google Play.")
    p.add_argument("--tracks", action="store_true",
                   help="Read-only: print what is live on every track, and stop. "
                        "Answers 'is this in open testing' without touching anything.")
    p.add_argument("--check", action="store_true",
                   help="Prove the credential and permissions work, then discard. "
                        "Opens an edit and deletes it — uploads nothing, touches no track.")
    # ⚑ Promote an ALREADY-UPLOADED build to another track. Necessary, not a convenience: Play
    #   rejects a versionCode it has seen, so once a bundle is up, re-running --aab to put the
    #   same build on a second track is impossible. Without this, "upload to internal, then
    #   promote if it looks good" cannot be expressed at all.
    p.add_argument("--version-code", type=int, metavar="N",
                   help="Promote this existing versionCode instead of uploading. "
                        "Mutually exclusive with --aab.")
    p.add_argument("--aab", required=not any(
        a in ("--check", "--tracks", "--version-code", "--images") or a.startswith(("--version-code=", "--images="))
        for a in sys.argv))
    p.add_argument("--track", default="internal", choices=TRACKS)
    p.add_argument("--rollout", type=float, metavar="FRACTION",
                   help="0.0-1.0. Omit for a draft that reaches nobody.")
    p.add_argument("--draft", action="store_true", help="Explicit draft (the default).")
    p.add_argument("--notes", default=None, help="Release notes (en-US).")
    p.add_argument("--name", default=None,
                   help="Release name shown in the Play Console, e.g. 1.5.2. Omit and Play "
                        "REUSES the name already on that track's draft.")
    p.add_argument("--images", metavar="DIR",
                   help="Replace listing images for --image-type from this directory.")
    p.add_argument("--image-type", default="phoneScreenshots")
    # ⚠️ en-US, and it is NOT a copy-paste of the App Store answer. The App Store listing is
    #    en-CA; the PLAY listing is en-US. Measured 2026-08-25 — `edits.listings.list` returns
    #    exactly ['en-US']. Defaulting to en-CA here CREATED A NEW EMPTY LOCALE and left the live
    #    screenshots untouched, while exiting 0. Two stores, two answers.
    p.add_argument("--language", default="en-US",
                   help="Play listing locale. The live listing is en-US (the App Store one "
                        "is en-CA — do not copy that answer across).")
    p.add_argument("--dry-run", action="store_true",
                   help="Validate and print the plan; touch nothing.")
    args = p.parse_args()

    if args.images:
        # ⛔ THIS RETURNS BEFORE THE BUNDLE IS EVER READ, and --aab is a required argument, so
        #    `--aab X --images Y` looks like one command that does both and silently does one.
        #    Cost 2026-09-04: I ran exactly that, watched eight screenshots upload, read a
        #    "✅ committed" and believed the 1.5.2 bundle was on Play. It was not — the highest
        #    versionCode was still 17, found only by listing bundles afterwards.
        #
        # ⚑ A silent drop becomes a visible one. Not an error: images-only is the normal way to
        #    run this, and refusing the required --aab would break every image upload.
        print(f"⚠️  images mode: the bundle at {args.aab} is NOT being uploaded.\n"
              f"   This run only replaces {args.image_type}. Upload the bundle separately.")
        cmd_images(args.images, args.image_type, args.language, args.dry_run)
        return

    key_path = os.environ.get("GOOGLE_PLAY_SERVICE_ACCOUNT_JSON")
    if not key_path:
        die("GOOGLE_PLAY_SERVICE_ACCOUNT_JSON is not set (see .env)")
    if not os.path.isfile(key_path):
        die(f"service account key not found: {key_path}")

    # ⚑ A real upload cannot be used as a smoke test: Play rejects a versionCode it has already
    #   seen, so the first genuine run of this script is otherwise also its first test. --check
    #   exercises auth, the Play Console grant and the edit lifecycle against the live API while
    #   uploading nothing and touching no track.
    # ⚑ READ-ONLY, and the question it answers gets asked far more often than an upload:
    #   "which version is on production / open testing right now?" Before this existed the only
    #   way to find out was the Play Console UI or a hand-rolled API call, so it got guessed.
    #
    # ⚠️ Play's track names are NOT the Console's labels, and this is the bit that misleads:
    #       production = Production        beta     = OPEN testing
    #       alpha      = CLOSED testing    internal = Internal testing
    #   So "is it in open testing" is answered by the row called `beta`.
    if args.tracks:
        bearer = access_token(key_path)
        edit = call("POST", f"{API}/applications/{PACKAGE}/edits", bearer, body={})
        try:
            LABEL = {"production": "Production", "beta": "OPEN testing",
                     "alpha": "CLOSED testing", "internal": "Internal testing"}
            data = call("GET", f"{API}/applications/{PACKAGE}/edits/{edit['id']}/tracks", bearer)
            print(f"{PACKAGE}\n")
            for t in data.get("tracks", []):
                label = LABEL.get(t["track"], t["track"])
                releases = t.get("releases") or []
                if not releases:
                    print(f"  {t['track']:<11} {label:<16} (no releases)")
                    continue
                for r in releases:
                    codes = ",".join(str(v) for v in r.get("versionCodes", []) or [])
                    frac = r.get("userFraction")
                    rollout = f" · {frac:.0%} of users" if frac else ""
                    print(f"  {t['track']:<11} {label:<16} {r.get('status','?'):<11} "
                          f"v{r.get('name','?'):<9} codes=[{codes}]{rollout}")
        finally:
            # Discard the edit — an abandoned edit is harmless but this leaves nothing behind.
            call("DELETE", f"{API}/applications/{PACKAGE}/edits/{edit['id']}", bearer)
        print("\nread-only: an edit was opened to read the tracks and then discarded.")
        return

    if args.check:
        bearer = access_token(key_path)
        edit = call("POST", f"{API}/applications/{PACKAGE}/edits", bearer, body={})
        call("DELETE", f"{API}/applications/{PACKAGE}/edits/{edit['id']}", bearer)
        print(f"✅ credential works. Opened edit {edit['id']} on {PACKAGE} and discarded it.")
        print("   Auth and the Play Console grant are good. Nothing was uploaded or changed.")
        return

    if args.version_code and args.aab:
        die("--version-code promotes an existing build; --aab uploads a new one. Pick one.")
    if not args.version_code and not os.path.isfile(args.aab):
        die(f"no such AAB: {args.aab}")

    if args.rollout is not None:
        if args.draft:
            die("--rollout and --draft contradict each other; pick one")
        if not 0.0 < args.rollout <= 1.0:
            die("--rollout must be within (0.0, 1.0]")

    # ⛔ THE GUARD. Reaching production users requires naming BOTH the track and the rollout.
    #    `--track production` alone uploads a draft and stops — it does not quietly go live.
    status = "completed" if args.rollout == 1.0 else ("inProgress" if args.rollout else "draft")
    if args.track == "production" and args.rollout:
        print(f"⚠️  PRODUCTION rollout at {args.rollout:.0%} of users. This reaches real people.")

    source = (f"promote existing versionCode {args.version_code}" if args.version_code
              else f"{args.aab} ({os.path.getsize(args.aab):,} bytes)")
    plan = (f"  package : {PACKAGE}\n"
            f"  source  : {source}\n"
            f"  track   : {args.track}\n"
            f"  status  : {status}"
            + (f" ({args.rollout:.0%})" if args.rollout else " — reaches nobody until rolled out"))
    print(plan)

    # ⛔ AT PLAN TIME, so --dry-run shows it too. MEASURED 2026-09-20: Play CARRIES FORWARD the
    #    previous release's notes on that track when you send none, so "no notes" does not mean
    #    an empty what's-new — it means the LAST release's text, presented as this one's.
    #
    #    1.5.4 reached open testing with NO notes, while the internal draft carried 1.5.2's
    #    "Threads: reply to any message…" as if it described 1.5.4. Neither is visible in the
    #    upload output, and a warning printed AFTER the commit is a warning that arrives too late.
    if not args.notes:
        print("   ⚠️  no --notes given: Play KEEPS THE PREVIOUS RELEASE'S NOTES on this track,\n"
              "      so testers may be shown an older version's what's-new as if it were this\n"
              "      one. Pass --notes, or read them back before anyone sees the release.")

    if args.dry_run:
        print("\ndry run: nothing was sent.")
        return

    bearer = access_token(key_path)

    edit = call("POST", f"{API}/applications/{PACKAGE}/edits", bearer, body={})
    edit_id = edit["id"]
    print(f"  edit    : {edit_id}")

    if args.version_code:
        version_code = args.version_code
        print(f"  version : {version_code} (promoting, nothing uploaded)")
    else:
        with open(args.aab, "rb") as fh:
            bundle = call(
                "POST",
                f"{UPLOAD_API}/applications/{PACKAGE}/edits/{edit_id}/bundles?uploadType=media",
                bearer, data=fh.read(), content_type="application/octet-stream")
        version_code = bundle["versionCode"]
        print(f"  version : {version_code}")

    release = {"versionCodes": [str(version_code)], "status": status}
    # ⛔ WITHOUT A NAME, PLAY KEEPS THE ONE ALREADY ON THE DRAFT. Measured 2026-09-04: this
    #    PUT replaced the internal draft's versionCodes with [18] — the 1.5.2 build — and
    #    left the release NAMED "1.5.1", because the previous draft carried that name and
    #    the body did not say otherwise. The console then shows a 1.5.1 release containing
    #    1.5.2, which is the label somebody reads before deciding what to roll out.
    if args.name:
        release["name"] = args.name
    if status == "inProgress":
        release["userFraction"] = args.rollout
    if args.notes:
        release["releaseNotes"] = [{"language": "en-US", "text": args.notes}]

    call("PUT", f"{API}/applications/{PACKAGE}/edits/{edit_id}/tracks/{args.track}",
         bearer, body={"track": args.track, "releases": [release]})

    call("POST", f"{API}/applications/{PACKAGE}/edits/{edit_id}:commit", bearer, body={})
    print(f"\n✅ committed. version {version_code} is on '{args.track}' as {status}.")
    if not args.name:
        print("   ⚠️  no --name given: the release kept whatever name that track's draft\n"
              "      already had. Check it in the console before rolling out.")

    if status == "draft":
        print("   It reaches nobody until a rollout is started — by design.")


# Play's image types. phoneScreenshots is the one that matters; the rest are optional.
IMAGE_TYPES = ("phoneScreenshots", "sevenInchScreenshots", "tenInchScreenshots",
               "featureGraphic", "icon")


def cmd_images(directory, image_type, language, dry_run):
    """Replace one image type for one language from a directory of PNG/JPEG files.

    ⛔ REPLACE, NOT APPEND. Play has no partial update: the flow is deleteall then upload,
    so a run that sends 3 of 5 images leaves a listing with 3. Everything is validated and
    counted BEFORE anything is deleted, and a short set is refused rather than published.
    """
    if image_type not in IMAGE_TYPES:
        die(f"unknown image type {image_type!r}; expected one of {', '.join(IMAGE_TYPES)}")
    if not os.path.isdir(directory):
        die(f"no such directory: {directory}")

    files = sorted(f for f in os.listdir(directory)
                   if f.lower().endswith((".png", ".jpg", ".jpeg")))
    if not files:
        die(f"{directory} contains no PNG/JPEG — nothing to upload, and a deleteall here "
            f"would empty the listing")

    # ⚠️ Play refuses a listing with fewer than 2 phone screenshots. Catching it here means
    # the store listing is never left short; catching it after deleteall means it is.
    if image_type == "phoneScreenshots" and len(files) < 2:
        die(f"only {len(files)} phone screenshot(s); Play requires at least 2. Refusing "
            f"rather than deleting the existing set and failing halfway.")

    print(f"  language : {language}")
    print(f"  type     : {image_type}")
    print(f"  files    : {len(files)}")
    for f in files:
        print(f"      {f}  ({os.path.getsize(os.path.join(directory, f)):,} bytes)")

    if dry_run:
        print("\n  dry run: the existing listing images were NOT deleted and nothing was sent.")
        return

    key_path = os.environ.get("GOOGLE_PLAY_SERVICE_ACCOUNT_JSON")
    bearer = access_token(key_path)
    edit = call("POST", f"{API}/applications/{PACKAGE}/edits", bearer, body={})
    edit_id = edit["id"]

    # ⛔ THE LOCALE GUARD. Play happily accepts images for a locale that is not on the listing:
    #    it creates one. Nothing errors, the commit succeeds, and the screenshots you meant to
    #    replace are still there — so the run reports success and changes nothing visible.
    #    That is a check that returns the same answer whether or not it worked, which is no check
    #    at all. Refuse a locale the listing does not already have, and name the ones it does.
    listed = [L["language"] for L in
              (call("GET", f"{API}/applications/{PACKAGE}/edits/{edit_id}/listings", bearer)
               .get("listings") or [])]
    if language not in listed:
        call("DELETE", f"{API}/applications/{PACKAGE}/edits/{edit_id}", bearer)
        die(f"{language!r} is not a locale on this listing. Uploading to it would create a new "
            f"empty locale and leave the live images untouched.\n"
            f"  locales that exist: {', '.join(listed) or '(none)'}")
    print(f"  locale ok : {language} is on the listing")

    base = f"{API}/applications/{PACKAGE}/edits/{edit_id}/listings/{language}/{image_type}"
    call("DELETE", base, bearer)
    print(f"\n  cleared existing {image_type}")

    upload = (f"{UPLOAD_API}/applications/{PACKAGE}/edits/{edit_id}"
              f"/listings/{language}/{image_type}?uploadType=media")
    for f in files:
        with open(os.path.join(directory, f), "rb") as fh:
            ct = "image/jpeg" if f.lower().endswith((".jpg", ".jpeg")) else "image/png"
            call("POST", upload, bearer, data=fh.read(), content_type=ct)
        print(f"  uploaded {f}")

    call("POST", f"{API}/applications/{PACKAGE}/edits/{edit_id}:commit", bearer, body={})
    print(f"\n✅ committed. {len(files)} {image_type} live on the {language} listing.")
    print("   The app itself is untouched — listing images are independent of releases.")


if __name__ == "__main__":
    main()
