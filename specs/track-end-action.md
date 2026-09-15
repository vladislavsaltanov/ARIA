# Track end action: global default + per-entry inheritance

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
  case-insensitive); remote select shows the current value and sends
  changes back (e01s03, 0.2.0), two-way with desktop, no reload.

## Future work (recorded per user request)

- Per-entry UI done via playlist context menu (e01s01, 0.2.0):
  writes `PlaylistOverrides.EndAction`, Наследовать clears the local
  value back to the global.
- Remote settings surface done (e01s03, 0.2.0).
- `TrackDefaults.EndAction` stays model/persistence-compatible; do not
  repurpose it as the global.
