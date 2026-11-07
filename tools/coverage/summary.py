#!/usr/bin/env python3
"""Print line coverage per area and per file from a Cobertura report (make coverage).

Only files the rules tests compile are in the report. The screens (view models, pages, platform
start-up) are not, so the total here is coverage of the logic, not of the whole app; the last line
says how much code sits outside it.
"""
import collections
import glob
import sys
import xml.etree.ElementTree as ET

report, src = sys.argv[1], sys.argv[2]
lines = collections.defaultdict(dict)
for cls in ET.parse(report).getroot().iter("class"):
    name = cls.get("filename")
    if "/src/" not in name:
        continue
    rel = "src/" + name.split("/src/", 1)[1]
    for line in cls.iter("line"):
        n, hits = int(line.get("number")), int(line.get("hits"))
        lines[rel][n] = max(lines[rel].get(n, 0), hits)

covered = sum(1 for f in lines.values() for h in f.values() if h)
total = sum(len(f) for f in lines.values())
print(f"Logic the tests reach: {covered}/{total} lines = {covered / total:.1%}\n")
for rel, f in sorted(lines.items(), key=lambda kv: sum(1 for h in kv[1].values() if h) / len(kv[1])):
    hit = sum(1 for h in f.values() if h)
    print(f"  {hit / len(f):6.1%}  {hit:4}/{len(f):<4} {rel}")

def from_src(path):
    """A path as 'src/…', whatever folder the source was given as (a worktree, an absolute path),
    so it compares with the report's names."""
    path = path.replace("\\", "/")
    return "src/" + path.split("/src/", 1)[1] if "/src/" in path else path


outside = [p for p in glob.glob(f"{src}/**/*.cs", recursive=True)
           if "/obj/" not in p and "/bin/" not in p and from_src(p) not in lines]
print(f"\nNot compiled into the tests ({len(outside)} files, screens and platform start-up):")
for p in sorted(outside):
    print(f"  {p}")
