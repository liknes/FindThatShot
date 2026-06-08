// Find That Shot landing page — en / nb-NO strings.
// Keys match data-i18n attributes in index.html.
(function () {
  const STRINGS = {
    en: {
      "meta.title": "Find That Shot — Catalog, search & review your local video archive",
      "meta.description":
        "A local-first Windows app that catalogs, tags, and helps you instantly find any clip across all your drives — without ever moving, renaming, or modifying a single source file.",
      "og.description":
        "Catalog, search & review your local video archive. Your source files are never moved, renamed, or modified.",

      "nav.features": "Features",
      "nav.drone": "Drone",
      "nav.deepdive": "Deep dive",
      "nav.how": "How it works",
      "nav.download": "Download",
      "nav.github": "GitHub",

      "lang.en": "English",
      "lang.nb": "Norsk",

      "hero.pill": "Local-first · Windows 10/11 · Open source",
      "hero.title": 'Find <span class="accent">that</span> shot.<br />Across every drive.',
      "hero.lead":
        'A desktop app that catalogs, tags, and instantly searches your entire local video archive — internal disks, externals, offline drives — <b style="color:#fff">without ever moving, renaming, or modifying a single source file.</b>',
      "hero.download": "Download for Windows",
      "hero.seeFeatures": "See what it does",
      "hero.meta.files": "<b>0</b> files touched on disk",
      "hero.meta.local": "<b>100%</b> local & offline",
      "hero.meta.license": "<b>GPLv3</b> open source",
      "hero.shotAlt":
        "Find That Shot main window — a grid of drone clip thumbnails with a folders sidebar and a location map in the detail panel.",

      "promise.text":
        "<b>Your footage is never modified.</b> Source video files are never moved, renamed, deleted, or touched — the app only writes its own catalog, thumbnails, and (optionally) sidecar files.",

      "features.eyebrow": "Built for big archives",
      "features.title": "Everything you need to find a single clip in thousands",
      "features.subtitle":
        "Catalog once, then slice your library by text, tags, rating, status, camera, date, location, and more — even for clips on drives that are currently unplugged.",

      "features.search.title": "Instant search & smart collections",
      "features.search.text":
        "AND-matched tokens across filename, path, location, notes, camera & tags. Save any filter combo as a live, self-updating collection.",
      "features.moments.title": "Moments — mark the exact shot",
      "features.moments.text":
        "A shot is rarely a whole file. Drop timestamped in/out markers with their own label, rating, notes & tags, and jump straight back to them.",
      "features.player.title": "Built-in player & Review mode",
      "features.player.text":
        "Preview clips in-app with an FFmpeg-powered player (H.264/265, ProRes, DNxHD…) while you tag, rate and take notes side-by-side.",
      "features.map.title": "Browse on a map",
      "features.map.text":
        'Every geotagged clip plotted on a clustered offline map. Click a region to scope your whole grid to "where did I shoot that?"',
      "features.calendar.title": "Browse by date",
      "features.calendar.text":
        "A year-by-month heatmap bucketed by when footage was actually captured. Click a month to scope the grid to it.",
      "features.dupes.title": "Find duplicates",
      "features.dupes.text":
        "Instantly group copies by metadata fingerprint — even across offline backups — and clear redundant catalog entries (never the files).",
      "features.ai.title": "Local AI tagging & NL search",
      "features.ai.text":
        'Opt-in CLIP model runs fully offline on your CPU. Get subject-tag suggestions and search by plain English — <em>"drone shot over snowy mountains at sunset."</em>',
      "features.sidecar.title": "Portable sidecar files",
      "features.sidecar.text":
        "Optionally write a tiny JSON next to each video so your tags, ratings, notes & moments travel with the footage to any machine.",
      "features.stats.title": "Stats & automatic backups",
      "features.stats.text":
        "A read-only dashboard of your whole archive, plus rotating, restorable backups of your catalog so curation work is never lost.",

      "drone.eyebrow": "Made for aerial archives",
      "drone.title": "Drone footage gets superpowers",
      "drone.lead":
        'Drop in your DJI clips and Find That Shot reads the <code style="color:#9be8db;background:rgba(46,230,201,.12);padding:1px 6px;border-radius:5px;font-size:.92em">.SRT</code> flight logs that ship next to them — turning raw footage into a geotagged, flyable, fully-searchable map of where you\'ve been.',
      "drone.p1.title": "Auto-geotagged on scan",
      "drone.p1.text": "The GPS takeoff fix is lifted straight from the flight log — no manual tagging needed.",
      "drone.p2.title": "Full flight path on the map",
      "drone.p2.text": "The whole route is drawn as a polyline with start & end markers and per-point altitude.",
      "drone.p3.title": "Live position & telemetry while you watch",
      "drone.p3.text":
        "A marker rides the path in sync with playback; an overlay shows ISO, shutter, aperture & altitude frame-by-frame.",
      "drone.p4.title": "Smooth 4K / 60p playback",
      "drone.p4.text": "GPU-accelerated decoding plays high-bitrate DJI & GoPro clips at real time, not slow-motion.",
      "drone.shotAlt":
        "Find That Shot review mode — a drone clip playing with a live telemetry strip, the flight path on the sidebar map, and a list of captured moments.",

      "deepdive.eyebrow": "Go deeper",
      "deepdive.title": "The advanced stuff, one tab at a time",
      "deepdive.subtitle": "Power features for serious archives — explore whichever you care about, skip the rest.",
      "deepdive.tabsLabel": "Advanced features",
      "deepdive.tab.ai": "AI search",
      "deepdive.tab.moments": "Moments",
      "deepdive.tab.nav": "Map & time",
      "deepdive.tab.safe": "Safe cleanup",

      "deepdive.ai.title": "AI that actually understands your footage",
      "deepdive.ai.lead":
        "An opt-in CLIP model runs <strong>fully offline on your CPU</strong> — nothing is uploaded. It looks at frames sampled across each clip, so a match counts even if the subject only appears halfway through.",
      "deepdive.ai.l1": 'Search by plain English — <em>"drone shot over snowy mountains at sunset."</em>',
      "deepdive.ai.l2": "Subject-tag suggestions you accept or reject — nothing is auto-applied.",
      "deepdive.ai.l3": "Adaptive thresholds learn from your choices and sharpen over time.",
      "deepdive.ai.demo": "drone shot over snowy mountains at sunset",

      "deepdive.moments.title": "Mark the exact shot, not the whole file",
      "deepdive.moments.lead":
        "A shot is rarely a whole clip. While reviewing, press <strong>I</strong> and <strong>O</strong> to capture a timestamped in/out range with its own thumbnail, label, rating, notes & tags.",
      "deepdive.moments.l1": "Auto-grabbed thumbnail at the in-point — every moment has a frame.",
      "deepdive.moments.l2": "<strong>Jump to</strong> seeks the player straight to that timestamp.",
      "deepdive.moments.l3": 'Search moments across the whole archive — <em>"jump to 00:01:32."</em>',

      "deepdive.nav.title": "Location & time become navigation",
      "deepdive.nav.lead":
        "Stop scrolling endless folders. Ask <em>where</em> and <em>when</em> you shot something and let the archive answer.",
      "deepdive.nav.l1": "Whole-archive map with clustering — click a cluster to scope the grid to it.",
      "deepdive.nav.l2": "Year × month heatmap by when footage was actually captured.",
      "deepdive.nav.l3": "Map & markers work offline — only the tiles need a connection.",

      "deepdive.safe.title": "A catalog that protects your work",
      "deepdive.safe.lead":
        "Curate fearlessly. Every cleanup tool touches only the catalog — your <strong>source files are never moved, renamed, or deleted</strong>.",
      "deepdive.safe.l1": "Find duplicates by metadata fingerprint — even copies on offline drives.",
      "deepdive.safe.l2": 'Offline clips stay searchable with a clear <em>Offline</em> badge.',
      "deepdive.safe.l3": "Rotating, restorable backups of your catalog database.",

      "how.eyebrow": "How it works",
      "how.title": "Three steps to a searchable archive",
      "how.s1.title": "Add your folders",
      "how.s1.text": "Point the app at any root directory on any drive — add as many as you like. Nothing is copied or moved.",
      "how.s2.title": "Scan",
      "how.s2.text":
        "It walks each folder, reads technical metadata with FFprobe, parses dates & locations, and generates thumbnails in the background.",
      "how.s3.title": "Find that shot",
      "how.s3.text": "Search, filter, tag, rate, mark moments, and review — across your entire library, online or offline.",

      "download.eyebrow": "Get started",
      "download.title": "Download Find That Shot",
      "download.lead":
        "A self-contained Windows build with a one-click installer (auto-updates) or a portable zip. Free and open source under the GPLv3.",
      "download.installer": "Installer (Setup.exe)",
      "download.portable": "Portable .zip",
      "download.notes": "Release notes",
      "download.source": "View source",
      "download.ffmpeg":
        "Requires Windows 10/11. FFmpeg is bundled — or install with <code>winget install Gyan.FFmpeg</code>.",
      "download.smartscreen":
        '<b>First launch:</b> the build isn\'t code-signed yet, so Windows may show <i>"Windows protected your PC."</i> That\'s expected — click <b>More info</b>, then <b>Run anyway</b>. It\'s fully open source, so you can read every line on <a href="https://github.com/liknes/FindThatShot" target="_blank" rel="noopener">GitHub</a> first if you like. And your footage is never modified — the app only ever reads your video files.',
      "download.reportBug": "Report a bug",
      "download.emailFeedback": "Email feedback",
      "download.diagnostics":
        "Hit a problem? In the app, use <b>Help → Diagnostics → Copy full report</b> and paste it into your bug report or email — it includes the version and recent logs, which makes issues far easier to fix.",

      "footer.reportBug": "Report a bug",
      "footer.feedback": "Feedback",
      "footer.license": "GPLv3 License",
      "footer.tagline": "Find. Tag. Review. Anywhere.",
    },

    nb: {
      "meta.title": "Find That Shot — Katalogiser, søk og gjennomgå ditt lokale videoarkiv",
      "meta.description":
        "En lokal-først Windows-app som katalogiserer, tagger og hjelper deg å finne ethvert klipp på tvers av alle diskene dine — uten å flytte, gi nytt navn til eller endre en eneste kildefil.",
      "og.description":
        "Katalogiser, søk og gjennomgå ditt lokale videoarkiv. Kildefilene dine flyttes, får aldri nytt navn og endres aldri.",

      "nav.features": "Funksjoner",
      "nav.drone": "Drone",
      "nav.deepdive": "Dypdykk",
      "nav.how": "Slik fungerer det",
      "nav.download": "Last ned",
      "nav.github": "GitHub",

      "lang.en": "English",
      "lang.nb": "Norsk",

      "hero.pill": "Lokal-først · Windows 10/11 · Åpen kildekode",
      "hero.title": 'Finn <span class="accent">det</span> opptaket.<br />På tvers av alle disker.',
      "hero.lead":
        'En skrivebordsapp som katalogiserer, tagger og søker i hele det lokale videoarkivet ditt — interne disker, eksterne, frakoblede — <b style="color:#fff">uten å flytte, gi nytt navn til eller endre en eneste kildefil.</b>',
      "hero.download": "Last ned for Windows",
      "hero.seeFeatures": "Se hva den gjør",
      "hero.meta.files": "<b>0</b> filer rørt på disk",
      "hero.meta.local": "<b>100%</b> lokalt og frakoblet",
      "hero.meta.license": "<b>GPLv3</b> åpen kildekode",
      "hero.shotAlt":
        "Find That Shot hovedvindu — et rutenett med droneklipp-miniatyrer, mappe-sidepanel og stedskart i detaljpanelet.",

      "promise.text":
        "<b>Materialet ditt endres aldri.</b> Kildevideofiler flyttes, får aldri nytt navn, slettes eller røres — appen skriver bare sin egen katalog, miniatyrer og (valgfritt) sidecar-filer.",

      "features.eyebrow": "Bygget for store arkiv",
      "features.title": "Alt du trenger for å finne ett enkelt klipp blant tusenvis",
      "features.subtitle":
        "Katalogiser én gang, og filtrer biblioteket etter tekst, tagger, vurdering, status, kamera, dato, sted og mer — også for klipp på disker som er frakoblet akkurat nå.",

      "features.search.title": "Øyeblikkelig søk og smarte samlinger",
      "features.search.text":
        "OG-sammenfallende søkeord på tvers av filnavn, bane, sted, notater, kamera og tagger. Lagre ethvert filter som en live, selvoppdaterende samling.",
      "features.moments.title": "Øyeblikk — merk det eksakte opptaket",
      "features.moments.text":
        "Et opptak er sjelden en hel fil. Sett tidsstemplede inn/ut-markører med egen etikett, vurdering, notater og tagger, og hopp rett tilbake.",
      "features.player.title": "Innebygd spiller og gjennomgangsmodus",
      "features.player.text":
        "Forhåndsvis klipp i appen med en FFmpeg-spiller (H.264/265, ProRes, DNxHD …) mens du tagger, vurderer og tar notater side om side.",
      "features.map.title": "Bla i kart",
      "features.map.text":
        'Alle geotaggede klipp plottet på et gruppert offline-kart. Klikk et område for å avgrense rutenettet til «hvor filmet jeg det?»',
      "features.calendar.title": "Bla etter dato",
      "features.calendar.text":
        "Et år-for-måned-varmekart basert på når materialet faktisk ble filmet. Klikk en måned for å avgrense rutenettet.",
      "features.dupes.title": "Finn duplikater",
      "features.dupes.text":
        "Grupper kopier etter metadata-fingeravtrykk — også på tvers av frakoblede sikkerhetskopier — og fjern overflødige katalogoppføringer (aldri filene).",
      "features.ai.title": "Lokal AI-tagging og naturlig språk-søk",
      "features.ai.text":
        'Valgfri CLIP-modell kjører helt offline på CPU-en din. Få taggforslag og søk med vanlig språk — <em>«droneopptak over snødekte fjell i solnedgang.»</em>',
      "features.sidecar.title": "Portable sidecar-filer",
      "features.sidecar.text":
        "Skriv valgfritt en liten JSON ved siden av hver video, så tagger, vurderinger, notater og øyeblikk følger materialet til enhver maskin.",
      "features.stats.title": "Statistikk og automatiske sikkerhetskopier",
      "features.stats.text":
        "Et skrivebeskyttet dashbord over hele arkivet, pluss roterende, gjenopprettbare sikkerhetskopier av katalogen slik at kurateringsarbeid aldri går tapt.",

      "drone.eyebrow": "Laget for luftarkiv",
      "drone.title": "Droneopptak får superkrefter",
      "drone.lead":
        'Legg inn DJI-klippene dine, så leser Find That Shot <code style="color:#9be8db;background:rgba(46,230,201,.12);padding:1px 6px;border-radius:5px;font-size:.92em">.SRT</code>-flyloggene som følger med — og gjør råmateriale om til et geotagged, flybart, fullt søkbart kart over hvor du har vært.',
      "drone.p1.title": "Auto-geotagged ved skanning",
      "drone.p1.text": "GPS-posisjonen ved avgang hentes rett fra flyloggen — ingen manuell tagging nødvendig.",
      "drone.p2.title": "Full flyrute på kartet",
      "drone.p2.text": "Hele ruten tegnes som en polylinje med start- og sluttmarkører og høyde per punkt.",
      "drone.p3.title": "Live posisjon og telemetri mens du ser",
      "drone.p3.text":
        "En markør følger ruten i takt med avspilling; et overlay viser ISO, lukker, blender og høyde bilde for bilde.",
      "drone.p4.title": "Jevn 4K / 60p-avspilling",
      "drone.p4.text": "GPU-akselerert dekoding spiller av høy bitrate DJI- og GoPro-klipp i sanntid, ikke slow motion.",
      "drone.shotAlt":
        "Find That Shot gjennomgangsmodus — et droneklipp som spilles av med live telemetristripe, flyrute på sidepanelkartet og en liste over fangede øyeblikk.",

      "deepdive.eyebrow": "Gå dypere",
      "deepdive.title": "Avanserte funksjoner, én fane om gangen",
      "deepdive.subtitle": "Kraftige funksjoner for seriøse arkiv — utforsk det du bryr deg om, hopp over resten.",
      "deepdive.tabsLabel": "Avanserte funksjoner",
      "deepdive.tab.ai": "AI-søk",
      "deepdive.tab.moments": "Øyeblikk",
      "deepdive.tab.nav": "Kart og tid",
      "deepdive.tab.safe": "Trygg opprydding",

      "deepdive.ai.title": "AI som faktisk forstår materialet ditt",
      "deepdive.ai.lead":
        "En valgfri CLIP-modell kjører <strong>helt offline på CPU-en din</strong> — ingenting lastes opp. Den ser på bilder samplet fra hvert klipp, så et treff teller selv om motivet bare vises halvveis.",
      "deepdive.ai.l1": 'Søk med vanlig språk — <em>«droneopptak over snødekte fjell i solnedgang.»</em>',
      "deepdive.ai.l2": "Emne-taggforslag du godtar eller avviser — ingenting legges på automatisk.",
      "deepdive.ai.l3": "Adaptive terskler lærer av valgene dine og blir skarpere over tid.",
      "deepdive.ai.demo": "droneopptak over snødekte fjell i solnedgang",

      "deepdive.moments.title": "Merk det eksakte opptaket, ikke hele filen",
      "deepdive.moments.lead":
        "Et opptak er sjelden et helt klipp. Under gjennomgang trykker du <strong>I</strong> og <strong>O</strong> for å fange et tidsstempelt inn/ut-område med egen miniatyr, etikett, vurdering, notater og tagger.",
      "deepdive.moments.l1": "Auto-hentet miniatyr ved inn-punktet — hvert øyeblikk har et bilde.",
      "deepdive.moments.l2": "<strong>Hopp til</strong> søker spilleren rett til det tidsstempelet.",
      "deepdive.moments.l3": 'Søk øyeblikk i hele arkivet — <em>«hopp til 00:01:32.»</em>',

      "deepdive.nav.title": "Sted og tid blir navigasjon",
      "deepdive.nav.lead":
        "Slutt å scrolle i endeløse mapper. Spør <em>hvor</em> og <em>når</em> du filmet noe, og la arkivet svare.",
      "deepdive.nav.l1": "Hele-arkiv-kart med gruppering — klikk en klynge for å avgrense rutenettet.",
      "deepdive.nav.l2": "År × måned-varmekart basert på når materialet faktisk ble filmet.",
      "deepdive.nav.l3": "Kart og markører fungerer offline — bare kartflisene trenger nett.",

      "deepdive.safe.title": "En katalog som beskytter arbeidet ditt",
      "deepdive.safe.lead":
        "Kurater uten frykt. Alle oppryddingsverktøy berører bare katalogen — <strong>kildefilene dine flyttes, får aldri nytt navn og slettes aldri</strong>.",
      "deepdive.safe.l1": "Finn duplikater etter metadata-fingeravtrykk — også kopier på frakoblede disker.",
      "deepdive.safe.l2": 'Frakoblede klipp forblir søkbare med tydelig <em>Frakoblet</em>-merke.',
      "deepdive.safe.l3": "Roterende, gjenopprettbare sikkerhetskopier av katalogdatabasen.",

      "how.eyebrow": "Slik fungerer det",
      "how.title": "Tre steg til et søkbart arkiv",
      "how.s1.title": "Legg til mappene dine",
      "how.s1.text": "Pek appen mot en rotmappe på hvilken som helst disk — legg til så mange du vil. Ingenting kopieres eller flyttes.",
      "how.s2.title": "Skann",
      "how.s2.text":
        "Den går gjennom hver mappe, leser teknisk metadata med FFprobe, tolker datoer og steder, og genererer miniatyrer i bakgrunnen.",
      "how.s3.title": "Finn det opptaket",
      "how.s3.text": "Søk, filtrer, tagg, vurder, merk øyeblikk og gjennomgå — i hele biblioteket, online eller offline.",

      "download.eyebrow": "Kom i gang",
      "download.title": "Last ned Find That Shot",
      "download.lead":
        "En frittstående Windows-bygg med ett-klikks installasjon (auto-oppdateringer) eller portable zip. Gratis og åpen kildekode under GPLv3.",
      "download.installer": "Installasjon (Setup.exe)",
      "download.portable": "Portable .zip",
      "download.notes": "Utgivelsesnotater",
      "download.source": "Se kildekode",
      "download.ffmpeg":
        "Krever Windows 10/11. FFmpeg følger med — eller installer med <code>winget install Gyan.FFmpeg</code>.",
      "download.smartscreen":
        '<b>Første oppstart:</b> bygget er ikke kodesignert ennå, så Windows kan vise <i>«Windows beskyttet PC-en din.»</i> Det er forventet — klikk <b>Mer info</b>, deretter <b>Kjør likevel</b>. Det er fullt åpen kildekode, så du kan lese hver linje på <a href="https://github.com/liknes/FindThatShot" target="_blank" rel="noopener">GitHub</a> først om du vil. Og materialet ditt endres aldri — appen leser bare videofilene dine.',
      "download.reportBug": "Rapporter en feil",
      "download.emailFeedback": "Send tilbakemelding på e-post",
      "download.diagnostics":
        "Støtt på et problem? I appen, bruk <b>Hjelp → Diagnostikk → Kopier full rapport</b> og lim inn i feilrapporten eller e-posten — den inkluderer versjon og nylige logger, noe som gjør feil langt enklere å fikse.",

      "footer.reportBug": "Rapporter en feil",
      "footer.feedback": "Tilbakemelding",
      "footer.license": "GPLv3-lisens",
      "footer.tagline": "Finn. Tagg. Gjennomgå. Overalt.",
    },
  };

  const STORAGE_KEY = "fts-lang";

  function detectLang() {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === "en" || stored === "nb") return stored;
    const browser = (navigator.language || "en").toLowerCase();
    return browser.startsWith("nb") || browser.startsWith("no") ? "nb" : "en";
  }

  function t(lang, key) {
    return STRINGS[lang]?.[key] ?? STRINGS.en[key] ?? key;
  }

  function applyLang(lang) {
    const dict = STRINGS[lang] || STRINGS.en;
    document.documentElement.lang = lang === "nb" ? "nb-NO" : "en";

    document.title = dict["meta.title"];
    const desc = document.querySelector('meta[name="description"]');
    if (desc) desc.setAttribute("content", dict["meta.description"]);
    const ogDesc = document.querySelector('meta[property="og:description"]');
    if (ogDesc) ogDesc.setAttribute("content", dict["og.description"]);

    document.querySelectorAll("[data-i18n]").forEach((el) => {
      const key = el.getAttribute("data-i18n");
      const val = dict[key];
      if (val == null) return;
      if (el.hasAttribute("data-i18n-html")) el.innerHTML = val;
      else el.textContent = val;
    });

    document.querySelectorAll("[data-i18n-alt]").forEach((el) => {
      const key = el.getAttribute("data-i18n-alt");
      const val = dict[key];
      if (val != null) el.setAttribute("alt", val);
    });

    document.querySelectorAll("[data-i18n-aria-label]").forEach((el) => {
      const key = el.getAttribute("data-i18n-aria-label");
      const val = dict[key];
      if (val != null) el.setAttribute("aria-label", val);
    });

    document.querySelectorAll("[data-lang-btn]").forEach((btn) => {
      const active = btn.getAttribute("data-lang-btn") === lang;
      btn.setAttribute("aria-pressed", active ? "true" : "false");
      btn.classList.toggle("lang-active", active);
    });

    localStorage.setItem(STORAGE_KEY, lang);
  }

  window.FTS_I18N = {
    apply(lang) {
      if (lang !== "en" && lang !== "nb") return;
      applyLang(lang);
    },
    init() {
      applyLang(detectLang());
      document.querySelectorAll("[data-lang-btn]").forEach((btn) => {
        btn.addEventListener("click", () => {
          window.FTS_I18N.apply(btn.getAttribute("data-lang-btn"));
        });
      });
    },
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", () => window.FTS_I18N.init());
  } else {
    window.FTS_I18N.init();
  }
})();
