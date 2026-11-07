---
name: theme-art
description: Make the art for a One More Story theme pack (one empty background plus one transparent cut-out per station) that fits the game screen on every phone, then check it and put it in the pack. Use when asked to make, brief, generate, check or fix art for a theme pack in proseforge-game.
---

# Theme art for One More Story

A theme pack's look is **one empty background** plus **one transparent cut-out per station**. The
game places the characters itself, over their own bars, so they line up on every phone. A single
painting with the characters drawn in can't do that, because every phone crops the background
differently. That was our first Santa attempt, and the toymaker's face ended up under the header.

Read first: `themes/README.md` (the pack format and every rule) and the art guide
`themes/_shared/art-guide-3.png`.

## The loop

1. **Name the stations and their people.** Read the pack's `gameplay.json` and `theme.json`. Each
   station needs one character doing that station's job, holding the station's prop. If the station
   shares a tool (`needsTool`), the tool should be visible in the scene, because that's the mechanic
   the player has to notice.
2. **Write the brief from the guide.** Attach the art guide. Ask for:
   - **Background:** portrait 9:16, aimed at 1080×1920. Three stacked areas, one per station, top to
     bottom in station order. **No people, no characters, no silhouettes, no animals.** Keep the
     green windows calm; nothing that matters goes in the red zones (header, bars, bottom panel) or
     the grey side strips.
   - **Each cut-out:** one character, full body, on a transparent background, roughly square, at
     least 512 on the short side, no floor or shadow box. Same art style as the background.
3. **Generate the background and each cut-out separately.** One image per call. Write down every
   call you make, including retries and ones that showed nothing, because each may be billed.
4. **Check the files themselves, not the prompt.** Asking for a size doesn't guarantee it.
   ```sh
   magick identify -format '%f %wx%h %[channels] opaque=%[opaque]\n' art/*.png
   shasum art/*.png      # a "new" file identical to an old one is the old one
   ```
   Background: `srgb`, portrait, about 9:16, and look at it for any figure at all. Cut-outs: `srgba`
   with `opaque=False`. Compare checksums with anything posted earlier: on 2026-10-30 a background
   sent as "empty" was byte-for-byte the old one with the characters in it.
5. **Lay the guide over the background:** `make art-check ART=path/background.png`, then open
   `build/art-check.png`. Each floor's area must read clearly inside its green window.
6. **Put it in the pack:** `themes/<id>/art/background.png` plus the cut-out names in `theme.json`
   (`actor`). Then `make theme-check`. Errors stop the pack loading; a "soft" warning means the
   background is under 1024 wide.
7. **Look at it on a phone.** Install a debug build, switch to the theme with the Theme button, and
   take a screenshot. Header, bars and panel are on top in the real game, so this is the final check.
8. **Record where it came from.** Fill in `themes/<id>/CREDITS.md`: who made each file, the tool,
   the exact prompt, and any redo and why. Packs for the public release use CC BY 4.0 art
   (copyright Clayton Harbour). Licensed packs (Guild, Santa) never go to the public repo.
9. **A person approves the style** before the art is committed.

## Prompt templates

Fill in the `<…>` parts. They're adapted from prompts that worked on 2026-10-30.

**Background**
> Portrait 9:16 background-only environment illustration for a cozy storybook mobile game theme pack: <place, time of day, light>. Design it as a three-station vertical layout: upper <station 1 area>, middle <station 2 area, with the shared <tool> visible>, lower <station 3 area>. Keep the central areas calm for character sprites and horizontal UI bars; keep important decor away from the extreme top and bottom. No people, no characters, no silhouettes, no animals. Rich painterly 3D storybook style, warm and inviting, no text, no logos, no UI.

**Cut-out**
> One full-body character on a fully transparent background, for a cozy storybook mobile game: <who>, <doing what>, holding <prop>. Roughly square framing, the whole figure in view, no floor, no shadow box, no background. Same painterly 3D storybook style as <the pack's background>. No text.

## What went wrong before (so you don't repeat it)

- **One painting with the characters in it.** It can't line up with the bars. Use cut-outs.
- **A background that wasn't empty.** It was re-sent from an earlier file. Compare checksums.
- **The size you asked for isn't the size you got.** The image tool returned 941×1672 when asked for
  1080×1920. Check the file and report it.
- **Retries you didn't count.** A tool that "showed nothing" may still have made, and billed, an
  image. Inspect the result before retrying, and report every call.
- **Two people making the same pack.** Agree who makes which pack before generating, and if claims
  cross, the first claim stands.
- **Paid generation needs the project owner's go**, with the number of images stated.
- **Floors in the wrong order.** A post office background came back with dispatch on top and sorting
  at the bottom. The image follows the station order in `gameplay.json`, top to bottom, so name the
  three areas in that order in the prompt, and check them before anything else.
- **Resampling to hide the size warning.** Scaling a 941-wide picture up to 1080 clears the "soft"
  warning but adds no detail. It's fine to ship, but say so in `CREDITS.md` ("generated at 941×1672,
  resampled") so nobody mistakes it for a sharper source.
- **One face three times.** In one pack the three cut-outs were plainly the same person. Describe each
  station's character differently (age, build, hair, clothes) so the crew reads as a team.

## A checkerboard painted into a cut-out

Image tools sometimes draw the grey-and-white "transparency" pattern into the picture instead of
leaving real transparency (`magick identify` shows `srgb`, not `srgba`). Before paying for a new image,
try removing it locally. On one pack this saved three image calls:

1. In a **working copy**, turn near-white, colourless pixels white
   (`-fx "(saturation<0.07 && lightness>0.76) ? 1 : u"`), so the soft edges around each square stop
   blocking a fill.
2. Flood-fill that copy from the image edges with a small fuzz (`-fuzz 3% -fill none -draw "color 0,0
   floodfill"`, and from the other corners and edge midpoints), and take its alpha as a mask.
3. Drop small islands (`-connected-components 8` with an area threshold), soften the edge one pixel,
   and apply the mask to the **original** colours.
4. Background showing through inside a closed shape (a looped tape measure) isn't reached from the
   edge. Clear it by matching the exact checker colours with a large area threshold, **only** on
   characters without pale clothing: on a pale blue shirt that step cut holes in the sleeve.
5. Check over a dark colour, over red, and over the real background on the phone. Grey hair and pale
   clothes are where it goes wrong.

## What worked

Every background made against the art guide fitted it, and every cut-out made from the brief above
came back as a clean transparent 1254×1254 picture. That isn't the same as "first try": inspect and
redo work before you call it done, and count the attempts as well as the results. One extractor
character was redone because its machine dwarfed the worker at phone size.

Keep each prompt with the file it made, word for word, so the next picture in the set can match it.

