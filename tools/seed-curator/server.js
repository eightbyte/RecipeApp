'use strict';

/**
 * Seed corpus curator — a throwaway, zero-dependency local tool for manually reducing the Phase 9
 * MyPlate harvest before Stage 3 parses it.
 *
 * Deliberately outside RecipeApp.API: curation is a one-time human step, so it has no business
 * being an API surface the app carries forever. It reads the same cache the harvester writes and
 * speaks only to 127.0.0.1.
 *
 * Deleting a recipe removes its cached page, its photo, any parsed/normalised artefacts, its
 * `manifest.json` entry and its `state.json` entry. The manifest is what every Phase 9 stage
 * iterates, so dropping the entry is what actually takes a recipe out of the pipeline — deleting
 * the files alone would leave `--harvest` free to download them again.
 *
 * Usage: node tools/seed-curator/server.js [--port 5174] [--cache <path>]
 */

const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');

const { extractRecipeRecord } = require('./extract.js');

// ── Configuration ─────────────────────────────────────────────────────────────

const REPOSITORY_ROOT = path.resolve(__dirname, '..', '..');
const DEFAULT_CACHE_DIRECTORY = path.join(
  REPOSITORY_ROOT, 'backend', 'RecipeApp.API', 'seed-data', 'myplate');
const DEFAULT_PORT = 5174;
const LOOPBACK_HOST = '127.0.0.1';

/** Mirrors `SeedCacheStore.SlugPattern` — this is the path-traversal guard, not a tidiness check. */
const SLUG_PATTERN = /^[a-z0-9]+(?:[-_][a-z0-9]+)*$/;

const IMAGE_CONTENT_TYPES = {
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.png': 'image/png',
  '.gif': 'image/gif',
  '.webp': 'image/webp',
};

const MAXIMUM_REQUEST_BODY_BYTES = 1_000_000;

function parseCommandLine(argv) {
  const options = { port: DEFAULT_PORT, cacheDirectory: DEFAULT_CACHE_DIRECTORY };

  for (let index = 0; index < argv.length; index += 1) {
    const value = argv[index + 1];

    switch (argv[index]) {
      case '--port':
        if (!value) throw new Error('--port requires a number.');
        options.port = Number.parseInt(value, 10);
        if (!Number.isInteger(options.port)) throw new Error(`'${value}' is not a port number.`);
        index += 1;
        break;

      case '--cache':
        if (!value) throw new Error('--cache requires a directory path.');
        options.cacheDirectory = path.resolve(value);
        index += 1;
        break;

      case '--help':
      case '-h':
        options.showHelp = true;
        break;

      default:
        throw new Error(`Unrecognised argument '${argv[index]}'.`);
    }
  }

  return options;
}

const HELP_TEXT = `
Seed corpus curator — review and prune the Phase 9 MyPlate harvest.

  node tools/seed-curator/server.js [options]

  --port <number>   Port to listen on (default ${DEFAULT_PORT})
  --cache <path>    Seed cache directory
                    (default backend/RecipeApp.API/seed-data/myplate)
  --help            Show this message

Deleting is permanent: the cached page, the photo, any parsed/normalised artefacts, and the
manifest.json and state.json entries all go. There is no undo — back up the cache first.
`;

// ── Cache paths ───────────────────────────────────────────────────────────────

class SeedCachePaths {
  constructor(rootPath) {
    this.rootPath = rootPath;
    this.rawDirectory = path.join(rootPath, 'raw');
    this.imagesDirectory = path.join(rootPath, 'images');
    this.parsedDirectory = path.join(rootPath, 'parsed');
    this.normalisedDirectory = path.join(rootPath, 'normalised');
    this.manifestPath = path.join(rootPath, 'manifest.json');
    this.statePath = path.join(rootPath, 'state.json');
  }

  /**
   * Resolves a path inside the cache, refusing anything that escapes it. Slugs originate from a
   * remote archive index and are used as filenames, so they are never trusted.
   */
  resolveWithin(...segments) {
    const resolved = path.resolve(this.rootPath, ...segments);
    const boundary = this.rootPath.endsWith(path.sep) ? this.rootPath : this.rootPath + path.sep;

    if (!resolved.startsWith(boundary)) {
      throw new HttpError(400, `Path '${resolved}' escapes the seed cache directory.`);
    }

    return resolved;
  }

  rawPath(slug) {
    return this.resolveWithin('raw', `${assertSafeSlug(slug)}.html`);
  }

  /** Cached photo for a slug, whatever its extension, or null when none was harvested. */
  findImagePath(slug) {
    assertSafeSlug(slug);
    if (!fs.existsSync(this.imagesDirectory)) return null;

    const match = fs
      .readdirSync(this.imagesDirectory)
      .find((fileName) => path.parse(fileName).name === slug);

    return match ? this.resolveWithin('images', match) : null;
  }
}

// ── Errors ────────────────────────────────────────────────────────────────────

class HttpError extends Error {
  constructor(statusCode, message) {
    super(message);
    this.statusCode = statusCode;
  }
}

function assertSafeSlug(slug) {
  if (typeof slug !== 'string' || !SLUG_PATTERN.test(slug)) {
    throw new HttpError(400, `'${slug}' is not a valid recipe slug.`);
  }
  return slug;
}

// ── JSON files ────────────────────────────────────────────────────────────────

/**
 * Reads a JSON file, remembering its line endings. The harvester writes CRLF on Windows;
 * preserving that keeps a delete's git diff to the entries actually removed.
 */
function readJsonFile(filePath) {
  const text = fs.readFileSync(filePath, 'utf8').replace(/^﻿/, '');
  return { value: JSON.parse(text), newline: text.includes('\r\n') ? '\r\n' : '\n' };
}

/** Writes through a temporary file, so an interrupted write cannot truncate the manifest. */
function writeJsonFileAtomically(filePath, value, newline) {
  let text = JSON.stringify(value, null, 2);
  if (newline === '\r\n') text = text.replace(/\n/g, '\r\n');

  const temporaryPath = `${filePath}.tmp`;
  fs.writeFileSync(temporaryPath, text, 'utf8');
  fs.renameSync(temporaryPath, filePath);
}

// ── Index ─────────────────────────────────────────────────────────────────────

/**
 * Reads every cached page into a searchable record set. Rebuilt on startup rather than cached:
 * the full corpus parses in about a second, which is cheaper than reasoning about a stale index.
 */
function buildIndex(cachePaths) {
  const startedAt = Date.now();

  const manifestEntriesBySlug = new Map();
  let manifestHarvestedAt = null;

  if (fs.existsSync(cachePaths.manifestPath)) {
    const manifest = readJsonFile(cachePaths.manifestPath).value;
    manifestHarvestedAt = manifest.harvestedAt ?? null;

    for (const entry of manifest.recipes ?? []) {
      manifestEntriesBySlug.set(entry.slug, entry);
    }
  }

  if (!fs.existsSync(cachePaths.rawDirectory)) {
    throw new Error(
      `No cached pages at ${cachePaths.rawDirectory}. Run 'dotnet run -- seed-recipes --harvest' first.`);
  }

  const pageFileNames = fs
    .readdirSync(cachePaths.rawDirectory)
    .filter((fileName) => fileName.endsWith('.html'));

  const recipes = [];
  const unreadable = [];

  for (const fileName of pageFileNames) {
    const slug = path.basename(fileName, '.html');
    if (!SLUG_PATTERN.test(slug)) continue;

    try {
      const html = fs.readFileSync(path.join(cachePaths.rawDirectory, fileName), 'utf8');
      const record = extractRecipeRecord(slug, html);
      const imagePath = cachePaths.findImagePath(slug);
      const manifestEntry = manifestEntriesBySlug.get(slug);

      recipes.push({
        ...record,
        hasImage: imagePath !== null,
        inManifest: manifestEntry !== undefined,
        originalUrl: manifestEntry?.originalUrl ?? '',
        archiveUrl: manifestEntry
          ? `https://web.archive.org/web/${manifestEntry.timestamp}/${manifestEntry.originalUrl}`
          : '',
      });
    } catch (error) {
      unreadable.push({ slug, message: error.message });
    }
  }

  recipes.sort((first, second) => first.title.localeCompare(second.title));

  return {
    recipes,
    unreadable,
    manifestHarvestedAt,
    manifestCount: manifestEntriesBySlug.size,
    builtAt: new Date().toISOString(),
    buildMilliseconds: Date.now() - startedAt,
  };
}

// ── Deletion ──────────────────────────────────────────────────────────────────

function unlinkIfExists(filePath) {
  if (filePath === null || !fs.existsSync(filePath)) return false;
  fs.unlinkSync(filePath);
  return true;
}

/**
 * Permanently removes recipes from the seed corpus.
 *
 * Files go first, then the manifest, then state — so an interruption leaves entries pointing at
 * missing files (which `--harvest` simply re-fetches) rather than orphaned files no stage will
 * ever look at again.
 */
function deleteRecipes(cachePaths, index, requestedSlugs) {
  const knownSlugs = new Set(index.recipes.map((recipe) => recipe.slug));

  const slugsToDelete = [];
  const skipped = [];

  for (const slug of requestedSlugs) {
    assertSafeSlug(slug);

    if (knownSlugs.has(slug)) slugsToDelete.push(slug);
    else skipped.push(slug);
  }

  let pagesDeleted = 0;
  let imagesDeleted = 0;
  let artefactsDeleted = 0;

  for (const slug of slugsToDelete) {
    if (unlinkIfExists(cachePaths.rawPath(slug))) pagesDeleted += 1;
    if (unlinkIfExists(cachePaths.findImagePath(slug))) imagesDeleted += 1;

    for (const directory of ['parsed', 'normalised']) {
      if (unlinkIfExists(cachePaths.resolveWithin(directory, `${slug}.json`))) {
        artefactsDeleted += 1;
      }
    }
  }

  const deletedSlugs = new Set(slugsToDelete);
  let manifestEntriesRemoved = 0;
  let stateEntriesRemoved = 0;

  if (fs.existsSync(cachePaths.manifestPath)) {
    const { value: manifest, newline } = readJsonFile(cachePaths.manifestPath);
    const remaining = (manifest.recipes ?? []).filter((entry) => !deletedSlugs.has(entry.slug));

    manifestEntriesRemoved = (manifest.recipes ?? []).length - remaining.length;

    if (manifestEntriesRemoved > 0) {
      writeJsonFileAtomically(
        cachePaths.manifestPath, { ...manifest, recipes: remaining }, newline);
    }
  }

  if (fs.existsSync(cachePaths.statePath)) {
    const { value: state, newline } = readJsonFile(cachePaths.statePath);

    for (const slug of deletedSlugs) {
      if (state.slugs && slug in state.slugs) {
        delete state.slugs[slug];
        stateEntriesRemoved += 1;
      }
    }

    if (stateEntriesRemoved > 0) {
      state.updatedAt = new Date().toISOString();
      writeJsonFileAtomically(cachePaths.statePath, state, newline);
    }
  }

  index.recipes = index.recipes.filter((recipe) => !deletedSlugs.has(recipe.slug));

  return {
    deleted: slugsToDelete,
    skipped,
    pagesDeleted,
    imagesDeleted,
    artefactsDeleted,
    manifestEntriesRemoved,
    stateEntriesRemoved,
    remaining: index.recipes.length,
  };
}

// ── HTTP ──────────────────────────────────────────────────────────────────────

function sendJson(response, statusCode, payload) {
  const body = Buffer.from(JSON.stringify(payload), 'utf8');
  response.writeHead(statusCode, {
    'Content-Type': 'application/json; charset=utf-8',
    'Content-Length': body.length,
    'Cache-Control': 'no-store',
  });
  response.end(body);
}

function readRequestBody(request) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let byteLength = 0;

    request.on('data', (chunk) => {
      byteLength += chunk.length;
      if (byteLength > MAXIMUM_REQUEST_BODY_BYTES) {
        reject(new HttpError(413, 'Request body is too large.'));
        request.destroy();
        return;
      }
      chunks.push(chunk);
    });

    request.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
    request.on('error', reject);
  });
}

function serveThumbnail(cachePaths, response, slug) {
  const imagePath = cachePaths.findImagePath(slug);
  if (imagePath === null) throw new HttpError(404, `No cached image for '${slug}'.`);

  const contentType = IMAGE_CONTENT_TYPES[path.extname(imagePath).toLowerCase()];
  if (!contentType) throw new HttpError(415, `Unsupported image type for '${slug}'.`);

  const body = fs.readFileSync(imagePath);
  response.writeHead(200, {
    'Content-Type': contentType,
    'Content-Length': body.length,
    'Cache-Control': 'no-store',
  });
  response.end(body);
}

function servePage(response) {
  const body = fs.readFileSync(path.join(__dirname, 'public', 'index.html'));
  response.writeHead(200, {
    'Content-Type': 'text/html; charset=utf-8',
    'Content-Length': body.length,
    'Cache-Control': 'no-store',
  });
  response.end(body);
}

async function handleRequest(cachePaths, index, request, response) {
  const url = new URL(request.url, `http://${LOOPBACK_HOST}`);

  if (request.method === 'GET' && (url.pathname === '/' || url.pathname === '/index.html')) {
    return servePage(response);
  }

  if (request.method === 'GET' && url.pathname === '/api/recipes') {
    return sendJson(response, 200, {
      cacheDirectory: cachePaths.rootPath,
      manifestHarvestedAt: index.manifestHarvestedAt,
      manifestCount: index.manifestCount,
      builtAt: index.builtAt,
      unreadable: index.unreadable,
      recipes: index.recipes,
    });
  }

  if (request.method === 'GET' && url.pathname.startsWith('/api/thumbnail/')) {
    const slug = decodeURIComponent(url.pathname.slice('/api/thumbnail/'.length));
    return serveThumbnail(cachePaths, response, assertSafeSlug(slug));
  }

  if (request.method === 'POST' && url.pathname === '/api/delete') {
    const body = await readRequestBody(request);

    let requestedSlugs;
    try {
      requestedSlugs = JSON.parse(body).slugs;
    } catch {
      throw new HttpError(400, 'Request body must be JSON.');
    }

    if (!Array.isArray(requestedSlugs) || requestedSlugs.length === 0) {
      throw new HttpError(400, 'Provide a non-empty "slugs" array.');
    }

    const result = deleteRecipes(cachePaths, index, requestedSlugs);

    console.log(
      `Deleted ${result.deleted.length} recipe(s): ${result.pagesDeleted} page(s), ` +
      `${result.imagesDeleted} image(s), ${result.manifestEntriesRemoved} manifest entr(ies). ` +
      `${result.remaining} remaining.`);

    return sendJson(response, 200, result);
  }

  throw new HttpError(404, `No route for ${request.method} ${url.pathname}.`);
}

// ── Entry point ───────────────────────────────────────────────────────────────

function main() {
  let options;
  try {
    options = parseCommandLine(process.argv.slice(2));
  } catch (error) {
    console.error(`${error.message}\n${HELP_TEXT}`);
    process.exitCode = 1;
    return;
  }

  if (options.showHelp) {
    console.log(HELP_TEXT);
    return;
  }

  const cachePaths = new SeedCachePaths(options.cacheDirectory);

  console.log(`Reading seed cache at ${cachePaths.rootPath} ...`);

  let index;
  try {
    index = buildIndex(cachePaths);
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
    return;
  }

  console.log(
    `Indexed ${index.recipes.length} recipe(s) in ${index.buildMilliseconds} ms ` +
    `(${index.manifestCount} in the manifest).`);

  if (index.unreadable.length > 0) {
    console.warn(`${index.unreadable.length} page(s) could not be read:`);
    for (const failure of index.unreadable) console.warn(`  ${failure.slug}: ${failure.message}`);
  }

  const server = http.createServer((request, response) => {
    Promise.resolve(handleRequest(cachePaths, index, request, response)).catch((error) => {
      const statusCode = error instanceof HttpError ? error.statusCode : 500;
      if (statusCode >= 500) console.error(error);
      if (!response.headersSent) sendJson(response, statusCode, { error: error.message });
    });
  });

  server.listen(options.port, LOOPBACK_HOST, () => {
    console.log(`\n  Seed curator ready at http://${LOOPBACK_HOST}:${options.port}\n`);
    console.log('  Deletions are permanent. Ctrl+C to stop.\n');
  });

  server.on('error', (error) => {
    console.error(
      error.code === 'EADDRINUSE'
        ? `Port ${options.port} is already in use. Try --port ${options.port + 1}.`
        : error.message);
    process.exitCode = 1;
  });
}

main();
