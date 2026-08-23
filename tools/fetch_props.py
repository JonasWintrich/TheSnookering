"""Download CC0 prop models (glTF @1k) from Poly Haven into the game assets.

  python tools/fetch_props.py

Idempotent: skips files that already exist. Poly Haven assets are CC0
(https://polyhaven.com/license) — logged in ATTRIBUTION.md anyway.
"""

import json
import os
import urllib.request

SLUGS = [
    "ArmChair_01",
    "CoffeeTable_01",
    "bar_chair_round_01",
    "dartboard",
    "chess_set",
    "calathea_orbifolia_01",
    "Shelf_01",
    # "decorative_book_set_01",  # no glTF export offered
]

OUT_BASE = os.path.join(os.path.dirname(__file__), "..", "game", "assets", "models", "props")


def fetch(url: str, dest: str) -> None:
    if os.path.exists(dest):
        print("skip", os.path.normpath(dest))
        return
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    req = urllib.request.Request(url, headers={"User-Agent": "snookering-asset-fetch"})
    with urllib.request.urlopen(req) as r, open(dest, "wb") as f:
        f.write(r.read())
    print("got ", os.path.normpath(dest))


def main() -> None:
    for slug in SLUGS:
        api = f"https://api.polyhaven.com/files/{slug}"
        with urllib.request.urlopen(urllib.request.Request(api, headers={"User-Agent": "snookering-asset-fetch"})) as r:
            files = json.load(r)
        if "gltf" not in files:
            print("no gltf format for", slug, "- skipping")
            continue
        entry = files["gltf"]["1k"]["gltf"]
        root = os.path.join(OUT_BASE, slug)
        fetch(entry["url"], os.path.join(root, os.path.basename(entry["url"])))
        for rel, inc in entry.get("include", {}).items():
            fetch(inc["url"], os.path.join(root, rel))


if __name__ == "__main__":
    main()
