# ART credits

| Asset | Author | Licence | URL | Used for |
| --- | --- | --- | --- | --- |
| M PLUS 1 Code (variable font, weight 700) | The M+ FONTS Project Authors | SIL Open Font License 1.1 | https://github.com/google/fonts/tree/main/ofl/mplus1code | Katakana/digit glyphs rendered into `Assets/Art/Textures/tex_glyphs.png` (glyph atlas: world SOURCE code, glyph rain, corrupt posters/screens) |
| Orbitron (variable font, weights 600-900) | The Orbitron Project Authors (Matt McInerney) | SIL Open Font License 1.1 | https://github.com/google/fonts/tree/main/ofl/orbitron | HALCYON wordmark in `tex_halcyon.png`; floor decal text in `tex_decal.png` |
| "Hash without Sine" | Dave Hoskins | MIT | https://www.shadertoy.com/view/4djSRW | Hash functions in `Assets/Art/Shaders/DesyncCommon.hlsl` |
| Barycentric wireframe (fwidth-based edge distance) | well-known technique (NVIDIA "Solid Wireframe" whitepaper, 2007) | technique, no code copied | https://developer.download.nvidia.com/SDK/10/direct3d/Source/SolidWireframe/Doc/SolidWireframe.pdf | WorldMat edges / SOURCE wireframe |

The fonts themselves are not shipped in the build; only the generated textures are (generator: `.work/art/gen_textures.py`, Python + Pillow). The HALCYON logo/poster is procedurally drawn by that script (no AI-generated art).
