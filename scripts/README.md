# Store publishing scripts

`make` runs these; you don't need to call them directly.

- `play/upload.py`: upload an Android App Bundle to Google Play. It defaults to a **draft on the
  internal testing track**, which reaches nobody. `--tracks` lists every track; `--check` proves your
  credential without uploading anything.
- `play/test_upload.py`: offline tests for the upload script's guards (`make check-play-scripts`).
- `asc/asc.py`: App Store Connect: builds, tester groups and their states (`make asc-status`).

Run them through `make` or their own `.venv/bin/python`, never by path: the shebang is the system
Python, which lacks their dependencies. Their settings come from the environment, which `make` fills
in for each command from `.env` (credentials) and `.env.local` (your app ID and key path). Don't
`export` the credentials into your shell.
