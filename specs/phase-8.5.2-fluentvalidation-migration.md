# Phase 8.5.2 — FluentValidation 12 Migration

**Version:** 1.0
**Date:** 2026-09-06
**Status:** Draft — for review
**Depends on:** Phase 8.5.1 (measurement handling) complete
**Blocks:** Phase 9 (Seed Recipe Library)

> A small corrective phase run **before** Phase 9 development begins. `FluentValidation.AspNetCore`
> is deprecated by its authors — marked **Legacy**, frozen at 11.3.1, and it will never see a
> version 12. It is the one thing pinning this codebase to the FluentValidation 11 line.
>
> The good news, established by the survey in §3: this project **never used what that package
> does**. Every endpoint already validates by hand. The package contributes one transitive
> dependency and nothing else. So this is a package swap, not a rewrite — confirmed by the
> verification spike in §4, which built clean and passed all 523 tests.
>
> The open question for review is whether to also collapse the twelve hand-copied validation
> blocks into a single endpoint filter (§6) while we are in here, or to leave them alone.

---

## 1. Overview

### 1.1 The problem

`dotnet list package --deprecated` reports:

```
> FluentValidation.AspNetCore      11.3.1      11.3.1     Legacy
```

`Legacy` is NuGet's "this package is deprecated and has no direct successor" signal. The
FluentValidation team retired the ASP.NET Core integration package deliberately: its headline
feature — automatic MVC model validation via an action filter — was judged to hide too much, and
the recommended replacement is *explicit* validation at the call site.

11.3.1 is the final version. It depends on `FluentValidation (>= 11.11.0)`, which is what holds
`RecipeApp.API` on the 11 line even though 12.1.1 has been out for some time.

### 1.2 What the package actually gives us — nothing we use

`FluentValidation.AspNetCore` ships exactly one assembly, and its public surface is:

| Feature | Used here? |
|---|---|
| `AddFluentValidationAutoValidation()` — MVC action filter | **No** — there is no MVC in this project |
| `AddFluentValidationClientsideAdapters()` — Razor/jQuery unobtrusive validation | **No** — the frontend is a Vue SPA |
| `IValidatorInterceptor` | **No** — never referenced |
| Transitively re-exports `FluentValidation.DependencyInjectionExtensions` | **Yes** — this is the only part in use |

The auto-validation filter, the package's entire reason to exist, hooks MVC's model-binding
pipeline. This API is Minimal API end to end (`app.MapGroup("/api/v1")`, endpoint extension methods
per CLAUDE.md), and a repository-wide search for `ControllerBase`, `AddControllers` and
`[ApiController]` returns **no matches**. The filter has never run a single time in this codebase.

What `Program.cs:29` actually calls —

```csharp
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
```

— is `FluentValidation.ServiceCollectionExtensions`, which lives in
**`FluentValidation.DependencyInjectionExtensions`**, a separate package that *is* maintained and
*does* ship a 12.1.1. We currently get it by accident, as a transitive dependency of the
deprecated one.

### 1.3 Why before Phase 9

Phase 9 seeds a recipe library, which means bulk-importing recipes through the same validators the
API uses. Three reasons to do this first:

1. **It is cheap right now and gets more expensive later.** Phase 9 adds validation rules and
   probably validator classes. Migrating five validator files is easier than migrating eight.
2. **The version skew is live on this branch today** (§3.3). Tests currently execute against
   FluentValidation 12 while the API ships 11 — so Phase 9 would be writing validator tests against
   an assembly that is not the one in production.
3. **It clears the last deprecation warning in the backend.** After the Phase 8.5.x package
   refresh, `--vulnerable` and `--outdated` are both clean on both projects; this is the only
   remaining `--deprecated` entry.

---

## 2. Deliverable

1. **Package swap** — `FluentValidation.AspNetCore` 11.3.1 out; `FluentValidation` 12.1.1 and
   `FluentValidation.DependencyInjectionExtensions` 12.1.1 in, declared explicitly on
   `RecipeApp.API`. Both projects unify on 12.1.1.
2. **A FluentValidation 12 compatibility audit** of all five validator files (§7).
3. *(Proposed — §6, needs a decision)* **`Filters/ValidationFilter.cs`** — one endpoint filter
   replacing the twelve copies of the same three-line validation block, and declaring the 400
   response to OpenAPI, which no endpoint currently does.
4. *(Proposed — §9.2, needs a decision)* **`ScrapeValidatorTests.cs`** — closing a test gap left by
   Phase 8.5.1.

**No database migration. No frontend change. No change to any HTTP response shape.**

---

## 3. Current state — verified

### 3.1 Every endpoint already does manual validation

The wiring this phase is supposed to "move to" is already there. All twelve mutating endpoints
follow one identical shape, e.g. [`RecipesEndpoints.cs:36-48`](../backend/RecipeApp.API/Endpoints/RecipesEndpoints.cs#L36-L48):

```csharp
group.MapPost("/", async (
    CreateRecipeRequest request,
    RecipeService svc,
    IValidator<CreateRecipeRequest> validator) =>
{
    var validation = await validator.ValidateAsync(request);
    if (!validation.IsValid)
        return Results.ValidationProblem(validation.ToDictionary());

    var created = await svc.CreateAsync(request);
    return Results.Created($"/api/v1/recipes/{created.Id}", created);
})
```

Injecting `IValidator<T>` and calling it explicitly *is* the pattern the FluentValidation authors
recommend. Distribution of the twelve:

| File | Endpoints validating |
|---|---|
| `Endpoints/IngredientsEndpoints.cs` | 2 (POST, PUT) |
| `Endpoints/MealPlanEndpoints.cs` | 4 |
| `Endpoints/RecipeScrapeEndpoints.cs` | 2 (`/scrape`, `/scrape/confirm`) |
| `Endpoints/RecipesEndpoints.cs` | 2 (POST, PUT) |
| `Endpoints/ShoppingListEndpoints.cs` | 2 |

Only `RecipeScrapeEndpoints` threads a `CancellationToken` into `ValidateAsync`; the other ten call
the single-argument overload. Not a bug — just an inconsistency the filter in §6 would settle.

### 3.2 The five validator files

| File | Validators | Notable constructs |
|---|---|---|
| `IngredientValidators.cs` | `CreateIngredientValidator`, `UpdateIngredientValidator` | `InclusiveBetween`, `.When()`, shared static rule constants |
| `MealPlanValidators.cs` | 4 request validators | `Must()`, `.When()` |
| `RecipeValidators.cs` | 4 | `RuleForEach().SetValidator(new ...)`, `RuleFor(x => x).Custom()`, `ctx.AddFailure()` |
| `ScrapeValidators.cs` | 3 | `RuleForEach().SetValidator()`, two `.Custom()` blocks |
| `ShoppingListValidators.cs` | 2 | `MaximumLength`, `.When()` |

Child validators are constructed with `new`, not resolved from DI — so DI lifetime changes cannot
affect them.

### 3.3 The version skew is live on this branch

The Phase 8.5.x package refresh bumped the **test** project to FluentValidation 12.1.1 but left the
API on the 11 line, because `FluentValidation.AspNetCore` pins it there:

| Project | Resolves `FluentValidation` to |
|---|---|
| `RecipeApp.API` | 11.11.0 (transitive, via `FluentValidation.AspNetCore` 11.3.1) |
| `RecipeApp.Tests` | **12.1.1** (explicit `PackageReference`) |

`FluentValidation.dll` in the test output directory is `12.1.1`. Because the test project
references the API project, the API's validators — compiled against 11.11.0 — are **executed
against 12.1.1** in every test run. All 523 tests pass, so nothing is actually broken, but the
suite is not testing the assembly that ships. This phase closes that.

> The comment above the `FluentValidation` reference in `RecipeApp.Tests.csproj` still says the
> reference exists "to lock the major version", which is now the opposite of what it does. It
> should be corrected or deleted as part of this work.

### 3.4 There are no MVC controllers

Confirmed by search: no `ControllerBase`, no `AddControllers()`, no `[ApiController]` anywhere in
`backend/`. This is the fact that makes the migration trivial rather than risky.

---

## 4. Verification spike — already run

Rather than reason about FluentValidation 12's breaking changes in the abstract, the migration was
performed against the working tree and measured, then reverted. Results:

| Check | Result |
|---|---|
| `dotnet build RecipeApp.API -t:Rebuild` | **0 errors, 0 warnings** |
| Full backend suite | **523 passed, 0 failed, 0 skipped** |
| `dotnet list package --deprecated` (both projects) | clean |
| `dotnet list package --vulnerable --include-transitive` | clean |
| Resolved `FluentValidation` | 12.1.1 in **both** projects |

Zero compiler warnings is the significant number: it means no validator in this codebase touches an
API that FluentValidation 12 marked obsolete. §7 records the specific surfaces that were checked.

This does not make the migration risk-free — see §12 — but it does mean the spec below describes a
change that has already been demonstrated to work, not one that is hoped to.

---

## 5. Workstream 1 — Package swap

### 5.1 `backend/RecipeApp.API/RecipeApp.API.csproj`

```diff
-    <PackageReference Include="FluentValidation.AspNetCore" Version="11.3.1" />
+    <PackageReference Include="FluentValidation" Version="12.1.1" />
+    <PackageReference Include="FluentValidation.DependencyInjectionExtensions" Version="12.1.1" />
```

Both are now declared explicitly. This is the point of the change: the API's dependency on
FluentValidation stops being an accident of a deprecated package's dependency list and becomes a
stated fact, versioned deliberately alongside everything else in the file.

### 5.2 `backend/RecipeApp.Tests/RecipeApp.Tests.csproj`

The `FluentValidation 12.1.1` reference stays, but its comment is now wrong and must be rewritten —
FluentValidation is no longer arriving transitively from `FluentValidation.AspNetCore`, and the
reference is no longer "locking a major version" against a lower one. Suggested replacement:

```xml
<!-- FluentValidation.TestHelper is bundled in the main FluentValidation package.
     Kept explicit and pinned to the same version as RecipeApp.API so the suite
     tests the assembly that actually ships. -->
<PackageReference Include="FluentValidation" Version="12.1.1" />
```

### 5.3 `Program.cs` — no code change

```csharp
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
```

stays exactly as written. It resolves to the same extension method, now from a directly-referenced
package instead of a transitively-supplied one.

**Lifetime is unchanged.** Verified against the shipped XML documentation of
`FluentValidation.DependencyInjectionExtensions` 12.1.1: the `lifetime` parameter is still
*"The lifetime of the validators. The default is scoped (per-request in web applications)"* — the
same default as 11.x. No registration audit is needed.

---

## 6. Workstream 2 — Validation endpoint filter *(proposed — decision required)*

### 6.1 The case for it

Twelve endpoints repeat these three lines verbatim. That repetition is *why* auto-validation
packages exist; the right answer in Minimal API is not an MVC-style filter that hides the step, but
an **endpoint filter** — explicit at the route, declared in the OpenAPI document, written once.

It also fixes two smaller things:

- **OpenAPI is currently silent about 400.** No endpoint in the project declares
  `.ProducesValidationProblem()`, so the Scalar docs at `/scalar/v1` do not document the validation
  response at all. The filter is the natural place to attach it.
- **The `CancellationToken` inconsistency** noted in §3.1 disappears — the filter always passes
  `HttpContext.RequestAborted`.

### 6.2 `Filters/ValidationFilter.cs` (new)

```csharp
using FluentValidation;

namespace RecipeApp.API.Filters;

/// <summary>
/// Validates the endpoint's <typeparamref name="TRequest"/> argument and short-circuits with an
/// RFC 7807 validation problem when it fails. Replaces the hand-copied validate-and-return block
/// that previously opened every mutating endpoint.
/// </summary>
public class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Endpoint has no argument of type {typeof(TRequest).Name} to validate.");

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);

        return result.IsValid
            ? await next(context)
            : Results.ValidationProblem(result.ToDictionary());
    }
}

public static class ValidationFilterExtensions
{
    /// <summary>Validates <typeparamref name="TRequest"/> and documents the 400 response.</summary>
    public static RouteHandlerBuilder WithValidation<TRequest>(this RouteHandlerBuilder builder)
        where TRequest : class
        => builder
            .AddEndpointFilter<ValidationFilter<TRequest>>()
            .ProducesValidationProblem();
}
```

### 6.3 Resulting endpoint

```diff
 group.MapPost("/", async (
     CreateRecipeRequest request,
-    RecipeService svc,
-    IValidator<CreateRecipeRequest> validator) =>
+    RecipeService svc) =>
 {
-    var validation = await validator.ValidateAsync(request);
-    if (!validation.IsValid)
-        return Results.ValidationProblem(validation.ToDictionary());
-
     var created = await svc.CreateAsync(request);
     return Results.Created($"/api/v1/recipes/{created.Id}", created);
 })
+.WithValidation<CreateRecipeRequest>()
 .WithSummary("Create a new recipe");
```

`using FluentValidation;` can then be dropped from all five endpoint files.

### 6.4 One implementation detail to confirm with a test

Validators are registered **scoped**, and the filter takes `IValidator<TRequest>` as a constructor
dependency. `AddEndpointFilter<TFilterType>()` builds its filter instance per request from
`HttpContext.RequestServices`, not from the root provider, so a scoped validator resolves correctly
and no captive dependency is created.

This must be **proved by an integration test, not assumed** — a POST with an invalid body returning
400 through the filter is sufficient, since a root-provider resolution would throw instead. If it
ever does throw, the bulletproof alternative is to drop the constructor injection and resolve
inside `InvokeAsync`:

```csharp
var validator = context.HttpContext.RequestServices.GetRequiredService<IValidator<TRequest>>();
```

### 6.5 If this workstream is rejected

The migration in §5 stands entirely on its own. The twelve blocks keep working unchanged against
FluentValidation 12 — the spike proved it. This is a tidiness improvement bundled into a phase that
is already touching validation, not a prerequisite.

---

## 7. FluentValidation 12 compatibility audit

Every surface this codebase touches, checked against 12.1.1:

| Surface | Used in | Status in 12.1.1 |
|---|---|---|
| `AbstractValidator<T>`, `RuleFor`, `RuleForEach` | all five validator files | Present |
| `NotEmpty`, `MaximumLength`, `GreaterThan`, `InclusiveBetween`, `Must`, `When`, `WithMessage` | all | Present |
| `RuleForEach(...).SetValidator(new ...)` | `RecipeValidators`, `ScrapeValidators` | Present |
| `RuleFor(x => x).Custom((req, ctx) => ...)` + `ctx.AddFailure(name, msg)` | `RecipeValidators`, `ScrapeValidators` | Present |
| `IValidator<T>` / `ValidateAsync` | all twelve endpoints | Present |
| **`ValidationResult.ToDictionary()`** | all twelve endpoints | Present — confirmed in shipped XML docs |
| `AddValidatorsFromAssemblyContaining<T>()` | `Program.cs:29` | Present, **default lifetime still Scoped** |
| `FluentValidation.TestHelper` (`TestValidate`, `ShouldHaveValidationErrorFor`) | `Tests/GlobalUsings.cs`, four test files | Present, still bundled in the main package |

### 7.1 Target framework narrowing — not a problem here

FluentValidation 11.11.0 shipped `netstandard2.0`, `netstandard2.1`, `net5.0`, `net6.0`, `net7.0`,
`net8.0`. **12.1.1 ships `net8.0` only.**

Both projects target `net10.0`, so this is a non-issue — but it is worth recording, because it is
the one change that would bite if any part of the solution were ever multi-targeted or shared with
a `netstandard2.0` library. Nothing here is.

### 7.2 Obsolete APIs

Zero warnings on a full rebuild of `RecipeApp.API` against 12.1.1 (§4). Had any validator used a
surface obsoleted between 11 and 12, it would have produced a `CS0618`/`CS0619`. None did.

---

## 8. Response shape must not change

The validation response contract is load-bearing and asserted by existing tests. Phase 7 already
corrected these tests to expect RFC 7807 `400`, per CLAUDE.md.

`Results.ValidationProblem(IDictionary<string, string[]>)` is an **ASP.NET Core** API, not a
FluentValidation one. It is untouched by this phase. The only FluentValidation-owned half is
`ValidationResult.ToDictionary()`, whose documented contract in 12.1.1 is unchanged: *"a dictionary
keyed by property name where each value is an array of error messages associated with that
property."*

Tests that must stay green **without modification** — if any needs editing, the migration is wrong:

- `Endpoints/IngredientsEndpointTests.cs:131`, `:164`
- `Endpoints/MealPlanEndpointsTests.cs:79`
- `Endpoints/RecipesEndpointTests.cs:126`, `:140`

Treating these as immovable is the single best regression check this phase has.

---

## 9. Test changes

### 9.1 Existing tests

Expected: **none**. The spike ran the full suite green with no edits. Any required edit is a signal
to stop and re-examine.

The four existing validator test files (`IngredientValidatorTests`, `MealPlanValidatorTests`,
`RecipeValidatorTests`, `ShoppingListValidatorTests`) use `FluentValidation.TestHelper` via
`GlobalUsings.cs` and are unaffected.

### 9.2 A gap worth closing *(proposed — decision required)*

There are **no dedicated tests for the scrape validators**. `ScrapeRecipeRequestValidator`,
`ScrapeConfirmIngredientValidator` and `ScrapeConfirmRequestValidator` have no
`Validators/ScrapeValidatorTests.cs`, even though Phase 8.5.1 specifically added the measurement
unit whitelist to `ScrapeConfirmIngredientValidator` — a rule that guards the LLM import path and
is currently only exercised indirectly.

Given Phase 9 imports recipes in bulk through exactly this path, adding that file here is cheap
insurance. Proposed coverage:

- `ScrapeRecipeRequestValidator` — rejects empty, non-absolute, and non-http(s) URLs; accepts http and https
- `ScrapeConfirmIngredientValidator` — the `IngredientId`-absent branch requires `NewIngredientName`,
  `NewIngredientDisplayName` and `Category`; the unit whitelist rejects a non-canonical unit and
  accepts each `MeasurementUnit.All` entry; `SourceAmount`/`SourceUnit` bounds
- `ScrapeConfirmRequestValidator` — out-of-range `Steps[].IngredientIndexes`; `StepNumber < 1`;
  empty instruction; empty ingredients list

### 9.3 New tests if §6 is accepted

- One integration test per HTTP verb shape proving the filter returns 400 with the same
  `ValidationProblemDetails` body as before (this is also the §6.4 scoped-resolution proof).
- A test that a **valid** request still reaches its handler — guarding against a filter that
  short-circuits unconditionally.

---

## 10. Documentation updates

- **`CLAUDE.md`** — the tech stack table row for Validation currently reads
  "FluentValidation | One validator class per request DTO". Extend to record that validators are
  invoked explicitly and that `FluentValidation.AspNetCore` is deliberately not used, so nobody
  re-adds it reaching for auto-validation. Add a Notes entry for Phase 8.5.2.
- **`specs/phase-8.5.1-revisions.md` §12** ("Further revisions") — cross-reference this phase.
- **`SPEC.md`** — no change; validation rules themselves are unaffected.

---

## 11. Out of scope

- **Changing any validation rule.** This phase moves packages, not behaviour. If a rule is wrong,
  it is a separate change.
- **`ShoppingListValidators` free-text `Unit`.** `AddCustomItemRequest.Unit` and
  `UpdateItemRequest.Unit` validate as `MaximumLength(20)` rather than against
  `MeasurementUnit.IsValid`. This looks like an inconsistency with the CLAUDE.md measurement rule,
  but custom shopping-list items are deliberately free-form — a user writing "bunch" or "packet" is
  the intended behaviour. Flagged here so it is not mistaken for an oversight; **not changed**.
- **Frontend.** Nothing consumes FluentValidation.
- **Database.** No model change, no migration.
- **The `xUnit1051` analyzer warnings** (474 of them, pre-existing and unrelated). Worth its own
  cleanup, not this one.

---

## 12. Risk and rollback

**Risk: low.** The whole change is a package swap already demonstrated green (§4), against a
codebase that never used the removed package's functionality (§1.2, §3.4).

The residual risks are the ones a clean compile cannot catch:

| Risk | Mitigation |
|---|---|
| A runtime-only behaviour change in FluentValidation 12 (message formatting, rule ordering, `.When()` evaluation) | 523 tests including four validator test files; §8's five untouchable response-shape assertions |
| §6 filter resolves a scoped validator from the root provider | Proved or disproved by the §6.4 integration test; documented fallback |
| §6 filter fails to find its argument on some endpoint shape | The `InvalidOperationException` is deliberate and fails loudly at first request rather than silently skipping validation |

**Rollback:** revert the two `PackageReference` lines. If §6 shipped, revert its commit
separately — it is intentionally a distinct commit from §5 so the package swap and the refactor can
be unwound independently.

---

## 13. Open questions for review

1. **Adopt the validation endpoint filter (§6), or leave the twelve blocks as they are?**
   *Recommendation: adopt.* It removes real duplication, fixes the undocumented 400 in OpenAPI, and
   settles the `CancellationToken` inconsistency — and it is the version of "manual validation
   wiring" worth having. But it touches all five endpoint files, so it is a bigger diff than the
   migration itself and is entirely optional.
   - I agree, adopt.

2. **Add `ScrapeValidatorTests.cs` (§9.2) now, or defer to Phase 9?**
   *Recommendation: now.* Phase 9 drives bulk imports through these exact validators, and 8.5.1
   added a rule there that has no direct test.
   - Now

3. **Pin to 12.1.1, or float to `12.*`?**
   *Recommendation: pin.* Every other reference in both csproj files is exactly pinned; the one
   floating range in the repo (`FluentValidation 11.*`) is what produced the skew in §3.3.
   - pin is fine

4. **Should this phase also address the 474 `xUnit1051` warnings?**
   *Recommendation: no* — unrelated to validation, and large enough to deserve its own scope.
   - not at this time.

---

## 14. Definition of Done

- [ ] `FluentValidation.AspNetCore` removed from `RecipeApp.API.csproj`
- [ ] `FluentValidation` 12.1.1 and `FluentValidation.DependencyInjectionExtensions` 12.1.1
      referenced explicitly by `RecipeApp.API`
- [ ] `dotnet list package --deprecated` clean on **both** projects
- [ ] `dotnet list package --vulnerable --include-transitive` clean on both projects
- [ ] Both projects resolve `FluentValidation` to the **same** version (§3.3 skew closed);
      `FluentValidation.dll` in the test output is that version
- [ ] Stale comment in `RecipeApp.Tests.csproj` corrected (§5.2)
- [ ] `dotnet build -t:Rebuild` on the API: 0 errors, **0 warnings**
- [ ] Full backend suite green with **no edits** to the five assertions listed in §8
- [ ] *(if §6 accepted)* `Filters/ValidationFilter.cs` added; all twelve endpoints converted;
      `using FluentValidation;` removed from all five endpoint files
- [ ] *(if §6 accepted)* Integration test proves scoped validator resolution through the filter
      (§6.4) and that valid requests still reach their handler
- [ ] *(if §6 accepted)* `/scalar/v1` documents the 400 validation response on all twelve endpoints
- [ ] *(if §6 accepted)* Package swap and filter refactor are **separate commits**
- [ ] *(if §9.2 accepted)* `Validators/ScrapeValidatorTests.cs` added with the coverage listed
- [ ] `CLAUDE.md` tech stack row and Notes updated (§10)
- [ ] Frontend suite untouched and green
