"""Stage the pictures a release build ships, as WebP, and check the staged copy is current.

    python3 tools/optimize/optimize.py build --src . --out build/packaged
    python3 tools/optimize/optimize.py check --src . --out build/packaged

The PNG masters in themes/ and gallery/ are never touched. `build` copies both folders into --out,
turning each PNG into a WebP of the same size (transparency kept) and pointing each staged theme.json
at the new names. A manifest records every master's SHA-256, so a picture is reconverted only when its
master changed, and an output whose master is gone is deleted: no stale cache, and no trust in
timestamps, which a checkout rewrites. `check` fails if the staged copy is out of date with the masters,
if a staged theme.json names a file that isn't there, or if a picture or the total is over budget.
Run with `-m unittest` from this folder to test it (test_optimize.py).
"""
import argparse
import hashlib
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

FOLDERS = ("themes", "gallery")
MANIFEST = "manifest.json"
FORMAT = 1
QUALITY = 82
MAX_PICTURE_KB = 1500   # one picture; a 1080×1920 background lands near 200 KB
MAX_TOTAL_MB = 40       # everything staged; the masters are about 90 MB
PICTURE_PATH = re.compile(r'"((?:[^"\\]|\\.)*\.png)"')


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def cwebp(src: Path, dst: Path, quality: int) -> None:
    if not shutil.which("cwebp"):
        sys.exit("optimize: cwebp isn't installed (macOS: brew install webp; Linux: apt install webp)")
    subprocess.run(["cwebp", "-quiet", "-mt", "-q", str(quality), "-alpha_q", "100", str(src), "-o", str(dst)], check=True)


def masters(src: Path):
    """Every file the app packs from themes/ and gallery/, as paths relative to src."""
    for folder in FOLDERS:
        for path in sorted((src / folder).rglob("*")):
            if path.is_file() and not path.name.startswith("."):
                yield path.relative_to(src).as_posix()


def staged_name(rel: str) -> str:
    return rel[:-4] + ".webp" if rel.endswith(".png") else rel


def rewrite_theme(text: str, pack_dir: Path, staged_pack: Path) -> str:
    """Point a theme.json at the staged .webp names, for every .png it names that became one."""
    def swap(m):
        name = m.group(1)
        webp = name[:-4] + ".webp"
        return f'"{webp}"' if (staged_pack / webp).exists() else m.group(0)
    return PICTURE_PATH.sub(swap, text)


def load_manifest(out: Path) -> dict:
    try:
        m = json.loads((out / MANIFEST).read_text())
        return m if m.get("format") == FORMAT else {}
    except (OSError, ValueError):
        return {}


def build(src: Path, out: Path, quality: int = QUALITY, convert=cwebp) -> dict:
    # build owns --out and prunes anything in it that no master produced, so it refuses a folder it
    # didn't make: one that already holds files but none of this tool's manifest (`--out build` or
    # `--out ..` would otherwise delete unrelated work).
    # (A manifest.json in some other format doesn't count.)
    if out.exists() and any(out.iterdir()) and not load_manifest(out):
        sys.exit(f"optimize: {out} already holds files that this tool didn't stage. Use an empty folder "
                 f"(the default is build/packaged), or delete {out} first.")
    old = load_manifest(out)
    old_files = old.get("files", {}) if old.get("quality") == quality else {}
    files, converted, kept = {}, 0, 0
    for rel in masters(src):
        master = src / rel
        digest = sha256(master)
        dest_rel = staged_name(rel)
        dest = out / dest_rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        if rel.endswith(".png"):
            entry = old_files.get(rel)
            if entry and entry["sha256"] == digest and entry["out"] == dest_rel and dest.exists():
                kept += 1
            else:
                convert(master, dest, quality)
                converted += 1
        elif not rel.endswith("theme.json"):
            shutil.copyfile(master, dest)
        files[rel] = {"sha256": digest, "out": dest_rel, "in": master.stat().st_size}
    # theme.json last, once every picture it names is staged.
    for rel in files:
        if rel.endswith("theme.json"):
            pack = Path(rel).parent
            text = rewrite_theme((src / rel).read_text(encoding="utf-8"), src / pack, out / pack)
            (out / rel).write_text(text, encoding="utf-8")
    for rel, entry in files.items():
        entry["bytes"] = (out / entry["out"]).stat().st_size
    # Anything staged that no master produced any more (a deleted or renamed picture) goes.
    wanted = {e["out"] for e in files.values()} | {MANIFEST}
    removed = 0
    for path in sorted(out.rglob("*"), reverse=True):
        rel = path.relative_to(out).as_posix()
        if path.is_file() and rel not in wanted:
            path.unlink(); removed += 1
        elif path.is_dir() and not any(path.iterdir()):
            path.rmdir()
    manifest = {"format": FORMAT, "quality": quality, "files": files}
    (out / MANIFEST).write_text(json.dumps(manifest, indent=1, sort_keys=True) + "\n")
    return {"converted": converted, "kept": kept, "removed": removed, "manifest": manifest}


def check(src: Path, out: Path, max_picture_kb: int = MAX_PICTURE_KB, max_total_mb: float = MAX_TOTAL_MB) -> list:
    """Problems that make the staged copy unsafe to ship; empty means it matches the masters."""
    problems = []
    manifest = load_manifest(out)
    if not manifest:
        return [f"{out}/{MANIFEST} is missing or unreadable: run `make optimize`"]
    files = manifest["files"]
    current = set(masters(src))
    for rel in sorted(current - files.keys()):
        problems.append(f"{rel} is new since the last optimize: run `make optimize`")
    for rel in sorted(files.keys() - current):
        problems.append(f"{rel} was deleted, but its staged copy is still listed: run `make optimize`")
    for rel in sorted(current & files.keys()):
        entry = files[rel]
        if sha256(src / rel) != entry["sha256"]:
            problems.append(f"{rel} changed since the last optimize: run `make optimize`")
        if not (out / entry["out"]).exists():
            problems.append(f"{entry['out']} is missing from {out}: run `make optimize`")
    staged = {p.relative_to(out).as_posix() for p in out.rglob("*") if p.is_file()} - {MANIFEST}
    for rel in sorted(staged - {e["out"] for e in files.values()}):
        problems.append(f"{out}/{rel} isn't from any master: run `make optimize`")
    # A staged theme.json must name the WebP wherever the master PNG exists, and every WebP it names
    # must be staged. A PNG with no master is the theme check's business (a placeholder shows).
    for theme in sorted(out.glob("themes/*/theme.json")):
        text = theme.read_text(encoding="utf-8")
        master_pack = src / theme.parent.relative_to(out)
        for name in PICTURE_PATH.findall(text):
            if (master_pack / name).exists():
                problems.append(f"{theme.relative_to(out)} still names {name}, not its WebP: run `make optimize`")
        for name in re.findall(r'"([^"]*\.webp)"', text):
            if not (theme.parent / name).exists():
                problems.append(f"{theme.relative_to(out)} names {name}, which isn't staged: run `make optimize`")
    total = 0
    for rel in sorted(staged):
        size = (out / rel).stat().st_size
        total += size
        if rel.endswith((".webp", ".png")) and size > max_picture_kb * 1024:
            problems.append(f"{rel} is {size // 1024} KB, over the {max_picture_kb} KB budget for one picture")
    if total > max_total_mb * 1_000_000:
        problems.append(f"the staged pictures and files total {total / 1e6:.1f} MB, over the {max_total_mb} MB budget")
    return problems


def report(manifest: dict) -> str:
    by_pack = {}
    for rel, e in manifest["files"].items():
        key = "/".join(rel.split("/")[:2]) if rel.startswith("themes/") else rel.split("/")[0]
        a, b = by_pack.get(key, (0, 0))
        by_pack[key] = (a + e["in"], b + e["bytes"])
    lines = [f"  {k:<26} {a / 1e6:6.1f} MB -> {b / 1e6:5.1f} MB" for k, (a, b) in sorted(by_pack.items())]
    a = sum(x for x, _ in by_pack.values()); b = sum(y for _, y in by_pack.values())
    lines.append(f"  {'total':<26} {a / 1e6:6.1f} MB -> {b / 1e6:5.1f} MB")
    return "\n".join(lines)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("command", choices=["build", "check"])
    ap.add_argument("--src", type=Path, default=Path("."))
    ap.add_argument("--out", type=Path, default=Path("build/packaged"))
    ap.add_argument("--quality", type=int, default=QUALITY)
    ap.add_argument("--max-picture-kb", type=int, default=MAX_PICTURE_KB)
    ap.add_argument("--max-total-mb", type=float, default=MAX_TOTAL_MB)
    args = ap.parse_args(argv)
    if args.command == "build":
        result = build(args.src, args.out, args.quality)
        print(f"optimize: {result['converted']} converted, {result['kept']} unchanged, {result['removed']} removed")
        print(report(result["manifest"]))
    problems = check(args.src, args.out, args.max_picture_kb, args.max_total_mb)
    for p in problems:
        print(f"optimize: {p}", file=sys.stderr)
    if problems:
        sys.exit(1)
    if args.command == "check":
        print(f"optimize: {args.out} matches the masters and is within budget")


if __name__ == "__main__":
    main()
