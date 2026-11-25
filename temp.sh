#!/bin/bash
cd /c/work/TGDrive
git rm --cached dir 2>/dev/null
git add TGDrive.Core/ TGDrive.Storage/ temp.sh
git commit -m "feat: add Core (paths, captions) and Storage (SQLite, base store) with tests"
