---
status: accepted
date: 2026-11-17
issue: "#76"
---

# US Measurements scale on the device, then the API corrects their units

The recipe view gains a **Unit System** toggle, Metric | US. US reads mass in `oz`/`lb` and volume
on the `tsp`/`tbsp`/`cup` ladder, as kitchen fractions. The servings stepper and the ½×/1×/2× Batch
Size shortcuts change the servings many times a second while someone taps, and every change must
show at once.

We split the work. **The API owns the Metric→US conversion** (`MeasurementConverter.ToUs`). It
returns US Measurements on `GET /recipes/{id}?servings=&system=us` and on the lightweight
`GET /recipes/{id}/measurements`. **The device owns the scaling between corrections.** When the
servings change, it multiplies the US Measurements it already has, in their existing units (so it
shows "3 tsp"). About 1 s after the last change it asks the API for the measurements at the new
count and swaps them in place ("1 tbsp"). Only the latest request applies, superseded ones are
cancelled, and a failure leaves the on-device value with no error shown.

## Context

- `.claude/rules/measurements.md` puts all unit arithmetic in `MeasurementConverter`. The densities
  that decide whether a cup Source Measurement stored as grams still holds live on the Ingredient,
  server-side. The frontend has neither the conversion table nor the density.
- Metric scaling has always been on the device, with no server call. Metric must stay exactly as it
  is.
- A recipe opened at `?servings=` must paint in US straight away, not in Metric first.

## Considered options

**A. All on the device.** Mirror the conversion factors, the ladder and the Source Measurement
rules in JavaScript. *Rejected:* it is a second copy of the arithmetic the measurements rule keeps
in one place. It also can't check a cup source against the Ingredient's density, which the
frontend never receives.

**B. All on the server.** Fetch on every servings change and show nothing new until the answer
arrives. *Rejected:* each tap would wait on a round trip, and a burst of taps would send a burst of
requests.

**C. Hybrid. (Chosen.)** The device scales instantly, and the debounced API call corrects the units.
The ladder and the source rules have one home, and taps feel immediate.

## Consequences

- For up to a second after a change, a row can read in a unit the ladder would not choose ("3 tsp"
  before it becomes "1 tbsp"). The amount is already right.
- The API returns numbers only. One frontend formatter (`formatUsAmount`) writes both on-device
  and server values as fractions. `fromSource` tells the view the main line was read from a US
  Source Measurement, so it hides that source rather than repeat it.
- Offline, US mode keeps scaling on the device and never corrects, which is fine to cook from.
- Metric makes no new calls: its `GET /recipes/{id}` carries no query string, so its PWA cache entry
  is unchanged.
