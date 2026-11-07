# Coding standards

The judgement rules for writing and reviewing code. A rule marked **Enforced** has a test behind
it, and a failure there names the rule.

## Match the surrounding idiom

New code reads like its neighbours: the comment density, naming, error handling and test layout of
the file and folder it lands in. Where a neighbour breaks a rule in this file, the rule wins.

## Comments

- A comment is at most one sentence and says why, not what. Clean code describes itself, and extra
  comments cost context.
- A comment states its reason itself rather than citing a phase, spec section or decision number.
  **Enforced** across backend and frontend source by
  `backend/RecipeApp.Tests/Conventions/CommentCitationTests.cs`.

## Naming

- Name domain concepts with the terms in `CONTEXT.md`, never the aliases it lists under _Avoid_.

## Types

- Backend: nullable reference types are on. Handle a value that can be null rather than silencing
  the warning. `!` asserts a guarantee the compiler cannot see, such as an EF Core required
  navigation (`= null!`) or a field a validator already required.
- Frontend: declare every prop and `defineModel` with a `type`, plus `required: true` or a
  `default`. **Enforced** by `npm run lint` (`vue/require-prop-types`, `vue/require-default-prop`).

## Values with one home

- Settings that differ by environment live in `appsettings*.json` (backend) or `.env*` (frontend).
- Lists of options (units, categories, recipe sorts) come from the backend `Enums/`
  and their mirrors in `frontend/src/constants/`.
- Recipe length and range limits come from `Validators/RecipeLimits.cs`.

## DTOs and mapping (backend)

- A DTO ends in `Request` or `Response`, or `Query` for a query string bound with `[AsParameters]`
  (`RecipeListQuery`). **Enforced** by `backend/RecipeApp.Tests/Conventions/DtoNamingTests.cs`.
- Query parameters are camelCase, because the frontend and the API docs use those names.
  `[AsParameters]` names a parameter after its PascalCase record property unless told otherwise.
  **Enforced** by `backend/RecipeApp.Tests/Endpoints/OpenApiDocumentTests.cs`.
- Mapping is manual: static `ToResponse()` / `ToDetail()` / `ToListItem()` extensions in
  `DTOs/Mappings.cs`. No AutoMapper.

## Vuetify (frontend)

- Prefer a Vuetify component over raw HTML wherever one exists.
- Vuetify is the only styling framework
  ([ADR 0001](docs/adr/0001-vuetify-is-the-only-styling-framework.md)). Don't add Bootstrap or
  another CSS framework.
- Spacing classes follow Vuetify's 4px-per-step scale (`mb-4` is 16px, `mb-6` is 24px).
