# Games directory

Your personal game library belongs in this directory, with one folder per game. Personal game folders are intentionally ignored by Git.

Use the JSON files in [`Examples`](Examples) as starting points. Copy an example into a new game folder and rename it to `Game.json`.

```text
Games/
├── Examples/
└── My Game/
    ├── Game.json
    ├── Cover.png
    ├── Header.png
    └── snaps.mp4
```

Do not commit copyrighted artwork, videos, account identifiers, personal paths, or private launcher data.

## Custom collections and shelves

Copy `Config/collection.example.json` to `Config/collection.json` to give the
cart its own collection name, accent color, and ordered shelf list.

Place a game on a shelf by adding this optional block to its `game.json`:

```json
"collection": {
  "shelf": "3D Era",
  "order": 20
}
```

Games without a shelf use the collection's `defaultShelf`. Each game keeps its
real Steam, Rockstar, local executable, or other launch configuration; the
collection only changes how the library is presented.
