# V2026.09.277 — responsive settings opening

V276 logs show a 103.549 ms UI stall immediately after opening Settings. Config/THT
preparation already runs on a worker; the remaining synchronous open builds and
lays out the entire WPF editor tree before returning to the dispatcher.

MainWindow now opens a lightweight settings shell, then prepares I/O, Relay/Leak,
Label and Resistance panels in separate dispatcher turns. Input and render work
can execute between layout batches. An opening cover hides intermediate reflow;
editors and Save become available together when all panels are ready. Back remains
available, and releasing the page prevents delayed preparation from reviving it.
COM/catalog discovery starts after editor preparation, using the existing worker
and generation guards. No device lifecycle, scan timing, manual output sequence,
configuration format, history or save/return behavior changes.

Diagnostics:
- `VIEW_BAML`: page component construction.
- `OPEN_VIEW`: constructor and initial window shell presentation, as measured by MainWindow.
- `OPEN_PANEL panel=...`: each synchronous panel layout batch.
- `OPEN_READY`: elapsed staged layout time, including dispatcher yields.

Self-tests exercise input callbacks between all four batches, repeated preparation,
1366/1920 widths, editor preservation and release during opening. These validate
responsiveness boundaries rather than assuming a machine-independent time limit.
Full opening/stall improvement must be confirmed with logs on the production PC;
cold WPF/template initialization and machine load can affect timings.
