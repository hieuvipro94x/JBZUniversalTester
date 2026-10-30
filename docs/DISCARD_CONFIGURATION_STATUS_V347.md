# Discard configuration status (V2026.09.347)

Explicit model selection and automatic startup restoration both notify the
operator about the loaded model's discard configuration, including the absence
of DISCARD. The same presentation is bound permanently on MainWindow.

Green identifies a valid pair and its actual IO. Amber identifies a model with
no discard IO and explains the Settings learning workflow. Red identifies an
incomplete, duplicated or out-of-system pair; it must not be described as valid.
With no selected model, MainWindow clears the previous model's status.

Startup notices are deferred until rendering is possible and guarded by the
model-load generation, current model reference and shutdown state. A stale
startup restore cannot notify over a newer explicit selection.

These are configuration notices, not physical sensor confirmations. They do
not enable a missing interlock, change the THT, count production, acknowledge NG,
or fire relays. Existing native-THT preservation and history rules remain intact.
