# Mosslight Clearing — UI Style Spec (Grove)

Cozy solarpunk RPG UI. Warm parchment panels over a painterly grove, storybook
serif for prose, small-caps display for headings, pixel font for chrome labels.
Target: Unity UI Toolkit (USS + UXML). Panels are meant to be 9-slice sprites;
everything else is flat fills, borders, and radius (USS-friendly).

## Fonts
- **Body / readable content** — Alegreya (serif). Sizes 13–19px, line-height ~1.55.
- **Decorative headings** — IM Fell English SC (small-caps serif). Entry titles,
  character names, thread titles, page titles. Sizes 16–22px.
- **UI chrome labels** — Silkscreen (pixel). ALL-CAPS section labels, tags, status
  text, key hints. Sizes 8–14px, letter-spacing .05–.1em.

## Color tokens
Chrome (HUD bars, prompts, dark record blocks)
- chrome-green        #2A3720   (pill/bar background)
- chrome-green-hover  #33421F
- chrome-text         #E7DCBE   (text on chrome)
- accent-green-bright #8FBF5A   (values, gauge fill, "known" ticks)

Parchment (panels, cards)
- parch-panel   #ECE0C2   (main panel fill)
- parch-card    #F4ECD3   (list cards / inset rows)
- parch-input   #F4ECD6   (input fields)
- parch-hover   #FAF2DC
- border-strong #9C8552   (panel outer border, 2px)
- border-soft   #C9B583   (cards, chips)
- border-hair   #DCCB9E   (inner row hairline)
- bevel-line    #DBCEA4   (inner 9-slice bevel line, 1px inset 5px)

Ink / text
- ink-body   #4A3E28
- ink-title  #3C3220
- ink-muted  #8A7A52
- ink-faint  #A79868   (locked / disabled)

Accents
- green      #5E8A3C  / green-dark #4A6E2E  / green-hover #6E9A46   (primary buttons, diamonds)
- amber      #C79A3C  (secondary diamond, "?" thread icon)
- gold-tag   #D9B36A bg / #4A3618 text       (THREAD / ARTIFACT tag pill)
- olive-found #5E7A2E (FOUND / positive status text)
- sage        #A6C088 (NPC nameplate, "met" chips)
- peach       #E3A368 (object/new-entry alert tile, "player" marker)
- lavender    #B7A0CE (artifact/etched-shard marker)
- tile-dark   #3C4A28 (icon tile background holding a colored diamond)

World grove (placeholder — swap for painted art)
- clearing gradient  #8AA750 → #6E8E3C → #4C6828 (radial, lighter center)
- canopy blobs       #3B571F / #466626 / #33501A (top + edge clusters)
- vignette           rgba(20,32,10,.58) at edges
- frame border       #26331d (screen bezel)

## Panel recipe (9-slice targets)
- Outer: fill parch-panel, 2px border-strong, radius 16px.
- Inner bevel: 1px border-soft/bevel-line inset ~5–6px, radius ~11–14px (the
  decorative inner line of the sprite).
- Card/list row: fill parch-card, 2px border-soft, radius 13px, padding 10–13px.
- Chip (filter): radius 999px. Active = chrome-green fill + chrome-text. Idle =
  #F2E9CE fill, 1px border-soft, ink-muted text.
- Tag pill: Silkscreen 8px, radius 5px, padding 2–7px (see accent tokens).
- Icon tile: 44px, tile-dark fill, radius 11px, containing a 20px colored diamond
  (rotate 45°). Portrait tiles use the character image with a 2px #7A8A4E border.
- Diamond bullets: 8–12px square rotated 45°, green (known) / amber (highlight) /
  hollow border-soft (unknown).

## HUD
- Top bar: chrome-green pills (location, understanding), radius 11px, 1px
  rgba(255,255,255,.08) border. Understanding gauge = conic accent-green on
  chrome-green with a #F0E7CC inner disc + number.
- Bottom prompt: chrome-green pill, cream key cap (#F0E7CC, radius 7px) + label.

## Dialogue panel
Parchment panel + inner bevel. Sage nameplate tab (top-right, IM Fell name +
Silkscreen role). Cream "← Leave" tab (top-left). Speech tail = 20px rotated
square on the right edge. Body prose Alegreya 19px. Choice buttons = parch-card
pills with a colored diamond; locked = dashed border-soft + 🔒 + Silkscreen hint.
Free-text input parch-input + green send button.

## Screens (see /screenshots)
- 01 world + interaction markers (parchment/peach/lavender labels)
- 02 dialogue (Rowan) with typewriter reveal + gated choices
- 03 Field Archive feed (filter chips, entry cards, unread dots)
- 04 thread entry (question, current understanding, progress checklist)
- 05 decode modal (Transmitter) over the fragment entry

## Notes for implementation
- Grove background is a placeholder (CSS gradients + speckle). Replace with the
  painted top-down grove sprite; keep the edge vignette for panel legibility.
- Character portrait shown is placeholder art (`assets/luma-portrait.png`).
- Narrative content is the working build's (Rowan / signal heart / Chorus /
  decode). Rename freely if the grove/lantern/shard wording is preferred.
