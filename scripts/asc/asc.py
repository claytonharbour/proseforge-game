#!/usr/bin/env python3
"""App Store Connect client — TestFlight groups and App Store submission.

⛔ SUBMISSION IS OPT-IN, ALWAYS. Reads are free; `submit` reaches Apple's review queue and
   is the one action that is not undoable on our side. Same posture as scripts/play/upload.py.

Settings, passed in by `make` from .env and .env.local, none printed:
    APPLE_KEY_ID · APPLE_UUID (issuer) · APPLE_P8 (path to the API key file) · APP_ID (bundle ID)
"""
import json, os, sys, time, urllib.request, urllib.error

VENV_PY = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".venv", "bin", "python")
""" ⚠️ The shebang is the SYSTEM python and stays that way (an absolute venv path in a shebang
    breaks on every other checkout). This constant exists so a dependency error can print a
    command that actually works — see the die() in token(). """

API = "https://api.appstoreconnect.apple.com/v1"
P8 = os.path.expanduser(os.environ.get("APPLE_P8", ""))
PLACEHOLDER_APP_ID = "com.yourorg.yourgame"
BUNDLE = os.environ.get("APP_ID", PLACEHOLDER_APP_ID)  # set your own in .env.local


def die(m):
    print(f"error: {m}", file=sys.stderr); sys.exit(1)


def token():
    try:
        import jwt  # PyJWT
    except ImportError:
        # ⛔ This message used to say "pip install pyjwt cryptography" into the venv. That
        #    remedy CANNOT WORK from here: the shebang is `#!/usr/bin/env python3`, so running
        #    this script by path uses the SYSTEM python and never reads the venv. Measured
        #    2026-09-21 — installed into the venv, re-ran, got this identical error, and only
        #    then looked at the shebang. An error that names a fix which changes nothing costs
        #    more than one that just says "missing".
        die("PyJWT missing — you are running under the SYSTEM python, which does not have it.\n"
            f"  re-run through this script's own venv:\n"
            f"    {VENV_PY} {os.path.relpath(__file__)} <args>\n"
            f"  (installing into the venv does NOT help while you invoke the script by path)")
    kid, iss = os.environ.get("APPLE_KEY_ID"), os.environ.get("APPLE_UUID")
    if not kid or not iss:
        die("APPLE_KEY_ID / APPLE_UUID not set (see .env)")
    if not os.path.isfile(P8):
        die(f"App Store Connect API key not found at {P8 or '(unset)'}: set APPLE_P8 in .env.local")
    now = int(time.time())
    # ⚠️ aud and 20-minute cap are Apple's requirements, not choices.
    return jwt.encode(
        {"iss": iss, "iat": now, "exp": now + 1200, "aud": "appstoreconnect-v1"},
        open(P8).read(), algorithm="ES256", headers={"kid": kid, "typ": "JWT"})


def call(method, path, body=None):
    url = path if path.startswith("http") else f"{API}{path}"
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers={
        "Authorization": f"Bearer {token()}",
        **({"Content-Type": "application/json"} if data else {})})
    try:
        with urllib.request.urlopen(req) as r:
            raw = r.read()
            return json.loads(raw) if raw else {}
    except urllib.error.HTTPError as e:
        detail = e.read().decode(errors="replace")
        die(f"HTTP {e.code} on {method} {url}\n{detail}")


def app_id():
    r = call("GET", f"/apps?filter[bundleId]={BUNDLE}")
    if not r.get("data"):
        die(f"no app for bundle {BUNDLE} — is the key's role high enough?")
    return r["data"][0]["id"]


def cmd_status():
    aid = app_id()
    print(f"  app {BUNDLE}  id={aid}\n")
    # ⚠️ Build -> betaGroups is CREATE/DELETE only (403 on read). Invert it: ask each
    # group for its builds and map back. Same answer, the direction the API permits.
    membership = {}
    for g in call("GET", f"/apps/{aid}/betaGroups").get("data", []):
        for b in call("GET", f"/betaGroups/{g['id']}/builds?limit=50").get("data", []):
            membership.setdefault(b["id"], []).append(g["attributes"]["name"])

    builds = call("GET", f"/builds?filter[app]={aid}&limit=10&sort=-version")
    print("  BUILDS")
    for b in builds.get("data", []):
        a = b["attributes"]
        names = ", ".join(membership.get(b["id"], [])) or "—"
        print(f"    {a.get('version'):>4}  {a.get('processingState'):<12} "
              f"expired={str(a.get('expired')):<5} groups=[{names}]")
    print("\n  APP STORE VERSIONS")
    vs = call("GET", f"/apps/{aid}/appStoreVersions?limit=5")
    for v in vs.get("data", []):
        a = v["attributes"]
        print(f"    {a.get('versionString'):>6}  {a.get('appStoreState')}  "
              f"release={a.get('releaseType')}")


def cmd_groups():
    for g in call("GET", f"/apps/{app_id()}/betaGroups").get("data", []):
        a = g["attributes"]
        print(f"    {g['id']}  {a['name']:<24} internal={a.get('isInternalGroup')}")


def cmd_add_to_group(version, group_name):
    aid = app_id()
    builds = [b for b in call("GET", f"/builds?filter[app]={aid}&limit=20").get("data", [])
              if b["attributes"].get("version") == str(version)]
    if not builds:
        die(f"build {version} not found")
    bid = builds[0]["id"]
    groups = [g for g in call("GET", f"/apps/{aid}/betaGroups").get("data", [])
              if g["attributes"]["name"].lower() == group_name.lower()]
    if not groups:
        die(f"no beta group named {group_name!r} — run `groups` to list them")
    gid = groups[0]["id"]
    call("POST", f"/betaGroups/{gid}/relationships/builds",
         {"data": [{"type": "builds", "id": bid}]})
    print(f"  ✅ build {version} added to '{groups[0]['attributes']['name']}'")


def cmd_create_version(vs, release_type="AFTER_APPROVAL"):
    """Create an editable App Store version.

    ⚑ DEFAULT IS AFTER_APPROVAL — it goes live on its own once Apple approves, which is
       how every earlier version was released (`asc.py status` shows it per version).
       A "safer" default of MANUAL would contradict that, and silently hold a release
       nobody knew was held.

    Pass MANUAL explicitly for a version you want to sit in Pending Developer Release.
    """
    if release_type not in ("AFTER_APPROVAL", "MANUAL", "SCHEDULED"):
        die(f"unknown releaseType {release_type!r}")
    r = call("POST", "/appStoreVersions", {"data": {
        "type": "appStoreVersions",
        "attributes": {"platform": "IOS", "versionString": vs, "releaseType": release_type},
        "relationships": {"app": {"data": {"type": "apps", "id": app_id()}}}}})
    d = r["data"]
    print(f"  ✅ created version {d['attributes']['versionString']}  id={d['id']}  "
          f"state={d['attributes']['appStoreState']}  release={d['attributes']['releaseType']}")


def cmd_attach(vs, build_version, notes):
    """Attach a build to an editable version and set the localised release notes."""
    aid = app_id()
    vers = [v for v in call("GET", f"/apps/{aid}/appStoreVersions?limit=20").get("data", [])
            if v["attributes"]["versionString"] == vs]
    if not vers:
        die(f"no App Store version {vs}")
    vid = vers[0]["id"]
    if vers[0]["attributes"]["appStoreState"] != "PREPARE_FOR_SUBMISSION":
        die(f"version {vs} is {vers[0]['attributes']['appStoreState']}, not editable")

    builds = [b for b in call("GET", f"/builds?filter[app]={aid}&limit=20").get("data", [])
              if b["attributes"].get("version") == str(build_version)]
    if not builds:
        die(f"build {build_version} not found")

    call("PATCH", f"/appStoreVersions/{vid}/relationships/build",
         {"data": {"type": "builds", "id": builds[0]["id"]}})
    print(f"  ✅ build {build_version} attached to {vs}")

    # ⚠️ NOT hardcoded to en-US — this app's primary locale is en-CA. Write to every
    # localization the version actually has, so adding a locale later cannot silently
    # ship with empty notes.
    locs = call("GET", f"/appStoreVersions/{vid}/appStoreVersionLocalizations").get("data", [])
    if not locs:
        die("version has no localizations at all")
    for l in locs:
        call("PATCH", f"/appStoreVersionLocalizations/{l['id']}",
             {"data": {"type": "appStoreVersionLocalizations", "id": l["id"],
                       "attributes": {"whatsNew": notes}}})
        print(f"  ✅ release notes set ({l['attributes']['locale']})")


def _version_id(vs):
    aid = app_id()
    vers = [v for v in call("GET", f"/apps/{aid}/appStoreVersions?limit=20").get("data", [])
            if v["attributes"]["versionString"] == vs]
    if not vers:
        die(f"no App Store version {vs}")
    return vers[0]["id"], vers[0]["attributes"]["appStoreState"]


def _editable_age_rating():
    """The age-rating declaration hangs off APP INFO, not the version.

    ⛔ `/appStoreVersions/{id}/ageRatingDeclaration` is a 404 — "the relationship does not
       exist". Apple moved it, and there are TWO appInfos: the live READY_FOR_SALE one and
       the editable PREPARE_FOR_SUBMISSION one. Patching the wrong one is either a 409 or a
       silent no-op against the shipped listing.
    """
    infos = call("GET", f"/apps/{app_id()}/appInfos").get("data", [])
    editable = [i for i in infos
                if (i["attributes"].get("appStoreState") or i["attributes"].get("state"))
                == "PREPARE_FOR_SUBMISSION"]
    if not editable:
        die("no editable appInfo — create the App Store version first")
    iid = editable[0]["id"]
    d = call("GET", f"/appInfos/{iid}/ageRatingDeclaration")["data"]
    return d["id"], d.get("attributes", {})


def cmd_age_rating(*pairs):
    """Read, or set, the age-rating declaration.

    ⚑ With no arguments this DUMPS the live field names and values. Apple's names are not
       guessable and change between API versions — read them, do not invent them.
    """
    did, attrs = _editable_age_rating()
    if not pairs:
        print(f"  age-rating declaration  id={did}\n")
        for k in sorted(attrs):
            print(f"    {k:56} {attrs[k]}")
        return

    patch = {}
    for pair in pairs:
        if "=" not in pair:
            die(f"expected key=value, got {pair!r}")
        k, v = pair.split("=", 1)
        if k not in attrs:
            die(f"{k!r} is not a field on this declaration — run with no args to list them")
        patch[k] = {"true": True, "false": False, "none": None}.get(v.lower(), v)

    call("PATCH", f"/ageRatingDeclarations/{did}",
         {"data": {"type": "ageRatingDeclarations", "id": did, "attributes": patch}})

    # ⚠️ Read back. A PATCH that Apple accepts and ignores is indistinguishable from one it
    #    applied, and this is a legal declaration about the product.
    _, after = _editable_age_rating()
    ok = True
    for k, want in patch.items():
        got = after.get(k)
        if got == want:
            print(f"  ✅ {k} = {got}")
        else:
            ok = False
            print(f"  ⛔ {k} = {got}   (wanted {want}) — NOT APPLIED")
    if not ok:
        sys.exit(1)


def cmd_screenshots(vs, display_type, directory):
    """Replace a version's screenshots for one display type.

    ⚠️ THE SIZE IS THE DISPLAY TYPE. 1320x2868 is APP_IPHONE_67 (6.9"), NOT APP_IPHONE_65 —
       uploading to the wrong set is rejected at commit, after the bytes are sent.

    ⛔ Apple's upload is FOUR steps and skipping the last leaves an invisible half-uploaded
       asset: reserve (POST, returns uploadOperations) -> PUT each part -> PATCH uploaded=true
       with the MD5 -> poll assetDeliveryState. A reserve without the PATCH is not an error,
       it is a screenshot that never appears.
    """
    import hashlib, glob
    vid, state = _version_id(vs)
    if state != "PREPARE_FOR_SUBMISSION":
        die(f"version {vs} is {state}, not editable")

    files = sorted(glob.glob(os.path.join(directory, "*.png")))
    if not files:
        die(f"no PNGs in {directory}")

    locs = call("GET", f"/appStoreVersionLocalizations/?filter[appStoreVersion]={vid}"
                ) if False else call("GET", f"/appStoreVersions/{vid}/appStoreVersionLocalizations")
    loc = locs["data"][0]
    lid, locale = loc["id"], loc["attributes"]["locale"]

    sets = call("GET", f"/appStoreVersionLocalizations/{lid}/appScreenshotSets")["data"]
    match = [x for x in sets if x["attributes"]["screenshotDisplayType"] == display_type]
    if match:
        sid = match[0]["id"]
        existing = call("GET", f"/appScreenshotSets/{sid}/appScreenshots")["data"]
        for e in existing:
            call("DELETE", f"/appScreenshots/{e['id']}")
        print(f"  cleared {len(existing)} existing shot(s) from {display_type}")
    else:
        r = call("POST", "/appScreenshotSets", {"data": {
            "type": "appScreenshotSets",
            "attributes": {"screenshotDisplayType": display_type},
            "relationships": {"appStoreVersionLocalization": {
                "data": {"type": "appStoreVersionLocalizations", "id": lid}}}}})
        sid = r["data"]["id"]
        print(f"  created {display_type} set for {locale}")

    for path in files:
        blob = open(path, "rb").read()
        name = os.path.basename(path)
        r = call("POST", "/appScreenshots", {"data": {
            "type": "appScreenshots",
            "attributes": {"fileSize": len(blob), "fileName": name},
            "relationships": {"appScreenshotSet": {
                "data": {"type": "appScreenshotSets", "id": sid}}}}})
        aid_ = r["data"]["id"]
        for op in r["data"]["attributes"]["uploadOperations"]:
            chunk = blob[op["offset"]:op["offset"] + op["length"]]
            req = urllib.request.Request(op["url"], data=chunk, method=op["method"])
            for h in op.get("requestHeaders", []):
                req.add_header(h["name"], h["value"])
            with urllib.request.urlopen(req) as resp:
                resp.read()
        call("PATCH", f"/appScreenshots/{aid_}", {"data": {
            "type": "appScreenshots", "id": aid_,
            "attributes": {"uploaded": True,
                           "sourceFileChecksum": hashlib.md5(blob).hexdigest()}}})
        print(f"    uploaded {name}  ({len(blob):,} bytes)")

    # ⚑ Verify Apple actually accepted them. A COMPLETE state with errors is the failure
    #    mode that looks like success in the listing UI until review rejects it.
    shots = call("GET", f"/appScreenshotSets/{sid}/appScreenshots")["data"]
    bad = 0
    for sh in shots:
        st = (sh["attributes"].get("assetDeliveryState") or {})
        errs = st.get("errors") or []
        if errs:
            bad += 1
            print(f"    ⛔ {sh['attributes'].get('fileName')}: {errs}")
    print(f"  {'⛔' if bad else '✅'} {len(shots)} shot(s) on {display_type}"
          + (f", {bad} with errors" if bad else ", no errors reported"))


def cmd_submit(vs, confirm=None):
    """Submit a version to Apple review.

    ⛔ THE ONE ACTION THAT IS NOT UNDOABLE ON OUR SIDE. Requires the literal argument
       `--yes`, so it cannot happen as a side effect of a typo'd command.

    ⚑ Apple's CURRENT flow is three calls, not one. `appStoreVersionSubmissions` is the
       retired API; the live one is:
         1  POST /reviewSubmissions            (app + platform)      -> a draft submission
         2  POST /reviewSubmissionItems        (+ the version)       -> what is in it
         3  PATCH /reviewSubmissions {submitted: true}               -> actually submits
       Stopping after 1 or 2 leaves a DRAFT submission that looks created and was never sent.
    """
    if confirm != "--yes":
        die("refusing: `submit` reaches Apple's review queue. Re-run with --yes to mean it.")

    vid, state = _version_id(vs)
    if state != "PREPARE_FOR_SUBMISSION":
        die(f"version {vs} is {state}, not submittable")
    aid = app_id()

    # Reuse an existing READY_FOR_REVIEW draft rather than creating a second one.
    drafts = [r for r in call("GET", f"/reviewSubmissions?filter[app]={aid}"
                              "&filter[state]=READY_FOR_REVIEW").get("data", [])]
    if drafts:
        rsid = drafts[0]["id"]
        print(f"  reusing existing draft submission {rsid}")
    else:
        r = call("POST", "/reviewSubmissions", {"data": {
            "type": "reviewSubmissions",
            "attributes": {"platform": "IOS"},
            "relationships": {"app": {"data": {"type": "apps", "id": aid}}}}})
        rsid = r["data"]["id"]
        print(f"  created review submission {rsid}")

    items = call("GET", f"/reviewSubmissions/{rsid}/items").get("data", [])
    if not any((i.get("relationships", {}).get("appStoreVersion", {}).get("data") or {}).get("id") == vid
               for i in items):
        call("POST", "/reviewSubmissionItems", {"data": {
            "type": "reviewSubmissionItems",
            "relationships": {
                "reviewSubmission": {"data": {"type": "reviewSubmissions", "id": rsid}},
                "appStoreVersion": {"data": {"type": "appStoreVersions", "id": vid}}}}})
        print(f"  added version {vs} to the submission")
    else:
        print(f"  version {vs} already in the submission")

    call("PATCH", f"/reviewSubmissions/{rsid}", {"data": {
        "type": "reviewSubmissions", "id": rsid, "attributes": {"submitted": True}}})

    # ⚠️ Read back. "Submitted" is the claim; the STATE is the fact.
    after = call("GET", f"/reviewSubmissions/{rsid}")["data"]["attributes"]
    _, vstate = _version_id(vs)
    print(f"  submission state : {after.get('state')}")
    print(f"  version state    : {vstate}")
    if after.get("state") in ("WAITING_FOR_REVIEW", "IN_REVIEW"):
        print(f"  ✅ {vs} is with Apple")
    else:
        print(f"  ⚠️  not in a review state — check App Store Connect before assuming it went")


if __name__ == "__main__":
    if BUNDLE == PLACEHOLDER_APP_ID:
        die(f"APP_ID is still the placeholder {PLACEHOLDER_APP_ID}. Set your own in .env.local (see env.local.example).")
    cmds = {"status": cmd_status, "groups": cmd_groups}
    if len(sys.argv) < 2:
        die("usage: asc.py status | groups | add-to-group <build> <group> | "
            "create-version <v> [AFTER_APPROVAL|MANUAL] | attach <v> <build> <notes> | age-rating [k=v ...] | screenshots <v> <type> <dir> | submit <v> --yes")
    c = sys.argv[1]
    if c == "add-to-group":
        if len(sys.argv) != 4:
            die("usage: asc.py add-to-group <version> <group>")
        cmd_add_to_group(sys.argv[2], sys.argv[3])
    elif c == "attach":
        cmd_attach(sys.argv[2], sys.argv[3], sys.argv[4])
    elif c == "create-version":
        cmd_create_version(*sys.argv[2:])
    elif c == "submit":
        cmd_submit(*sys.argv[2:])
    elif c == "screenshots":
        cmd_screenshots(sys.argv[2], sys.argv[3], sys.argv[4])
    elif c == "age-rating":
        cmd_age_rating(*sys.argv[2:])
    elif c in cmds:
        cmds[c]()
    else:
        die(f"unknown command {c!r}")
