#!/usr/bin/env dotnet-script
// Quick fix: Apply missing DB columns that the migration history says are applied but aren't in the DB.
// Run via: dotnet script apply-missing-columns.csx
// OR just compile and run via dotnet-run as a standalone program (see apply-missing-columns.cs)
