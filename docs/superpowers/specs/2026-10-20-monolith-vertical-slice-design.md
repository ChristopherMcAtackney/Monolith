# Monolith: vertical slice design

Date: 2026-10-20. Status: approved by the user on 2026-10-20; story and tone revised by the user on
2026-10-23 (the human diaspora, gallows humour, 80s synth).

Monolith is a single-player space combat game in Unity, in the spirit of FreeSpace 2 (FS2) and, to a
lesser degree, TIE Fighter (TF). It is built entirely by agents, headless, using the gauntlet loop. This
document describes the game and its first milestone, a vertical slice: one Oort Cloud mission with most
of the mechanics, feel and style in place, built on clean systems so new missions, ships and weapons can
be added later without rebuilding anything. Its one big addition to the FS2 formula is the cabin: at a
few key moments the pilot gets up out of the seat and is a person inside a small ship (section 6).

Throughout, **(user)** marks something the user decided. **(call)** marks a decision made during
brainstorming on the user's behalf, which the user can override. Anything unmarked is description.

---

## 1. Intent

The thing that matters is the feeling of being a starfighter pilot in a much bigger battle (user): a
small, quick, weighty fighter weaving between capital ships trading fire, wingmen on the radio, and the
mission changing around you. The user loved FS2's combat above all: "a very smooth and satisfying
feeling", missions with "lots of tense moments punctuated by huge fights", and "excellent narrative
storytelling", while being light on customisation and resource management ("spreadsheet crap") (user).

When in doubt, choose what makes the moment-to-moment flying and fighting feel better, then what makes
the battle readable, then everything else.

### Being a person in the ship (user)

Inspired by Outer Wilds, where you feel like a person inside your ship because you can get up from the
seat, but serious and somber rather than cute or folksy. Settled into the seat, the ship all but
disappears and the HUD takes over (FS2). Get up, and it becomes a physical, immersive experience in a
very small machine. It is for key moments, for example going dark and leaving the cockpit while an enemy
capital ship scans you, so it takes you for a derelict or a casualty of the battle. This also settles
FS2-versus-TF for the view: FS2 in the seat, a real cabin out of it.

### The premise (user)

Revised by the user on 2026-10-23. You fly for the human diaspora: the descendants of the people who
spread out from Earth during its conquering expansion, now arriving at the outer edge of the solar system
to fight the humans who stayed behind (user). The campaign fights its way inward: the Oort Cloud, past
the ice giants, Jupiter, the asteroid belt, Mars, and a huge final battle at Earth, with lots of capital
ships and fighters. Earth gets nuked at the end (user; the idea began as Ender's Game turned around).

- **Earth's humans** stayed behind and have nearly ascended into techno-god machine tyrants, like Mass
  Effect's Reapers. They aren't quite gods yet, but they think they're close (user). They hold
  "biologics" in contempt and see the diaspora as impure, fallen from grace (user). They still talk to
  you: pronouncements, taunts and demands over the radio (user).
- **The diaspora** diverged as it spread: generations on other worlds changed them, so they are
  noticeably different from baseline humans but still unmistakably human (user). The player must feel
  affinity for the people they fly with (user). Some diaspora peoples allied with alien species (user),
  often ones Earth's expansion had subjugated (call), and those allies fly alongside them.
- **Not Star Wars:** no chosen one, no mystic order, no destiny (user, choosing ideas 1 and 3 from the
  brainstorm). Earth cut its own diaspora loose; the diaspora comes home with the peoples Earth
  conquered. The gods are what humanity became and the diaspora is what's left of what it was, so the
  gods' taunts can be personal: "we were you, once" (call).
- **The story needs a MacGuffin**, to be worked out later (user).
- **Tone (user, 2026-10-23):** the universe plays it completely straight. The stakes are real, and the
  gods are terrifying and speak like scripture. The protagonists are gritty, ordinary people with
  gallows humour: the contrast of Ghostbusters' three New Yorkers facing a Sumerian god. Touchstones:
  Venkman against Gozer, the Nostromo's crew in Alien, the Colonial Marines in Aliens, The Expanse's
  Belters. The humour is dry and comes only from the crew, never from the world winking at the player,
  and it is never zany (the user's other games were zany; this one is not).
- The story's emotional arc ("the pathos") is deliberately parked for now (user). The slice needs only
  enough story to frame one mission: a briefing, radio chatter and a debrief.

## 2. Goals, non-goals, anti-goals

**Goals for the slice**
1. Flying feels like FS2: arcade-leaning but weighty, smooth, the ship goes where it points (user).
2. One complete, replayable Oort Cloud mission: dogfighting, capital ships, and a human outpost (user),
   paced as tension building into big fights.
3. The core combat features in section 5, working together.
4. Photorealistic visuals (user), with FS2's sense of spectacle (user).
5. Clean, data-driven systems so new missions, ships and weapons are content, not code.
6. An architecture that does not rule out co-op later (section 10).
7. The cabin (section 6): briefing and loadout in the cabin, one go-dark moment, one hand repair, and
   the death sequence (user's idea; which moments go in the slice is a call).

**Non-goals for the slice**
- The campaign beyond the one mission, campaign map, save system beyond what one mission needs.
- Co-op or any networking (keep it in mind only, user).
- Story arc, cutscenes, the diaspora's worlds or alien-ally lore beyond what the mission needs.
- Joystick tuning (supported, not tuned around; see section 8).
- Walking anywhere outside the cabin, spacewalks, inventory, survival meters.

**Anti-goals: things that would make it a failure even if it technically works**
- Spreadsheet systems: economy, crafting, upgrade trees, deep loadout fiddling (user).
- Floaty, drifting, Newtonian flight; or a "100% accurate physics sim" (user). Also twitchy flight.
- Any third-person or external view of the player's ship, ever (user).
- Battles that are noise: if the player can't tell what matters, the battle has failed.
- Glossy, decorative UI. The player's tech is rough and minimal (user).
- Cabin moments that feel like chores. Getting up is rare, tense and purposeful; the seat is where the
  game lives (call).
- Anything copied from FS2 or TF: code, assets, text, or audio (user; section 14).
- Star Wars trappings: chosen ones, mystic orders, destiny (user).
- Zany humour, or a world that winks at the player. The jokes belong to the crew (user).

## 3. The vertical slice

### Flow (call)
Boot → minimal title/main menu → in the cabin: briefing at the plan table → loadout at the weapon rack →
sit down and launch → mission → debrief at the plan table → back to the menu. Plus a pause menu and a
controls/options screen. Command-line flags and a debug menu to jump straight into the
mission, skip phases, toggle invulnerability, and spawn waves (Star Frog had the same and it paid off).

### The mission (call: an outline the builder may reshape, keeping its beats)

A strike at Earth's early-warning outpost, built into a comet nucleus in the Oort Cloud: Earth's
outermost picket. It should run roughly 10 to 15 minutes.

1. **Quiet opening (TF-style).** The strike group drops in from its jump short of the outpost, with
   almost nothing on radar. Ice bodies drift by, the Sun is just the brightest star. Short radio
   exchanges with the wing. Tension, not action. The player has time to feel the ship.
2. **Going dark.** To get close unseen, the wing goes dark. The player gets out of the seat, pulls the
   main breaker and drifts among the ice while the outpost's scan sweeps past, its light crawling
   through the canopy (section 6). Being detected is not a failure: the outpost is alerted early, so its
   turrets are ready, its interceptors launch sooner and the comms array starts its warning sooner.
3. **The strike.** Back in the seat, power up, and attack. The outpost's turrets wake up and
   interceptors scramble from it.
4. **The fight opens up.** Waves of Earth's fighters, arriving on FS2-style triggers (section 7). The
   outpost has a comms array: if it isn't knocked out (subsystem targeting), it warns the inner system and
   a bigger human force will come. This is the first real decision. Call: if the array survives, the
   warning gets out and a heavier human response arrives late in the mission. It makes the mission
   harder, not failed, and the debrief says so.
5. **Escalation.** A human corvette arrives, then a destroyer with beam cannons and bombers. Bombers go
   for the player's own capital ship (the "stop the bombers" job). The destroyer's beams visibly charge
   before firing. At a dramatic point, a heavy hit knocks out something the ship can't heal by itself,
   and the player has to leave the seat to fix it by hand while the battle carries on outside.
6. **Climax.** Disable the destroyer's beam turrets or engines so friendly capital ships can finish it;
   the outpost falls. Huge explosions, the battle resolves.
7. **Debrief.** At the plan table, terse, in the same rough style: objectives met or failed, kills,
   losses.

If the player dies at any point, the death sequence plays (section 6).

Cast for the slice (call): the player's fighter, 2 or 3 named wingmen (a mix of diaspora humans and alien allies), at least one friendly capital
ship (the strike group's carrier or cruiser, the bombers' target; more are allowed if the climax needs
them), the human outpost, human interceptors, heavy
fighters, bombers, one corvette and one destroyer. Peak counts around 3 to 4 capital ships and up to
roughly 20 fighters in play at once.

### Lighting in the Oort Cloud (call)
At that distance the Sun is a very bright point and gives almost no light. Realism here must not cost
readability: use a hard directional "sun" light, HDR auto-exposure, the outpost's floodlights, engine
glows and weapons fire to light the scene. Readable beats physically correct.

## 4. Flight

### Feel (user)
Arcade-leaning but weighty; smooth; ships go where they point; not a physics sim. "More FS2 than TF".

### Model (call: FS2's behaviour, our own code)
Every axis of motion chases a goal value with an exponential approach, instead of forces and inertia:

    v(t) = v_goal + (v_start - v_goal) * e^(-t / tau)

This is frame-rate independent, and `tau` (a time constant in seconds) sets how "heavy" each axis feels.

- **Turning:** stick deflection × max turn rate gives the goal rate per axis (pitch, yaw, roll). Max
  turn rate comes from "seconds for a full 360°". Turn rate chases its goal with a short time constant,
  so turns ramp in and settle; releasing the stick bleeds rotation off quickly.
- **Auto-bank:** yawing adds roll at half the yaw rate, in the opposite sense, as FS2 does.
- **No drift:** sideways and vertical velocity are damped to near zero within about 0.1 to 0.25 s, so
  velocity follows the nose. No strafing, no reverse thrust on fighters (call: as FS2).
- **Throttle:** forward speed chases throttle × max speed, with separate speed-up and slow-down time
  constants; a "match target speed" control.
- **Afterburner:** a higher goal speed with a fast ramp, burning a fuel bar that recharges when not in use.
- **Getting knocked about:** big hits, collisions and shockwaves loosen the damping for about 2 s (fading),
  and add rotational shake. The player feels physically shoved.
- **Collisions:** bump and damage, scaled by speed and mass; grazing a capital ship hurts but is not
  instant death.

Starting numbers for every ship class are in the appendix (FS2's real values). Every tuning constant
lives in data with a note of where its value came from.

### Measured feel (call)
Like Star Frog's ROM-traced flight, the flight model writes a per-tick trace (inputs, rates, speeds), and
a test compares it against the curves FS2's model produces for the same ship numbers: time to reach
63% / 95% of top speed and of max turn rate, side-slip decay, settle time after stick release,
afterburner ramp. These pass within a tolerance. The user's playtests overrule the traces.

## 5. Combat systems

All of these were proposed and accepted (user approved the list; details are calls unless marked).

- **Targeting:** cycle hostiles, target nearest attacker, target the ship in the reticle, cycle
  subsystems on the current target (turrets, beam cannons, engines, hangar, comms array). A lead
  indicator for guns. A terse target box: class, name, hull and shield, distance.
- **Guns:** energy-based, from the weapon energy pool. Fast-firing anti-fighter gun; slow heavy gun for
  hulls and subsystems. Each weapon has separate damage multipliers against shields, hull and subsystems.
- **Missiles:** limited ammo. A heat-seeker (fire and forget, easy to dodge); a lock-on missile that
  needs time on target to lock; a heavy torpedo for capital ships (slow, can be shot down). Locking on
  is tense: lock tone, then launch. Being targeted is tense too: missile warning.
- **Countermeasures:** limited decoys that can spoof incoming missiles.
- **Afterburner:** see flight.
- **Power (simplified from FS2's three sliders):** one quick choice that favours shields, guns or
  engines, or balanced. It shifts recharge rates (and engine favour adds a little speed). A split-second
  call, not housekeeping.
- **Shields:** one shield around the whole ship (no quadrants: cut as fiddly), recharging from energy.
- **Damage you feel:** the player's ship has subsystems. Damaged engines cap speed; a damaged weapon
  mount takes a gun offline; damaged sensors put static on the radar and target box. The player knows
  from how the ship behaves before reading a number.
- **Destructible parts and visible, progressive damage (user, 2026-10-24):** as in FS2, ship parts
  are separately targetable and visibly destroyable: weapon launchers, flak and turrets, sensor and
  radar arrays, comms, engines, the hangar. Destroying one has a real effect (a silenced turret, a blind
  ship, a crippled engine). Hulls show damage building up as they are hit: scorching, holes, sparks,
  venting, pieces breaking away. Capital ships first, but every ship class supports it, fighters
  included.
- **Living-tech self-healing (user's idea):** the diaspora's ships slowly heal hull and subsystems,
  using living technology from their alien allies (call). It starts after a few seconds without taking damage and is never fast enough to
  tank a fight. It creates a rhythm: dive in, take damage, break off to heal, go back in. Earth's ships
  do not heal; they rely on repair ships or drones (not needed in the slice).
- **Wingman orders:** attack my target, disable my target, cover me, form up. From a hold-a-button quick
  menu so they don't use up buttons.
- **Radar:** always on the HUD, FS2-style (blips above/below the plane), with incoming missiles shown.
- **Battle map (user):** a switch to a full map of the area, very retro: blips on a radar-style display,
  no 3D renderings of ships. Call: it does not pause the game (keeps tension, and co-op couldn't pause).
  In the seat it is a quick switch; the same display is physically on the cabin's plan table.
- **Objectives:** a short directives list on the HUD (what to do right now), plus primary, secondary and
  bonus objectives in data (TF had hidden bonus objectives; the system supports them, the slice may use
  one).
- **Loadout:** before the mission, pick 2 guns and 2 missile types from a small set, by loading them at
  the cabin's weapon rack (user's idea) rather than on a menu screen. Nothing more (call). One player
  ship class in the slice (call).

## 6. The cabin (out of the seat)

### What it is (user)
A serious, somber take on Outer Wilds' "person in the ship" feeling. The ship is very small: out of the
seat you only move a few steps. There is artificial gravity, so you walk (user). In the seat, the cabin
all but disappears and the HUD takes over. Out of the seat, the cabin is physical and immersive.

### Rules (calls)
- **The seat is home.** Stand up or sit down by holding a button. The change is short and physical: the
  view rises out of the seat and the HUD collapses into the cabin's own displays; sitting reverses it.
- **While you're up, nobody is flying.** The ship holds its last heading and speed. In a fight, getting up
  is a gamble.
- **Your own hands are visible**: human, but noticeably changed by the diaspora's divergence (call),
  doing physical things. Seeing them
  work tells the player who they are without a word of story.
- **Interactions are short physical actions** (pull, hold, turn, push; a few seconds) on one context
  button, with minimal prompts in the rough tech style. No puzzles, no inventory, no minigames, and they
  don't pause the game.
- **Out-of-seat moments are rare and purposeful**: set up by mission data or triggered by specific
  damage, never routine upkeep.

### The interactions
- **Go dark:** pull the main breaker. The HUD dies, the cabin goes black apart from emergency glow, and
  the ship drifts. A ship or station's scan sweeps past: its light crawls through the canopy, the hull
  creaks, you hear your own breathing. Earth's forces scan for life signs, so going dark hides the fact that you're alive (call).
  Whether you are detected depends on your power state (the builder
  may add more, such as moving or making noise while dark). The result is a mission event, so the mission
  data decides what detection means.
- **Hand repairs:** for damage beyond self-healing, such as a destroyed engine module or weapon mount.
  It's living tech: you press or reseat a module into the ship's tissue and hold it while it knits. A few
  seconds, while the ship is exposed.
- **The plan table:** the retro blip display. Briefings and debriefs happen here, and it is the same
  battle map the seat can switch to.
- **The weapon rack:** the loadout, chosen by loading it before launch.
- **Later, not in the slice:** quiet moments during jumps between missions, for story; personal objects
  that build affinity.

### The death sequence (user)
Terrifying, and first person throughout: the ship's geometry rips apart around you, you're physically
flung out into space, and then you die. It works whether you are seated or standing. Variations by cause
are the builder's call; the slice needs at least one strong version.

### What the mission system needs for it
- **Conditions:** player seated or standing; ship powered or dark; player detected by a scan; a
  subsystem needs a hand repair.
- **Actions:** start a scan sweep from a ship or station; call for the player to leave the seat (a radio
  line plus a prompt); knock out a subsystem so it needs a hand repair; set the cabin's lighting state.

### Look and sound
Cramped, rough and utilitarian; photoreal; nothing decorative. Modelled by script in Blender, like the
ships. Light comes from dim instrument glow, hard sun and starlight through the canopy, and emergency
lighting when dark. Sound is close and physical: hull creaks, your breathing, the battle muffled through
the hull, terse alarms. Realism references (call): real spacecraft interiors such as Soyuz, Apollo and
ISS modules.

## 7. Enemies, AI and pacing

### Earth's forces (user: "take basic direction from FS2 and/or TF")
The roles follow FS2; the fiction is that Earth's forces are near-machine now (user). Calls,
approved by the user in the playback:
- **Most fighters are drone-like:** no fear, no breaking off when hurt (FS2's courage at zero).
- **A few "ascended" aces** are the frightening exceptions: much better pilots, recognisable ships, and
  cold contempt over the radio. Not required in the slice, but the AI profiles must allow them.
- **Their radio** is contemptuous, grand and scriptural, aimed at "biologics" and the "fallen" diaspora
  (user). Against it, your crew's dry gallows humour (user).

The roster:
- **Interceptors:** fast, fragile, in packs; go straight for the player.
- **Heavy fighters:** slower, shielded, missile-heavy; escort bombers and capital ships.
- **Bombers:** slow, tough; make runs on capital ships with slow torpedoes that can be shot down.
- **Corvette:** flak and anti-fighter guns.
- **Cruiser/destroyer:** big beam cannons that visibly charge before firing; launches fighters from a
  hangar; turrets with lead.
- **Outpost:** fixed turrets, a hangar, a comms array.
- Each class has a distinct silhouette (TF touch), so the battle reads at a glance.

### AI behaviour (from FS2)
- **Fighters:** pick a target and get on its tail; fire guns with lead; fire missiles when locked; jink
  and afterburn away when hit; break off and re-engage; peel off when badly damaged depending on courage.
  They also attack the player's wingmen and capital ships, not only the player.
- **Bombers:** line up on a capital ship, launch, peel away; escorted.
- **Turrets:** track with lead, flak bursts in the player's path; beams telegraph and then sweep.
- **Wingmen:** the same AI, taking the player's orders.
- **Skill knobs (FS2's ai table):** accuracy, evasion, courage (how long a pilot stays in a fight when
  hurt), patience (how long it holds a firing line before shooting). Difficulty levels, drones and
  aces are different values for these four.

### Pacing: event-driven, not timer-driven (from FS2's missions)
FS2 missions are scripted almost entirely with reactive triggers, which is why they always feel pressured
but never swamped. The slice's mission system must support at least:
- **Conditions:** time elapsed; ship or wing arrived / destroyed / disabled (with delay); percentage of
  a wing destroyed; hull or subsystem of a ship below X%; player in range of something; another event
  true (with delay); objective state; and, or, not.
- **Actions:** bring in a ship or wing (with an arrival effect); send a radio message; set or complete an
  objective; give an AI order (attack, guard, disable, depart); free or hold beam weapons; change the
  music state; end the mission.
- **Patterns to use:**
  - The next enemy wave arrives when the current one is about two-thirds destroyed.
  - Friendly capital ships arrive one after another, a few seconds after the previous one dies (relay).
  - Reinforcements trigger off a big target's hull thresholds (for example below 70%, then below 40%).
  - Radio lines react to events: "we lost X", "the destroyer is critical", "bombers inbound".
  - The quiet opening that turns (TF).
- **Measured pacing (call):** the mission logs a timeline (threats alive, distance to the nearest
  hostile, events fired). During the combat phases, no stretch longer than about 30 s without a threat,
  and the number of hostiles engaging the player stays within a per-difficulty cap.

Missions are data files (one file per mission), not C#. A future mission editor is out of scope.

## 8. Controls and camera

- **Xbox controller first** (user). Everything is tuned for it. Stick response curves and deadzones are
  part of the flight feel and get tuned and traced with it.
- **Joystick support** (user: "it'd be cool"): supported via Unity's Input System, not tuned around.
- **Keyboard** fallback, mainly for development and testing (call).
- **Pad layout (call):** start from Star Wars: Squadrons' scheme, which has solved full-featured space
  combat on a pad; wingman orders and power on a hold-a-button quick menu. The builder finalises it.
- **Camera: first person only, never any third-person or external view** (user).
  - **Seated:** the cabin all but disappears and the HUD takes over (user): FS2's open view over space,
    with at most a faint suggestion of canopy frame at the edges (call). No TF-style cockpit dashboard.
  - **Standing:** first person in the cabin, walking under artificial gravity (user).
  - **Death:** first person throughout, no external death camera (user; section 6).
- **Cabin controls on the pad (call):** left stick walks, right stick looks, one context button; hold a
  button to stand or sit.

## 9. Look, sound and juice

- **Photorealistic all round** (user), with FS2's spectacle (user): capital ships that fill the screen,
  beams crossing, planets hanging behind the fight.
- **The player's tech is rough and minimal** (user): "like no spare energy could be spent on unnecessary
  fluff". The HUD, battle map, cabin displays, briefing, debrief and menus share one style: single-colour line graphics,
  blips, terse text. The contrast between spare tech and photoreal spectacle is the point (call).
- **Ships and stations are modelled by script in Blender** (user), as in Trap Door. PBR materials,
  panel detail, wear, lights and engine glows. Capital ships need to read as enormous: surface detail at
  several scales.
- **Ship design language: FS2, not TIE Fighter or Star Wars (user, 2026-10-23).** Long, layered
  silhouettes built from distinct plated segments; panel lines; rows of lit windows and small coloured
  running lights that give away the scale; spines, fins and some asymmetry; engines as glowing recessed
  exhausts. Never wedges, grey bricks covered in random greebles, Star Destroyer-style towers or flat white
  engine discs. This applies to both sides and to every placeholder (the first flight build's placeholder
  hulls looked like TIE Fighter, and the user called it out).
- **Rendering, materials and light: modern FS2 made photoreal, never 90s TIE Fighter (user,
  2026-10-23).** What the user objected to most in the first placeholders was this, more than the
  geometry. Plated PBR surfaces with panel variation, coloured trim and wear, and real specular response,
  never flat grey or noise textures. A hard sun key light plus a subtle coloured ambient fill from the
  environment, so shadow sides keep their form and never crush to pure black. Emissive window rows and
  running lights that bloom; engines as hot, glowing exhausts with a colour gradient, never flat discs.
- **Your side's look:** the diaspora's ships are worn, used-future human hulls, practical and patched,
  in the spirit of the Nostromo and The Expanse's Belter ships (call), with alien-ally technology grafted
  on. That grafted tech, and the HUD and cabin displays it powers, take inspiration from the technology
  of the aliens in Independence Day (1996), not from their bodies (user). Approved guess for those
  parts: dark, ribbed, biomechanical shapes and glowing, glyph-like displays, kept rough and spare; it is
  where the self-healing comes from. Inspiration only; no copied designs.
- **Earth's look (proposed, approved in the playback):** cold and precise. Matte black and chrome,
  perfect hard geometry, harsh white light, and a deep Reaper-like horn when a capital ship arrives. The
  contrast with your patched, part-living ships helps the battle read at a glance.
- **Sky and planets (call):** a real star catalogue / Milky Way panorama for the sky (for example NASA
  SVS "Deep Star Maps"; verify licence). Planets later from real NASA and mission imagery (verify licence
  per source; much is public domain). The slice needs the sky and the Sun only.
- **Music: 80s epic synth (user, 2026-10-23).** Touchstones: Vangelis's Blade Runner, Tangerine Dream,
  John Carpenter, Mass Effect 1's score. Its structure follows FS2's music table: the music follows the
  fight with ambience, three battle intensities, short stings for friendly and enemy arrivals, and
  victory, failure and death. Driven by mission state and the music actions in mission data. Agents
  compose it as MIDI (user) and render it through an analog-style synth with a proper 80s sound (call:
  polysynths, chorus, big reverb, gated drums; never plain General MIDI); the user can replace any
  track with their own MP3s later (user). The same 80s feel carries into the atmosphere (user).
- **Radio voices (call):** generated with Trap Door's TTS pipeline, with a radio filter. Diaspora crews
  sound human, with varied accents and dry humour; alien allies get an alien vocal texture; Earth's gods
  sound vast, cold and machine-edged. The user's own recordings can replace
  any line. Radio and briefing text are data.
- **Sound effects:** weighty and physical: hits, shield and hull impacts, lock tones, missile warning,
  beam charge-up, big explosions. Sound in space, as FS2 has.

### Juice (user: "really quite a bit of effort")
Explosions, lasers, rumble, oomph and screen shake get serious effort (user). The rules come from the
research behind Dr Insane's juice pass (see section 14):
- **Success gets the loudest reaction.** Feedback that fires only on success raises enjoyment; big effects
  that fire regardless, or that cover the action, lower it (Kao et al., CHI 2024). Kills and destroyed
  subsystems get the biggest reactions. Nothing ever hides your target or the HUD's essentials.
- **Medium beats extreme** (Kao 2020). Medium is the default, with an intensity setting.
- **Shake is "trauma"** (Eiserloh, GDC 2016): hits add trauma, which fades; the shake grows with trauma
  squared and moves smoothly (noise), not as random jitter. In first person it is mostly rotation.
- **Accessibility:** shake and flashes have an intensity setting and an off switch (Xbox Accessibility
  Guideline 117).

What gets it (calls):
- **Explosions,** layered and scaled by size: a flash, a fireball that dims fast in vacuum, glowing
  debris with trails, sparks, and a shockwave ring. Capital-ship deaths are staged: internal blasts, the
  ship breaking into sections, then a final flash and a shockwave that shoves nearby ships, the player
  included (the loosened damping in section 4).
- **Weapons:** the user loved FS2's "big powerful meaty lasers everywhere", so capital-ship battles are
  thick with heavy laser and beam fire. Bright HDR bolts with bloom and muzzle flash; hits that look different on shields (a
  ripple) and hulls (sparks, debris, scorch); beams with a visible charge glow before they fire; missile
  exhaust glow.
- **Rumble:** the pad's motors are driven by the same trauma and events: gun fire, afterburner, hits,
  nearby explosions, a beam charging. Rumble in the triggers too, if Unity can do it on Windows (nice to
  have).
- **Sound:** layered and weighty, with near and far versions; the biggest moments duck everything else.
- **In the cabin:** hull hits shake it, lights flicker, sparks fly.
- **Hit-stop** (a tiny freeze or slow-down on kills) is tried in the sandbox, not assumed.

**The juice sandbox (call):** this worked especially well for Dr Insane. An in-game test build where the
user fires events (fighter kill, bomber kill, capital-ship death, shield hit, hull hit, near miss, beam
hit), switches each effect on and off, picks intensity presets, slows time down, and tunes it by feel.

## 10. How it gets made (calls, carried over from Star Frog and Trap Door; the user's direction is to
treat those as useful, not gospel)

- **Engine:** Unity 2022.3.26f1 (the only editor installed), **HDRP** for physically based lighting and
  exposure. Machine: RTX 4070 Ti (12 GB). Blender 5.2 is installed for scripted models.
- **Headless, agent-built:** compile, build, capture and Blender all run from scripts (Trap Door's
  `tools/unity.ps1`, `tools/capture.ps1`, `tools/blender.ps1` are the model). Nobody edits in the
  Unity editor by hand.
- **Code-first structure:** an empty boot scene; everything constructed from C#. One folder and assembly
  per system (for example Core, Flight, Weapons, AI, Mission, Hud, Audio, Render, Menus). Systems talk
  through a shared world state and named game events, not direct references. One composition root wires
  them up.
- **Simulation:** a fixed-rate simulation tick (60 Hz is the starting point), rendered with
  interpolation. Every ship is flown by a "control input" each tick from a source: the player's pad, the
  AI, a scripted test, or later a remote player. Game rules live in one authoritative place. This is the
  co-op readiness requirement: no networking now, but nothing that assumes one local player is the only
  pilot.
- **Data-driven content:** ship classes, weapons, AI skill profiles, missions, radio lines, briefings and
  music cues are data files (FS2's tables are the model). Adding a ship, weapon or mission should not
  need code.
- **Capture harness:** scripted, deterministic screenshots, clips and traces from real builds
  (flight traces, mission timelines, performance runs), for the gauntlet loop's critics and tests.
- **Docs:** short, split by system. Star Frog's single architecture file grew into a wall of text that
  fresh agents struggled with; avoid that.
- **Outside material (user):** free libraries (for example CC0 textures, sound libraries, sky maps) are
  fine as placeholders until the look is established, then replaced; the user doesn't want obviously
  stock textures in the end. Never Unity Asset Store models for the ships. Ships, stations and the cabin
  are always our own Blender models.
- **Agent rules (Trap Door's, which the user agreed to keep):** Sonnet builders by default; Opus only for
  hard code and only with the user's OK (expected for the flight model, the AI and the mission system);
  at most two builders at once; no timer check-ins; the user's playtests are the main judge, with an
  occasional single blind critic.
- **Repo:** https://github.com/ChristopherMcAtackney/Monolith (user).

## 11. Quality bar (for the gauntlet loop)

The user's playtests are the main judge and overrule everything below.

1. **Flight feel:** traces match FS2-derived curves within tolerance (section 4).
2. **Look:** blind critic on in-game stills; plus a readability check (can you tell what matters?).
   **The bar (user, 2026-10-20): The Expanse's space battles, with FS2's big, meaty lasers everywhere.**
   The critic compares against real frames from The Expanse's battle scenes for photoreal look, and
   real footage of FS2's capital-ship beam battles for the weight and density of laser fire. Reference
   images are kept locally for the critic and never go into the game. Cabin stills are judged against photos of
   real spacecraft interiors.
   Lesson from Trap Door: critics are reliable about which image is real, unreliable about how to fix
   ours. Treat the verdict as the signal, the prescription as noise.
3. **Pacing:** the mission timeline meets section 7's measured pacing.
4. **Performance:** a steady 60 fps minimum at 1920×1080 on the RTX 4070 Ti during the mission's
   biggest moment (call), with headroom for the larger battles the campaign will need.
5. **Clean content boundaries:** a second, different test mission can be made from data alone, without
   code changes.
6. **Cabin moments:** the go-dark moment, the hand repair and the death sequence are judged on the
   user's playtests: tense and physical, never a chore.
7. **Juice:** the user tunes it in the sandbox. Frame rate holds during the biggest capital-ship death.

## 12. Build order (call)

Flight feel comes first and gates the rest. The first deliverable is a flight-only playtest build: one
fighter, a few targets and some capital-ship hulls to fly around, and the pad. The juice sandbox comes
soon after, so the user can tune the feel of hits and explosions early. Work that depends on the
flight numbers (AI tuning, weapon speeds and lead, engagement ranges, mission distances) waits for the
user's sign-off on the feel. Work that doesn't (Blender models, the sky, HUD style, the mission and event
system, the audio system, the cabin's interior model) can go ahead in parallel. Walking, the stand/sit
change and the cabin interactions come after the flight sign-off.

The cabin makes the slice noticeably bigger. If something has to give, protect the go-dark moment and
the death sequence first, then the hand repair; the plan table and weapon rack can fall back to plain
screens (call).

## 13. Left to the builder

Exact tuning numbers (starting from the appendix), the mission's layout and exact script, the pad
mapping, the number of difficulty levels, HUD layout within the stated style, ship and station designs within the stated direction,
weapon names and visuals, sound design, the debug and capture tools, file formats, and how the
architecture is split in detail, the cabin's layout, and the exact interactions and death variations. Placeholder names are fine for the diaspora peoples, alien allies,
factions, ships and characters.

## 14. Reference material and boundaries

- FS2 install: `C:\Program Files (x86)\Steam\steamapps\common\Freespace 2`. Tables (ships, weapons, ai,
  music) and all campaign missions are inside `Root_fs2.vp`. VP format: `VPVP`, int version, int
  directory offset, int entry count; 44-byte entries (int offset, int size, char[32] name, int
  timestamp); entries with size 0 and no extension are directories, `..` goes up. Missions are plain
  text; `FreeSpace2.fc2` gives the campaign order.
- FS2 source for reference: https://github.com/ptitSeb/freespace2 (the flight model is in
  `src/physics/physics.cpp`).
- TF install: `C:\Program Files (x86)\Steam\steamapps\common\STAR WARS Tie Fighter`. Mission files
  (`remastered\MISSION\*.TIE`) are binary, but briefing questions, radio lines and objective text are
  readable strings.
- Dr Insane's juice research and plan: `C:\Users\chris\Documents\Code\Dr_Insane\docs\superpowers\plans\2026-09-27-juice.md`.
- Outer Wilds install: `C:\Program Files (x86)\Steam\steamapps\common\Outer Wilds`, the reference for
  the feeling of being a person inside your ship (in the opposite tone to ours).
- **Boundary (user):** these are reference only. Study the numbers, behaviour and structure; never copy
  code, models, textures, sounds, music or text. Numbers in the appendix are starting points for our own
  tuning.

---

## Appendix: FS2 reference numbers (starting points)

### Flight, by class (from `ships.tbl`)

"Turn" is seconds for a full 360° (pitch, yaw, roll). Turn rate in rad/s = 2π / seconds. "Rot damp" is
the turn time constant. "Accel"/"Decel" are forward speed time constants in seconds. Side-slip damping
("Damp") for fighters and bombers is 0.1 to 0.25 s. Fighters and bombers have no slide or reverse.

| Class | Speed m/s | Afterburner m/s | Turn s (p, y, r) | Rot damp s | Accel / Decel s | Shield | Hull |
|---|---|---|---|---|---|---|---|
| Light/fast fighter | 75-90 | 135-170 | 2.0-3.9 | 0.3-0.4 | 2-3 / 1.5 | 200-500 | 100-250 |
| Medium fighter | 65-80 | 120-150 | 2.6-5.0 | 0.35-0.4 | 2-3.6 / 1.0-2.0 | 380-700 | 180-475 |
| Heavy fighter | 50-55 | 120-130 | 3.8-5.0 | 0.35-0.4 | 2.4-3.6 / 1.3-1.5 | 580-650 | 250-425 |
| Bomber | 45-65 | 85-110 | 3.0-6.0 | 0.5-0.55 | 1-5 / 0.5-2 | 450-1600 | 200-600 |
| Transport/freighter | 30-55 | none | 4-25 | 0.6-0.7 | 2-8 / 1-4 | 0 | 2,000-13,500 |
| Corvette | 30-35 | none | 100 (roll 70-80) | 1-2 | 8 / 4 | 0 | 80,000-180,000 |
| Cruiser | 10-35 | none | 40-130 | 1-2 | 8-18 / 4-7 | 0 | 10,000-75,000 |
| Destroyer | 15-20 | none | 150-200 | 1.5 | 20 / 10 | 0 | 100,000-400,000 (most 100,000-160,000) |
| Super-capital | 15-25 | none | 200 | 1.5 | 20 / 10 | 0 | 800,000-1,000,000 |

Afterburner: ramp time constant about 0.7 s; fuel 300 to 400, burn 50 to 60 per second, recharge 19 to
25 per second (about 6 s of burn, about 12 to 16 s to refill).

Model details: turn rate and side velocity chase their goals with the exponential approach above. When
yawing, roll goal += -(yaw input × max yaw rate) / 2. After big hits or shockwaves, damping is
stretched by up to 10× for 2 s, fading out (rotation damping by up to 5× during shockwave shake).

### Weapons (from `weapons.tbl`)

| Kind | Speed m/s | Life s | Fire every s | Damage | Notes |
|---|---|---|---|---|---|
| Fighter guns | 450-750 (one 1,000; one 1,800) | 1.5-3 | 0.15-0.45 | 7-45 | energy 0.2-3 per shot; separate shield / hull / subsystem multipliers (0-1.5) |
| Heat-seeking missile | 150-190 | 10-12 | 0.5-1.5 | 30-45 | fire and forget, easier to decoy; one anti-subsystem variant (220 m/s, 775 damage) |
| Lock-on missile | 180-280 | 5-18 | 2-6 | 18-350 | needs a lock; swarm, corkscrew and long-range variants |
| Anti-capital torpedo/bomb | 50-95 | 25-30 | 20-30 | 400-6,800 | huge vs hulls, about 2% vs shields; can be shot down |
| Flak | 700-750 | 1.5-4.2 | 0.15-1.5 | 20-50 | bursts near the target |
| Capital turret laser | 80-350 | 5-20 | 0.7-5 | 20-500 | slow bolts, dodgeable |
| Anti-fighter beam | instant | short | 2.2-5.5 | 5-30 | frequent |
| Heavy beam | instant | sustained | 7-45 | 150-2,100 | visible charge-up, ship killers |

Beam damage values apply continuously while the beam touches, so compare beams with beams only.

### AI skill (from `ai.tbl`)

Per AI class and per difficulty level (five levels): accuracy (for example 0.4 to 0.6 for a junior
pilot), evasion, courage and patience (0 to 40 for a junior pilot, higher for veterans).

### Mission structure (from the FS2 campaign, 41 missions)

Typical mission: 30 to 70 events (up to about 105), 15 to 45 radio messages, 30 to 85 ships, 7 to 15
wings, 1 to 5 goals, 3 to 4 briefing stages. Example, "Slaying Ravana": most enemy wings arrive when the
previous wing is 66% destroyed; allied capital ships arrive in a relay as earlier ones die (6, 18 and
10 s after a death); extra wings arrive when the boss's hull drops below 70% and below 40%; radio lines
fire on "ally lost", "target critical" and similar events. Almost nothing runs on a plain timer.

### Music states (from `music.tbl`)

Ambience; allied arrival (normal and battle); enemy arrival (normal and battle); battle 1, 2, 3; victory
(goal completed), victory (all done); goal failed; player died.

### TIE Fighter touches

Missions that start quiet and turn (the first mission is cargo inspection until hidden Rebels appear);
question-and-answer briefings ("What are the mission objectives?", "What opposition will there be?");
hidden bonus objectives from a secret society that run a second story thread.
