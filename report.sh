#!/bin/bash
cd "$(dirname "$0")"
{
  echo "=== STATUS ===";   git status -sb
  echo "=== LOG ===";      git log --oneline -10
  echo "=== TAGS ===";     git tag --sort=-creatordate
  echo "=== UNPUSHED ==="; git log origin/main..HEAD --oneline
  echo "=== DIFF ===";     git diff --stat HEAD~5 HEAD 2>/dev/null || echo "(???? ????????)"
} | tee git_report.txt