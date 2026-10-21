# TODO

Follow-ups deliberately left out of the sync / auth / end-game work, roughly in priority order.

## Bugs found along the way

- **Unlocked seats are still assigned by join order and only held in memory.** A named player can now lock their
  seat (`GameSeat`, via the "Lock my seat" button), and locked seats survive the session being reloaded. An unlocked
  seat is still lost once every connection drops: whoever opens the game first next time takes the lowest free seat.
  Consider auto-locking a named player's seat on their first move. Guests can't lock, since a guest account can sit
  unused for up to the refresh-token lifetime before `GuestCleanup` frees it.
- **`GamesPlayed` / `GamesWon` / `GamesLost` are never updated.** This depends on persisted seats (above). Locked
  seats now give a user id -> player number mapping for named players. With that, `EndGame` in `GridGameHub` can use
  `GetFinalScores`' winner rule: the highest score among players who didn't resign.
- **Progress is persisted once per turn.** `ApplyMove` saves at the end of every turn (and the hub saves when the last
  connection leaves), so an app unload or restart loses at most the turn in progress. Saving every action would close
  that too, at the cost of a database write per action.

## End-game analysis (BoardAnalysis)

- **Jump-origin death isn't modelled.** A jump kills its origin tile, so a tile whose only exits are jumps in
  different directions can take one of them, never both:

  ```
  # # # # #
  . # P # .     P jumps left -> P's tile dies -> right side unreachable, and vice versa
  # # # # #
  ```

  The flood fill counts every exit as reachable. That can over-count reach, uncontested cells, Floor, Projected and
  Ceiling in narrow corridors. It errs in both directions. An opponent's inflated Ceiling makes Locked/Decided arrive
  late. A player's own inflated Floor/Projected can make them arrive early.

  A possible next step: find "articulation" tiles whose exits are all jumps. Then compute reach separately for each
  exit, and take the player's score bound as the **max** over those alternatives rather than their union. Doing that
  properly is a search over sequences of choices, not a single flood fill. It's probably only worth it if the
  indicative stats prove misleading in real games.
- **At-risk is conservative.** A route through a contested cell or a threatened tile counts as at-risk even when the
  cut would be only 1 tile wide, and so could still be jumped. Refining this means asking whether an opponent can
  create a 2-wide seal, which interacts with the point above.
- **Kill-for-kill trades are ignored.** "Decided" assumes threatened tiles roughly cancel out. There's no modelling of
  who wins an exchange of kills.
- The analysis is a named-account perk, consistent with final scores. That covers the table, the territory overlay
  and the `boardAnalysisUpdated` push. Everyone still gets `informallyDecidedFor`, so the resign offer isn't gated.
  Revisit this if the overlay should be for everyone.

## Sync / connection

- Pushes and acks are keyed by user. A spectator (unseated) never owes an ack, so a spectator who misses a push only
  catches up on the next push, or on reconnect.
- The unresponsive threshold is hard-coded in `SessionMonitor`: 3 prompts, 4s apart, then every 15s. Tune it after
  real play.

## Auth

- **Refresh-token reuse detection.** If a revoked token is presented again, that suggests theft; revoke the whole
  family. It's held back because concurrent renewals must never look like reuse: the Web Lock in `auth.ts` now
  serialises renewal across tabs, but separate browsers and devices each keep their own chain. `GuestCleanup` already
  keeps revoked rows for 30 days, which reuse detection would need.
- REST calls outside route loaders (`GameList`, `StartGameDialog`) show a SessionExpiredError as a plain error message
  rather than redirecting to the login page. `CreateMapPage` deliberately keeps the editor up instead, so an unsaved
  design isn't lost, and points to logging in again in another tab.
