#!/bin/bash
# One-click installer: builds LiteCap.app on this Mac, moves it to Applications and opens it.
cd "$(dirname "$0")"
clear
echo "=============================="
echo "  LiteCap installer"
echo "=============================="
echo ""

# Step 1: Apple's free Command Line Tools provide the Swift compiler.
if ! xcode-select -p >/dev/null 2>&1; then
  echo "Step 1: Apple's free Command Line Tools are required."
  echo ""
  echo "  1. A window will pop up - click Install, then Agree"
  echo "  2. Wait for it to finish (about 5-10 minutes)"
  echo "  3. Run Install.command again"
  echo ""
  xcode-select --install >/dev/null 2>&1
  read -n 1 -s -r -p "Press any key to close this window..."
  exit 0
fi

# Step 2: compile.
echo "Building LiteCap - this takes about a minute..."
chmod +x build.sh
if ! ./build.sh > build.log 2>&1; then
  echo ""
  echo "The build failed."
  echo "Please send a screenshot of this window, or the build.log file in this folder."
  echo ""
  echo "---------- Errors ----------"
  if grep -q "error:" build.log; then
    grep -E "^[^ ].*error:" build.log | sort -u | head -25
  else
    tail -30 build.log
  fi
  echo ""
  read -n 1 -s -r -p "Press any key to close this window..."
  exit 1
fi

# Step 3: replace any older copy in Applications and launch.
osascript -e 'tell application "LiteCap" to quit' >/dev/null 2>&1
pkill -x LiteCap >/dev/null 2>&1
sleep 1
rm -rf "/Applications/LiteCap.app"
if ! mv -f LiteCap.app /Applications/ 2>/dev/null; then
  echo "Administrator permission is needed to copy into Applications."
  echo "Enter your Mac login password (nothing is shown while you type):"
  sudo mv -f LiteCap.app /Applications/
fi
open "/Applications/LiteCap.app"
open "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture"

echo ""
echo "Installed. The LiteCap icon is now in the menu bar (top right)."
echo ""
echo "Last step (once, and again after every rebuild):"
echo "  1. In System Settings > Privacy & Security > Screen Recording,"
echo "     turn LiteCap off and on again (or switch it on if it is new)"
echo "  2. If macOS offers \"Quit & Reopen\", click it;"
echo "     otherwise choose Quit LiteCap from the menu bar icon and open LiteCap again"
echo ""
echo "Then press Shift-Command-1 for a region capture, Shift-Command-2 for full screen."
echo ""
read -n 1 -s -r -p "Press any key to close this window..."
