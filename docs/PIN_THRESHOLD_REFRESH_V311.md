# PIN threshold refresh — V2026.09.311

TestView cached the maintenance threshold from its last counter snapshot, so saving a different CFG threshold did not reliably update an already-loaded product.

The threshold now reads the active production settings. The shared settings-refresh path notifies the threshold, formatted count/threshold text and all maintenance-warning properties immediately. Counter snapshots update the accumulated count without replacing the active configured threshold, including when an older asynchronous snapshot arrives after settings changed.

The old timing-policy clamp silently changed valid thresholds below 1,000 or above 100,000,000 despite the editor allowing any positive integer. Threshold normalization now matches that editor rule and preserves the saved positive value.

Regression coverage uses an existing count of 220, changes the threshold from 200,000 to 150,000, applies a stale 200,000 snapshot, lowers/raises the warning boundary and checks the actual bound text. It verifies that the count is unchanged and no relay commands or scan restart are introduced. No production database/history migration is required.
