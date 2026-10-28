# Following Pokémon: planned dialogue, interaction and gift changes

Status: **design notes, not final.** Picked up after the standalone transition
and Frost's plugin tools are done. Last discussed 2026-10-28.

Goal of the hack this serves: more variety and replayability, with every
interaction deliberate and ecological. The follower should get the same kind of
rare, condition-driven interactions as the overworld Pokémon.

## Decisions so far

- Only the **base** follower package (grounded followers) is kept. Riding and
  custom Surf are dropped for good.
- **One-time gifts and the 10 claim slots are dropped.** Gifts become repeatable
  and come from dialogue rules.
- Trades and the GTS don't matter. Data stored on a Pokémon only has to survive
  the party, the PC, and saving.
- Pokeweb's follower editor is abandoned. The file formats can change freely;
  Frost's Following Pokémon editor is the only editor.
- Stay inside the existing files (`contextual-dialogues.narc`,
  `contextual-items.narc`, `emotes.narc`); a NARC can gain members without new
  ROM files.

## Conditions (dialogue, gifts and ambient emotes)

Every condition is optional; a rule applies when all of its set conditions match.
The existing ones stay: zone, species, form, type, HP, friendship, status,
facing, tile bounds.

New:

| Condition | Notes |
| --- | --- |
| Story flag | Set / not set. |
| Variable | Compare with `=`, `≠`, `≥`, `≤` against a value. |
| Season | Read from the game's own current season, so the season-changing code injection (day / week / month / real time / fixed) works without changes. |
| Time of day | The game's own periods (morning, day, evening, night), which already shift by season. |
| Weather | The field's current weather. |
| Level range | Min and max. |
| Nature | One or more natures. |
| Steps | Steps walked **as the current follower** (see Step counters). |
| Terrain | From the tile the follower stands on, using tile behaviors. The editor names groups such as grass, tall grass, sand, shallow (walkable) water, puddle, snow, cave, building, instead of raw values. |

## Selection: priority and weight

1. Collect every rule whose conditions all match.
2. Keep only the rules with the highest **priority**.
3. Pick one at random by **weight**.

The editor can suggest a priority from how specific a rule is (species + season
+ terrain above a generic line), so it rarely needs setting by hand. To be
refined when implemented.

## Dialogue

- **Steps:** a rule is a list of steps instead of one message. Each step can
  have a message, a box style, an emote, a motion and an action (give item,
  friendship, script).
- **Box styles:** Default, Bubble (current speech balloon with the tail),
  Yell, Event (plain window for descriptions such as "Pikachu is sniffing the
  grass."). Needs research: map each style to the game's own window functions.
- **Text codes:** the same as Frost's text editor: `[N]` new line, `[L]` scroll
  to the next line, `[C]` clear and continue. Multi-page messages remove the
  current 192-character limit per rule.
- **Preview** in the editor in the game's font and box size, for each box
  style, page by page, so lines can be checked without testing in game.
- **New emotes:** sprites made by the user once the list is decided; the editor
  imports them into `emotes.narc`. Currently seven from HeartGold/SoulSilver.
- **Friendship gain on talking:** small, probably once a day.
- **Ambient emotes:** a rule can be marked to show only an emote over the
  follower in the field when its conditions match (for example after N steps,
  or when an item is waiting), without stopping the player. Needs a cooldown.

## Interactions

- **Movement routines:** short movements played on the follower, with
  conditions on the surrounding tiles. Examples: run a circle around the player
  if all 8 tiles are clear; run a few tiles away and back, then give an item it
  "found". Open question: visual-only motion (current talk animations move only
  the sprite) or real tile movement (more work, more fragile).
- **Terrain dialogue:** covered by the Terrain condition.
- **Run a custom event script:** a step that starts a script, for special
  cases. Care point: hand control over cleanly so the follower isn't recalled.

## Gifts

- Gifts are a **dialogue step**: either a **specific item** (for particular
  lines) or a roll from an **item pool**. All dialogue conditions apply.
- **Pools tab** in the editor: named, weighted item lists reused by any rule
  (for example sweet berries, battle items, "trash" items like Leftovers).
  Zone, season and terrain conditions on the rule cover "only in cities" or
  "only in forests".
- **Pickup:** after N steps as the current follower, a chance to find an item
  from a pool. The follower shows an ambient emote; talking to it gives the
  item with its dialogue. Repeatable without limit, but can be made rare.
- A full Bag should keep the item waiting and say so, as now.

## Step counters (stored on the Pokémon)

- Counted only while the Pokémon is the **current follower**; kept when it is
  swapped out or placed in the PC.
- **Resetting counter:** probably reset when a gift is claimed or a rule says
  so. Used for "after N steps" pickups and dialogue.
- **Total counter**, if space allows, capped at a safe maximum.
- A pending found item also needs storing (item ID) so it survives saving.
- Research: find PK5 bytes the game never writes in the party or PC (the claim
  slots used the low 10 bits at Block C 0x1E; those bits become free).

## Memory and data layout

The runtime reads each archive into a fixed 4 KiB buffer that stays in field
memory, which is why the archives are capped at 4 KiB. ROM space is not the
limit.

Plan: split the data so only what's relevant is loaded.

- An **index by species**, a shared block of rules for any species, one block
  per species, and a separate **text section**.
- On follower change or zone change, load only the shared block and the current
  species' block into the 4 KiB buffer. Read message text from ROM only when it
  is shown.
- Pools in their own section, read when an item is rolled.

Total data can then grow to hundreds of KB for 600+ species; the limit is what
one species needs at a time.

## Later, unrelated ideas (feasible, look at after the follower work)

- Day/night wild encounter tables for each season (8 tables per area).
- Overworld encounters: 2–3 roaming Pokémon in tall grass using follower
  sprites, drawn from the area's encounters for that grass type, level scaled,
  touching one starts the battle.
- Gen 6-style party-wide Exp. Share.
- A very rare, expensive item that makes a Pokémon shiny (change only the top
  half of the personality value, so gender and ability stay the same).
