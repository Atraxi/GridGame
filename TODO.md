# TODO

Follow-ups deliberately left out of the sync / auth / end-game work, roughly in priority order.

## Bugs found along the way

- **Seats are assigned by join order and only held in memory.** `GameSession.PlayerNumbers` is rebuilt from
  scratch whenever a session is loaded from the database. Once every connection to a game has dropped, whoever opens it
  first next time becomes player 1, whoever held that seat before. Seat ownership (user id → player number) needs
  persisting per game, e.g. a `GameSeat` table. `GridGame.Players` / the `AspNetUsers.GridGameId` shadow FK doesn't fit:
  it limits a user to one game.
- **`GamesPlayed` / `GamesWon` / `GamesLost` are never updated.** This depends on persisted seats (above). With seats
  in place, `EndGame` in `GridGameHub` can use `GetFinalScores`' winner rule: the highest score among players who
  didn't resign.
- **Progress is persisted once per completed round.** `ApplyMove` only saves to the database when a full round
  completes, or when the last connection leaves. If the free App Service plan unloads or restarts the app mid-round,
  moves since the last save are lost. Consider saving on every turn end; it's a single-row update.

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
- REST calls outside route loaders (`GameList`, `StartGameDialog`, `CreateMapPage`) show a SessionExpiredError as a
  plain error message rather than redirecting to the login page.
