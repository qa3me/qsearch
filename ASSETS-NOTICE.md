# Assets notice

## Qsearch / QA3 styling

The Qsearch orbit mark in `assets/brand/qsearch-icon.svg` is the project-local
vector supplied and refined for Qsearch. The checked-in dark, light, and
multi-resolution Windows icons are generated from that vector and have a
transparent background. No stock logo or downloaded artwork is bundled.

The glass surfaces, fine grid, restrained green accent, charts, and information
cards follow the visual direction requested for QA3 Academy. No standalone QA3
brand-book file was available in this repository, so these choices are documented
as Qsearch's QA3-inspired product language rather than a complete QA3 brand
specification.

## Interface icons

Qsearch incorporates nine Font Awesome Free 7.3.1 icon geometries. Their license
and attribution are preserved under `assets/fontawesome` in source and under
`licenses/FontAwesome-Free` in binary packages. See
`THIRD-PARTY-NOTICES.md` for the complete redistribution notice.

## Fonts

The interface can use separately licensed **Farhang2** for body text and
**Tahrir** for headings when they are installed locally. Those proprietary font
binaries and their private source archives are not included in the public
repository, Release builds, portable packages, or slim packages.

Release builds fall back to Windows-provided Segoe UI and Tahoma. These system
fonts are referenced by family name and are not redistributed by Qsearch.
