# Track end action: global default + future per-entry inheritance

## Status

Global default implemented (2026-09-13). Per-entry UI done (e01s01, 0.2.0).

## Resolution order (implemented)

`ShowController._defaultEndAction` (default `Advance`, changed via
`SetDefaultEndAction`, validated with `Enum.IsDefined`) participates in
`EffectiveSettings` as the last fallback:

```
entry.Override?.EndAction
  ?? (track.Defaults.EndAction != Advance ? track.Defaults.EndAction : global)
```

- Entry override always wins (existing behavior, unchanged).
- A non-default track default wins over the global (keeps existing
  `EffectiveSettingsTests` / persistence fixtures green).
- Otherwise the global applies. Fresh installs behave exactly as before
  (`Advance`).

## Surfaces

- Desktop Settings → "Воспроизведение" → ComboBox
  (Пауза/Стоп/Повтор/Далее), persisted in `AppSettings.DefaultEndAction`
  (nullable DTO field; legacy files fall back to `Advance`, never `Pause`).
- `SettingsViewModel` pushes the stored value to Core at startup and on
  every change (`SubmitEndAction`).
- Remote codec accepts `set_default_end_action` (`end_action` string,
  case-insensitive); no remote settings UI yet.

## Future work (recorded per user request)

- Per-track-entry setting in the playlist (row editor / overrides form):
  writes `PlaylistOverrides.EndAction`, inherits the global when the local
  value is not overridden (resolution above already supports this; only
  the UI is missing).
- Remote settings surface for the global default (codec is ready).
- `TrackDefaults.EndAction` stays model/persistence-compatible; do not
  repurpose it as the global.
