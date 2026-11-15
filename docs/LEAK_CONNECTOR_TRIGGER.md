# Leak connector installation trigger

- Each enabled Leak channel uses its configured THT connector.
- A connector with an eligible RET/RT-number network still requires that RET network to be connected correctly.
- A connector without RET/RT may start Leak when at least one direct, expected THT IO connection exists to any other connector. That other connector does not need RET/RT or an enabled Leak channel.
- A partial connection on a multi-endpoint network is sufficient for the fallback, but never constitutes full product PASS. Wrong edges, ignored IO and probe contacts do not qualify. The production start gate still rejects wiring faults and contact instability.
- A no-RET connector with no expected connection to another connector is a configuration error. No new channel mapping or topology is inferred.
- Leak-only retest waits until all qualifying edges of the no-RET connector are absent before accepting a reconnect. RET connectors retain their existing removal rule. Full product removal remains independently required before re-arming production.
- Channels execute individually through the existing Leak COM lifecycle; a passed channel is not repeated in the same cycle.
- Leak COM runs independently while D2XX keeps scanning. The continuity table remains live during initial Leak and retries; opening Leak does not clear its rows.
- V2026.09.416: wait for every enabled channel's configured connector trigger before starting. Send one existing TEST command with all enabled channel bits, including nonconsecutive CH1/CH3 configurations. Do not run channels one by one.
- On failure, wait for removal/refitting of the failed connector(s); other connectors may remain fitted. Before retry, all enabled connectors must be fitted. Retry measures every enabled channel again, including channels that previously passed.
- Leak qualification comes from one full-group PASS, never a combination of partial PASS results from separate attempts. Removing any qualified connector before final product PASS invalidates the entire group's qualification.
- The same grouping applies to good Master Leak. Single-channel models retain their existing flow. Final production PASS still requires full continuity and all other configured checks.
