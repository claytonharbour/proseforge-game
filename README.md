# ProseForge Game

A small idle-game engine with swappable theme packs. It's the code that goes with the book
*A Claude Agent and a Codex Agent Walk Into a Room...*, by Clayton Harbour.

A game is a production line: stations that make something, hand it on, and earn when it sells.
The engine knows nothing about monks, elves or post offices. Each world is a folder under
`themes/`: `gameplay.json` holds the rules, and `theme.json` holds the words, colours, pictures and voices.

## This repository follows the book

Each chapter that changes the code has a tag named for what it adds, such as `first-playable` or `themes`.
Check out a tag to see the code as that chapter describes it, or compare two:

```sh
git checkout themes
git diff first-playable themes
```

## Run the tests

You need the .NET 10 SDK. The tests and the theme checker run without any phone tooling:

```sh
make test          # the rules tests, then a check of every theme pack
make theme-check   # check the packs only
make coverage      # line coverage of the logic (needs: dotnet tool install -g dotnet-coverage)
```

Building the phone app needs the .NET MAUI workloads for Android or iOS.

## Make your own world

Copy a pack folder under `themes/`, change its words and numbers, and run `make theme-check`.
`make theme-guide` draws the art guide that backgrounds are painted against, and
`make art-check ART=path/to/background.png` lays it over a picture so you can see what the screen covers.
The book's "Make It Yours" chapter walks through it.

## What isn't here

The guild and Santa worlds are included as rules and words only (`theme.json` and `gameplay.json`),
so the game runs and you can see a second and third example of the format. Those files are
Copyright Clayton Harbour, all rights reserved, and aren't licensed for reuse; their art and voices
ship only in the app, so here the game shows plain stand-ins for them. The app icon and splash are stand-ins
too. To make your own world, start from the Village Bakery or Rocket Yard pack, which are CC BY 4.0.

The code, art, words and voices made by the project's AI agents are Clayton Harbour's; the code is
Apache 2.0, and the Village Bakery and Rocket Yard packs (art, voices, words and rules) are CC BY 4.0. Each pack's `CREDITS.md` says who
made each file and how.
