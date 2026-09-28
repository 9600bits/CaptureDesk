# CaptureDesk 0.3.1 — interface and identity

Compact Windows utility, with restrained Organic Biophilic color and a capture-frame/leaf identity. MiSans 330/520 and Lucide outline controls remain unchanged. The UI/UX skill's web landing-page and typography suggestions do not apply to this desktop workflow.

- Main window: 620 × 420 default; screenshot as the single primary action, clipboard and history as secondary rows, recovery as a quiet utility action. No promotional claims or dashboard cards.
- Settings: pale green selection with a visible border; existing keyboard focus retained.
- Shared controls: semantic pressed colors in both themes, visible disabled primary actions, 5 px primary-button corners.
- Capture overlay: outlined confirmation action; all five capture/recognition actions remain visible.
- Identity: original image generated using codex-image2 / gpt-image-2, low quality. Requested 1024 × 1024; provider returned 1254 × 1254. Source retained in output/imagegen/capturedesk-icon-concept.png. tools/prepare-brand.py removes paper, flattens ink and creates a 256 px PNG and 16–256 px ICO. Ivory backing keeps the Windows icon readable on dark taskbars. PNG used in the app header, ICO embedded in executable, WPF windows and tray.

Validation: Release build, 16 Core tests, existing full --verify-ui regression suite, light/dark main-window snapshots and settings snapshot. Known pre-existing Windows OCR short-number omission remains documented by the verification suite.

This release does not implement an installer or change recognition dependencies.
