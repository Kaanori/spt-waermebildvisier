# Changelog

## 1.2.0 — 2026-11-12

- New: fixes see-through chain-link fences in thermal vision (e.g. the blue construction fence on Factory). They turned into solid walls because of a game shader bug ("p0/Reflective/Bumped Specular SMap Transparent Cutoff" sets alpha to 1 before the alpha test). While a thermal camera renders (goggles and scope camera), affected materials temporarily use the correct cutout shader, then switch back. Setting: "Fix see-through fences in thermal (game shader bug)", on by default (`ZaunFix.cs`).

## 1.1.0 — 2026-11-03

- First release as a standalone repository (SPT 4.1.6).
