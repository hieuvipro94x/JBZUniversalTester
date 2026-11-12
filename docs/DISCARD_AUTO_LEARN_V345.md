# Learn the discard sensor and update THT (V2026.09.345)

The operator connects the sensor to two available IO on the configured D2XX
cards, opens Settings / I/O and chooses TU NHAN IO THUNG LOI. With the product
removed, establish a clear baseline, pass a hand/object through the sensor, then
remove it. A stable unique pair plus a confirmed release fills the two fields.
97/98 are examples, never fixed pins.

Save Settings, then explicitly select a THT. Files without DISCARD receive its
two pins (1/2) and a connector pin-count of two. The native CP949/MFC editor
changes only those tables inside OLE Contents, preserves remaining text and
binary metadata/other streams, checks the temporary result, then atomically
replaces the THT with an exact original backup in ThtBackups. The product catalog
excludes this backup folder and in-progress editor files.

If a THT already has a valid DISCARD pair, show the existing-configuration notice
with its own IO numbers and preserve that file byte-for-byte, even if the newly
learned station pair differs. Do not claim that this configuration notice is a
physical NG confirmation. Incomplete DISCARD definitions are rejected rather
than silently overwritten. Two blank fields disable rewriting without removing
existing file configuration. Startup and ordinary parser reads stay read-only.

Learning observes the existing topology-learning/background D2XX scan and its
Probe classifier; it never creates another device owner/reader. Duplicate,
corrupt, incomplete, stale-generation, Probe and ambiguous multi-pair frames
cannot complete learning. If the stream cannot prove both IO, do not guess.
Cancellation preserves previous settings and restores the scan before applying
new fields. Learning never enters product/discard interlocks, increments
production or fires relays. Occupied product IO and IO outside configured card
capacity are rejected. Existing Settings I/O permissions apply.

Tests use temporary native THT/CFG files, simulated frames and real WPF
rendering. Copies of two original V1.110 THTs were checked without editing the
originals; unrelated OLE stream bytes were preserved. No SQLite/history migration
is involved. Actual sensor wiring, scan traces and speaker output still require
a station hardware test.
