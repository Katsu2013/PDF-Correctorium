# Material Symbols icon attribution

The `link` and `picture_as_pdf` paths in
`src/PdfCorrectorium.App/Resources/VectorIcons.xaml` are derived from
Google Material Symbols (outlined, 24px, weight 400).

- Upstream: https://github.com/google/material-design-icons
- Source revision: `bd8cb85bd4bad964fe6918f79665bb40c3a8efef`
- License: Apache License 2.0; a copy is included in `LICENSE` in this folder.
- Changes by PDF Correctorium: normalized the link to a 24-by-24 canvas,
  added a plus badge with a transparent clearance, and isolated/scaled the
  PDF letters into a custom portrait file outline. Other approved simple
  shapes are drawn specifically for this application's icon system.

Source paths at the upstream revision:

- `symbols/web/link/materialsymbolsoutlined/link_24px.svg`
- `symbols/web/picture_as_pdf/materialsymbolsoutlined/picture_as_pdf_24px.svg`

## Additional operation icons (dev.210)

`MaterialActionIcons.xaml` contains only the individual outlined SVG contours needed
for these operations, not an icon font. They use the same pinned Google upstream
revision and Apache License 2.0 above. A matrix normalizes the original
`0 -960 960 960` viewport to 24 × 24; paths are rendered as filled outlines.
`CommentsAdd` subtracts a transparent clearance and adds the app's simple plus.

| App key(s) | Upstream source at the pinned revision |
| --- | --- |
| `Reset` | `symbols/web/restart_alt/materialsymbolsoutlined/restart_alt_24px.svg` |
| `Apply` | `symbols/web/check/materialsymbolsoutlined/check_24px.svg` |
| `Deselect` | `symbols/web/deselect/materialsymbolsoutlined/deselect_24px.svg` |
| `SelectAll` | `symbols/web/select_all/materialsymbolsoutlined/select_all_24px.svg` |
| `ReviewList` | `symbols/web/checklist/materialsymbolsoutlined/checklist_24px.svg` |
| `Replace` | `symbols/web/swap_horiz/materialsymbolsoutlined/swap_horiz_24px.svg` |
| `Keep` | `symbols/web/shield/materialsymbolsoutlined/shield_24px.svg` |
| `Comments` | `symbols/web/chat_bubble/materialsymbolsoutlined/chat_bubble_24px.svg` |
| `Thumbnail` | `symbols/web/grid_view/materialsymbolsoutlined/grid_view_24px.svg` |
| `Eyedropper` | `symbols/web/colorize/materialsymbolsoutlined/colorize_24px.svg` |
| `Info`, `DocumentProperties` | `symbols/web/info/materialsymbolsoutlined/info_24px.svg` |
| `Restore` | `symbols/web/history/materialsymbolsoutlined/history_24px.svg` |
| `Redo` | `symbols/web/redo/materialsymbolsoutlined/redo_24px.svg` |
| `Warning` | `symbols/web/warning/materialsymbolsoutlined/warning_24px.svg` |
| `Undo` | `symbols/web/undo/materialsymbolsoutlined/undo_24px.svg` |
| `Validate` | `symbols/web/verified_user/materialsymbolsoutlined/verified_user_24px.svg` |
| `Edit` | `symbols/web/edit/materialsymbolsoutlined/edit_24px.svg` |
| `Exit` | `symbols/web/logout/materialsymbolsoutlined/logout_24px.svg` |
| `Settings` | `symbols/web/settings/materialsymbolsoutlined/settings_24px.svg` |
| `Help` | `symbols/web/help/materialsymbolsoutlined/help_24px.svg` |
| `MultiCharacterInput` | `symbols/web/text_fields/materialsymbolsoutlined/text_fields_24px.svg` |
| `Propagate` | `symbols/web/content_copy/materialsymbolsoutlined/content_copy_24px.svg` |
| `Unreviewed` | `symbols/web/radio_button_unchecked/materialsymbolsoutlined/radio_button_unchecked_24px.svg` |
| `Excluded` | `symbols/web/block/materialsymbolsoutlined/block_24px.svg` |
| `Analysis` | `symbols/web/bar_chart/materialsymbolsoutlined/bar_chart_24px.svg` |
| `Search` | `symbols/web/search/materialsymbolsoutlined/search_24px.svg` |

Stop, Pending, PageExtract, PdfSplit, PageNumber, Header, Footer, Decoration,
TextWatermark, HorizontalWriting, VerticalWriting and ImageOptimize are original
simple vector drawings. OpenFolder reuses the existing approved folder without its
PDF/project badge. These app-specific drawings are not claimed to be upstream SVGs.

