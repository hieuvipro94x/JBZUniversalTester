# Exclusive testing / PASS labels — V2026.09.314

The large status label previously mixed a bound `Run` and an animated `InlineUIContainer` inside one TextBlock. It now binds the entire status string to one dedicated TextBlock, with dots in a separate fixed-width grid column. Translation updates replace the complete status text; changing dots cannot rebuild or shift the status text's inline content.

The center-result host additionally collapses explicitly whenever the canonical status is testing. PASS remains spelled `PASS` in both the large and center labels, and the canonical Vietnamese state/trigger values remain unchanged.

WPF regression coverage repeatedly switches testing → PASS in Vietnamese and Korean, advances every dots frame, verifies non-overlapping bounds and stationary status text, checks that center PASS is hidden during testing, and confirms that PASS removes all testing text/dots. It renders actual Korean testing and PASS views for layout inspection and verifies that these view changes send no relay or scan commands. Test engine, product data, history and hardware timing are unchanged.
