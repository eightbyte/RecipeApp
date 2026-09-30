'use strict';

/**
 * Turns one cached MyPlate page into a searchable record.
 *
 * This is deliberately independent of the Phase 9 Stage 3 parser: it only has to be good enough
 * to *find* a recipe by name or ingredient, never good enough to import one. Anything it cannot
 * read degrades to an empty field rather than throwing, so a single odd page never costs you the
 * whole index.
 */

// Two Drupal content types are present in the corpus. The `mp-` class covers 1,058 pages; the
// bare class covers the 65 legacy pages. Neither class name is a substring of the other.
const INGREDIENT_FIELD_CLASSES = [
  'field--name-field-mp-ingredients',
  'field--name-field-ingredients',
];
const INSTRUCTIONS_FIELD_CLASS = 'field--name-field-instructions';
const NOTES_FIELD_CLASS = 'field--name-field-notes';
const SOURCE_FIELD_CLASS = 'field--name-field-source';
const LEGACY_SERVING_SIZE_FIELD_CLASS = 'field--name-field-recipe-serving-size';

const JSON_LD_SCRIPT_PATTERN =
  /<script\b[^>]*type\s*=\s*["']application\/ld\+json["'][^>]*>([\s\S]*?)<\/script>/gi;

const NAMED_ENTITIES = {
  amp: '&',
  apos: "'",
  bull: '•',
  deg: '°',
  frac12: '½',
  frac14: '¼',
  frac34: '¾',
  gt: '>',
  hellip: '…',
  lsquo: '‘',
  ldquo: '“',
  lt: '<',
  mdash: '—',
  ndash: '–',
  nbsp: ' ',
  quot: '"',
  rsquo: '’',
  rdquo: '”',
};

function decodeHtmlEntities(text) {
  return text.replace(/&(#x[0-9a-f]+|#\d+|[a-z][a-z0-9]*);/gi, (whole, body) => {
    if (body[0] === '#') {
      const codePoint =
        body[1] === 'x' || body[1] === 'X'
          ? Number.parseInt(body.slice(2), 16)
          : Number.parseInt(body.slice(1), 10);

      if (!Number.isFinite(codePoint) || codePoint <= 0 || codePoint > 0x10ffff) return whole;
      return String.fromCodePoint(codePoint);
    }

    const named = NAMED_ENTITIES[body.toLowerCase()];
    return named === undefined ? whole : named;
  });
}

/** Collapses every run of whitespace — including the non-breaking spaces Drupal emits — to one space. */
function collapseWhitespace(text) {
  return text.replace(/[\s ]+/g, ' ').trim();
}

function stripTags(fragment) {
  return fragment
    .replace(/<(script|style)\b[^>]*>[\s\S]*?<\/\1>/gi, ' ')
    .replace(/<!--[\s\S]*?-->/g, ' ')
    .replace(/<[^>]+>/g, ' ');
}

function toPlainText(fragment) {
  if (!fragment) return '';
  return collapseWhitespace(decodeHtmlEntities(stripTags(fragment)));
}

/**
 * Finds the first element carrying `className` and returns it with its content, using depth
 * counting rather than a lazy `.*?` so a nested element of the same tag cannot end the match early.
 */
function extractElementByClass(html, className) {
  const openTagPattern = /<([a-z][\w-]*)\b([^>]*)>/gi;
  let openTag;

  while ((openTag = openTagPattern.exec(html)) !== null) {
    const classAttribute = /\bclass\s*=\s*(?:"([^"]*)"|'([^']*)')/i.exec(openTag[2]);
    if (!classAttribute) continue;

    const classNames = (classAttribute[1] || classAttribute[2] || '').split(/\s+/);
    if (!classNames.includes(className)) continue;

    const tagName = openTag[1].toLowerCase();
    const contentStart = openTag.index + openTag[0].length;
    const closePattern = new RegExp(`<(/?)${tagName}\\b`, 'gi');
    closePattern.lastIndex = contentStart;

    let depth = 1;
    let boundary;

    while ((boundary = closePattern.exec(html)) !== null) {
      if (boundary[1]) {
        depth -= 1;
        if (depth === 0) {
          return { innerHtml: html.slice(contentStart, boundary.index) };
        }
      } else {
        depth += 1;
      }
    }

    // Unbalanced markup: take the rest of the document rather than losing the field entirely.
    return { innerHtml: html.slice(contentStart) };
  }

  return null;
}

function extractFirstElementByClass(html, classNames) {
  for (const className of classNames) {
    const element = extractElementByClass(html, className);
    if (element) return element;
  }
  return null;
}

/** Drops the `<h2>Ingredients</h2>` style label Drupal renders above every field. */
function removeFieldLabel(fragment) {
  return fragment.replace(/<h[1-6]\b[^>]*>[\s\S]*?<\/h[1-6]>/i, ' ');
}

function extractListItems(fragment) {
  return [...fragment.matchAll(/<li\b[^>]*>([\s\S]*?)<\/li>/gi)]
    .map((item) => toPlainText(item[1]))
    .filter(Boolean);
}

/** List items when the field is a list, otherwise the whole field as a single block of prose. */
function extractSteps(fragment) {
  const listItems = extractListItems(fragment);
  if (listItems.length > 0) return listItems;

  const prose = toPlainText(fragment);
  return prose ? [prose] : [];
}

function findRecipeNode(node) {
  if (Array.isArray(node)) {
    for (const item of node) {
      const found = findRecipeNode(item);
      if (found) return found;
    }
    return null;
  }

  if (!node || typeof node !== 'object') return null;

  const type = node['@type'];
  if (type === 'Recipe' || (Array.isArray(type) && type.includes('Recipe'))) return node;

  for (const value of Object.values(node)) {
    const found = findRecipeNode(value);
    if (found) return found;
  }

  return null;
}

function extractJsonLdRecipe(html) {
  JSON_LD_SCRIPT_PATTERN.lastIndex = 0;
  let script;

  while ((script = JSON_LD_SCRIPT_PATTERN.exec(html)) !== null) {
    try {
      const recipe = findRecipeNode(JSON.parse(script[1]));
      if (recipe) return recipe;
    } catch {
      // A malformed block is skipped; the HTML fallbacks below still produce a usable record.
    }
  }

  return null;
}

function extractHeadingTitle(html) {
  const heading = /<h1\b[^>]*>([\s\S]*?)<\/h1>/i.exec(html);
  if (heading) return toPlainText(heading[1]);

  const title = /<title\b[^>]*>([\s\S]*?)<\/title>/i.exec(html);
  return title ? toPlainText(title[1]) : '';
}

/**
 * Builds the search record for one cached page.
 *
 * @param {string} slug Cache key, also the recipe's filename stem.
 * @param {string} html Raw cached page markup.
 */
function extractRecipeRecord(slug, html) {
  const jsonLd = extractJsonLdRecipe(html) || {};

  const ingredientsField = extractFirstElementByClass(html, INGREDIENT_FIELD_CLASSES);
  const instructionsField = extractElementByClass(html, INSTRUCTIONS_FIELD_CLASS);
  const notesField = extractElementByClass(html, NOTES_FIELD_CLASS);
  const sourceField = extractElementByClass(html, SOURCE_FIELD_CLASS);
  const legacyServingSizeField = extractElementByClass(html, LEGACY_SERVING_SIZE_FIELD_CLASS);

  const servingsFromJsonLd =
    typeof jsonLd.recipeYield === 'string' ? collapseWhitespace(jsonLd.recipeYield) : '';

  return {
    slug,
    title: (typeof jsonLd.name === 'string' && collapseWhitespace(jsonLd.name)) ||
      extractHeadingTitle(html) ||
      slug,
    description:
      typeof jsonLd.description === 'string' ? collapseWhitespace(jsonLd.description) : '',
    servings:
      servingsFromJsonLd ||
      (legacyServingSizeField ? toPlainText(removeFieldLabel(legacyServingSizeField.innerHtml)) : ''),
    ingredients: ingredientsField
      ? extractListItems(removeFieldLabel(ingredientsField.innerHtml))
      : [],
    directions: instructionsField
      ? extractSteps(removeFieldLabel(instructionsField.innerHtml))
      : [],
    notes: notesField ? toPlainText(removeFieldLabel(notesField.innerHtml)) : '',
    source: sourceField ? toPlainText(sourceField.innerHtml).replace(/^Source:\s*/i, '') : '',
  };
}

module.exports = {
  collapseWhitespace,
  decodeHtmlEntities,
  extractElementByClass,
  extractRecipeRecord,
  toPlainText,
};
