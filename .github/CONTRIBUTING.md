# Contributing

Issues and pull requests are welcome. For anything bigger than a small fix, open an issue first so we can agree the approach.

## Branches

- `main` holds both Umbraco lines (package 17.x on CMS 17.8+, 18.x on CMS 18.3+) in one codebase. `-p:UmbracoMajor=17` or `18` (default 18) selects the line; `eng/lines.txt` lists them. Code for one line only sits behind `#if UMBRACO_17`.
- Open pull requests against `main`. A change must pass on both lines: run `./scripts/test.sh`.

## Build and test

