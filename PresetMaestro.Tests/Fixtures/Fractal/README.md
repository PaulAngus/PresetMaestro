# FM9 replay fixture

`fm9-12-slot-000.hex` contains the ten received stored-preset SysEx frames captured read-only on 5 October 2026 from the connected FM9 running 12.00, stored slot 0 (`59 Bassman Tweed 5F6-A`). The companion JSON is the expected decoded preset from that full scan. The source transaction is retained under ignored `build_test/hardware/20261005-065625-42f600d8bd044b1cb0ca24916fd8423b/trace.jsonl`.

The test feeds these bytes into the production protocol client and decoder; it does not contact a device. Fingerprint/bypass mutation tests change a copy in memory. No IR/audio payload is included. The existing amp roster capture in `docs/catalog/fm9-12-device-roster.json` is linked into test output for the catalogue parser tests.
