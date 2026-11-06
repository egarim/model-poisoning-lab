/* Species catalog + summoning: turns who the user is into three daemon candidates.
   Inputs:  { name, country, birthDate "YYYY-MM-DD", energy, social, mind, spirit }  (traits 0..1)
            energy: calm 0 .. energetic 1     social: introvert 0 .. extrovert 1
            mind:   heart 0 .. head 1         spirit: traditional 0 .. rebel 1
   Output:  three { spec, archetype, title, reasons[] }  — spec is ready for DaemonRender.render.
   Pure functions, seeded by name + birth date, so the same person always gets the same three. */
(function (root) {
  "use strict";

  // ---------------- species templates ----------------
  const CAT_EYES = { shape: "almond", size: 0.18, spacing: 0.35, y: -0.1, pupil: "slit" };
  const DOG_EYES = { shape: "round", size: 0.15, spacing: 0.34, y: -0.12, iris: [25, 45, 28] };
  const SPECIES = {
    red_panda: {
      name: "Red panda", local: { es: "panda rojo", pt: "panda-vermelho", ru: "малая панда" },
      body: { w: 1.0, squash: 1.0, cheeks: 0.07 }, eyes: { shape: "round", size: 0.14, spacing: 0.34, y: -0.1 },
      parts: [
        { type: "tail", style: "bushy", length: 1.5, side: 1, layer: "back", tip: "dark", rings: "dark", minStage: 1 },
        { type: "ear", style: "round", angle: 0.74, size: 0.26, outer: "light", inner: "dark", layer: "back", minStage: 1 },
        { type: "nose", style: "round", layer: "front", minStage: 1 },
        { type: "paws", color: "dark", layer: "front", minStage: 1 }],
      markings: [{ style: "mask" }],
      variants: { classic: { base: [16, 75, 46], light: [35, 40, 94], dark: [12, 55, 20], nose: [0, 0, 12] } }
    },
    panda: {
      name: "Panda", local: { es: "panda", pt: "panda", ru: "панда" },
      body: { w: 1.02, squash: 1.0, cheeks: 0.08, bottom: 0.08 }, eyes: { shape: "round", size: 0.13, spacing: 0.35, y: -0.08 },
      parts: [
        { type: "ear", style: "round", angle: 0.68, size: 0.26, outer: "dark", inner: "dark", layer: "back", minStage: 1 },
        { type: "nose", style: "round", layer: "front", minStage: 1 },
        { type: "paws", color: "dark", layer: "front", minStage: 1 }],
      markings: [{ style: "eyepatch" }],
      variants: { classic: { base: [40, 12, 95], light: [40, 10, 100], dark: [240, 10, 14], nose: [240, 10, 10] } }
    },
    cat: {
      name: "Cat", local: { es: "gato", pt: "gato", ru: "кот" },
      body: { w: 0.98, squash: 1.0, cheeks: 0.1 }, eyes: CAT_EYES,
      parts: [
        { type: "tail", style: "thin", length: 1.2, side: 1, layer: "back", minStage: 1 },
        { type: "ear", style: "pointed", angle: 0.55, size: 1.48, width: 0.3, droop: 0.6, inner: "accent2", layer: "back", minStage: 1 },
        { type: "nose", style: "cat", layer: "front", minStage: 1 },
        { type: "whiskers", layer: "front", minStage: 1 },
        { type: "paws", layer: "front", beans: true, minStage: 1 }],
      variants: {
        orange_tabby: { name: "orange tabby", base: [28, 80, 56], dark: [22, 70, 36], light: [35, 60, 88], accent2: [350, 60, 80], nose: [350, 50, 62], iris: [95, 55, 50], markings: [{ style: "tabby" }] },
        grey_tabby:   { name: "grey tabby", base: [220, 6, 58], dark: [220, 8, 30], light: [220, 8, 90], accent2: [350, 50, 80], nose: [350, 40, 60], iris: [45, 85, 55], markings: [{ style: "tabby" }] },
        black:        { name: "black", base: [255, 12, 16], light: [255, 10, 40], dark: [255, 10, 8], accent2: [350, 30, 45], nose: [0, 0, 8], iris: [50, 90, 55] },
        white:        { name: "white", base: [40, 12, 95], light: [0, 0, 100], dark: [40, 10, 72], accent2: [350, 60, 82], nose: [350, 55, 72], iris: [200, 70, 60] },
        calico:       { name: "calico", base: [40, 20, 95], accent: [28, 80, 55], dark: [250, 10, 15], accent2: [350, 60, 82], nose: [350, 50, 70], iris: [40, 85, 50], markings: [{ style: "calico" }] },
        siamese:      { name: "siamese", base: [38, 35, 88], dark: [20, 30, 24], light: [40, 30, 96], accent2: [20, 30, 40], nose: [20, 30, 20], iris: [210, 80, 55], markings: [{ style: "points" }] },
        russian_blue: { name: "Russian blue", base: [210, 14, 55], dark: [210, 14, 35], light: [210, 10, 75], accent2: [350, 30, 70], nose: [210, 15, 30], iris: [110, 70, 45] }
      }
    },
    dog: {
      name: "Dog", local: { es: "perro", pt: "cachorro", ru: "собака" },
      body: { w: 1.0, squash: 1.02, cheeks: 0.08 }, eyes: DOG_EYES,
      parts: [
        { type: "tail", style: "thin", length: 1.1, side: 1, layer: "back", minStage: 1 },
        { type: "ear", style: "floppy", size: 0.85, layer: "front", minStage: 1 },
        { type: "nose", style: "big", muzzle: "light", tongue: true, layer: "front", minStage: 1 },
        { type: "paws", layer: "front", minStage: 1 }],
      variants: {
        xolo:     { name: "xoloitzcuintle", base: [215, 6, 30], light: [215, 5, 45], dark: [215, 8, 18], accent2: [350, 25, 50], nose: [215, 8, 10],
                    swap: { ear: { layer: "back", style: "pointed", angle: 0.6, size: 1.75, width: 0.32, droop: 0.5, inner: "accent2" }, nose: { muzzle: null } } },
        caramelo: { name: "vira-lata caramelo", base: [30, 65, 52], light: [35, 50, 85], dark: [25, 55, 30], nose: [20, 30, 15], swap: { ear: { color: "dark" } } },
        shiba:    { name: "shiba", base: [26, 75, 55], light: [35, 40, 94], dark: [22, 60, 30], nose: [0, 0, 10],
                    swap: { ear: { layer: "back", style: "pointed", angle: 0.6, size: 1.35, width: 0.33, droop: 0.4, inner: "light" }, tail: { style: "curl" } }, markings: [{ style: "belly" }] },
        laika:    { name: "laika", base: [215, 8, 40], light: [210, 8, 94], dark: [215, 10, 22], nose: [215, 10, 8],
                    swap: { ear: { layer: "back", style: "pointed", angle: 0.6, size: 1.45, width: 0.3, droop: 0.4, inner: "light" }, tail: { style: "curl" } }, markings: [{ style: "belly" }] },
        golden:   { name: "golden", base: [40, 62, 62], light: [42, 50, 86], dark: [32, 50, 40], accent: [36, 60, 50], nose: [20, 20, 12], swap: { ear: { color: "accent" } } },
        black:    { name: "black", base: [250, 8, 16], light: [250, 6, 40], dark: [250, 8, 8], nose: [0, 0, 5] }
      }
    },
    spitz: {
      name: "Spitz", local: { es: "spitz", pt: "spitz", ru: "шпиц" },
      body: { w: 1.0, squash: 1.0, cheeks: 0.1, fluff: 0.06 }, eyes: { shape: "round", size: 0.14, spacing: 0.32, y: -0.12, iris: [25, 45, 22] },
      parts: [
        { type: "mane", soft: true, count: 11, length: 0.32, color: "light", layer: "back", minStage: 1 },
        { type: "tail", style: "curl", fluffy: true, length: 1.2, side: 1, layer: "back", minStage: 1 },
        { type: "ear", style: "pointed", angle: 0.58, size: 1.3, width: 0.26, droop: 0.4, inner: "light", layer: "back", minStage: 1 },
        { type: "nose", style: "round", muzzle: "light", tongue: true, layer: "front", minStage: 1 },
        { type: "paws", layer: "front", minStage: 1 }],
      variants: {
        orange: { name: "orange pomeranian", base: [30, 80, 58], light: [38, 60, 88], dark: [25, 65, 38], nose: [0, 0, 10] },
        white:  { name: "white", base: [40, 10, 96], light: [0, 0, 100], dark: [40, 8, 75], nose: [0, 0, 10] },
        cream:  { name: "cream", base: [38, 45, 82], light: [40, 40, 94], dark: [32, 40, 60], nose: [20, 20, 15] },
        black:  { name: "black", base: [250, 8, 14], light: [250, 6, 40], dark: [250, 8, 6], nose: [0, 0, 5] },
        wolf:   { name: "wolf-grey keeshond", base: [215, 6, 58], light: [215, 5, 88], dark: [215, 8, 25], nose: [0, 0, 8], markings: [{ style: "points" }] }
      }
    },
    wisp: {
      name: "Wisp", local: { es: "fuego fatuo", pt: "fogo-fátuo", ru: "блуждающий огонёк" }, spiritForm: true,
      body: { w: 0.88, squash: 1.1, top: 0.45 }, eyes: { shape: "round", size: 0.15, spacing: 0.3, y: -0.05 },
      parts: [{ type: "crest", style: "flame", count: 3, height: 1.3, layer: "back", minStage: 1 }],
      markings: [], variants: { spirit: { name: "flame", spirit: true } }
    },
    cloud: {
      name: "Cloud spirit", local: { es: "espíritu de nube", pt: "espírito da nuvem", ru: "облачный дух" },
      body: { w: 1.1, squash: 0.92, bumps: 0.09 }, eyes: { shape: "almond", size: 0.14, spacing: 0.34, y: -0.05, lid: 0.2 }, tempo: 0.8,
      parts: [], markings: [{ style: "blush" }], variants: { pale: { name: "pale", elementCoat: [25, 90] } }
    },
    star: {
      name: "Star spirit", local: { es: "espíritu estelar", pt: "espírito estelar", ru: "звёздный дух" }, spiritForm: true,
      body: { w: 0.95, squash: 1.05 }, eyes: { shape: "round", size: 0.16, spacing: 0.33, y: -0.1 },
      parts: [{ type: "antennae", length: 0.45, layer: "back", minStage: 1 }],
      markings: [{ style: "stars" }], variants: { night: { name: "night", elementCoat: [50, 40] } }
    },
    cadejo: {
      // The white Cadejo of Salvadoran and Central American folklore: a spirit dog that walks night travellers home.
      name: "Cadejo", local: { es: "cadejo", pt: "cadejo", ru: "кадехо" }, // solid body: its spirit shows in the aura, not by being see-through
      body: { w: 1.0, squash: 1.02, cheeks: 0.06, fluff: 0.03 }, eyes: { shape: "almond", size: 0.17, spacing: 0.34, y: -0.12, iris: [35, 95, 55] },
      parts: [
        { type: "tail", style: "thin", length: 1.35, side: 1, layer: "back", minStage: 1 },
        { type: "ear", style: "pointed", angle: 0.56, size: 1.5, width: 0.28, droop: 0.6, inner: "accent2", layer: "back", minStage: 1 },
        { type: "nose", style: "round", muzzle: "light", layer: "front", minStage: 1 },
        { type: "paws", layer: "front", minStage: 1 }],
      variants: { white: { name: "white", base: [230, 12, 95], light: [0, 0, 100], dark: [230, 10, 70], accent2: [340, 40, 82], nose: [240, 15, 30] } }
    },
    owl: {
      name: "Owl", local: { es: "búho", pt: "coruja", ru: "сова" },
      body: { w: 0.95, squash: 1.08 }, eyes: { shape: "round", size: 0.2, spacing: 0.33, y: -0.12, iris: [42, 95, 55] },
      parts: [
        { type: "wing", style: "feather", span: 1.0, feathers: 5, layer: "back" },
        { type: "ear", style: "pointed", angle: 0.5, size: 1.35, width: 0.18, droop: 0.4, layer: "back", minStage: 1 },
        { type: "beak", style: "hooked", length: 0.32, color: [40, 15, 40], layer: "front", minStage: 1 }],
      markings: [{ style: "disc" }, { style: "belly" }],
      variants: {
        brown: { name: "brown", base: [30, 35, 42], light: [35, 30, 80], dark: [25, 35, 22] },
        snowy: { name: "snowy", base: [40, 10, 94], light: [0, 0, 100], dark: [220, 10, 30], extraMarkings: [{ style: "spots" }] }
      }
    },
    macaw: {
      name: "Macaw", local: { es: "guacamaya", pt: "arara", ru: "ара" },
      body: { w: 0.95, squash: 1.05 }, eyes: { shape: "round", size: 0.13, spacing: 0.32, y: -0.15 },
      parts: [
        { type: "tail", style: "plume", length: 1.4, count: 5, side: 0, layer: "back", tip: "accent2" },
        { type: "wing", style: "feather", span: 1.3, feathers: 7, colors: ["body", "accent", "accent2", "accent2"], layer: "back" },
        { type: "beak", style: "hooked", length: 0.45, color: [40, 10, 90], layer: "front", minStage: 1 }],
      markings: [],
      variants: {
        guacamaya: { name: "scarlet", base: [355, 82, 48], accent: [48, 95, 55], accent2: [205, 85, 45] },
        arara_azul: { name: "hyacinth", base: [235, 60, 40], light: [235, 40, 65], accent: [235, 60, 30], accent2: [48, 95, 55], beak: [0, 0, 15] }
      }
    },
    toucan: {
      name: "Toucan", local: { es: "tucán", pt: "tucano", ru: "тукан" },
      body: { w: 0.95, squash: 1.06 }, eyes: { shape: "round", size: 0.12, spacing: 0.32, y: -0.18, iris: [200, 60, 55] },
      parts: [
        { type: "wing", style: "feather", span: 1.1, feathers: 5, dark: true, layer: "back" },
        { type: "beak", length: 0.6, color: "accent", layer: "front", minStage: 1 }],
      markings: [{ style: "breast", color: "light" }],
      variants: { classic: { name: "toco", base: [250, 10, 14], light: [48, 90, 82], dark: [250, 10, 8], accent: [28, 95, 55] } }
    },
    bullfinch: {
      name: "Bullfinch", local: { es: "camachuelo", pt: "dom-fafe", ru: "снегирь" },
      body: { w: 1.0, squash: 1.0, bottom: 0.06 }, eyes: { shape: "round", size: 0.13, spacing: 0.33, y: -0.15 },
      parts: [
        { type: "wing", style: "feather", span: 1.05, feathers: 5, colors: ["dark", "dark", "body"], layer: "back" },
        { type: "beak", style: "stubby", length: 0.2, color: "dark", layer: "front", minStage: 1 }],
      markings: [{ style: "breast", color: "accent" }, { style: "cap" }],
      variants: { classic: { name: "winter", base: [215, 10, 42], light: [0, 0, 95], dark: [240, 10, 10], accent: [5, 72, 58] } }
    },
    octopus: {
      name: "Octopus", local: { es: "pulpo", pt: "polvo", ru: "осьминог" },
      body: { w: 1.0, squash: 1.1, top: 0.08 }, eyes: { shape: "round", size: 0.17, spacing: 0.33, y: -0.05 },
      parts: [{ type: "tentacles", count: 6, length: 1.0, layer: "back", minStage: 1 }],
      markings: [{ style: "spots", color: "light" }],
      variants: { coral: { name: "coral", base: [350, 60, 58] }, violet: { name: "violet", base: [275, 40, 55] } }
    },
    jellyfish: {
      name: "Jellyfish", local: { es: "medusa", pt: "água-viva", ru: "медуза" },
      body: { w: 1.05, squash: 0.95, bell: true, translucent: true }, eyes: { shape: "round", size: 0.12, spacing: 0.3, y: -0.15 },
      parts: [{ type: "tendrils", count: 8, length: 1.6, layer: "back", minStage: 1 }],
      markings: [], variants: { spirit: { name: "moon", spirit: true } }
    },
    seal: {
      name: "Seal", local: { es: "foca", pt: "foca", ru: "нерпа (Baikal seal)" },
      body: { w: 1.05, squash: 0.98, bottom: 0.15 }, eyes: { shape: "round", size: 0.2, spacing: 0.33, y: -0.12, iris: [220, 20, 15] },
      parts: [
        { type: "flippers", span: 0.7, layer: "back", minStage: 1 },
        { type: "nose", style: "round", layer: "front", minStage: 1 },
        { type: "whiskers", color: "dark", layer: "front", minStage: 1 }],
      markings: [{ style: "spots" }],
      variants: { classic: { name: "silver", base: [215, 10, 58], light: [215, 8, 82], dark: [215, 12, 32], nose: [215, 15, 15] } }
    },
    capybara: {
      name: "Capybara", local: { es: "carpincho", pt: "capivara", ru: "капибара" },
      body: { w: 1.06, squash: 0.95, top: -0.04, bottom: 0.08 }, eyes: { shape: "almond", size: 0.12, spacing: 0.36, y: -0.15, lid: 0.35 }, tempo: 0.7,
      parts: [
        { type: "ear", style: "round", angle: 0.78, size: 0.13, outer: "dark", layer: "back", minStage: 1 },
        { type: "nose", style: "big", muzzle: "dark", layer: "front", minStage: 1 },
        { type: "paws", layer: "front", minStage: 1 }],
      variants: { classic: { name: "river", base: [28, 40, 45], light: [30, 30, 62], dark: [25, 40, 28], nose: [25, 30, 20] } }
    }
  };
  // Archetype creatures (specs/*.json) that can also be summoned, keyed by spec id.
  const FOLK = {
    "keeper-latam": "Jaguar", "healer-latam": "Axolotl", "seer-latam": "Monarch butterfly", "flame-latam": "Fire hummingbird",
    "wanderer-latam": "Sea turtle", "shadow-latam": "Bat", "wild-latam": "Wolf", "trickster-latam": "Coyote",
    "trickster-slavic": "Fox", "shadow-slavic": "Raven", "keeper-slavic": "Domovoi", "wild-slavic": "Leshy",
    "flame-slavic": "Firebird", "seer-slavic": "Gamayun", "wanderer-slavic": "Crane", "healer-slavic": "Bereginya"
  };

  // ---------------- birthplaces ----------------
  const COUNTRIES = {
    MX: { name: "Mexico", lang: "es", pool: ["dog:xolo", "folk:healer-latam", "folk:seer-latam", "folk:keeper-latam", "macaw:guacamaya", "folk:flame-latam", "octopus:*"], charm: { shape: "flower", hue: 30, label: "cempasúchil marigold" } },
    BR: { name: "Brazil", lang: "pt", pool: ["dog:caramelo", "macaw:arara_azul", "toucan:classic", "capybara:classic", "folk:keeper-latam", "cat:*"], charm: { shape: "flower", hue: 50, label: "yellow ipê blossom" } },
    AR: { name: "Argentina", lang: "es", pool: ["capybara:classic", "owl:brown", "dog:caramelo", "cat:*", "folk:wild-latam"], charm: { shape: "flower", hue: 355, sat: 85, lum: 50, label: "ceibo flower" } },
    CO: { name: "Colombia", lang: "es", pool: ["macaw:guacamaya", "folk:flame-latam", "toucan:classic", "octopus:*", "folk:seer-latam"], charm: { shape: "flower", hue: 285, sat: 60, lum: 65, label: "orchid" } },
    PE: { name: "Peru", lang: "es", pool: ["owl:brown", "folk:flame-latam", "macaw:guacamaya", "capybara:classic", "folk:keeper-latam"], charm: { shape: "flower", hue: 345, label: "kantuta flower" } },
    CL: { name: "Chile", lang: "es", pool: ["seal:classic", "owl:brown", "jellyfish:spirit", "octopus:*", "folk:wanderer-latam"], charm: { shape: "flower", hue: 350, sat: 80, lum: 48, label: "copihue flower" } },
    RU: { name: "Russia", lang: "ru", pool: ["dog:laika", "spitz:*", "cat:russian_blue", "owl:snowy", "bullfinch:classic", "seal:classic", "folk:trickster-slavic", "folk:shadow-slavic", "folk:keeper-slavic", "folk:wild-slavic", "folk:flame-slavic"], charm: { shape: "berries", hue: 8, label: "rowan berries" } },
    SV: { name: "El Salvador", lang: "es", pool: ["cadejo:white", "folk:flame-latam", "macaw:guacamaya", "owl:brown", "dog:caramelo", "cat:*"], charm: { shape: "flower", hue: 50, sat: 15, lum: 95, label: "flor de izote" } },
    XX: { name: "Somewhere else", lang: "en", pool: ["red_panda:classic", "panda:classic", "cat:*", "dog:golden", "spitz:*", "owl:brown", "octopus:*", "jellyfish:spirit", "wisp:spirit", "star:night", "cloud:pale"], charm: { shape: "star", hue: 48, label: "star" } }
  };
  const FAVOURITES = ["red_panda:classic", "panda:classic", "cat:*", "dog:*", "spitz:*", "octopus:*", "jellyfish:spirit", "wisp:spirit"];
  const BY_TEMPERAMENT = { // a wild card that suits the personality
    calm: ["panda:classic", "seal:classic", "capybara:classic", "jellyfish:spirit", "cloud:pale"],
    lively: ["dog:*", "spitz:*", "macaw:*", "bullfinch:classic", "folk:flame-latam", "wisp:spirit"],
    head: ["owl:*", "octopus:*", "folk:shadow-slavic", "star:night"],
    heart: ["red_panda:classic", "folk:healer-latam", "cat:*", "cloud:pale"]
  };

  // ---------------- archetypes (the daemon's voice) ----------------
  // nearest target in (energy, social, mind, spirit)
  const ARCHETYPES = {
    keeper: [0.2, 0.4, 0.3, 0.1], trickster: [0.8, 0.8, 0.5, 0.9], seer: [0.3, 0.3, 0.2, 0.5], wild: [0.8, 0.2, 0.4, 0.8],
    healer: [0.3, 0.7, 0.1, 0.3], shadow: [0.4, 0.2, 0.8, 0.7], wanderer: [0.7, 0.5, 0.7, 0.6], flame: [0.9, 0.8, 0.4, 0.3]
  };
  const ARCH_NAMES = { keeper: "Guardian", trickster: "Trickster", seer: "Seer", wild: "Wild One", healer: "Healer", shadow: "Shadow", wanderer: "Wanderer", flame: "Flame" };

  const ELEMENT_HS = { fire: [22, 88], water: [192, 70], earth: [104, 46], air: [268, 55], moon: [222, 26] };
  // ---------------- helpers ----------------
  function hash(s) { let h = 2166136261; for (const c of s) { h ^= c.codePointAt(0); h = Math.imul(h, 16777619); } return h >>> 0; }
  function rng(seed) { let a = seed; return () => { a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; }; }
  const clone = o => JSON.parse(JSON.stringify(o));
  const pick = (r, arr) => arr[Math.floor(r() * arr.length)];

  function zodiac(dateStr) {
    const d = new Date(dateStr + "T12:00:00Z"); if (isNaN(d)) return { sign: "Unknown", element: "water" };
    const md = (d.getUTCMonth() + 1) * 100 + d.getUTCDate();
    const signs = [[120, "Capricorn", "earth"], [219, "Aquarius", "air"], [321, "Pisces", "water"], [420, "Aries", "fire"], [521, "Taurus", "earth"], [621, "Gemini", "air"],
                   [723, "Cancer", "water"], [823, "Leo", "fire"], [923, "Virgo", "earth"], [1023, "Libra", "air"], [1122, "Scorpio", "water"], [1222, "Sagittarius", "fire"], [1232, "Capricorn", "earth"]];
    for (const [end, sign, element] of signs) if (md < end) return { sign, element };
    return { sign: "Capricorn", element: "earth" };
  }

  function archetypeFor(t) {
    let best = null, bestD = 1e9;
    for (const [id, v] of Object.entries(ARCHETYPES)) {
      const d = (t.energy - v[0]) ** 2 + (t.social - v[1]) ** 2 + (t.mind - v[2]) ** 2 + (t.spirit - v[3]) ** 2;
      if (d < bestD) { bestD = d; best = id; }
    }
    return best;
  }

  // ---------------- build one spec ----------------
  let spec_rebelCoat = false;
  function build(key, profile, r, folkSpecs) {
    spec_rebelCoat = false;
    const [kind, variantKey] = key.split(":");
    const t = profile, z = zodiac(profile.birthDate), country = COUNTRIES[profile.country] || COUNTRIES.XX;
    const reasons = [];
    let spec, title;
    if (kind === "folk") {
      spec = clone(folkSpecs[variantKey]); title = FOLK[variantKey];
      // keep the folk spirit's own colours as its coat; the zodiac element only tints aura and sigil
      const e = ELEMENT_HS[spec.element] || ELEMENT_HS.water, h = e[0] + (spec.hueShift || 0);
      spec.coat = { base: [h, e[1], 52], light: [h, e[1] * 0.7, 78], dark: [h, e[1], 22], accent: [h, Math.max(45, e[1]), 58], accent2: [h + 40, Math.max(45, e[1]), 58], nose: [h, e[1] * 0.6, 18] };
      // its name in the person's language, and the folklore it really comes from (a Slavic raven offered in Argentina is
      // still a Slavic raven, named "cuervo")
      if (spec.local) spec.creature = spec.local[country.lang] || spec.local.en || spec.creature;
      reasons.push(`${title}, a spirit from ${spec.culture ? spec.culture + " folklore" : "the folklore of " + country.name}`);
    } else {
      const sp = SPECIES[kind];
      const vKeys = Object.keys(sp.variants);
      let vk = variantKey === "*" || !variantKey ? pick(r, vKeys) : variantKey;
      if (variantKey === "*" && t.spirit > 0.7 && kind === "cat") { vk = pick(r, ["black", "siamese", "calico"]); spec_rebelCoat = true; }
      const v = sp.variants[vk];
      spec = { id: `${kind}-${vk}`, archetype: null, form: "species", creature: sp.local[country.lang] || sp.name,
               element: z.element, hueShift: 0, body: clone(sp.body), eyes: clone(sp.eyes), parts: clone(sp.parts),
               markings: clone(v.markings || sp.markings || []), tempo: sp.tempo || 1 };
      if (v.extraMarkings) spec.markings.push(...clone(v.extraMarkings));
      if (v.elementCoat) { const e = ELEMENT_HS[z.element]; spec.coat = { base: [e[0], v.elementCoat[0], v.elementCoat[1]], light: [e[0], v.elementCoat[0], Math.min(98, v.elementCoat[1] + 25)], dark: [e[0], v.elementCoat[0] + 10, Math.max(10, v.elementCoat[1] - 25)] }; }
      else if (!v.spirit) spec.coat = { base: v.base, light: v.light, dark: v.dark, accent: v.accent, accent2: v.accent2, nose: v.nose };
      if (sp.spiritForm) spec.spiritForm = true;
      if (v.iris) spec.eyes.iris = v.iris;
      if (v.beak) spec.parts.forEach(p => { if (p.type === "beak") p.color = v.beak; });
      for (const [type, changes] of Object.entries(v.swap || {})) spec.parts.forEach(p => { if (p.type === type) Object.assign(p, changes); });
      title = `${sp.name}${v.name && vKeys.length > 1 ? ` (${v.name})` : ""}`;
      const local = country.pool.some(k => k.startsWith(kind + ":")) && country.name !== "Somewhere else";
      reasons.push(local ? `${title}: a creature of ${country.name}` : `${title}: answered your call`);
    }
    // ---- personality shapes the body ----
    spec.element = z.element;
    reasons.push(`${z.sign}, ${z.element === "earth" || z.element === "air" ? "an" : "a"} ${z.element} sign: ${z.element} aura and sigil`);
    spec.tempo = (spec.tempo || 1) * (0.7 + 0.6 * t.energy);
    spec.body = Object.assign({ w: 0.96, squash: 1.07 }, spec.body);
    spec.body.w *= 1.04 - 0.1 * t.energy; spec.body.squash *= 0.97 + 0.08 * t.energy;
    spec.body.cheeks = (spec.body.cheeks || 0) + 0.06 * (1 - t.energy);
    reasons.push(t.energy < 0.4 ? "calm: rounder, slow breathing" : t.energy > 0.6 ? "energetic: leaner, quick movements" : "balanced energy: steady rhythm");
    spec.eyes = Object.assign({}, spec.eyes, { size: spec.eyes.size * (0.9 + 0.25 * t.social), lid: Math.max(spec.eyes.lid || 0, (1 - t.social) * 0.2) });
    reasons.push(t.social > 0.6 ? "extrovert: big open eyes" : t.social < 0.4 ? "introvert: soft half-lidded gaze" : "ambivert: attentive eyes");
    if (t.mind < 0.4) { spec.markings.push({ style: "blush" }); reasons.push("led by the heart: warm blush"); }
    if (t.mind > 0.6) reasons.push("led by the head: a sharper, many-pointed sigil");
    if (t.spirit > 0.65) {
      const ear = spec.parts.find(p => p.type === "ear" && p.style === "pointed");
      if (ear) { ear.bent = r() < 0.5 ? -1 : 1; reasons.push("a rebel streak: one ear bent its own way"); }
      else if (spec_rebelCoat) reasons.push("a rebel streak: an unusual coat");
    }
    // ---- birthplace charm ----
    spec.parts.push(Object.assign({ type: "charm", layer: "front", minStage: 1 }, country.charm));
    reasons.push(profile.country === "XX" || !COUNTRIES[profile.country] ? "talisman: a small star" : `talisman: ${country.charm.label} from ${country.name}`);
    spec.sigilOpts = { points: Math.round(5 + 4 * t.mind) };
    return { spec, title, reasons };
  }

  function summon(profile, folkSpecs) {
    const p = Object.assign({ name: "Daemon", country: "XX", birthDate: "2000-01-01", energy: 0.5, social: 0.5, mind: 0.5, spirit: 0.5 }, profile);
    const r = rng(hash(`${p.name.trim().toLowerCase()}|${p.birthDate}|${p.country}`));
    const country = COUNTRIES[p.country] || COUNTRIES.XX;
    const temper = p.energy < 0.4 ? "calm" : p.energy > 0.65 ? "lively" : p.mind > 0.55 ? "head" : "heart";
    const chosen = [];
    const weighted = (opts) => { const w = opts.map((_, i) => 1 / (1 + 0.6 * i)), tot = w.reduce((a, b) => a + b, 0); let x = r() * tot; for (let i = 0; i < opts.length; i++) { x -= w[i]; if (x <= 0) return opts[i]; } return opts[opts.length - 1]; };
    const take = (list, weigh) => { const opts = list.filter(k => !chosen.some(c => c.split(":")[0] === k.split(":")[0] && (k.split(":")[0] !== "folk" || c === k))); if (opts.length) chosen.push(weigh ? weighted(opts) : pick(r, opts)); };
    take(country.pool, true); take(FAVOURITES); take(BY_TEMPERAMENT[temper]);
    while (chosen.length < 3) take(FAVOURITES.concat(COUNTRIES.XX.pool));
    const archetype = archetypeFor(p);
    return chosen.map(key => {
      const c = build(key, p, r, folkSpecs);
      c.spec.archetype = archetype;
      c.archetype = archetype; c.archetypeName = ARCH_NAMES[archetype];
      return c;
    });
  }

  root.DaemonSpecies = { summon, zodiac, archetypeFor, SPECIES, COUNTRIES, ARCH_NAMES, build };
})(typeof window !== "undefined" ? window : globalThis);
