FIXED ONE-CLICK PUBLISH

Replace these two files in the project:
1. BUILD_ONE_FILE.cmd
2. Scripts\Publish-OneFile.ps1

Then double-click BUILD_ONE_FILE.cmd.

The PowerShell file is saved as UTF-8 with BOM and uses ASCII messages,
which avoids parser errors in Windows PowerShell 5.1.

Output:
PublishSingle\JBZUniversalTester.exe

If publishing fails for a real build reason, inspect:
publish.log
