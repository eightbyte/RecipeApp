# Seed Corpus Curator

A throwaway local tool for manually reducing the Phase 9 MyPlate harvest before Stage 3 parses it.
Browse the 1,123 cached recipes, search them by name or by ingredient, and permanently delete the
ones you do not want in the seed library.

It lives outside `RecipeApp.API` on purpose: curation is a one-time human step, so it has no
business being an API surface the app carries forever. It has **no dependencies** — Node's built-in
modules only, no `npm install` — and binds to `127.0.0.1` only.

## Running

```bash
node tools/seed-curator/server.js
```

Then open <http://127.0.0.1:5174>. The whole corpus re-indexes on every start (about a second), so
there is no cache to go stale.

| Option | Default | Purpose |
|---|---|---|
| `--port <number>` | `5174` | Port to listen on |
| `--cache <path>` | `backend/RecipeApp.API/seed-data/myplate` | Seed cache directory |
| `--help` | | Usage summary |

## Searching

Type in the box at the top. Multiple words must **all** match by default; tick *Any word* to switch
to "any of". The *Search in* chips control which fields are searched — name and ingredients are on
by default, so a search for `mushrooms` finds recipes containing them without also matching every
recipe whose directions happen to mention a mushroom-shaped garnish.

| Syntax | Meaning |
|---|---|
| `chicken rice` | Both words appear |
| `"cream of chicken"` | That exact phrase |
| `-mushrooms` | Exclude anything matching. Always applied, whatever the Any/All setting |
| `chicken -mushrooms` | Chicken recipes that do not mention mushrooms |

*Whole words* stops `corn` from matching `cornstarch` or `popcorn`. Press `/` anywhere to jump to
the search box.

Rows show the ingredient or direction lines that caused the match, so a hit is explained without
opening anything. Click a row to expand the full recipe — photo, ingredients, directions, notes,
source, and a link to the original archived page.

## Deleting

Tick the rows you want gone (or use **Select all matches** to take the whole current result set),
then **Delete selected**. Selection survives changing the search, so you can build a set across
several searches — tick *Selected only* to review it before deleting.

Deleting a recipe removes:

- `raw/<slug>.html` — the cached page
- `images/<slug>.*` — the cached photo
- `parsed/<slug>.json` and `normalised/<slug>.json` — later-stage artefacts, if they exist yet
- its entry in `manifest.json`
- its entry in `state.json`

**The `manifest.json` entry is the one that matters.** Every Phase 9 stage iterates
`manifest.Recipes` — `HarvestAsync` included — so deleting the files alone would leave the recipe
in the pipeline and let the next `--harvest` download it again. Removing the manifest entry is what
actually takes a recipe out of the corpus.

Both JSON files are rewritten atomically (temp file, then rename) and keep their existing CRLF line
endings, so a delete produces a git diff containing only the entries you removed.

### There is no undo

Deletion is immediate and permanent — no recycle bin, no `rejected/` folder. Back up
`backend/RecipeApp.API/seed-data/myplate/` before a session if you want a way back.

Note that `manifest.json` is committed to git, so `git diff` will show exactly which recipes you
removed, and re-running `dotnet run -- seed-recipes --discover` would rediscover every deleted slug
from the archive and re-add it. Don't run `--discover` after curating unless that is what you want.

## Afterwards

Check the corpus size the pipeline now sees:

```bash
cd backend/RecipeApp.API
dotnet run -- seed-recipes --report
```

Once you are happy with the reduced corpus, this whole directory can be deleted.

## Files

| File | Purpose |
|---|---|
| `server.js` | HTTP server, index build, and the delete logic |
| `extract.js` | Cached page → searchable record (name, ingredients, directions, notes, source) |
| `public/index.html` | The entire UI — markup, styles and script in one file |

`extract.js` is intentionally *not* the Phase 9 Stage 3 parser. It only has to be good enough to
find a recipe, never good enough to import one, so every field degrades to empty rather than
throwing. It handles both Drupal templates in the corpus
(`field--name-field-mp-ingredients`, 1,058 pages; `field--name-field-ingredients`, 65 pages) and
reads the title, description and serving yield from each page's JSON-LD `Recipe` node.
