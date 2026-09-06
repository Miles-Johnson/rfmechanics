"""Generate goblin spit-packed textures: every diggable-earth family recolored onto the
vanilla stonepath tile set (block/stone/path/normal1-6.png), so every spitpacked variant
shares the same base pattern but takes on the color of whatever vanilla block it replaced.

Stage A: compute a representative (H, S, V) for a family/sub-variant by averaging its own
vanilla source texture(s) -- hue is averaged circularly (it wraps at 0/1), saturation and
value are plain arithmetic means.

Stage B: recolor each of the 6 stonepath tiles by replacing hue+saturation outright with the
target's, but rescaling (not replacing) the tile's own per-pixel value so its shading/detail
survives while its overall brightness matches the target -- then layering the same "packed"
darkening already established for every prior spitpacked texture (saturation x0.88, value
x0.82, hue +6 out of 255) on top, for visual continuity with earlier passes.

Usage: python tools/recolor_spitpacked.py
"""
import colorsys
import glob
import math
import os

from PIL import Image, ImageStat

VINTAGE_STORY = os.environ.get("VINTAGE_STORY", r"C:\Games\Vintagestory")
GAME_TEX = os.path.join(VINTAGE_STORY, "assets", "survival", "textures")
OUT_ROOT = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "assets", "rfmechanics", "textures", "block", "spitpacked",
)
STONEPATH_TILES = [
    os.path.join(GAME_TEX, "block", "stone", "path", f"normal{i}.png") for i in range(1, 7)
]

ROCKS = [
    "andesite", "chalk", "chert", "conglomerate", "limestone", "claystone", "granite",
    "sandstone", "shale", "basalt", "peridotite", "phyllite", "slate", "bauxite",
]
FERTILITIES = ["verylow", "low", "medium", "compost", "high"]
DIRTYGRAVEL_MOISTURE_TYPES = [
    (moisture, type_)
    for moisture in ("dry", "wet", "wetdark", "wetverydark")
    for type_ in ("cracked", "plain", "stoney")
    if (moisture, type_) not in {("wetverydark", "stoney"), ("wetverydark", "cracked")}
]

VALUE_RATIO_MIN = 0.5
VALUE_RATIO_MAX = 1.8
PACKED_SAT_MULT = 0.88
PACKED_VAL_MULT = 0.82
PACKED_HUE_SHIFT = 6  # out of 255


def average_hsv(paths):
    """Pool every opaque pixel across all given source images, return (H, S, V) in 0-1."""
    h_sin_sum = h_cos_sum = s_sum = v_sum = n = 0
    for p in paths:
        img = Image.open(p).convert("RGBA")
        for r, g, b, a in img.getdata():
            if a < 128:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            h_sin_sum += math.sin(h * 2 * math.pi)
            h_cos_sum += math.cos(h * 2 * math.pi)
            s_sum += s
            v_sum += v
            n += 1
    if n == 0:
        raise ValueError(f"no opaque pixels found across {paths}")
    h_avg = math.atan2(h_sin_sum / n, h_cos_sum / n) / (2 * math.pi)
    if h_avg < 0:
        h_avg += 1.0
    return h_avg, s_sum / n, v_sum / n


def recolor_tile(tile_path, h_target, s_target, v_target, out_path):
    img = Image.open(tile_path).convert("RGBA")
    r, g, b, a = img.split()
    rgb = Image.merge("RGB", (r, g, b))
    h_band, s_band, v_band = rgb.convert("HSV").split()

    tile_v_avg = ImageStat.Stat(v_band).mean[0] / 255.0
    if tile_v_avg <= 0:
        tile_v_avg = 1.0 / 255.0
    ratio = max(VALUE_RATIO_MIN, min(VALUE_RATIO_MAX, v_target / tile_v_avg))

    h_target_255 = int(round(h_target * 255)) % 256
    s_target_255 = int(round(max(0.0, min(1.0, s_target)) * 255))

    h_new = Image.new("L", img.size, color=h_target_255).point(
        lambda x: (x + PACKED_HUE_SHIFT) % 256
    )
    s_new = Image.new("L", img.size, color=s_target_255).point(
        lambda x: max(0, min(255, int(round(x * PACKED_SAT_MULT))))
    )
    v_new = v_band.point(
        lambda x: max(0, min(255, int(round(x * ratio * PACKED_VAL_MULT))))
    )

    rgb_new = Image.merge("HSV", (h_new, s_new, v_new)).convert("RGB")
    r2, g2, b2 = rgb_new.split()
    Image.merge("RGBA", (r2, g2, b2, a)).save(out_path)


def emit_family(out_family_dir, out_prefix, source_paths):
    os.makedirs(out_family_dir, exist_ok=True)
    h, s, v = average_hsv(source_paths)
    for i, tile in enumerate(STONEPATH_TILES, start=1):
        out_path = os.path.join(out_family_dir, f"{out_prefix}{i}.png")
        recolor_tile(tile, h, s, v, out_path)
    return h, s, v


def main():
    counts = {}

    def run(label, out_family_dir, out_prefix, source_paths):
        emit_family(out_family_dir, out_prefix, source_paths)
        counts[label] = counts.get(label, 0) + 6

    # -- rock-varying families: sand, gravel, sandwavy --
    for rock in ROCKS:
        run(
            "sand", os.path.join(OUT_ROOT, "sand", rock), f"spitpackedsand-{rock}",
            [os.path.join(GAME_TEX, "block", "stone", "sand", f"{rock}.png")],
        )
        run(
            "gravel", os.path.join(OUT_ROOT, "gravel", rock), f"spitpackedgravel-{rock}",
            [os.path.join(GAME_TEX, "block", "stone", "gravel", f"{rock}.png")],
        )
        run(
            "sandwavy", os.path.join(OUT_ROOT, "sandwavy", rock), f"spitpackedsandwavy-{rock}",
            sorted(glob.glob(os.path.join(GAME_TEX, "block", "stone", "sand", "wavy", f"{rock}*.png"))),
        )

    for fertility in FERTILITIES:
        run(
            "soil", os.path.join(OUT_ROOT, "soil", fertility), f"spitpackedsoil-{fertility}",
            [os.path.join(GAME_TEX, "block", "soil", f"fert{fertility}.png")],
        )

    for moisture, type_ in DIRTYGRAVEL_MOISTURE_TYPES:
        run(
            "dirtygravel",
            os.path.join(OUT_ROOT, "dirtygravel", f"{moisture}-{type_}"),
            f"spitpackeddirtygravel-{moisture}-{type_}",
            sorted(glob.glob(os.path.join(GAME_TEX, "block", "stone", "dirtygravel", moisture, f"{type_}*.png"))),
        )

    flat_families = {
        "bonysoil": [os.path.join(GAME_TEX, "block", "soil", "bony", f"normal{i}.png") for i in (1, 2, 3)],
        "cob": [os.path.join(GAME_TEX, "block", "soil", "cob", f"normal{i}.png") for i in (1, 2, 3, 4)],
        "forestfloor": [os.path.join(GAME_TEX, "block", "soil", "forest", "side.png")],
        "muddygravel": [os.path.join(GAME_TEX, "block", "soil", "muddygravel.png")],
        "sludgygravel": [os.path.join(GAME_TEX, "block", "soil", "sludgygravel.png")],
    }
    for family, sources in flat_families.items():
        run("flat:" + family, os.path.join(OUT_ROOT, family), f"spitpacked{family}", sources)

    total = sum(counts.values())
    print(f"Generated {total} texture files.")
    for label, count in sorted(counts.items()):
        print(f"  {label}: {count}")


if __name__ == "__main__":
    main()
