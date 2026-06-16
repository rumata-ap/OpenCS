# Fire parity fixtures exporter

This tool runs the GreenSectionPy fire thermal solver and exports JSON fixtures for parity checks in C# tests.

## Run

From the OpenCS repository root:

```bash
python tools/fire-parity/export_fixtures.py
```

## Output

Fixtures are written to:

- `tools/fire-parity/fixtures/beam_200x400_R60_3sided.json`
- `tools/fire-parity/fixtures/rectangle_200x400_5min_3sided.json`

The exporter uses GreenSectionPy from:

- `C:\Users\palex\devel\GreenSection\GreenSectionPy`
