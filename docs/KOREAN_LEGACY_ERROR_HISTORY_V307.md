# Korean daily history output — V2026.09.307

- Current runtime writes append-only PASS records to `C:\Pass\YearYYYY\MonthMM\DayDD.dat` (UTF-8 without BOM) and FAIL records to `C:\Error\YearYYYY\MonthMM\DayDD.err` (CP949 without BOM), using the result finish date.
- `C:\Pass_` and `C:\Error_` are legacy locations; this writer does not create or update them.
- Leak error messages previously passed Vietnamese pressure/leak descriptions directly to CP949, producing replacement `?` characters. Translate these known fields to Korean before encoding, preserving measured values, limits and channel identification. Replace the unsupported bullet separator with `/`.
- Resistance and general error descriptions use the existing structured Korean fault formatter. New short/open header qualifiers use Korean; the reader accepts both the old `Open` qualifier and the new `단선` qualifier.
- Existing production files and SQLite history are not rewritten. Already replaced characters cannot be recovered from the ERR text alone. Some inspected daily files also contain binary/control-byte segments; these are distinct from the Vietnamese-to-CP949 replacement issue.
- Regression tests use temporary daily files only, checking Korean Leak details, unchanged measurements, no replacement characters, append preservation and existing daily history compatibility.
