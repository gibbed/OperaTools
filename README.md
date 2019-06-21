# Opera Tools

Personal tools I've made for use with the Opera browser, the original (12.x). Not the new Opera based on Chromium.

## RepairVisitedLinks

Opera has a bug where it will eventually overflow the visited links file (`vlink4.dat`) with too much data, generally triggered by anchors in URLs, commonly used by sites like G-Mail. This tool attempts to rebuild `vlink4.dat` in a way that keeps as much information possible and removing corruption.
