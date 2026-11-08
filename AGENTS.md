# Working on this repository

This is a small idle-game engine with swappable theme packs, written in C# with .NET MAUI. It is the
code that goes with the book *A Claude Agent and a Codex Agent Walk Into a Room...*. A game is a
production line: stations make something, hand it on, and earn when it sells. Each world is a folder
under `themes/`.

These instructions are for any coding agent (Codex reads this file; Claude reads it through
`CLAUDE.md`). Change them to suit how you want your agents to work.

## Where things are

- `src/ProseForge.Game/Engine/`: the rules. Plain C#, no screens, so it can be tested on its own.
- `src/ProseForge.Game/Presentation/` and `ViewModels/`: what the screen shows, as plain classes.
- `src/ProseForge.Game/` (the rest): the MAUI app, the pages and the phone-specific code.
- `themes/<pack>/`: `gameplay.json` holds the rules and `theme.json` the words, colours, pictures and
  voices. `themes/index.json` lists the packs the app offers.
- `tests/`: the rules tests. `tools/ThemeCheck/` checks every pack.
- `docs/RELEASE.md`: building and releasing your own copy to the stores.

## Commands

You need the .NET 10 SDK. The tests and the theme checker need no phone tooling.

```sh
make test          # the rules tests, then a check of every theme pack
make theme-check   # check the packs only
make coverage      # line coverage of the logic (needs: dotnet tool install -g dotnet-coverage)
make build         # compile the rules and engine (everything the tests cover)
make run-mac       # build and open the game on a Mac
```

Building the phone app needs the .NET MAUI workload (`dotnet workload install maui`) and Android's or Apple's own tools.

## How to work here

- Run `make test` before you change anything, and again after. Report what it printed, not that it passed.
- Make one change at a time, small enough that a person can read the whole diff.
- When you fix a bug, add a test that fails without the fix, and say that you saw it fail.
- Keep the rules in `Engine/` free of screen code; that's what keeps them testable.
- A new or changed theme pack must pass `make theme-check`.
- Don't put passwords, signing keys or store credentials in the repository. Personal settings go in
  `.env.local` (start from `env.local.example`) and secrets in `.env`; Git ignores both.
- Ask before anything that costs money or can't be undone, such as uploading to a store.

## Licences

The code is Apache 2.0. The Village Bakery and Rocket Yard packs are CC BY 4.0, so they're the ones to
copy for your own world. The guild and Santa packs are rules and words only and aren't licensed for
reuse; see the README.
