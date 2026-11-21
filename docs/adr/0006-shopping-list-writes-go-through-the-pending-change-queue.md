---
status: accepted
date: 2026-11-21
issue: "#45"
---

# Every Shopping List write goes through the Pending Change queue; last write wins

The Shopping List is used in a shop, where the signal comes and goes. Ticking Purchased and adding,
editing or deleting a Custom Item must work with no signal, survive a reload or a killed PWA, and
reach the server once the phone is back online.

**Every Shopping List write goes through one queue of Pending Changes on the phone, online or
offline.** A write updates the screen at once, is stored in IndexedDB, and is sent by the queue.
Online, the queue empties within milliseconds. There is no second path that calls the API
directly, so a new kind of list write is added to the queue (its kind, merge rule, request and
conflict rule in `utils/pendingChanges.js` and `stores/shoppingList.js`), never sent on its own.

**When two phones change the same item, the last write to reach the server wins.** There is no
versioning.

## Context

- Before this, each write called the API directly, and a failed save was reverted on screen or
  reported. Offline, every write failed.
- The List Refresh (#77) already keeps on screen any item with unsaved changes on this phone. A
  Pending Change is exactly such a change, for as long as it waits.
- The two phones sharing a list are one household's. A rare clash on one item is cheap to put right
  by hand, while versioning would touch every write endpoint and its storage.

## Considered options

**A. Queue only while offline, call the API directly while online.** *Rejected:* online and
offline have to be told apart, and `navigator.onLine` can't be trusted. Two paths drift apart, and
a write made just as the signal drops falls between them.

**B. Background Sync from the service worker.** *Rejected:* it isn't supported on every phone the
app runs on. It also moves the conflict rules, and the snackbars they raise, out of the app.

**C. One queue for every write, sent while the app is open. (Chosen.)** One path, one set of merge
and conflict rules. The cost is that writes made offline wait until the app is next open.

**D. Versioned items with server-side conflict detection.** *Rejected:* it would need a backend
change on every write and a merge UI for a clash that hardly ever happens.

## Consequences

- The screen never waits on the server. The edit sheet closes on Save, and a rejected change is
  reported afterwards by its conflict rule.
- Pending Changes for one item merge while they wait (a tick then an untick keeps the last value,
  and an add then a delete sends nothing), so the server sees each item's net change.
- An added Custom Item has a temporary id on this phone until its add is sent, and later changes
  for it wait behind the add.
- A write sent for the list of a Past Meal Plan gets 410 Gone, so changes made offline never land
  on a list a newer Meal Plan has replaced. They are dropped with a summary, never moved.
- Regenerating stays online-only and waits until the queue is empty, because it replaces every
  Generated Item.
- Two tabs on one phone share the queue in IndexedDB, and a Web Lock lets only one send at a time.
